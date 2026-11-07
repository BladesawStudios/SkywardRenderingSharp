namespace SkywardRenderingSharp.Textures;

/// <summary>Converts Tegra X1 block-linear texture memory to linear rows of blocks.</summary>
/// <remarks>Works in block units: a texel for uncompressed formats, a 4x4..12x12 block for compressed ones.</remarks>
public static class TegraSwizzle
{
    const int GobWidthBytes = 64, GobHeight = 8, GobBytes = 512;

    public static byte[] Deswizzle(ReadOnlySpan<byte> swizzled, int widthInBlocks, int heightInBlocks, int bytesPerBlock, int blockHeightLog2)
    {
        int blockHeight = 1 << blockHeightLog2;
        int widthInGobs = (widthInBlocks * bytesPerBlock + GobWidthBytes - 1) / GobWidthBytes;
        var linear = new byte[widthInBlocks * heightInBlocks * bytesPerBlock];
        for (int y = 0; y < heightInBlocks; y++)
        {
            for (int x = 0; x < widthInBlocks; x++)
            {
                int source = BlockOffset(x * bytesPerBlock, y, widthInGobs, blockHeight);
                int target = (y * widthInBlocks + x) * bytesPerBlock;
                if (source + bytesPerBlock <= swizzled.Length)
                    swizzled.Slice(source, bytesPerBlock).CopyTo(linear.AsSpan(target));
            }
        }
        return linear;
    }

    // The byte offset of (x bytes, y rows) inside a surface made of GOBs (64 x 8 bytes) stacked blockHeight high.
    static int BlockOffset(int xBytes, int y, int widthInGobs, int blockHeight)
    {
        int gob = y / (GobHeight * blockHeight) * GobBytes * blockHeight * widthInGobs
                + xBytes / GobWidthBytes * GobBytes * blockHeight
                + y % (GobHeight * blockHeight) / GobHeight * GobBytes;
        return gob + xBytes % 64 / 32 * 256 + y % 8 / 2 * 64 + xBytes % 32 / 16 * 32 + y % 2 * 16 + xBytes % 16;
    }
}
