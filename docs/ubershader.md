# The uber shader

The game builds every material shader from one encrypted template (`romfs/Shaders/Dummy.vert|frag`) by prepending `#define`s, and caches
the results (4192 programs in `Shaders.arc`). The defines are the registers of the Wii's GX pipeline. This library implements the same pipeline
once, reading those registers as uniforms (`Glsl/uber.vert`, `uber.frag`).

| Game define | Here |
|---|---|
| `TEV_COUNT`, `TEV_n_COLOR_IN_x`, `_OP`, `_BIAS_SCALE`, `_CLAMP`, `_OUT` (and `ALPHA`) | `uColorInputs/Ops`, `uAlphaInputs/Ops`, `uStageDest` |
| `TEV_n_RAS_INPUT`, `_TEXMAP_INPUT`, `_RAS/TEX_COLOR_SWAP` | `uStageRas`, `uStageDest.z/w`, `uSwap` |
| TEV constants (`E0..E7`, `KSEL`) | `uTevRegister`, `uKonst`, `uStageRas.w`, `uStageKonst.x` |
| `TEXGEN_COUNT`, `TEXGEN n SOURCE/FUNCTION` | `uTexGen`, `uTexMatrix`, `uPostMatrix` |
| `COLOR_COUNT`, `COLOR_MAT_SRC/AMB_SRC/ENABLE`, `LIGHT_COUNT`, `CHANn_*ATTEN` | `uChannelControl` (raw XF word), `uMaterialColor`, `uAmbientColor`, `uLight*` |
| `ALPHATEST_*` | `uAlphaTest`, `uAlphaLogic` |
| blend, depth, cull | GL state, set by `PixelStateApplier` |

The combiner equation is the hardware's: `out = (d + (sub ? -1 : 1) * lerp(a, b, c) + bias) * scale`, clamped. Input codes are the GX ones.

## Behaviour worth knowing

- **The output register.** The game takes a material's colour and alpha from the register its *last stage* wrote (several programs end in
  register 3, not PREV). The shader outputs `reg[uOutputRegister.x].rgb` and `reg[uOutputRegister.y].a`.
- **Lighting is small.** Character materials add the vertex-lit colour onto the albedo (`2 * (albedo * shade + 0.5 * (lit + ...))`), so
  `LightRig` intensities are low by design. The `LmCha*` lightmap textures are filled by the engine from the stage's `.blmap`; the shared
  default is flat, so Multi and Skin resolve to white and Add to black.
- **Texgen matrices.** The texture matrices the engine feeds normal-sourced texgens (the lightmap lookups) are not recovered; they are identity.

- **Winding.** GX front faces are clockwise in GL terms (`glFrontFace(GL_CW)`); with the usual counter-clockwise convention every surface shows its inside.

## Not covered

Indirect textures, fog, emboss and colour texgens, spot/point light attenuation and per-vertex texture matrix indices are decoded by the
game's template but not implemented here.
