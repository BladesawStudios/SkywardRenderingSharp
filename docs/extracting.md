# Extracting the game

The library needs the **romfs** folder of a decrypted dump. The steps used while building it:

1. Decompress the `.nsz` to `.nsp` with `nsz` (`pip install nsz`; it finds `prod.keys` in `~/.switch`).
2. Extract the NSP's NCAs with `hactool -t pfs0 --pfs0dir=nca game.nsp`.
3. Extract the Program NCA: `hactool -k prod.keys -t nca --romfsdir=romfs --exefsdir=exefs <program.nca>`.

Keep keys out of this repository. The renderer only needs `romfs`; `exefs` is useful for research.

The post-processing shaders in `romfs/Shaders/custom/` are NVN shader caches; `SkUberShader/tools/CacheTranslate` translates them to GLSL
with the Ryujinx translator. `Glsl/post/*` are readable ports of those translations.
