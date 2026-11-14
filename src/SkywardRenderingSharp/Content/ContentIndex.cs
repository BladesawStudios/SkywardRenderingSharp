using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BresSharp;
using SkywardRenderingSharp.Archives;
using U8Sharp;

namespace SkywardRenderingSharp.Content;

/// <summary>Every model in the romfs by name: the object pack, each stage archive, and the object (oarc) and room (rarc) archives nested inside them.</summary>
/// <remarks>Building scans and decompresses every stage archive (a minute or so), so the result is cached on disk and reused until the romfs path changes.
/// A name found in several places resolves to the first one found, object pack first.</remarks>
public sealed class ContentIndex
{
    const int MaxDepth = 3;

    readonly Dictionary<string, List<ModelLocation>> _byModel = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> ModelNames => _byModel.Keys.Order(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ModelLocation> All => _byModel.Values.SelectMany(v => v);

    public IReadOnlyList<ModelLocation> Find(string model) => _byModel.TryGetValue(model, out var list) ? list : [];

    public IEnumerable<ModelLocation> Search(string text) =>
        _byModel.Where(p => p.Key.Contains(text, StringComparison.OrdinalIgnoreCase)).SelectMany(p => p.Value);

    /// <summary>All models of one archive (an object such as "Alink", or a stage such as "F000").</summary>
    public IEnumerable<ModelLocation> InArchive(string archive) =>
        _byModel.Values.SelectMany(v => v).Where(l => l.Archive.Equals(archive, StringComparison.OrdinalIgnoreCase));

    /// <summary>Models of sibling archives named after a model's own (Zelda_face, Zelda_hair, Zelda_handL next to Zelda): the parts an object is assembled from.</summary>
    public IEnumerable<ModelLocation> Companions(ModelLocation main)
    {
        if (main.Chain.Length == 0) return [];
        string stem = Path.GetFileNameWithoutExtension(main.Chain[^1]) + "_";
        return _byModel.Values.SelectMany(v => v).Where(l => l.Container == main.Container && l.Chain.Length == main.Chain.Length
            && l.Chain[..^1].SequenceEqual(main.Chain[..^1]) && Path.GetFileNameWithoutExtension(l.Chain[^1]).StartsWith(stem, StringComparison.Ordinal));
    }

    public static ContentIndex Open(string romfs, Action<string>? progress = null)
    {
        string cache = CachePath(romfs);
        if (File.Exists(cache) && JsonSerializer.Deserialize<List<ModelLocation>>(File.ReadAllText(cache)) is { } cached) return From(cached);
        var all = new List<ModelLocation>();
        foreach (string container in Containers(romfs))
        {
            progress?.Invoke(container);
            try { Scan(GameArchive.OpenFile(Path.Combine(romfs, container)), container, [], all, 0); }
            catch (Exception e) when (e is InvalidDataException or IOException) { progress?.Invoke($"skipped {container}: {e.Message}"); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllText(cache, JsonSerializer.Serialize(all));
        return From(all);
    }

    static ContentIndex From(IEnumerable<ModelLocation> locations)
    {
        var index = new ContentIndex();
        foreach (var l in locations)
        {
            if (!index._byModel.TryGetValue(l.Model, out var list)) index._byModel[l.Model] = list = [];
            list.Add(l);
        }
        return index;
    }

    static IEnumerable<string> Containers(string romfs)
    {
        yield return Path.Combine("Object", "NX", "ObjectPack.arc.LZ");
        foreach (string stage in Directory.GetDirectories(Path.Combine(romfs, "Stage")).Order())
        {
            string nx = Path.Combine(stage, "NX");
            if (!Directory.Exists(nx)) continue;
            foreach (string file in Directory.GetFiles(nx, "*.arc*").Order()) yield return Path.GetRelativePath(romfs, file);
        }
    }

    static void Scan(U8File archive, string container, string[] chain, List<ModelLocation> into, int depth)
    {
        foreach (string path in archive.Paths())
        {
            if (path.EndsWith(".brres", StringComparison.OrdinalIgnoreCase) && path.StartsWith("g3d/", StringComparison.OrdinalIgnoreCase)) AddModels(archive, path, container, chain, into);
            else if (depth < MaxDepth && path.EndsWith(".arc", StringComparison.OrdinalIgnoreCase) && (path.StartsWith("oarc/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("rarc/", StringComparison.OrdinalIgnoreCase)))
            {
                try { Scan(GameArchive.Open(archive.Get(path)), container, [.. chain, path], into, depth + 1); }
                catch (InvalidDataException) { }
            }
        }
    }

    static void AddModels(U8File archive, string path, string container, string[] chain, List<ModelLocation> into)
    {
        try { foreach (var model in BresFile.FromBinary(archive.Get(path))["3DModels(NW4R)"]?.Files ?? []) into.Add(new ModelLocation(model.Name, container, chain, path)); }
        catch (InvalidDataException) { }
    }

    static string CachePath(string romfs)
    {
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(romfs).ToLowerInvariant())))[..12];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkywardRenderingSharp", $"models-v2-{hash}.json");
    }
}
