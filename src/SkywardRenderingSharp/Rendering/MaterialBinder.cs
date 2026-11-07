using System.Numerics;
using SkywardRenderingSharp.Gpu;
using SkywardRenderingSharp.Model;

namespace SkywardRenderingSharp.Rendering;

/// <summary>Packs a <see cref="Material"/>'s GX state into the uber shader's uniforms.</summary>
/// <remarks>Packing buffers are reused between calls. A channel whose light mask is empty gets the frame's lights, as the game assigns them per draw.</remarks>
sealed class MaterialBinder
{
    const int Stages = 16, TexGens = 8;

    readonly int[] _colorInputs = new int[Stages * 4], _colorOps = new int[Stages * 4], _alphaInputs = new int[Stages * 4], _alphaOps = new int[Stages * 4];
    readonly int[] _dest = new int[Stages * 4], _ras = new int[Stages * 4], _konst = new int[Stages * 4], _texGen = new int[TexGens * 4];
    readonly Matrix4x4[] _first = new Matrix4x4[TexGens], _post = new Matrix4x4[TexGens];

    public void Bind(GlProgram p, Material m, uint lightMask, System.Numerics.Vector3 sceneAmbient, IReadOnlyDictionary<int, Animation.TextureSrt>? srt = null,
        IReadOnlyDictionary<int, Vector4>? colorOverrides = null)
    {
        PackStages(m);
        p.SetInt4s("uColorInputs", _colorInputs); p.SetInt4s("uColorOps", _colorOps);
        p.SetInt4s("uAlphaInputs", _alphaInputs); p.SetInt4s("uAlphaOps", _alphaOps);
        p.SetInt4s("uStageDest", _dest); p.SetInt4s("uStageRas", _ras); p.SetInt4s("uStageKonst", _konst);
        p.Set("uStageCount", m.Stages.Count);
        p.Set("uIrradianceMap", m.Layers.FirstOrDefault(l => l.TextureName == "SSS")?.TexMap ?? -1);
        var last = m.Stages[^1];
        p.SetInts2("uOutputRegister", [last.Color.Dest, last.Alpha.Dest]);
        BindColors(p, m, colorOverrides);
        BindTexGens(p, m, srt);
        BindChannels(p, m, lightMask, sceneAmbient);
        var px = m.Pixel;
        p.SetInt4s("uAlphaTest", [px.AlphaRef0, px.AlphaRef1, px.AlphaComp0, px.AlphaComp1]);
        p.Set("uAlphaLogic", px.AlphaLogic);
    }

    void PackStages(Material m)
    {
        for (int i = 0; i < m.Stages.Count; i++)
        {
            var s = m.Stages[i];
            Put(_colorInputs, i, s.Color.A, s.Color.B, s.Color.C, s.Color.D);
            Put(_colorOps, i, s.Color.Bias, s.Color.Subtract ? 1 : 0, s.Color.Clamp ? 1 : 0, s.Color.Scale);
            Put(_alphaInputs, i, s.Alpha.A, s.Alpha.B, s.Alpha.C, s.Alpha.D);
            Put(_alphaOps, i, s.Alpha.Bias, s.Alpha.Subtract ? 1 : 0, s.Alpha.Clamp ? 1 : 0, s.Alpha.Scale);
            bool textured = s.TexEnabled && s.TexCoord < m.TexGens.Count;
            Put(_dest, i, s.Color.Dest, s.Alpha.Dest, textured ? s.TexMap : -1, textured ? s.TexCoord : 0);
            Put(_ras, i, s.RasChannel, s.RasSwap, s.TexSwap, s.KonstColor);
            Put(_konst, i, s.KonstAlpha, textured && m.TexGens[s.TexCoord].Projective ? 1 : 0, 0, 0);
        }
    }

    static void Put(int[] a, int i, int x, int y, int z, int w) { a[i * 4] = x; a[i * 4 + 1] = y; a[i * 4 + 2] = z; a[i * 4 + 3] = w; }

    static void BindColors(GlProgram p, Material m, IReadOnlyDictionary<int, Vector4>? overrides)
    {
        var regs = new float[16]; var konst = new float[16]; var swap = new int[16];
        for (int i = 0; i < 4; i++)
        {
            (overrides is not null && overrides.TryGetValue(i, out var replaced) ? replaced : m.Colors.Registers[i]).CopyTo(regs.AsSpan(i * 4)); (overrides is not null && overrides.TryGetValue(4 + i, out var konstReplaced) ? konstReplaced : m.Colors.Konst[i]).CopyTo(konst.AsSpan(i * 4));
            var t = m.Colors.SwapTables[i];
            (swap[i * 4], swap[i * 4 + 1], swap[i * 4 + 2], swap[i * 4 + 3]) = ((int)t.X, (int)t.Y, (int)t.Z, (int)t.W);
        }
        p.SetVec4s("uTevRegister", regs); p.SetVec4s("uKonst", konst); p.SetInt4s("uSwap", swap);
    }

    void BindTexGens(GlProgram p, Material m, IReadOnlyDictionary<int, Animation.TextureSrt>? srt)
    {
        for (int i = 0; i < m.TexGens.Count; i++)
        {
            var g = m.TexGens[i];
            Put(_texGen, i, g.SourceRow, g.Projective ? 1 : 0, 0, g.Normalize ? 1 : 0);
        }
        p.Set("uTexGenCount", m.TexGens.Count);
        p.SetInt4s("uTexGen", _texGen);
        for (int i = 0; i < TexGens; i++) (_first[i], _post[i]) = i < m.TexGens.Count && i < m.TextureMatrices.Count ? TextureMatrices.For(m.TexGens[i], srt is not null && srt.TryGetValue(i, out var animated) ? m.TextureMatrices[i] with { Scale = animated.Scale, Rotation = animated.Rotation, Translation = animated.Translation } : m.TextureMatrices[i]) : (Matrix4x4.Identity, Matrix4x4.Identity);
        p.SetMatrices("uTexMatrix", _first);
        p.SetMatrices("uPostMatrix", _post);
    }

    // A channel's ambient is the material's stored ambient colour times the scene's ambient light (characters store black, stage materials white).
    // TODO: confirm against the game's ambient setup; the product matches both authoring conventions.
    static void BindChannels(GlProgram p, Material m, uint lightMask, System.Numerics.Vector3 sceneAmbient)
    {
        var controls = new uint[4]; var material = new float[8]; var ambient = new float[8];
        for (int c = 0; c < 2; c++)
        {
            var ch = c < m.Channels.Count ? m.Channels[c] : ColorChannel.Default;
            controls[c * 2] = Control(ch.Color, lightMask); controls[c * 2 + 1] = Control(ch.Alpha, lightMask);
            Rgba(ch.MaterialColor).CopyTo(material, c * 4); { var a = Rgba(ch.AmbientColor); ambient[c * 4] = a[0] * sceneAmbient.X; ambient[c * 4 + 1] = a[1] * sceneAmbient.Y; ambient[c * 4 + 2] = a[2] * sceneAmbient.Z; ambient[c * 4 + 3] = a[3]; }
        }
        p.SetUInts("uChannelControl", controls); p.SetVec4s("uMaterialColor", material); p.SetVec4s("uAmbientColor", ambient);
    }

    // Rebuilds the XF channel control word, filling an empty light mask when lighting is on.
    static uint Control(Gx.ChannelControl c, uint fallbackMask)
    {
        uint mask = c.LightMask == 0 ? fallbackMask : c.LightMask;
        return (c.MaterialFromVertex ? 1u : 0) | (c.LightingEnabled ? 2u : 0) | (mask & 15) << 2 | (c.AmbientFromVertex ? 64u : 0)
            | (uint)c.DiffuseFunction << 7 | (mask >> 4 & 15) << 11;
    }

    static float[] Rgba(uint c) => [(c >> 24 & 255) / 255f, (c >> 16 & 255) / 255f, (c >> 8 & 255) / 255f, (c & 255) / 255f];
}
