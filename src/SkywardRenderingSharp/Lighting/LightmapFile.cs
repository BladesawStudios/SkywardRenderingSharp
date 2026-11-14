using System.Buffers.Binary;
using RevolutionEggSharp;
using SkywardRenderingSharp.Textures;

namespace SkywardRenderingSharp.Lighting;

/// <summary>The game's .blmap, read through RevolutionEggSharp: the lookup curves the engine turns into the LmCha*, LmBG* and other lightmap textures.</summary>
/// <remarks>Each LTEX entry ends in 128 pairs of floats; the first of each pair is the curve value. In the shared data the character
/// curves are flat apart from a few entries, which is why the lightmaps add no visible shading by default. Which input indexes a curve
/// (the engine's texture matrices) is not recovered, so the renderer does not apply them.</remarks>
public sealed class LightmapFile
{
    const int Samples = 128, CurveBytes = Samples * 8;

    readonly Dictionary<string, float[]> _curves = new(StringComparer.Ordinal);

    public LightmapFile(EggLightMap file)
    {
        foreach (var texture in file.Textures)
        {
            var curve = texture.Body[^CurveBytes..];
            var values = new float[Samples];
            for (int i = 0; i < Samples; i++) values[i] = BinaryPrimitives.ReadSingleBigEndian(curve[(8 * i)..]);
            _curves[texture.Name] = values;
        }
    }

    public IEnumerable<string> Names => _curves.Keys;

    public float[] Curve(string name) => _curves[name];

    /// <summary>The curve as a 128x1 greyscale image.</summary>
    public RgbaImage Image(string name)
    {
        var curve = _curves[name];
        var pixels = new byte[Samples * 4];
        for (int i = 0; i < Samples; i++) pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = pixels[i * 4 + 3] = (byte)Math.Clamp(curve[i] * 255f, 0f, 255f);
        return new RgbaImage(Samples, 1, pixels);
    }
}
