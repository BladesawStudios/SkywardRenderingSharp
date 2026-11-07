namespace SkywardRenderingSharp.Archives;

/// <summary>Decompresses Nintendo LZ11 data, the compression on the game's .LZ archives.</summary>
/// <remarks>The header is the type byte 0x11 and a 24-bit size; a zero size means a 32-bit size follows.</remarks>
public static class Lz11
{
    public static bool IsLz11(ReadOnlySpan<byte> data) => data.Length > 4 && data[0] == 0x11;

    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (!IsLz11(data)) throw new InvalidDataException("Not LZ11 data.");
        int size = data[1] | data[2] << 8 | data[3] << 16, p = 4;
        if (size == 0) { size = BitConverter.ToInt32(data[4..8]); p = 8; }
        var output = new byte[size];
        int o = 0;
        while (o < size && p < data.Length)
        {
            int flags = data[p++];
            for (int bit = 7; bit >= 0 && o < size && p < data.Length; bit--)
            {
                if ((flags >> bit & 1) == 0) { output[o++] = data[p++]; continue; }
                (int length, int distance) = ReadReference(data, ref p);
                for (int i = 0; i < length && o < size; i++, o++) output[o] = output[o - distance];
            }
        }
        return output;
    }

    // The top nibble of the first byte selects one of three reference encodings.
    static (int Length, int Distance) ReadReference(ReadOnlySpan<byte> d, ref int p)
    {
        int b0 = d[p], top = b0 >> 4;
        if (top == 0)
        {
            int length = ((b0 & 0xF) << 4 | d[p + 1] >> 4) + 0x11, distance = ((d[p + 1] & 0xF) << 8 | d[p + 2]) + 1;
            p += 3;
            return (length, distance);
        }
        if (top == 1)
        {
            int length = ((b0 & 0xF) << 12 | d[p + 1] << 4 | d[p + 2] >> 4) + 0x111, distance = ((d[p + 2] & 0xF) << 8 | d[p + 3]) + 1;
            p += 4;
            return (length, distance);
        }
        int len = top + 1, dist = ((b0 & 0xF) << 8 | d[p + 1]) + 1;
        p += 2;
        return (len, dist);
    }
}
