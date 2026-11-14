using LzSharp;
using U8Sharp;

namespace SkywardRenderingSharp.Archives;

/// <summary>Opens the game's U8 archives (.arc, and LZ-compressed .arc.LZ) and reads their files by path.</summary>
/// <remarks>Paths are as the archive stores them, without the root: "g3d/model.brres", "oarc/Alink.arc".</remarks>
public static class GameArchive
{
    public static U8File Open(byte[] data) => U8File.FromBinary(Lz.DecompressIfNeeded(data));

    public static U8File OpenFile(string path) => Open(File.ReadAllBytes(path));

    public static byte[] Get(this U8File archive, string path) =>
        archive.TryGet(path, out U8Entry? entry) ? entry.Data : throw new FileNotFoundException($"'{path}' is not in the archive.");

    public static IEnumerable<string> Paths(this U8File archive) => archive.Entries.Select(e => e.Path);
}
