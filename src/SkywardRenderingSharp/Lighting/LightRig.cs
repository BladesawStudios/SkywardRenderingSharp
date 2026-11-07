using System.Numerics;

namespace SkywardRenderingSharp.Lighting;

/// <summary>The lights a frame is lit with: up to eight directional lights in world space and an ambient colour.</summary>
/// <remarks>The game picks lights per stage through light sets in a scene file this library does not read;
/// <see cref="FromBlight"/> builds the stage-neutral rig from the first light objects of the shared .blight file.</remarks>
public sealed class LightRig
{
    public const int MaxLights = 8;

    // Stage defaults, tuned against the game's look: stage materials multiply texture, baked vertex colour and light by two, so the lit side wants about 0.6 and the shadow side about 0.35.
    // TODO: the engine's own scaling of the palette's light colours was not found; raw colours clip every stage to white.
    const float StageDiffuse = 0.4f, StageAmbient = 0.7f;

    public Vector3[] Directions { get; } = new Vector3[MaxLights];
    public Vector3[] Colors { get; } = new Vector3[MaxLights];
    public Vector3 Ambient { get; set; } = new(0.25f);

    public int Count { get; private set; }

    public void Add(Vector3 directionTowardsLight, Vector3 color)
    {
        if (Count == MaxLights) throw new InvalidOperationException("A rig holds eight lights.");
        Directions[Count] = Vector3.Normalize(directionTowardsLight);
        Colors[Count++] = color;
    }

    /// <summary>The rig characters are drawn with: no lights. The engine's character light set (SCN0 "Chara", lights Chara_00 to Chara_02) ships black, and character shading comes from the
    /// Gra* gradient ramps, so any light colour added on top washes the ramps out.</summary>
    public static LightRig Characters() => new() { Ambient = Vector3.Zero };

    // Builds a viewer rig from one light set of the shared .blight: its directional lights and ambient colour, normalised so the directional luma sum is `intensity`.
    // TODO: the game takes light colours and strengths per stage from the stage's .spf palette (see docs/formats.md); raw .blight colours sum far past 1 and clip to white,
    // so this stage-neutral rig stands in until stages carry their palette.
    public static LightRig FromBlight(LightFile file, int set = 0, float intensity = 0.4f, float ambientScale = 0.3f)
    {
        var rig = new LightRig();
        var directional = file.Set(set).Where(l => l.IsDirectional && l.Color >> 8 != 0).Take(MaxLights).ToList();
        float total = directional.Sum(l => Vector3.Dot(l.Rgb, new Vector3(0.299f, 0.587f, 0.114f)));
        float scale = total > 0 ? intensity / total : 0;
        foreach (var light in directional) rig.Add(light.Vector, light.Rgb * scale);
        if (file.Set(set).FirstOrDefault(l => l.IsAmbient) is { Color: not 0 } ambient) rig.Ambient = ambient.Rgb * intensity * ambientScale;
        return rig;
    }

    /// <summary>Builds the rig of a palette slot: the directions of the blight set's key, fill and third lights with the slot's colours and ambient, normalised as in <see cref="FromBlight"/>.</summary>
    public static LightRig FromPalette(LightFile file, PaletteSlot slot, float? intensity = 0.4f, float ambientScale = 0.3f, bool hemisphereAmbient = false)
    {
        var rig = new LightRig();
        var set = file.Set(0).Where(l => l.IsDirectional && l.Color >> 8 != 0).Take(3).ToList();
        var colors = new[] { slot.KeyLight, slot.FillLight, slot.ThirdLight };
        float total = colors.Take(set.Count).Sum(c => Vector3.Dot(c, new Vector3(0.299f, 0.587f, 0.114f)));
        float scale = total > 0 ? (intensity ?? StageDiffuse) / total : 0;   // no intensity: the stage defaults below
        for (int i = 0; i < set.Count; i++) rig.Add(set[i].Vector, colors[i] * scale);
        rig.Ambient = hemisphereAmbient && intensity is null ? (slot.HemisphereSky + slot.HemisphereGround) / 2 * StageAmbient : slot.Ambient * ((intensity ?? 0.4f) * ambientScale);
        return rig;
    }

    // Light enable bits for the channels of materials whose own mask is empty (the game sets it per draw).
    public uint Mask => (1u << Count) - 1;
}
