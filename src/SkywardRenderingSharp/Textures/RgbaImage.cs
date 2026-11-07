namespace SkywardRenderingSharp.Textures;

/// <summary>A decoded image: tightly packed 8-bit RGBA, top row first.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    /// <summary>Whether the texels are sRGB encoded colour (decoded to linear light when sampled) or plain data such as masks and ramps.</summary>
    public bool Srgb { get; init; } = true;

    public static RgbaImage Solid(byte r, byte g, byte b, byte a)
    {
        var pixels = new byte[] { r, g, b, a };
        return new RgbaImage(1, 1, pixels);
    }
}
