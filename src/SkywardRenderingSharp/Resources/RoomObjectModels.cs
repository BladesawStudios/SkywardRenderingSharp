using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Resources;

/// <summary>Reads the models a room's placements draw, each once however many placements share it.</summary>
/// <remarks>
/// An object archive is looked up by name through the content index: the object pack's first, then the stage's own archives, then any
/// other stage's. A stage part comes from the stage's g3d/stage.brres. An object model's textures resolve through its own archive, then
/// the stage's pool and the sky package, as the game binds them; an archive borrowed from another stage (a dungeon's entrance door) looks in
/// that stage's pool first. A part no archive holds is kept aside as missing.
/// </remarks>
sealed class RoomObjectModels(SkywardRom rom, StageArchive stage, ModelTextures pool)
{
    readonly Dictionary<ActorPart, int> _index = [];
    readonly Dictionary<string, (ModelArchive Archive, ModelTextures Fallback)?> _archives = new(StringComparer.OrdinalIgnoreCase);
    ModelArchive? _props;

    public List<PlacedModel> Models { get; } = [];

    public List<ActorPart> Missing { get; } = [];

    public int[] Of(IReadOnlyList<ActorPart> parts) => [.. parts.Select(Index).Where(i => i >= 0)];

    int Index(ActorPart part)
    {
        if (_index.TryGetValue(part, out int known)) return known;
        var read = Read(part);
        if (read is null) Missing.Add(part);
        else Models.Add(read);
        return _index[part] = read is null ? -1 : Models.Count - 1;
    }

    PlacedModel? Read(ActorPart part)
    {
        var (archive, fallback) = part.Archive is null ? (_props ??= stage.Props, pool) : Archive(part.Archive) ?? default;
        string? name = archive?.ModelNames.FirstOrDefault(n => n.Equals(part.Model, StringComparison.OrdinalIgnoreCase));
        if (archive is null || name is null) return null;
        return new PlacedModel(part, Mdl0Model.Read(archive.Bres, name), part.Archive is null ? pool : new ModelTextures(archive, fallback));
    }

    (ModelArchive Archive, ModelTextures Fallback)? Archive(string name)
    {
        if (_archives.TryGetValue(name, out var cached)) return cached;
        var found = rom.Index.All.Where(l => l.Chain is [.., var last] && last.StartsWith("oarc/", StringComparison.Ordinal)
                && l.Archive.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(l => l.Container.EndsWith("ObjectPack.arc.LZ", StringComparison.OrdinalIgnoreCase) ? 0 : StageOf(l.Container) == stage.Name ? 1 : 2)
            .FirstOrDefault();
        if (found is null) return _archives[name] = null;
        string? owner = found.Container.EndsWith("ObjectPack.arc.LZ", StringComparison.OrdinalIgnoreCase) ? null : StageOf(found.Container);
        var fallback = owner is null || owner == stage.Name ? pool : new ModelTextures(rom.OpenStage(owner).Props, pool);
        return _archives[name] = (rom.Open(found), fallback);
    }

    static string? StageOf(string container) => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(container)));
}
