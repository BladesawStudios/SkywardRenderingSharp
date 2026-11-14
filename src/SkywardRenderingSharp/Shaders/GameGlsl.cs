using System.Text.RegularExpressions;

namespace SkywardRenderingSharp.Shaders;

/// <summary>Makes decompiled game GLSL compile in the renderer's 4.3 core context with bindings the host can predict.</summary>
/// <remarks>
/// Samplers move to the unit their texture handle names (handle 0x8 is unit 0, 0xA unit 1, ...), so a pass binds its inputs by the
/// game's own slot order. Constant buffers keep the translator's bindings (support buffer 0, c1 2, c3 4, c5 6) but are cut to
/// <see cref="ConstantVectors"/> vectors, so a small buffer backs them.
/// </remarks>
public static partial class GameGlsl
{
    public const int ConstantVectors = 16;

    // The translator's binding for constant buffer n of a stage.
    public static uint ConstantBufferBinding(int buffer) => (uint)(buffer + 1);

    public const uint SupportBufferBinding = 0;

    public static string ForGl43(string glsl)
    {
        var lines = glsl.Split('\n').Where(l => !l.StartsWith("#extension") && !l.StartsWith("#pragma"));
        string text = string.Join('\n', lines).Replace("#version 450 core", "#version 430 core");
        text = BlockArray().Replace(text, $"vec4 data[{ConstantVectors}];");
        return Sampler().Replace(text, m => $"layout (binding = {SamplerUnit(m.Groups[3].Value)}) uniform {m.Groups[1].Value} {m.Groups[2].Value}{m.Groups[3].Value};");
    }

    // A texture handle in the decompiled names is a word offset in the stage's texture constant buffer, two words per texture from 8.
    public static int SamplerUnit(string handle) => (Convert.ToInt32(handle, 16) - 8) / 2;

    [GeneratedRegex(@"vec4 data\[4096\];")]
    private static partial Regex BlockArray();

    [GeneratedRegex(@"layout \(binding = \d+\) uniform (\w+) ((?:fp|vp)_t_tcb_)([0-9A-F]+);")]
    private static partial Regex Sampler();
}
