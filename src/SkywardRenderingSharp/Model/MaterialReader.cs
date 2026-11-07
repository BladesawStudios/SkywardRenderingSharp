using System.Numerics;
using SkywardRenderingSharp.Brres;
using SkywardRenderingSharp.Gx;

namespace SkywardRenderingSharp.Model;

/// <summary>Reads an MDL0 material, its TEV record and its layers into a <see cref="Material"/>.</summary>
/// <remarks>The pixel-state and texgen command streams follow the material's layer data; their sizes are fixed by the format.</remarks>
static class MaterialReader
{
    const int PixelStreamLength = 0xE0, ChannelBlock = 0x3F0, SrtBlock = 0x1B0, EffectBlock = 0x250;

    public static Material Read(BigEndian f, string name, int m)
    {
        int numTexGens = f.U8(m + 0x14), numStages = f.U8(m + 0x16);
        var state = new GxState();
        int tev = m + f.I32(m + 0x28);
        state.Run(f, tev + 0x20, tev + f.I32(tev));
        int pixel = m + f.I32(m + 0x3C);
        state.Run(f, pixel, pixel + PixelStreamLength + 0xA0);
        return new Material(name, f.I32(m + 0x10) != 0, f.I32(m + 0x18), ReadLayers(f, m),
            Enumerable.Range(0, numTexGens).Select(i => TexGenInfo.Read(state, i)).ToList(), ReadChannels(f, m),
            Enumerable.Range(0, numStages).Select(i => TevStage.Read(state, i)).ToList(), TevColors.Read(state), PixelState.Read(state), ReadTextureMatrices(f, m),
            Enumerable.Range(0, numTexGens).Select(i => PostMatrix(state, TexGenInfo.Read(state, i))).ToList());
    }

    // The dual-texgen (post) matrix a texgen selects, from the XF memory the material's stream loaded at 0x500: three rows of four floats starting at row `DualMatrix` (null when never loaded).
    static Matrix4x4? PostMatrix(GxState state, TexGenInfo gen)
    {
        int at = 0x500 + 4 * gen.DualMatrix;
        if (!state.Xf.ContainsKey(at)) return null;
        float F(int i) => BitConverter.UInt32BitsToSingle(state.Xfr(at + i));
        return new Matrix4x4(F(0), F(1), F(2), F(3), F(4), F(5), F(6), F(7), F(8), F(9), F(10), F(11), 0, 0, 0, 1);
    }

    // Eight SRT records (scale xy, rotation, translation xy: five floats) and eight effect records (camera, light, map mode, flag, then a 3x4 matrix).
    static List<TextureMatrix> ReadTextureMatrices(BigEndian f, int m) =>
        Enumerable.Range(0, 8).Select(i =>
        {
            int srt = m + SrtBlock + 0x14 * i, effect = m + EffectBlock + 0x34 * i;
            return new TextureMatrix(new Vector2(f.F32(srt), f.F32(srt + 4)), f.F32(srt + 8), new Vector2(f.F32(srt + 12), f.F32(srt + 16)),
                f.U8(effect + 2), (sbyte)f.U8(effect), (sbyte)f.U8(effect + 1), f.Matrix3x4(effect + 4));
        }).ToList();

    static List<MaterialLayer> ReadLayers(BigEndian f, int m)
    {
        int count = f.I32(m + 0x2C), at = m + f.I32(m + 0x30);
        var layers = new List<MaterialLayer>(count);
        for (int i = 0; i < count; i++)
        {
            int l = at + 0x34 * i;
            layers.Add(new MaterialLayer(f.CString(l + f.I32(l)), f.I32(l + 0x10), f.I32(l + 0x18), f.I32(l + 0x1C), f.I32(l + 0x20), f.I32(l + 0x24)));
        }
        return layers;
    }

    static List<ColorChannel> ReadChannels(BigEndian f, int m) =>
        Enumerable.Range(0, 2).Select(i =>
        {
            int c = m + ChannelBlock + 0x14 * i;
            return new ColorChannel(f.U32(c + 4), f.U32(c + 8), ChannelControl.Read(f.U32(c + 12)), ChannelControl.Read(f.U32(c + 16)));
        }).ToList();
}
