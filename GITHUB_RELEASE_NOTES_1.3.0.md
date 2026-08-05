# PakRat Modern 1.3.0

The application is now a compiled .NET program instead of a PowerShell script
packaged with `ps2exe`, and several data-corruption bugs in the PAK writer are
fixed.

## Changed: no more ps2exe

Previous releases were a PowerShell script embedded in an executable. That
binary's only job was to host PowerShell and run the embedded script, which is
structurally identical to how real droppers work, so antivirus heuristics
flagged it on sight.

1.3.0 is a normal compiled .NET assembly: no embedded script, no PowerShell
host, no networking or registry APIs. It targets .NET Framework 4.7.2, the same
requirement the previous releases already had, so nothing new to install.

The interface is unchanged.

## Fixed: maps with one bad entry could not be opened at all

Path validation was an all-or-nothing gate while reading, so a single entry with
an unsafe path made the whole map refuse to load. Some maps in the wild are
packed with absolute paths such as `C:/Program Files/.../x.vmt` or
`/sound/y.mp3`, and a handful of those made hundreds of perfectly good entries
unreachable.

Unsafe entries are now skipped individually and reported. They are still never
loaded or extracted, so nothing about the safety changes; the rest of the map
opens normally.

## Fixed: BSP corruption when the PAK was not the last lump

Resizing the `PAKFILE` lump shifted every following lump by an arbitrary number
of bytes, breaking the 4-byte alignment the Source engine assumes. The shift is
now always a multiple of 4, in both the GUI and the CLI.

## Fixed: the GUI and the CLI produced different PAKs

Three separate causes:

- **Entry order.** The GUI sorted with the current culture, which orders
  `_pre.vmt` before `A.vmt`; the CLI did not sort at all and wrote entries in
  insertion order. Both now use an explicit ordinal sort. As a side effect, the
  GUI's output no longer depends on the system language.
- **Timestamps.** The GUI stamped the current time, so saving twice with no
  changes produced different files. The CLI instead preserved whatever date each
  entry already had, and maps commonly carry a DOS date of 0, which is not a
  valid date at all. Both now write a fixed 1980-01-01.
- **ZIP metadata.** The CLI copied `create_system` and `external_attr` from the
  original PAK, so the result depended on which tool and OS had created the map
  first. Both now write a fixed canonical form.

The two tools now emit byte-identical PAK data for the same set of files.

## Fixed: the CLI could destroy a BSP mid-write

`--inplace` wrote directly over the target. A failure partway through (full
disk, power loss) left a truncated map. The CLI now writes to a temporary file
and replaces the original, which is what the GUI already did.

## Fixed: game paths with accented characters

Settings were written as ASCII, so a path like `D:\Programación\` was silently
stored as `D:\Programaci?n\` and stopped resolving on the next launch. Settings
are now UTF-8.

Settings and the log also moved from the application folder to
`%LOCALAPPDATA%\PakRatModern\`, so the app works when installed somewhere
read-only such as `Program Files`. Existing settings are migrated automatically
on first run.

## Fixed: duplicate entries differing only in case

The CLI treated `materials/Custom/a.vmt` and `materials/custom/a.vmt` as two
entries. The engine resolves paths case-insensitively, so which one won was
unpredictable. Internal paths are now case-insensitive in both tools.

## Known limitation: LZMA-compressed PAKs

Some maps store their PAK entries with LZMA. The GUI cannot read those, because
it relies on the ZIP support built into .NET, which handles only Stored and
Deflate. This is not new in 1.3.0 — the previous PowerShell GUI used the same
component and had the same limitation.

What is new is that the map is refused with an explanation instead of a generic
error. Opening it partially would silently delete those files on save, so
refusing is deliberate.

The CLI does read LZMA. Running an `add` with it rewrites every entry
uncompressed, after which the GUI opens the map normally.

## Added

- Entry viewer: preview a packed file as text or as a hex dump
- Scan results can be exported to a text file
- Duplicate entries are reported when a map is opened, and merged, because the
  engine cannot tell them apart
- Text fields now have a visible outline. Without one an empty field blends into
  the panel behind it and there is no way to tell where to click
- Folder pickers use the modern Explorer dialog, so a path can be typed or pasted

## Verifying this download

```text
38a1b963c259706fa303f1e3e325a4dd14b98b91352779d1455c42cad3cdf028  PakRatModern-release.zip
4696b8ff920e71a6a2ea3678c63455ec655d225937ab77406b6c8b1888c4992f  PakRatModern.exe
d261961945164c76388f81cd011d3a91d73f84951a0befa194c14f0351cd1c75  PakRatModern.Core.dll
```

```powershell
Get-FileHash .\PakRatModern-release.zip -Algorithm SHA256
```
