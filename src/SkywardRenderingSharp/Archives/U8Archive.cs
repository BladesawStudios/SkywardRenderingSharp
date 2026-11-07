using System.Buffers.Binary;
using System.Text;

namespace SkywardRenderingSharp.Archives;

/// <summary>A read-only U8 archive (the Wii .arc container), holding its files by full path.</summary>
/// <remarks>The file data are slices of the buffer given to <see cref="Open"/>; the buffer must outlive them. An LZ11 buffer is decompressed first.</remarks>
public sealed class U8Archive
{
    static readonly byte[] Magic = [0x55, 0xAA, 0x38, 0x2D];
    readonly Dictionary<string, ReadOnlyMemory<byte>> _files = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Paths => _files.Keys;

    public bool TryGet(string path, out ReadOnlyMemory<byte> data) => _files.TryGetValue(path, out data);

    public ReadOnlyMemory<byte> Get(string path) =>
        _files.TryGetValue(path, out var d) ? d : throw new FileNotFoundException($"'{path}' is not in the archive.");

    public static U8Archive Open(byte[] data)
    {
        if (Lz11.IsLz11(data)) data = Lz11.Decompress(data);
        if (!data.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException("Not a U8 archive.");
        var archive = new U8Archive();
        archive.ReadNodes(data);
        return archive;
    }

    public static U8Archive OpenFile(string path) => Open(File.ReadAllBytes(path));

    void ReadNodes(byte[] d)
    {
        int root = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(4));
        int count = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(root + 8));
        int strings = root + 12 * count;
        var directories = new Stack<(int End, string Path)>();
        for (int i = 1; i < count; i++)
        {
            while (directories.Count > 0 && i >= directories.Peek().End) directories.Pop();
            var node = d.AsSpan(root + 12 * i, 12);
            int nameOffset = node[1] << 16 | node[2] << 8 | node[3];
            int a = BinaryPrimitives.ReadInt32BigEndian(node[4..]), b = BinaryPrimitives.ReadInt32BigEndian(node[8..]);
            string path = (directories.Count > 0 ? directories.Peek().Path : "") + ReadName(d, strings + nameOffset);
            if (node[0] == 1) directories.Push((b, path + "/"));
            else _files[path] = d.AsMemory(a, b);
        }
    }

    static string ReadName(byte[] d, int offset)
    {
        int end = Array.IndexOf(d, (byte)0, offset);
        return Encoding.Latin1.GetString(d, offset, end - offset);
    }
}
