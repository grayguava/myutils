# dirdiff — directory copy verification

- **Tool:** `dirdiff/src/dirdiff/` (package)
- **Language:** Python 3 (stdlib only — no pip dependencies)
- **Role:** Point-in-time comparison of a source and destination directory. Picks two folders via native Windows dialog, then walks both trees and reports discrepancies in filename presence, file sizes, and SHA256 hashes.

---

## Usage

No command-line arguments. The tool opens two native Windows folder pickers in sequence — first for the source directory, then for the destination (the copy). After selection, it scans both directories and prints a report to stdout.

### Run without installing

```
python -m dirdiff
```

(Must be run from the `src/` parent directory, or with `PYTHONPATH` set accordingly.)

### Install (editable, recommended for development)

```
pip install -e dirdiff/
```

Then use the `dirdiff` command anywhere:

```
dirdiff
```

### Install system-wide (once)

```
pip install dirdiff/
```

Then uninstall with:

```
pip uninstall dirdiff
```

### Report sections

1. **Files present** — count and percentage of source files found in destination.
2. **Missing files** — in source but not in dest (capped at 20 shown).
3. **Extra files** — in dest but not in source (capped at 20 shown).
4. **Size matches** — count and percentage of files with matching sizes.
5. **Size mismatches** — files where sizes differ (capped at 20 shown).
6. **Hash matches** — count and percentage of files with matching SHA256 hashes.
7. **Hash mismatches** — files whose content differs (capped at 20 shown).
8. **Hash errors** — files that could not be read on either side (capped at 10 shown).

The final line is either "All N files verified OK" or a bullet summary of issues.

---

## How it works

### Folder selection

Uses `System.Windows.Forms.OpenFileDialog` via an inline PowerShell command — the same technique as `exiftool/src/clean.py`. The dialog is repurposed as a folder picker by disabling `ValidateNames` and `CheckFileExists`, with a placeholder filename ("Select this folder") that the user never selects. The actual folder is obtained via `Split-Path $d.FileName -Parent` on the placeholder's parent directory.

The PowerShell invocation uses `powershell.exe` (Windows PowerShell 5.1), not `pwsh` (PowerShell 7.x). This is deliberate — `System.Windows.Forms` is available in Windows PowerShell without additional setup.

If PowerShell or the assembly-load fails, the script raises `DialogUnavailable` and exits. There is no typed-path fallback.

### Directory scanning

Both directories are walked with `os.walk` (`followlinks=False`). Each file is indexed by its relative path from the root, along with its absolute path and size in bytes. Empty directories produce no entries — they are invisible to the comparison.

File map keys are `pathlib.Path` objects representing the relative path (e.g. `subdir/file.txt`). The map is a plain `dict` — all entries live in memory for the duration of the script.

### Comparison phases

Three passes over the data:

1. **Set arithmetic on relative paths** — produces `in_both`, `missing`, and `extra` sets. Sorted for deterministic output.
2. **Size comparison** — iterates `in_both` in sorted order and compares `st_size`.
3. **Hash comparison** — submits one `(rel_path, src_abs, dst_abs)` tuple per file pair to a `ThreadPoolExecutor` with 8 workers. Each task hashes both sides sequentially (1 MB chunks, SHA256). Results collected with `as_completed`; progress printed on a single line via `\r`.

### Progress display

During the hash phase, a single-line progress counter is shown:

```
Computing SHA256 hashes (47/963)
```

This overwrites itself via carriage return. The final count is left visible after completion.

---

## Compatibility

| Aspect | Status |
|---|---|
| Python version | 3.8+ (f-strings, `pathlib` features) |
| OS | Windows only (PowerShell folder picker) |
| Dependencies | None beyond stdlib |
| Unicode paths | Supported (PowerShell emits UTF-8, `pathlib` handles natively) |
| Network drives | Works if accessible from PowerShell |
| Very long paths | Depends on Windows path length support |
| Large file sets | Bounded by available RAM (full file map in memory) |

The script is Windows-only because the folder picker calls `powershell.exe` with `System.Windows.Forms`. The scanning and hashing logic is cross-platform, but there is no non-Windows fallback for folder selection.

---

## Output format

The report is plain text formatted for terminal readability, not machine parsing:

```
  ================================================
  Directory Comparison Report
  ================================================

  Source:      D:\originals
  Dest:        D:\backup\originals

  ──────────────────────────────────────────────────

  Scanning directories...

  Files present:     957 / 959      ( 99.8%)

  Missing files (2):

    - subdir\notes.txt
    - config.ini


  ──────────────────────────────────────────────────

  Sizes matched:     957 / 957      (100.0%)

  ──────────────────────────────────────────────────

  Computing SHA256 hashes (957/957)

  Hashes matched:    957 / 957      (100.0%)

  ──────────────────────────────────────────────────

  All 959 files verified OK.
```

There is no `--json` or `--csv` flag. For machine-consumable output, the source code's data structures (`src_map`, `dst_map`, the mismatch lists) are trivially accessible by importing the module.

---

## Comparison phases

### Filename presence

The relative path is the primary key. A file that exists in source under `docs/readme.txt` is matched against `docs/readme.txt` in dest. If the path doesn't exist in dest, it's "missing" regardless of whether the same content exists elsewhere under a different name.

### Size comparison

`st_size` from `os.stat()` is compared. A mismatch of even 1 byte is reported. Zero-byte files are handled correctly — if both sides are zero bytes, they match.

### Hash comparison

SHA256 is computed on the full file content, read in 1 MB chunks. Both sides of each pair are hashed in the same worker thread so that a slow disk on one side doesn't delay the other side's results. A mismatch means the content is different with cryptographic certainty.

---

## Concurrency

Hash workers: 8 threads in a `ThreadPoolExecutor`. This is a fixed constant, not configurable.

Each worker sequentially opens and hashes one source + one destination file pair. The pool is not saturated by individual file pairs — the parallelism comes from having multiple file pairs being hashed simultaneously. For an SSD, 8 concurrent readers is well within the device's queue depth and keeps the pipeline filled without overwhelming the scheduler.

---

## Error handling

| Scenario | Handling |
|---|---|
| Dialog fails to launch | `DialogUnavailable` raised → exit code 1 with message |
| User cancels dialog | `sys.exit(1)` with "No folder selected" |
| Selected path not a directory | `sys.exit(1)` with message |
| File cannot be stat'd during scan | Warning printed, file skipped |
| File cannot be opened for hashing | Counted as hash error, listed separately |
| KeyboardInterrupt (Ctrl+C) | Clean exit with "Interrupted" message |

---

## Design decisions

### Why SHA256 and not a faster algorithm

SHA256 is the default choice for verification because:
- Collision resistance matters for verification — even if the chance is negligible for random bit flips, SHA256 removes the question entirely.
- The stdlib `hashlib` module provides it without any dependency.
- File-reading throughput is the bottleneck, not the hash function. Switching to SHA1 or MD5 would not meaningfully speed up a disk-bound operation.

### Why 8 workers

8 threads is a reasonable default for a desktop machine with an SSD. Hashing is CPU-light and I/O-bound per thread; more threads than CPU cores doesn't hurt since each thread spends most of its time waiting for `read()` to return. 8 keeps the pool small enough not to overwhelm the OS scheduler.

### Why the folder picker and not a CLI argument

The script is designed for occasional use — "did this copy succeed?" — not for automation. A native GUI dialog is more ergonomic than typing two paths for this use case. The same PowerShell dialog pattern is shared with `clean.py` for consistency.

### Why no multiprocessing

Hashing is I/O-bound, not CPU-bound. Python threads are sufficient because the GIL is released during `read()` and `hashlib.update()` calls. Multiprocessing would add overhead (pickling file paths, IPC) without meaningful throughput gain.

---

## Known limitations

- **No recursive comparison of empty directories** — empty folders produce no file entries and are invisible to the comparison.
- **No progress indicator during directory scan** — only during the hash phase. For very large trees (>100k files), the initial `os.walk` can take noticeable time without feedback.
- **Windows-only** — the folder picker relies on `System.Windows.Forms`.
- **No typed-path fallback** — if the PowerShell dialog fails, the script exits. There is no "enter path manually" mode.
- **120-second dialog timeout** — if the system is locked or the dialog doesn't close, `subprocess.run` times out.
- **File changes during scan** — no locks are held. Files created/deleted during scanning produce an inconsistent point-in-time snapshot. Acceptable for post-copy verification.
- **Symlinks not followed** — `os.walk` uses `followlinks=False`. Symlinks are compared as files (size + hash of the link target if resolvable).
