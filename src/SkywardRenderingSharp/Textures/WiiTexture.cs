namespace SkywardRenderingSharp.Textures;

/// <summary>Decodes the Wii GX texture formats a BRRES TEX0 stores inline (the textures that did not move to .xtx).</summary>
/// <remarks>Intensity formats replicate to RGB; I4 and I8 also replicate to alpha, as the hardware does.</remarks>
public static class WiiTexture
{
    public enum Format { I4 = 0, I8 = 1, IA4 = 2, IA8 = 3, Rgb565 = 4, Rgb5A3 = 5, Rgba8 = 6, Cmpr = 14 }

    public static RgbaImage Decode(ReadOnlySpan<byte> data, int width, int height, Format format)
    {
        var (bw, bh, bytes) = format switch
        {
            Format.I4 => (8, 8, 32), Format.I8 or Format.IA4 => (8, 4, 32), Format.Cmpr => (8, 8, 32),
            Format.Rgba8 => (4, 4, 64), _ => (4, 4, 32),
        };
        var pixels = new byte[width * height * 4];
        int offset = 0;
        for (int by = 0; by < height; by += bh)
        {
            for (int bx = 0; bx < width; bx += bw, offset += bytes)
                DecodeBlock(data.Slice(offset, bytes), pixels, width, height, bx, by, format);
        }
        return new RgbaImage(width, height, pixels);
    }

    static void DecodeBlock(ReadOnlySpan<byte> b, byte[] image, int width, int height, int bx, int by, Format format)
    {
        if (format == Format.Cmpr) { DecodeCmpr(b, image, width, height, bx, by); return; }
        var (bw, bh) = format is Format.I4 ? (8, 8) : format is Format.I8 or Format.IA4 ? (8, 4) : (4, 4);
        for (int y = 0; y < bh; y++)
        {
            for (int x = 0; x < bw; x++)
            {
                if (bx + x >= width || by + y >= height) continue;
                int i = y * bw + x;
                var (r, g, bl, a) = Texel(b, i, format, bw * bh);
                int o = ((by + y) * width + bx + x) * 4;
                image[o] = r; image[o + 1] = g; image[o + 2] = bl; image[o + 3] = a;
            }
        }
    }

    static (byte R, byte G, byte B, byte A) Texel(ReadOnlySpan<byte> b, int i, Format format, int count)
    {
        switch (format)
        {
            case Format.I4: { int v = (i & 1) == 0 ? b[i / 2] >> 4 : b[i / 2] & 15; byte e = (byte)(v * 17); return (e, e, e, e); }
            case Format.I8: return (b[i], b[i], b[i], b[i]);
            case Format.IA4: { byte a = (byte)((b[i] >> 4) * 17), v = (byte)((b[i] & 15) * 17); return (v, v, v, a); }
            case Format.IA8: return (b[i * 2 + 1], b[i * 2 + 1], b[i * 2 + 1], b[i * 2]);
            case Format.Rgb565: return Rgb565(b[i * 2] << 8 | b[i * 2 + 1]);
            case Format.Rgb5A3: return Rgb5A3(b[i * 2] << 8 | b[i * 2 + 1]);
            default: return (b[i * 2 + 1], b[32 + i * 2], b[32 + i * 2 + 1], b[i * 2]);
        }
    }

    static (byte, byte, byte, byte) Rgb565(int v) => (Expand(v >> 11, 5), Expand(v >> 5 & 63, 6), Expand(v & 31, 5), 255);

    static (byte, byte, byte, byte) Rgb5A3(int v) => (v & 0x8000) != 0
        ? (Expand(v >> 10 & 31, 5), Expand(v >> 5 & 31, 5), Expand(v & 31, 5), (byte)255)
        : (Expand(v >> 8 & 15, 4), Expand(v >> 4 & 15, 4), Expand(v & 15, 4), Expand(v >> 12 & 7, 3));

    static byte Expand(int v, int bits) => (byte)(v * 255 / ((1 << bits) - 1));

    // CMPR is a 8x8 tile of four DXT1 blocks with big-endian colours.
    static void DecodeCmpr(ReadOnlySpan<byte> b, byte[] image, int width, int height, int bx, int by)
    {
        for (int sub = 0; sub < 4; sub++)
        {
            var s = b.Slice(sub * 8, 8);
            int c0 = s[0] << 8 | s[1], c1 = s[2] << 8 | s[3];
            var palette = new (byte R, byte G, byte B, byte A)[4];
            var (r0, g0, b0, _) = Rgb565(c0); var (r1, g1, b1, _) = Rgb565(c1);
            palette[0] = (r0, g0, b0, 255); palette[1] = (r1, g1, b1, 255);
            if (c0 > c1)
            {
                palette[2] = Mix(palette[0], palette[1], 2, 1); palette[3] = Mix(palette[0], palette[1], 1, 2);
            }
            else
            {
                palette[2] = Mix(palette[0], palette[1], 1, 1); palette[3] = (0, 0, 0, 0);
            }
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int px = bx + sub % 2 * 4 + x, py = by + sub / 2 * 4 + y;
                    if (px >= width || py >= height) continue;
                    var c = palette[s[4 + y] >> (6 - x * 2) & 3];
                    int o = (py * width + px) * 4;
                    image[o] = c.R; image[o + 1] = c.G; image[o + 2] = c.B; image[o + 3] = c.A;
                }
            }
        }
    }

    static (byte, byte, byte, byte) Mix((byte R, byte G, byte B, byte A) a, (byte R, byte G, byte B, byte A) b, int wa, int wb)
    {
        int n = wa + wb;
        return ((byte)((a.R * wa + b.R * wb) / n), (byte)((a.G * wa + b.G * wb) / n), (byte)((a.B * wa + b.B * wb) / n), 255);
    }
}
