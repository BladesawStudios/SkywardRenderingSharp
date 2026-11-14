using System.Numerics;
using RevolutionEggSharp;

namespace SkywardRenderingSharp.Lighting;

/// <summary>One light object from the game's .blight file.</summary>
/// <remarks>Kind is the object's four type bytes (angle and distance falloff, coordinate space, light type): 1 is a directional light (Vector is its direction towards the
/// light), 0x401 one in coordinate space 4, which the renderer treats as an ambient colour. Flags is the object's index and flag words. Color is RGBA bytes.</remarks>
public readonly record struct LightObject(uint Kind, uint Flags, Vector3 Vector, uint Color)
{
    public bool IsDirectional => Kind == 1;
    public bool IsAmbient => (Kind & 0x400) != 0;
    public Vector3 Rgb => new((Color >> 24 & 255) / 255f, (Color >> 16 & 255) / 255f, (Color >> 8 & 255) / 255f);
}

/// <summary>The game's .blight file (System.arc dat/default.blight), read through RevolutionEggSharp: the light objects the stages' light sets pick from.</summary>
public sealed class LightFile(EggLight file)
{
    // The file lists 16 light sets of eight objects each.
    public const int SetSize = 8;

    public IReadOnlyList<LightObject> Lights { get; } = file.Lights.Select(l => new LightObject(
        (uint)(l.AngleFalloff << 24 | l.DistanceFalloff << 16 | l.CoordinateSpace << 8 | (byte)l.Type), (uint)(l.Index << 16 | l.Flags), l.Origin, l.Color)).ToList();

    public IEnumerable<LightObject> Set(int index) => Lights.Skip(index * SetSize).Take(SetSize);
}
