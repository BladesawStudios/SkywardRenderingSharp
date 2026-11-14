using BresSharp;
using Res = BresSharp.Mdl0;

namespace SkywardRenderingSharp.Model;

/// <summary>A model draw: a polygon rendered with a material, attached to a bone, in the opaque or translucent list.</summary>
public readonly record struct DrawCall(int Material, int Mesh, int Bone, int Priority, bool Translucent);

/// <summary>A BRRES MDL0 model as the renderer draws it: skeleton, materials, decoded polygons and the draw list that joins them.</summary>
/// <remarks>Read through BresSharp's MDL0 views. Meshes are in bind pose. Texture names resolve against the same BRRES file's texture list.</remarks>
public sealed class Mdl0Model
{
    // A draw list command: [material u16][polygon u16][bone u16][priority u8].
    const byte DrawCommand = 0x04;

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
        BresSubfile file = bres["3DModels(NW4R)"]?[name] ?? throw new KeyNotFoundException($"No model '{name}'.");
        var model = new Res.Mdl0Model(file);
        var bones = model.Bones.Select(b => new Bone(b.Name, b.Index, b.Parent?.Index ?? -1, b.Scale, b.Rotation, b.Translation, b.Transform, b.InverseTransform))
            .OrderBy(b => b.Index).ToList();
        var decoder = new MeshDecoder(model, bones, NodeMatrices.Read(model, bones));
        return new Mdl0Model(name, bones, model.Materials.Select(MaterialReader.Read).ToList(), model.Shapes.Select(decoder.Decode).ToList(), ReadDraws(model));
    }

    static List<DrawCall> ReadDraws(Res.Mdl0Model model) =>
        model.Definitions.Where(d => d.Name.StartsWith("Draw", StringComparison.Ordinal))
            .SelectMany(d => d.Commands.Where(c => c.Op == DrawCommand).Select(c => new DrawCall(
                c.Args[0] << 8 | c.Args[1], c.Args[2] << 8 | c.Args[3], c.Args[4] << 8 | c.Args[5], c.Args[6], d.Name == "DrawXlu")))
            .ToList();
}
