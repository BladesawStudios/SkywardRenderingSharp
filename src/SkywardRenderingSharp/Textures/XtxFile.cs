using System.Buffers.Binary;
using AstcSharp;
using AstcSharp.Core;

namespace SkywardRenderingSharp.Textures;

/// <summary>A Tegra .xtx texture container: the pixel data the game's BRRES models reference by order.</summary>
/// <remarks>Only the base mip is decoded; the renderer generates the rest on the GPU. ASTC, BC1 to BC5 and the common uncompressed formats are supported.</remarks>
public sealed class XtxFile
{
    const uint InfoBlock = 2, DataBlock = 3;
    const uint FormatAstc4x4 = 0x79, FormatAstc12x12 = 0x86;

    readonly List<(Info Info, byte[] Data)> _textures = [];

    public int Count => _textures.Count;

    public static XtxFile Open(ReadOnlySpan<byte> file)
    {
        if (!file[..4].SequenceEqual("DFvN"u8)) throw new InvalidDataException("Not an XTX file.");
        var xtx = new XtxFile();
        var infos = new List<Info>();
        int p = BinaryPrimitives.ReadInt32LittleEndian(file[4..]);
        while (p + 0x28 <= file.Length)
        {
            var h = file[p..];
            if (!h[..4].SequenceEqual("HBvN"u8)) break;
            long dataSize = BinaryPrimitives.ReadInt64LittleEndian(h[8..]), dataOffset = BinaryPrimitives.ReadInt64LittleEndian(h[16..]);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(h[24..]);
            var data = file.Slice(p + (int)dataOffset, (int)dataSize);
            if (type == InfoBlock) infos.Add(Info.Read(data));
            else if (type == DataBlock) xtx._textures.Add((infos[xtx._textures.Count], data.ToArray()));
            p += (int)dataOffset + (int)dataSize;
        }
        return xtx;
    }

    public (int Width, int Height) SizeOf(int index) => (_textures[index].Info.Width, _textures[index].Info.Height);

    public RgbaImage Decode(int index)
    {
        var (info, data) = _textures[index];
        // The sRGB ASTC variants follow the unorm ones; the Wii-era art is authored and blended in gamma space, so both decode raw.
        // The sRGB ASTC variants (0x87 on) are colour textures; the unorm ones (0x79 to 0x86) and the one and two channel formats are data.
        if (info.Format is >= FormatAstc4x4 and <= FormatAstc12x12 + 14) return DecodeAstc(info, data) with { Srgb = info.Format > FormatAstc12x12 };
        if (BcKind(info.Format) is { } kind) return DecodeBc(info, data, kind);
        return info.Format switch
        {
            0x25 or 0x38 => DecodeRaw(info, data, 4, (s, o) => (s[o], s[o + 1], s[o + 2], s[o + 3])),
            0x6D => DecodeRaw(info, data, 4, (s, o) => (s[o + 2], s[o + 1], s[o], s[o + 3])),
            0x01 => DecodeRaw(info, data, 1, (s, o) => (s[o], s[o], s[o], 255)) with { Srgb = false },
            0x0D => DecodeRaw(info, data, 2, (s, o) => (s[o], s[o + 1], 0, 255)) with { Srgb = false },
            0x3C => DecodeRaw(info, data, 2, (s, o) => { int v = s[o] | s[o + 1] << 8; return ((byte)((v >> 11) * 255 / 31), (byte)((v >> 5 & 63) * 255 / 63), (byte)((v & 31) * 255 / 31), 255); }),
            0x3B => DecodeRaw(info, data, 2, (s, o) => { int v = s[o] | s[o + 1] << 8; return ((byte)((v >> 11 & 31) * 255 / 31), (byte)((v >> 6 & 31) * 255 / 31), (byte)((v >> 1 & 31) * 255 / 31), (byte)((v & 1) * 255)); }),
            0x39 => DecodeRaw(info, data, 2, (s, o) => { int v = s[o] | s[o + 1] << 8; return ((byte)((v >> 12) * 17), (byte)((v >> 8 & 15) * 17), (byte)((v >> 4 & 15) * 17), (byte)((v & 15) * 17)); }),
            // TODO: BC6H (0x50) and BC7 (0x4D) are not decoded; Link, Zelda and the common objects do not use them, and a decoder would remove the limit.
            _ => throw new NotSupportedException($"XTX format 0x{info.Format:x} is not supported."),
        };
    }

    static BcDecoder.Kind? BcKind(uint format) => format switch
    {
        0x42 => BcDecoder.Kind.Bc1, 0x43 => BcDecoder.Kind.Bc2, 0x44 => BcDecoder.Kind.Bc3,
        0x49 or 0x4A => BcDecoder.Kind.Bc4, 0x4B or 0x4C => BcDecoder.Kind.Bc5, _ => null,
    };

    static RgbaImage DecodeBc(Info info, byte[] data, BcDecoder.Kind kind)
    {
        int bytes = BcDecoder.BytesPerBlock(kind), wb = (info.Width + 3) / 4, hb = (info.Height + 3) / 4;
        return BcDecoder.Decode(TegraSwizzle.Deswizzle(data, wb, hb, bytes, info.BlockHeightLog2), info.Width, info.Height, kind);
    }

    // Uncompressed texels: a reader per format turns one texel into RGBA.
    static RgbaImage DecodeRaw(Info info, byte[] data, int bytesPerPixel, Func<byte[], int, (byte R, byte G, byte B, byte A)> read)
    {
        var linear = TegraSwizzle.Deswizzle(data, info.Width, info.Height, bytesPerPixel, info.BlockHeightLog2);
        var pixels = new byte[info.Width * info.Height * 4];
        for (int i = 0; i < info.Width * info.Height; i++)
        {
            var (r, g, b, a) = read(linear, i * bytesPerPixel);
            pixels[i * 4] = r; pixels[i * 4 + 1] = g; pixels[i * 4 + 2] = b; pixels[i * 4 + 3] = a;
        }
        return new RgbaImage(info.Width, info.Height, pixels);
    }

    static RgbaImage DecodeAstc(Info info, byte[] data)
    {
        var (bw, bh) = AstcBlock(info.Format);
        int wb = (info.Width + bw - 1) / bw, hb = (info.Height + bh - 1) / bh;
        var blocks = TegraSwizzle.Deswizzle(data, wb, hb, 16, info.BlockHeightLog2);
        using var source = new MemoryStream(blocks);
        using var destination = new MemoryStream();
        AstcDecoder.DecompressImage(source, destination, info.Width, info.Height, Footprint.FromFootprintType(Footprints[(info.Format - FormatAstc4x4) % 14]), LdrDecodeMode.Linear);
        return new RgbaImage(info.Width, info.Height, destination.ToArray());
    }

    static readonly (int W, int H)[] Blocks = [(4, 4), (5, 4), (5, 5), (6, 5), (6, 6), (8, 5), (8, 6), (8, 8), (10, 5), (10, 6), (10, 8), (10, 10), (12, 10), (12, 12)];

    static readonly FootprintType[] Footprints =
    [
        FootprintType.Footprint4x4, FootprintType.Footprint5x4, FootprintType.Footprint5x5, FootprintType.Footprint6x5, FootprintType.Footprint6x6,
        FootprintType.Footprint8x5, FootprintType.Footprint8x6, FootprintType.Footprint8x8, FootprintType.Footprint10x5, FootprintType.Footprint10x6,
        FootprintType.Footprint10x8, FootprintType.Footprint10x10, FootprintType.Footprint12x10, FootprintType.Footprint12x12,
    ];

    static (int, int) AstcBlock(uint format) => Blocks[(int)((format - FormatAstc4x4) % 14)];

    readonly record struct Info(int Width, int Height, uint Format, int BlockHeightLog2)
    {
        // DataSize u64, Alignment, Width, Height, Depth, Target, Format, MipCount, SliceSize, 17 mip offsets, then the layout word.
        public static Info Read(ReadOnlySpan<byte> d) => new(
            BinaryPrimitives.ReadInt32LittleEndian(d[12..]), BinaryPrimitives.ReadInt32LittleEndian(d[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(d[28..]), (int)(BinaryPrimitives.ReadUInt32LittleEndian(d[(40 + 17 * 4)..]) & 7));
    }
}
