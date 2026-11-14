using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SkywardRenderingSharp.Shaders;

/// <summary>A compiled GL program from the game's shader cache (<c>Shaders/custom/*</c> and the entries of <c>Shaders.arc</c>): one Maxwell stage per shader.</summary>
/// <remarks>
/// The program is a 0x50-byte header, a 0x90-byte descriptor per stage and the stage blobs (magic 0x98761234). A stage's code sits at
/// blob + 0x880 behind a 0x30-byte preamble (magic 0x12345678), starting with the 0x50-byte shader program header. A stage whose
/// descriptor flags immediate constants keeps them (constant buffer 1) in the last 0x100 bytes of its blob. 20 bytes follow the program.
/// </remarks>
public sealed class NvProgramBinary
{
    const uint Magic = 0x19866891, StageMagic = 0x98761234, CodeMagic = 0x12345678;
    const int HeaderSize = 0x50, DescriptorSize = 0x90, CodeOffset = 0x880, CodePreamble = 0x30, ConstantsSize = 0x100;

    public IReadOnlyList<NvStage> Stages { get; }

    NvProgramBinary(List<NvStage> stages) => Stages = stages;

    public static bool IsProgram(ReadOnlySpan<byte> data) => data.Length >= HeaderSize && U32(data, 0) == Magic;

    public static NvProgramBinary Read(ReadOnlySpan<byte> data)
    {
        if (!IsProgram(data)) throw new InvalidDataException("Not an NV program binary.");
        int total = (int)U32(data, 0x44), first = (int)U32(data, 0x48), count = (int)U32(data, 0x4C);
        if (total > data.Length || count is < 1 or > 5 || HeaderSize + count * DescriptorSize > first)
            throw new InvalidDataException($"Bad program header (size 0x{total:X}, {count} stages).");

        var stages = new List<NvStage>(count);
        for (int s = 0; s < count; s++)
        {
            int descriptor = HeaderSize + s * DescriptorSize;
            int size = (int)U32(data, descriptor + 0x40), at = (int)U32(data, descriptor + 0x44);
            if (at < first || at + size > total || size < CodeOffset + CodePreamble + 0x50) throw new InvalidDataException($"Stage {s} lies outside the program.");
            ReadOnlySpan<byte> blob = data.Slice(at, size);
            if (U32(blob, 0) != StageMagic || U32(blob, CodeOffset) != CodeMagic) throw new InvalidDataException($"Stage {s} is not a code blob.");

            bool hasConstants = U32(data, descriptor + 0x6C) == 1;
            float[] constants = hasConstants ? MemoryMarshal.Cast<byte, float>(blob[^ConstantsSize..]).ToArray() : [];
            byte[] code = blob[(CodeOffset + CodePreamble)..].ToArray();
            stages.Add(new NvStage(KindOf(U32(code, 0)), code, constants));
        }
        return new NvProgramBinary(stages);
    }

    // The shader program header's first word: bits 0-4 are the type (1 vertex/tessellation/geometry, 2 pixel), bits 10-13 the stage.
    static NvStageKind KindOf(uint sph) => (sph & 0x1F) == 2 ? NvStageKind.Fragment : ((sph >> 10) & 0xF) switch
    {
        1 => NvStageKind.Vertex,
        2 => NvStageKind.TessellationControl,
        3 => NvStageKind.TessellationEvaluation,
        4 => NvStageKind.Geometry,
        _ => throw new InvalidDataException($"Unknown shader program header 0x{sph:X8}."),
    };

    static uint U32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt32LittleEndian(d[at..]);
}

public enum NvStageKind { Vertex, TessellationControl, TessellationEvaluation, Geometry, Fragment }

/// <summary>One stage of a program: its Maxwell code (shader program header first) and its immediate constants (constant buffer 1), empty when it has none.</summary>
public sealed record NvStage(NvStageKind Kind, byte[] Code, float[] Constants)
{
    public string Extension => Kind switch
    {
        NvStageKind.Vertex => "vert",
        NvStageKind.TessellationControl => "tesc",
        NvStageKind.TessellationEvaluation => "tese",
        NvStageKind.Geometry => "geom",
        _ => "frag",
    };
}
