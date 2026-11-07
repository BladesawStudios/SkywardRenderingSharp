using System.Numerics;
using System.Runtime.CompilerServices;

namespace SkywardRenderingSharp.Model;

/// <summary>Eight texture coordinate sets, one per GX texcoord slot.</summary>
[InlineArray(8)]
public struct UvSet { Vector2 _first; }

/// <summary>A model-space vertex with every attribute a GX material can ask for. Texgens pick their input from these.</summary>
public struct MeshVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Color;
    public UvSet Uv;
    // Up to four bone influences; weights all zero means the vertex is not skinned.
    public Vector4 BoneIds;
    public Vector4 Weights;
}

/// <summary>A decoded polygon object: bind-pose vertices and a triangle index list.</summary>
public sealed record MeshData(string Name, MeshVertex[] Vertices, int[] Indices);
