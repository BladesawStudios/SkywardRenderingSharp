using System.Numerics;

namespace SkywardRenderingSharp.Rendering;

/// <summary>What a frame looks like beyond the models: the background, the painterly post chain and its distances.</summary>
/// <remarks>Distances are eye distances in world units. Paint far/near ramps and the bloom threshold come from the game's System.arc parameters.</remarks>
public sealed class RenderSettings
{
    // The clear colour in linear light (a light sky blue once encoded).
    public Vector3 SkyColor { get; set; } = new(0.34f, 0.58f, 0.83f);

    // Draws both faces of every polygon, ignoring the materials' cull modes.
    public bool DisableCulling { get; set; }

    public bool Paint { get; set; } = true;
    // How far the painted (dilated) image replaces the scene everywhere, as the game's Paint_Constant passes do with the alpha they output; added to the distance ramps.
    public float PaintConstant { get; set; } = 0.15f;
    public int PaintPasses { get; set; } = 3;
    public int PaintDownscale { get; set; } = 2;
    public Vector2 PaintFar { get; set; } = new(3300, 3300);
    public Vector2 PaintNear { get; set; } = new(30, 20);

    public bool Fog { get; set; }
    public Vector3 FogColor { get; set; } = Vector3.One;
    public Vector2 FogRange { get; set; } = new(2000, 100000);

    public bool DepthOfField { get; set; }
    public Vector2 DofFar { get; set; } = new(6000, 23000);
    // Pixels nearer than this eye distance are fully blurred (the game's Dof_MaskNear: a hard cut, floor(1 - ramp)); 0 turns the near blur off.
    public float DofNear { get; set; }

    public bool Bloom { get; set; } = true;
    public Vector4 BloomMaskThreshold { get; set; } = new(0.535f);
    public Vector2 BloomWeights { get; set; } = Vector2.One;
    public float BloomStrength { get; set; } = 2f;
    public Vector3 BloomTint { get; set; } = Vector3.One;
}
