# Status

## Verified against the game's data

- **Models.** BRRES geometry decodes to the vertex and triangle counts each polygon's header declares; the skeleton's local transforms rebuild
  every stored world matrix to 1e-4. Rigid and blended matrix ids, the omitted toe ids, and mirrored matrices are handled (see formats.md).
- **Winding.** GX counts clockwise (in GL terms) as front-facing. Back-face culling with that convention shows the surfaces the game shows;
  the opposite convention shows the insides of everything.
- **Textures.** All 280 XTX textures in the 82 object archives that have one decode (ASTC, BC1-BC5, common uncompressed formats).
- **Animation.** Link's 365 bone clips parse; poses (`BattleReady`, `dashA`) render as the believable poses they are. Constant, byte-linear
  and 32-bit keyframe tracks are decoded as verified layouts. Texture patterns (the eye blink) are exact.
- **Materials.** TEV programs, texgens, channel controls and pixel state come from the models' own GX register streams. The output colour and alpha are
  the registers the last stage wrote.

## Inferred (works, but the meaning is read from values and shaders, not engine code)

- Depth of field from the cutscene file: only the far limit is used; the other floats are kept raw.
- 48-bit and float keyframe layouts: a few tracks per clip (3 to 6 of ~100) use them and fall back to the bind value where they do not fit
  (`ChrClip.UnreadableTracks` counts them).
- Light intensities: the game's light set is normalised to a small total because the character combiners add the lit colour onto the albedo. Raw `.blight` colours
  sum far above 1 and clip to white. A channel's ambient is the material's stored ambient times the rig's ambient (characters store black, stage materials white).

## Not done

- **The encrypted template.** `Shaders/Dummy.vert|frag` cannot be decrypted from the executable: its loader reads both files into buffers and nothing in
  `main` ever reads them again (the retail game uses the prebuilt cache instead). The uber shader was built from the decoded GX state, not from the template.
- **CLR0** (colour animation). PAT0 (expressions) and SRT0 (eye gaze) are read and applied.
- **Stage light sets** are stored in a scene file (SCN0 in System.arc) that is not parsed; the rig uses the shared `.blight` sets.
- **Lightmap lookups.** The `.blmap` curves are parsed (`LightmapFile`) but not applied: they are nearly flat in the shared data, and the engine texture
  matrices that index them are not recovered, so the LmCha* textures resolve to flat defaults.
- **Near depth-of-field** (`Dof_MaskNear`) and the other post passes (Sobel, TimeArea, particle composite).
- **BC6H and BC7** textures; indirect texturing, fog, emboss and colour texgens; point and spot lights.
- **Stage placement.** `LoadStage` draws one room's geometry (authored in world space), the cloud objects and a sky box. Props in `stage.brres` and
  objects placed from `.bzs`/`room.bzs` are not placed. 
- **LTEX lightmap records** (`default.blmap`) are not sampled ramps: reading them as 128-sample ramps turns the Add maps white and overexposes everything, so lightmaps stay flat.

## Added in the third pass

- **Any ID.** `LoadObject(id)` takes a model name or archive name from the `ContentIndex` (objects, stages, rooms, nested archives); no Link-specific code.
- **Eye gaze.** `SrtClip` reads SRT0 (per material, per texture slot scale, rotation, translation); the binder applies it to the texture matrix.
- **Sky and clouds.** `LoadStage("F000")` draws the room, `F020Cloud`/`CloudCover`/`F020UnderCloud`, and `Vrbox03`, with textures from the shared `SkyCmn` package.
- **Realtime viewer.** `src/SkywardViewer`: model and stage picker, orbit camera, clip/expression/gaze playback, post toggles.
- **Tests.** `tests/SkywardRenderingSharp.Tests` (set `SKYWARD_ROMFS`; they skip without it): index, skeleton, textures, every clip readable, SRT, and offscreen renders
  of Link (complete, no magenta), eye gaze, and Skyloft with sky.

## Added in the fourth pass

- **Normals bug fixed.** Normal arrays keep their stride at +0x1D (like positions); reading +0x1C gave stride 0, so every vertex of a mesh shared a handful of normals.
  This was the "lighter skin around the eyes" (eye polygons had true normals, the face had the first one repeated), the earlier "inverted normals", and the B100 room failure.
- **Stage palettes** (`StagePalette`, `Palette_Test.arc`): 20 slots per stage. Decoded: fill, third and ambient light colours (identical to `default.blight` set 0 in slot 0),
  key light, sky top and horizon colours, paint far and near distances (the same four-float shape as BDFP; slot 0 holds 3000, 3000, 880, 730), three fog layers
  (colour, near, far) and the hemisphere ambient from `.eaf`. The renderer applies them (`ApplyPalette`): lights, paint distances, fog, clear colour, and the sky material's
  colour register (C2). Not decoded: what each slot number means (time of day or weather), shadow and rim words, the second light layout's key light (slots whose word 6 is 0.3 use a white key and the hemisphere ambient).
- **Multi-archive objects.** Parts stored beside a model (Zelda_face, Zelda_hair, Zelda_handL/R) attach to its joints; part roots named `HandLRoot` map to `HandL`, and `Face`/`Hair` to `Head`.
- **Viewer.** Folder browser for the romfs (remembered), model search, stage list, palette/slot/fog/intensity controls, animation, post toggles.

## Fifth pass

- **Rigid polygons.** A polygon's node field is a matrix id of the rigid table (ids skip some bones), not a bone index: Fi's jewels (id 7) follow Spine2, not the foot.
  Omitted rigid ids are filled up to the first blended id instead of a fixed 19, so Zelda's skirt and stole keep their influences (they collapsed to the origin).
- **SSS.** The map the engine renders at runtime is replaced by the interpolated lit colour (`uIrradianceMap`); the combiner adds it twice (times the albedo and squared),
  so white overexposed everything and black darkened it. This is an approximation of the engine's blurred UV-space irradiance, not its output.
- **Lightmaps.** Every LTEX curve is 1.0 except three samples, and the Add records name their parent (`LmChaMulti`) with a 0.75 factor, so an Add map is derived from its
  multiply map rather than sampled: flat multiply white and add black is what the data gives. Nothing further is applied.
- **Palette slots.** Unused slots hold a default template and are hidden; the viewer describes each used slot by its sky colours, paint range and fill light, because the file does not
  name them (stages pick a slot by number). F000 uses slots 0-12, B100 0-3, F100 0-2, D100 0-8.
- **Backgrounds.** Any sky box (`Vrbox*`) or a solid colour can back any scene; `LoadSkyBox` and `SkyBoxNames` expose them.

## Sky colours

The sky materials ship red placeholder colours that the engine overwrites from the stage palette: the gradient material blends constant colour 3 with register 3, and the cloud layers shade
between constant colour 3 and white. `SkyBox` now sets constant 3 to the palette's horizon colour and register 3 of the `_of_sky` material to its top colour (`ColorOverrides` keys 4 to 7 are constants).
With no stage palette the viewer uses Skyloft's day slot (F000 slot 6).

## Engine data found but not usable yet

- `System.arc` `g3d/model.brres` holds the `Lm*` TEX0s (8x8 white placeholders the engine overwrites) and two SCN0s (`Default_zelda`, `lightSet`) listing the light sets: `Chara` = lights
  `Chara_00..02` + ambient `Cha`, `BGMulti*`, `UI`. The lights' colours are black or white placeholders, the palette supplies the real ones, so the rig already follows them.
- `System.arc` `tex/Gra*.bti` (64x4 gradients) are the sources the engine bakes the `Lm*` lightmaps from; `LTEX` records in the `.blmap` (default, F000, B200, D101, D200) remap them. Used raw as
  `LmChaSkin`/`LmChaMulti` they bleach every character, so the lightmaps stay flat; the remap that makes them right is not decoded.
- The bloom pass (`BloomMask_Custom`, c1 = luma weights .504/.098 with .257 inline, c5 = four channel thresholds and a scale) is known exactly, but the CPU code that fills c5 from `default.bblm`
  was not found (the loader at 0xe12d34 only stores the file); the strength stays a stand-in.
- Raw palette light colours (`FromPalette` with no intensity) bleach characters even with correct normals, so the normalised rig stays.
- `SSS` is replaced by the lit colour as described above.

## Bloom, from the engine's code (0xee018c, 0xee0e70, parser 0xee1a8c)

The `.bblm` block is parsed into a bloom object at +0x440 and read as: threshold mapped to video range, `(t * 219 + 16) / 255` (0.55 gives 0.535); the mask colour at 0x14 (white);
per-channel mask thresholds = that value times the mask colour, mask scale 2.0 (c5 of `BloomMask_Custom`); up to three (radius, intensity) layers at 0x20, 0x40, 0x60 with the layer count at 0x81
(2: the base layer, intensity 1.0, and one more, intensity 1.0, radius 0.5). The renderer now uses these: the mask thresholds, and two layers (the blurred mask and that blurred again) weighted by the
file's intensities. What is not known is the final additive scale and the radius unit, so `BloomStrength` (0.2) remains a stand-in; with the stand-in lighting, skin sits above the 0.5 threshold and
blooms harder than it would in the game.

## Bloom pass layout (engine draw method 0xee0314)

The bloom mask draws into a quarter-resolution target (a half when flag bit 2 of the block's short at 0x1c is set; the shipped file has it clear), is blurred separably through a transposed
target, and the second layer (flag bit 0) works from a further halved, eighth-resolution copy. The renderer now sizes its targets the same way. The composite weights are the file's layer intensities;
the engine also stores 0.5 and -0.5 beside the composite setup (offsets 0x258 and 0x388) whose roles are not traced, so `BloomStrength` (0.2) is still a stand-in. With the stand-in lighting the
scene is brighter than the game's (skin sits well above the 0.5 mask threshold), so the real strength 0.5 blows characters out; the strength should rise once character exposure is right.

## Lightmaps are the Gra* gradients (engine loader 0xdf1ef4)

At start-up the engine loads System.arc `tex/GraChaMulti`, `GraChaSkin`, `GraChaAdd0`, `GraChaAdd1`, `GraUIMulti`, `GraUIAdd0`, `GraUIAdd1`, `GraGetItem` into GPU slots 10 to 17 and binds them in place of the
`Lm*` placeholders the models carry. The renderer does the same (`SkywardRom.SystemTextures`, `GradientRamps`): `LmChaMulti`/`LmChaSkin`/`LmGetItem`/`LmUIMulti` take their gradient, `LmChaAdd` takes `GraChaAdd1`
(the stepped ramp; `GraChaAdd0` bleaches characters) and `LmUIAdd` `GraUIAdd0`. With these characters get the game's hard light/shadow cel shading, indexed by the view-space normal (light from the right),
and the stand-in light intensity barely matters any more. `LmBG*` have no gradient, so stages stay flat. Which Add variant the engine picks per situation, and the `.blmap` remap, are not traced.
Bloom strength is now 0.08 (0.2 and up blow the bright lit side out); the engine's composite scale is still unknown.

## Sixth-pass findings

- **SSS.** Only Fi's cloth/face material (`m_Main`, `m_Body`) uses it. The engine creates a 256x256 render target at 0x4ee870 (inside the model-setup callback at 0x4ecc28 that special-cases Fi, Zelda,
  Link's bird, Rival, the sky boxes and CloudCover) and renders into it at runtime; the render pass itself was not found. The material adds (0.09 + SSS) times the albedo and squares it, so the map is a
  brightness term sampled by UV. `uIrradianceMap` now substitutes `mix(0.45, 1, luma of the lit colour)` (a neutral brightness, not the warm light colour), which gives Fi her pale cyan face and cloak.
  This is an approximation of the engine's map, not its output.
- **Lightmap remap.** Each Gra gradient is multiplied by its `.blmap` curve (128 samples across the ramp); the curves are 1.0 apart from the first samples, so the gradients are effectively unchanged.
- **Palette slots.** `stage.bzs` section offsets are relative to each section's own table entry. `LYLT` holds (layer, value, kind) entries whose values (21, 68, 69, 87, ...) go far past the 20 slots, so it is
  not the slot choice; no data file picks a slot, the engine selects one from game state (time of day or weather). Slots are therefore listed by what their colours look like (`PaletteSlot.Mood`).
- **Depth of field.** `Dof_FogToAlpha` is fed a centre and a width and ramps depth between `centre - width / 2` and `centre + width / 2` (the scale register is 0.5). The `.bdof` is parsed by 0xee9ba0 into
  a 0x50-byte block (file 0x18 and 0x1c, 0x20, 0x30/0x34/0x3c, ...); which of its floats is the centre and the width was not resolved because the pass reads a different object's offsets (+0x40/+0x44).
  The cutscene-only depth of field keeps its far-limit-based ramp.

## Seventh-pass findings

- **Depth of field decoded.** The engine's parser (0xee9ba0) copies the `.bdof` floats at 0x18 and 0x1c into the effect object at +0x40 and +0x44; its depth conversion (0xee24a4) reads them as the focus
  distance and the width of the ramp around it (blur none below `focus - width / 2`, complete above `focus + width / 2`). The cutscene's 23000 and 10 make a nearly hard cut-off, and `UseCutsceneDepthOfField`
  now uses exactly that (it used `0.25 * far` to `far`).
- **Palette slots blend.** The classes at 0xee1c08 (bloom) and 0xee9c34 (depth of field) are parameter lerps with a time value: the engine mixes two slots by a blend factor when the time of day or weather
  changes, and picks the slots from game state. `PaletteSlot.Lerp` does the same and the viewer has a "Blend toward" slot and slider.
- **Bloom composite.** The layer colours are `min(intensity / (n / 8), 1)` grey bytes (n is a blur setting byte not traced, 8 would give 1), the block's second colour (0xfff0b4, warm) tints the result
  (`uBloomTint`), and the 0.5 and -0.5 constants beside it are texel offsets, not intensities. The overall weight is therefore at most 1 per layer; `BloomStrength` (0.08) is below that because the
  stand-in scene is brighter than the game's and blows out at full weight.

## Characters are lit by the ramps alone

The engine's character light set (SCN0 `Chara`: `Chara_00` to `Chara_02`, ambient `Cha`) ships black; character shading comes entirely from the Gra* gradient ramps. `LightRig.Characters()` is therefore a
rig with no lights and no ambient, and it is the renderer's default. With it Link, Zelda and Fi render vividly and cel-shaded (Zelda's pink dress and white shawl, Link's green tunic, Fi's pale cyan);
the earlier stand-in lights (normalised blight colours) only washed the ramps out. Stages keep palette lights (`ApplyPalette`) because their `BGMulti` set is white. With the clean scene the neutral `SSS`
stand-in (0.45 to 1.0 by lit luma) is simply 0.45.

The bloom parameters give each layer weight 1 (`n` = 8: the ctor stores 0x801 at +0x368, so +0x38 = 1 and +0x39 = 8). Applied in gamma space that blows characters out, but the mask and the additive
composite computed in linear light (the scene treated as sRGB) look right at weight 1, so `BloomStrength` is 1 and the post chain linearises (`pow 2.2`) around the bloom. That the scene target is sRGB is
inferred from this, not traced; Link's face still glows more than it should.

## Skyloft haze and fog

The pale band cutting through the island was `CloudCover`, a small plane that a stage places with an object (at the origin it slices the island); it and the `_s` twins are no longer drawn, only the world-sized
`F020Cloud` and `F020UnderCloud` layers. Stage clouds take the palette tint like the sky. Fog layer 0 of the palette (white, 2000 to 100000) turns the cloud sea white as in the game's look, so the viewer's
fog is on by default for stage palettes.

Other skyboxes (F100, F200, ...) ship the same red placeholders in their constants and registers; every pure red one now takes the palette's horizon colour (`SkyBox.Tint`). Stages F100, D100,
B100, F001r, F200 and S000 load and draw without errors, framed by the bench's default camera; room props and placed objects are still not placed.

## Blur gain: what was wrong with bloom and depth of field

The game's `GaussianBlur_*` constants sum to 2 per pass (0.45405 + 2 x 0.63243 + 2 x 0.14054), so a horizontal plus vertical blur has gain 4. Used as shipped, every blurred region came out four times
too bright: depth-of-field regions went white and bloom needed an arbitrary 0.08 to 0.4 strength. The renderer now halves the weights (unit gain), which makes depth of field preserve brightness (the far
side of Skyloft blurs correctly around the focus) and lets bloom run at the file's own weight, `BloomStrength` 1, in linear light. Depth of field gained the engine's near mask (`Dof_MaskNear`, a hard cut,
`DofNear`) and the viewer has focus, width and near controls. The engine's own compensation (probably in its blend factors) was not located.

## Tuned against a screenshot of Skyloft in the game

- **Stage light.** Raw palette lights and ambient clip every stage to white (stage materials multiply texture, baked vertex colour and light by two), so stages use the palette's light directions and
  colours normalised to a luma sum of 0.25 with 0.75 of the hemisphere (`.eaf`) ambient (`LightRig` stage defaults): sunlit surfaces land near 0.6 and shadows near 0.35 before the doubling,
  which gives the reference's bright, low-contrast grass and cliffs. The values are tuned, not decoded.
- **Watercolour.** The distance-only paint ramp did nothing at ordinary distances. The game's `Paint_Constant` passes output a constant alpha, i.e. the dilated image replaces the scene by a fixed amount
  everywhere, so `PaintConstant` (0.3) is added to the distance ramps and the paint image is built at half resolution (it ghosted at a quarter). The constant is a stand-in for the engine's value.
- **Bloom.** `BloomStrength` is 0.4: the file's weight of 1 over-brightens characters, whose lit sides already clip near white by the ramp maths (tex x 0.95 + 0.5 x vertex alpha x ramp).
- **Still different from the reference:** the sky is more periwinkle than the game's cyan blue (the palette slot's colours are used as they are, and which slot the game shows is not known), the horizon haze
  is white where the game's is warm cream, and no props or placed objects exist.

## Link's colours: the Add ramp

With the `GraChaAdd1` ramp at full strength every character was washed out: the lit side adds about half the vertex alpha on top of the texture, which turned brown boots lavender and the tunic pale. With
the Add ramp removed Link, Zelda and Fi come out with the game's colours (green tunic, brown leather, peach skin), a little dark. The ramp is now scaled by 0.35 (`AddRampGain`), which keeps a soft highlight and the
colours; the value is tuned by eye and the engine's reason for the smaller contribution was not found (the dual texgen matrices for slots 3 and 6 are loaded by the engine at run time, not by the material, and
may place the Add lookup mostly in the ramp's low end).

## Linear light and the real sky colours (matching the Skyloft screenshot)

- **Pipeline in linear light.** Colour textures decode from sRGB when sampled, lighting and the TEV run in linear light (the targets are RGBA16F), and the composite encodes to sRGB at the end; the bloom mask and
  blur work on the linear scene. The sky gradient's palette colours then land exactly on the game's blue: the deep blue of word 0 encodes to the screenshot's cyan-blue. (The XTX flags mark Link's textures unorm;
  treating them as raw with the final encode looked washed out, so all colour textures decode as sRGB, a TODO.)
- **Sky colours.** The two palette layouts differ: the first keeps its sky at words 128 and 129, the second (daytime) keeps it in its leading colours (word 0 the zenith, word 3 the horizon).
- **Add ramp.** In linear light the characters need `AddRampGain` 0.12 (0.35 before); at 1 they wash out.
- **Numbers against the screenshot** (different framing, so only the sky is comparable): sky (111, 203, 248) in the game, (119, 173, 222) here; mean luminance 183 against 166, mean saturation 0.26 against 0.27.
  Still different: the game's sky is brighter and more cyan, its clouds warmer cream, its contrast higher, and no props or placed objects exist here. This is not a 100% match.

## Brightness pass

Sky: the daytime palette's zenith is the deep blue (word 0) mixed 40 percent toward the horizon cyan (word 3), which gives (128, 211, 236) against the screenshot's (111, 203, 248) (it was (119, 173, 222)); the mix
is tuned to the screenshot. Fi: the `SSS` stand-in is `mix(0.62, 1, luma)`, which keeps her face detail and pale cyan glow in the linear pipeline (0.45 was dull grey-teal). `PaintConstant` is 0.15: at 0.3 the
dilation washed out characters. Tests pass.

## Fi, stage light and bloom (latest)

Fi's upper half washed out because her cloak material squares (0.09 + SSS) and adds it, so a bright constant SSS whitens everything it covers; the lower half has no SSS. The stand-in is now a rim
term, `0.34 + 0.6 * rim^2` with `rim = 1 - |N.z|`, so the whole figure keeps its detail with a soft edge glow. The `SSS` texture is created in three places (0x189d70, 0x4ee860, 0xdc0c88) through a helper that only
registers a 256x256 texture descriptor, and the pass that fills it was not found. Stage light is up (diffuse 0.4, ambient 0.7 of the hemisphere ambient), matching the screenshot's mean luminance (185 against 183;
saturation 0.31 against 0.26). `BloomStrength` is 2: the engine's file weight is 1 per layer, but the stand-in scene is darker than the game's and needs the extra to give the screenshot's glow on pale stone and clouds.

## Still open

- The render pass behind `SSS`, the scene colour encoding behind the bloom strength, and which slot pair and blend factor the engine picks for a given time of day.
