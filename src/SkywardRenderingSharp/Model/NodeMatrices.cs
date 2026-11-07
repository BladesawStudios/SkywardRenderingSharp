using System.Numerics;
using SkywardRenderingSharp.Brres;

namespace SkywardRenderingSharp.Model;

/// <summary>Resolves the matrix IDs a polygon's primitive stream loads into a bind-pose matrix and a set of bone influences.</summary>
/// <remarks>The model's NodeMix byte code binds IDs to bones (op 0x05 [matrix ID][bone]: a rigid bone, whose vertices are bone-local, so the bind matrix is its
/// world transform) and defines blended IDs (op 0x03: weights over matrix IDs, whose vertices are model-space, so the bind-pose matrix is the identity).</remarks>
sealed class NodeMatrices
{
    readonly Dictionary<int, Matrix4x4> _byId = [];
    readonly Dictionary<int, int> _rigidBone = [];
    readonly Dictionary<int, (int Id, float Weight)[]> _mixes = [];
    readonly Dictionary<int, (int Bone, float Weight)[]> _influences = [];

    // A blended id (or an unknown one) is the bind-pose skin matrix, the identity.
    public Matrix4x4 Get(int id) => _byId.GetValueOrDefault(id, Matrix4x4.Identity);

    /// <summary>The bone a rigid polygon's node field selects: it names a matrix id of the rigid table (ids skip some bones), so Fi's jewels with node 7 follow bone 9.</summary>
    public int RigidBone(int id, int boneCount) => _rigidBone.TryGetValue(id, out int bone) ? bone : id < boneCount ? id : -1;

    public (int Bone, float Weight)[] Influences(int id) => _influences.GetValueOrDefault(id, []);

    public static NodeMatrices Read(BigEndian f, int dict, IReadOnlyList<Bone> bones)
    {
        var result = new NodeMatrices();
        foreach (var (name, offset) in ResDict.Read(f, dict))
        {
            if (name == "NodeMix") result.ReadMix(f, offset, bones);
        }
        result.FillSkippedLeaves(bones);
        result.ResolveInfluences();
        return result;
    }

    // The rigid table omits leaf bones such as the toes, yet polygons use their ids: ids run in bone order, so a missing id below the
    // blended range (from the first blended id up) is the bone after the previous id's bone.
    void FillSkippedLeaves(IReadOnlyList<Bone> bones)
    {
        int firstBlend = _mixes.Keys.DefaultIfEmpty(int.MaxValue).Min();
        int max = _rigidBone.Keys.Where(k => k < firstBlend).DefaultIfEmpty(-1).Max();
        for (int id = 1; id < max; id++)
        {
            if (_rigidBone.ContainsKey(id) || !_rigidBone.TryGetValue(id - 1, out int previous) || previous + 1 >= bones.Count) continue;
            _byId[id] = bones[previous + 1].World;
            _rigidBone[id] = previous + 1;
        }
    }

    // Rigid ids influence their one bone; a blend influences the bones its component ids stand for.
    void ResolveInfluences()
    {
        foreach (var (id, bone) in _rigidBone) _influences[id] = [(bone, 1f)];
        foreach (var (id, parts) in _mixes)
        {
            var resolved = parts.Where(p => _rigidBone.ContainsKey(p.Id)).Select(p => (_rigidBone[p.Id], p.Weight)).OrderByDescending(p => p.Weight).Take(4).ToArray();
            float sum = resolved.Sum(p => p.Weight);
            if (sum > 0) _influences[id] = resolved.Select(p => (p.Item1, p.Weight / sum)).ToArray();
        }
    }

    void ReadMix(BigEndian f, int p, IReadOnlyList<Bone> bones)
    {
        while (true)
        {
            byte op = f.U8(p);
            if (op == 0x05)
            {
                int id = f.U16(p + 1), bone = f.U16(p + 3);
                if (bone < bones.Count) { _byId[id] = bones[bone].World; _rigidBone[id] = bone; }
                p += 5;
            }
            else if (op == 0x03)
            {
                int count = f.U8(p + 3);
                _mixes[f.U16(p + 1)] = Enumerable.Range(0, count).Select(i => (f.U16(p + 4 + 6 * i), f.F32(p + 6 + 6 * i))).ToArray();
                p += 4 + 6 * count;
            }
            else return;
        }
    }
}
