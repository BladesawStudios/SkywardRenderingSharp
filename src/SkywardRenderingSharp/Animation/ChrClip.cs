using System.Numerics;
using SkywardRenderingSharp.Brres;
using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Animation;

/// <summary>A bone animation (CHR0): per-bone scale, rotation and translation tracks sampled by frame.</summary>
/// <remarks>Node flags: bit 1 identity; bits 3, 5, 6 mean scale is one, rotation is zero, translation is zero (that group has no words); bit 4 an isotropic scale (one word).
/// A present group holds three words (x, y, z): a float when its const bit is set (bits 13-15 scale, 16-18 rotation, 19-21 translation),
/// else an offset to a keyframe track whose format is chosen by bits 25/26 (scale), 27/28 (rotation) and 30/31 (translation).</remarks>
public sealed class ChrClip
{
    readonly Dictionary<string, KeyTrack?[]> _nodes = new(StringComparer.Ordinal);

    public string Name { get; }
    public int FrameCount { get; }
    public bool Loops { get; }

    // Tracks whose data could not be read with the assumed layout; they animate as their bind value.
    public int UnreadableTracks { get; private set; }
    public List<string> UnreadableDetails { get; } = [];

    ChrClip(string name, int frames, bool loops) { Name = name; FrameCount = frames; Loops = loops; }

    public static ChrClip Read(BigEndian f, string name, int chr0)
    {
        int frames = f.U16(chr0 + 0x20);
        var clip = new ChrClip(name, frames, f.U32(chr0 + 0x24) != 0);
        foreach (var (node, entry) in ResDict.Read(f, chr0 + 0x2C)) clip._nodes[node] = clip.ReadNode(f, entry, frames);
        return clip;
    }

    // Nine slots: scale xyz, rotation xyz, translation xyz. A null slot means the group is absent (identity value).
    KeyTrack?[] ReadNode(BigEndian f, int entry, int frames)
    {
        uint flags = f.U32(entry + 4);
        var slots = new KeyTrack?[9];
        if ((flags & 2) != 0) return slots;
        int word = entry + 8;
        bool[] present = [(flags >> 3 & 1) == 0, (flags >> 5 & 1) == 0, (flags >> 6 & 1) == 0];
        int[] constBit = [13, 16, 19], formatBit = [25, 27, 30];
        for (int g = 0; g < 3; g++)
        {
            if (!present[g]) continue;
            int format = FormatOf(flags, g);
            // An isotropic scale (bit 4) is one word shared by x, y and z.
            int words = g == 0 && (flags >> 4 & 1) != 0 ? 1 : 3;
            for (int c = 0; c < words; c++, word += 4)
            {
                KeyTrack? track;
                if ((flags >> (constBit[g] + c) & 1) != 0) track = KeyTrack.Constant(f.F32(word));
                else if ((track = KeyTrack.Read(f, entry + (int)f.U32(word), format, frames)) is null) { UnreadableTracks++; UnreadableDetails.Add($"group{g} fmt{format} flags{flags:x8}"); }
                if (words == 1) slots[0] = slots[1] = slots[2] = track; else slots[g * 3 + c] = track;
            }
        }
        return slots;
    }

    /// <summary>Every animated (non-constant) track, for checking the key layouts against the data.</summary>
    public IEnumerable<KeyTrack> KeyedTracks => _nodes.Values.SelectMany(t => t).Where(t => t is { Format: >= 0 })!;

    // Scale uses bits 25/26, rotation 27/28/29, translation 30/31: the first bit of a pair is 32-bit keys, the second 48-bit, both float keys. Rotation's bit 29
    // marks a linear track (28 + 29: floats, 29 alone: bytes); no bit at all is also a linear byte track.
    static int FormatOf(uint flags, int group)
    {
        int low = group == 0 ? 25 : group == 1 ? 27 : 30;
        bool a = (flags >> low & 1) != 0, b = (flags >> (low + 1) & 1) != 0;
        if (group == 1 && (flags >> 29 & 1) != 0) return b ? 4 : 0;
        return a && b ? 3 : a ? 1 : b ? 2 : 0;
    }

    public bool HasNode(string bone) => _nodes.ContainsKey(bone);

    /// <summary>The bone's local matrix at a frame; bones the clip does not animate keep their bind transform.</summary>
    public Matrix4x4 Local(Bone bone, float frame)
    {
        if (!_nodes.TryGetValue(bone.Name, out var t)) return bone.LocalMatrix;
        if (Loops && FrameCount > 0) frame %= FrameCount;
        float V(int i, float fallback) => t[i]?.Sample(frame) ?? fallback;
        return Bone.LocalTransform(new Vector3(V(0, 1), V(1, 1), V(2, 1)), new Vector3(V(3, 0), V(4, 0), V(5, 0)), new Vector3(V(6, 0), V(7, 0), V(8, 0)));
    }

    public Pose Evaluate(IReadOnlyList<Bone> bones, float frame) => Pose.FromLocals(bones, b => Local(b, frame));
}
