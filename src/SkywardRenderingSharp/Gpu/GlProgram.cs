using System.Numerics;
using System.Reflection;
using Silk.NET.OpenGL;

namespace SkywardRenderingSharp.Gpu;

/// <summary>A linked GL program built from embedded GLSL, with cached uniform locations and typed setters.</summary>
/// <remarks>Uniforms that the compiler removed are ignored silently, so a shader can drop one without the host noticing.</remarks>
public sealed class GlProgram : IDisposable
{
    readonly GL _gl;
    readonly Dictionary<string, int> _locations = [];

    public uint Handle { get; }

    GlProgram(GL gl, uint handle) { _gl = gl; Handle = handle; }

    public static GlProgram FromResources(GL gl, string vertex, string fragment) => FromSources(gl, Load(vertex), Load(fragment), $"{vertex} + {fragment}");

    public static GlProgram FromSources(GL gl, string vertexSource, string fragmentSource, string label)
    {
        uint vs = Compile(gl, ShaderType.VertexShader, vertexSource, label), fs = Compile(gl, ShaderType.FragmentShader, fragmentSource, label);
        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs); gl.AttachShader(program, fs);
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException($"Linking {label} failed: {gl.GetProgramInfoLog(program)}");
        gl.DeleteShader(vs); gl.DeleteShader(fs);
        return new GlProgram(gl, program);
    }

    public static string Load(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        // MSBuild writes subfolders into resource names with the platform separator.
        string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == $"Glsl/{name}") ?? throw new FileNotFoundException($"No embedded shader '{name}'.");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static uint Compile(GL gl, ShaderType type, string source, string label)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException($"{type} of {label} failed to compile: {gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    public void Use() => _gl.UseProgram(Handle);

    int Loc(string name)
    {
        if (!_locations.TryGetValue(name, out int l)) _locations[name] = l = _gl.GetUniformLocation(Handle, name);
        return l;
    }

    public void Set(string name, int v) { if (Loc(name) is var l and >= 0) _gl.Uniform1(l, v); }
    public void Set(string name, float v) { if (Loc(name) is var l and >= 0) _gl.Uniform1(l, v); }
    public void Set(string name, Vector2 v) { if (Loc(name) is var l and >= 0) _gl.Uniform2(l, v.X, v.Y); }
    public void Set(string name, Vector3 v) { if (Loc(name) is var l and >= 0) _gl.Uniform3(l, v.X, v.Y, v.Z); }
    public void Set(string name, Vector4 v) { if (Loc(name) is var l and >= 0) _gl.Uniform4(l, v.X, v.Y, v.Z, v.W); }

    // System.Numerics matrices are row-major for row vectors; uploaded untransposed they are the column-major GL matrices for column vectors.
    public unsafe void Set(string name, Matrix4x4 m) { if (Loc(name) is var l and >= 0) _gl.UniformMatrix4(l, 1, false, (float*)&m); }

    public unsafe void Set3x3(string name, Matrix4x4 m)
    {
        if (Loc(name) is not (var l and >= 0)) return;
        Span<float> v = [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
        _gl.UniformMatrix3(l, 1, false, v);
    }

    public void SetInts2(string name, ReadOnlySpan<int> values) { if (Loc(name) is var l and >= 0) _gl.Uniform2(l, values); }
    public void SetInts(string name, ReadOnlySpan<int> values) { if (Loc(name) is var l and >= 0) _gl.Uniform1(l, values); }
    public void SetUInts(string name, ReadOnlySpan<uint> values) { if (Loc(name) is var l and >= 0) _gl.Uniform1(l, values); }
    public void SetInt4s(string name, ReadOnlySpan<int> values) { if (Loc(name) is var l and >= 0) _gl.Uniform4(l, values); }
    public void SetVec3s(string name, ReadOnlySpan<float> values) { if (Loc(name) is var l and >= 0) _gl.Uniform3(l, values); }
    public void SetVec4s(string name, ReadOnlySpan<float> values) { if (Loc(name) is var l and >= 0) _gl.Uniform4(l, values); }

    public unsafe void SetMatrices(string name, ReadOnlySpan<Matrix4x4> values)
    {
        if (Loc(name) is not (var l and >= 0)) return;
        fixed (Matrix4x4* p = values) _gl.UniformMatrix4(l, (uint)values.Length, false, (float*)p);
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
