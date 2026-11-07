namespace SkywardRenderingSharp.Model;

/// <summary>One texture slot of a material: which texture, the TEV texture map it feeds, and its sampler state.</summary>
/// <remarks>Wrap: 0 clamp, 1 repeat, 2 mirror. Filters: 0 nearest, 1 linear, 2..5 the mip variants.</remarks>
public sealed record MaterialLayer(string TextureName, int TexMap, int WrapS, int WrapT, int MinFilter, int MagFilter);
