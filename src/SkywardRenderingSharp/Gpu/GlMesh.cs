using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Gpu;

/// <summary>A polygon uploaded to the GPU: one interleaved vertex buffer of <see cref="MeshVertex"/> and an index buffer.</summary>
public sealed class GlMesh : IDisposable
{
    readonly GL _gl;
    readonly uint _vao, _vbo, _ebo;
    readonly uint _indexCount;

    public unsafe GlMesh(GL gl, MeshData mesh)
    {
        _gl = gl; _indexCount = (uint)mesh.Indices.Length;
        _vao = gl.GenVertexArray(); _vbo = gl.GenBuffer(); _ebo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<MeshVertex>)mesh.Vertices, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        gl.BufferData(BufferTargetARB.ElementArrayBuffer, (ReadOnlySpan<int>)mesh.Indices, BufferUsageARB.StaticDraw);
        uint stride = (uint)sizeof(MeshVertex);
        Attribute(0, 3, 0, stride); Attribute(1, 3, 12, stride); Attribute(2, 4, 24, stride);
        for (uint i = 0; i < 4; i++) Attribute(3 + i, 4, 40 + i * 16, stride);
        Attribute(7, 4, 104, stride); Attribute(8, 4, 120, stride);
    }

    unsafe void Attribute(uint index, int size, uint offset, uint stride)
    {
        _gl.EnableVertexAttribArray(index);
        _gl.VertexAttribPointer(index, size, VertexAttribPointerType.Float, false, stride, (void*)offset);
    }

    public unsafe void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, (void*)0);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao); _gl.DeleteBuffer(_vbo); _gl.DeleteBuffer(_ebo);
    }
}
