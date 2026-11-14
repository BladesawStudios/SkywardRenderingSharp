using BresSharp;
using SkywardRenderingSharp.Textures;
using TexSharp;
using XtxSharp;

namespace SkywardRenderingSharp.Resources;

/// <summary>Resolves a model's texture names to decoded images: the .xtx textures the BRRES hands over, its own Wii-format TEX0s, and flat defaults for lightmaps.</summary>
/// <remarks>A TEX0 whose word at 0x34 is set takes the next .xtx texture, in the BRRES's texture order (BresSharp's <c>XtxTextureSlots</c>); the rest decode from
/// their GX data. The LmCha*, LmBG* lightmaps are built by the engine from .blmap records whose layout is not decoded; see the TODO in <see cref="Find"/>.</remarks>
public sealed class ModelTextures
{
    readonly Dictionary<string, Lazy<RgbaImage>> _images = new(StringComparer.Ordinal);

    readonly ModelTextures? _fallback;

    /// <summary>Textures of a model file; names it does not hold resolve through <paramref name="fallback"/> (a room's textures live in its stage's pool).</summary>
    public ModelTextures(ModelArchive archive, ModelTextures? fallback = null, IReadOnlyDictionary<string, RgbaImage>? replacements = null)
    {
        _fallback = fallback;
        var slots = archive.Bres.XtxTextureSlots;
        for (int i = 0; i < slots.Count && archive.Xtx is { } xtx && i < xtx.Textures.Count; i++)
        {
            XtxTexture texture = xtx.Textures[i];
            _images[slots[i].Name] = new Lazy<RgbaImage>(() => new RgbaImage((int)texture.Width, (int)texture.Height, texture.ToRgba8()));
        }
        foreach (var file in archive.Bres["Textures(NW4R)"]?.Files.Where(f => !f.UsesXtx) ?? [])
        {
            var tex0 = new Tex0Texture(file);
            _images[file.Name] = new Lazy<RgbaImage>(() => new RgbaImage(tex0.Width, tex0.Height, GxTexture.ToRgba8((GxFormat)tex0.GxFormat, tex0.Data, tex0.Width, tex0.Height)));
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
