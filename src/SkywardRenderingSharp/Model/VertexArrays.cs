using System.Numerics;
using SkywardRenderingSharp.Brres;

namespace SkywardRenderingSharp.Model;

/// <summary>The position, normal, colour and texture coordinate arrays a model's polygons index into.</summary>
/// <remarks>Each array is decoded to floats once. Index spaces are the dictionary order of each section.</remarks>
sealed class VertexArrays
{
    public List<Vector3[]> Positions { get; } = [];
    public List<Vector3[]> Normals { get; } = [];
    public List<Vector4[]> Colors { get; } = [];
    public List<Vector2[]> Uvs { get; } = [];

    public static VertexArrays Read(BigEndian f, int mdl0, IReadOnlyList<int> sections)
    {
        var a = new VertexArrays();
        foreach (var (_, at) in Entries(f, mdl0, sections, 2)) a.Positions.Add(ReadFloats(f, at, Math.Max(2, f.I32(at + 0x14) + 2), true).Select(v => new Vector3(v[0], v[1], v.Length > 2 ? v[2] : 0)).ToArray());
        foreach (var (_, at) in Entries(f, mdl0, sections, 3)) a.Normals.Add(ReadFloats(f, at, 3, false).Select(v => new Vector3(v[0], v[1], v[2])).ToArray());
        foreach (var (_, at) in Entries(f, mdl0, sections, 4)) a.Colors.Add(ReadColors(f, at));
        foreach (var (_, at) in Entries(f, mdl0, sections, 5)) a.Uvs.Add(ReadFloats(f, at, 2, true).Select(v => new Vector2(v[0], v[1])).ToArray());
        return a;
    }

    static IEnumerable<(string, int)> Entries(BigEndian f, int mdl0, IReadOnlyList<int> sections, int section) =>
        sections[section] == 0 ? [] : ResDict.Read(f, mdl0 + sections[section]);

    // Components are read as integers of the stated format and divided by 2^divisor; normals carry their own fixed divisors.
    static float[][] ReadFloats(BigEndian f, int at, int components, bool hasDivisor)
    {
        int data = at + f.I32(at + 8), format = f.I32(at + 0x18), count = f.U16(at + 0x1E), stride = f.U8(at + 0x1D);
        float scale = hasDivisor ? 1f / (1 << f.U8(at + 0x1C)) : NormalScale(format);
        var result = new float[count][];
        for (int i = 0; i < count; i++)
        {
            result[i] = new float[components];
            for (int c = 0; c < components; c++) result[i][c] = Component(f, data + i * stride, c, format) * (format == 4 ? 1f : scale);
        }
        return result;
    }

    static float NormalScale(int format) => format == 1 ? 1f / 64 : format == 3 ? 1f / 16384 : 1f;

    static float Component(BigEndian f, int at, int c, int format) => format switch
    {
        0 => f.U8(at + c), 1 => (sbyte)f.U8(at + c), 2 => f.U16(at + c * 2), 3 => f.S16(at + c * 2), _ => f.F32(at + c * 4),
    };

    static Vector4[] ReadColors(BigEndian f, int at)
    {
        int data = at + f.I32(at + 8), format = f.I32(at + 0x18), count = f.U16(at + 0x1E), stride = f.U8(at + 0x1C);
        var colors = new Vector4[count];
        for (int i = 0; i < count; i++) colors[i] = Color(f, data + i * stride, format);
        return colors;
    }

    static Vector4 Color(BigEndian f, int p, int format)
    {
        switch (format)
        {
            case 0: { int v = f.U16(p); return new Vector4((v >> 11) / 31f, (v >> 5 & 63) / 63f, (v & 31) / 31f, 1); }
            case 1: return new Vector4(f.U8(p), f.U8(p + 1), f.U8(p + 2), 255) / 255f;
            case 2: return new Vector4(f.U8(p), f.U8(p + 1), f.U8(p + 2), 255) / 255f;
            case 3: { int v = f.U16(p); return new Vector4((v >> 12) / 15f, (v >> 8 & 15) / 15f, (v >> 4 & 15) / 15f, (v & 15) / 15f); }
            case 4: { int v = f.U8(p) << 16 | f.U8(p + 1) << 8 | f.U8(p + 2); return new Vector4((v >> 18) / 63f, (v >> 12 & 63) / 63f, (v >> 6 & 63) / 63f, (v & 63) / 63f); }
            default: return new Vector4(f.U8(p), f.U8(p + 1), f.U8(p + 2), f.U8(p + 3)) / 255f;
        }
    }
}
