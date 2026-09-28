# PakRat Modern 1.3.2

Maintenance release. The file format written is the same; maps compressed with
`bspzip` and Left 4 Dead 2 maps now open.

## Fixed: saving could revert a map that was recompiled while it was open

The GUI keeps the whole BSP in memory. If Hammer recompiled the map while it
was open, **Save BSP** wrote the old build back over the new one, geometry
included; a second save also replaced the `.bak`. PakRat Modern now checks the
file before overwriting it and asks whether to reload it, overwrite it or
cancel. Coming back to the window after a recompile offers to reload it.

## Fixed: files added from outside the Game Path could get the wrong path

The path inside the map was taken from the first folder in the full path that
shared a name with a content folder, so `C:\Users\me\Documents\maps\mymap\materials\wall.vmt`
was packed as `maps/mymap/materials/wall.vmt`. The innermost content folder now
wins (keeping `materials/models/…` and `materials/maps/…` whole), adding a
whole folder anchors paths at that folder, and files outside the Game Path are
listed with their deduced paths for confirmation before they are added.

## Fixed: scan

- Maps compressed with `bspzip -repack -compress` (common in TF2) have their
  entity, texture and static prop lumps compressed with LZMA. Scan read them
  raw and found nothing, without a warning. They are now decompressed.
- Scan now follows the particle manifest to its `.pcf` files and their
  materials, soundscapes and level sounds to their `.wav` / `.mp3`, sprites
  (`env_sprite`, `env_glow`, `.spr`), `env_projectedtexture` textures and
  VScripts.
- A model texture is looked up in each `$cdmaterials` folder in order, like the
  engine does, and composed with the folder even when its name has
  subfolders. Before, every combination was reported as missing and textures
  with subfolders were looked up in the wrong place.
- No more false "Missing on disk": engine render targets (`_rt_*`), optional
  model files (`.phy`, `.dx80.vtx`, `.sw.vtx`) and `.spr.vmt` paths.
- Files on disk are searched in the exact order of `gameinfo.txt`, so a file in
  both `custom/` and the game folder is packed from the copy the game loads.
- A reference with a character such as `|` no longer aborts the whole scan.

## Fixed: PAK reading

- The GUI now reads the ZIP itself instead of relying on .NET's `ZipArchive`:
  - every entry's CRC-32 is checked, so a damaged PAK is refused instead of
    being saved with a fresh checksum that hides the damage;
  - LZMA entries (as written by `bspzip -repack -compress`) open directly;
  - names stored without the ZIP UTF-8 flag, as vbsp and bspzip write them,
    keep their exact bytes when saved. Before, a name like `café.vmt` was
    rewritten in UTF-8 and the engine no longer found the file; the GUI and the
    CLI also rewrote it differently.
- Left 4 Dead 2 maps, whose lump table stores the version first, were rejected
  with "Lump 0 out of range". They are read and saved in their own format.

## Fixed: command line

- On Linux, rewritten maps were left with permissions `600` (a regression in
  1.3.1), which breaks FastDL and servers running under another account. The
  original permissions are kept.
- `add` and `remove` without `--inplace` or `--out` failed on Python 3.8.
- New `repack` command: rewrites the PAK uncompressed without adding or
  removing anything. The GUI now suggests it for BZip2 maps, instead of adding
  an arbitrary file to the map.
- `--version`. Messages are now in English, like the GUI and this README.
- `pakrat_modern.ps1` requires Python 3.8 or later, and when it falls back to
  WSL it resolves relative paths from the current folder.

## Fixed: interface

- In Preferences, a checked box looked empty: the dark theme drew the tick in
  near-white on a white box. Both options are drawn by the application now.
- The window is DPI aware: at 125 % or 150 % scaling it is no longer stretched
  and blurry.
- **Set as current** in Manage Game Paths is no longer applied when the dialog
  is cancelled.
- The scan results dialog no longer hides a long Game Path.
- Settings are saved atomically, and the startup log is capped at 512 KB.

## Changed: release

- The zip no longer contains a shortcut. It stored absolute paths from the
  build machine and its name.
- Releases are built by GitHub Actions from the tagged commit, with a signed
  build provenance attestation (`gh attestation verify`), and the binaries no
  longer embed the local path they were built from.
- CI runs the CLI tests on Windows and Linux with Python 3.8 and the latest
  release, pins every action by commit, and no longer lets a failure of the
  parity reference generator pass unnoticed.
