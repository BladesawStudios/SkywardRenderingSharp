namespace SkywardRenderingSharp.Content;

/// <summary>Where a model lives: a container file under the romfs, the chain of archives inside it, and the BRRES that holds the model.</summary>
/// <remarks>Chain entries are U8 paths such as "oarc/Alink.arc". The .xtx with the BRRES's name in the same archive holds its textures.</remarks>
public sealed record ModelLocation(string Model, string Container, string[] Chain, string BresPath)
{
    public string XtxPath => Path.ChangeExtension(BresPath, ".xtx");

    public string Archive => Chain.Length > 0 ? Path.GetFileNameWithoutExtension(Chain[^1]) : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(Container));

    public override string ToString() => $"{Model} ({Container}{(Chain.Length > 0 ? ":" + string.Join(":", Chain) : "")}:{BresPath})";
}
