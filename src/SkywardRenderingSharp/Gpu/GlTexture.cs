using Silk.NET.OpenGL;
using SkywardRenderingSharp.Textures;

namespace SkywardRenderingSharp.Gpu;

/// <summary>A 2D RGBA8 texture uploaded from a decoded image, with a full mip chain. sRGB textures decode to linear light when sampled, as the game's colour textures do.</summary>
public sealed class GlTexture : IDisposable
{
    const int ScratchUnit = 15;

    readonly GL _gl;

    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    public GlTexture(GL gl, RgbaImage image, bool srgb = false)
    {
        _gl = gl; Width = image.Width; Height = image.Height;
        Handle = gl.GenTexture();
        // Uploading binds the texture, so it goes through a scratch unit the draws never sample from.
        gl.ActiveTexture(TextureUnit.Texture0 + ScratchUnit);
        gl.BindTexture(TextureTarget.Texture2D, Handle);
        gl.TexImage2D(TextureTarget.Texture2D, 0, srgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (ReadOnlySpan<byte>)image.Pixels);
        gl.GenerateMipmap(TextureTarget.Texture2D);
    }

    public void Bind(int unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}
