# etsu — ExifTool Simple Use

- **Language:** PowerShell (no modules required)
- **Role:** Two interactive tools built around `exiftool` — one for **reading** metadata, one for **cleaning** it.

---

## Tools

### read.ps1 — metadata viewer

Displays all metadata from a single file in a styled, color-coded layout.

```
powershell -ExecutionPolicy Bypass -File read.ps1
```

The tool:
1. Detects `exiftool.exe` on PATH (falls back to local `exiftool.exe`).
2. Shows a styled header with the detected version.
3. Prompts to open a file picker — single file only.
4. Displays every tag returned by `exiftool`, with labels in default white and values dimmed.

No logging, no multi-file support.

### clean.ps1 — metadata stripper

Strips EXIF/IPTC/XMP/metadata from images, videos, and PDFs with full safety guarantees.

```
powershell -ExecutionPolicy Bypass -File clean.ps1
```

The tool:
1. Detects `exiftool.exe` on PATH (falls back to local `exiftool.exe`).
2. Shows a styled header with the detected version.
3. Prompts to open a file picker — multi-file selection.
4. **Stage 1** — Copies selected files to a temp workspace and verifies byte sizes match.
5. **Stage 2** — Runs `exiftool -all= -overwrite_original -P -v` on each temp copy to strip metadata.
6. **Stage 3** — Verifies cleaned files exist, are non-empty, and are readable.
7. **Stage 4** — Renames originals to `.bak`, moves cleaned copies into place, confirms integrity, then deletes `.bak` files. On any failure, all originals are restored before exit.
8. **Stage 5** — Reports success, writes a timestamped log, and waits for Enter/Spacebar.

#### Cancellation

Pressing `n` at the file picker prompt, or closing the file dialog without selecting files, exits immediately without creating a log.

---

## Common features

Both tools share the same UI style:

```
 ┌─ 🐾 ETSU   |   Read   |   Exiftool: vX.X.X
 ──────────────────────────────────────────────

  Open file picker? (Y/N): y
  ...
```

- Box-drawing header with tool name, mode, and exiftool version
- Enter / Spacebar to exit (other keys ignored)
- exiftool resolved from PATH first, then local `exiftool.exe`

---

## Requirements

| Dependency | Why |
|---|---|
| **PowerShell 5.1+** | Ships with Windows 10/11. |
| **exiftool.exe** | The actual metadata engine. Place on PATH *or* copy into the `etsu/` directory. |
| **System.Windows.Forms** | Built into .NET Framework (used for the native file dialog via `Add-Type`). |

### Supported file types

`jpg`, `jpeg`, `png`, `webp`, `heic`, `tif`, `tiff`, `mp4`, `mov`, `pdf`

---

## Logging (clean.ps1 only)

Each clean run produces a timestamped log in `logs/`:

```
logs/
  clean_20260713_212950.log
  clean_20260713_213100.log
  ...
```

Only successful processing runs (files were selected and work began) produce a log. Cancelled runs do not. Logs are rotated — the 10 most recent are kept.

### Log format

```
ExifTool Metadata Clean Log
Timestamp : 2026-07-13 21:29:50
Outcome   : SUCCESS
----------------------------------------

ExifTool path: D:\exiftool\exiftool.exe
ExifTool version: 12.92
Files selected (3):
  D:\pics\photo1.jpg
  D:\pics\photo2.png
  D:\pics\photo3.pdf

[1/5] Copying files to temp workspace
  ...
[2/5] Cleaning metadata
  D:\pics\photo1.jpg
    - EXIF
    - IPTC
  ...
All done. 3 file(s) cleaned in place.
```

---

## Design decisions

### Why temp workspace first instead of in-place stripping?

Running exiftool directly on originals risks corrupting files if the process is interrupted or a disk error occurs. By copying to a temp directory first, the originals remain untouched until the cleaned copies are fully verified (exists, non-empty, readable). Only then are originals swapped out one-by-one via `.bak` rename, with full rollback on any single failure.

### Why byte-size check during copy?

A silent partial copy (disk full, permission issue mid-write) can produce a file that appears to exist but is truncated. Comparing byte sizes immediately after copy catches this before any processing begins.

### Why a file dialog instead of drag-and-drop or CLI args?

Metadata inspection and cleaning are inherently interactive — you need to see the files being selected. The native Explorer dialog provides search, thumbnails, multi-select, and quick-access navigation that a CLI argument can't match.

### Why no log on cancellation?

Writing a log for every cancelled or mis-typed prompt would clutter the log directory with noise. Logs are only created when actual work happens.

---

## File structure

```
etsu/
├── clean.ps1         ← metadata stripper
├── read.ps1          ← metadata viewer
├── logs/             ← auto-created (clean only), holds last 10 logs
└── README.md
```

---

## Compatibility

| Aspect | Status |
|---|---|
| OS | Windows 7+ (requires PowerShell 5.1+ and .NET Framework) |
| exiftool | Required on PATH or in `etsu/` directory |
| File dialog | Native Windows Explorer (via WinForms) |
| Log format | Plain text UTF-8, append-only |
| Log retention | Last 10 logs, auto-rotated (clean.ps1 only) |

## Known limitations

- **Windows-only** — the WinForms file dialog via `Add-Type` won't work on non-Windows systems.
- **PowerShell required** — not a standalone executable. Must be invoked via `powershell -ExecutionPolicy Bypass`.
- **clean.ps1: Sequential processing** — files are processed one at a time. No parallel metadata stripping.
- **clean.ps1: No dry-run mode** — there is no preview of what metadata will be deleted before committing.
- **read.ps1: Single file only** — one file per invocation.
- **exiftool must be on PATH or local** — no automatic download or bundled binary.
