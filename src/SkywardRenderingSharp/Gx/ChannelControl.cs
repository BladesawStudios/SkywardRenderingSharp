namespace SkywardRenderingSharp.Gx;

/// <summary>A GX colour or alpha channel's lighting setup (XF color/alpha control registers).</summary>
public readonly record struct ChannelControl(bool MaterialFromVertex, bool LightingEnabled, uint LightMask, bool AmbientFromVertex,
    int DiffuseFunction, bool Attenuation, bool SpotAttenuation)
{
    public static ChannelControl Read(uint r) => new(
        (r & 1) != 0, (r >> 1 & 1) != 0, (r >> 2 & 15) | (r >> 11 & 15) << 4, (r >> 6 & 1) != 0,
        (int)(r >> 7 & 3), (r >> 9 & 1) != 0, (r >> 10 & 1) != 0);
}
