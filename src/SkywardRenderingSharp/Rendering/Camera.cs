using System.Numerics;

namespace SkywardRenderingSharp.Rendering;

/// <summary>A perspective camera. The world is right-handed and Y-up, the game's own, in centimetre-scale units.</summary>
public sealed record Camera(Vector3 Position, Vector3 Target, float FovYDegrees = 40f, float Near = 10f, float Far = 40000f)
{
    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);

    // An OpenGL projection (clip z from -w to w) in System.Numerics' row-vector form.
    public Matrix4x4 Projection(float aspect)
    {
        float f = 1f / MathF.Tan(FovYDegrees * MathF.PI / 360f);
        return new Matrix4x4(f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (Far + Near) / (Near - Far), -1, 0, 0, 2 * Far * Near / (Near - Far), 0);
    }
}
