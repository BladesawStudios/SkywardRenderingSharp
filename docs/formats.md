# Formats, as decoded

Everything here was verified against the game's data (a model's header counts, bounds and triangle edge lengths), not taken from a spec.

## Archives

`Object/NX/ObjectPack.arc.LZ` is LZ11 around a U8 archive; each `oarc/<Name>.arc` inside is another U8 holding `g3d/model.brres` and, for
most objects, `g3d/model.xtx`. `Object/NX/System.arc` holds the shared data: `dat/default.blight` (lights), `dat/default.bblm` (bloom, `PBLM`),
`dat/default.bdfp` (paint, `BDFP`), `dat/default.blmap` (lightmaps, `LMAP`), `dat/opning.bdof` (depth of field, cutscene only).

## XTX

`DFvN` header, then `HBvN` blocks (36-byte headers, little-endian): type 2 = texture info, type 3 = data. Info: data size u64, alignment, width, height,
depth, target, format, mips, slice size, 17 mip offsets, then a layout word whose low three bits are log2 of the block height. Data is Tegra
block-linear. Formats seen: ASTC 4x4 (0x79) and 6x6 (0x7D); BC1-BC5, RGBA8 and 16-bit formats are decoded too. **Stub TEX0 entries (1x1) in the BRRES take their pixels from the XTX in order.**

## BRRES (big-endian)

Resource dictionaries are 16-byte entries. `3DModels` holds MDL0, `Textures` holds TEX0 (inline Wii formats for those that did not move to XTX).

MDL0 (v11): a table of section offsets at +0x10 (definitions, bones, vertices, normals, colours, UVs, fur x2, materials, TEV, polygons, texture refs).

- **Bone**: index at +0x0C, world transform (3x4) at +0x70, inverse bind at +0xA0.
- **Vertex arrays**: data offset +8, format +0x18, divisor +0x1C, stride +0x1D, count +0x1E.
- **Definitions** are byte code. `DrawOpa/DrawXlu`: `04 [material u16][polygon u16][bone u16][priority u8]`. `NodeMix`:
  `05 [matrix id u16][bone u16]` binds a **rigid** matrix id to a bone; `03 [mix id u16][n u8] (matrix id u16, weight f32)*` defines a **blended** id.
- **Polygon**: node id +0x08 (-1 = weighted), CP setup block at `+0x20 + [+0x20]` (VCD low/high, VAT), array ids (s16) at +0x48, primitive data
  at the end of the object (`length - [+0x24]`). The primitive stream interleaves indexed matrix loads (`30 [matrix id u16][len|addr u16]`),
  BP/XF/CP commands and primitives (`0x80` quads, `0x90` triangles, `0x98` strips, `0xA0` fans).
- **Matrices.** A vertex's position-matrix byte times four is an XF address; the stream's last load to that address names a matrix id. A rigid id
  means the vertex is bone-local and its matrix is the bone's world transform; a blended id means the vertex is already in model space
  (the bind-pose skin matrix is the identity). The rigid table omits some leaf bones (the toes, ids 10 and 14): an omitted id is the bone after the previous id's bone.
  A matrix with a negative determinant reverses triangle winding. Each vertex's skin influences are its rigid bone, or the bones of its blend's component ids.
- **Material**: counts at +0x14 (texgens, channels, stages), cull +0x18, TEV record at `+[+0x28]`, layers at `+[+0x30]` (0x34 bytes each; the name offset is relative to
  the layer), pixel and XF command streams at `+[+0x3C]` (0xE0 then 0xA0 bytes), channel block at +0x3F0 (material, ambient, colour and alpha control).
- **GX state** is the BP/XF register writes in those streams. BP `0xFE` masks the next write. Registers `0xE0..0xE7` are written twice, as colour registers and as
  constants (bit 23). The blend register's colour/alpha update bits default to on.

## Animation (CHR0)

`External` group entries of `PLCommonAnime` (`*.rchaC`, `*.rtpaC`) are LZ11-compressed one-clip BRRES files. A CHR0 has frames at +0x20, a node dictionary at +0x2C.
A node entry is `[name][flags]` then, for each group present, three words (x, y, z): a float if the component's const bit is set, else an offset (from the entry)
to a keyframe track.

- Flags: bit 1 identity; bits 3, 5, 6 mean scale one, rotation zero, translation zero (that group has no words); const bits 13-15 scale, 16-18 rotation, 19-21 translation;
  key format bits 25/26 scale, 27/28 rotation, 30/31 translation. Neither bit of a pair set = a **linear byte track**; the first bit = 32-bit keys; the second = 48-bit keys; both = float keys.
- Linear track: `step f32, base f32`, then `frames + 1` bytes; value = base + step * byte.
- 32-bit keys: header `count u16, pad, frameScale f32, step f32, base f32`, then a u32 per key: frame (8 bits), value (12), tangent (12, signed, /32); Hermite interpolation.
- Local transform = scale, rotate X then Y then Z (degrees), translate; world = local * parent world.

PAT0 (`*.rtpaC`): frames at +0x30, material count +0x32, texture count +0x34, texture-name table at +0x14, material dictionary at +0x10. A material record holds
the offset (word 2) of a key list: `count u16, pad, frameScale f32`, then `(frame f32, texture u16, palette u16)` keys; a key holds until the next.

## Lightmaps and parameters

`.blight`: `LGHT`, then 128 `LOBJ` objects of 0x50 bytes (16 sets of eight): kind (+0x10: 1 directional, 0x401 ambient), direction (+0x18), RGBA colour (+0x34).
`.blmap`: `LMAP`, then `LTEX` entries (0x488 bytes): name at +0x14, a 128-entry curve of float pairs in the last 0x400 bytes.
`.bdof`: `PDOF`, flags, then floats (far limit at +0x18).

## Post-processing parameters

`PBLM`: bloom threshold at +0x10, tint colour at +0x18. `BDFP`: floats at +0x10..+0x1C are (3300, 3300, 30, 20), read as the far paint ramp
and the near paint ramp. These meanings are inferred from the shaders that use them (see Paint_NearFar in docs), not from the engine code.

## SRT0 (texture SRT animation)

Header frame count at +0x20, material dictionary at +[0x10]. A material record is `[name][texture mask][indirect mask]` then one offset per enabled texture (relative to the
record). An entry is a flags word (bit 1 scale is one, bit 2 rotation is zero, bit 3 translation is zero, bit 4 uniform scale, bits 5-6 scale u/v constant, bit 7 rotation
constant, bits 8-9 translation u/v constant) then one word per present component: a float when constant, else an offset relative to the word to a format-3 key track.
Eye gaze is the translation of texture 0 of `al_eyeballL_m`/`al_eyeballR_m`.

## Stage archives

`Stage/<S>/NX/<S>_stg_l0.arc.LZ` holds `g3d/stage.brres|xtx` (props and a texture pool), `rarc/<S>_rNN.arc` rooms (`g3d/room.brres`, model `model0` and its shadow twin
`model0_s`, no textures of its own), `oarc/*.arc` objects (`Vrbox03` sky, clouds) and `palette/palette.spf|sff|eaf`. Room textures resolve through the stage pool and
the object pack's `SkyCmn` package (`F000_WK2_*`, `VR_WK2_*`).
