using SkywardRenderingSharp.Brres;

namespace SkywardRenderingSharp.Animation;

/// <summary>A texture pattern animation (PAT0): per material, which of the clip's textures is shown at each frame.</summary>
/// <remarks>This is how Link's eyes blink and change expression: the eye materials swap between the al_eye.N textures. Only the first
/// texture slot of each material is animated, which is all the face clips use.</remarks>
public sealed class PatClip
{
    readonly Dictionary<string, (float Frame, int Texture)[]> _materials = new(StringComparer.Ordinal);
    readonly string[] _textures;

    public string Name { get; }
    public int FrameCount { get; }

    PatClip(string name, int frames, string[] textures) { Name = name; FrameCount = frames; _textures = textures; }

    public static PatClip Read(BigEndian f, string name, int pat0)
    {
        int textureTable = pat0 + (int)f.U32(pat0 + 0x14);
        int textureCount = f.U16(pat0 + 0x34);
        var textures = Enumerable.Range(0, textureCount).Select(i => f.CString(textureTable + (int)f.U32(textureTable + 4 * i))).ToArray();
        var clip = new PatClip(name, f.U16(pat0 + 0x30), textures);
        foreach (var (material, record) in ResDict.Read(f, pat0 + (int)f.U32(pat0 + 0x10))) clip._materials[material] = ReadKeys(f, record + (int)f.U32(record + 8));
        return clip;
    }

    static (float, int)[] ReadKeys(BigEndian f, int list) =>
        Enumerable.Range(0, f.U16(list)).Select(i => (f.F32(list + 8 + 8 * i), f.U16(list + 12 + 8 * i))).ToArray();

    /// <summary>For each animated material, the texture name shown at the frame (a key holds until the next).</summary>
    public IReadOnlyDictionary<string, string> Evaluate(float frame)
    {
        var result = new Dictionary<string, string>();
        foreach (var (material, keys) in _materials)
        {
            int texture = keys.Length == 0 ? 0 : keys.LastOrDefault(k => k.Frame <= frame, keys[0]).Texture;
            if (texture < _textures.Length) result[material] = _textures[texture];
        }
        return result;
    }
}
