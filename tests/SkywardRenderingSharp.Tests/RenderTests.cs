using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkywardRenderingSharp.Rendering;

namespace SkywardRenderingSharp.Tests;

/// <summary>Renders real models offscreen and checks properties that regressions break: complete geometry, no missing textures, working animation and sky.</summary>
public class RenderTests
{
    const int Size = 480;

    static byte[] Draw(Func<SkywardRenderer, (Camera Camera, IReadOnlyList<ModelInstance> World, IReadOnlyList<ModelInstance>? Sky)> scene, bool post = false)
    {
        using var window = Window.Create(WindowOptions.Default with
        {
            Size = new(Size, Size), IsVisible = false, API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 3)),
        });
        window.Initialize();
        using var gl = GL.GetApi(window.GLContext);
        using var renderer = new SkywardRenderer(gl, Romfs.Rom!);
        renderer.Settings.Paint = renderer.Settings.Bloom = post;
        var (camera, world, sky) = scene(renderer);
        renderer.Render(camera, world, Size, Size, sky);
        return renderer.ReadPixels();
    }

    static (Camera, IReadOnlyList<ModelInstance>, IReadOnlyList<ModelInstance>?) Link(SkywardRenderer r, SkywardRenderingSharp.Rendering.SceneObject? keep = null,
        string gaze = "", float gazeFrame = 0)
    {
        var obj = keep ?? r.LoadObject("Alink");
        var pose = obj.Animations.Clip("BattleReady")!.Evaluate(obj.Main.Model.Bones, 6);
        var face = obj.Animations.Pattern("Fmaba01")?.Evaluate(0);
        var eyes = gaze == "" ? null : obj.Animations.Srt(gaze)!.Evaluate(gazeFrame);
        var instances = obj.Instances(Matrix4x4.CreateRotationY(0.35f), pose, face, eyes);
        var (min, max) = (obj.Main.BoundsMin, obj.Main.BoundsMax);
        var center = (min + max) / 2;
        return (new Camera(new Vector3(0, center.Y + 15, Vector3.Distance(min, max) * 1.6f), new Vector3(0, center.Y, 0)), instances, null);
    }

    static (int Foreground, int Magenta) Coverage(byte[] rgba)
    {
        int fg = 0, magenta = 0;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i] == 255 && rgba[i + 1] == 0 && rgba[i + 2] == 255) magenta++;
            // The default sky is (158, 199, 235); anything else is a model.
            if (Math.Abs(rgba[i] - 158) > 6 || Math.Abs(rgba[i + 1] - 199) > 6 || Math.Abs(rgba[i + 2] - 235) > 6) fg++;
        }
        return (fg, magenta);
    }

    [RomFact]
    public void LinkRendersCompleteWithoutMissingTextures()
    {
        var (foreground, magenta) = Coverage(Draw(r => Link(r)));
        Assert.Equal(0, magenta);
        // A complete Link (head, face, hands, boots) covers well over this; a missing head or collapsed limbs does not.
        Assert.InRange(foreground, 14000, 140000);
    }

    [RomFact]
    public void EyeGazeMovesPixelsOnlyAroundTheEyes()
    {
        byte[] a = [], b = [];
        Draw(r => { var o = r.LoadObject("Alink"); a = Draw2(r, o, 0); b = Draw2(r, o, 30); return Link(r, o); });
        int changed = Enumerable.Range(0, a.Length / 4).Count(i => a[i * 4] != b[i * 4] || a[i * 4 + 1] != b[i * 4 + 1]);
        Assert.InRange(changed, 5, 2500);

        static byte[] Draw2(SkywardRenderer r, SkywardRenderingSharp.Rendering.SceneObject o, float frame)
        {
            var (cam, world, _) = Link(r, o, "F_Notice", frame);
            r.Render(cam, world, Size, Size);
            return r.ReadPixels();
        }
    }

    [RomFact]
    public void SkyloftRendersTexturedWithSkyAndClouds()
    {
        var pixels = Draw(r =>
        {
            var stage = r.LoadStage("F000");
            Assert.True(stage.HasSky);
            Assert.All(stage.Models, m => Assert.Empty(m.MissingTextures));
            r.Lights = stage.Lights;
            var eye = new Vector3(2000, 1500, 16000);
            return (new Camera(eye, new Vector3(2000, 300, -3000), 55, 50, 200000), stage.World(), stage.Sky(eye));
        });
        Assert.Equal(0, Coverage(pixels).Magenta);
        // Sky and clouds fill the top: not the flat clear colour.
        int skyDistinct = Enumerable.Range(0, Size * 40).Select(i => (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2])).Distinct().Count();
        Assert.True(skyDistinct > 100);
    }
}
