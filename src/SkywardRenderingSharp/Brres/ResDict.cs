namespace SkywardRenderingSharp.Brres;

/// <summary>Reads a BRRES resource dictionary (the name tree every section uses) as a list of names and absolute data offsets.</summary>
public static class ResDict
{
    public static List<(string Name, int Offset)> Read(BigEndian file, int dictOffset)
    {
        int count = (int)file.U32(dictOffset + 4);
        var entries = new List<(string, int)>(count);
        for (int i = 1; i <= count; i++)
        {
            int entry = dictOffset + 8 + 16 * i;
            entries.Add((file.CString(dictOffset + (int)file.U32(entry + 8)), dictOffset + (int)file.U32(entry + 12)));
        }
        return entries;
    }
}
