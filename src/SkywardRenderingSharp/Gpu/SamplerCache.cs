using Silk.NET.OpenGL;

namespace SkywardRenderingSharp.Gpu;

/// <summary>GL sampler objects for GX texture wrap and filter modes, created on first use.</summary>
/// <remarks>Wrap: 0 clamp, 1 repeat, 2 mirror. Filter: 0 nearest, 1 linear, 2..5 the nearest/linear mip variants.</remarks>
public sealed class SamplerCache(GL gl) : IDisposable
{
    readonly Dictionary<(int, int, int, int), uint> _samplers = [];

    public uint Get(int wrapS, int wrapT, int minFilter, int magFilter)
    {
        if (_samplers.TryGetValue((wrapS, wrapT, minFilter, magFilter), out uint s)) return s;
        s = gl.GenSampler();
        gl.SamplerParameter(s, SamplerParameterI.WrapS, Wrap(wrapS));
        gl.SamplerParameter(s, SamplerParameterI.WrapT, Wrap(wrapT));
        gl.SamplerParameter(s, SamplerParameterI.MinFilter, Min(minFilter));
        gl.SamplerParameter(s, SamplerParameterI.MagFilter, magFilter == 0 ? (int)GLEnum.Nearest : (int)GLEnum.Linear);
        gl.SamplerParameter(s, SamplerParameterF.MaxAnisotropy, 8f);
        return _samplers[(wrapS, wrapT, minFilter, magFilter)] = s;
    }

    static int Wrap(int mode) => (int)(mode switch { 0 => GLEnum.ClampToEdge, 2 => GLEnum.MirroredRepeat, _ => GLEnum.Repeat });

    static int Min(int mode) => (int)(mode switch
    {
        0 => GLEnum.Nearest, 1 => GLEnum.Linear, 2 => GLEnum.NearestMipmapNearest, 3 => GLEnum.LinearMipmapNearest,
        4 => GLEnum.NearestMipmapLinear, _ => GLEnum.LinearMipmapLinear,
    });

    public void Dispose() { foreach (uint s in _samplers.Values) gl.DeleteSampler(s); }
}
