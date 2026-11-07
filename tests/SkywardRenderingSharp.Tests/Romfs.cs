using SkywardRenderingSharp.Resources;

namespace SkywardRenderingSharp.Tests;

/// <summary>Finds the extracted romfs the data-driven tests read, from the SKYWARD_ROMFS environment variable.</summary>
static class Romfs
{
    public static string? Path => Environment.GetEnvironmentVariable("SKYWARD_ROMFS") is { Length: > 0 } p && Directory.Exists(System.IO.Path.Combine(p, "Stage")) ? p : null;

    static readonly Lazy<SkywardRom?> Shared = new(() => Path is { } p ? SkywardRom.Open(p) : null);

    public static SkywardRom? Rom => Shared.Value;
}

/// <summary>A test that needs the game's data; it is skipped when SKYWARD_ROMFS does not point at an extracted romfs.</summary>
sealed class RomFactAttribute : FactAttribute
{
    public RomFactAttribute()
    {
        if (Romfs.Path is null) Skip = "Set SKYWARD_ROMFS to an extracted Skyward Sword HD romfs to run this test.";
    }
}
