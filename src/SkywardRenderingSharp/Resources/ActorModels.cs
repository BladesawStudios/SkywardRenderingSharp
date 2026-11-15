using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkywardRenderingSharp.Resources;

/// <summary>One model a placed actor draws: an object archive's model, or one of the current stage's own when Archive is null.</summary>
public sealed record ActorPart(string? Archive, string Model);

/// <summary>Which models the game draws for a placed actor, by its name and parameters, as the actor's own code chooses them.</summary>
/// <remarks>
/// The table (Resources/actors.json) was read from the zeldaret/ss decompilation's actors and checked against the HD dump. An actor
/// picks a variant from a bit field of its params or raw angles, after the rewrites its code makes; a variant is the models drawn
/// together, main model first; a value with no variant takes the actor's fallback, if it has one. An actor the table does not list, or
/// lists as drawing nothing (tags, spawners, effects), has no parts, and neither has one whose value selects no variant. A backdrop is
/// scenery drawn far beyond the room, such as the cloud sea under the sky or another island's distant stand-in, whose extent says nothing about the room's.
/// An actor with a scale of its own reads the placement's scale as something else (a Timeshift Stone's radius) and draws at that one.
/// </remarks>
public sealed class ActorModels
{
    sealed record Part([property: JsonPropertyName("arc")] string? Arc, [property: JsonPropertyName("stage")] bool Stage,
        [property: JsonPropertyName("model")] string Model);

    sealed record Selection([property: JsonPropertyName("field")] string Field, [property: JsonPropertyName("shift")] int Shift,
        [property: JsonPropertyName("mask")] uint Mask, [property: JsonPropertyName("map")] Dictionary<string, int>? Map,
        [property: JsonPropertyName("otherwise")] int? Otherwise);

    sealed record Entry([property: JsonPropertyName("select")] Selection? Select, [property: JsonPropertyName("variants")] List<List<Part>>? Variants,
        [property: JsonPropertyName("backdrop")] bool Backdrop, [property: JsonPropertyName("scale")] float? Scale);

    readonly Dictionary<string, Entry> _actors;

    ActorModels(Dictionary<string, Entry> actors) => _actors = actors;

    /// <summary>The table the library ships.</summary>
    public static ActorModels Default => _default ??= Read(typeof(ActorModels).Assembly.GetManifestResourceStream("Resources/actors.json")
        ?? throw new InvalidDataException("The actor table is not embedded."));

    static ActorModels? _default;

    public static ActorModels Read(Stream json) =>
        new((JsonSerializer.Deserialize<Dictionary<string, Entry>>(json) ?? []).Where(p => p.Value.Variants is { Count: > 0 })
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));

    public IEnumerable<string> Actors => _actors.Keys;

    // Every part any variant names.
    internal IEnumerable<ActorPart> Parts => _actors.Values.SelectMany(e => e.Variants!).SelectMany(v => v).Select(p => new ActorPart(p.Stage ? null : p.Arc, p.Model)).Distinct();

    public bool IsBackdrop(string actor) => _actors.TryGetValue(actor, out var entry) && entry.Backdrop;

    /// <summary>Where a placement's models stand: its <see cref="Placement.World"/>, at the actor's own scale when it has one.</summary>
    public Matrix4x4 WorldOf(Placement placement) =>
        _actors.TryGetValue(placement.Actor, out var entry) && entry.Scale is float scale ? (placement with { Scale = new Vector3(scale) }).World : placement.World;

    public IReadOnlyList<ActorPart> PartsOf(Placement placement)
    {
        if (!_actors.TryGetValue(placement.Actor, out var entry)) return [];
        var variants = entry.Variants!;
        long variant = entry.Select is { } select ? Variant(select, placement) : 0;
        if (variant < 0 || variant >= variants.Count) variant = entry.Select?.Otherwise ?? -1;
        return variant >= 0 && variant < variants.Count ? [.. variants[(int)variant].Select(p => new ActorPart(p.Stage ? null : p.Arc, p.Model))] : [];
    }

    // The field's bits, rewritten where the actor's code rewrites them.
    static long Variant(Selection select, Placement placement)
    {
        uint value = (Field(select.Field, placement) >> select.Shift) & select.Mask;
        foreach (var (from, to) in select.Map ?? []) if (Number(from) == value) return to;
        return value;
    }

    static uint Field(string field, Placement p) => field switch
    {
        "params1" => p.Params1,
        "params2" => p.Params2,
        "rotx" => p.Angles.X,
        "roty" => p.Angles.Y,
        "rotz" => p.Angles.Z,
        _ => throw new InvalidDataException($"The actor table selects on '{field}', which a placement does not have."),
    };

    static uint Number(string text) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? uint.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : uint.Parse(text, CultureInfo.InvariantCulture);
}

