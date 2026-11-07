using SkywardRenderingSharp.Brres;

namespace SkywardRenderingSharp.Animation;

/// <summary>One animated value over time: a constant, or Hermite-interpolated keyframes.</summary>
public sealed class KeyTrack
{
    readonly float[] _frames, _values, _tangents;

    KeyTrack(float[] frames, float[] values, float[] tangents, int format = -1) { _frames = frames; _values = values; _tangents = tangents; Format = format; }

    /// <summary>The key format this track was read from (0 linear, 1 32-bit, 2 48-bit, 3 float), or -1 for a constant.</summary>
    public int Format { get; }
    public int KeyCount => _frames.Length;
    public float FirstFrame => _frames[0];
    public float LastFrame => _frames[^1];
    public float FrameScale { get; private set; }
    public bool FramesIncrease { get { for (int i = 1; i < _frames.Length; i++) if (_frames[i] < _frames[i - 1]) return false; return true; } }
    public bool ValuesFinite => _values.All(float.IsFinite) && _tangents.All(float.IsFinite);

    public static KeyTrack Constant(float value) => new([0f], [value], [0f]);

    /// <summary>Reads a track. Formats: 0 linear bytes, 1 32-bit keys, 2 48-bit keys, 3 float keys, 4 linear floats.</summary>
    /// <remarks>Each layout was checked against every track of the game's 365 Link clips (frames never decrease, last keys land on the clip's end). Keyed tracks start with a
    /// header: count u16, pad, frameScale f32 (and, for 32 and 48-bit keys, step f32 and base f32). 32-bit keys: frame u8, value 12 bits, tangent 12 bits (signed, /32).
    /// 48-bit keys: frame s16 /32 (a first key may sit at frame -1), value u16, tangent s16 /256. Float keys follow an 8 byte header: frame, value, tangent. Linear bytes are step, base,
    /// then one byte per frame; linear floats are one float per frame with no header.</remarks>
    public static KeyTrack? Read(BigEndian f, int at, int format, int clipFrames)
    {
        if (format == 0) return ReadLinear(f, at, clipFrames);
        if (format == 4) return ReadLinearFloats(f, at, clipFrames);
        int count = at >= 0 && at + 8 <= f.Length ? f.U16(at) : 0;
        int keyBytes = format == 1 ? 4 : format == 2 ? 6 : 12;
        // A track that does not fit its file means the layout assumed for its flags is wrong; the caller keeps the bind value instead.
        int header = format == 3 ? 8 : 16;
        if (count == 0 || at + header + count * keyBytes > f.Length) return null;
        var frames = new float[count]; var values = new float[count]; var tangents = new float[count];
        float scale = f.F32(at + 4);
        var lastScale = scale;
        for (int i = 0; i < count; i++)
        {
            if (format == 1) Read32(f, at + 16 + 4 * i, f.F32(at + 8), f.F32(at + 12), out frames[i], out values[i], out tangents[i]);
            else if (format == 2) Read48(f, at + 16 + 6 * i, scale, clipFrames, f.F32(at + 8), f.F32(at + 12), out frames[i], out values[i], out tangents[i]);
            else { frames[i] = f.F32(at + 8 + 12 * i); values[i] = f.F32(at + 12 + 12 * i); tangents[i] = f.F32(at + 16 + 12 * i); }
        }
        return new KeyTrack(frames, values, tangents, format);
    }

    static KeyTrack? ReadLinearFloats(BigEndian f, int at, int clipFrames)
    {
        int count = clipFrames + 1;
        if (at < 0 || at + 4 * count > f.Length) return null;
        var frames = new float[count]; var values = new float[count]; var tangents = new float[count];
        for (int i = 0; i < count; i++) { frames[i] = i; values[i] = f.F32(at + 4 * i); }
        for (int i = 0; i < count; i++) tangents[i] = i + 1 < count ? values[i + 1] - values[i] : i > 0 ? values[i] - values[i - 1] : 0f;
        return new KeyTrack(frames, values, tangents, 4);
    }

    // A linear track is a step and a base (floats) followed by one byte per frame, frames + 1 of them: value = base + step * byte.
    static KeyTrack? ReadLinear(BigEndian f, int at, int clipFrames)
    {
        int count = clipFrames + 1;
        if (at < 0 || at + 8 + count > f.Length) return null;
        float step = f.F32(at), baseValue = f.F32(at + 4);
        var frames = new float[count]; var values = new float[count]; var tangents = new float[count];
        for (int i = 0; i < count; i++) { frames[i] = i; values[i] = baseValue + step * f.U8(at + 8 + i); }
        for (int i = 0; i < count; i++) tangents[i] = i + 1 < count ? values[i + 1] - values[i] : i > 0 ? values[i] - values[i - 1] : 0f;
        return new KeyTrack(frames, values, tangents, 0);
    }

    static void Read32(BigEndian f, int at, float step, float baseValue, out float frame, out float value, out float tangent)
    {
        uint raw = f.U32(at);
        frame = raw >> 24;
        value = baseValue + step * (raw >> 12 & 0xFFF);
        tangent = ((int)(raw & 0xFFF) << 20 >> 20) / 32f;
    }

    static void Read48(BigEndian f, int at, float frameScale, int clipFrames, float step, float baseValue, out float frame, out float value, out float tangent)
    {
        frame = f.S16(at) / 32f;
        value = baseValue + step * f.U16(at + 2);
        tangent = f.S16(at + 4) / 256f;
    }

    public float Sample(float frame)
    {
        int n = _frames.Length;
        if (n == 1 || frame <= _frames[0]) return _values[0];
        if (frame >= _frames[n - 1]) return _values[n - 1];
        int i = 1;
        while (_frames[i] < frame) i++;
        float f0 = _frames[i - 1], f1 = _frames[i], dt = f1 - f0;
        if (dt <= 0) return _values[i];
        float t = (frame - f0) / dt, t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * _values[i - 1] + (t3 - 2 * t2 + t) * dt * _tangents[i - 1] + (-2 * t3 + 3 * t2) * _values[i] + (t3 - t2) * dt * _tangents[i];
    }
}
