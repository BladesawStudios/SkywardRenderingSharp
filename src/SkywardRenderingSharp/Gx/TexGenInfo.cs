namespace SkywardRenderingSharp.Gx;

/// <summary>One texture coordinate generator: where its input comes from and how the matrix chain treats it.</summary>
/// <remarks>SourceRow: 0 position, 1 normal, 2 colour, 3 and 4 binormals, 5..12 texture coordinates 0..7. DualMatrix is the post-transform row (normalised if Normalize).</remarks>
public readonly record struct TexGenInfo(bool Projective, bool InputIsAbc1, int Type, int SourceRow, int DualMatrix, bool Normalize)
{
    public static TexGenInfo Read(GxState s, int i)
    {
        uint a = s.Xfr(0x1040 + i), b = s.Xfr(0x1050 + i);
        return new TexGenInfo((a >> 1 & 1) != 0, (a >> 2 & 1) != 0, (int)(a >> 4 & 7), (int)(a >> 7 & 31), (int)(b & 0x3F), (b >> 8 & 1) != 0);
    }
}
