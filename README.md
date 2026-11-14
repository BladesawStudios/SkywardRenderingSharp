# SkywardRenderingSharp

A C# OpenGL renderer for *The Legend of Zelda: Skyward Sword HD*. Where its sibling WildRenderingSharp drives the games' own decompiled
shaders, this one runs a **uber shader**: the game's shader template (a Wii GX emulator configured by `#define`s) rebuilt as a single
GLSL program configured by uniforms. It reads the game's own BRRES models, XTX textures and animations, and runs the painterly post chain
(paint dilation, depth-of-field blur, bloom) that gives the game its watercolour look.

## Using it

```csharp
using SkywardRenderingSharp;
using SkywardRenderingSharp.Characters;
using SkywardRenderingSharp.Rendering;
using SkywardRenderingSharp.Resources;

var rom = SkywardRom.Open(@"path\to\extracted\romfs");     // the folder holding Object, Stage and Shaders
using var renderer = new SkywardRenderer(gl, rom);          // a 4.3 core GL context, on the GL thread

var link = LinkRig.Load(renderer);                          // body, head, face and hands, attached by joint
var clips = rom.OpenAnimations("PLCommonAnime");            // 365 bone clips, 25 face patterns
var pose = clips.Get("BattleReady").Evaluate(link.Body.Model.Bones, frame: 6);
var face = clips.GetPattern("Fmaba01").Evaluate(frame: 0);  // eye textures for the blink

var camera = new Camera(new Vector3(0, 110, 330), new Vector3(0, 95, 0));
uint texture = renderer.Render(camera, link.Instances(Matrix4x4.Identity, pose, face), 1280, 720);
```

- `SkywardRom` - an extracted romfs: model archives, animation libraries, light sets, lightmaps and the post-processing parameters.
- `SkywardRenderer` - loads models, draws them with the uber shader and runs the post chain into an offscreen texture.
- `LinkRig` - Link from the game's separate models, attached to the skeleton by joint name; `ModelInstance` places any model, with an optional
  pose (skinning), material filter and per-material texture overrides.
- `ChrClip` / `PatClip` / `AnimationLibrary` - bone animation and texture-pattern animation (blinking).
- `RenderSettings` - sky, paint passes and distances, depth of field, bloom, culling. Defaults come from the game's own parameter files;
  `SkywardRenderer.UseCutsceneDepthOfField()` applies the game's depth-of-field file.
- `LightRig` - up to eight directional lights; `FromBlight` builds one of the game's 16 light sets.
- The world is right-handed and Y-up, in the game's centimetre-scale units.

## Building

The game's formats are read by libraries that are submodules in `lib`:

```bash
git submodule update --init --recursive
```

## Getting the game data

The library reads an **extracted romfs**; it does not touch keys or dumps. See [docs/extracting.md](docs/extracting.md).

## Test bench

```bash
dotnet run --project tests/SkywardRenderingSharp.TestBench -c Release -- render <romfs> out/link.png --clip BattleReady --frame 6 --face Fmaba01
dotnet run --project tests/SkywardRenderingSharp.TestBench -c Release -- render <romfs> out/row.png --count 7 --spacing 1500 --clip BattleReady,dashA,cutT --dof on
dotnet run --project tests/SkywardRenderingSharp.TestBench -c Release -- dump <romfs> Alink al        # a model's draws, bounds and skeleton check
dotnet run --project tests/SkywardRenderingSharp.TestBench -c Release -- texcheck <romfs>            # decode every XTX texture in every archive
```

The game's compiled shaders (the post programs in `Shaders/custom` and the material programs in `Shaders.arc.gz`) decompile to GLSL with
`tools/SkywardShaderDecompile` (needs `git submodule update --init` for ShaderLibrary's translator):

```bash
dotnet run --project tools/SkywardShaderDecompile -c Release -- <romfs>/Shaders/custom out/custom
```

`render` takes `--post on|off`, `--dof on|off`, `--cull on|off`, `--light-set`, `--yaw`, `--distance`, `--target-y`, `--size`. It uses a hidden GLFW window.

## Documentation

[docs/architecture.md](docs/architecture.md) (layers and the frame), [docs/ubershader.md](docs/ubershader.md) (the uber shader against the game's `#define`s),
[docs/formats.md](docs/formats.md) (BRRES, XTX, CHR0, PAT0 and the GX command streams, as decoded) and
[docs/status.md](docs/status.md) (what is verified, what is inferred, what is missing).

## Viewer and tests

```
dotnet run --project src/SkywardViewer -c Release -- <romfs> --id Alink      # or --stage F000
SKYWARD_ROMFS=<romfs> dotnet test tests/SkywardRenderingSharp.Tests
```

Left drag orbits, right drag pans, the wheel zooms. The test bench (`tests/SkywardRenderingSharp.TestBench`) renders PNGs: `render <romfs> out.png --id Alink --clip BattleReady --gaze F_Notice --gaze-frame 30`, `--stage F000`.
