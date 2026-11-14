using BresSharp;
using LzSharp;

namespace SkywardRenderingSharp.Animation;

/// <summary>A BRRES whose "External" group holds LZ11-compressed one-clip BRRES files (PLCommonAnime: Link's animations, .rchaC).</summary>
/// <remarks>Clips are decompressed and parsed on first request.</remarks>
public sealed class AnimationLibrary
{
    readonly Dictionary<string, BresSubfile> _clipFiles = new(StringComparer.Ordinal), _patternFiles = new(StringComparer.Ordinal), _srtFiles = new(StringComparer.Ordinal);
    readonly Dictionary<string, ChrClip> _clips = new(StringComparer.Ordinal);
    readonly Dictionary<string, PatClip> _patterns = new(StringComparer.Ordinal);
    readonly Dictionary<string, SrtClip> _srt = new(StringComparer.Ordinal);

    public AnimationLibrary(byte[] brres)
    {
        foreach (var file in BresFile.FromBinary(brres)["External"]?.Files ?? [])
        {
            if (file.Name.EndsWith(".rchaC", StringComparison.Ordinal)) _clipFiles[file.Name[..^6]] = file;
            else if (file.Name.EndsWith(".rtpaC", StringComparison.Ordinal)) _patternFiles[file.Name[..^6]] = file;
            else if (file.Name.EndsWith(".rtsaC", StringComparison.Ordinal)) _srtFiles[file.Name[..^6]] = file;
        }
    }

    public IEnumerable<string> ClipNames => _clipFiles.Keys;
    public IEnumerable<string> PatternNames => _patternFiles.Keys;
    public IEnumerable<string> SrtNames => _srtFiles.Keys;

    public ChrClip Get(string name) => Cached(_clips, _clipFiles, name, "AnmChr(NW4R)", ChrClip.Read);

    public PatClip GetPattern(string name) => Cached(_patterns, _patternFiles, name, "AnmTexPat(NW4R)", PatClip.Read);

    public SrtClip GetSrt(string name) => Cached(_srt, _srtFiles, name, "AnmTexSrt(NW4R)", SrtClip.Read);

    // An External entry is a compressed BRRES holding the one clip.
    static T Cached<T>(Dictionary<string, T> cache, Dictionary<string, BresSubfile> files, string name, string folder, Func<BresSubfile, T> read)
    {
        if (cache.TryGetValue(name, out var cached)) return cached;
        if (!files.TryGetValue(name, out var file)) throw new KeyNotFoundException($"No animation '{name}'.");
        var clip = BresFile.FromBinary(Lz.Decompress(file.Data))[folder]?.Files.FirstOrDefault() ?? throw new InvalidDataException($"'{name}' holds no {folder} clip.");
        return cache[name] = read(clip);
    }
}
