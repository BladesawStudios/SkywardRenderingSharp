using System.Buffers.Binary;

namespace SkywardRenderingSharp.Gx;

/// <summary>The GX BP (pixel pipeline), XF (transform) and CP (vertex) register files, as written by a command stream.</summary>
/// <remarks>BP register 0xFE is the write mask for the next BP write, as on the hardware. Only registers a stream writes are meaningful.</remarks>
public sealed class GxState
{
    public readonly uint[] Bp = new uint[256];
    public readonly Dictionary<int, uint> Xf = [];
    public readonly Dictionary<int, uint> Cp = [];

    // Registers 0xE0..0xE7 hold two files (colour registers and constants, bit 23 selects), so each write is kept by type: [register, type].
    public readonly uint[,] TevColorWrites = new uint[8, 2];

    uint _mask = 0xFFFFFF;

    // The blend register's colour and alpha update bits (3, 4) default to on; materials write the register around them with a mask.
    public GxState() { Bp[0x41] = 0x18; }

    public uint Xfr(int address) => Xf.GetValueOrDefault(address);

    // Executes every command in the stream; stops at the first byte that is not a state command.
    public int Run(ReadOnlySpan<byte> code)
    {
        int p = 0;
        while (p < code.Length)
        {
            byte op = code[p];
            if (op == 0) { p++; continue; }
            if (op == 0x61) { WriteBp(code[p + 1], U32(code, p + 1) & 0xFFFFFF); p += 5; }
            else if (op == 0x08) { Cp[code[p + 1]] = U32(code, p + 2); p += 6; }
            else if (op == 0x10) p = LoadXf(code, p);
            else break;
        }
        return p;
    }

    void WriteBp(int register, uint value)
    {
        if (register == 0xFE) { _mask = value; return; }
        uint merged = (Bp[register] & ~_mask) | (value & _mask);
        Bp[register] = merged;
        if (register is >= 0xE0 and <= 0xE7) TevColorWrites[register - 0xE0, merged >> 23 & 1] = merged;
        _mask = 0xFFFFFF;
    }

    int LoadXf(ReadOnlySpan<byte> code, int p)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(code[(p + 1)..]) + 1, address = BinaryPrimitives.ReadUInt16BigEndian(code[(p + 3)..]);
        for (int i = 0; i < count; i++) Xf[address + i] = U32(code, p + 5 + 4 * i);
        return p + 5 + 4 * count;
    }

    static uint U32(ReadOnlySpan<byte> code, int at) => BinaryPrimitives.ReadUInt32BigEndian(code[at..]);
}
