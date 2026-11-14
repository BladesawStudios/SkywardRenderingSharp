# Architecture

Layers, bottom to top. Each only depends on the ones above it in this list.

| Folder | Role |
|---|---|
| `Archives` | `Lz11` decompression and the `U8Archive` (.arc) reader. |
| `Textures` | `XtxFile` (Tegra container, block-linear deswizzle, ASTC), `WiiTexture` (I4/I8/IA4/IA8/RGB565/RGB5A3/RGBA8/CMPR). |
| `Brres` | Big-endian reads, resource dictionaries, `BresFile` (models and textures by name). |
| `Gx` | `GxState`: the BP/XF/CP register files written by GX command streams, and typed views of them (`TevStage`, `TevColors`, `TexGenInfo`, `ChannelControl`, `PixelState`). |
| `Model` | `Mdl0Model`: bones, materials (`MaterialReader`), polygons (`MeshDecoder`, `VertexArrays`, `NodeMatrices`) and the draw list. Vertices keep bind-space positions and up to four bone influences. |
| `Animation` | `Pose` (skin matrices), `ChrClip` (bone animation), `PatClip` (texture patterns), `KeyTrack`, `AnimationLibrary`. |
| `Characters` | `LinkRig`: Link's separate models attached to one skeleton. |
| `Shaders` | `NvProgramBinary` (the game's compiled GL programs), `MaxwellDecompiler` (Ryujinx's translator, from the ShaderLibrary submodule) and `GameGlsl` (decompiled GLSL made to compile in a 4.3 context). |
| `Resources` | `SkywardRom` (an extracted romfs), `ModelTextures`, `PostParameters`. |
| `Lighting` | `LightFile` (.blight light sets), `LightRig`, `LightmapFile` (.blmap curves). |
| `Gpu` | GL helpers: `GlProgram`, `GlTexture`, `GlMesh`, `RenderTarget`, `SamplerCache`. |
| `Rendering` | `ModelDrawer` + `MaterialBinder` (the uber shader), `PixelStateApplier`, `PostChain` + `GamePostPrograms` + `DepthRamps`, `Camera`, `RenderSettings`. |
| `Glsl` | `uber.vert/frag` and `post/*` (embedded): the vertex shader the game's post programs run with, the depth-of-field mix and the composite. |

## A frame

1. Scene target (RGBA16F colour + depth texture) is cleared to the sky colour.
2. `ModelDrawer` draws every instance (front faces are clockwise, as GX counts them): opaque draws first, then translucent by priority. The vertex shader skins
   the bind-space vertices with the instance's pose. Per draw it applies the material's blend/depth/cull state,
   packs its TEV stages, texgens, light channels and constants into uniforms, binds the layer textures with their sampler state, and draws.
3. `PostChain` runs the game's own post programs, decompiled from `Shaders/custom` when the renderer starts: depth of field (`GaussianBlur_*`,
   `Dof_FogToAlpha`, `Dof_MaskNear`, mixed by `dof_mix.frag`), the **paint** image (`Blit_RGBA`, then `Paint_Constant` and `_NewKernel` in turn),
   laid over the frame at a constant alpha (`Paint_Constant`) and swapped in by distance (`Paint_NearFar`), and **bloom** (`BloomMask_Custom` into an
   8-bit target, blurred twice). `DepthRamps` turns eye distances into the constants the depth ramps read.
4. The composite pass adds fog (not on the sky) and the bloom layers and encodes to sRGB.

## Models

A model is read once into CPU data (`Mdl0Model`): meshes are expanded into bind-space `MeshVertex` arrays carrying their bone influences, and
`LoadedModel` uploads them (textures on first use). A `Pose` turns local bone transforms, from the bind pose or a `ChrClip`, into skin matrices
(inverse bind, then animated world) that the shader blends.
