namespace SkywardRenderingSharp.Textures;

/// <summary>CPU decoders for the BC1 to BC5 block-compressed formats (DXT1, DXT3, DXT5, BC4, BC5), to RGBA8.</summary>
/// <remarks>BC6H and BC7 are not decoded here (see <see cref="XtxFile"/>).</remarks>
public static class BcDecoder
{
    public enum Kind { Bc1, Bc2, Bc3, Bc4, Bc5 }

    public static int BytesPerBlock(Kind kind) => kind is Kind.Bc1 or Kind.Bc4 ? 8 : 16;

    public static RgbaImage Decode(ReadOnlySpan<byte> blocks, int width, int height, Kind kind)
    {
        var pixels = new byte[width * height * 4];
        int bw = (width + 3) / 4, bytes = BytesPerBlock(kind);
        Span<byte> texels = stackalloc byte[64];
        for (int by = 0; by < (height + 3) / 4; by++)
        {
            for (int bx = 0; bx < bw; bx++)
            {
                DecodeBlock(blocks.Slice((by * bw + bx) * bytes, bytes), kind, texels);
                for (int y = 0; y < 4 && by * 4 + y < height; y++)
                {
                    for (int x = 0; x < 4 && bx * 4 + x < width; x++) texels.Slice((y * 4 + x) * 4, 4).CopyTo(pixels.AsSpan(((by * 4 + y) * width + bx * 4 + x) * 4));
                }
            }
        }
        return new RgbaImage(width, height, pixels);
    }

    static void DecodeBlock(ReadOnlySpan<byte> b, Kind kind, Span<byte> texels)
    {
        switch (kind)
        {
            case Kind.Bc1: Color(b, texels, true); break;
            case Kind.Bc2:
                Color(b[8..], texels, false);
                for (int i = 0; i < 16; i++) texels[i * 4 + 3] = (byte)((b[i / 2] >> (i % 2 * 4) & 15) * 17);
                break;
            case Kind.Bc3: Color(b[8..], texels, false); Channel(b, texels, 3); break;
            case Kind.Bc4:
                Channel(b, texels, 0);
                for (int i = 0; i < 16; i++) { texels[i * 4 + 1] = texels[i * 4 + 2] = texels[i * 4]; texels[i * 4 + 3] = 255; }
                break;
            default:
                Channel(b, texels, 0); Channel(b[8..], texels, 1);
                for (int i = 0; i < 16; i++) { texels[i * 4 + 2] = 0; texels[i * 4 + 3] = 255; }
                break;
        }
    }

    // The 565 colour endpoints and the 2-bit selectors; in BC1's three-colour mode selector 3 is transparent.
    static void Color(ReadOnlySpan<byte> b, Span<byte> texels, bool oneBitAlpha)
    {
        int c0 = b[0] | b[1] << 8, c1 = b[2] | b[3] << 8;
        Span<int> p = stackalloc int[16];
        Expand(c0, p[..4]); Expand(c1, p.Slice(4, 4));
        bool four = c0 > c1 || !oneBitAlpha;
        for (int i = 0; i < 3; i++)
        {
            p[8 + i] = four ? (2 * p[i] + p[4 + i]) / 3 : (p[i] + p[4 + i]) / 2;
            p[12 + i] = four ? (p[i] + 2 * p[4 + i]) / 3 : 0;
        }
        p[11] = 255; p[15] = four ? 255 : 0;
        uint selectors = (uint)(b[4] | b[5] << 8 | b[6] << 16 | b[7] << 24);
        for (int i = 0; i < 16; i++)
        {
            int s = (int)(selectors >> (i * 2) & 3);
            for (int c = 0; c < 4; c++) texels[i * 4 + c] = (byte)p[s * 4 + c];
        }
    }

    static void Expand(int c, Span<int> rgba)
    {
        rgba[0] = (c >> 11) * 255 / 31; rgba[1] = (c >> 5 & 63) * 255 / 63; rgba[2] = (c & 31) * 255 / 31; rgba[3] = 255;
    }

    // A BC4-style single channel: two endpoints and 3-bit selectors, written to the given channel.
    static void Channel(ReadOnlySpan<byte> b, Span<byte> texels, int channel)
    {
        Span<int> v = stackalloc int[8];
        v[0] = b[0]; v[1] = b[1];
        bool eight = v[0] > v[1];
        for (int i = 1; i < (eight ? 7 : 5); i++) v[i + 1] = eight ? ((7 - i) * v[0] + i * v[1]) / 7 : ((5 - i) * v[0] + i * v[1]) / 5;
        if (!eight) { v[6] = 0; v[7] = 255; }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)b[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) texels[i * 4 + channel] = (byte)v[(int)(bits >> (3 * i) & 7)];
    }
}
