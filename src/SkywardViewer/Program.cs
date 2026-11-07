using SkywardViewer;

// SkywardViewer [romfs] [--id MODEL_OR_ARCHIVE] [--stage STAGE]
// With no romfs argument the viewer uses the folder it remembers, or shows a folder browser on start.
string? romfs = args.FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a)) ?? Environment.GetEnvironmentVariable("SKYWARD_ROMFS");
string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
using var app = new ViewerApp(romfs, Option("--id"), Option("--stage"));
app.Run();
return 0;
