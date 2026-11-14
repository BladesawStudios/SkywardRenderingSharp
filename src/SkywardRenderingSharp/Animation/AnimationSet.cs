using BresSharp;

namespace SkywardRenderingSharp.Animation;

/// <summary>The animations a model can play: the clips stored in its own BRRES, plus a shared library when the skeleton matches one.</summary>
/// <remarks>Names are the clip names as the game stores them. Looking a clip up reads it on first use.</remarks>
public sealed class AnimationSet
{
    readonly Dictionary<string, ChrClip> _bone = new(StringComparer.Ordinal);
    readonly Dictionary<string, PatClip> _patterns = new(StringComparer.Ordinal);
    readonly Dictionary<string, SrtClip> _srt = new(StringComparer.Ordinal);
    readonly Dictionary<string, BresSubfile> _ownBone, _ownPattern, _ownSrt;
    readonly AnimationLibrary? _shared;

    public AnimationSet(BresFile own, AnimationLibrary? shared = null)
    {
        _shared = shared;
        _ownBone = Files(own, "AnmChr(NW4R)");
        _ownSrt = Files(own, "AnmTexSrt(NW4R)");
        _ownPattern = Files(own, "AnmTexPat(NW4R)");
    }

    static Dictionary<string, BresSubfile> Files(BresFile bres, string folder) => (bres[folder]?.Files ?? []).ToDictionary(f => f.Name, StringComparer.Ordinal);

    public IEnumerable<string> ClipNames => _ownBone.Keys.Concat(_shared?.ClipNames ?? []);
    public IEnumerable<string> PatternNames => _ownPattern.Keys.Concat(_shared?.PatternNames ?? []);
    public IEnumerable<string> SrtNames => _ownSrt.Keys.Concat(_shared?.SrtNames ?? []);

    public ChrClip? Clip(string name)
    {
        if (_bone.TryGetValue(name, out var c)) return c;
        if (_ownBone.TryGetValue(name, out var own)) return _bone[name] = ChrClip.Read(own);
        return _shared is not null && _shared.ClipNames.Contains(name) ? _shared.Get(name) : null;
    }

    public SrtClip? Srt(string name)
    {
        if (_srt.TryGetValue(name, out var s)) return s;
        if (_ownSrt.TryGetValue(name, out var own)) return _srt[name] = SrtClip.Read(own);
        return _shared is not null && _shared.SrtNames.Contains(name) ? _shared.GetSrt(name) : null;
    }

    public PatClip? Pattern(string name)
    {
        if (_patterns.TryGetValue(name, out var p)) return p;
        if (_ownPattern.TryGetValue(name, out var own)) return _patterns[name] = PatClip.Read(own);
        return _shared is not null && _shared.PatternNames.Contains(name) ? _shared.GetPattern(name) : null;
    }
}
