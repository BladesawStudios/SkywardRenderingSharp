using SkywardRenderingSharp.Content;
using Silk.NET.OpenGL;
using SkywardRenderingSharp.Animation;
using SkywardRenderingSharp.Gpu;
using SkywardRenderingSharp.Lighting;
using SkywardRenderingSharp.Model;
using SkywardRenderingSharp.Rendering;
using SkywardRenderingSharp.Resources;

namespace SkywardRenderingSharp;

/// <summary>The renderer: loads Skyward Sword HD models, draws them with the GX uber shader into an offscreen view and runs the painterly post chain.</summary>
/// <remarks>Everything here runs on the thread that owns the GL context (a 4.3 core context or newer). The result of
/// <see cref="Render"/> is a texture handle, bottom row first. The optional sky instances draw first, behind everything.</remarks>
public sealed class SkywardRenderer : IDisposable
{
    readonly GL _gl;
    readonly SkywardRom _rom;
    readonly SamplerCache _samplers;
    readonly ModelDrawer _drawer;
    readonly PostChain _post;
    RenderTarget? _scene, _output;

    public RenderSettings Settings { get; }
    public LightRig Lights { get; set; }

    public SkywardRenderer(GL gl, SkywardRom rom)
    {
        _gl = gl; _rom = rom;
        using var host = GlHostState.Enter(gl);
        _samplers = new SamplerCache(gl);
        _drawer = new ModelDrawer(gl, _samplers);
        _post = new PostChain(gl, rom);
        Lights = LightRig.Characters();
        Settings = new RenderSettings
        {
            BloomMaskThreshold = rom.Post.BloomMaskThreshold, BloomWeights = new System.Numerics.Vector2(rom.Post.BloomLayers[0].Y, rom.Post.BloomLayers.Count > 1 ? rom.Post.BloomLayers[1].Y : 0), BloomTint = rom.Post.BloomTint, PaintFar = rom.Post.PaintFar, PaintNear = rom.Post.PaintNear,
        };
    }

    /// <summary>Applies a palette slot: its lights, painterly distances, fog layer and clear colour. Returns the slot so a stage can tint its sky from it.</summary>
    public PaletteSlot ApplyPalette(StagePalette palette, int slot, int fogLayer, bool fog, float? intensity = 0.4f, int? blendSlot = null, float blend = 0f)
    {
        var s = blendSlot is { } other ? PaletteSlot.Lerp(palette.Slot(slot), palette.Slot(other), blend) : palette.Slot(slot);
        // Stage lights run at the palette's own colours with the hemisphere (.eaf) ambient: stage materials multiply texture, baked vertex colour and light by two, so full light gives the game's bright look.
        Lights = LightRig.FromPalette(_rom.Lights, s, intensity, 1.2f, hemisphereAmbient: intensity is null);
        Settings.PaintFar = s.PaintFar; Settings.PaintNear = s.PaintNear;
        var layer = s.Fog[Math.Clamp(fogLayer, 0, s.Fog.Count - 1)];
        Settings.Fog = fog; Settings.FogColor = layer.Color; Settings.FogRange = new System.Numerics.Vector2(layer.Near, layer.Far);
        Settings.SkyColor = s.SkyHorizon;
        return s;
    }

    /// <summary>Turns on depth of field with the opening cutscene's parameters, the only ones the game ships.</summary>
    public void UseCutsceneDepthOfField()
    {
        Settings.DepthOfField = true;
        Settings.DofFar = new System.Numerics.Vector2(_rom.CutsceneDof.FarLimit - _rom.CutsceneDof.FarWidth / 2, _rom.CutsceneDof.FarLimit + _rom.CutsceneDof.FarWidth / 2);
    }

    /// <summary>Loads something by ID: a model name ("al", "EquipSwordMaster") or an archive name ("Alink", "F000") for all of its models.
    /// Models in the same file whose bones hang from the main model's joints attach to it.</summary>
    public SceneObject LoadObject(string id)
    {
        using var host = GlHostState.Enter(_gl);
        var locations = _rom.Index.Find(id);
        bool isModel = locations.Count > 0;
        var group = (isModel ? locations : _rom.Index.InArchive(id).ToList()) switch { { Count: 0 } => throw new KeyNotFoundException($"No model or archive '{id}'."), var l => l };
        var first = group[0];
        var package = _rom.Open(first);
        var names = package.ModelNames.ToList();
        var loaded = names.ToDictionary(n => n, n => new LoadedModel(_gl, Mdl0Model.Read(package.Bres, n), Textures(package)));
        string main = isModel ? id : names.OrderByDescending(n => loaded[n].Model.Bones.Count).First();
        main = names.First(n => n.Equals(main, StringComparison.OrdinalIgnoreCase));
        var others = names.Where(n => n != main).Select(n => loaded[n]).ToList();
        if (isModel) others.AddRange(CompanionModels(first));
        var attachments = Attachments.Select(loaded[main], others);
        // An archive ID whose models do not hang from one skeleton (a stage) loads them all, each placed as authored.
        if (!isModel && attachments.Count == 0) attachments = others.Select(o => (o, new int[o.Model.Bones.Count].Select(_ => -1).ToArray())).ToList();
        return new SceneObject(id, loaded[main], attachments, _rom.AnimationsOf(package, loaded[main].Model));
    }

    

    // Every model's textures fall back to the engine's own (the Lm* lightmaps are the Gra* gradients).
    ModelTextures Textures(ModelArchive archive, ModelTextures? fallback = null) => new(archive, fallback ?? _rom.SystemTextures);

    // Parts stored in sibling archives (Zelda_face next to Zelda): each loads with its own textures and attaches when its bones name joints of the main model.
    List<LoadedModel> CompanionModels(ModelLocation main) =>
        _rom.Index.Companions(main).DistinctBy(l => l.Chain[^1]).SelectMany(l =>
        {
            var file = _rom.Open(l);
            var textures = Textures(file);
            return file.ModelNames.Select(m => new LoadedModel(_gl, Mdl0Model.Read(file.Bres, m), textures));
        }).ToList();

    /// <summary>Loads a stage by ID ("F000" is Skyloft): one room of its geometry, the cloud objects, and a sky box when it has one.</summary>
    public StageScene LoadStage(string stage, string? room = null)
    {
        using var host = GlHostState.Enter(_gl);
        var archive = _rom.OpenStage(stage);
        var data = _rom.OpenRoom(stage, room ?? archive.Rooms.First());
        return LoadRoom(data, archive);
    }

    /// <summary>Uploads a room read with <see cref="SkywardRom.OpenRoom"/>, with its placed objects' models and its stage's clouds and sky box; the
    /// stage's own objects (<see cref="SkywardRom.OpenStageObjects"/>) come without them, since a room of the stage brings them.</summary>
    public StageScene LoadRoom(RoomData data) => LoadRoom(data, _rom.OpenStage(data.Stage));

    StageScene LoadRoom(RoomData data, StageArchive archive)
    {
        using var host = GlHostState.Enter(_gl);
        var common = _rom.SkyTextures;
        var roomModels = data.Models.Select(m => new LoadedModel(_gl, m, data.Textures)).ToList();
        var objects = data.ObjectModels.Select(o => new LoadedModel(_gl, o.Model, o.Textures)).ToList();
        var placed = data.Placements.SelectMany((p, i) => data.Drawn[i].Select(model => (model, data.WorldOf(i)))).ToList();
        bool room = data.Room is not null;
        return new StageScene(data.Stage, data.Room, roomModels, room ? ObjectModels(archive, "Cloud", common) : [], room ? ObjectModels(archive, "Vrbox", common).Take(1).ToList() : [],
            LightRig.FromBlight(_rom.Lights, 0, 0.4f, 1.2f), objects, placed);
    }

    /// <summary>Loads a sky box by model name (Vrbox00, Vrbox03, ...), with the shared sky textures, so it can back any scene.</summary>
    public SkyBox LoadSkyBox(string name)
    {
        using var host = GlHostState.Enter(_gl);
        var location = _rom.Index.Find(name) is { Count: > 0 } found ? found[0] : throw new KeyNotFoundException($"No sky box '{name}'.");
        var file = _rom.Open(location);
        var textures = Textures(file, Textures(_rom.OpenModelArchive("SkyCmn")));
        return new SkyBox(name, file.ModelNames.Select(m => new LoadedModel(_gl, Mdl0Model.Read(file.Bres, m), textures)).ToList());
    }

    /// <summary>Names of the sky boxes in the game's data.</summary>
    public IEnumerable<string> SkyBoxNames => _rom.Index.Search("Vrbox").Select(l => l.Model).Distinct().Order();

    List<LoadedModel> ObjectModels(StageArchive archive, string prefix, ModelTextures common)
    {
        var models = new List<LoadedModel>();
        foreach (string name in archive.Objects.Where(o => o.Contains(prefix, StringComparison.OrdinalIgnoreCase) && !o.Contains("Inside", StringComparison.OrdinalIgnoreCase)
            // CloudCover is a small local plane that a stage places with an object (at the origin it slices through the island); only the world-sized cloud layers are drawn.
            && !o.Contains("Cover", StringComparison.OrdinalIgnoreCase)))
        {
            var file = archive.Object(name);
            var textures = Textures(file, common);
            models.AddRange(file.ModelNames.Where(m => !m.EndsWith("_s", StringComparison.Ordinal)).Select(m => new LoadedModel(_gl, Mdl0Model.Read(file.Bres, m), textures)));
        }
        return models;
    }

    public LoadedModel LoadModel(string archive, string model)
    {
        using var host = GlHostState.Enter(_gl);
        var file = _rom.OpenModelArchive(archive);
        return new LoadedModel(_gl, Mdl0Model.Read(file.Bres, model), Textures(file));
    }

    /// <summary>Renders the instances and returns the finished colour texture (also available as <see cref="OutputFramebuffer"/>).</summary>
    public uint Render(Camera camera, IReadOnlyList<ModelInstance> instances, int width, int height, IReadOnlyList<ModelInstance>? skyInstances = null)
    {
        using var host = GlHostState.Enter(_gl);
        Ensure(width, height);
        _scene!.Bind();
        var sky = Settings.SkyColor;
        _gl.ColorMask(true, true, true, true); _gl.DepthMask(true);
        _gl.ClearColor(sky.X, sky.Y, sky.Z, 1f); _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.FrontFace(FrontFaceDirection.CW);   // GX treats clockwise (in GL terms) as front-facing
        float aspect = width / (float)height;
        if (skyInstances is { Count: > 0 })
        {
            _drawer.Draw(skyInstances, camera.View, (camera with { Near = 1000f, Far = 1_000_000f }).Projection(aspect), Lights, Settings.DisableCulling);
            _gl.Clear(ClearBufferMask.DepthBufferBit);
        }
        _drawer.Draw(instances, camera.View, camera.Projection(aspect), Lights, Settings.DisableCulling);
        _post.Run(_scene, _output!, camera, Settings);
        return _output!.Color;
    }

    public uint OutputFramebuffer => _output?.Framebuffer ?? 0;

    /// <summary>The depth of the last frame's scene (24-bit, the camera's standard OpenGL projection), for a host that composites the frame into its own.</summary>
    public uint DepthTexture => _scene?.Depth ?? 0;

    /// <summary>Reads the last frame back as top-row-first RGBA8.</summary>
    public byte[] ReadPixels()
    {
        var o = _output ?? throw new InvalidOperationException("Nothing has been rendered.");
        var raw = new byte[o.Width * o.Height * 4];
        using var host = GlHostState.Enter(_gl);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, o.Framebuffer);
        _gl.ReadPixels(0, 0, (uint)o.Width, (uint)o.Height, PixelFormat.Rgba, PixelType.UnsignedByte, raw.AsSpan());
        var flipped = new byte[raw.Length];
        for (int y = 0; y < o.Height; y++) raw.AsSpan(y * o.Width * 4, o.Width * 4).CopyTo(flipped.AsSpan((o.Height - 1 - y) * o.Width * 4));
        return flipped;
    }

    void Ensure(int width, int height)
    {
        if (_scene is not null && _scene.Width == width && _scene.Height == height) return;
        _scene?.Dispose(); _output?.Dispose();
        _scene = new RenderTarget(_gl, width, height, true);
        _output = new RenderTarget(_gl, width, height, false);
    }

    public void Dispose()
    {
        _post.Dispose(); _drawer.Dispose(); _samplers.Dispose(); _scene?.Dispose(); _output?.Dispose();
    }
}
