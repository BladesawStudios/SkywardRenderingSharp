using System.Numerics;
using SkywardRenderingSharp.Rendering;

namespace SkywardViewer;

/// <summary>A camera that orbits a target: drag to turn, drag with the pan button to slide, wheel to zoom.</summary>
sealed class OrbitCamera
{
    public Vector3 Target { get; set; }
    public float Yaw { get; set; } = 20;
    public float Pitch { get; set; } = 10;
    public float Distance { get; set; } = 500;
    public float FovYDegrees { get; set; } = 40;
    public float Near { get; private set; } = 10;
    public float Far { get; private set; } = 40000;

    Vector3 Offset
    {
        get
        {
            float yaw = Yaw * MathF.PI / 180f, pitch = Pitch * MathF.PI / 180f;
            return new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
        }
    }

    public Camera ToCamera() => new(Target + Offset * Distance, Target, FovYDegrees, Near, Far);

    public void Orbit(float dx, float dy)
    {
        Yaw -= dx * 0.3f;
        Pitch = Math.Clamp(Pitch + dy * 0.3f, -85f, 85f);
    }

    public void Pan(float dx, float dy)
    {
        var forward = -Offset;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        float scale = Distance * 0.0015f;
        Target += (-right * dx + up * dy) * scale;
    }

    public void Zoom(float wheel) => Distance = Math.Clamp(Distance * MathF.Pow(0.88f, wheel), Near * 2, Far * 0.9f);

    /// <summary>Frames a bounding box: looks at its centre from three radii away, with clip planes scaled to its size.</summary>
    public void Frame(Vector3 min, Vector3 max)
    {
        Target = (min + max) / 2;
        float radius = MathF.Max(Vector3.Distance(min, max) / 2, 1f);
        Distance = radius * 3.2f;
        Near = MathF.Max(radius * 0.01f, 1f);
        Far = radius * 60f;
    }
}
