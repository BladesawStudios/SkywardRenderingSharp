using SkywardRenderingSharp.Brres;

namespace SkywardRenderingSharp.Animation;

/// <summary>The animations a model can play: the clips stored in its own BRRES, plus a shared library when the skeleton matches one.</summary>
/// <remarks>Names are the clip names as the game stores them. Looking a clip up reads it on first use.</remarks>
public sealed class AnimationSet
{
    readonly Dictionary<string, ChrClip> _bone = new(StringComparer.Ordinal);
    readonly Dictionary<string, PatClip> _patterns = new(StringComparer.Ordinal);
    readonly Dictionary<string, SrtClip> _srt = new(StringComparer.Ordinal);
    readonly Dictionary<string, (BigEndian File, int Offset)> _ownBone = new(StringComparer.Ordinal), _ownPattern = new(StringComparer.Ordinal), _ownSrt = new(StringComparer.Ordinal);
    readonly AnimationLibrary? _shared;

    public AnimationSet(BresFile own, AnimationLibrary? shared = null)
    {
        _shared = shared;
        foreach (var (group, entries) in own.Groups)
        {
            if (group.StartsWith("AnmChr", StringComparison.Ordinal)) foreach (var (n, o) in entries) _ownBone[n] = (own.Data, o);
            else if (group.StartsWith("AnmTexSrt", StringComparison.Ordinal)) foreach (var (n, o) in entries) _ownSrt[n] = (own.Data, o);
            else if (group.StartsWith("AnmTexPat", StringComparison.Ordinal)) foreach (var (n, o) in entries) _ownPattern[n] = (own.Data, o);
        }
    }

    public IEnumerable<string> ClipNames => _ownBone.Keys.Concat(_shared?.ClipNames ?? []);
    public IEnumerable<string> PatternNames => _ownPattern.Keys.Concat(_shared?.PatternNames ?? []);

    public ChrClip? Clip(string name)
    {
        if (_bone.TryGetValue(name, out var c)) return c;
        if (_ownBone.TryGetValue(name, out var own)) return _bone[name] = ChrClip.Read(own.File, name, own.Offset);
        return _shared is not null && _shared.ClipNames.Contains(name) ? _shared.Get(name) : null;
    }

    public IEnumerable<string> SrtNames => _ownSrt.Keys.Concat(_shared?.SrtNames ?? []);

    public SrtClip? Srt(string name)
    {
        if (_srt.TryGetValue(name, out var s)) return s;
        if (_ownSrt.TryGetValue(name, out var own)) return _srt[name] = SrtClip.Read(own.File, name, own.Offset);
        return _shared is not null && _shared.SrtNames.Contains(name) ? _shared.GetSrt(name) : null;
    }

    public PatClip? Pattern(string name)
    {
        if (_patterns.TryGetValue(name, out var p)) return p;
        if (_ownPattern.TryGetValue(name, out var own)) return _patterns[name] = PatClip.Read(own.File, name, own.Offset);
        return _shared is not null && _shared.PatternNames.Contains(name) ? _shared.GetPattern(name) : null;
    }
}
