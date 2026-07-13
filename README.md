Personal collection of utility tools I've built for daily use on Windows. Covers file management, backups, metadata stripping, desktop customization, and system monitoring.

## Tools

| Tool | Lang | Portable | Description |
|---|---|---|---|
| `wallswitch/` | C# | Yes — standalone `.exe` | On-demand wallpaper randomizer with a shuffle queue (no repeats until all images are shown). Compiled with `csc.exe` — no runtime install needed. |
| `kdbx-backup/` | C# | Yes — two standalone `.exe` files | Backup pipeline for KeePass `.kdbx` files. An always-on watcher daemon snapshots databases on file change; a scheduler-triggered tool pushes snapshots to cloud remotes via rclone. |
| `shared/` | C# | Yes — all `.exe` files in `shared/bin/` | Unified directory for portable CLI tools. Contains `delcache` (find/delete cache dirs) and `dirdiff` (directory comparison). Config in `conf/`, sources in `src/`. Add **one** PATH entry (`shared/bin/`) for all tools. See [`shared/README.md`](shared/README.md). |
| `etsu/` | PowerShell | No — requires PowerShell | Two interactive exiftool frontends (`etsu` = ExifTool Simple Use). `read.ps1` displays metadata from a single file with dimmed values; `clean.ps1` strips EXIF/IPTC/XMP from images/videos/PDFs with `.bak` rollback, 5-stage progress, and auto-logging. Both share the same styled CLI. Requires `exiftool.exe` on PATH. |
| [`archive/`](archive/README.md) | — | — | Retired/abandoned tools kept for reference. |

**Portable** means the tool is a standalone `.exe` compiled with Windows' built-in `csc.exe` — no runtime, no install step, just copy and run. CLI tools are colocated in `shared/bin/` so a single PATH entry covers all of them.

### Why `shared/` exists instead of standalone tools?

Windows `setx PATH` has a ~2048-character limit. Each standalone `bin\` entry costs ~40–60 characters, so adding multiple bin entries would hit the ceiling. By putting every CLI `.exe` in one `bin/`, only **one** PATH entry is needed. Tools that don't need PATH access (e.g. `kdbx-backup`, `wallswitch`) stay in their own directories — `shared/` is only for commands you type in a terminal.

Each tool inside `shared/` is fully independent — removing one `.exe` and its config file won't affect any other tool. The entire `shared/` folder is portable: copy it anywhere, add `bin/` to PATH, and all CLI tools work.

## Highlights

- **Zero-dependency C# tools** — compiled with `csc.exe` (part of Windows), no NuGet, no .NET SDK needed beyond what ships with the OS.
- **PowerShell tools** — `etsu` uses PowerShell with WinForms for the native file dialog; no modules required.
- **Python tools** — stdlib-only where possible; `torui` (archived) depends on `rich` + `stem`.
- **C# tools are Windows-only** — they use Win32 APIs (`SystemParametersInfo` for wallpapers, `FileSystemWatcher`, etc.). PowerShell/Python tools can be installed on any platform (core logic is cross-platform), but the Windows-native folder dialogs (PowerShell + WinForms) won't work outside Windows.
