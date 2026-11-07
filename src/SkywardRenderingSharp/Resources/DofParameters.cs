using System.Buffers.Binary;

namespace SkywardRenderingSharp.Resources;

/// <summary>The depth-of-field parameters of the game's .bdof file (System.arc dat/opning.bdof, the opening cutscene's).</summary>
/// <remarks>The engine's parser (0xee9ba0) copies the file's floats at 0x18 and 0x1c into the effect object's +0x40 and +0x44, which its depth conversion (0xee24a4) reads as the
/// focus distance and the width of the ramp around it: blur is none below <c>focus - width / 2</c> and complete beyond <c>focus + width / 2</c>. The cutscene's values are 23000 and 10,
/// a nearly hard cut-off. The next floats (0x20 and on) are kept raw, in file order.</remarks>
public sealed record DofParameters(float FarLimit, float FarWidth, IReadOnlyList<float> Values)
{
    public static DofParameters Read(ReadOnlySpan<byte> file)
    {
        if (!file[..4].SequenceEqual("PDOF"u8)) throw new InvalidDataException("Not a .bdof file.");
        var values = new List<float>();
        for (int o = 0x20; o + 4 <= file.Length; o += 4) values.Add(BinaryPrimitives.ReadSingleBigEndian(file[o..]));
        return new DofParameters(BinaryPrimitives.ReadSingleBigEndian(file[0x18..]), BinaryPrimitives.ReadSingleBigEndian(file[0x1C..]), values);
    }
}
