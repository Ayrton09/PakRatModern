<div align="center">

# PakRat Modern

**Pack custom content into Source engine `.bsp` maps — scan, add, verify, done.**

[![CI](https://github.com/Ayrton09/PakRatModern/actions/workflows/ci.yml/badge.svg)](https://github.com/Ayrton09/PakRatModern/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/Ayrton09/PakRatModern?label=release)](https://github.com/Ayrton09/PakRatModern/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078d4)

<img src="docs/pakrat-modern-screenshot-clean.png" alt="PakRat Modern main window" width="820">

</div>

PakRat Modern edits the `PAKFILE` lump inside Source `.bsp` files: the embedded
ZIP where a map carries its custom materials, models, sounds and overviews. It
scans the map for everything it references, tells you what is missing, and packs
it — without rebuilding the BSP and without touching any other lump.

It ships as a native Windows GUI and a Python CLI that produce byte-identical
output.

---

## Quick start

1. Download `PakRatModern-release.zip` from the [latest release](https://github.com/Ayrton09/PakRatModern/releases/latest) and extract it anywhere.
2. Run `PakRatModern.exe`. Nothing to install: it targets .NET Framework 4.7.2, which every supported Windows already has.
3. Open your `.bsp` (or drag it onto the window).
4. Set **Game Path** to the folder that contains `gameinfo.txt` — `cstrike`, `hl2`, `tf`…
5. Click **Scan**, review the list, then **Add checked** (or just **Auto**).
6. **Save**. A `.bak` of the original is kept next to it.

<details>
<summary><b>Verifying the download</b></summary>

Every release lists the SHA256 of the zip and of the binaries inside it.

```powershell
Get-FileHash .\PakRatModern-release.zip -Algorithm SHA256
```

GitHub also records a digest for each asset, readable without downloading:

```powershell
gh api repos/Ayrton09/PakRatModern/releases/latest --jq ".assets[].digest"
```

From 1.3.2 on, releases are built by GitHub Actions from the tagged commit and
carry a signed build provenance attestation:

```powershell
gh attestation verify PakRatModern-release.zip -R Ayrton09/PakRatModern
```
</details>

---

## What it does

| | |
|---|---|
| **Scan** | Collects every file the map references from the entity lump, the texdata string table and the static prop lump, then follows dependencies: a `.mdl` pulls its `.vvd` / `.vtx` / `.phy` and materials, a `.vmt` pulls its textures and includes, the particle manifest pulls its `.pcf` files and their materials, soundscapes and level sounds pull their `.wav` / `.mp3`. Sprites, projected textures and VScripts are recognised too. |
| **Classify** | Each reference is marked *Already in PAK*, *Base game VPK* (shipped with the game — not packed), *Can add* (found on disk) or *Missing on disk*. Engine render targets (`_rt_*`) and optional model files (`.phy`, `.dx80.vtx`, `.sw.vtx`) are not reported as missing. |
| **Pack** | Add checked results, single files, whole folders, or drag and drop. Internal paths are deduced from the disk location. |
| **Edit** | Rename internal paths, delete entries, extract to disk, preview any entry as text or hex. |
| **Verify** | Round-trips the PAK through the writer and reader before you save. |
| **Save safely** | Atomic write, optional `.bak`, 4-byte lump alignment preserved, game lump guarded, and a warning if the BSP was recompiled since you opened it. |

Files on disk are looked up in **Game Path** and in every `SearchPaths` entry of
its `gameinfo.txt` — including `custom/*` — in the same order the engine uses,
so a file present in both `custom/` and the game folder is packed from the copy
the game actually loads. Content already shipped in the game's `_dir.vpk` files
is recognised and left out of the map. A model texture is looked up in each of
its `$cdmaterials` folders in order, like the engine does, and only reported
once if it is missing from all of them.

Maps from Left 4 Dead 2, whose lump table stores its fields in a different
order, are read and saved in their own format.

<details>
<summary><b>Optional extras the scan also checks</b></summary>

These are not referenced inside the BSP but the game looks for them by name.
They are only reported when present on disk, and what they list is followed:
the `.pcf` files named by the particle manifest and the sounds named by the
soundscape and level sounds files.

```
maps/<map>.nav                      maps/<map>.txt
maps/<map>_particles.txt            maps/<map>_level_sounds.txt
scripts/soundscapes_<map>.txt
resource/overviews/<map>.txt        resource/overviews/<map>.dds
resource/overviews/<map>_radar.dds
materials/overviews/<map>.vmt       materials/overviews/<map>.vtf
materials/overviews/<map>_radar.vmt materials/overviews/<map>_radar.vtf
```
</details>

<details>
<summary><b>Where added files end up</b></summary>

The internal path is the file's location relative to **Game Path**. Source
mounts every `custom/<name>/` folder and `download/` as search paths of their
own, so these are stripped:

| On disk | Packed as |
|---|---|
| `cstrike/materials/custom/wall.vmt` | `materials/custom/wall.vmt` |
| `cstrike/custom/mymod/materials/custom/wall.vmt` | `materials/custom/wall.vmt` |
| `cstrike/download/models/props/crate.mdl` | `models/props/crate.mdl` |
| `D:\work\pack\materials\custom\wall.vmt` (outside Game Path) | `materials/custom/wall.vmt` |
| `C:\Users\me\Documents\maps\mymap\materials\wall.vmt` (outside Game Path) | `materials/wall.vmt` |

Outside the Game Path, the innermost content folder (`materials`, `models`,
`sound`, `maps`…) wins, so folders of yours that happen to share those names
(`Documents\maps`, `D:\Media`) do not end up inside the map;
`materials/models/…` and `materials/maps/…` stay whole. When you add a whole
folder, paths are taken relative to it. Files outside the Game Path are listed
with their deduced paths for you to confirm before they are added.

A file with no recognisable content folder in its path is reported instead of
being packed under a guessed name.
</details>

Settings and the startup log live in `%LOCALAPPDATA%\PakRatModern\`. The log is
capped at 512 KB; the previous one is kept as `.old`.

---

## Command line

[`pakrat_modern.py`](pakrat_modern.py) needs Python 3.8+. The wrapper
[`pakrat_modern.ps1`](pakrat_modern.ps1) finds a local Python or falls back to
WSL, so on Windows you can call it as:

```powershell
.\pakrat_modern.ps1 <command> [options]
```

| Command | What it does |
|---|---|
| `list <map.bsp>` | Print every embedded file with its size |
| `verify <map.bsp>` | Round-trip the PAK. Exit code 0 if valid, 2 if the PAK is damaged or uses an unsupported compression method, 1 if the BSP itself cannot be read |
| `extract <map.bsp> [--out DIR] [--overwrite] [PATTERN ...]` | Extract all entries, or those matching case-insensitive globs. Existing files are kept unless `--overwrite` |
| `add <map.bsp> PATH... --base DIR [--inplace \| --out FILE] [--no-backup]` | Add files or folders; internal paths are relative to `--base` |
| `remove <map.bsp> NAME... [--inplace \| --out FILE] [--no-backup]` | Remove entries by internal path |
| `repack <map.bsp> [--inplace \| --out FILE] [--no-backup]` | Rewrite the PAK uncompressed without adding or removing anything |
| `--version` | Print the version |

`add`, `remove` and `repack` write `<map>_packed.bsp`, `<map>_stripped.bsp` and
`<map>_repacked.bsp` by default. Whenever the output already exists, a `.bak`
copy is made first. On Linux the rewritten file keeps its permissions.

```powershell
.\pakrat_modern.ps1 add C:\maps\de_mine.bsp C:\content\materials --base C:\content --inplace
```

The GUI and the CLI emit the same bytes for the same set of files; a test on
each side checks it against a shared reference hash.

---

## Safety

- Only the `PAKFILE` lump and the offsets its resize shifts are rewritten; every other byte of the BSP is preserved.
- Following lumps keep their 4-byte alignment. A resize is refused when `LUMP_GAME_LUMP` sits after the PAK, because its internal offsets are absolute.
- Writes go to a temporary file in the same folder and are swapped in atomically; a failure mid-save leaves the original intact.
- The GUI keeps the whole BSP in memory, so it checks the file before overwriting it: if the map was recompiled after you opened it, it asks before replacing the new build with the old one, and offers to reload it when you come back to the window.
- Every entry's CRC-32 is checked when reading. A damaged PAK is refused instead of being rewritten with a fresh checksum that would hide the damage.
- Internal paths are validated on read and write: no `..`, no absolute paths, no NTFS-invalid characters, no reserved device names. Extraction is confined to the chosen folder.
- Names stored without the ZIP UTF-8 flag (as vbsp and bspzip write them) keep their exact bytes when saved; the engine looks those bytes up.
- Size and entry-count limits count the bytes that actually decompress, not what the ZIP directory claims.
- Paths are case-insensitive, like the engine. Duplicate entries are merged and reported — with a note when their contents differ.

<details>
<summary><b>Compressed maps</b></summary>

Maps compressed with `bspzip -repack -compress` (common in TF2) store both their
lumps and their PAK entries with LZMA. Both tools read them: the GUI scans the
compressed lumps and opens the LZMA entries. Saving writes the PAK uncompressed,
which is what the engine loads from it, and leaves the other lumps as they
were; run `bspzip -repack -compress` again afterwards if you want the smaller
file back.

A PAK that uses BZip2 is refused by the GUI with an explanation, rather than
opened partially (which would silently drop those entries on save). The CLI
reads it: `repack --inplace` rewrites every entry uncompressed without adding or
removing anything, after which the GUI opens the map normally. Methods neither
tool reads (Deflate64, PPMd, XZ) are refused by both, by name.
</details>

<details>
<summary><b>Antivirus false positives</b></summary>

Releases up to 1.2.2 were a PowerShell script packaged with `ps2exe`: a binary
whose only job is to host PowerShell and run an embedded script, which is the
same shape as real droppers, so heuristic engines flagged it on sight.

Since 1.3.0 the application is a normal compiled .NET assembly with no embedded
script, no PowerShell host, and no networking or registry APIs. If a scanner
still flags it, please open an issue with the detection name.
</details>

---

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) and Python 3.

```powershell
powershell -ExecutionPolicy Bypass -File .\build_release.ps1
```

That runs both test suites, publishes the application into
`release\PakRatModern\`, zips it, and prints the SHA256 of the outputs. The
same tests run in GitHub Actions on every push (the CLI on Windows and Linux,
with Python 3.8 and the latest release). Published releases are built by the
`Release` workflow, which attaches the zip, adds its hashes to the notes and
signs a provenance attestation.

```powershell
dotnet run --project src\PakRatModern.Tests\PakRatModern.Tests.csproj -c Release -f net472
python -m unittest discover -s tests
```

```
src/PakRatModern.Core/    BSP, PAK/ZIP, LZMA, VPK, gameinfo, reference scanner
src/PakRatModern.App/     WinForms interface
src/PakRatModern.Tests/   core tests (no external dependencies)
src/testdata/             parity reference hash and LZMA fixtures
pakrat_modern.py          CLI
tests/                    CLI tests
tools/                    generates the parity reference and the LZMA fixtures
```

The PowerShell GUI that preceded 1.3.0 is kept on the
[`legacy/powershell-gui`](https://github.com/Ayrton09/PakRatModern/tree/legacy/powershell-gui)
branch and is no longer maintained.

## License

[MIT](LICENSE)
