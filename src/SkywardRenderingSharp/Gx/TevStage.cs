namespace SkywardRenderingSharp.Gx;

/// <summary>One TEV combiner: out = (d + lerp(a, b, c) (+/-) bias) * scale, with its texture, rasterised colour and constant selections.</summary>
/// <remarks>Input codes are the GX ones: colour inputs 0..15 (CPREV, APREV, C0, A0, C1, A1, C2, A2, TEXC, TEXA, RASC, RASA, ONE, HALF, KONST, ZERO), alpha inputs 0..7.</remarks>
public sealed record TevStage(TevCombine Color, TevCombine Alpha, int TexMap, int TexCoord, bool TexEnabled, int RasChannel,
    int RasSwap, int TexSwap, int KonstColor, int KonstAlpha)
{
    public static TevStage Read(GxState s, int i)
    {
        uint color = s.Bp[0xC0 + 2 * i], alpha = s.Bp[0xC1 + 2 * i];
        uint tref = s.Bp[0x28 + i / 2] >> (i % 2 * 12), ksel = s.Bp[0xF6 + i / 2];
        int konstShift = i % 2 == 0 ? 4 : 14;
        return new TevStage(
            TevCombine.ReadColor(color), TevCombine.ReadAlpha(alpha),
            (int)(tref & 7), (int)(tref >> 3 & 7), (tref >> 6 & 1) != 0, (int)(tref >> 7 & 7),
            (int)(alpha & 3), (int)(alpha >> 2 & 3),
            (int)(ksel >> konstShift & 31), (int)(ksel >> (konstShift + 5) & 31));
    }
}

/// <summary>The a, b, c, d inputs and output controls of one combiner equation.</summary>
public readonly record struct TevCombine(int A, int B, int C, int D, int Bias, bool Subtract, bool Clamp, int Scale, int Dest)
{
    public static TevCombine ReadColor(uint r) =>
        new((int)(r >> 12 & 15), (int)(r >> 8 & 15), (int)(r >> 4 & 15), (int)(r & 15), Common(r).Bias, Common(r).Sub, Common(r).Clamp, Common(r).Scale, Common(r).Dest);

    public static TevCombine ReadAlpha(uint r) =>
        new((int)(r >> 13 & 7), (int)(r >> 10 & 7), (int)(r >> 7 & 7), (int)(r >> 4 & 7), Common(r).Bias, Common(r).Sub, Common(r).Clamp, Common(r).Scale, Common(r).Dest);

    // Bias, operation, clamp, scale and destination sit at the same bits in both registers.
    static (int Bias, bool Sub, bool Clamp, int Scale, int Dest) Common(uint r) =>
        ((int)(r >> 16 & 3), (r >> 18 & 1) != 0, (r >> 19 & 1) != 0, (int)(r >> 20 & 3), (int)(r >> 22 & 3));
}
