using System.Numerics;
using SkywardRenderingSharp.Brres;

namespace SkywardRenderingSharp.Animation;

/// <summary>One texture slot's animated transform: scale, rotation (degrees) and translation in texture space.</summary>
public readonly record struct TextureSrt(Vector2 Scale, float Rotation, Vector2 Translation);

/// <summary>A texture SRT animation (SRT0): per material and texture slot, a scale, rotation and translation over time. This is how the eyes look around.</summary>
/// <remarks>A material record is [name][texture mask][indirect mask] then an offset per enabled texture to its entry. An entry is flags (bit 1 scale is one, bit 2 rotation is zero,
/// bit 3 translation is zero, bit 4 uniform scale, bits 5-6 scale u/v const, bit 7 rotation const, bits 8-9 translation u/v const) then one word per present component: a float if
/// const, else an offset, relative to the word, to a track of (frame, value, tangent) floats after an 8 byte header.</remarks>
public sealed class SrtClip
{
    sealed record Entry(KeyTrack? ScaleU, KeyTrack? ScaleV, KeyTrack? Rotation, KeyTrack? TranslationU, KeyTrack? TranslationV);

    readonly Dictionary<string, Dictionary<int, Entry>> _materials = new(StringComparer.Ordinal);

    public string Name { get; }
    public int FrameCount { get; }

    SrtClip(string name, int frames) { Name = name; FrameCount = frames; }

    public static SrtClip Read(BigEndian f, string name, int srt0)
    {
        var clip = new SrtClip(name, f.U16(srt0 + 0x20));
        foreach (var (material, record) in ResDict.Read(f, srt0 + (int)f.U32(srt0 + 0x10))) clip._materials[material] = ReadMaterial(f, record, clip.FrameCount);
        return clip;
    }

    static Dictionary<int, Entry> ReadMaterial(BigEndian f, int record, int frames)
    {
        var result = new Dictionary<int, Entry>();
        uint mask = f.U32(record + 4);
        int offset = record + 12;
        for (int texture = 0; texture < 8; texture++)
        {
            if ((mask >> texture & 1) == 0) continue;
            result[texture] = ReadEntry(f, record + (int)f.U32(offset), frames);
            offset += 4;
        }
        return result;
    }

    static Entry ReadEntry(BigEndian f, int entry, int frames)
    {
        uint flags = f.U32(entry);
        int word = entry + 4;
        KeyTrack? Next(bool isConst)
        {
            var track = isConst ? KeyTrack.Constant(f.F32(word)) : KeyTrack.Read(f, word + (int)f.U32(word), 3, frames);
            word += 4;
            return track;
        }
        KeyTrack? su = null, sv = null, rot = null, tu = null, tv = null;
        if ((flags & 2) == 0)
        {
            su = Next((flags & 0x20) != 0);
            sv = (flags & 0x10) != 0 ? su : Next((flags & 0x40) != 0);
        }
        if ((flags & 4) == 0) rot = Next((flags & 0x80) != 0);
        if ((flags & 8) == 0) { tu = Next((flags & 0x100) != 0); tv = Next((flags & 0x200) != 0); }
        return new Entry(su, sv, rot, tu, tv);
    }

    /// <summary>For each animated material, the transform of each animated texture slot at the frame.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, TextureSrt>> Evaluate(float frame)
    {
        if (FrameCount > 0) frame %= FrameCount;
        return _materials.ToDictionary(m => m.Key, m => (IReadOnlyDictionary<int, TextureSrt>)m.Value.ToDictionary(t => t.Key, t => new TextureSrt(
            new Vector2(t.Value.ScaleU?.Sample(frame) ?? 1, t.Value.ScaleV?.Sample(frame) ?? 1), t.Value.Rotation?.Sample(frame) ?? 0,
            new Vector2(t.Value.TranslationU?.Sample(frame) ?? 0, t.Value.TranslationV?.Sample(frame) ?? 0))));
    }
}
