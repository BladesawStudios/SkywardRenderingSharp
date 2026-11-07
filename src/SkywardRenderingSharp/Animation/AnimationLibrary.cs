using SkywardRenderingSharp.Archives;
using SkywardRenderingSharp.Brres;

namespace SkywardRenderingSharp.Animation;

/// <summary>A BRRES whose "External" group holds LZ11-compressed one-clip BRRES files (PLCommonAnime: Link's animations, .rchaC).</summary>
/// <remarks>Clips are decompressed and parsed on first request.</remarks>
public sealed class AnimationLibrary
{
    readonly BresFile _bres;
    readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal), _patternOffsets = new(StringComparer.Ordinal), _srtOffsets = new(StringComparer.Ordinal);
    readonly Dictionary<string, ChrClip> _clips = new(StringComparer.Ordinal);
    readonly Dictionary<string, PatClip> _patterns = new(StringComparer.Ordinal);
    readonly Dictionary<string, SrtClip> _srt = new(StringComparer.Ordinal);

    public AnimationLibrary(ReadOnlyMemory<byte> brres)
    {
        _bres = new BresFile(brres);
        foreach (var (name, offset) in _bres.Groups["External"])
        {
            if (name.EndsWith(".rchaC", StringComparison.Ordinal)) _offsets[name[..^6]] = offset;
            else if (name.EndsWith(".rtpaC", StringComparison.Ordinal)) _patternOffsets[name[..^6]] = offset;
            else if (name.EndsWith(".rtsaC", StringComparison.Ordinal)) _srtOffsets[name[..^6]] = offset;
        }
    }

    public IEnumerable<string> ClipNames => _offsets.Keys;
    public IEnumerable<string> PatternNames => _patternOffsets.Keys;
    public IEnumerable<string> SrtNames => _srtOffsets.Keys;

    public ChrClip Get(string name)
    {
        if (_clips.TryGetValue(name, out var cached)) return cached;
        if (!_offsets.TryGetValue(name, out int offset)) throw new KeyNotFoundException($"No animation '{name}'.");
        var clipFile = new BresFile(Lz11.Decompress(_bres.Data.Span[offset..]));
        var (clipName, chr0) = clipFile.Groups.First(g => g.Key.StartsWith("AnmChr", StringComparison.Ordinal)).Value[0];
        return _clips[name] = ChrClip.Read(clipFile.Data, clipName, chr0);
    }

    public PatClip GetPattern(string name)
    {
        if (_patterns.TryGetValue(name, out var cached)) return cached;
        if (!_patternOffsets.TryGetValue(name, out int offset)) throw new KeyNotFoundException($"No texture pattern '{name}'.");
        var file = new BresFile(Lz11.Decompress(_bres.Data.Span[offset..]));
        var (patName, pat0) = file.Groups.First(g => g.Key.StartsWith("AnmTexPat", StringComparison.Ordinal)).Value[0];
        return _patterns[name] = PatClip.Read(file.Data, patName, pat0);
    }

    public SrtClip GetSrt(string name)
    {
        if (_srt.TryGetValue(name, out var cached)) return cached;
        if (!_srtOffsets.TryGetValue(name, out int offset)) throw new KeyNotFoundException($"No texture SRT animation '{name}'.");
        var file = new BresFile(Lz11.Decompress(_bres.Data.Span[offset..]));
        var (srtName, srt0) = file.Groups.First(g => g.Key.StartsWith("AnmTexSrt", StringComparison.Ordinal)).Value[0];
        return _srt[name] = SrtClip.Read(file.Data, srtName, srt0);
    }
}
