using Silk.NET.OpenGL;
using SkywardRenderingSharp.Gx;

namespace SkywardRenderingSharp.Rendering;

/// <summary>Applies a material's GX blend, depth and cull state to the GL context.</summary>
/// <remarks>GX cull modes: 0 none, 1 front, 2 back, 3 all. Front faces are clockwise in GL terms, which is how GX counts them.</remarks>
sealed class PixelStateApplier(GL gl)
{
    public void Apply(PixelState s, int cullMode)
    {
        Toggle(EnableCap.CullFace, cullMode != 0);
        if (cullMode != 0) gl.CullFace(cullMode == 1 ? TriangleFace.Front : cullMode == 2 ? TriangleFace.Back : TriangleFace.FrontAndBack);

        Toggle(EnableCap.Blend, s.BlendEnabled);
        if (s.BlendEnabled) gl.BlendFunc(SourceFactor(s.BlendSrc), DestinationFactor(s.BlendDst));
        gl.ColorMask(s.ColorWrite, s.ColorWrite, s.ColorWrite, s.AlphaWrite);

        Toggle(EnableCap.DepthTest, s.DepthTest);
        if (s.DepthTest) gl.DepthFunc(DepthFunction(s.DepthFunction));
        gl.DepthMask(s.DepthWrite);
    }

    void Toggle(EnableCap cap, bool on) { if (on) gl.Enable(cap); else gl.Disable(cap); }

    static BlendingFactor SourceFactor(int f) => f switch
    {
        0 => BlendingFactor.Zero, 1 => BlendingFactor.One, 2 => BlendingFactor.DstColor, 3 => BlendingFactor.OneMinusDstColor,
        4 => BlendingFactor.SrcAlpha, 5 => BlendingFactor.OneMinusSrcAlpha, 6 => BlendingFactor.DstAlpha, _ => BlendingFactor.OneMinusDstAlpha,
    };

    static BlendingFactor DestinationFactor(int f) => f switch
    {
        0 => BlendingFactor.Zero, 1 => BlendingFactor.One, 2 => BlendingFactor.SrcColor, 3 => BlendingFactor.OneMinusSrcColor,
        4 => BlendingFactor.SrcAlpha, 5 => BlendingFactor.OneMinusSrcAlpha, 6 => BlendingFactor.DstAlpha, _ => BlendingFactor.OneMinusDstAlpha,
    };

    static DepthFunction DepthFunction(int f) => f switch
    {
        0 => Silk.NET.OpenGL.DepthFunction.Never, 1 => Silk.NET.OpenGL.DepthFunction.Less, 2 => Silk.NET.OpenGL.DepthFunction.Equal,
        3 => Silk.NET.OpenGL.DepthFunction.Lequal, 4 => Silk.NET.OpenGL.DepthFunction.Greater, 5 => Silk.NET.OpenGL.DepthFunction.Notequal,
        6 => Silk.NET.OpenGL.DepthFunction.Gequal, _ => Silk.NET.OpenGL.DepthFunction.Always,
    };
}
