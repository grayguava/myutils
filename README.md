Personal collection of utility tools built for daily use on Windows. Covers file management, backups, metadata stripping, desktop customization, and system monitoring.

## Tools

| Tool | Lang | Portable | Description |
|---|---|---|---|---|
| [wallswitch/](wallswitch/README.md) | C# | Yes | Wallpaper randomizer with shuffle queue. |
| [kdbx-backup/](kdbx-backup/README.md) | C# | Yes | KeePass backup pipeline with watcher daemon + rclone push. |
| shared/ | C# | Yes | Portable CLI tool collection. One PATH entry for all. See [README](shared/README.md). |
| [etsu/](etsu/README.md) | PowerShell | No | ExifTool frontends: read metadata, strip EXIF/IPTC/XMP with rollback. |
| [diskwatch/](diskwatch/README.md) | C# | Yes | Read-only disk health monitor with change detection and popup alerts. |
| [archive/](archive/README.md) | — | — | Retired tools kept for reference. |

**Portable** means the tool is a standalone .exe compiled with Windows' built-in `csc.exe` — no runtime, no install step, just copy and run. CLI tools are colocated in `shared/bin/` so a single PATH entry covers all of them.

### Why shared/ exists instead of standalone tools?

Windows `setx PATH` has a ~2048-character limit. Each standalone `bin\` entry costs ~40-60 characters, so adding multiple bin entries would hit the ceiling. By putting every CLI .exe in one `bin/`, only one PATH entry is needed. Tools that don't need PATH access (e.g. kdbx-backup, wallswitch) stay in their own directories — shared/ is only for commands you type in a terminal.

Each tool inside shared/ is fully independent — removing one .exe and its config file won't affect any other tool. The entire shared/ folder is portable: copy it anywhere, add `bin/` to PATH, and all CLI tools work.

## Highlights

- **AI-vibed** — all tools were written with AI assistance (opencode - various models/claude). The code is functional but not obsessively polished.
- **Zero-dependency C# tools** — compiled with `csc.exe` (part of Windows), no NuGet, no .NET SDK needed beyond what ships with the OS.
- **PowerShell tools** — etsu uses PowerShell with WinForms for the native file dialog; no modules required.
- **Python tools** — stdlib-only where possible; torui (archived) depends on `rich` + `stem`.
- **C# tools are Windows-only** — they use Win32 APIs (`SystemParametersInfo` for wallpapers, `FileSystemWatcher`, etc.). PowerShell/Python tools can be installed on any platform (core logic is cross-platform), but the Windows-native folder dialogs (PowerShell + WinForms) won't work outside Windows.
