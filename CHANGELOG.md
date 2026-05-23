# Changelog

## 1.1.0 - 2026-05-23

### Added
- Added dark title bar support for the Windows GUI.
- Added base game VPK detection using `gameinfo.txt` search paths.
- Added scan support for overview VTF files:
  - `materials/overviews/<mapname>.vtf`
  - `materials/overviews/<mapname>_radar.vtf`
- Added scan summary counts for missing, addable, not found, and already packed files.
- Added safer release build packaging with `build_release.ps1`.

### Changed
- Optimized scan performance with VMT dependency caching.
- Optimized base VPK lookup with a compiled helper for targeted VPK directory parsing.
- Optimized disk existence checks during scan.
- Improved dark theme consistency across app-owned dialogs and controls.
- Updated app version to `1.1.0`.
- Updated EXE metadata to `1.1.0.0`.
- Removed the extra "Dark UI" line from About.

### Fixed
- Fixed model companion detection so generic `.vtx` is not required.
- Fixed scan paths that could show `..` instead of normalized paths.
- Fixed VMT dependency parsing for valid unquoted keys like `$basetexture`.
- Fixed CLI wrapper path handling and Python 3 / WSL fallback.
- Fixed app base path detection when running as `.ps1`.

### Security
- Hardened internal PAK path normalization.
- Blocked unsafe absolute paths, `../`, reserved Windows names, and invalid path characters.
- Added PAK entry count and size limits.
- Added safer extraction path validation.
- Blocked unsafe PAK resize cases that could corrupt BSP lump offsets.
