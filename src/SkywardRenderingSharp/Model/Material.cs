using System.Numerics;
using SkywardRenderingSharp.Gx;

namespace SkywardRenderingSharp.Model;

/// <summary>A material as the game's GX state describes it: texture layers, texgens, light channels, TEV stages and pixel state.</summary>
/// <remarks>This is everything the game turns into #defines for its shader template; the uber shader reads it as uniforms instead.</remarks>
public sealed record Material(string Name, bool IsTranslucent, int CullMode, IReadOnlyList<MaterialLayer> Layers,
    IReadOnlyList<TexGenInfo> TexGens, IReadOnlyList<ColorChannel> Channels, IReadOnlyList<TevStage> Stages,
    TevColors Colors, PixelState Pixel, IReadOnlyList<TextureMatrix> TextureMatrices, IReadOnlyList<Matrix4x4?> PostMatrices);

/// <summary>A texture slot's matrix data: SRT values for UV layers, and the effect matrix with its map mode for generated coordinates.</summary>
/// <remarks>MapMode: 0 standard UV, 1 environment (camera), 2 projection, 3 environment (light), 4 environment (specular). Rotation is in degrees.</remarks>
public sealed record TextureMatrix(Vector2 Scale, float Rotation, Vector2 Translation, int MapMode, int Camera, int Light, Matrix4x4 Effect)
{
    public static TextureMatrix Identity { get; } = new(Vector2.One, 0, Vector2.Zero, 0, -1, -1, Matrix4x4.Identity);
}
