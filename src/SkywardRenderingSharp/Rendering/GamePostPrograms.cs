using System.Numerics;
using Silk.NET.OpenGL;
using SkywardRenderingSharp.Gpu;
using SkywardRenderingSharp.Resources;
using SkywardRenderingSharp.Shaders;

namespace SkywardRenderingSharp.Rendering;

/// <summary>The game's own post-processing fragment programs, decompiled from the romfs and drawn as full-screen passes.</summary>
/// <remarks>
/// Each program's fragment stage is decompiled once, made to compile in a 4.3 core context and linked with <c>post/game.vert</c>. A pass
/// fills constant buffer 5 (the engine's per-draw values) and binds its inputs to the units the game's texture handles name.
/// </remarks>
sealed class GamePostPrograms : IDisposable
{
    const int SupportBufferSize = 2048;

    readonly GL _gl;
    readonly Dictionary<string, GlProgram> _programs = [];
    readonly uint _constants, _support, _vao;

    public GamePostPrograms(GL gl, SkywardRom rom, IEnumerable<string> names)
    {
        _gl = gl;
        string vertex = GlProgram.Load("post/game.vert");
        foreach (string name in names)
        {
            NvStage fragment = rom.PostProgram(name).Stages.Single(s => s.Kind == NvStageKind.Fragment);
            _programs[name] = GlProgram.FromSources(gl, vertex, GameGlsl.ForGl43(MaxwellDecompiler.Decompile(fragment)), name);
        }
        _constants = Buffer(GameGlsl.ConstantVectors * 16);
        _support = Buffer(SupportBufferSize);
        _vao = gl.GenVertexArray();
    }

    uint Buffer(int size)
    {
        uint buffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, buffer);
        _gl.BufferData<byte>(BufferTargetARB.UniformBuffer, new byte[size], BufferUsageARB.DynamicDraw);
        return buffer;
    }

    /// <summary>Draws a program into a target. <paramref name="inputs"/> are textures by the game's slot (unit), <paramref name="c5"/> the leading vectors of constant buffer 5.</summary>
    public unsafe void Draw(string name, RenderTarget target, ReadOnlySpan<uint> inputs, params ReadOnlySpan<Vector4> c5)
    {
        _programs[name].Use();
        for (int unit = 0; unit < inputs.Length; unit++)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _gl.BindTexture(TextureTarget.Texture2D, inputs[unit]);
            _gl.BindSampler((uint)unit, 0);
        }
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, _constants);
        fixed (Vector4* p = c5) _gl.BufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)(c5.Length * 16), p);
        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, GameGlsl.ConstantBufferBinding(5), _constants);
        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, GameGlsl.SupportBufferBinding, _support);
        target.Bind();
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void Dispose()
    {
        foreach (var p in _programs.Values) p.Dispose();
        _gl.DeleteBuffer(_constants); _gl.DeleteBuffer(_support);
        _gl.DeleteVertexArray(_vao);
    }
}
