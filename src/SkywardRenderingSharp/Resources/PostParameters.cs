using System.Numerics;
using RevolutionEggSharp;
using SkywardRenderingSharp.Archives;
using U8Sharp;

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

    public static PostParameters Read(U8File system)
    {
        var bloom = new EggBloom(system.Get("dat/default.bblm"));
        var paint = new EggPaint(system.Get("dat/default.bdfp"));
        // Layers are (radius, intensity) pairs; Blur2StageCount says how many are drawn (the base layer and the ones after it).
        Vector2[] stages = [new(bloom.BlurStrength, bloom.BlurIntensity), new(bloom.Blur2Stage1Strength, bloom.Blur2Stage1Intensity), new(bloom.Blur2Stage2Strength, bloom.Blur2Stage2Intensity)];
        return new PostParameters(bloom.ThresholdIntensity, Rgba(bloom.TintColor).AsVector3(), paint.Far, paint.Near, Rgba(bloom.ThresholdColor),
            stages.Take(Math.Min((int)bloom.Blur2StageCount, stages.Length)).ToList());
    }

    static Vector4 Rgba(uint c) => new Vector4(c >> 24 & 255, c >> 16 & 255, c >> 8 & 255, c & 255) / 255f;
}
