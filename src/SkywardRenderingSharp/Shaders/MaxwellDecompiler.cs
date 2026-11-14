using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Ryujinx.Graphics.Shader;
using Ryujinx.Graphics.Shader.Translation;
namespace SkywardRenderingSharp.Shaders;

/// <summary>Turns one program stage into GLSL with Ryujinx's translator, writing its immediate constants in as literals.</summary>
public static partial class MaxwellDecompiler
{
    public static string Decompile(NvStage stage)
    {
        var options = new TranslationOptions(TargetLanguage.Glsl, TargetApi.OpenGL, TranslationFlags.DebugMode);
        string glsl = Translator.CreateContext(0, new Gpu(stage.Code), options).Translate().Code;
        return ConstantRead().Replace(glsl, m =>
        {
            int index = int.Parse(m.Groups[1].Value) * 4 + "xyzw".IndexOf(m.Groups[2].Value[0]);
            return index < stage.Constants.Length ? Literal(stage.Constants[index]) : m.Value;
        });
    }

    static string Literal(float value)
    {
        if (!float.IsFinite(value)) return $"uintBitsToFloat(0x{BitConverter.SingleToUInt32Bits(value):X8}u)";
        string s = value.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }

    [GeneratedRegex(@"(?:fp|vp)_c1\.data\[(\d+)\]\.([xyzw])")]
    private static partial Regex ConstantRead();

    sealed class Gpu(byte[] code) : IGpuAccessor
    {
        public uint ConstantBuffer1Read(int offset) => 0;

        public ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize) =>
            MemoryMarshal.Cast<byte, ulong>(new ReadOnlySpan<byte>(code)[(int)address..]);
    }
}
