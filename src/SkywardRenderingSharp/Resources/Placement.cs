using System.Numerics;
using BzsSharp;
using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Resources;

/// <summary>One object a stage or room places: the actor's name, its record type, layer, transform and parameters, read through BzsSharp.</summary>
/// <remarks>
/// Rotation is the record's three 16-bit angles in degrees (0x10000 is a full turn). <see cref="World"/> applies scale, then the Z, X and Y
/// rotations, then the position, the engine's ZXY order as Wii Zelda games place actors; the order is inferred, since most placements
/// turn about Y alone. <see cref="Angles"/> are the same angles raw, which some actors read as parameters. A record outside the layer
/// table (layer -1) and layer 0 are always present; the other layers are the story and time-of-day variants the game picks between.
/// </remarks>
public sealed record Placement(string Actor, string Kind, int Layer, Vector3 Position, Vector3 RotationDegrees, Vector3 Scale, uint Params1, uint Params2, ushort Id)
{
    const float Turn = 360f / 65536f;

    public (ushort X, ushort Y, ushort Z) Angles => ((ushort)MathF.Round(RotationDegrees.X / Turn), (ushort)MathF.Round(RotationDegrees.Y / Turn), (ushort)MathF.Round(RotationDegrees.Z / Turn));

    public bool AlwaysPresent => Layer <= 0;

    public Matrix4x4 World
    {
        get
        {
            Vector3 r = RotationDegrees * (MathF.PI / 180f);
            return Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationZ(r.Z) * Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y)
                * Matrix4x4.CreateTranslation(Position);
        }
    }

    public static IReadOnlyList<Placement> Read(byte[] bzs) =>
        [.. BzsFile.FromBinary(bzs).AllActors().Select(a => new Placement(a.Actor.Name, a.Node.Type.Trim(), a.Layer, a.Actor.Position,
            new Vector3(a.Actor.RotationX, a.Actor.RotationY, a.Actor.RotationZ) * Turn, a.Actor.HasScale ? a.Actor.Scale : Vector3.One,
            a.Actor.Params1, a.Actor.Params2, a.Actor.Id))];
}

/// <summary>A model a room's placements draw, with the textures it resolves through.</summary>
public sealed record PlacedModel(ActorPart Part, Mdl0Model Model, ModelTextures Textures);

/// <summary>One room of a stage, read but not uploaded: its models, the textures they resolve through, the objects it places, and the
/// models those objects draw.</summary>
/// <remarks>
/// Room geometry is authored in world coordinates, so the models are placed at the origin. Drawn lists, for each placement, the
/// <see cref="ObjectModels"/> it draws at its <see cref="Placement.World"/>, as <see cref="ActorModels"/> resolves it: only the
/// always-present placements draw, since the other layers' variants overlap. MissingParts are the parts no archive in the dump holds.
/// </remarks>
public sealed record RoomData(string Stage, string Room, IReadOnlyList<Mdl0Model> Models, ModelTextures Textures, IReadOnlyList<Placement> Placements,
    IReadOnlyList<PlacedModel> ObjectModels, IReadOnlyList<int[]> Drawn, IReadOnlyList<ActorPart> MissingParts);
