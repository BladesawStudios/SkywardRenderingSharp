using System.IO.Compression;
using SkywardRenderingSharp.Archives;
using SkywardRenderingSharp.Shaders;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: SkywardShaderDecompile <romfs/Shaders/custom | romfs/Shaders/Shaders.arc.gz | program file> <output folder>");
    return 2;
}

string input = args[0], output = args[1];
Directory.CreateDirectory(output);
int done = 0, failed = 0;

foreach ((string name, byte[] data) in Programs(input))
{
    try
    {
        NvProgramBinary program = NvProgramBinary.Read(data);
        foreach (NvStage stage in program.Stages)
            File.WriteAllText(Path.Combine(output, $"{name}.{stage.Extension}"), MaxwellDecompiler.Decompile(stage));
        done++;
    }
    catch (Exception e)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {e.Message}");
    }
}

Console.WriteLine($"{done} programs decompiled, {failed} failed");
return failed == 0 ? 0 : 1;

static IEnumerable<(string Name, byte[] Data)> Programs(string input)
{
    if (Directory.Exists(input))
    {
        foreach (string path in Directory.EnumerateFiles(input).Order()) yield return (Path.GetFileName(path), File.ReadAllBytes(path));
        yield break;
    }
    if (!input.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) && !input.EndsWith(".arc", StringComparison.OrdinalIgnoreCase))
    {
        yield return (Path.GetFileName(input), File.ReadAllBytes(input));
        yield break;
    }

    byte[] archive = File.ReadAllBytes(input);
    if (input.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
    {
        using var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress);
        using var unpacked = new MemoryStream();
        gzip.CopyTo(unpacked);
        archive = unpacked.ToArray();
    }
    // Shaders.arc keeps the programs in cache/ and, under the same names, the material descriptions they were built for in desccache/.
    var u8 = GameArchive.Open(archive);
    foreach (string path in u8.Paths().Where(p => p.StartsWith("cache/", StringComparison.OrdinalIgnoreCase)).Order())
        yield return (Path.GetFileName(path), u8.Get(path));
}
