using System.Numerics;

namespace SkywardRenderingSharp.Gx;

/// <summary>The four TEV colour registers (PREV, REG0..2: signed 11 bit) and four constant colours (K0..K3: 8 bit), as RGBA.</summary>
public sealed class TevColors
{
    public Vector4[] Registers { get; } = new Vector4[4];
    public Vector4[] Konst { get; } = new Vector4[4];
    public Vector4[] SwapTables { get; } = new Vector4[4];

    public static TevColors Read(GxState s)
    {
        var c = new TevColors();
        for (int i = 0; i < 4; i++)
        {
            c.Registers[i] = Decode(s.TevColorWrites[2 * i, 0], s.TevColorWrites[2 * i + 1, 0]) / 255f;
            c.Konst[i] = Decode(s.TevColorWrites[2 * i, 1], s.TevColorWrites[2 * i + 1, 1]) / 255f;
            c.SwapTables[i] = new Vector4(s.Bp[0xF6 + 2 * i] & 3, s.Bp[0xF6 + 2 * i] >> 2 & 3, s.Bp[0xF7 + 2 * i] & 3, s.Bp[0xF7 + 2 * i] >> 2 & 3);
        }
        return c;
    }

    // Red and alpha are in the low register, blue and green in the high one.
    static Vector4 Decode(uint lo, uint hi) => new(Signed(lo & 0x7FF), Signed(hi >> 12 & 0x7FF), Signed(hi & 0x7FF), Signed(lo >> 12 & 0x7FF));

    static int Signed(uint v) => (v & 0x400) != 0 ? (int)v - 0x800 : (int)v;
}
