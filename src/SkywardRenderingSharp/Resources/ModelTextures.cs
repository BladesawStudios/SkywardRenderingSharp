using SkywardRenderingSharp.Brres;
using SkywardRenderingSharp.Textures;

namespace SkywardRenderingSharp.Resources;

/// <summary>Resolves a model's texture names to decoded images: .xtx for the 1x1 stubs, Wii formats for inline ones, flat defaults for lightmaps.</summary>
/// <remarks>The LmCha*, LmBG* lightmaps are built by the engine from .blmap records whose layout is not decoded; see the TODO in <see cref="Find"/>.</remarks>
public sealed class ModelTextures
{
    readonly Dictionary<string, Lazy<RgbaImage>> _images = new(StringComparer.Ordinal);

    readonly ModelTextures? _fallback;

    /// <summary>Textures of a model file; names it does not hold resolve through <paramref name="fallback"/> (a room's textures live in its stage's pool).</summary>
    public ModelTextures(ModelArchive archive, ModelTextures? fallback = null, IReadOnlyDictionary<string, RgbaImage>? replacements = null)
    {
        _fallback = fallback;
        var f = archive.Bres.Data;
        int xtxIndex = 0;
        foreach (var (name, offset) in archive.Bres.Textures)
        {
            int width = f.U16(offset + 0x1C), height = f.U16(offset + 0x1E);
            if (width == 1 && height == 1 && archive.Xtx is { } xtx)
            {
                int index = xtxIndex++;
                _images[name] = new Lazy<RgbaImage>(() => xtx.Decode(index));
            }
            else
            {
                int at = offset;
                _images[name] = new Lazy<RgbaImage>(() => WiiTexture.Decode(f.Span[(at + (int)f.U32(at + 0x10))..], width, height, (WiiTexture.Format)f.I32(at + 0x20)));
            }
        }
        // The engine binds its Gra* gradient textures in place of the Lm* placeholders models carry (see SkywardRom.GradientRamps).
        if (replacements is not null) foreach (var (name, image) in replacements) _images[name] = new Lazy<RgbaImage>(image);
    }

    public RgbaImage? Find(string name)
    {
        if (_images.TryGetValue(name, out var lazy)) return lazy.Value;
        if (_fallback?.Find(name) is { } shared) return shared;
        // TODO: the .blmap LTEX records are not sampled ramps (the Add records read as white and overexpose every material), so lightmaps stay flat: multiply white, add black.
        if (name.StartsWith("Lm", StringComparison.Ordinal)) return name.EndsWith("Add", StringComparison.Ordinal) ? RgbaImage.Solid(0, 0, 0, 255) : RgbaImage.Solid(255, 255, 255, 255);
        return null;
    }
}
