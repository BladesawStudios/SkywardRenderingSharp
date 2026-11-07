using System.Buffers.Binary;
using System.Numerics;

namespace SkywardRenderingSharp.Lighting;

/// <summary>One light object from the game's .blight file.</summary>
/// <remarks>Kind 1 is a directional light (Vector is its direction towards the light), kind 0x401 an ambient colour. Color is RGBA bytes.</remarks>
public readonly record struct LightObject(uint Kind, uint Flags, Vector3 Vector, uint Color)
{
    public bool IsDirectional => Kind == 1;
    public bool IsAmbient => (Kind & 0x400) != 0;
    public Vector3 Rgb => new((Color >> 24 & 255) / 255f, (Color >> 16 & 255) / 255f, (Color >> 8 & 255) / 255f);
}

/// <summary>The game's .blight file (System.arc dat/default.blight): the light objects the stages' light sets pick from.</summary>
public sealed class LightFile
{
    const int ObjectSize = 0x50;

    // The file lists 16 light sets of eight objects each.
    public const int SetSize = 8;

    public IReadOnlyList<LightObject> Lights { get; }

    public IEnumerable<LightObject> Set(int index) => Lights.Skip(index * SetSize).Take(SetSize);

    LightFile(IReadOnlyList<LightObject> lights) { Lights = lights; }

    public static LightFile Open(ReadOnlySpan<byte> file)
    {
        if (!file[..4].SequenceEqual("LGHT"u8)) throw new InvalidDataException("Not a .blight file.");
        var lights = new List<LightObject>();
        for (int p = 0x28; p + ObjectSize <= file.Length && file.Slice(p, 4).SequenceEqual("LOBJ"u8); p += ObjectSize)
        {
            var o = file.Slice(p, ObjectSize);
            lights.Add(new LightObject(BinaryPrimitives.ReadUInt32BigEndian(o[0x10..]), BinaryPrimitives.ReadUInt32BigEndian(o[0x14..]),
                new Vector3(BinaryPrimitives.ReadSingleBigEndian(o[0x18..]), BinaryPrimitives.ReadSingleBigEndian(o[0x1C..]), BinaryPrimitives.ReadSingleBigEndian(o[0x20..])),
                BinaryPrimitives.ReadUInt32BigEndian(o[0x34..])));
        }
        return new LightFile(lights);
    }
}
