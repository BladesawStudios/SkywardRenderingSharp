using System.Buffers.Binary;
using System.Numerics;
using SkywardRenderingSharp.Archives;

namespace SkywardRenderingSharp.Resources;

/// <summary>The post-processing parameters stored in the game's System.arc: bloom (.bblm) and paint (.bdfp).</summary>
/// <remarks>The bloom block's use is read from the engine's setup code (0xee018c, 0xee0e70): threshold in video range times the mask colour, a mask scale of 2, and up to three
/// (radius, intensity) layers; the paint pairs follow the same shape as the palette's.
/// The depth-of-field file (.bdof) ships only for the opening cutscene, so the default frame has no depth of field.</remarks>
public sealed record PostParameters(float BloomThreshold, Vector3 BloomTint, Vector2 PaintFar, Vector2 PaintNear, Vector4 BloomMaskColor, IReadOnlyList<Vector2> BloomLayers)
{
    public static PostParameters Default { get; } = new(0.55f, new Vector3(1f, 0.94f, 0.71f), new Vector2(3300, 3300), new Vector2(30, 20), Vector4.One, [new(0, 1), new(0.5f, 1)]);

    /// <summary>The mask threshold per channel, as the engine computes it: the file's threshold mapped to video range ((t * 219 + 16) / 255), times the mask colour (+0x14).</summary>
    public Vector4 BloomMaskThreshold => (BloomThreshold * 219f + 16f) / 255f * BloomMaskColor;

    public static PostParameters Read(U8Archive system)
    {
        var bloom = system.Get("dat/default.bblm").Span;
        var paint = system.Get("dat/default.bdfp").Span;
        if (!bloom[..4].SequenceEqual("PBLM"u8) || !paint[..4].SequenceEqual("BDFP"u8)) throw new InvalidDataException("Unexpected post parameter files.");
        float F(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadSingleBigEndian(s[o..]);
        uint tint = BinaryPrimitives.ReadUInt32BigEndian(bloom[0x18..]);
        uint mask = BinaryPrimitives.ReadUInt32BigEndian(bloom[0x14..]);
        // Layers are (radius, intensity) float pairs at 0x20, 0x40 and 0x60; the byte at 0x81 says how many are drawn (the base layer and the ones after it).
        var layers = new List<Vector2>();
        for (int i = 0; i < Math.Min((int)bloom[0x81], 3); i++) layers.Add(new Vector2(F(bloom, 0x20 + 0x20 * i), F(bloom, 0x24 + 0x20 * i)));
        return new PostParameters(F(bloom, 0x10), new Vector3(tint >> 24 & 255, tint >> 16 & 255, tint >> 8 & 255) / 255f,
            new Vector2(F(paint, 0x10), F(paint, 0x14)), new Vector2(F(paint, 0x18), F(paint, 0x1C)),
            new Vector4(mask >> 24 & 255, mask >> 16 & 255, mask >> 8 & 255, mask & 255) / 255f, layers);
    }
}
