Personal collection of utility tools I've built for daily use on Windows. Covers file management, backups, metadata stripping, desktop customization, and system monitoring.

## Tools

| Tool | Lang | Portable | Description |
|---|---|---|---|
| `wallswitch/` | C# | Yes — standalone `.exe` | On-demand wallpaper randomizer with a shuffle queue (no repeats until all images are shown). Compiled with `csc.exe` — no runtime install needed. |
| `kdbx-backup/` | C# | Yes — two standalone `.exe` files | Backup pipeline for KeePass `.kdbx` files. An always-on watcher daemon snapshots databases on file change; a scheduler-triggered tool pushes snapshots to cloud remotes via rclone. |
| `delcache/` | C# | Yes — standalone `.exe` | Finds and deletes cache/temp directories (`__pycache__`, `node_modules`, etc.). Config-driven — add targets in `cacheDirs.ini`. Recursive, Y/N prompt, no Python dependency. Built with `csc.exe`. Supersedes `delpyc/` (archived). |
| `dirdiff/` | C# | Yes — standalone `.exe` | Compares two directories by filename, size, and SHA256. Opens native Windows folder pickers (`FolderBrowserDialog`). Hashes in parallel (8 threads). Built with `csc.exe`. |
| `exiftool/` | Python | No — run directly (`python src/clean.py`) | Strips EXIF/IPTC/XMP/metadata from images, videos, and PDFs. Safe copy-then-swap workflow with full rollback on failure. Requires `exiftool` CLI on PATH. |
| [`archive/`](archive/README.md) | — | — | Retired/abandoned tools kept for reference. |

**Portable** means the tool is a standalone `.exe` compiled with Windows' built-in `csc.exe` — no runtime, no install step, just copy and run.

## Highlights

- **Zero-dependency C# tools** — compiled with `csc.exe` (part of Windows), no NuGet, no .NET SDK needed beyond what ships with the OS.
- **Python tools** — stdlib-only where possible; `torui` (archived) depends on `rich` + `stem`.
- **C# tools are Windows-only** — they use Win32 APIs (`SystemParametersInfo` for wallpapers, `FileSystemWatcher`, etc.). Python tools can be installed on any platform (core logic is cross-platform), but the Windows-native folder dialogs (PowerShell + WinForms) won't work outside Windows.
