namespace SkywardRenderingSharp.Gx;

/// <summary>Alpha test, blending, depth and cull state a material asks for.</summary>
/// <remarks>Alpha compare functions: 0 never, 1 less, 2 equal, 3 lequal, 4 greater, 5 nequal, 6 gequal, 7 always; logic: 0 and, 1 or, 2 xor, 3 xnor.</remarks>
public readonly record struct PixelState(int AlphaRef0, int AlphaRef1, int AlphaComp0, int AlphaComp1, int AlphaLogic,
    bool BlendEnabled, int BlendSrc, int BlendDst, bool BlendSubtract, bool ColorWrite, bool AlphaWrite,
    bool DepthTest, int DepthFunction, bool DepthWrite)
{
    public static PixelState Read(GxState s)
    {
        uint a = s.Bp[0xF3], blend = s.Bp[0x41], z = s.Bp[0x40];
        return new PixelState((int)(a & 255), (int)(a >> 8 & 255), (int)(a >> 16 & 7), (int)(a >> 19 & 7), (int)(a >> 22 & 3),
            (blend & 1) != 0, (int)(blend >> 8 & 7), (int)(blend >> 5 & 7), (blend >> 11 & 1) != 0, (blend >> 3 & 1) != 0, (blend >> 4 & 1) != 0,
            (z & 1) != 0, (int)(z >> 1 & 7), (z >> 4 & 1) != 0);
    }
}
