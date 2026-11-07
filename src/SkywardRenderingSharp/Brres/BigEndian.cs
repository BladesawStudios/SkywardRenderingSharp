using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SkywardRenderingSharp.Brres;

/// <summary>Big-endian reads over a BRRES buffer, with the offsets the format uses everywhere.</summary>
public readonly struct BigEndian(ReadOnlyMemory<byte> data)
{
    public ReadOnlySpan<byte> Span => data.Span;
    public int Length => data.Length;

    public int I32(int offset) => BinaryPrimitives.ReadInt32BigEndian(data.Span[offset..]);
    public uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Span[offset..]);
    public int U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Span[offset..]);
    public int S16(int offset) => BinaryPrimitives.ReadInt16BigEndian(data.Span[offset..]);
    public byte U8(int offset) => data.Span[offset];
    public float F32(int offset) => BinaryPrimitives.ReadSingleBigEndian(data.Span[offset..]);

    public Vector3 Vec3(int offset) => new(F32(offset), F32(offset + 4), F32(offset + 8));

    public string CString(int offset)
    {
        var span = data.Span[offset..];
        return Encoding.Latin1.GetString(span[..span.IndexOf((byte)0)]);
    }

    // A 3x4 row-major matrix (the form GX and BRRES store) as a System.Numerics matrix for row vectors.
    public Matrix4x4 Matrix3x4(int offset)
    {
        var f = new float[12];
        for (int i = 0; i < 12; i++) f[i] = F32(offset + i * 4);
        return new Matrix4x4(f[0], f[4], f[8], 0, f[1], f[5], f[9], 0, f[2], f[6], f[10], 0, f[3], f[7], f[11], 1);
    }
}
