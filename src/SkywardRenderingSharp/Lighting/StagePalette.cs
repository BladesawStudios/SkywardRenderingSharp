using SkywardPaletteSharp;
using System.Numerics;

namespace SkywardRenderingSharp.Lighting;

/// <summary>One fog layer of a palette slot: colour and the eye distances where it starts and is complete.</summary>
public readonly record struct FogLayer(Vector3 Color, float Near, float Far);

/// <summary>One slot of a stage palette: the lights, ambient, sky, fog and painterly distances the game uses for one time of day or weather.</summary>
/// <remarks>The light colours are the ones that also appear in <c>default.blight</c> (the fill and third lights and the ambient match it exactly), so they replace its colours.
/// Paint distances are two pairs (far start and end, near start and end) in the shape of the BDFP file's four floats.</remarks>
public sealed record PaletteSlot(int Index, Vector3 KeyLight, Vector3 FillLight, Vector3 ThirdLight, Vector3 Ambient, Vector3 SkyTop, Vector3 SkyHorizon,
    Vector2 PaintFar, Vector2 PaintNear, IReadOnlyList<FogLayer> Fog, Vector3 HemisphereSky, Vector3 HemisphereGround, bool IsTemplate)
{
    /// <summary>A one line description for pickers: the sky colours, paint range and light layout.</summary>
    public string Describe() => IsTemplate ? "(unused template)" : $"{Mood}  sky {Hex(SkyTop)}/{Hex(SkyHorizon)}  paint {PaintFar.X:0}-{PaintFar.Y:0}";

    /// <summary>A name read from the sky colours: the file does not name its slots (the engine picks one by game state), so this only says what the colours look like.</summary>
    public string Mood => SkyHorizon.Z > SkyHorizon.X + 0.15f ? "blue sky" : SkyHorizon.X > SkyHorizon.Z + 0.05f ? "warm sky" : "pale sky";

    /// <summary>The slot between two slots: every colour and distance mixed linearly, as the engine's parameter blending does (0xee1c08, 0xee9c34) when the time of day or weather changes.</summary>
    public static PaletteSlot Lerp(PaletteSlot a, PaletteSlot b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var fog = a.Fog.Zip(b.Fog, (x, y) => new FogLayer(Vector3.Lerp(x.Color, y.Color, t), x.Near + (y.Near - x.Near) * t, x.Far + (y.Far - x.Far) * t)).ToList();
        return a with
        {
            KeyLight = Vector3.Lerp(a.KeyLight, b.KeyLight, t), FillLight = Vector3.Lerp(a.FillLight, b.FillLight, t), ThirdLight = Vector3.Lerp(a.ThirdLight, b.ThirdLight, t),
            Ambient = Vector3.Lerp(a.Ambient, b.Ambient, t), SkyTop = Vector3.Lerp(a.SkyTop, b.SkyTop, t), SkyHorizon = Vector3.Lerp(a.SkyHorizon, b.SkyHorizon, t),
            PaintFar = Vector2.Lerp(a.PaintFar, b.PaintFar, t), PaintNear = Vector2.Lerp(a.PaintNear, b.PaintNear, t), Fog = fog,
            HemisphereSky = Vector3.Lerp(a.HemisphereSky, b.HemisphereSky, t), HemisphereGround = Vector3.Lerp(a.HemisphereGround, b.HemisphereGround, t),
        };
    }

    static string Hex(Vector3 c) => $"#{(int)(c.X * 255 + 0.5f):X2}{(int)(c.Y * 255 + 0.5f):X2}{(int)(c.Z * 255 + 0.5f):X2}";
}

/// <summary>A stage's <c>palette.spf</c> (lights, sky, paint), <c>palette.sff</c> (fog) and <c>palette.eaf</c> (hemisphere ambient), read through SkywardPaletteSharp: twenty slots each.</summary>
/// <remarks>Slots a stage does not use hold a default template (words 117-118 are 00fff0b4 and ff010000; <see cref="PaletteSlot.IsTemplate"/>). A stage's layers pick slots by number; the file does not say what each stands for, but the used ones differ in sky colour (blue sky, peach, pale or overcast), paint range and light layout. An spf slot is one 130-word record; the words used are 1 key light, 3 ambient, 7 fill light, 14 third light, 119-122 paint distances
/// and 128-129 sky colours. An sff slot is three (colour, near, far) fog entries. The eaf holds 32 records of 160 bytes; how they pair with the slots is not known (most are white defaults, and their order does not follow the slots'), so the first record's first two colours, the stage's own, are the sky and ground ambient of every slot.
/// The remaining words (shadow and rim parameters, further light sets) are not decoded. What each slot number means (time of day or weather) is not known.</remarks>
public sealed class StagePalette
{
    public const int Slots = 20;
    const int FogLayers = 3;

    readonly PaletteFile _spf;
    readonly PaletteFogFile _sff;
    readonly PaletteEffectFile? _eaf;

    StagePalette(PaletteFile spf, PaletteFogFile sff, PaletteEffectFile? eaf) { _spf = spf; _sff = sff; _eaf = eaf; }

    // Some stages have no eaf; their hemisphere colours stay at the ambient.
    public static StagePalette Open(byte[] spf, byte[] sff, byte[]? eaf = null)
    {
        var palette = new StagePalette(PaletteFile.FromBinary(spf), PaletteFogFile.FromBinary(sff), eaf is { Length: > 0 } ? PaletteEffectFile.FromBinary(eaf) : null);
        if (palette._spf.Palettes.Count < Slots) throw new InvalidDataException("Not a stage palette (.spf).");
        if (palette._sff.Entries.Count < Slots * FogLayers) throw new InvalidDataException("Not a stage fog file (.sff).");
        return palette;
    }

    public PaletteSlot Slot(int index)
    {
        if (index is < 0 or >= Slots) throw new ArgumentOutOfRangeException(nameof(index));
        var spf = _spf.Palettes[index];
        Vector3 C(int word) => Rgb(spf.Color(word));
        var fog = Enumerable.Range(0, FogLayers).Select(i => _sff.Entries[index * FogLayers + i]).Select(f => new FogLayer(Rgb(f.Color), f.Near, f.Far)).ToList();
        // Slots come in two layouts: the leading six colours (words 0-5) are lights in one and something else in the other, told apart by word 6 (0.35 or 0.3).
        // Only the first layout's key light and ambient are read from the file; the other uses a white key and the hemisphere ambient, and its leading colours are the sky instead: word 0 the zenith and word 3 the horizon
        // (a deep blue and a cyan in Skyloft's daytime slots; mixed along the dome they give the game's sky blue), where the first layout keeps its sky at words 128 and 129.
        bool lights = MathF.Abs(spf.Float(6) - 0.35f) < 1e-3f;
        var effect = _eaf?.Records[0];
        var hemisphereSky = effect is null ? C(3) : Rgb(effect.Color(0));
        var hemisphereGround = effect is null ? C(3) : Rgb(effect.Color(1));
        return new PaletteSlot(index, lights ? C(1) : Vector3.One, C(7), C(14), lights ? C(3) : (hemisphereSky + hemisphereGround) / 2,
            lights ? C(128) : Vector3.Lerp(C(0), C(3), 0.4f), lights ? C(129) : C(3), Ordered(spf.Float(119), spf.Float(120)), new Vector2(spf.Float(121), spf.Float(122)), fog, hemisphereSky, hemisphereGround,
            spf.UInt(117) == 0x00fff0b4 && spf.UInt(118) == 0xff010000);
    }

    // A far pair is stored start then end but some slots list it largest first; the ramp always runs from the smaller to the larger.
    static Vector2 Ordered(float a, float b) => new(MathF.Min(a, b), MathF.Max(a, b));

    static Vector3 Rgb(uint c) => new((c >> 24 & 255) / 255f, (c >> 16 & 255) / 255f, (c >> 8 & 255) / 255f);
}
