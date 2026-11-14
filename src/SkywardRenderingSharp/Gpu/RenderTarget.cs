using Silk.NET.OpenGL;

namespace SkywardRenderingSharp.Gpu;

/// <summary>A framebuffer with an RGBA colour texture and, optionally, a sampleable depth texture.</summary>
/// <remarks>Colour is 16-bit float so the post chain can carry values above one, unless the target asks for 8-bit unsigned normalised colour,
/// which clamps what is written to 0..1; depth is a 24-bit texture.</remarks>
public sealed class RenderTarget : IDisposable
{
    readonly GL _gl;

    public uint Framebuffer { get; }
    public uint Color { get; }
    public uint Depth { get; }
    public int Width { get; }
    public int Height { get; }

    public RenderTarget(GL gl, int width, int height, bool withDepth, bool unorm8 = false)
    {
        _gl = gl; Width = width; Height = height;
        Framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        Color = unorm8 ? Texture(InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte) : Texture(InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, Color, 0);
        if (withDepth)
        {
            Depth = Texture(InternalFormat.DepthComponent24, PixelFormat.DepthComponent, PixelType.UnsignedInt);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, Depth, 0);
        }
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete) throw new InvalidOperationException("Incomplete framebuffer.");
    }

    uint Texture(InternalFormat format, PixelFormat pixel, PixelType type)
    {
        uint t = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, t);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)Width, (uint)Height, 0, pixel, type, ReadOnlySpan<byte>.Empty);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return t;
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(Framebuffer);
        _gl.DeleteTexture(Color);
        if (Depth != 0) _gl.DeleteTexture(Depth);
    }
}
