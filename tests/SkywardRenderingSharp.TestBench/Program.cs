using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;
using SkywardRenderingSharp;
using SkywardRenderingSharp.Animation;
using SkywardRenderingSharp.Lighting;
using SkywardRenderingSharp.Model;
using SkywardRenderingSharp.Rendering;
using SkywardRenderingSharp.Resources;

// TestBench dump     <romfs> <model>      a model's draws, bounds and skeleton check (SKY_STAGES=<material> prints its GX program)
// TestBench textures <romfs> <archive> <out.png>    contact sheet of an archive's textures
// TestBench texcheck <romfs>                        decode every XTX texture in every archive
// TestBench clips    <romfs>                        list Link's animation clips
// TestBench layers   <romfs> <out.json>             count every stage's story-layer placements (Resources/layers.json)
// TestBench render   <romfs> <out.png> [--post on|off] [--count N] [--spacing S] [--yaw DEG] [--size WxH] [--distance D] [--target-y Y]
//                    [--clip NAME] [--frame F] [--face NAME] [--face-frame F] [--dof on|off] [--light-set N] [--cull on|off]
if (args.Length < 2) { Console.WriteLine("usage: dump|textures|texcheck|clips|render ..."); return 1; }
var rom = SkywardRom.Open(args[1]);
return args[0] switch
{
    "dump" => Dump(rom, args.Length > 3 ? args[3] : "", args[2]),
    "textures" => Textures(rom, args[2], args[3]),
    "object" => Info(rom, args[2]),
    "texcheck" => TexCheck(rom),
    "clips" => Clips(rom),
    "layers" => Layers(rom, args[2]),
    "find" => Find(rom, args[2]),
    "lightmaps" => Lightmaps(rom),
    "gra" => Gra(args[1]),
    "texstats" => TexStats(rom, args[2], args[3]),
    _ => Render(rom, args),
};

static int Layers(SkywardRom rom, string path)
{
    using var file = File.Create(path);
    StageLayers.Count(rom).Write(file);
    return 0;
}

static string Opt(string[] a, string name, string fallback) => Array.IndexOf(a, name) is var i and >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;

static int TexStats(SkywardRom rom, string model, string texture)
{
    var img = new ModelTextures(rom.Open(rom.Index.Find(model)[0])).Find(texture)!;
    var sum = new double[4]; int n = img.Width * img.Height; int alpha0 = 0, alpha255 = 0;
    for (int i = 0; i < n; i++)
    {
        for (int c = 0; c < 4; c++) sum[c] += img.Pixels[i * 4 + c];
        if (img.Pixels[i * 4 + 3] == 0) alpha0++; else if (img.Pixels[i * 4 + 3] == 255) alpha255++;
    }
    for (int y = 0; y < img.Height; y += img.Height / 8)
        Console.WriteLine(string.Join(" ", Enumerable.Range(0, 8).Select(k => { int x = k * img.Width / 8 + img.Width / 16, yy = y + img.Height / 16; int o = (Math.Min(yy, img.Height - 1) * img.Width + x) * 4; return $"({img.Pixels[o]},{img.Pixels[o + 1]},{img.Pixels[o + 2]})"; })));
    Console.WriteLine($"srgb={img.Srgb}");
    Console.WriteLine($"{texture} {img.Width}x{img.Height} mean rgba {sum[0] / n:F0} {sum[1] / n:F0} {sum[2] / n:F0} {sum[3] / n:F0}  alpha0 {alpha0}  alpha255 {alpha255} of {n}");
    return 0;
}

static int Gra(string romfs)
{
    var system = SkywardRenderingSharp.Archives.GameArchive.OpenFile(Path.Combine(romfs, "Object", "NX", "System.arc"));
    foreach (string path in SkywardRenderingSharp.Archives.GameArchive.Paths(system).Where(p => p.EndsWith(".bti")))
    {
        var bti = TexSharp.Bti.Read(SkywardRenderingSharp.Archives.GameArchive.Get(system, path));
        int w = bti.Width, h = bti.Height;
        var pixels = bti.ToRgba8();
        Console.WriteLine($"{path} {bti.Format} {w}x{h}");
        for (int y = 0; y < h; y++) Console.WriteLine("   " + string.Join(" ", Enumerable.Range(0, w).Where(x => x % 4 == 0).Select(x => $"{pixels[(y * w + x) * 4]:x2}/{pixels[(y * w + x) * 4 + 1]:x2}/{pixels[(y * w + x) * 4 + 2]:x2}/{pixels[(y * w + x) * 4 + 3]:x2}")));
    }
    return 0;
}

static int Lightmaps(SkywardRom rom)
{
    foreach (string name in rom.Lightmaps.Names)
    {
        var c = rom.Lightmaps.Curve(name);
        Console.WriteLine($"{name,-20} 0:{c[0]:F2} 32:{c[32]:F2} 64:{c[64]:F2} 96:{c[96]:F2} 127:{c[127]:F2}");
    }
    return 0;
}

static int Info(SkywardRom rom, string id)
{
    var first = rom.Index.Find(id).FirstOrDefault() ?? rom.Index.InArchive(id).First();
    var package = rom.Open(first);
    Console.WriteLine($"package {first}");
    foreach (var name in package.ModelNames)
    {
        var m = Mdl0Model.Read(package.Bres, name);
        Console.WriteLine($"  {name,-24} bones={m.Bones.Count,3} meshes={m.Meshes.Count,3} first bones: {string.Join(",", m.Bones.Take(int.Parse(Environment.GetEnvironmentVariable("SKY_BONES") ?? "4")).Select(b => b.Name))}");
    }
    return 0;
}

static int Find(SkywardRom rom, string text)
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var index = rom.Index;
    Console.WriteLine($"index ready in {watch.Elapsed.TotalSeconds:F1}s: {index.ModelNames.Count()} model names");
    foreach (var l in index.Search(text).Take(40)) Console.WriteLine($"  {l}");
    return 0;
}

static int Clips(SkywardRom rom)
{
    var library = rom.OpenAnimations("PLCommonAnime");
    Console.WriteLine($"clips: {string.Join(' ', library.ClipNames)}");
    Console.WriteLine($"face patterns: {string.Join(' ', library.PatternNames)}");
    return 0;
}

static void DumpMaterial(Material m)
{
    Console.WriteLine($"    texgens: {string.Join(" | ", m.TexGens.Select(g => $"src{g.SourceRow} proj{g.Projective} dual{g.DualMatrix} norm{g.Normalize}"))}");
    Console.WriteLine($"    layers: {string.Join(" | ", m.Layers.Select(l => $"{l.TextureName} map{l.TexMap}"))}");
    Console.WriteLine($"    texmtx: {string.Join(" | ", m.TextureMatrices.Select(x => $"mode{x.MapMode} cam{x.Camera} light{x.Light} s({x.Scale.X:F1},{x.Scale.Y:F1}) t({x.Translation.X:F1},{x.Translation.Y:F1})"))}");
    for (int i = 0; i < m.PostMatrices.Count; i++) Console.WriteLine($"    post[{i}]: {m.PostMatrices[i]?.ToString() ?? "none"}");
    Console.WriteLine($"    colors: regs {string.Join(" ", m.Colors.Registers.Select(v => v.ToString("F2")))}  konst {string.Join(" ", m.Colors.Konst.Select(v => v.ToString("F2")))}");
    Console.WriteLine($"    channels: {string.Join(" | ", m.Channels.Select(c => $"color[{c.Color}] alpha[{c.Alpha}] mat{c.MaterialColor:X8} amb{c.AmbientColor:X8}"))}");
    Console.WriteLine($"    pixel: {m.Pixel}");
    string[] ci = ["CPREV", "APREV", "C0", "A0", "C1", "A1", "C2", "A2", "TEXC", "TEXA", "RASC", "RASA", "ONE", "HALF", "KONST", "ZERO"];
    string[] ai = ["APREV", "A0", "A1", "A2", "TEXA", "RASA", "KONST", "ZERO"];
    for (int i = 0; i < m.Stages.Count; i++)
    {
        var s = m.Stages[i];
        Console.WriteLine($"    stage{i}: color ({ci[s.Color.A]},{ci[s.Color.B]},{ci[s.Color.C]},{ci[s.Color.D]}) x{s.Color.Scale} ->r{s.Color.Dest}   alpha ({ai[s.Alpha.A]},{ai[s.Alpha.B]},{ai[s.Alpha.C]},{ai[s.Alpha.D]}) ->r{s.Alpha.Dest}   tex{s.TexMap}/coord{s.TexCoord} konst c{s.KonstColor} a{s.KonstAlpha} bias{s.Color.Bias} ras{s.RasChannel} rswap{s.RasSwap} tswap{s.TexSwap}");
    }
}

static int Dump(SkywardRom rom, string id, string model)
{
    var file = rom.Open(rom.Index.Find(model).First(l => l.ToString().Contains(id)));
    var m = Mdl0Model.Read(file.Bres, model);
    var world = new Matrix4x4[m.Bones.Count];
    float worst = 0;
    foreach (var b in m.Bones)
    {
        world[b.Index] = b.Parent < 0 ? b.LocalMatrix : b.LocalMatrix * world[b.Parent];
        worst = MathF.Max(worst, MathF.Abs(world[b.Index].M41 - b.World.M41) + MathF.Abs(world[b.Index].M11 - b.World.M11));
    }
    Console.WriteLine($"{m.Name}: {m.Bones.Count} bones (skeleton recompute error {worst:E1}), {m.Materials.Count} materials, {m.Meshes.Count} meshes");
    foreach (var d in m.Draws)
    {
        var mesh = m.Meshes[d.Mesh]; var mat = m.Materials[d.Material];
        if (Environment.GetEnvironmentVariable("SKY_STAGES") == mat.Name) DumpMaterial(mat);
        if (Environment.GetEnvironmentVariable("SKY_NORMALS") == mat.Name)
            foreach (var v in mesh.Vertices.Where(v => v.Position.X > 13).Take(12)) Console.WriteLine($"    pos {v.Position} normal {v.Normal} len {v.Normal.Length():F2} color {v.Color}");
        if (Environment.GetEnvironmentVariable("SKY_NORMALS") == "all") Console.WriteLine($"    {mat.Name}: {mesh.Vertices.Select(v => v.Normal).Distinct().Count()} distinct normals over {mesh.Vertices.Length} vertices");
        if (Environment.GetEnvironmentVariable("SKY_NORMALS") == "weights") Console.WriteLine($"    {mat.Name}: weight sum<0.99 on {mesh.Vertices.Count(v => (v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W) < 0.99f)} of {mesh.Vertices.Length}, no influence {mesh.Vertices.Count(v => (v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W) == 0)}, bone {d.Bone}");
        if (Environment.GetEnvironmentVariable("SKY_NORMALS") == "bones") Console.WriteLine("    " + mat.Name + ": bones " + string.Join(",", mesh.Vertices.SelectMany(v => new[] { v.BoneIds.X }).Distinct().Select(b => b + "=" + (b >= 0 && b < m.Bones.Count ? m.Bones[(int)b].Name : "?"))));
        if (Environment.GetEnvironmentVariable("SKY_NORMALS") == "alpha") Console.WriteLine($"    {mat.Name}: vertex rgb mean {mesh.Vertices.Average(v => v.Color.X):F2} alpha min {mesh.Vertices.Min(v => v.Color.W):F2} mean {mesh.Vertices.Average(v => v.Color.W):F2} max {mesh.Vertices.Max(v => v.Color.W):F2}");
        var min = mesh.Vertices.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position));
        var max = mesh.Vertices.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position));
        Console.WriteLine($"  alphaWrite={mat.Pixel.AlphaWrite} blend={mat.Pixel.BlendEnabled} cull={mat.CullMode} {mat.Name,-20} {mesh.Name,-10} tris={mesh.Indices.Length / 3,4} min=({min.X:F0},{min.Y:F0},{min.Z:F0}) max=({max.X:F0},{max.Y:F0},{max.Z:F0})");
    }
    return 0;
}

// Writes a labelled contact sheet of every texture a model archive resolves, to check the .xtx pairing by eye.
static int Textures(SkywardRom rom, string id, string path)
{
    var file = rom.Open(rom.Index.Find(id)[0]);
    var textures = new ModelTextures(file);
    var names = file.Bres["Textures(NW4R)"]?.Files.Select(t => t.Name).ToList() ?? [];
    const int cell = 150, cols = 7;
    using var bitmap = new SKBitmap(cols * cell, (names.Count + cols - 1) / cols * (cell + 14));
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.DimGray);
    using var paint = new SKPaint { Color = SKColors.White };
    using var font = new SKFont { Size = 11 };
    for (int i = 0; i < names.Count; i++)
    {
        int x = i % cols * cell, y = i / cols * (cell + 14);
        canvas.DrawText($"{i} {names[i]}", x + 2, y + 11, font, paint);
        if (textures.Find(names[i]) is not { } img) continue;
        using var tex = ToBitmap(img.Pixels, img.Width, img.Height);
        canvas.DrawBitmap(tex, new SKRect(x + 2, y + 14, x + cell - 2, y + 14 + cell - 4));
    }
    Save(bitmap, path);
    return 0;
}

static int TexCheck(SkywardRom rom)
{
    int ok = 0, failed = 0, archives = 0; var errors = new Dictionary<string, int>();
    foreach (var name in rom.ModelArchives)
    {
        ModelArchive model;
        try { model = rom.OpenModelArchive(name); } catch (FileNotFoundException) { continue; }
        if (model.Xtx is not { } xtx) continue;
        archives++;
        foreach (var texture in xtx.Textures)
        {
            try { texture.ToRgba8(); ok++; }
            catch (Exception e) { failed++; errors[e.Message] = errors.GetValueOrDefault(e.Message) + 1; }
        }
    }
    Console.WriteLine($"{archives} archives with xtx: {ok} textures decoded, {failed} failed");
    foreach (var (m, c) in errors) Console.WriteLine($"  {c} x {m}");
    return 0;
}

static int Render(SkywardRom rom, string[] args)
{
    var size = Opt(args, "--size", "1280x720").Split('x');
    int w = int.Parse(size[0]), h = int.Parse(size[1]);
    using var window = Window.Create(WindowOptions.Default with
    {
        Size = new(w, h), IsVisible = false, API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 3)),
    });
    window.Initialize();
    using var gl = GL.GetApi(window.GLContext);
    using var renderer = new SkywardRenderer(gl, rom);
    renderer.Settings.Paint = renderer.Settings.Bloom = Opt(args, "--post", "on") == "on";
    renderer.Settings.DisableCulling = Opt(args, "--cull", "on") == "off";
    if (Opt(args, "--dof", "off") == "on") renderer.UseCutsceneDepthOfField();
    if (Opt(args, "--dof-focus", "") is { Length: > 0 } focus) { float f = float.Parse(focus), wd = float.Parse(Opt(args, "--dof-width", "1000")); renderer.Settings.DepthOfField = true; renderer.Settings.DofFar = new Vector2(f - wd / 2, f + wd / 2); }
    renderer.Settings.DofNear = float.Parse(Opt(args, "--dof-near", "0"));
    renderer.Settings.PaintConstant = float.Parse(Opt(args, "--paint-constant", renderer.Settings.PaintConstant.ToString()));
    renderer.Settings.PaintPasses = int.Parse(Opt(args, "--paint-passes", renderer.Settings.PaintPasses.ToString()));
    renderer.Settings.BloomStrength = float.Parse(Opt(args, "--bloom", renderer.Settings.BloomStrength.ToString()));
    renderer.Settings.PaintFar = new Vector2(float.Parse(Opt(args, "--paint-far", renderer.Settings.PaintFar.X.ToString())), renderer.Settings.PaintFar.Y);
    renderer.Lights = Opt(args, "--light-intensity", "") is { Length: > 0 } intensity ? LightRig.FromBlight(rom.Lights, int.Parse(Opt(args, "--light-set", "0")), float.Parse(intensity)) : LightRig.Characters();

    if (Opt(args, "--stage", "") is { Length: > 0 } stageId) return RenderStage(rom, renderer, stageId, args, w, h);
    if (Opt(args, "--palette", "") is { Length: > 0 } objectPalette)
        renderer.ApplyPalette(rom.OpenPalette(objectPalette), int.Parse(Opt(args, "--slot", "6")), 0, false, Opt(args, "--light-intensity", "") is { Length: > 0 } li ? float.Parse(li) : null);
    var obj = renderer.LoadObject(Opt(args, "--id", "Alink"));
    string[] clips = Opt(args, "--clip", "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    string faceName = Opt(args, "--face", "");
    var face = faceName == "" ? null : obj.Animations.Pattern(faceName)?.Evaluate(float.Parse(Opt(args, "--face-frame", "0")));
    string gazeName = Opt(args, "--gaze", "");
    var gaze = gazeName == "" ? null : obj.Animations.Srt(gazeName)?.Evaluate(float.Parse(Opt(args, "--gaze-frame", "0")));
    int count = int.Parse(Opt(args, "--count", "1"));
    float spacing = float.Parse(Opt(args, "--spacing", "700")), yaw = float.Parse(Opt(args, "--yaw", "20")), frame = float.Parse(Opt(args, "--frame", "0"));
    var instances = new List<ModelInstance>();
    for (int i = 0; i < count; i++)
    {
        var world = Matrix4x4.CreateRotationY(yaw * MathF.PI / 180f) * Matrix4x4.CreateTranslation(count == 1 ? 0 : (i % 2 == 0 ? -80 : 80), 0, -i * spacing);
        var pose = clips.Length == 0 ? obj.BindPose : obj.Animations.Clip(clips[i % clips.Length])?.Evaluate(obj.Main.Model.Bones, frame) ?? obj.BindPose;
        instances.AddRange(obj.Instances(world, pose, face, gaze));
    }
    var bmin = obj.Main.BoundsMin; var bmax = obj.Main.BoundsMax; var center = (bmin + bmax) / 2; float radius = Vector3.Distance(bmin, bmax) / 2;
    if (args.Contains("--list")) foreach (var inst in instances.Take(8)) Console.WriteLine($"  instance {inst.Model.Model.Name,-12} at ({inst.World.M41:F0},{inst.World.M42:F0},{inst.World.M43:F0}) draws={Enumerable.Range(0, inst.Model.Model.Draws.Count).Count(i => inst.DrawFilter?.Invoke(inst.Model.Model.Draws[i], inst.Model.Model.Materials[inst.Model.Model.Draws[i].Material]) ?? true)}");
    float distance = float.Parse(Opt(args, "--distance", (radius * 3.2f).ToString("F1")));
    float targetY = float.Parse(Opt(args, "--target-y", center.Y.ToString("F1")));
    renderer.Render(new Camera(new Vector3(0, targetY + 15, distance), new Vector3(0, targetY, 0)), instances, w, h);
    var pixels = renderer.ReadPixels();
    using var bitmap = ToBitmap(pixels, w, h);
    Save(bitmap, args[2]);
    Console.WriteLine($"wrote {args[2]}");
    return 0;
}

static Vector3 Vec(string text) { var p = text.Split(',').Select(float.Parse).ToArray(); return new Vector3(p[0], p[1], p[2]); }

static int RenderStage(SkywardRom rom, SkywardRenderer renderer, string stageId, string[] args, int w, int h)
{
    var stage = renderer.LoadStage(stageId, Opt(args, "--room", "") is { Length: > 0 } room ? room : null);
    foreach (var model in stage.Models) Console.WriteLine($"  {model.Model.Name}: missing textures {string.Join(' ', model.MissingTextures)}");
    if (Opt(args, "--light-intensity", "") == "") renderer.Lights = stage.Lights;
    PaletteSlot? slot = null;
    if (Opt(args, "--palette", "") is { Length: > 0 } palette)
    {
        slot = renderer.ApplyPalette(rom.OpenPalette(palette), int.Parse(Opt(args, "--slot", "6")), int.Parse(Opt(args, "--fog-layer", "0")), args.Contains("--fog"), Opt(args, "--light-intensity", "") is { Length: > 0 } intensityText ? float.Parse(intensityText) : null);
        Console.WriteLine($"palette {palette} slot {slot.Index}: paint far {slot.PaintFar} near {slot.PaintNear}, sky {slot.SkyTop}/{slot.SkyHorizon}, fog {string.Join(" ", slot.Fog)}");
    }
    var (min, max) = stage.Bounds();
    var center = (min + max) / 2;
    Console.WriteLine($"stage {stage.Name} room {stage.Room}: {stage.Models.Count} models, sky {stage.HasSky}, bounds {min} .. {max}");
    var eye = Opt(args, "--eye", "") is { Length: > 0 } e ? Vec(e) : center + new Vector3(0, (max.Y - min.Y) * 0.8f + 3000, (max.Z - min.Z) * 0.9f);
    var target = Opt(args, "--target", "") is { Length: > 0 } t ? Vec(t) : center;
    renderer.Render(new Camera(eye, target, float.Parse(Opt(args, "--fov", "40")), 50f, 200000f), stage.World(slot), w, h, Opt(args, "--sky", "on") == "on" ? stage.Sky(eye, slot) : null);
    using var bitmap = ToBitmap(renderer.ReadPixels(), w, h);
    Save(bitmap, args[2]);
    Console.WriteLine($"wrote {args[2]}");
    return 0;
}

static SKBitmap ToBitmap(byte[] rgba, int w, int h)
{
    var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
    return bitmap;
}

static void Save(SKBitmap bitmap, string path)
{
    using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 95);
    using var file = File.Create(path);
    data.SaveTo(file);
}
