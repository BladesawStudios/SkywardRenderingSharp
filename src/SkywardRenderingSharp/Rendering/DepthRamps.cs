using System.Numerics;

namespace SkywardRenderingSharp.Rendering;

/// <summary>The constants of the game's depth ramps (Paint_NearFar, Paint_Far, Dof_FogToAlpha, Dof_MaskNear) for an eye-distance range.</summary>
/// <remarks>
/// The programs read <c>t = 1 / (bias - 1 + depth)</c> from the depth buffer and a ramp <c>t * scale - offset</c>. With
/// <c>bias = -near / (far - near)</c> and the OpenGL projection, <c>t = -z / K</c> with <c>K = far * near / (far - near)</c>, linear in
/// eye distance <c>z</c>, so a ramp from one distance to another maps exactly onto a scale and offset.
/// </remarks>
readonly record struct DepthRamps(float Bias, float K)
{
    const float MinimumWidth = 1e-3f;

    public static DepthRamps For(Camera camera) =>
        new(-camera.Near / (camera.Far - camera.Near), camera.Far * camera.Near / (camera.Far - camera.Near));

    // A ramp that is 0 at eye distance `from` and 1 at `to`, as (scale, offset). `from` may be beyond `to` for a ramp that rises toward the camera.
    public Vector2 Between(float from, float to)
    {
        float width = to - from;
        if (MathF.Abs(width) < MinimumWidth) width = width < 0 ? -MinimumWidth : MinimumWidth;
        return new(-K / width, from / width);
    }
}
