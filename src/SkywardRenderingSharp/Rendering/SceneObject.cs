using System.Numerics;
using System.Text.RegularExpressions;
using SkywardRenderingSharp.Animation;
using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Rendering;

/// <summary>Something loaded by model ID: a main model and the models that attach to its skeleton, ready to place, pose and draw.</summary>
/// <remarks>A part whose bones are named after joints of the main skeleton (a head's "head_root", a hands model's "al_handsL") follows that joint. Alternate
/// variants of a part (seven hand poses, A to G) are drawn as their first only; <see cref="Variants"/> says which are hidden.</remarks>
public sealed class SceneObject
{
    readonly List<Part> _parts = [];

    public string Id { get; }
    public LoadedModel Main { get; }
    public AnimationSet Animations { get; }

    sealed record Part(LoadedModel Model, int[] JointOfBone, HashSet<int> HiddenDraws);

    internal SceneObject(string id, LoadedModel main, IEnumerable<(LoadedModel Model, int[] Joints)> attachments, AnimationSet animations)
    {
        Id = id; Main = main; Animations = animations;
        _parts.Add(new Part(main, [], Variants.Hidden(main.Model)));
        foreach (var (model, joints) in attachments) _parts.Add(new Part(model, joints, Variants.Hidden(model.Model)));
    }

    public IReadOnlyList<LoadedModel> Models => _parts.Select(p => p.Model).ToList();

    public Pose BindPose => Pose.Bind(Main.Model.Bones);

    /// <summary>The instances that draw the object at a placement. The pose skins the main model; parts follow its joints.</summary>
    public IReadOnlyList<ModelInstance> Instances(Matrix4x4 world, Pose? pose = null, IReadOnlyDictionary<string, string>? textureOverrides = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, TextureSrt>>? textureSrt = null)
    {
        pose ??= BindPose;
        var result = new List<ModelInstance> { new(Main, world, pose, textureOverrides, (d, m) => !_parts[0].HiddenDraws.Contains(IndexOf(Main, d)), textureSrt) };
        foreach (var part in _parts.Skip(1))
        {
            foreach (int joint in part.JointOfBone.Where(j => j >= 0).Distinct().DefaultIfEmpty(-1))
            {
                var attach = joint < 0 ? world : pose.World[joint] * world;
                var p = part;
                result.Add(new ModelInstance(part.Model, attach, null, textureOverrides, (d, m) => !p.HiddenDraws.Contains(IndexOf(p.Model, d)) && DrawFollows(p, d, joint), textureSrt));
            }
        }
        return result;
    }

    static int IndexOf(LoadedModel model, DrawCall draw) => model.Model.Draws.ToList().IndexOf(draw);

    // A draw follows the joint its bone (or the nearest mapped ancestor) maps to; with no mapping at all the part draws as a whole.
    static bool DrawFollows(Part part, DrawCall draw, int joint)
    {
        if (joint < 0) return true;
        int bone = draw.Bone;
        return bone < part.JointOfBone.Length && part.JointOfBone[bone] == joint;
    }
}

/// <summary>Derives which main-skeleton joint each bone of an attached model hangs from, from the bones' names.</summary>
static class Attachments
{
    /// <summary>The candidates that hang from the main skeleton, with the joint of each of their bones (-1 where none).</summary>
    /// <remarks>A model that repeats most of the main skeleton's bone names is an alternate of it, not a part. A part rooted at "x_root" that matches no joint adopts the joint
    /// of a sibling "_root" part whose root does match one (the face follows the head). Of several parts with identical skeletons only one is kept, preferring the one named like the main model.</remarks>
    public static List<(LoadedModel Model, int[] Joints)> Select(LoadedModel main, IReadOnlyList<LoadedModel> candidates)
    {
        var names = main.Model.Bones.Select(b => b.Name.ToLowerInvariant()).ToHashSet();
        var mapped = candidates
            .Where(c => c.Model.Bones.Count(b => names.Contains(b.Name.ToLowerInvariant())) <= main.Model.Bones.Count / 2)
            .Select(c => (Model: c, Joints: JointsOfBones(c.Model.Bones, main.Model.Bones))).ToList();
        string prefix = main.Model.Name.Split('_')[0];
        foreach (var part in mapped.Where(m => m.Joints.All(j => j < 0) && IsRoot(m.Model)))
        {
            var sibling = mapped.FirstOrDefault(q => q.Joints[0] >= 0 && IsRoot(q.Model));
            if (sibling.Joints is not null) Array.Fill(part.Joints, sibling.Joints.First(j => j >= 0));
        }
        return mapped.Where(m => m.Joints.Any(j => j >= 0))
            .GroupBy(m => string.Join(',', m.Model.Model.Bones.Select(b => b.Name).Order()))
            .Select(g => g.OrderByDescending(m => m.Model.Model.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).First()).ToList();
    }

    static bool IsRoot(LoadedModel m) => m.Model.Bones[0].Name.EndsWith("_root", StringComparison.OrdinalIgnoreCase);

    public static int[] JointsOfBones(IReadOnlyList<Bone> part, IReadOnlyList<Bone> main)
    {
        var byName = main.ToDictionary(b => b.Name.ToLowerInvariant(), b => b.Index);
        var joints = new int[part.Count];
        foreach (var bone in part)
        {
            int joint = Candidates(bone.Name).Select(n => byName.GetValueOrDefault(n, -1)).FirstOrDefault(j => j >= 0, -1);
            joints[bone.Index] = joint >= 0 ? joint : bone.Parent >= 0 ? joints[bone.Parent] : -1;
        }
        return joints;
    }

    // "head_root" is the head joint; "al_handsL" is handL; "HandLRoot" is HandL: drop a root suffix, then leading "xx_" prefixes, then a plural "hands".
    static IEnumerable<string> Candidates(string name)
    {
        name = name.ToLowerInvariant();
        yield return name;
        string stem = name.EndsWith("_root", StringComparison.Ordinal) ? name[..^5] : name.EndsWith("root", StringComparison.Ordinal) ? name[..^4] : name;
        yield return stem;
        // Head parts stored as their own objects (Zelda_face, Zelda_hair) are rooted at a bone named for the part and hang from the head joint.
        if (stem is "face" or "hair" or "hat" or "cap" or "ear") yield return "head";
        for (int i = stem.IndexOf('_'); i >= 0; i = stem.IndexOf('_', i + 1))
        {
            yield return stem[(i + 1)..];
            yield return stem[(i + 1)..].Replace("hands", "hand").Replace("feet", "foot");
        }
    }
}

/// <summary>Finds alternate variants of a part (the seven hand poses): draws whose material names differ only by one capital letter A to G.</summary>
static class Variants
{
    static readonly Regex Letter = new("(?<=[a-z])[A-G](?=(_m)?$)", RegexOptions.Compiled);

    public static HashSet<int> Hidden(Mdl0Model model)
    {
        var hidden = new HashSet<int>();
        var groups = Enumerable.Range(0, model.Draws.Count)
            .GroupBy(i => Letter.Replace(model.Materials[model.Draws[i].Material].Name, "?"))
            .Where(g => g.Key.Contains('?') && g.Select(i => model.Materials[model.Draws[i].Material].Name).Distinct().Count() >= 3);
        foreach (var g in groups)
        {
            string keep = g.Select(i => model.Materials[model.Draws[i].Material].Name).Min()!;
            foreach (int i in g.Where(i => model.Materials[model.Draws[i].Material].Name != keep)) hidden.Add(i);
        }
        return hidden;
    }
}
