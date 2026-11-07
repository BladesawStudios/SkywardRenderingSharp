using System.Buffers.Binary;
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

/// <summary>A stage's <c>palette.spf</c> (lights, sky, paint), <c>palette.sff</c> (fog) and <c>palette.eaf</c> (hemisphere ambient): twenty slots each.</summary>
/// <remarks>Slots a stage does not use hold a default template (words 117-118 are 00fff0b4 and ff010000; <see cref="PaletteSlot.IsTemplate"/>). A stage's layers pick slots by number; the file does not say what each stands for, but the used ones differ in sky colour (blue sky, peach, pale or overcast), paint range and light layout. An spf slot is 130 big-endian words after a one word header; the words used are 1 key light, 3 ambient, 7 fill light, 14 third light, 119-122 paint distances
/// and 128-129 sky colours. An sff slot is three (colour, near, far) fog layers. An eaf slot is 256 bytes whose first two colours are the sky and ground ambient.
/// The remaining words (shadow and rim parameters, further light sets) are not decoded. What each slot number means (time of day or weather) is not known.</remarks>
public sealed class StagePalette
{
    public const int Slots = 20;
    const int SpfSlotBytes = 520, SffSlotBytes = 36, EafSlotBytes = 256, Header = 4;

    readonly byte[] _spf, _sff, _eaf;

    StagePalette(byte[] spf, byte[] sff, byte[] eaf) { _spf = spf; _sff = sff; _eaf = eaf; }

    public static StagePalette Open(ReadOnlySpan<byte> spf, ReadOnlySpan<byte> sff, ReadOnlySpan<byte> eaf = default)
    {
        if (spf.Length != Header + Slots * SpfSlotBytes) throw new InvalidDataException("Not a stage palette (.spf).");
        if (sff.Length != Header + Slots * SffSlotBytes) throw new InvalidDataException("Not a stage fog file (.sff).");
        if (eaf.Length != 0 && eaf.Length != Slots * EafSlotBytes) throw new InvalidDataException("Not a stage environment file (.eaf).");
        return new StagePalette(spf.ToArray(), sff.ToArray(), eaf.ToArray());   // an empty eaf (some stages have none) leaves the hemisphere colours at the ambient
    }

    public PaletteSlot Slot(int index)
    {
        if (index is < 0 or >= Slots) throw new ArgumentOutOfRangeException(nameof(index));
        int s = Header + index * SpfSlotBytes, f = Header + index * SffSlotBytes, e = index * EafSlotBytes;
        Vector3 C(byte[] d, int at) => Rgb(BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at)));
        float F(byte[] d, int at) => BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(at));
        var fog = Enumerable.Range(0, 3).Select(i => new FogLayer(C(_sff, f + i * 12), F(_sff, f + i * 12 + 4), F(_sff, f + i * 12 + 8))).ToList();
        // Slots come in two layouts: the leading six colours (words 0-5) are lights in one and something else in the other, told apart by word 6 (0.35 or 0.3).
        // Only the first layout's key light and ambient are read from the file; the other uses a white key and the hemisphere ambient, and its leading colours are the sky instead: word 0 the zenith and word 3 the horizon
        // (a deep blue and a cyan in Skyloft's daytime slots; mixed along the dome they give the game's sky blue), where the first layout keeps its sky at words 128 and 129.
        bool lights = MathF.Abs(F(_spf, s + 24) - 0.35f) < 1e-3f;
        var hemisphereSky = _eaf.Length == 0 ? C(_spf, s + 12) : C(_eaf, e);
        var hemisphereGround = _eaf.Length == 0 ? C(_spf, s + 12) : C(_eaf, e + 4);
        return new PaletteSlot(index, lights ? C(_spf, s + 4) : Vector3.One, C(_spf, s + 28), C(_spf, s + 56), lights ? C(_spf, s + 12) : (hemisphereSky + hemisphereGround) / 2,
            lights ? C(_spf, s + 512) : Vector3.Lerp(C(_spf, s + 0), C(_spf, s + 12), 0.4f), lights ? C(_spf, s + 516) : C(_spf, s + 12), Ordered(F(_spf, s + 476), F(_spf, s + 480)), new Vector2(F(_spf, s + 484), F(_spf, s + 488)), fog, hemisphereSky, hemisphereGround,
            BinaryPrimitives.ReadUInt32BigEndian(_spf.AsSpan(s + 468)) == 0x00fff0b4 && BinaryPrimitives.ReadUInt32BigEndian(_spf.AsSpan(s + 472)) == 0xff010000);
    }

    // A far pair is stored start then end but some slots list it largest first; the ramp always runs from the smaller to the larger.
    static Vector2 Ordered(float a, float b) => new(MathF.Min(a, b), MathF.Max(a, b));

    static Vector3 Rgb(uint c) => new((c >> 24 & 255) / 255f, (c >> 16 & 255) / 255f, (c >> 8 & 255) / 255f);
}
