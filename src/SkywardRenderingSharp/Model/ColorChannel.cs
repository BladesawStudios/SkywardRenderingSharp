using SkywardRenderingSharp.Gx;

namespace SkywardRenderingSharp.Model;

/// <summary>One of a material's two colour channels: its material and ambient colours (RGBA bytes) and lighting controls.</summary>
public sealed record ColorChannel(uint MaterialColor, uint AmbientColor, ChannelControl Color, ChannelControl Alpha)
{
    public static ColorChannel Default { get; } = new(0xFFFFFFFF, 0xFF, ChannelControl.Read(0), ChannelControl.Read(0));
}
