using System.Numerics;
using SkywardRenderingSharp.Animation;
using SkywardRenderingSharp.Rendering;

namespace SkywardViewer;

/// <summary>Which animations an object is playing and where: a skeleton clip, a face pattern (expression) and a gaze (eye SRT) clip, each with its own frame.</summary>
sealed class Playback
{
    const float FramesPerSecond = 30f;

    public string Clip { get; set; } = "";
    public string Face { get; set; } = "";
    public string Gaze { get; set; } = "";
    public bool Playing { get; set; } = true;
    public float Speed { get; set; } = 1;
    public float ClipFrame { get; set; }
    public float FaceFrame { get; set; }
    public float GazeFrame { get; set; }

    public void Reset() { Clip = Face = Gaze = ""; ClipFrame = FaceFrame = GazeFrame = 0; }

    public void Advance(float seconds, SceneObject obj)
    {
        if (!Playing) return;
        float step = seconds * FramesPerSecond * Speed;
        ClipFrame = Wrap(ClipFrame + step, obj.Animations.Clip(Clip)?.FrameCount ?? 0);
        FaceFrame = Wrap(FaceFrame + step, obj.Animations.Pattern(Face)?.FrameCount ?? 0);
        GazeFrame = Wrap(GazeFrame + step, obj.Animations.Srt(Gaze)?.FrameCount ?? 0);
    }

    static float Wrap(float frame, int count) => count <= 0 ? 0 : frame % count;

    /// <summary>The instances that draw the object with the current clip, expression and gaze.</summary>
    public IReadOnlyList<ModelInstance> Instances(SceneObject obj)
    {
        var pose = obj.Animations.Clip(Clip)?.Evaluate(obj.Main.Model.Bones, ClipFrame) ?? obj.BindPose;
        var face = obj.Animations.Pattern(Face)?.Evaluate(FaceFrame);
        var gaze = obj.Animations.Srt(Gaze)?.Evaluate(GazeFrame);
        return obj.Instances(Matrix4x4.Identity, pose, face, gaze);
    }
}
