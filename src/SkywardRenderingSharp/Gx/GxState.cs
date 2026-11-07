using SkywardRenderingSharp.Brres;

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

    // Executes every command in [start, end); stops at the first byte that is not a state command.
    public int Run(BigEndian file, int start, int end)
    {
        int p = start;
        while (p < end)
        {
            byte op = file.U8(p);
            if (op == 0) { p++; continue; }
            if (op == 0x61) { WriteBp(file.U8(p + 1), file.U32(p + 1) & 0xFFFFFF); p += 5; }
            else if (op == 0x08) { Cp[file.U8(p + 1)] = file.U32(p + 2); p += 6; }
            else if (op == 0x10) p = LoadXf(file, p);
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

    int LoadXf(BigEndian file, int p)
    {
        int count = file.U16(p + 1) + 1, address = file.U16(p + 3);
        for (int i = 0; i < count; i++) Xf[address + i] = file.U32(p + 5 + 4 * i);
        return p + 5 + 4 * count;
    }
}
