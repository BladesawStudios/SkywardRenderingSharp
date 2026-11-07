using System.Numerics;
using Silk.NET.OpenGL;
using SkywardRenderingSharp.Gpu;

namespace SkywardRenderingSharp.Rendering;

/// <summary>The painterly post chain: dilation "paint" passes, depth-of-field blur and bloom, composited by distance.</summary>
/// <remarks>It is the game's own pass set (Paint_*, GaussianBlur_*, BloomMask_*) as readable GLSL. The paint image is the scene
/// downsampled and repeatedly max-filtered; the composite swaps it in for far and very near pixels.</remarks>
sealed class PostChain : IDisposable
{
    readonly GL _gl;
    readonly GlProgram _copy, _dilate, _blur, _bloomMask, _composite;
    readonly uint _vao;
    RenderTarget? _half0, _half1, _half2, _half3, _half4, _half5, _quarter0, _quarter1;

    public PostChain(GL gl)
    {
        _gl = gl;
        _vao = gl.GenVertexArray();
        _copy = Program("copy.frag"); _dilate = Program("dilate.frag"); _blur = Program("blur.frag");
        _bloomMask = Program("bloom_mask.frag"); _composite = Program("composite.frag");
    }

    GlProgram Program(string fragment) => GlProgram.FromResources(_gl, "post/fullscreen.vert", $"post/{fragment}");

    public void Run(RenderTarget scene, RenderTarget output, Camera camera, RenderSettings s)
    {
        Resize(scene.Width, scene.Height, s.PaintDownscale);
        _gl.Disable(EnableCap.DepthTest); _gl.Disable(EnableCap.Blend); _gl.Disable(EnableCap.CullFace);
        _gl.ColorMask(true, true, true, true);
        uint paint = s.Paint ? BuildPaint(scene, s) : scene.Color;
        uint blur = s.DepthOfField ? BuildBlur(scene) : scene.Color;
        var bloom = s.Bloom ? BuildBloom(scene, s) : (scene.Color, scene.Color);
        Composite(scene, output, camera, s, paint, blur, bloom, _quarter0);
    }

    void Resize(int w, int h, int paintDownscale)
    {
        if (_half0 is not null && _half0.Width == Math.Max(1, w / 2) && _quarter0!.Width == Math.Max(1, w / paintDownscale)) return;
        foreach (var t in new[] { _half0, _half1, _half2, _half3, _half4, _half5, _quarter0, _quarter1 }) t?.Dispose();
        _half0 = new(_gl, Math.Max(1, w / 2), Math.Max(1, h / 2), false); _half1 = new(_gl, _half0.Width, _half0.Height, false);
        // Bloom runs at a quarter of the resolution (flag bit 2 of the bloom block selects a half) and its second layer at an eighth, as the engine's draw method sizes its targets.
        int bw = Math.Max(1, w / 4), bh = Math.Max(1, h / 4);
        _half2 = new(_gl, bw, bh, false); _half3 = new(_gl, bw, bh, false);
        _half4 = new(_gl, Math.Max(1, bw / 2), Math.Max(1, bh / 2), false); _half5 = new(_gl, _half4.Width, _half4.Height, false);
        _quarter0 = new(_gl, Math.Max(1, w / paintDownscale), Math.Max(1, h / paintDownscale), false); _quarter1 = new(_gl, _quarter0.Width, _quarter0.Height, false);
    }

    // The paint image: downsample, then ping-pong the max filter, alternating the two kernels as the game's pair does.
    uint BuildPaint(RenderTarget scene, RenderSettings s)
    {
        Pass(_copy, _quarter0!, ("uSource", scene.Color));
        var (a, b) = (_quarter0!, _quarter1!);
        for (int i = 0; i < s.PaintPasses; i++)
        {
            _dilate.Use(); _dilate.Set("uTexelSize", new Vector2(1f / a.Width, 1f / a.Height)); _dilate.Set("uKernel", i % 2);
            Pass(_dilate, b, ("uSource", a.Color));
            (a, b) = (b, a);
        }
        if (!ReferenceEquals(a, _quarter0)) Pass(_copy, _quarter0!, ("uSource", a.Color));
        return _quarter0!.Color;
    }

    uint BuildBlur(RenderTarget scene) => Blur(scene.Color, _half0!, _half1!);

    // The bloom mask at quarter resolution and its blur (the first layer), then that downsampled to an eighth and blurred (the second layer), each weighted by its intensity.
    (uint First, uint Second) BuildBloom(RenderTarget scene, RenderSettings s)
    {
        _bloomMask.Use(); _bloomMask.Set("uThreshold", s.BloomMaskThreshold);
        Pass(_bloomMask, _half2!, ("uScene", scene.Color), ("uDepth", scene.Depth));
        uint first = Blur(_half2!.Color, _half3!, _half2!);
        Pass(_copy, _half4!, ("uSource", first));
        return (first, Blur(_half4!.Color, _half5!, _half4!));
    }

    // Horizontal then vertical Gaussian through two half-resolution targets; the result ends in the second.
    uint Blur(uint source, RenderTarget a, RenderTarget b)
    {
        _blur.Use(); _blur.Set("uStep", new Vector2(1f / a.Width, 0));
        Pass(_blur, a, ("uSource", source));
        _blur.Use(); _blur.Set("uStep", new Vector2(0, 1f / a.Height));
        Pass(_blur, b, ("uSource", a.Color));
        return b.Color;
    }

    void Composite(RenderTarget scene, RenderTarget output, Camera camera, RenderSettings s, uint paint, uint blur, (uint First, uint Second) bloom, RenderTarget? paintTarget)
    {
        _composite.Use();
        _composite.Set("uNearFar", new Vector2(camera.Near, camera.Far));
        _composite.Set("uPaintTexel", paintTarget is null ? Vector2.One : new Vector2(1f / paintTarget.Width, 1f / paintTarget.Height));
        _composite.Set("uPaintConstant", s.PaintConstant); _composite.Set("uPaintFar", s.PaintFar); _composite.Set("uPaintNear", s.PaintNear); _composite.Set("uDofFar", s.DofFar); _composite.Set("uDofNear", s.DofNear);
        _composite.Set("uBloomStrength", s.BloomStrength); _composite.Set("uBloomWeights", s.BloomWeights); _composite.Set("uBloomTint", s.BloomTint);
        _composite.Set("uFlags", (s.Paint ? 1 : 0) | (s.DepthOfField ? 2 : 0) | (s.Bloom ? 4 : 0) | (s.Fog ? 8 : 0));
        _composite.Set("uFogColor", s.FogColor); _composite.Set("uFogRange", s.FogRange);
        Pass(_composite, output, ("uSharp", scene.Color), ("uDepth", scene.Depth), ("uPaint", paint), ("uBlur", blur), ("uBloom", bloom.First), ("uBloom2", bloom.Second));
    }

    // Draws the screen triangle into a target, binding each named texture to its own unit.
    void Pass(GlProgram program, RenderTarget target, params (string Name, uint Texture)[] inputs)
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
        foreach (var p in new[] { _copy, _dilate, _blur, _bloomMask, _composite }) p.Dispose();
        foreach (var t in new[] { _half0, _half1, _half2, _half3, _half4, _half5, _quarter0, _quarter1 }) t?.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
