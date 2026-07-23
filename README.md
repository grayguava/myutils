# winkit

A collection of small Windows utilities, each built to solve one specific recurring problem — file management, backups, metadata handling, desktop customization, and system monitoring. Designed with safety in mind: destructive operations verify before they commit.

## Tools

| Tool | Lang | Portable | Description |
|---|---|---|---|
| [wallswitch/](wallswitch/README.md) | C# | Yes | Wallpaper randomizer with shuffle queue. |
| [kdbx-backup/](kdbx-backup/README.md) | C# | Yes | KeePass backup pipeline with watcher daemon + rclone push. |
| shared/ | C# | Yes | Portable CLI tool collection (delcache, dirdiff, catsort, reindex). One PATH entry for all. See [README](shared/README.md). |
| [etsu/](etsu/README.md) | PowerShell | No | simple ExifTool frontends: read metadata, strip EXIF/IPTC/XMP |
| [diskwatch/](diskwatch/README.md) | C# | Yes | Read-only disk health monitor with change detection and popup alerts. |
| [batcap/](batcap/README.md) | C# | Yes | Battery capacity logger via WMI, appends to logs/batcap.log. |
| [archive/](archive/README.md) | — | — | Retired tools, kept for reference only. |


**Portable** means the tool is a standalone `.exe` compiled with Windows' built-in `csc.exe` — no runtime, no install step, just copy and run. CLI tools are colocated in `shared/bin/` so a single PATH entry covers all of them.

### Why shared/ exists instead of standalone tools

Windows' `setx PATH` has a ~2048-character limit. Adding a separate PATH entry per tool would eventually hit that ceiling. Putting every CLI `.exe` in one `bin/` means the whole collection only costs a single PATH entry — while each tool inside stays fully independent (removing one `.exe` and its config file doesn't affect any other).

## Highlights

- **Built with AI-assisted development** — I direct the design and logic (safety checks, edge cases, config-driven behavior), and use AI tools to help with implementation.
- **Zero-dependency C# tools** — compiled with `csc.exe` (built into Windows), no NuGet, no .NET SDK beyond what ships with the OS.
- **Safety-first design** — destructive tools verify before committing (copy → hash-check → delete) and support `--dry-run` where relevant. `reindex` includes rollback logs for its last 25 runs.
- **PowerShell tools** — `etsu` uses PowerShell with WinForms for the native file dialog; no modules required.
- **C# tools are Windows-only** — they use Win32 APIs (`SystemParametersInfo`, `FileSystemWatcher`, etc.). PowerShell/Python tools are portable in logic, but native folder dialogs won't work outside Windows.
