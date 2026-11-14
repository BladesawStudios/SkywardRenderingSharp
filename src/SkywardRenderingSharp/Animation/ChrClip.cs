using System.Numerics;
using BresSharp;
using BresSharp.Anim;
using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Animation;

/// <summary>A bone animation (CHR0): per-bone scale, rotation and translation sampled by frame, read through BresSharp.</summary>
/// <remarks>A component group the clip leaves out is the identity (scale one, no rotation, no translation); a bone the clip does not name keeps its bind transform.</remarks>
public sealed class ChrClip
{
    readonly Dictionary<string, Chr0Bone> _nodes;

    public string Name { get; }
    public int FrameCount { get; }
    public bool Loops { get; }

    ChrClip(Chr0Animation animation)
    {
        Name = animation.Name; FrameCount = animation.FrameCount; Loops = animation.Loop;
        _nodes = animation.Bones.ToDictionary(b => b.Name, StringComparer.Ordinal);
    }

    public static ChrClip Read(BresSubfile chr0) => new(new Chr0Animation(chr0));

    public bool HasNode(string bone) => _nodes.ContainsKey(bone);

    /// <summary>The bone's local matrix at a frame; bones the clip does not animate keep their bind transform.</summary>
    public Matrix4x4 Local(Bone bone, float frame)
    {
        if (!_nodes.TryGetValue(bone.Name, out var node)) return bone.LocalMatrix;
        if (Loops && FrameCount > 0) frame %= FrameCount;
        return Bone.LocalTransform(node.ScaleAt(frame), node.RotationAt(frame), node.TranslationAt(frame));
    }

    public Pose Evaluate(IReadOnlyList<Bone> bones, float frame) => Pose.FromLocals(bones, b => Local(b, frame));
}
