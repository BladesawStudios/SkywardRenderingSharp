using System.Numerics;
using SkywardRenderingSharp.Gx;
using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Rendering;

/// <summary>Builds the two matrices of a texgen from its material's texture-matrix data: the first transform, and the post transform after normalisation.</summary>
/// <remarks>UV layers (map mode 0) use their SRT as the first transform. Environment layers feed the view-space normal through the effect matrix, normalise it,
/// and map it into the texture with the post matrix (scale one half, bias one half, v flipped), the sphere-map form the game's lightmaps and metals use.
/// Modes 2 to 4 (projection, light and specular environments) are treated like the camera environment. The shader multiplies column vectors, so each
/// matrix is returned transposed (System.Numerics stores row vectors).</remarks>
static class TextureMatrices
{
    public static (Matrix4x4 First, Matrix4x4 Post) For(TexGenInfo gen, TextureMatrix t)
    {
        if (gen.SourceRow >= 5 && t.MapMode == 0) return (Transposed(Srt(t)), Matrix4x4.Identity);
        if (gen.SourceRow >= 5) return (Matrix4x4.Identity, Matrix4x4.Identity);
        if (t.MapMode == 0) return (Matrix4x4.Identity, Matrix4x4.Identity);
        var sphere = new Matrix4x4(0.5f, 0, 0, 0.5f, 0, -0.5f, 0, 0.5f, 0, 0, 0, 1, 0, 0, 0, 1);
        return (t.Effect, Transposed(sphere));
    }

    // Maps (u, v, 1): scale, then rotate, then translate.
    static Matrix4x4 Srt(TextureMatrix t)
    {
        float c = MathF.Cos(t.Rotation * MathF.PI / 180f), s = MathF.Sin(t.Rotation * MathF.PI / 180f);
        return new Matrix4x4(t.Scale.X * c, -t.Scale.Y * s, 0, t.Translation.X, t.Scale.X * s, t.Scale.Y * c, 0, t.Translation.Y, 0, 0, 1, 0, 0, 0, 0, 1);
    }

    // The matrices above are written in column-vector form; the binder uploads System.Numerics matrices untransposed, so hand back the transpose.
    static Matrix4x4 Transposed(Matrix4x4 columnForm) => Matrix4x4.Transpose(columnForm);
}
