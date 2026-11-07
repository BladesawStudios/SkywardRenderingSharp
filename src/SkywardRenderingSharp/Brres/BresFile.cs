namespace SkywardRenderingSharp.Brres;

/// <summary>A Wii BRRES resource file: its models (MDL0) and textures (TEX0) by name.</summary>
/// <remarks>Only the sections the renderer uses are read. Texture pixels that moved to a sibling .xtx show as 1x1 stubs here.</remarks>
public sealed class BresFile
{
    public BigEndian Data { get; }
    public IReadOnlyList<(string Name, int Offset)> Models { get; }
    public IReadOnlyList<(string Name, int Offset)> Textures { get; }

    // Every top-level group by name ("AnmChr(NW4R)", "External", ...).
    public IReadOnlyDictionary<string, IReadOnlyList<(string Name, int Offset)>> Groups { get; }

    public BresFile(ReadOnlyMemory<byte> bytes)
    {
        Data = new BigEndian(bytes);
        if (!Data.Span[..4].SequenceEqual("bres"u8)) throw new InvalidDataException("Not a BRRES file.");
        int root = Data.U16(12);
        Models = []; Textures = [];
        var groups = new Dictionary<string, IReadOnlyList<(string, int)>>();
        foreach (var (name, offset) in ResDict.Read(Data, root + 8))
        {
            var entries = ResDict.Read(Data, offset);
            groups[name] = entries;
            if (name.StartsWith("3DModels", StringComparison.Ordinal)) Models = entries;
            else if (name.StartsWith("Textures", StringComparison.Ordinal)) Textures = entries;
        }
        Groups = groups;
    }

    public int ModelOffset(string name) => Models.FirstOrDefault(m => m.Name == name).Offset is > 0 and var o ? o : throw new KeyNotFoundException($"No model '{name}'.");
}
