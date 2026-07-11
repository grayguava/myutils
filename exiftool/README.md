# ExifTool Metadata Clean — `clean.py`

- **Tool:** `exiftool/src/clean.py`
- **Language:** Python 3 (stdlib only)
- **External dependency:** [ExifTool by Phil Harvey](https://exiftool.org/) must be on `PATH`
- **Role:** Strip all metadata from image, video, and PDF files in-place, with a safe copy-then-swap workflow and automatic `.bak` rollback on failure.

---

## Usage

```
python src/clean.py
```

No command-line arguments. The script is interactive:

1. Checks `exiftool` availability on `PATH` and prints its version.
2. Prompts "Open file selector? (y/n)" — `y` opens the native Windows multi-select file dialog.
3. Copies selected files to a temporary workspace alongside the script.
4. Runs `exiftool -all= -overwrite_original -P` on each copy to strip metadata.
5. Verifies the wiped copies (exists, non-empty, readable).
6. Renames each original to `.bak`, moves the wiped copy into its place.
7. On success, deletes all `.bak` files and the temp workspace.

If any step fails — copy mismatch, exiftool error, missing temp file — the script restores all `.bak` files, cleans up the temp directory, and exits. The filesystem is left in its original state.

---

## Supported file types

The file dialog filter lists: `jpg`, `jpeg`, `png`, `webp`, `heic`, `tif`, `tiff`, `mp4`, `mov`, `pdf`, and `*.*`.

The tool passes every selected file to ExifTool regardless of extension. ExifTool's response determines what happens:
- **Known format with metadata** — tags are deleted, changes reported.
- **Known format without metadata** — "0 image files updated" reported, no error.
- **Unknown/unrecognised format** — ExifTool reports a warning or error, the tool detects the non-zero exit code and aborts.

---

## Compatibility

| Aspect | Status |
|---|---|
| Python version | 3.8+ |
| OS | Windows only (PowerShell file dialog) |
| Dependencies | None beyond stdlib |
| External tool | [ExifTool](https://exiftool.org/) 12.00+ (any version with `-all=` support) |
| Image formats | JPEG, PNG, WebP, HEIC, TIFF |
| Video formats | MP4, MOV |
| Document formats | PDF |
| Other formats | Any format ExifTool supports (pass-through) |
| Unicode paths | Supported (PowerShell emits UTF-8) |
| Very long paths | Mitigated by flat rename in temp dir |

### ExifTool requirements

- The `exiftool` command must be on `PATH` (or available as `exiftool` from a shell).
- Version 10.00 or later recommended for `-all=` support across all tag groups.
- No Perl runtime needed — the standalone Windows executable (`exiftool.exe`) works.
- Tested with ExifTool 12.x. Older versions may work but are untested.

---

## Logging

Every run produces a timestamped log file in `logs/clean/` with the naming pattern `clean_YYYYMMDD_HHMMSS.log`.

### Log content

```
ExifTool Metadata Clean Log
Timestamp : 2026-07-10 18:30:00
Outcome   : SUCCESS
----------------------------------------

ExifTool version: 12.76
Files selected (3):
  photo.jpg
  video.mp4
  document.pdf

[1/4] Copying files to temp workspace
  All copies verified OK

[2/4] Wiping metadata
  photo.jpg
    - EXIF
    - XMP
    - IPTC
  video.mp4
    - QuickTime
  document.pdf
    (no metadata found)

[3/4] Verifying wiped files
  All wiped files verified OK

[4/4] Replacing originals
  Swapped OK: photo.jpg
  Swapped OK: video.mp4
  Swapped OK: document.pdf

All done. 3 file(s) cleaned in place.
```

### Log retention

Only the 10 most recent logs are kept. Older logs are pruned automatically on each run. Pruning is based on filename sort (reverse chronological), so the 10 most recent `clean_*.log` files survive.

### Outcome values

| Outcome | Meaning |
|---|---|
| `SUCCESS` | All files processed, swapped, and verified |
| `ABORT: <reason>` | Run failed before completion, rollback performed |

---

## Workflow stages

### Stage 1 — Copy to temp

A uniquely-named temp directory is created alongside the script: `_exiftool_tmp_<uuid>/`. Each selected file is copied into it with a flat rename — the file's 0-based index plus its original extension (e.g. `0.jpg`, `1.png`, `2.pdf`).

Why flat rename:
- Eliminates path-length issues (no nested subdirectories).
- Avoids name collisions (two `photo.jpg` from different folders).
- Keeps ExifTool command lines simple and consistent.

After copy, the original and copy sizes are compared byte-for-byte. A mismatch aborts immediately — the copy is incomplete or corrupted, and the original is untouched.

### Stage 2 — Strip metadata

For each temp file, runs:

```
exiftool -all= -overwrite_original -P <temp_file>
```

- `-all=` — delete all writable metadata tag groups (EXIF, IPTC, XMP, GPS, MakerNotes, QuickTime, etc.).
- `-overwrite_original` — ExifTool updates the file in-place internally (not a separate copy).
- `-P` — preserve the original file's modification date.

Deleted tags are extracted from ExifTool's output by searching for lines starting with "Deleting ". Each deleted tag is printed and logged. If no tags were deleted, "no metadata found" is logged for that file.

If ExifTool returns a non-zero exit code, the script aborts and triggers rollback. ExifTool's stdout/stderr is included in the log for debugging.

### Stage 3 — Verify

Three checks per wiped temp file:
1. **Exists** — `os.path.exists()` after ExifTool processing.
2. **Non-empty** — `os.path.getsize() > 0`.
3. **Readable** — `open(temp_file, 'rb').read(1)` succeeds.

Any failure triggers a full rollback of all previously swapped originals and exits.

These checks catch ExifTool bugs, disk errors, and antivirus interference. A file that passes all three is extremely likely to be intact.

### Stage 4 — Swap

For each original file, in order:
1. `os.rename(original, original + ".bak")` — preserve the original.
2. `shutil.move(temp_file, original)` — put the wiped copy in place.
3. Verify the replacement exists and is non-empty.

The `bak_files` list grows as each rename succeeds. If any swap fails:
- All `.bak` files are restored: `os.rename(bak, original)`.
- The temp directory is removed.
- The script exits with ABORT.

This guarantees that a partial failure (e.g. disk full on the 3rd file) leaves the filesystem exactly as it was before the run started — no half-swapped state.

After all swaps succeed:
- All `.bak` files are deleted (`os.remove`).
- The temp directory is removed (`shutil.rmtree`).

---

## Error handling

### Rollback scenarios

| Failure point | What happens |
|---|---|
| Copy size mismatch | Abort before any `.bak` created. Original untouched. |
| ExifTool failure on file N | Abort. Files 1..N-1 already swapped — their `.bak` files restored. |
| Temp file missing after wipe | Abort. All `.bak` files restored. |
| Final swap verify fails | Abort. All `.bak` files restored. |

The rollback function (`restore_baks()`) iterates `bak_files` in order and renames each back to its original. It does not stop on failure — it attempts all restores even if some fail.

### Edge cases

| Scenario | Behaviour |
|---|---|
| User cancels at "Open file selector?" | Clean exit, no temp files created |
| No files selected in dialog | Clean exit |
| File deleted externally during run | Swap step will fail → rollback |
| ExifTool not on PATH | Abort before any file operations |
| Temp dir cleanup fails on exit | Ignored — `shutil.rmtree` with `ignore_errors=True` |
| `.bak` deletion fails on success | Ignored — `os.remove` in try block, no rollback needed |

---

## Design decisions

### Why copy-then-wipe and not wipe-in-place

ExifTool's `-overwrite_original` is safe (it writes to a temp file internally and swaps atomically), but running it directly on originals means there is no second copy to fall back to if:
- Power fails mid-wipe.
- ExifTool crashes due to a bug or memory error.
- The filesystem corrupts during write.
- Antivirus quarantines the temp file mid-process.

By copying to a temp workspace first, originals are never touched until the wiped copy has been verified. The `.bak` rollback mechanism extends this safety to the final swap step. The cost is double the write I/O per file (one copy + one move), which is negligible for typical batch sizes.

### Why no command-line arguments

The tool is designed for occasional interactive use — "I have a batch of files I want to clean" — not for automation. A native GUI file picker is more ergonomic than typing paths. The same dialog pattern (PowerShell + `System.Windows.Forms.OpenFileDialog`) is shared with `dirdiff.py` for consistency.

### Why `-all=` deletes everything

`-all=` targets all writable tag groups — EXIF, IPTC, XMP, GPS, MakerNotes, QuickTime, PDF metadata, etc. For a general-purpose privacy tool, the default should remove everything possible. There is no whitelist mode; files that need some metadata preserved should use ExifTool directly with selective tags.

### Why logs go next to the script

The log directory is `exiftool/logs/clean/`, relative to the script. This is deliberate:
- No configuration needed for logging — it's automatic.
- Logs stay with the tool, not scattered across the system.
- The `logs/` directory is gitignored (or at least not committed).

### Why 10-log retention

10 logs at typical usage covers about a week of runs. Older logs are rarely needed — each log is self-contained with full file lists and outcomes, so individual runs can be audited independently.

---

## Known limitations

- **ExifTool must be on PATH** — the script runs `exiftool -ver` at startup. If ExifTool is installed but not on `PATH`, the script aborts. No fallback to a configured path or prompt.
- **No batch re-run protection** — running twice on the same file is harmless (second run has no metadata to delete) but copies and wipes unnecessarily. No deduplication or skip-if-already-clean logic.
- **No PDF-specific handling** — ExifTool strips PDF metadata differently from image metadata. Some PDF tags (e.g. document structure) are not deletable with `-all=`. The tool treats all files identically.
- **Temp directory on same drive** — the temp workspace is created alongside the script. If selected files are on a different drive, `shutil.move` degenerates to copy + delete rather than atomic rename. Functionally identical, slightly slower.
- **Windows-only file dialog** — the PowerShell dialog is Windows-specific. There is no CLI fallback for non-Windows systems.
- **No progress bar per file** — ExifTool output is captured in full after completion, not streamed. For very large files, there is no real-time progress.
