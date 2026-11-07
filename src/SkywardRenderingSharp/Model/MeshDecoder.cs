using System.Numerics;
using SkywardRenderingSharp.Brres;
using SkywardRenderingSharp.Gx;

namespace SkywardRenderingSharp.Model;

/// <summary>Decodes an MDL0 polygon object (its CP setup and GX primitive stream) into bind-pose triangles.</summary>
/// <remarks>Vertices are expanded per primitive. A weighted object's positions are already in model space; a rigid object's are in its bone's space.
/// Only indexed vertex attributes are supported, which is all the game's models use.</remarks>
sealed class MeshDecoder(BigEndian f, VertexArrays arrays, IReadOnlyList<Bone> bones, NodeMatrices matrices)
{
    // Position matrix slots (XF address) to the matrix ID the primitive stream loaded there.
    readonly Dictionary<int, int> _nodeByAddress = [];

    const int NodeId = 0x08, PrimitiveSize = 0x24, SetupOffset = 0x20, NodeTable = 0x68, ArrayIds = 0x48;

    public MeshData Decode(string name, int o)
    {
        var state = new GxState();
        state.Run(f, o + SetupOffset + f.I32(o + SetupOffset), o + f.I32(o));
        var layout = VertexLayout.From(state.Cp.GetValueOrDefault(0x50), state.Cp.GetValueOrDefault(0x60));
        var ids = new ArrayIds(f, o + ArrayIds);
        int node = f.I32(o + NodeId) is var id and >= 0 ? matrices.RigidBone(id, bones.Count) : -1;
        var rigid = node >= 0 && node < bones.Count ? bones[node].World : (Matrix4x4?)null;
        int length = f.I32(o), size = f.I32(o + PrimitiveSize);
        var vertices = new List<MeshVertex>();
        var indices = new List<int>();
        _nodeByAddress.Clear();
        Walk(o + length - size, o + length, layout, ids, rigid, node, vertices, indices);
        return new MeshData(name, vertices.ToArray(), indices.ToArray());
    }

    // The stream interleaves state commands (indexed matrix loads, BP, XF) with primitives.
    void Walk(int p, int end, VertexLayout layout, ArrayIds ids, Matrix4x4? rigid, int rigidBone, List<MeshVertex> vertices, List<int> indices)
    {
        while (p < end)
        {
            byte op = f.U8(p);
            if (op == 0) p++;
            else if (op is 0x20 or 0x28 or 0x30 or 0x38) { _nodeByAddress[f.U16(p + 3) & 0xFFF] = f.U16(p + 1); p += 5; }
            else if (op == 0x08) p += 6;
            else if (op == 0x61) p += 5;
            else if (op == 0x10) p += 5 + 4 * (f.U16(p + 1) + 1);
            else if (op >= 0x80) p = ReadPrimitive(p, layout, ids, rigid, rigidBone, vertices, indices);
            else throw new InvalidDataException($"Unknown GX command 0x{op:x2} at 0x{p:x}.");
        }
    }

    int ReadPrimitive(int p, VertexLayout layout, ArrayIds ids, Matrix4x4? rigid, int rigidBone, List<MeshVertex> vertices, List<int> indices)
    {
        int type = f.U8(p) & 0xF8, count = f.U16(p + 1);
        p += 3;
        int first = vertices.Count, firstIndex = indices.Count;
        bool mirrored = false;
        for (int i = 0; i < count; i++, p += layout.Stride)
        {
            int slot = layout.PositionMatrixBytes > 0 ? f.U8(p) : 0;
            var matrix = rigid ?? NodeMatrix(slot);
            if (i == 0) mirrored = matrix.GetDeterminant() < 0;
            var vertex = ReadVertex(p, layout, ids, matrix);
            ApplyInfluences(ref vertex, rigidBone, slot);
            vertices.Add(vertex);
        }
        Triangulate(type, first, count, indices);
        // A mirroring matrix reverses the winding, which back-face culling would then take for the far side of the surface.
        if (mirrored) for (int i = firstIndex; i + 2 < indices.Count; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        return p;
    }

    MeshVertex ReadVertex(int p, VertexLayout layout, ArrayIds ids, Matrix4x4 matrix)
    {
        var v = new MeshVertex { Color = Vector4.One };
        int a = layout.PositionMatrixBytes + layout.TexMatrixBytes;
        v.Position = Vector3.Transform(arrays.Positions[ids.Position][Index(p + a, layout.Position)], matrix);
        a += layout.Position.Size;
        if (layout.Normal.Size > 0) { v.Normal = Vector3.Normalize(Vector3.TransformNormal(arrays.Normals[ids.Normal][Index(p + a, layout.Normal)], matrix)); a += layout.Normal.Size; }
        if (layout.Color0.Size > 0) { v.Color = arrays.Colors[ids.Color0][Index(p + a, layout.Color0)]; }
        a += layout.Color0.Size + layout.Color1.Size;
        for (int t = 0; t < 8; t++)
        {
            if (layout.Uv[t].Size == 0) continue;
            v.Uv[t] = arrays.Uvs[ids.Uv[t]][Index(p + a, layout.Uv[t])];
            a += layout.Uv[t].Size;
        }
        return v;
    }

    // The matrix a vertex's position-matrix index selects: whatever matrix ID the stream last loaded into that slot.
    Matrix4x4 NodeMatrix(int slotIndex) =>
        _nodeByAddress.TryGetValue(slotIndex * 4, out int id) ? matrices.Get(id) : Matrix4x4.Identity;

    // A rigid object (a node id on the polygon) follows that bone; otherwise the vertex follows whatever its matrix slot's id resolves to.
    void ApplyInfluences(ref MeshVertex v, int rigidBone, int slot)
    {
        var influences = rigidBone >= 0 && rigidBone < bones.Count ? [(rigidBone, 1f)]
            : _nodeByAddress.TryGetValue(slot * 4, out int id) ? matrices.Influences(id) : [];
        for (int i = 0; i < influences.Length && i < 4; i++) { v.BoneIds[i] = influences[i].Bone; v.Weights[i] = influences[i].Weight; }
    }

    int Index(int p, AttributeMode mode) => mode.Size == 1 ? f.U8(p) : f.U16(p);

    static void Triangulate(int type, int first, int count, List<int> indices)
    {
        switch (type)
        {
            case 0x80: for (int i = 0; i + 3 < count; i += 4) { Tri(indices, first + i, 1, 2); Tri(indices, first + i, 2, 3); } break;
            case 0x90: for (int i = 0; i + 2 < count; i += 3) Tri(indices, first + i, 1, 2); break;
            case 0x98: for (int i = 0; i + 2 < count; i++) { if (i % 2 == 0) Tri(indices, first + i, 1, 2); else { indices.Add(first + i + 1); indices.Add(first + i); indices.Add(first + i + 2); } } break;
            case 0xA0: for (int i = 1; i + 1 < count; i++) { indices.Add(first); indices.Add(first + i); indices.Add(first + i + 1); } break;
        }
    }

    static void Tri(List<int> indices, int baseIndex, int b, int c) { indices.Add(baseIndex); indices.Add(baseIndex + b); indices.Add(baseIndex + c); }
}
