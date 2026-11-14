using System.Buffers.Binary;
using System.Numerics;
using Res = BresSharp.Mdl0;

namespace SkywardRenderingSharp.Model;

/// <summary>Resolves the matrix IDs a polygon's primitive stream loads into a bind-pose matrix and a set of bone influences.</summary>
/// <remarks>The model's NodeMix definition binds IDs to bones (op 0x05 [matrix ID][bone]: a rigid bone, whose vertices are bone-local, so the bind matrix is its
/// world transform) and defines blended IDs (op 0x03: weights over matrix IDs, whose vertices are model-space, so the bind-pose matrix is the identity).</remarks>
sealed class NodeMatrices
{
    const byte Blend = 0x03, Rigid = 0x05;

    readonly Dictionary<int, Matrix4x4> _byId = [];
    readonly Dictionary<int, int> _rigidBone = [];
    readonly Dictionary<int, (int Id, float Weight)[]> _mixes = [];
    readonly Dictionary<int, (int Bone, float Weight)[]> _influences = [];

    // A blended id (or an unknown one) is the bind-pose skin matrix, the identity.
    public Matrix4x4 Get(int id) => _byId.GetValueOrDefault(id, Matrix4x4.Identity);

    /// <summary>The bone a rigid polygon's node field selects: it names a matrix id of the rigid table (ids skip some bones), so Fi's jewels with node 7 follow bone 9.</summary>
    public int RigidBone(int id, int boneCount) => _rigidBone.TryGetValue(id, out int bone) ? bone : id < boneCount ? id : -1;

    public (int Bone, float Weight)[] Influences(int id) => _influences.GetValueOrDefault(id, []);

    public static NodeMatrices Read(Res.Mdl0Model model, IReadOnlyList<Bone> bones)
    {
        var result = new NodeMatrices();
        foreach (var definition in model.Definitions.Where(d => d.Name == "NodeMix"))
            foreach (var command in definition.Commands) result.Add(command, bones);
        result.FillSkippedLeaves(bones);
        result.ResolveInfluences();
        return result;
    }

    void Add(Res.Mdl0DefinitionCommand command, IReadOnlyList<Bone> bones)
    {
        var a = command.Args;
        if (command.Op == Rigid)
        {
            int id = BinaryPrimitives.ReadUInt16BigEndian(a), bone = BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(2));
            if (bone < bones.Count) { _byId[id] = bones[bone].World; _rigidBone[id] = bone; }
        }
        else if (command.Op == Blend)
            _mixes[BinaryPrimitives.ReadUInt16BigEndian(a)] = Enumerable.Range(0, a[2])
                .Select(i => ((int)BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(3 + 6 * i)), BinaryPrimitives.ReadSingleBigEndian(a.AsSpan(5 + 6 * i)))).ToArray();
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
}
