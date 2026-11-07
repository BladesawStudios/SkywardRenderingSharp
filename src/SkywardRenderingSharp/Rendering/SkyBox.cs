using System.Numerics;
using SkywardRenderingSharp.Lighting;

namespace SkywardRenderingSharp.Rendering;

/// <summary>A sky box object (Vrbox00, Vrbox03, ...): a huge dome with cloud layers, drawn first and centred on the eye so it never gets closer.</summary>
public sealed class SkyBox
{
    readonly List<LoadedModel> _models;

    public string Name { get; }

    internal SkyBox(string name, List<LoadedModel> models) { Name = name; _models = models; }

    public IReadOnlyList<LoadedModel> Models => _models;

    /// <summary>The instances for an eye position. With a palette slot, the sky gradient material takes the slot's sky colour.</summary>
    public IReadOnlyList<ModelInstance> Instances(Vector3 eye, PaletteSlot? palette = null) =>
        _models.Select(m => new ModelInstance(m, Matrix4x4.CreateTranslation(eye), ColorOverrides: palette is null ? null : Tint(m, palette))).ToList();

    // The sky materials ship pure red placeholder colours the engine overwrites from the stage palette. The gradient material (`*_of_sky*`) blends constant colour 3 with register 3 (horizon to top);
    // every other red colour (constants or registers, which the cloud layers shade with) takes the horizon colour.
    internal static Dictionary<string, IReadOnlyDictionary<int, Vector4>> Tint(LoadedModel model, PaletteSlot palette) =>
        model.Model.Materials.ToDictionary(m => m.Name, m =>
        {
            var colors = new Dictionary<int, Vector4>();
            for (int i = 0; i < 4; i++)
            {
                if (IsPlaceholder(m.Colors.Registers[i])) colors[i] = new(palette.SkyHorizon, m.Colors.Registers[i].W);
                if (IsPlaceholder(m.Colors.Konst[i])) colors[4 + i] = new(palette.SkyHorizon, m.Colors.Konst[i].W);
            }
            if (m.Name.Contains("_of_sky", StringComparison.Ordinal)) colors[3] = new(palette.SkyTop, m.Colors.Registers[3].W);
            return (IReadOnlyDictionary<int, Vector4>)colors;
        });

    static bool IsPlaceholder(Vector4 c) => c.X > 0.95f && c.Y < 0.05f && c.Z < 0.05f;
}
