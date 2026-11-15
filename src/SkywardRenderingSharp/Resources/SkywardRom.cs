using BresSharp;
using RevolutionEggSharp;
using SkywardRenderingSharp.Animation;
using SkywardRenderingSharp.Archives;
using SkywardRenderingSharp.Content;
using SkywardRenderingSharp.Lighting;
using SkywardRenderingSharp.Model;
using SkywardRenderingSharp.Shaders;
using SkywardRenderingSharp.Textures;
using TexSharp;
using U8Sharp;
using XtxSharp;

namespace SkywardRenderingSharp.Resources;

/// <summary>An extracted Skyward Sword HD romfs: its model archives and the shared lighting and post-processing data.</summary>
/// <remarks>Open it on the romfs folder (the one holding Object, Stage and Shaders).</remarks>
public sealed class SkywardRom
{
    readonly U8File _objectPack;
    readonly string _romfs;
    readonly Dictionary<string, U8File> _containers = new(StringComparer.OrdinalIgnoreCase);
    ContentIndex? _index;
    U8File? _palettes;

    public LightFile Lights { get; }
    public LightmapFile Lightmaps { get; }

    /// <summary>The engine's own lightmap textures: the Gra* gradients from System.arc under the Lm* names models use, plus every other texture the engine supplies.</summary>
    public ModelTextures SystemTextures { get; }
    public PostParameters Post { get; }
    public DofParameters CutsceneDof { get; }

    SkywardRom(string romfs, U8File objectPack, U8File systemArchive, byte[] systemModel, LightFile lights, LightmapFile lightmaps, PostParameters post, DofParameters dof)
    {
        _romfs = romfs; _objectPack = objectPack; SystemTextures = new ModelTextures(new ModelArchive(BresFile.FromBinary(systemModel), null), null, GradientRamps(systemArchive, lightmaps)); Lights = lights; Lightmaps = lightmaps; Post = post; CutsceneDof = dof;
    }

    /// <summary>One of the game's compiled post-processing programs (<c>Shaders/custom</c>), such as <c>BloomMask_Custom</c>.</summary>
    public NvProgramBinary PostProgram(string name) => NvProgramBinary.Read(File.ReadAllBytes(Path.Combine(_romfs, "Shaders", "custom", name)));

    public static SkywardRom Open(string romfs)
    {
        string nx = Path.Combine(romfs, "Object", "NX");
        var pack = GameArchive.OpenFile(Path.Combine(nx, "ObjectPack.arc.LZ"));
        var system = GameArchive.OpenFile(Path.Combine(nx, "System.arc"));
        return new SkywardRom(romfs, pack, system, system.Get("g3d/model.brres"), new LightFile(new EggLight(system.Get("dat/default.blight"))),
            new LightmapFile(new EggLightMap(system.Get("dat/default.blmap"))), PostParameters.Read(system), DofParameters.Read(new EggDepthOfField(system.Get("dat/opning.bdof"))));
    }

    // The engine loads the Gra* gradients (System.arc tex/*.bti, 64x4, identical rows) into GPU slots 10 to 17 at start-up (0xdf1ef4) and binds them in place of models' Lm* textures.
    // Each gradient is scaled by its .blmap curve (128 samples across the ramp): the shared curves are 1.0 apart from a few samples at the left edge, so this barely changes them.
    static Dictionary<string, RgbaImage> GradientRamps(U8File system, LightmapFile lightmaps)
    {
        var map = new Dictionary<string, string>
        {
            ["LmChaMulti"] = "GraChaMulti", ["LmChaSkin"] = "GraChaSkin", ["LmChaAdd"] = "GraChaAdd1", ["LmGetItem"] = "GraGetItem", ["LmGetItemAdd"] = "GraChaAdd0",
            ["LmUIMulti"] = "GraUIMulti", ["LmUIAdd"] = "GraUIAdd0",
        };
        var result = new Dictionary<string, RgbaImage>();
        foreach (var (lightmap, gradient) in map)
        {
            var bti = Bti.Read(system.Get($"tex/{gradient}.bti"));
            var image = new RgbaImage(bti.Width, bti.Height, bti.ToRgba8());
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

    public IEnumerable<string> ModelArchives => _objectPack.Paths().Where(p => p.StartsWith("oarc/", StringComparison.Ordinal) && p.EndsWith(".arc", StringComparison.Ordinal))
        .Select(p => Path.GetFileNameWithoutExtension(p));

    /// <summary>The clips a model can play: its own file's, and Link's shared library (PLCommonAnime) when its skeleton is Link's (it has "backbone1").</summary>
    public AnimationSet AnimationsOf(ModelArchive package, Mdl0Model model) =>
        new(package.Bres, model.Bones.Any(b => b.Name == "backbone1") && Index.Find("al").Count > 0 ? _linkClips ??= OpenAnimations("PLCommonAnime") : null);

    AnimationLibrary? _linkClips;

    /// <summary>Opens an animation library archive such as PLCommonAnime (Link's clips).</summary>
    public AnimationLibrary OpenAnimations(string name) =>
        new(GameArchive.Open(_objectPack.Get($"oarc/{name}.arc")).Get("g3d/model.brres"));

    /// <summary>The index of every model in the romfs. The first call builds it (a minute or so) and caches it on disk.</summary>
    public ContentIndex Index => _index ??= ContentIndex.Open(_romfs);

    /// <summary>Opens the BRRES and textures a model location points to.</summary>
    public ModelArchive Open(ModelLocation location)
    {
        var archive = Container(location.Container);
        foreach (string step in location.Chain) archive = GameArchive.Open(archive.Get(step));
        return ModelArchive.Read(archive, location.BresPath, location.XtxPath);
    }

    // The last few decompressed containers are kept: a stage archive is tens of megabytes to decompress.
    U8File Container(string path)
    {
        if (_containers.TryGetValue(path, out var cached)) return cached;
        if (_containers.Count >= 3) _containers.Remove(_containers.Keys.First());
        return _containers[path] = path.EndsWith("ObjectPack.arc.LZ", StringComparison.OrdinalIgnoreCase) ? _objectPack : GameArchive.OpenFile(Path.Combine(_romfs, path));
    }

    /// <summary>The stages the romfs holds (Stage/*/NX/*_stg_l0.arc.LZ), by name: "F000" is Skyloft.</summary>
    public IEnumerable<string> StageNames => Directory.EnumerateDirectories(Path.Combine(_romfs, "Stage"))
        .Select(Path.GetFileName).OfType<string>().Where(s => File.Exists(Path.Combine(_romfs, "Stage", s, "NX", $"{s}_stg_l0.arc.LZ"))).Order(StringComparer.Ordinal);

    /// <summary>Every room with models, by stage then room ("F000", "F000_r00"): the rarc archives of each stage's first archive, as the
    /// content index lists them, so nothing is decompressed once the index is built.</summary>
    public IEnumerable<(string Stage, string Room)> RoomNames => Index.All
        .Where(l => l.Chain is [var room] && room.StartsWith("rarc/", StringComparison.Ordinal) && l.Container.EndsWith("_stg_l0.arc.LZ", StringComparison.OrdinalIgnoreCase))
        .Select(l => (Stage: Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(l.Container)))!, Room: l.Archive)).Distinct().Order();

    /// <summary>The textures every stage's models fall back to: the object pack's SkyCmn package (sky, clouds, distant islands), then the engine's own.</summary>
    public ModelTextures SkyTextures => _skyTextures ??= new ModelTextures(OpenModelArchive("SkyCmn"), SystemTextures);

    ModelTextures? _skyTextures;

    /// <summary>One room of a stage, read but not uploaded: its models (without the shadow-pass "_s" twins), the textures they resolve through
    /// (the room's, the stage's shared pool, then <see cref="SkyTextures"/>), the objects its room.bzs places, and the models
    /// <see cref="ActorModels.Default"/> says the always-present ones draw.</summary>
    public RoomData OpenRoom(string stage, string room)
    {
        var archive = OpenStage(stage);
        var file = archive.Room(room);
        var pool = new ModelTextures(archive.Props, SkyTextures);
        var models = file.ModelNames.Where(m => !m.EndsWith("_s", StringComparison.Ordinal)).Select(m => Mdl0Model.Read(file.Bres, m)).ToList();
        var placements = archive.RoomPlacements(room);
        var objects = new RoomObjectModels(this, archive, pool);
        var drawn = placements.Select(p => p.AlwaysPresent ? objects.Of(ActorModels.Default.PartsOf(p)) : []).ToList();
        return new RoomData(stage, room, models, new ModelTextures(file, pool), placements, objects.Models, drawn, objects.Missing);
    }

    /// <summary>Opens a stage's first archive: its shared prop models and textures, its rooms and its object archives.</summary>
    public StageArchive OpenStage(string stage)
    {
        string path = Path.Combine("Stage", stage, "NX", $"{stage}_stg_l0.arc.LZ");
        if (!File.Exists(Path.Combine(_romfs, path))) throw new FileNotFoundException($"No stage '{stage}'.", path);
        return new StageArchive(stage, path, Container(path));
    }

    /// <summary>The stage palettes the game ships (object pack's Palette_Test archive): one per stage, plus variants such as F000sasaki. Palettes without a fog file are left out.</summary>
    public IEnumerable<string> PaletteNames => PaletteArchive.Paths().Where(p => p.EndsWith(".spf", StringComparison.Ordinal) && PaletteArchive.TryGet(Path.ChangeExtension(p, ".sff"), out _))
        .Select(p => Path.GetFileNameWithoutExtension(p)).Order(StringComparer.Ordinal);

    public StagePalette OpenPalette(string name) => StagePalette.Open(PaletteArchive.Get($"palette/{name}.spf"), PaletteArchive.Get($"palette/{name}.sff"),
        PaletteArchive.TryGet($"palette/{name}.eaf", out var eaf) ? eaf.Data : null);

    U8File PaletteArchive => _palettes ??= GameArchive.OpenFile(Path.Combine(_romfs, "Object", "NX", "Palette_Test.arc"));

    /// <summary>Opens one object's model file (BRRES) and its external texture container, if it has one.</summary>
    public ModelArchive OpenModelArchive(string name)
    {
        return ModelArchive.Read(GameArchive.Open(_objectPack.Get($"oarc/{name}.arc")), "g3d/model.brres", "g3d/model.xtx");
    }
}

/// <summary>A model file and the .xtx its textures live in: TEX0 entries marked for it take their pixels from the .xtx in order.</summary>
public sealed record ModelArchive(BresFile Bres, XtxFile? Xtx)
{
    public IEnumerable<string> ModelNames => Bres["3DModels(NW4R)"]?.Files.Select(f => f.Name) ?? [];

    public static ModelArchive Read(U8File archive, string bres, string xtx) =>
        new(BresFile.FromBinary(archive.Get(bres)), archive.TryGet(xtx, out var data) ? XtxFile.FromBinary(data.Data) : null);
}

/// <summary>A stage's main archive: g3d/stage.brres (props and the texture pool every room shares), rarc/*_rNN.arc rooms and oarc/*.arc objects such as the sky.</summary>
public sealed class StageArchive
{
    readonly U8File _archive;

    public string Name { get; }
    public string Container { get; }

    internal StageArchive(string name, string container, U8File archive) { Name = name; Container = container; _archive = archive; }

    public ModelArchive Props => ModelArchive.Read(_archive, "g3d/stage.brres", "g3d/stage.xtx");

    public IEnumerable<string> Rooms => _archive.Paths().Where(p => p.StartsWith("rarc/", StringComparison.Ordinal) && p.EndsWith(".arc", StringComparison.Ordinal))
        .Select(p => Path.GetFileNameWithoutExtension(p)).Order();

    public ModelArchive Room(string room) => ModelArchive.Read(GameArchive.Open(_archive.Get($"rarc/{room}.arc")), "g3d/room.brres", "g3d/room.xtx");

    public IEnumerable<string> Objects => _archive.Paths().Where(p => p.StartsWith("oarc/", StringComparison.Ordinal) && p.EndsWith(".arc", StringComparison.Ordinal))
        .Select(p => Path.GetFileNameWithoutExtension(p)).Order();

    public ModelArchive Object(string name) => ModelArchive.Read(GameArchive.Open(_archive.Get($"oarc/{name}.arc")), "g3d/model.brres", "g3d/model.xtx");

    /// <summary>The objects a room places: every actor record of its dat/room.bzs, on every layer.</summary>
    public IReadOnlyList<Placement> RoomPlacements(string room) => Placement.Read(GameArchive.Open(_archive.Get($"rarc/{room}.arc")).Get("dat/room.bzs"));

    /// <summary>The objects the stage itself places (dat/stage.bzs): the ones that belong to no single room.</summary>
    public IReadOnlyList<Placement> StagePlacements => Placement.Read(_archive.Get("dat/stage.bzs"));
}
