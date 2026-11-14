using System.Numerics;
using SkywardRenderingSharp.Gx;
using Res = BresSharp.Mdl0;

namespace SkywardRenderingSharp.Model;

/// <summary>Turns an MDL0 material and its TEV shader into a <see cref="Material"/>: the GX state their command streams write, and the layers, light channels and texture matrices.</summary>
/// <remarks>The shader's code sets up the TEV stages; the material's display list then sets the pixel state, TEV colours and constants, and the texgen and dual-texgen matrices.</remarks>
static class MaterialReader
{
    public static Material Read(Res.Mdl0Material m)
    {
        var state = new GxState();
        if (m.Shader is { } shader) state.Run(shader.Code);
        state.Run(m.DisplayList);
        var texGens = Enumerable.Range(0, m.TexGenCount).Select(i => TexGenInfo.Read(state, i)).ToList();
        return new Material(m.Name, m.IsTranslucent, (int)m.Cull, Layers(m), texGens, Channels(m),
            Enumerable.Range(0, m.TevStageCount).Select(i => TevStage.Read(state, i)).ToList(), TevColors.Read(state), PixelState.Read(state), TextureMatrices(m),
            texGens.Select(gen => PostMatrix(state, gen)).ToList());
    }

    // The dual-texgen (post) matrix a texgen selects, from the XF memory the material's stream loaded at 0x500: three rows of four floats starting at row `DualMatrix` (null when never loaded).
    static Matrix4x4? PostMatrix(GxState state, TexGenInfo gen)
    {
        int at = 0x500 + 4 * gen.DualMatrix;
        if (!state.Xf.ContainsKey(at)) return null;
        float F(int i) => BitConverter.UInt32BitsToSingle(state.Xfr(at + i));
        return new Matrix4x4(F(0), F(1), F(2), F(3), F(4), F(5), F(6), F(7), F(8), F(9), F(10), F(11), 0, 0, 0, 1);
    }

    static List<TextureMatrix> TextureMatrices(Res.Mdl0Material m) =>
        Enumerable.Range(0, 8).Select(i =>
        {
            var srt = m.TextureSrt(i);
            var effect = m.TextureEffect(i);
            return new TextureMatrix(srt.Scale, srt.Rotation, srt.Translation, effect.MapMode, effect.Camera, effect.Light, effect.Matrix);
        }).ToList();

    static List<MaterialLayer> Layers(Res.Mdl0Material m) =>
        m.Textures.Select(t => new MaterialLayer(t.Texture ?? "", t.TextureId, (int)t.WrapU, (int)t.WrapV, (int)t.MinFilter, (int)t.MagFilter)).ToList();

    static List<ColorChannel> Channels(Res.Mdl0Material m) =>
        Enumerable.Range(0, 2).Select(i => new ColorChannel(Rgba(m.MaterialColor(i)), Rgba(m.AmbientColor(i)),
            ChannelControl.Read(m.ColorControl(i)), ChannelControl.Read(m.AlphaControl(i)))).ToList();

    static uint Rgba((byte R, byte G, byte B, byte A) c) => (uint)(c.R << 24 | c.G << 16 | c.B << 8 | c.A);
}
