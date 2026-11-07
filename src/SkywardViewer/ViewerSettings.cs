using System.Text.Json;

namespace SkywardViewer;

/// <summary>What the viewer remembers between runs: the romfs folder last used.</summary>
sealed class ViewerSettings
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkywardViewer", "settings.json");

    public string Romfs { get; set; } = "";

    public static ViewerSettings Load()
    {
        try { return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* remembering the folder is a convenience */ }
    }
}
