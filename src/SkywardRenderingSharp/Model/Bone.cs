using System.Numerics;

namespace SkywardRenderingSharp.Model;

/// <summary>A skeleton node: hierarchy, bind-pose local transform (scale, XYZ Euler degrees, translation), world transform and inverse bind.</summary>
/// <remarks>Parent is -1 for roots. LocalMatrix is scale, then rotation about X, Y, Z in that order, then translation (row-vector convention).</remarks>
public sealed record Bone(string Name, int Index, int Parent, Vector3 Scale, Vector3 RotationDegrees, Vector3 Translation, Matrix4x4 World, Matrix4x4 InverseBind)
{
    public Matrix4x4 LocalMatrix => LocalTransform(Scale, RotationDegrees, Translation);

    public static Matrix4x4 LocalTransform(Vector3 scale, Vector3 rotationDegrees, Vector3 translation)
    {
        const float rad = MathF.PI / 180f;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationX(rotationDegrees.X * rad) * Matrix4x4.CreateRotationY(rotationDegrees.Y * rad)
            * Matrix4x4.CreateRotationZ(rotationDegrees.Z * rad) * Matrix4x4.CreateTranslation(translation);
    }
}
