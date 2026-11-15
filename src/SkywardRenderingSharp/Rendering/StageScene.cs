using System.Numerics;

namespace SkywardRenderingSharp.Rendering;

/// <summary>A stage ready to draw: one room's geometry and placed objects, the clouds that sit in the world, and the sky that follows the eye.</summary>
/// <remarks>Room and cloud geometry is authored in world coordinates, so it is placed at the origin; each placed object's model stands at its
/// placement. The sky box is far larger than the far plane and is drawn first with its own projection, centred on the camera.</remarks>
public sealed class StageScene : IDisposable
{
    readonly List<LoadedModel> _room, _clouds, _sky, _objects;
    readonly List<(int Model, Matrix4x4 World)> _placed;

    public string Name { get; }
    /// <summary>The room drawn, or null for the stage's own objects alone.</summary>
    public string? Room { get; }

    /// <summary>The lights to draw the stage with. Stage materials store a white ambient colour and rely on a bright scene ambient.</summary>
    public Lighting.LightRig Lights { get; }

    internal StageScene(string name, string? room, List<LoadedModel> roomModels, List<LoadedModel> clouds, List<LoadedModel> sky, Lighting.LightRig lights,
        List<LoadedModel>? objects = null, List<(int Model, Matrix4x4 World)>? placed = null)
    {
        Lights = lights; Name = name; Room = room; _room = roomModels; _clouds = clouds; _sky = sky; _objects = objects ?? []; _placed = placed ?? [];
    }

    public IReadOnlyList<LoadedModel> Models => [.. _room, .. _objects, .. _clouds, .. _sky];

    public bool HasSky => _sky.Count > 0;

    /// <summary>The room, placed object and cloud instances that the main pass draws.</summary>
    public IReadOnlyList<ModelInstance> World(Lighting.PaletteSlot? palette = null) =>
        _room.Select(m => new ModelInstance(m, Matrix4x4.Identity)).Concat(_placed.Select(p => new ModelInstance(_objects[p.Model], p.World))).Concat(_clouds.Select(m => new ModelInstance(m, Matrix4x4.Identity, ColorOverrides: palette is null ? null : SkyBox.Tint(m, palette)))).ToList();

    /// <summary>The stage's own sky box, if it has one.</summary>
    public SkyBox? DefaultSky => _sky.Count > 0 ? new SkyBox("stage", _sky) : null;

    /// <summary>The sky instances for a camera: translated to the eye so the sky never gets closer.</summary>
    public IReadOnlyList<ModelInstance> Sky(Vector3 eye, Lighting.PaletteSlot? palette = null) => DefaultSky?.Instances(eye, palette) ?? [];

    /// <summary>The room geometry's bounds, for framing a camera.</summary>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var model in _room)
            foreach (var mesh in model.Model.Meshes)
                foreach (var v in mesh.Vertices) { min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position); }
        return (min, max);
    }

    public void Dispose()
    {
        foreach (var model in Models) model.Dispose();
    }
}
