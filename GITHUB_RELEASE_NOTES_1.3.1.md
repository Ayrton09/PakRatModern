# PakRat Modern 1.3.1

Maintenance release. No interface changes beyond
the ones listed; the file format written is the same.

## Fixed: files under `custom/` were packed with a path the engine never loads

Adding `cstrike/custom/mymod/materials/x.vmt` packed it as
`custom/mymod/materials/x.vmt`. Source mounts each `custom/<name>/` folder
(and `download/`) as its own search path, so the map needs `materials/x.vmt`.
The result was a map with pink textures and no error. Both mount points are
now stripped when deducing the internal path.

## Fixed: the size limits on packed data could be bypassed

The limits protecting against oversized PAKs compared the size declared in the
ZIP directory, and the .NET Framework decompressor does not stop at that
declared size. A small BSP claiming tiny entries could inflate without bound.
The reader now counts the bytes that actually come out and refuses an entry
that exceeds its declaration.

## Fixed: the CLI did not produce the same PAK as the GUI on real maps

`pakrat_modern.py` copied each entry's ZIP comment and internal attributes from
the original map, so a map packed by an older tool came out different from the
GUI's output after an `add`. Nothing from the source archive is copied now. The
parity reference used by the tests also covers a non-ASCII file name.

## Fixed: `verify` in the CLI

- Reported "valid" for a map whose unsafe entries would be dropped on save.
  It now performs the same read → write → read round trip as the GUI and
  mentions entries that would be discarded or merged.
- Crashed with a generic error on compression methods it cannot read
  (Deflate64, PPMd, XZ). They are now refused up front, by name, with exit
  code 2, matching the GUI.

## Fixed: CLI backups and output handling

- A `.bak` is made whenever the output file already exists, not only with
  `--inplace`. `add map.bsp ... --out map.bsp` used to overwrite with no backup.
- `--inplace` and `--out` are mutually exclusive instead of `--out` being
  silently ignored. `--base` must be a directory.
- The temporary file used for atomic writes has a unique name and is created
  exclusively.
- `extract` no longer overwrites files that already exist in `--out`; pass
  `--overwrite` to replace them. Patterns now match case-insensitively on every
  platform.

## Changed: scan looks in every search path

Files on disk are now looked up in `Game Path` and in each `SearchPaths` entry
of `gameinfo.txt`, including `custom/*`, in engine order. Content that the game
loads from one of those folders was previously reported as "Missing on disk".
Scan also checks the particle manifest (`maps/<map>_particles.txt`),
`scripts/soundscapes_<map>.txt`, `maps/<map>_level_sounds.txt`, and
color-correction `.raw` files referenced by entities.

## Changed: duplicate entries with different contents are called out

When two entries differ only in case, one copy is kept (the last one). If their
contents differ, the warning on open now says so, because one version of that
file is being dropped.

## Changed: GUI

- Extracting over existing files asks first: overwrite, keep existing, or
  cancel.
- While a scan or a save is running the window is locked, so a second click
  cannot start another operation on a document that is mid-write.
- The "Path fixup" preference, which was saved but never used, is gone from
  the dialog. The backup option's label now says what it does: a `.bak` is made
  before overwriting any existing BSP, including with Save As.
- The About box reads the version from the assembly.

## Changed: repository

- A BSP whose lump table points inside the header is rejected instead of being
  rewritten into a corrupt file.
- GitHub Actions runs both test suites and rebuilds the parity reference on
  every push and pull request.
- `PakRatModern.sln` at the root; the CLI has its own test suite under
  `tests/`; `build_release.ps1` runs it before packaging.
- The PowerShell GUI that preceded 1.3.0 moved to the `legacy/powershell-gui`
  branch. It shared the settings file with the new application and could not
  load it once new fields appeared.

## Verifying this download

```text
345066af5feb68b32dd9a8f29b066e0c630d94a2141af8eea58dcdb31eec4b53  PakRatModern-release.zip
87ff628d881d698de890913da6fd57aaad905c4290e228feec858f1dca0eec05  PakRatModern.exe
25aedf54c72b3e5d305ed2223ad85082bee5dace4e2a099c6d2b8991cc9ed550  PakRatModern.Core.dll
```

```powershell
Get-FileHash .\PakRatModern-release.zip -Algorithm SHA256
```
