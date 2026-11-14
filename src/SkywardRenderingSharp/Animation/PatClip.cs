using BresSharp;
using BresSharp.Anim;

namespace SkywardRenderingSharp.Animation;

/// <summary>A texture pattern animation (PAT0): per material, which of the clip's textures is shown at each frame, read through BresSharp.</summary>
/// <remarks>This is how Link's eyes blink and change expression: the eye materials swap between the al_eye.N textures. Only the first
/// texture slot of each material is animated, which is all the face clips use.</remarks>
public sealed class PatClip
{
    readonly Pat0Animation _animation;

    public string Name => _animation.Name;
    public int FrameCount => _animation.FrameCount;

    PatClip(Pat0Animation animation) { _animation = animation; }

    public static PatClip Read(BresSubfile pat0) => new(new Pat0Animation(pat0));

    /// <summary>For each animated material, the texture name shown at the frame (a key holds until the next).</summary>
    public IReadOnlyDictionary<string, string> Evaluate(float frame)
    {
        var result = new Dictionary<string, string>();
        foreach (var material in _animation.Materials)
        {
            if (material.Slots.FirstOrDefault() is not { HasTexture: true } slot) continue;
            int texture = slot.At(frame).Texture;
            if (texture >= 0 && texture < _animation.TextureNames.Count) result[material.Name] = _animation.TextureNames[texture];
        }
        return result;
    }
}
