using System.Numerics;
using Silk.NET.OpenGL;
using SkywardRenderingSharp.Gpu;
using SkywardRenderingSharp.Model;
using SkywardRenderingSharp.Resources;

namespace SkywardRenderingSharp.Rendering;

/// <summary>A model on the GPU: its polygons as buffers and its textures, ready to draw.</summary>
/// <remarks>Create it on the GL thread through <see cref="SkywardRenderer.LoadModel"/>.</remarks>
public sealed class LoadedModel : IDisposable
{
    readonly GlMesh[] _meshes;
    readonly GL _gl;
    readonly ModelTextures _images;
    readonly Dictionary<string, GlTexture?> _textures = [];

    public Mdl0Model Model { get; }

    /// <summary>Texture names the model's materials use that no texture pool resolves (they draw magenta).</summary>
    public IEnumerable<string> MissingTextures => Model.Materials.SelectMany(m => m.Layers).Select(l => l.TextureName).Distinct().Where(n => _images.Find(n) is null);
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }

    internal LoadedModel(GL gl, Mdl0Model model, ModelTextures textures)
    {
        Model = model; _gl = gl; _images = textures;
        _meshes = model.Meshes.Select(m => new GlMesh(gl, m)).ToArray();
        var positions = model.Draws.SelectMany(d => model.Meshes[d.Mesh].Vertices.Select(v => v.Position)).ToList();
        BoundsMin = positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        BoundsMax = positions.Aggregate(new Vector3(float.MinValue), Vector3.Max);
    }

    internal void DrawMesh(int draw) => _meshes[Model.Draws[draw].Mesh].Draw();

    // Textures upload on first use, so patterns that swap in other textures (the eyes) cost nothing until shown.
    // TODO: every colour texture decodes as sRGB, though the XTX flags mark Link's as unorm; raw values with the final encode look washed out against the game.
    internal GlTexture? Texture(string name)
    {
        if (_textures.TryGetValue(name, out var cached)) return cached;
        return _textures[name] = _images.Find(name) is { } image ? new GlTexture(_gl, image, !name.StartsWith("Lm", StringComparison.Ordinal)) : null;
    }

    public void Dispose()
    {
        foreach (var m in _meshes) m.Dispose();
        foreach (var t in _textures.Values) t?.Dispose();
    }
}
