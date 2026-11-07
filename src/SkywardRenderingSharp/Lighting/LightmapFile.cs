using System.Buffers.Binary;
using SkywardRenderingSharp.Textures;

namespace SkywardRenderingSharp.Lighting;

/// <summary>The game's .blmap: the lookup curves the engine turns into the LmCha*, LmBG* and other lightmap textures.</summary>
/// <remarks>Each LTEX entry holds 128 pairs of floats after its name; the first of each pair is the curve value. In the shared data the character
/// curves are flat apart from a few entries, which is why the lightmaps add no visible shading by default. Which input indexes a curve
/// (the engine's texture matrices) is not recovered, so the renderer does not apply them.</remarks>
public sealed class LightmapFile
{
    const int CurveBytes = 0x400, NameOffset = 0x14, NameLength = 0x1C;

    readonly Dictionary<string, float[]> _curves = new(StringComparer.Ordinal);

    public IEnumerable<string> Names => _curves.Keys;

    public static LightmapFile Open(ReadOnlySpan<byte> file)
    {
        if (!file[..4].SequenceEqual("LMAP"u8)) throw new InvalidDataException("Not a .blmap file.");
        var map = new LightmapFile();
        for (int p = 0x20; p + 8 <= file.Length; )
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file[(p + 4)..]);
            var entry = file.Slice(p, length);
            var name = System.Text.Encoding.Latin1.GetString(entry.Slice(NameOffset, NameLength)).TrimEnd('\0');
            var curve = new float[128];
            for (int i = 0; i < 128; i++) curve[i] = BinaryPrimitives.ReadSingleBigEndian(entry[(length - CurveBytes + 8 * i)..]);
            map._curves[name] = curve;
            p += length;
        }
        return map;
    }

    public float[] Curve(string name) => _curves[name];

    /// <summary>The curve as a 128x1 greyscale image.</summary>
    public RgbaImage Image(string name)
    {
        var curve = _curves[name];
        var pixels = new byte[128 * 4];
        for (int i = 0; i < 128; i++) pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = pixels[i * 4 + 3] = (byte)Math.Clamp(curve[i] * 255f, 0f, 255f);
        return new RgbaImage(128, 1, pixels);
    }
}
