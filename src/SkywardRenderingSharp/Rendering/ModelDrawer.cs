using System.Numerics;
using Silk.NET.OpenGL;
using SkywardRenderingSharp.Gpu;
using SkywardRenderingSharp.Lighting;
using SkywardRenderingSharp.Textures;

namespace SkywardRenderingSharp.Rendering;

/// <summary>A model placed in the world, optionally posed (skinned), with some draws hidden and some materials' first texture swapped by name.</summary>
/// <remarks>A DrawFilter hides draws it rejects, as the game's visibility animations hide alternate parts; TextureOverrides is what a texture
/// pattern animation produces, and TextureSrt what a texture SRT animation produces (material name, then texture slot, to its transform).
/// ColorOverrides replace a material's TEV colour registers 0 to 3 and constant colours (keys 4 to 7), as the engine does for the sky from the stage palette.</remarks>
public sealed record ModelInstance(LoadedModel Model, Matrix4x4 World, Animation.Pose? Pose = null, IReadOnlyDictionary<string, string>? TextureOverrides = null,
    Func<Model.DrawCall, Model.Material, bool>? DrawFilter = null, IReadOnlyDictionary<string, IReadOnlyDictionary<int, Animation.TextureSrt>>? TextureSrt = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, Vector4>>? ColorOverrides = null);

/// <summary>Draws model instances with the uber shader: opaque polygons first, then translucent ones by priority.</summary>
/// <remarks>Binds each material's GX state per draw. A texture the model does not have shows as magenta.</remarks>
sealed class ModelDrawer : IDisposable
{
    readonly GL _gl;
    readonly GlProgram _program;
    readonly MaterialBinder _binder = new();
    readonly PixelStateApplier _state;
    readonly SamplerCache _samplers;
    readonly GlTexture _missing;

    public ModelDrawer(GL gl, SamplerCache samplers)
    {
        _gl = gl; _samplers = samplers;
        _program = GlProgram.FromResources(gl, "uber.vert", "uber.frag");
        _state = new PixelStateApplier(gl);
        _missing = new GlTexture(gl, RgbaImage.Solid(255, 0, 255, 255));
    }

    public void Draw(IReadOnlyList<ModelInstance> instances, Matrix4x4 view, Matrix4x4 projection, LightRig lights, bool disableCulling)
    {
        _program.Use();
        _program.Set("uProjection", projection);
        _program.SetInts("uTexture", [0, 1, 2, 3, 4, 5, 6, 7]);
        BindLights(lights, view);
        foreach (bool translucent in new[] { false, true })
        {
            foreach (var instance in instances) DrawInstance(instance, view, lights, translucent, disableCulling);
        }
    }

    void BindLights(LightRig rig, Matrix4x4 view)
    {
        var dirs = new float[LightRig.MaxLights * 3]; var colors = new float[LightRig.MaxLights * 3];
        for (int i = 0; i < rig.Count; i++)
        {
            Vector3.Normalize(Vector3.TransformNormal(rig.Directions[i], view)).CopyTo(dirs, i * 3);
            rig.Colors[i].CopyTo(colors, i * 3);
        }
        _program.SetVec3s("uLightDirection", dirs); _program.SetVec3s("uLightColor", colors);
    }

    void DrawInstance(ModelInstance instance, Matrix4x4 view, LightRig lights, bool translucent, bool disableCulling)
    {
        var model = instance.Model.Model;
        var modelView = instance.World * view;
        Matrix4x4.Invert(modelView, out var inverse);
        _program.Set("uModelView", modelView);
        _program.Set3x3("uNormalMatrix", Matrix4x4.Transpose(inverse));
        BindSkin(instance.Pose);
        var draws = Enumerable.Range(0, model.Draws.Count)
            .Where(i => model.Draws[i].Translucent == translucent && (instance.DrawFilter?.Invoke(model.Draws[i], model.Materials[model.Draws[i].Material]) ?? true))
            .OrderBy(i => model.Draws[i].Priority);
        foreach (int i in draws)
        {
            var material = model.Materials[model.Draws[i].Material];
            _state.Apply(material.Pixel, disableCulling ? 0 : material.CullMode);
            _binder.Bind(_program, material, lights.Mask, lights.Ambient, instance.TextureSrt?.GetValueOrDefault(material.Name), instance.ColorOverrides?.GetValueOrDefault(material.Name));
            BindLayers(instance.Model, material, instance.TextureOverrides);
            instance.Model.DrawMesh(i);
        }
    }

    // Models without a pose draw in bind pose: identity skin matrices.
    void BindSkin(Animation.Pose? pose)
    {
        var skin = new Matrix4x4[Animation.Pose.MaxBones];
        Array.Fill(skin, Matrix4x4.Identity);
        pose?.Skin.CopyTo(skin, 0);
        _program.SetMatrices("uSkin", skin);
    }

    void BindLayers(LoadedModel model, Model.Material material, IReadOnlyDictionary<string, string>? overrides)
    {
        foreach (var layer in material.Layers)
        {
            if (layer.TexMap is < 0 or > 7) continue;
            string name = layer == material.Layers[0] && overrides is not null && overrides.TryGetValue(material.Name, out var swapped) ? swapped : layer.TextureName;
            (model.Texture(name) ?? _missing).Bind(layer.TexMap);
            _gl.BindSampler((uint)layer.TexMap, _samplers.Get(layer.WrapS, layer.WrapT, layer.MinFilter, layer.MagFilter));
        }
    }

    public void Dispose() { _program.Dispose(); _missing.Dispose(); }
}
