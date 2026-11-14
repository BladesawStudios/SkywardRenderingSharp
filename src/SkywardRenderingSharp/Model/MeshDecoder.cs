using System.Numerics;
using Res = BresSharp.Mdl0;

namespace SkywardRenderingSharp.Model;

/// <summary>Turns an MDL0 polygon object, as BresSharp decodes its primitives, into bind-pose triangles with bone influences.</summary>
/// <remarks>Each vertex is expanded once per primitive. A weighted object's positions are already in model space; a rigid object's are in its bone's space.
/// The matrix loads before a primitive decide which matrix ID each position-matrix slot holds while it draws.</remarks>
sealed class MeshDecoder(Res.Mdl0Model model, IReadOnlyList<Bone> bones, NodeMatrices matrices)
{
    // Matrix slots (XF address) to the matrix ID the primitive stream last loaded there.
    readonly Dictionary<int, int> _nodeByAddress = [];

    public MeshData Decode(Res.Mdl0Shape shape)
    {
        int node = shape.BoneIndex >= 0 ? matrices.RigidBone(shape.BoneIndex, bones.Count) : -1;
        var rigid = node >= 0 && node < bones.Count ? bones[node].World : (Matrix4x4?)null;
        var vertices = new List<MeshVertex>();
        var indices = new List<int>();
        _nodeByAddress.Clear();
        foreach (var primitive in shape.Primitives)
        {
            foreach (var load in primitive.MatrixLoads) _nodeByAddress[load.Address & 0xFFF] = load.Index;
            var indexOf = new Dictionary<Res.Mdl0Vertex, (int Index, bool Mirrored)>(ReferenceEqualityComparer.Instance);
            foreach (var v in primitive.Vertices)
            {
                int slot = Math.Max(0, v.MatrixIndex);
                var matrix = rigid ?? NodeMatrix(slot);
                indexOf[v] = (vertices.Count, matrix.GetDeterminant() < 0);
                vertices.Add(Vertex(shape, v, matrix, node, slot));
            }
            foreach (var (a, b, c) in primitive.Triangles())
            {
                var (first, mirrored) = indexOf[a];
                // A mirroring matrix reverses the winding, which back-face culling would then take for the far side of the surface.
                if (mirrored) indices.AddRange([first, indexOf[c].Index, indexOf[b].Index]);
                else indices.AddRange([first, indexOf[b].Index, indexOf[c].Index]);
            }
        }
        return new MeshData(shape.Name, vertices.ToArray(), indices.ToArray());
    }

    MeshVertex Vertex(Res.Mdl0Shape shape, Res.Mdl0Vertex source, Matrix4x4 matrix, int rigidBone, int slot)
    {
        var v = new MeshVertex { Color = Vector4.One };
        v.Position = Vector3.Transform(model.Positions[shape.PositionArray].ReadVector3(source.Position), matrix);
        if (source.Normal >= 0) v.Normal = Vector3.Normalize(Vector3.TransformNormal(model.Normals[shape.NormalArray].ReadVector3(source.Normal), matrix));
        if (source.Color0 >= 0)
        {
            var (r, g, b, a) = model.Colors[shape.ColorArray(0)].Read(source.Color0);
            v.Color = new Vector4(r, g, b, a) / 255f;
        }
        for (int t = 0; t < 8; t++)
            if (source.TexCoord[t] >= 0) v.Uv[t] = model.TexCoords[shape.TexCoordArray(t)].ReadVector2(source.TexCoord[t]);
        var influences = rigidBone >= 0 && rigidBone < bones.Count ? [(rigidBone, 1f)]
            : _nodeByAddress.TryGetValue(slot * 4, out int id) ? matrices.Influences(id) : [];
        for (int i = 0; i < influences.Length && i < 4; i++) { v.BoneIds[i] = influences[i].Bone; v.Weights[i] = influences[i].Weight; }
        return v;
    }

    // The matrix a vertex's position-matrix index selects: whatever matrix ID the stream last loaded into that slot.
    Matrix4x4 NodeMatrix(int slot) => _nodeByAddress.TryGetValue(slot * 4, out int id) ? matrices.Get(id) : Matrix4x4.Identity;
}
