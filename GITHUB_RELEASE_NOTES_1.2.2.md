# PakRat Modern 1.2.2

Bug-fix release focused on data integrity when writing BSPs and packing files from the CLI.

## Fixed
- **CLI packed resources were unreadable by the Source engine.** The command-line tool wrote new PAK entries with DEFLATE compression (and passed through BZIP2/LZMA on rewrite), but the Source engine only reads uncompressed (STORE) entries from the `PAKFILE` lump. All entries are now written as STORE, and any pre-existing compressed entry is re-stored on rewrite, matching the GUI behavior.
- **BSP writes are now atomic.** The GUI wrote directly over the destination file, so a failure mid-write could corrupt the original BSP with no backup. Saves now write to a temporary file in the same folder and atomically replace the target, so the original is never left in a corrupt state.
- **"Save As" now creates a backup when overwriting.** A `.bak` is now produced whenever an existing file is overwritten, including via "Save As" (previously only in-place saves created one).

## Notes
No changes to the on-disk BSP format or the GUI workflow; this release only hardens the save/pack paths.
