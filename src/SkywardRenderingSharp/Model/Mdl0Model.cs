using SkywardRenderingSharp.Brres;

namespace SkywardRenderingSharp.Model;

/// <summary>A model draw: a polygon rendered with a material, attached to a bone, in the opaque or translucent list.</summary>
public readonly record struct DrawCall(int Material, int Mesh, int Bone, int Priority, bool Translucent);

/// <summary>A BRRES MDL0 model: skeleton, materials, decoded polygons and the draw list that joins them.</summary>
/// <remarks>Meshes are in bind pose. Texture names resolve against the same BRRES file's texture list.</remarks>
public sealed class Mdl0Model
{
    // MDL0 section order after the 16 byte header: definitions, bones, vertices, normals, colours, UVs, fur x2, materials, TEV, polygons, texture refs.
    const int SectionTable = 16, Definitions = 0, BoneSection = 1, MaterialSection = 8, PolygonSection = 10;

    public string Name { get; }
    public IReadOnlyList<Bone> Bones { get; }
    public IReadOnlyList<Material> Materials { get; }
    public IReadOnlyList<MeshData> Meshes { get; }
    public IReadOnlyList<DrawCall> Draws { get; }

    Mdl0Model(string name, IReadOnlyList<Bone> bones, IReadOnlyList<Material> materials, IReadOnlyList<MeshData> meshes, IReadOnlyList<DrawCall> draws)
    {
        Name = name; Bones = bones; Materials = materials; Meshes = meshes; Draws = draws;
    }

    public static Mdl0Model Read(BresFile bres, string name)
    {
        var f = bres.Data;
        int m = bres.ModelOffset(name);
        var sections = Enumerable.Range(0, 14).Select(i => f.I32(m + SectionTable + 4 * i)).ToList();
        var bones = ReadBones(f, m + sections[BoneSection], m);
        var arrays = VertexArrays.Read(f, m, sections);
        var decoder = new MeshDecoder(f, arrays, bones, NodeMatrices.Read(f, m + sections[Definitions], bones));
        var meshes = ResDict.Read(f, m + sections[PolygonSection]).Select(e => decoder.Decode(e.Name, e.Offset)).ToList();
        var materials = ResDict.Read(f, m + sections[MaterialSection]).Select(e => MaterialReader.Read(f, e.Name, e.Offset)).ToList();
        return new Mdl0Model(name, bones, materials, meshes, ReadDraws(f, m + sections[Definitions]));
    }

    // The parent is a signed offset from the bone to its parent's record; it is zero for the root.
    static List<Bone> ReadBones(BigEndian f, int dict, int mdl0)
    {
        var entries = ResDict.Read(f, dict);
        var indexByOffset = entries.ToDictionary(e => e.Offset, e => f.I32(e.Offset + 0x0C));
        return entries.Select(e =>
        {
            int parentOffset = f.I32(e.Offset + 0x5C);
            int parent = parentOffset != 0 && indexByOffset.TryGetValue(e.Offset + parentOffset, out int p) ? p : -1;
            return new Bone(e.Name, f.I32(e.Offset + 0x0C), parent, f.Vec3(e.Offset + 0x20), f.Vec3(e.Offset + 0x2C), f.Vec3(e.Offset + 0x38),
                f.Matrix3x4(e.Offset + 0x70), f.Matrix3x4(e.Offset + 0xA0));
        }).OrderBy(b => b.Index).ToList();
    }

    // Draw lists are byte code: op 0x04 = [material u16][polygon u16][bone u16][priority u8], op 0x01 ends.
    static List<DrawCall> ReadDraws(BigEndian f, int dict)
    {
        var draws = new List<DrawCall>();
        foreach (var (name, offset) in ResDict.Read(f, dict))
        {
            if (!name.StartsWith("Draw", StringComparison.Ordinal)) continue;
            for (int p = offset; f.U8(p) == 0x04; p += 8)
                draws.Add(new DrawCall(f.U16(p + 1), f.U16(p + 3), f.U16(p + 5), f.U8(p + 7), name == "DrawXlu"));
        }
        return draws;
    }
}
