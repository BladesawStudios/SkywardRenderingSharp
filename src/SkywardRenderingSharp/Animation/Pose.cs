using System.Numerics;
using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Animation;

/// <summary>A skeleton posed for a frame: each bone's world matrix and the skin matrix (inverse bind, then animated world) the shader blends.</summary>
/// <remarks>Bones are evaluated in index order, which lists parents before children.</remarks>
public sealed class Pose
{
    public const int MaxBones = 64;

    public Matrix4x4[] World { get; }
    public Matrix4x4[] Skin { get; }

    Pose(Matrix4x4[] world, Matrix4x4[] skin) { World = world; Skin = skin; }

    public static Pose Bind(IReadOnlyList<Bone> bones) => FromLocals(bones, b => b.LocalMatrix);

    public static Pose FromLocals(IReadOnlyList<Bone> bones, Func<Bone, Matrix4x4> local)
    {
        if (bones.Count > MaxBones) throw new NotSupportedException($"A skeleton of {bones.Count} bones exceeds the {MaxBones} the shader skins.");
        var world = new Matrix4x4[bones.Count];
        var skin = new Matrix4x4[bones.Count];
        foreach (var b in bones)
        {
            world[b.Index] = b.Parent < 0 ? local(b) : local(b) * world[b.Parent];
            skin[b.Index] = b.InverseBind * world[b.Index];
        }
        return new Pose(world, skin);
    }
}
