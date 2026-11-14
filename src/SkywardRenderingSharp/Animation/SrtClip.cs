using System.Numerics;
using BresSharp;
using BresSharp.Anim;

namespace SkywardRenderingSharp.Animation;

/// <summary>One texture slot's animated transform: scale, rotation (degrees) and translation in texture space.</summary>
public readonly record struct TextureSrt(Vector2 Scale, float Rotation, Vector2 Translation);

/// <summary>A texture SRT animation (SRT0): per material and texture slot, a scale, rotation and translation over time, read through BresSharp. This is how the eyes look around.</summary>
/// <remarks>Only the texture matrices are evaluated; the indirect ones are not used by the renderer.</remarks>
public sealed class SrtClip
{
    readonly Srt0Animation _animation;

    public string Name => _animation.Name;
    public int FrameCount => _animation.FrameCount;

    SrtClip(Srt0Animation animation) { _animation = animation; }

    public static SrtClip Read(BresSubfile srt0) => new(new Srt0Animation(srt0));

    /// <summary>For each animated material, the transform of each animated texture slot at the frame.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, TextureSrt>> Evaluate(float frame)
    {
        if (FrameCount > 0) frame %= FrameCount;
        return _animation.Materials.ToDictionary(m => m.Name, m => (IReadOnlyDictionary<int, TextureSrt>)m.Matrices.Where(x => !x.IsIndirect)
            .ToDictionary(x => x.Index, x => new TextureSrt(x.ScaleAt(frame), x.RotationAt(frame), x.TranslationAt(frame))));
    }
}
