# etsw — ExifTool simple wrapper

**`bin\etgui_wrapper.exe`** — WinForms GUI that strips all metadata from image/video/PDF files in-place. Safe copy-then-swap with `.bak` rollback.

**Requires:** `exiftool.exe` on PATH (place in [`exiftool/`](../exiftool/)).

## Usage

1. Click **Select Files...** — opens a native multi-select file dialog.
2. Cleaning starts automatically. Progress streams to the dark log panel.
3. Done — all original files replaced with metadata-free copies.

## Workflow

| Stage | What happens |
|---|---|
| 1/4 Copy | Files copied to temp dir, verified by size |
| 2/4 Wipe | `exiftool -all= -overwrite_original -P` strips metadata |
| 3/4 Verify | Wiped files checked: exists, non-empty, readable |
| 4/4 Swap | Orig → `.bak`, cleaned copy → orig path; `.bak` cleaned up on success |

Any failure triggers rollback — all `.bak` restored, temp cleaned.

## Structure

```
etgui_wrapper/
├── bin/etgui_wrapper.exe    ← compiled (19 KB)
├── src/etgui_wrapper.cs     ← single-file source
├── logs/clean/              ← run logs (max 10)
├── build.bat
└── README.md
```

## Building

```
build.bat
```

Uses `csc.exe` from .NET Framework. No install step.
