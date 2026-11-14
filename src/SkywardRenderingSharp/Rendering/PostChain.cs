using System.Numerics;
using Silk.NET.OpenGL;
using SkywardRenderingSharp.Gpu;
using SkywardRenderingSharp.Resources;

namespace SkywardRenderingSharp.Rendering;

/// <summary>The painterly post chain: depth of field, the paint passes and bloom, drawn with the game's own post programs.</summary>
/// <remarks>Every image operation is a decompiled game program (<see cref="GamePostPrograms"/>); this class supplies the targets, the order and the
/// engine's constants. The depth-of-field mix and the final composite (fog, bloom, sRGB) are the renderer's own, because the game does them with
/// blend state and passes outside <c>Shaders/custom</c>.</remarks>
sealed class PostChain : IDisposable
{
    const string Blit = "Blit_RGBA", Paint = "Paint_Constant", PaintPinwheel = "Paint_Constant_NewKernel", PaintNearFar = "Paint_NearFar",
        BlurH = "GaussianBlur_Horizontal", BlurV = "GaussianBlur_Vertical", BloomMask = "BloomMask_Custom", DofFar = "Dof_FogToAlpha", DofNear = "Dof_MaskNear";

    // The engine's bloom mask scale (c5[1].x of BloomMask_Custom).
    const float BloomMaskScale = 2f;

    // TODO: the game's GaussianBlur_* weights sum to 2 per pass, so a horizontal and vertical pass has gain 4; where the engine takes that back
    // (blend state or its composite) is not traced. The chain divides the blurred images by 4 where it uses them.
    const float BlurGain = 0.25f;

    readonly GL _gl;
    readonly GamePostPrograms _game;
    readonly GlProgram _dofMix, _composite;
    readonly uint _vao;
    RenderTarget? _workA, _workB, _dofAmount, _dofBlurA, _dofBlurB, _mask, _bloomA, _bloomA2, _bloomB, _bloomC, _paintA, _paintB;

    public PostChain(GL gl, SkywardRom rom)
    {
        _gl = gl;
        _game = new GamePostPrograms(gl, rom, [Blit, Paint, PaintPinwheel, PaintNearFar, BlurH, BlurV, BloomMask, DofFar, DofNear]);
        _dofMix = GlProgram.FromResources(gl, "post/fullscreen.vert", "post/dof_mix.frag");
        _composite = GlProgram.FromResources(gl, "post/fullscreen.vert", "post/composite.frag");
        _vao = gl.GenVertexArray();
    }

    public void Run(RenderTarget scene, RenderTarget output, Camera camera, RenderSettings s)
    {
        Resize(scene.Width, scene.Height, s.PaintDownscale);
        _gl.Disable(EnableCap.DepthTest); _gl.Disable(EnableCap.Blend); _gl.Disable(EnableCap.CullFace);
        _gl.ColorMask(true, true, true, true);
        var ramps = DepthRamps.For(camera);

        RenderTarget current = scene;
        if (s.DepthOfField) current = DepthOfField(scene, ramps, s);
        if (s.Paint) current = Painted(scene, current, ramps, s);
        var bloom = s.Bloom ? BuildBloom(scene, s) : (scene.Color, scene.Color);
        Composite(current, scene, output, camera, s, bloom);
    }

    void Resize(int w, int h, int paintDownscale)
    {
        if (_workA is not null && _workA.Width == w && _workA.Height == h && _paintA!.Width == Math.Max(1, w / paintDownscale)) return;
        foreach (var t in Targets) t?.Dispose();
        _workA = new(_gl, w, h, false); _workB = new(_gl, w, h, false); _dofAmount = new(_gl, w, h, false);
        _dofBlurA = new(_gl, Math.Max(1, w / 2), Math.Max(1, h / 2), false); _dofBlurB = new(_gl, _dofBlurA.Width, _dofBlurA.Height, false);
        // Bloom runs at a quarter of the resolution (flag bit 2 of the bloom block selects a half) and its second layer at an eighth, as the engine's draw method sizes its targets.
        // The mask target is unsigned normalised, as the game's is, which clamps the part of each channel below the threshold to zero.
        int bw = Math.Max(1, w / 4), bh = Math.Max(1, h / 4);
        _mask = new(_gl, bw, bh, false, unorm8: true); _bloomA = new(_gl, bw, bh, false); _bloomA2 = new(_gl, bw, bh, false);
        _bloomB = new(_gl, Math.Max(1, bw / 2), Math.Max(1, bh / 2), false); _bloomC = new(_gl, _bloomB.Width, _bloomB.Height, false);
        _paintA = new(_gl, Math.Max(1, w / paintDownscale), Math.Max(1, h / paintDownscale), false); _paintB = new(_gl, _paintA.Width, _paintA.Height, false);
    }

    RenderTarget?[] Targets => [_workA, _workB, _dofAmount, _dofBlurA, _dofBlurB, _mask, _bloomA, _bloomA2, _bloomB, _bloomC, _paintA, _paintB];

    // The far ramp (Dof_FogToAlpha) and, with a near distance, the hard near cut (Dof_MaskNear) kept by a max blend; then the blurred scene mixed in by that amount.
    RenderTarget DepthOfField(RenderTarget scene, DepthRamps ramps, RenderSettings s)
    {
        uint blur = Blur(scene.Color, _dofBlurA!, _dofBlurB!);
        Vector2 far = ramps.Between(s.DofFar.X, s.DofFar.Y);
        _game.Draw(DofFar, _dofAmount!, [scene.Depth], Vector4.One, new Vector4(0, far.X, ramps.Bias, far.Y));
        if (s.DofNear > 0)
        {
            Vector2 near = ramps.Between(0, s.DofNear);
            _gl.Enable(EnableCap.Blend); _gl.BlendEquation(BlendEquationModeEXT.Max); _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            _game.Draw(DofNear, _dofAmount!, [scene.Depth], Vector4.One, new Vector4(0, near.X, ramps.Bias, near.Y));
            _gl.Disable(EnableCap.Blend); _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        }
        _dofMix.Use(); _dofMix.Set("uBlurGain", BlurGain);
        Own(_dofMix, _workA!, ("uSharp", scene.Color), ("uBlur", blur), ("uAmount", _dofAmount!.Color));
        return _workA!;
    }

    // The paint image (the scene downsampled, then max-filtered with the game's two kernels in turn) laid over everything at the constant alpha,
    // then Paint_NearFar swapping it in by the near and far ramps.
    RenderTarget Painted(RenderTarget scene, RenderTarget current, DepthRamps ramps, RenderSettings s)
    {
        _game.Draw(Blit, _paintA!, [scene.Color]);
        var (a, b) = (_paintA!, _paintB!);
        for (int i = 0; i < s.PaintPasses; i++)
        {
            _game.Draw(i % 2 == 0 ? Paint : PaintPinwheel, b, [a.Color], new Vector4(a.Width, a.Height, 1, 0));
            (a, b) = (b, a);
        }
        if (!ReferenceEquals(current, _workA)) _game.Draw(Blit, _workA!, [current.Color]);

        // TODO: the engine's paint alpha (c5[0].z of the Paint_* programs) and the order it blends the passes in are not traced; RenderSettings.PaintConstant
        // stands in for the alpha and the constant pass is drawn before the near/far one.
        _gl.Enable(EnableCap.Blend); _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); _gl.ColorMask(true, true, true, false);
        _game.Draw(Paint, _workA!, [a.Color], new Vector4(a.Width, a.Height, s.PaintConstant, 0));
        _gl.Disable(EnableCap.Blend); _gl.ColorMask(true, true, true, true);

        Vector2 near = ramps.Between(s.PaintNear.X, s.PaintNear.Y), far = ramps.Between(s.PaintFar.X, s.PaintFar.Y);
        _game.Draw(PaintNearFar, _workB!, [a.Color, scene.Depth, _workA!.Color],
            new Vector4(a.Width, a.Height, 1, 0), new Vector4(ramps.Bias, near.X, near.Y, far.X), new Vector4(far.Y, 0, 0, 0));
        return _workB!;
    }

    // The bloom mask at quarter resolution and its blur (the first layer), then that downsampled to an eighth and blurred (the second layer).
    (uint First, uint Second) BuildBloom(RenderTarget scene, RenderSettings s)
    {
        _game.Draw(BloomMask, _mask!, [scene.Color], s.BloomMaskThreshold, new Vector4(BloomMaskScale, 0, 0, 0));
        uint first = Blur(_mask!.Color, _bloomA!, _bloomA2!);
        _game.Draw(Blit, _bloomB!, [first]);
        return (first, Blur(_bloomB!.Color, _bloomC!, _bloomB!));
    }

    // GaussianBlur_Horizontal into the first target, then _Vertical into the second; the result ends in the second.
    uint Blur(uint source, RenderTarget a, RenderTarget b)
    {
        _game.Draw(BlurH, a, [source], new Vector4(a.Width, a.Height, 0, 0));
        _game.Draw(BlurV, b, [a.Color], new Vector4(b.Width, b.Height, 0, 0));
        return b.Color;
    }

    void Composite(RenderTarget current, RenderTarget scene, RenderTarget output, Camera camera, RenderSettings s, (uint First, uint Second) bloom)
    {
        _composite.Use();
        _composite.Set("uNearFar", new Vector2(camera.Near, camera.Far));
        // The second layer is blurred twice, so it carries the blur gain twice.
        _composite.Set("uBloomWeights", s.BloomWeights * s.BloomStrength * new Vector2(BlurGain, BlurGain * BlurGain)); _composite.Set("uBloomTint", s.BloomTint);
        _composite.Set("uFlags", (s.Bloom ? 4 : 0) | (s.Fog ? 8 : 0));
        _composite.Set("uFogColor", s.FogColor); _composite.Set("uFogRange", s.FogRange);
        Own(_composite, output, ("uScene", current.Color), ("uDepth", scene.Depth), ("uBloom", bloom.First), ("uBloom2", bloom.Second));
    }

    // Draws one of the renderer's own full-screen programs into a target, binding each named texture to its own unit.
    void Own(GlProgram program, RenderTarget target, params (string Name, uint Texture)[] inputs)
    {
        program.Use();
        for (int i = 0; i < inputs.Length; i++)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + i);
            _gl.BindTexture(TextureTarget.Texture2D, inputs[i].Texture);
            _gl.BindSampler((uint)i, 0);
            program.Set(inputs[i].Name, i);
        }
        target.Bind();
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void Dispose()
    {
        _game.Dispose(); _dofMix.Dispose(); _composite.Dispose();
        foreach (var t in Targets) t?.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
