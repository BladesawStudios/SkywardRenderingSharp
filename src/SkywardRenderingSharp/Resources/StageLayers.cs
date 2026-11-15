using System.Text.Json;

namespace SkywardRenderingSharp.Resources;

/// <summary>How many placements each stage has on each of its story layers, so the layers can be listed without opening every stage.</summary>
/// <remarks>
/// A stage loads layer 0 and one story layer (1 to 28), which the game picks from the story flags; layer 0 and the records outside the
/// layer table are always present and are not counted. The table (Resources/layers.json) was counted from the HD dump with
/// <see cref="Count"/>, over every room's room.bzs and the stage's stage.bzs.
/// </remarks>
public sealed class StageLayers
{
    readonly SortedDictionary<string, SortedDictionary<int, int>> _stages;

    StageLayers(SortedDictionary<string, SortedDictionary<int, int>> stages) => _stages = stages;

    /// <summary>The table the library ships.</summary>
    public static StageLayers Default => _default ??= Read(typeof(StageLayers).Assembly.GetManifestResourceStream("Resources/layers.json")
        ?? throw new InvalidDataException("The layer table is not embedded."));

    static StageLayers? _default;

    /// <summary>The stages with any placement on a story layer.</summary>
    public IEnumerable<string> Stages => _stages.Keys;

    /// <summary>A stage's story layers and how many placements each has.</summary>
    public IReadOnlyDictionary<int, int> Of(string stage) => _stages.TryGetValue(stage, out var layers) ? layers : new SortedDictionary<int, int>();

    public static StageLayers Read(Stream json) =>
        new(new((JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, int>>>(json) ?? []).ToDictionary(p => p.Key, p => new SortedDictionary<int, int>(p.Value)),
            StringComparer.Ordinal));

    // One stage a line, as the actor table is written.
    public void Write(Stream json)
    {
        using var writer = new StreamWriter(json, leaveOpen: true) { NewLine = "\n" };
        writer.WriteLine("{");
        writer.WriteLine(string.Join(",\n", _stages.Select(p => $"  {JsonSerializer.Serialize(p.Key)}: {JsonSerializer.Serialize(p.Value)}")));
        writer.WriteLine("}");
    }

    /// <summary>Counts the layers of every stage in a dump, decompressing each stage's first archive.</summary>
    public static StageLayers Count(SkywardRom rom)
    {
        var stages = new SortedDictionary<string, SortedDictionary<int, int>>(StringComparer.Ordinal);
        foreach (string stage in rom.StageNames)
        {
            var archive = rom.OpenStage(stage);
            var layers = new SortedDictionary<int, int>();
            foreach (var p in archive.Rooms.SelectMany(archive.RoomPlacements).Concat(archive.StagePlacements).Where(p => !p.AlwaysPresent))
                layers[p.Layer] = layers.GetValueOrDefault(p.Layer) + 1;
            if (layers.Count > 0) stages[stage] = layers;
        }
        return new StageLayers(stages);
    }

    public bool SameAs(StageLayers other) =>
        _stages.Count == other._stages.Count && _stages.All(p => other._stages.TryGetValue(p.Key, out var o) && p.Value.SequenceEqual(o));
}
