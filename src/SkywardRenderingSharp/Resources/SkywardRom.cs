using SkywardRenderingSharp.Animation;
using SkywardRenderingSharp.Archives;
using SkywardRenderingSharp.Brres;
using SkywardRenderingSharp.Content;
using SkywardRenderingSharp.Lighting;
using SkywardRenderingSharp.Textures;

namespace SkywardRenderingSharp.Resources;

/// <summary>An extracted Skyward Sword HD romfs: its model archives and the shared lighting and post-processing data.</summary>
/// <remarks>Open it on the romfs folder (the one holding Object, Stage and Shaders).</remarks>
public sealed class SkywardRom
{
    readonly U8Archive _objectPack;
    readonly string _romfs;
    readonly Dictionary<string, U8Archive> _containers = new(StringComparer.OrdinalIgnoreCase);
    ContentIndex? _index;
    U8Archive? _palettes;

    public LightFile Lights { get; }
    public LightmapFile Lightmaps { get; }

    /// <summary>The engine's own lightmap textures: the Gra* gradients from System.arc under the Lm* names models use, plus every other texture the engine supplies.</summary>
    public ModelTextures SystemTextures { get; }
    public PostParameters Post { get; }
    public DofParameters CutsceneDof { get; }

    SkywardRom(string romfs, U8Archive objectPack, U8Archive systemArchive, ReadOnlyMemory<byte> systemModel, LightFile lights, LightmapFile lightmaps, PostParameters post, DofParameters dof)
    {
        _romfs = romfs; _objectPack = objectPack; SystemTextures = new ModelTextures(new ModelArchive(new BresFile(systemModel), null), null, GradientRamps(systemArchive, lightmaps)); Lights = lights; Lightmaps = lightmaps; Post = post; CutsceneDof = dof;
    }

    public static SkywardRom Open(string romfs)
    {
        string nx = Path.Combine(romfs, "Object", "NX");
        var pack = U8Archive.OpenFile(Path.Combine(nx, "ObjectPack.arc.LZ"));
        var system = U8Archive.OpenFile(Path.Combine(nx, "System.arc"));
        return new SkywardRom(romfs, pack, system, system.Get("g3d/model.brres"), LightFile.Open(system.Get("dat/default.blight").Span), LightmapFile.Open(system.Get("dat/default.blmap").Span),
            PostParameters.Read(system), DofParameters.Read(system.Get("dat/opning.bdof").Span));
    }

    // The engine loads the Gra* gradients (System.arc tex/*.bti, 64x4, identical rows) into GPU slots 10 to 17 at start-up (0xdf1ef4) and binds them in place of models' Lm* textures.
    // Each gradient is scaled by its .blmap curve (128 samples across the ramp): the shared curves are 1.0 apart from a few samples at the left edge, so this barely changes them.
    static Dictionary<string, RgbaImage> GradientRamps(U8Archive system, LightmapFile lightmaps)
    {
        var map = new Dictionary<string, string>
        {
            ["LmChaMulti"] = "GraChaMulti", ["LmChaSkin"] = "GraChaSkin", ["LmChaAdd"] = "GraChaAdd1", ["LmGetItem"] = "GraGetItem", ["LmGetItemAdd"] = "GraChaAdd0",
            ["LmUIMulti"] = "GraUIMulti", ["LmUIAdd"] = "GraUIAdd0",
        };
        var result = new Dictionary<string, RgbaImage>();
        foreach (var (lightmap, gradient) in map)
        {
            var d = system.Get($"tex/{gradient}.bti").Span;
            var image = WiiTexture.Decode(d[32..], d[2] << 8 | d[3], d[4] << 8 | d[5], (WiiTexture.Format)d[0]);
            if (lightmaps.Names.Contains(lightmap)) ApplyCurve(image, lightmaps.Curve(lightmap));
            if (lightmap.EndsWith("Add", StringComparison.Ordinal)) ScaleColor(image, AddRampGain);
            result[lightmap] = image;
        }
        return result;
    }

    // TODO: at full strength the Add ramps wash every character out (about half the vertex alpha is added on the lit side); the game's characters show only a hint of it, so the ramp is scaled down.
    // The factor is tuned by eye; the engine's reason for the smaller contribution was not found.
    const float AddRampGain = 0.12f;

    static void ScaleColor(RgbaImage image, float gain)
    {
        for (int i = 0; i < image.Pixels.Length; i += 4)
            for (int c = 0; c < 3; c++) image.Pixels[i + c] = (byte)Math.Clamp(image.Pixels[i + c] * gain, 0, 255);
    }

    static void ApplyCurve(RgbaImage image, float[] curve)
    {
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                float scale = curve[Math.Min(curve.Length - 1, x * curve.Length / image.Width)];
                for (int c = 0; c < 3; c++) { int i = (y * image.Width + x) * 4 + c; image.Pixels[i] = (byte)Math.Clamp(image.Pixels[i] * scale, 0, 255); }
            }
    }

    public IEnumerable<string> ModelArchives => _objectPack.Paths.Where(p => p.StartsWith("oarc/", StringComparison.Ordinal) && p.EndsWith(".arc", StringComparison.Ordinal))
        .Select(p => Path.GetFileNameWithoutExtension(p));

    /// <summary>Opens an animation library archive such as PLCommonAnime (Link's clips).</summary>
    public AnimationLibrary OpenAnimations(string name) =>
        new(U8Archive.Open(_objectPack.Get($"oarc/{name}.arc").ToArray()).Get("g3d/model.brres"));

    /// <summary>The index of every model in the romfs. The first call builds it (a minute or so) and caches it on disk.</summary>
    public ContentIndex Index => _index ??= ContentIndex.Open(_romfs);

    /// <summary>Opens the BRRES and textures a model location points to.</summary>
    public ModelArchive Open(ModelLocation location)
    {
        var archive = Container(location.Container);
        foreach (string step in location.Chain) archive = U8Archive.Open(archive.Get(step).ToArray());
        var xtx = archive.TryGet(location.XtxPath, out var data) ? XtxFile.Open(data.Span) : null;
        return new ModelArchive(new BresFile(archive.Get(location.BresPath)), xtx);
    }

    // The last few decompressed containers are kept: a stage archive is tens of megabytes to decompress.
    U8Archive Container(string path)
    {
        if (_containers.TryGetValue(path, out var cached)) return cached;
        if (_containers.Count >= 3) _containers.Remove(_containers.Keys.First());
        return _containers[path] = path.EndsWith("ObjectPack.arc.LZ", StringComparison.OrdinalIgnoreCase) ? _objectPack : U8Archive.OpenFile(Path.Combine(_romfs, path));
    }

    /// <summary>Opens a stage's first archive: its shared prop models and textures, its rooms and its object archives.</summary>
    public StageArchive OpenStage(string stage)
    {
        string path = Path.Combine("Stage", stage, "NX", $"{stage}_stg_l0.arc.LZ");
        if (!File.Exists(Path.Combine(_romfs, path))) throw new FileNotFoundException($"No stage '{stage}'.", path);
        return new StageArchive(stage, path, Container(path));
    }

    /// <summary>The stage palettes the game ships (object pack's Palette_Test archive): one per stage, plus variants such as F000sasaki. Palettes without a fog file are left out.</summary>
    public IEnumerable<string> PaletteNames => PaletteArchive.Paths.Where(p => p.EndsWith(".spf", StringComparison.Ordinal) && PaletteArchive.TryGet(Path.ChangeExtension(p, ".sff"), out _))
        .Select(p => Path.GetFileNameWithoutExtension(p)).Order(StringComparer.Ordinal);

    public StagePalette OpenPalette(string name) => StagePalette.Open(PaletteArchive.Get($"palette/{name}.spf").Span, PaletteArchive.Get($"palette/{name}.sff").Span,
        PaletteArchive.TryGet($"palette/{name}.eaf", out var eaf) ? eaf.Span : default);

    U8Archive PaletteArchive => _palettes ??= U8Archive.OpenFile(Path.Combine(_romfs, "Object", "NX", "Palette_Test.arc"));

    /// <summary>Opens one object's model file (BRRES) and its external texture container, if it has one.</summary>
    public ModelArchive OpenModelArchive(string name)
    {
        var archive = U8Archive.Open(_objectPack.Get($"oarc/{name}.arc").ToArray());
        var xtx = archive.TryGet("g3d/model.xtx", out var data) ? XtxFile.Open(data.Span) : null;
        return new ModelArchive(new BresFile(archive.Get("g3d/model.brres")), xtx);
    }
}

/// <summary>A model file and the .xtx its textures live in: stub TEX0 entries take their pixels from the .xtx in order.</summary>
public sealed record ModelArchive(BresFile Bres, XtxFile? Xtx);

/// <summary>A stage's main archive: g3d/stage.brres (props and the texture pool every room shares), rarc/*_rNN.arc rooms and oarc/*.arc objects such as the sky.</summary>
public sealed class StageArchive
{
    readonly U8Archive _archive;

    public string Name { get; }
    public string Container { get; }

    internal StageArchive(string name, string container, U8Archive archive) { Name = name; Container = container; _archive = archive; }

    public ModelArchive Props => Read(_archive, "g3d/stage.brres", "g3d/stage.xtx");

    public IEnumerable<string> Rooms => _archive.Paths.Where(p => p.StartsWith("rarc/", StringComparison.Ordinal) && p.EndsWith(".arc", StringComparison.Ordinal))
        .Select(p => Path.GetFileNameWithoutExtension(p)).Order();

    public ModelArchive Room(string room) => Read(U8Archive.Open(_archive.Get($"rarc/{room}.arc").ToArray()), "g3d/room.brres", "g3d/room.xtx");

    public IEnumerable<string> Objects => _archive.Paths.Where(p => p.StartsWith("oarc/", StringComparison.Ordinal) && p.EndsWith(".arc", StringComparison.Ordinal))
        .Select(p => Path.GetFileNameWithoutExtension(p)).Order();

    public ModelArchive Object(string name) => Read(U8Archive.Open(_archive.Get($"oarc/{name}.arc").ToArray()), "g3d/model.brres", "g3d/model.xtx");

    static ModelArchive Read(U8Archive archive, string bres, string xtx) =>
        new(new BresFile(archive.Get(bres)), archive.TryGet(xtx, out var data) ? XtxFile.Open(data.Span) : null);
}
