using Silk.NET.OpenGL;

namespace SkywardRenderingSharp.Gpu;

/// <summary>Puts the GL context into the default state the renderer is written against, and puts the host's state back afterwards:
/// <c>using (GlHostState.Enter(gl)) { ... }</c>.</summary>
/// <remarks>
/// A host such as a viewer may run with reversed depth (<c>glClipControl</c> zero-to-one), its own framebuffer, program, vertex array,
/// textures and blend state bound; all of that is saved, set to GL's defaults for the renderer, and restored on dispose. Clip control is
/// only touched on a context that has it (4.5 or ARB_clip_control). Texture, sampler and uniform-buffer bindings are saved for the
/// first <see cref="Units"/> units and binding points, which covers every one the renderer uses.
/// </remarks>
public sealed class GlHostState : IDisposable
{
    public const int Units = 16;

    static readonly EnableCap[] Caps =
        [EnableCap.DepthTest, EnableCap.Blend, EnableCap.CullFace, EnableCap.ScissorTest, EnableCap.StencilTest, EnableCap.FramebufferSrgb, EnableCap.PolygonOffsetFill];

    readonly GL _gl;
    readonly bool _clipControl;
    readonly int _clipOrigin, _clipDepth, _readFbo, _drawFbo, _program, _vao, _activeTexture, _arrayBuffer, _depthFunc, _cullFace, _frontFace;
    readonly int _blendSrcRgb, _blendDstRgb, _blendSrcAlpha, _blendDstAlpha, _blendEqRgb, _blendEqAlpha;
    readonly int[] _viewport = new int[4], _colorMask = new int[4], _textures = new int[Units], _samplers = new int[Units];
    readonly float[] _clearColor = new float[4];
    readonly float _clearDepth;
    readonly bool _depthMask;
    readonly bool[] _caps = new bool[Caps.Length];
    readonly (int Buffer, long Start, long Size)[] _ubos = new (int, long, long)[Units];

    GlHostState(GL gl)
    {
        _gl = gl;
        _clipControl = gl.GetInteger(GetPName.MajorVersion) * 10 + gl.GetInteger(GetPName.MinorVersion) >= 45 || gl.IsExtensionPresent("GL_ARB_clip_control");
        if (_clipControl)
        {
            _clipOrigin = gl.GetInteger(GLEnum.ClipOrigin);
            _clipDepth = gl.GetInteger(GLEnum.ClipDepthMode);
        }
        _readFbo = gl.GetInteger(GetPName.ReadFramebufferBinding);
        _drawFbo = gl.GetInteger(GetPName.DrawFramebufferBinding);
        _program = gl.GetInteger(GetPName.CurrentProgram);
        _vao = gl.GetInteger(GetPName.VertexArrayBinding);
        _activeTexture = gl.GetInteger(GetPName.ActiveTexture);
        _arrayBuffer = gl.GetInteger(GetPName.ArrayBufferBinding);
        gl.GetInteger(GetPName.Viewport, _viewport);
        gl.GetInteger(GetPName.ColorWritemask, _colorMask);
        _depthMask = gl.GetBoolean(GetPName.DepthWritemask);
        _clearDepth = gl.GetFloat(GetPName.DepthClearValue);
        gl.GetFloat(GetPName.ColorClearValue, _clearColor);
        _depthFunc = gl.GetInteger(GetPName.DepthFunc);
        _cullFace = gl.GetInteger(GetPName.CullFaceMode);
        _frontFace = gl.GetInteger(GetPName.FrontFace);
        _blendSrcRgb = gl.GetInteger(GetPName.BlendSrcRgb); _blendDstRgb = gl.GetInteger(GetPName.BlendDstRgb);
        _blendSrcAlpha = gl.GetInteger(GetPName.BlendSrcAlpha); _blendDstAlpha = gl.GetInteger(GetPName.BlendDstAlpha);
        _blendEqRgb = gl.GetInteger(GetPName.BlendEquationRgb); _blendEqAlpha = gl.GetInteger(GetPName.BlendEquationAlpha);
        for (int i = 0; i < Caps.Length; i++) _caps[i] = gl.IsEnabled(Caps[i]);
        for (int i = 0; i < Units; i++)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + i);
            _textures[i] = gl.GetInteger(GetPName.TextureBinding2D);
            _samplers[i] = gl.GetInteger(GetPName.SamplerBinding);
            gl.GetInteger(GLEnum.UniformBufferBinding, (uint)i, out int buffer);
            gl.GetInteger64(GLEnum.UniformBufferStart, (uint)i, out long start);
            gl.GetInteger64(GLEnum.UniformBufferSize, (uint)i, out long size);
            _ubos[i] = (buffer, start, size);
        }
        ApplyDefaults();
    }

    public static GlHostState Enter(GL gl) => new(gl);

    void ApplyDefaults()
    {
        if (_clipControl) _gl.ClipControl(ClipControlOrigin.LowerLeft, ClipControlDepth.NegativeOneToOne);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.ColorMask(true, true, true, true);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        foreach (var cap in Caps) _gl.Disable(cap);
    }

    public void Dispose()
    {
        var gl = _gl;
        if (_clipControl) gl.ClipControl((ClipControlOrigin)_clipOrigin, (ClipControlDepth)_clipDepth);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)_readFbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)_drawFbo);
        gl.UseProgram((uint)_program);
        gl.BindVertexArray((uint)_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_arrayBuffer);
        gl.Viewport(_viewport[0], _viewport[1], (uint)_viewport[2], (uint)_viewport[3]);
        gl.ColorMask(_colorMask[0] != 0, _colorMask[1] != 0, _colorMask[2] != 0, _colorMask[3] != 0);
        gl.DepthMask(_depthMask);
        gl.ClearDepth(_clearDepth);
        gl.ClearColor(_clearColor[0], _clearColor[1], _clearColor[2], _clearColor[3]);
        gl.DepthFunc((DepthFunction)_depthFunc);
        gl.CullFace((TriangleFace)_cullFace);
        gl.FrontFace((FrontFaceDirection)_frontFace);
        gl.BlendFuncSeparate((BlendingFactor)_blendSrcRgb, (BlendingFactor)_blendDstRgb, (BlendingFactor)_blendSrcAlpha, (BlendingFactor)_blendDstAlpha);
        gl.BlendEquationSeparate((BlendEquationModeEXT)_blendEqRgb, (BlendEquationModeEXT)_blendEqAlpha);
        for (int i = 0; i < Caps.Length; i++) { if (_caps[i]) gl.Enable(Caps[i]); else gl.Disable(Caps[i]); }
        for (int i = 0; i < Units; i++)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + i);
            gl.BindTexture(TextureTarget.Texture2D, (uint)_textures[i]);
            gl.BindSampler((uint)i, (uint)_samplers[i]);
            var (buffer, start, size) = _ubos[i];
            if (buffer == 0) gl.BindBufferBase(BufferTargetARB.UniformBuffer, (uint)i, 0);
            else if (size > 0) gl.BindBufferRange(BufferTargetARB.UniformBuffer, (uint)i, (uint)buffer, (nint)start, (nuint)size);
            else gl.BindBufferBase(BufferTargetARB.UniformBuffer, (uint)i, (uint)buffer);
        }
        gl.ActiveTexture((TextureUnit)_activeTexture);
    }
}
