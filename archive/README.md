# archive — retired / abandoned tools

| Tool | Lang | Portable | Description |
|---|---|---|---|
| `wallsys_old/` | C# | Yes — `.exe` files | Predecessor to `wallswitch/`. Two separate binaries for nature/tech wallpaper pools. Simple random picker (no shuffle queue). Still works — compile with `build.bat`, populate `assets/nature/` and `assets/tech/`, then map hotkeys (e.g. <kbd>Alt+Shift+N</kbd> for nature, <kbd>Alt+Shift+T</kbd> for tech) to each `.exe`. |
| `delpyc/` | Python | No — pip install required | Superseded by `delcache/`. Recursively deletes `__pycache__` directories. Requires Python + `click`. |
| `torui/` | Python | No — pip install required | Abandoned live terminal dashboard for a local Tor daemon. Uses `rich` and `stem`. |
| `PCHealth/` | PowerShell | Yes — run `.ps1` directly | Collection of scripts for system health telemetry (temps, storage, network, software, events, drivers, file integrity). |
| `dirdiff_old/` | Python | No — pip install required | Superseded by `shared/bin/dirdiff.exe` (C# rewrite). Compares two directories by filename, size, and SHA256. Stdlib-only. |
| `exiftool_old/python/` | Python | No — run `python src/clean.py` | Strips EXIF/IPTC/XMP/metadata from images, videos, and PDFs. Requires `exiftool` CLI on PATH. PowerShell-based file dialog. |
