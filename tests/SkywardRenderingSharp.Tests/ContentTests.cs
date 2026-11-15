using SkywardRenderingSharp.Model;
using SkywardRenderingSharp.Resources;

namespace SkywardRenderingSharp.Tests;

public class ContentTests
{
    [RomFact]
    public void IndexFindsModelsObjectsRoomsAndSky()
    {
        var index = Romfs.Rom!.Index;
        Assert.NotEmpty(index.Find("al"));
        Assert.NotEmpty(index.Find("Vrbox03"));
        Assert.NotEmpty(index.InArchive("Alink"));
        Assert.Contains(index.Find("model0"), l => l.Chain.Any(c => c.StartsWith("rarc/F000_r00", StringComparison.Ordinal)));
        Assert.True(index.ModelNames.Count() > 1400);
    }

    [RomFact]
    public void LinkSkeletonRecomputesFromItsLocalTransforms()
    {
        var model = Mdl0Model.Read(Romfs.Rom!.Open(Romfs.Rom.Index.Find("al")[0]).Bres, "al");
        Assert.True(model.Bones.Count > 20);
        var world = new System.Numerics.Matrix4x4[model.Bones.Count];
        foreach (var b in model.Bones)
        {
            world[b.Index] = b.Parent < 0 ? b.LocalMatrix : b.LocalMatrix * world[b.Parent];
            Assert.InRange(MathF.Abs(world[b.Index].M41 - b.World.M41), 0, 0.01f);
        }
        Assert.All(model.Draws, d => Assert.NotEmpty(model.Materials[d.Material].Stages));
    }

    [RomFact]
    public void EveryTextureOfLinkAndSkyloftResolves()
    {
        var rom = Romfs.Rom!;
        var link = rom.Open(rom.Index.Find("al")[0]);
        var textures = new ModelTextures(link);
        var model = Mdl0Model.Read(link.Bres, "al");
        Assert.All(model.Materials.SelectMany(m => m.Layers).Select(l => l.TextureName).Distinct(), n => Assert.NotNull(textures.Find(n)));

        var stage = rom.OpenStage("F000");
        var pool = new ModelTextures(stage.Props, new ModelTextures(rom.OpenModelArchive("SkyCmn")));
        var room = stage.Room("F000_r00");
        var roomModel = Mdl0Model.Read(room.Bres, "model0");
        var roomTextures = new ModelTextures(room, pool);
        Assert.All(roomModel.Materials.SelectMany(m => m.Layers).Select(l => l.TextureName).Distinct(), n => Assert.NotNull(roomTextures.Find(n)));
    }

    [RomFact]
    public void LinkFaceTextureAndEyeRingAgreeOnSkinColour()
    {
        var rom = Romfs.Rom!;
        var textures = new ModelTextures(rom.Open(rom.Index.Find("al_face")[0]));
        var face = textures.Find("al_face")!; var eye = textures.Find("al_eye.1")!;
        // The eye socket's flat skin tone is painted to match the face texture's.
        Assert.InRange(eye.Pixels[0], 232, 236); Assert.InRange(eye.Pixels[1], 179, 183); Assert.InRange(eye.Pixels[2], 134, 138);
        int match = Enumerable.Range(0, face.Width * face.Height).Count(i => Math.Abs(face.Pixels[i * 4] - eye.Pixels[0]) < 3 && Math.Abs(face.Pixels[i * 4 + 1] - eye.Pixels[1]) < 3 && Math.Abs(face.Pixels[i * 4 + 2] - eye.Pixels[2]) < 3);
        Assert.True(match > 1000);
    }

    [RomFact]
    public void MeshNormalsVaryPerVertex()
    {
        // Normal arrays store their stride at +0x1D like the position arrays; reading it from +0x1C made every vertex share a handful of normals.
        var rom = Romfs.Rom!;
        var model = Mdl0Model.Read(rom.Open(rom.Index.Find("al_face")[0]).Bres, "al_face");
        var face = model.Meshes.OrderByDescending(m => m.Vertices.Length).First();
        Assert.True(face.Vertices.Select(v => v.Normal).Distinct().Count() > 200);
    }

    [RomFact]
    public void PaletteSlotsDecodeLightsFogAndPaintDistances()
    {
        var rom = Romfs.Rom!;
        Assert.Contains("F000", rom.PaletteNames);
        var palette = rom.OpenPalette("F000");
        var first = palette.Slot(0);
        // Slot 0's fill light, third light and ambient are the .blight set's own colours (ffb787, ffc8ad, ffffc0).
        Assert.Equal(new System.Numerics.Vector3(1f, 0xb7 / 255f, 0x87 / 255f), first.FillLight);
        Assert.Equal(new System.Numerics.Vector3(1f, 0xc8 / 255f, 0xad / 255f), first.ThirdLight);
        Assert.Equal(new System.Numerics.Vector3(1f, 1f, 0xc0 / 255f), first.Ambient);
        Assert.Equal(new System.Numerics.Vector2(3000, 3000), first.PaintFar);
        Assert.Equal(new System.Numerics.Vector2(880, 730), first.PaintNear);
        Assert.Equal(3, first.Fog.Count);
        Assert.Equal(new System.Numerics.Vector2(5000, 300000), palette.Slot(6).PaintFar);
        Assert.All(rom.PaletteNames, n => Assert.NotNull(rom.OpenPalette(n).Slot(StagePaletteSlots - 1)));
    }

    const int StagePaletteSlots = SkywardRenderingSharp.Lighting.StagePalette.Slots;

    [RomFact]
    public void RigidPolygonsFollowTheirMatrixIdAndEveryVertexIsWeighted()
    {
        var rom = Romfs.Rom!;
        // Fi's jewels name matrix id 7, which the rigid table maps to Spine2 (bone 9): they sit in the chest frame, not at the foot.
        var fi = Mdl0Model.Read(rom.Open(rom.Index.Find("SwordGirl")[0]).Bres, "SwordGirl");
        var jewelMesh = fi.Draws.Where(d => fi.Materials[d.Material].Name == "m_JewelA").Select(d => fi.Meshes[d.Mesh]).Single();
        Assert.All(jewelMesh.Vertices, v => Assert.InRange(v.Position.Y, 100f, 150f));
        // Zelda's skirt and stole use blended ids; none may lose its influences (it would collapse to the origin).
        var zelda = Mdl0Model.Read(rom.Open(rom.Index.Find("Zelda_body")[0]).Bres, "Zelda_body");
        Assert.All(zelda.Meshes.SelectMany(m => m.Vertices), v => Assert.True(v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W > 0.99f));
    }

    [RomFact]
    public void SkyBoxesAndPaletteSlotsAreListed()
    {
        var rom = Romfs.Rom!;
        Assert.True(rom.Index.Search("Vrbox").Select(l => l.Model).Distinct().Count() >= 3);
        var used = Enumerable.Range(0, SkywardRenderingSharp.Lighting.StagePalette.Slots).Select(i => rom.OpenPalette("F000").Slot(i)).Where(s => !s.IsTemplate).ToList();
        Assert.InRange(used.Count, 8, 18);
    }

    [RomFact]
    public void PaletteBlendAndDepthOfFieldFollowTheEngine()
    {
        var rom = Romfs.Rom!;
        var palette = rom.OpenPalette("F000");
        var a = palette.Slot(0); var b = palette.Slot(6);
        var mid = SkywardRenderingSharp.Lighting.PaletteSlot.Lerp(a, b, 0.5f);
        Assert.Equal((a.PaintFar.X + b.PaintFar.X) / 2, mid.PaintFar.X, 1);
        Assert.Equal(a.SkyTop, SkywardRenderingSharp.Lighting.PaletteSlot.Lerp(a, b, 0f).SkyTop);
        Assert.Equal(b.SkyTop, SkywardRenderingSharp.Lighting.PaletteSlot.Lerp(a, b, 1f).SkyTop);
        // The cutscene file's focus (23000) and ramp width (10) sit at 0x18 and 0x1c.
        Assert.Equal(23000f, rom.CutsceneDof.FarLimit);
        Assert.Equal(10f, rom.CutsceneDof.FarWidth);
        Assert.Equal(0.535f, rom.Post.BloomMaskThreshold.X, 3);
    }

    [RomFact]
    public void ActorTableNamesOnlyModelsTheDumpHolds()
    {
        var all = Romfs.Rom!.Index.All.ToList();
        bool Holds(ActorPart part) => all.Any(l => l.Model.Equals(part.Model, StringComparison.OrdinalIgnoreCase) && (part.Archive is null
            ? l.Chain.Length == 0 && l.BresPath == "g3d/stage.brres"
            : l.Chain is [.., var last] && last.StartsWith("oarc/", StringComparison.Ordinal) && l.Archive.Equals(part.Archive, StringComparison.OrdinalIgnoreCase)));
        Assert.True(ActorModels.Default.Actors.Count() > 50);
        Assert.DoesNotContain(ActorModels.Default.Parts, p => !Holds(p));
    }

    [RomFact]
    public void SkyloftDrawsItsPotsWhereTheyStand()
    {
        var room = Romfs.Rom!.OpenRoom("F000", "F000_r00");
        Assert.Empty(room.MissingParts);
        var pots = room.Placements.Select((p, i) => (p, i)).Where(x => x.p.Actor == "Tubo" && x.p.AlwaysPresent).ToList();
        Assert.NotEmpty(pots);
        Assert.All(pots, x => Assert.Contains(room.Drawn[x.i], m => room.ObjectModels[m].Model.Name.StartsWith("Tubo", StringComparison.Ordinal)));
        Assert.All(room.Placements.Select((p, i) => (p, i)).Where(x => !x.p.AlwaysPresent), x => Assert.Empty(room.Drawn[x.i]));
    }

    [RomFact]
    public void AChosenStoryLayerDrawsAndTheOthersDoNot()
    {
        var room = Romfs.Rom!.OpenRoom("F000", "F000_r00", layer => layer == 1);
        var placed = room.Placements.Select((p, i) => (p, i)).ToList();
        Assert.Contains(placed, x => x.p.Layer == 1 && room.Drawn[x.i].Length > 0);
        Assert.All(placed.Where(x => x.p.Layer > 1), x => Assert.Empty(room.Drawn[x.i]));
    }

    [RomFact]
    public void LayerTableMatchesTheDump()
    {
        Assert.True(StageLayers.Default.Of("F000").Count > 10);
        Assert.True(StageLayers.Count(Romfs.Rom!).SameAs(StageLayers.Default));
    }
}
