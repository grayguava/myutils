# dirdiff — compare two directories by filename, size, and SHA256

- **Tool:** `bin\dirdiff.exe`
- **Source:** `src\dirdiff.cs`
- **Language:** C#, compiled via `csc.exe /target:exe`
- **Role:** Opens two native folder pickers, walks both directories, and reports discrepancies — missing files, extra files, size mismatches, and SHA256 hash differences.

---

## Usage

```
dirdiff
```

No arguments. Two modern Explorer-style folder pickers pop up sequentially — pick source first, then destination. The report prints to the console and exits.

### What happens

1. **Pick source** — native `OpenFileDialog` (repurposed as a folder picker via `ValidateNames=false`, `CheckFileExists=false`).
2. **Pick destination** — same dialog for the copy target.
3. **Scan** — walk both directories recursively, building a map of relative paths to absolute paths and sizes.
4. **Compare presence** — set difference: which files exist in only one side.
5. **Compare sizes** — for files in both, check if byte counts match.
6. **Compare SHA256** — hash all files present in both sides, in parallel (8 threads).
7. **Print report** — summary statistics + detailed lists of every discrepancy found.

---

## How it works

### Directory scanning

`Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)` returns every file recursively. Each file's relative path is computed by stripping the root prefix. Files that cannot be stat'd (permission errors, locked files) are silently skipped.

### Presence comparison

Three sets computed from relative paths:

- **In both** — intersection of source and destination keys.
- **Missing** — source paths not found in destination.
- **Extra** — destination paths not found in source.

Each is sorted alphabetically for stable diffs. Lists longer than 20 entries are truncated with "... and N more".

### Size comparison

For every file present in both directories, `FileInfo.Length` is compared. Size mismatches are shown with both byte counts.

### SHA256 hashing

Files present in both are hashed using `SHA256.Create()` in 1 MB chunks via `Parallel.ForEach` (8 threads). Progress is printed inline (`N/M`). A progress counter is shown during computation. Mismatches or unreadable files are counted and reported.

### Report format

```
  ================================================
  Directory Comparison Report
  ================================================

  Source:      D:\source
  Dest:        D:\dest

  ──────────────────────────────────────────────────

  Scanning directories...

  Files present:      957 / 959        ( 99.8%)

  Missing files (2):

    - file_a.txt
    - file_b.txt

  ──────────────────────────────────────────────────

  Sizes matched:      957 / 957        (100.0%)

  ──────────────────────────────────────────────────

  Computing SHA256 hashes (957/957)

  Hashes matched:     957 / 957        (100.0%)

  ──────────────────────────────────────────────────

  All 959 files verified OK.
```

---

## Building

### Prerequisites

- .NET Framework 4.0+ (ships with Windows 8+).
- The C# compiler `csc.exe` at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- `System.Windows.Forms.dll` — part of .NET Framework, available on all Windows systems with .NET installed.

### Build

```
build.bat
```

Compiles `src\dirdiff.cs` → `bin\dirdiff.exe`. Links `System.Windows.Forms.dll` for the native folder picker. No Visual Studio, no `dotnet` CLI, no NuGet, no install step.

### Build output

```
dirdiff/
├── src/
│   └── dirdiff.cs         ← source (edit this)
├── bin/
│   └── dirdiff.exe         ← compiled binary (build output)
├── build.bat
└── README.md
```

---

## Design decisions

### Why C# and not Python

The original `dirdiff` (now archived) was a Python script that launched PowerShell to show a folder picker. That meant two runtime dependencies (Python + PowerShell) and a fragile command-line construction. C# references `System.Windows.Forms` directly — no subprocess, no PowerShell dependency, and the picker is a native Windows dialog, not a COM wrapper launched through a shell command.

### Why a folder picker instead of command-line arguments

Directory comparison is inherently interactive — you need two paths. A folder picker is faster than typing paths (especially long Windows paths), eliminates typos, and shows the actual directory tree. `dirdiff` is a fire-and-forget tool: run it, pick two folders, get the report.

### Why parallel hashing

SHA256 of large files is CPU-bound. Hashing sequentially can take minutes for directories with many large files (videos, ISOs, disk images). `Parallel.ForEach` with 8 threads saturates modern CPUs while remaining I/O-bound for smaller files (SSD).

### Why no recursive diff / subdirectory breakdown

The report lists files that differ but doesn't group them by subdirectory or show a tree view. This is intentional: the output is flat and grep-friendly. If you need a tree breakdown, pipe the output through a script.

### Why `OpenFileDialog` repurposed as folder picker

The classic `FolderBrowserDialog` is an XP-era tree widget that doesn't support the address bar, search, or Favorites. The `OpenFileDialog` with `ValidateNames=false` and `CheckFileExists=false` provides the full modern Explorer experience — breadcrumb navigation, quick access, search, and network path entry.

---

## Known limitations

- **No single-file diff** — dirdiff compares presence and hashes but does not show line-by-line or binary diffs of mismatched files. For text files, use a dedicated diff tool.
- **No filtering** — all files are included. There is no way to exclude paths or extensions. Add `.gitignore`-style filtering at the shell level (`dirdiff | grep ...`).
- **Large directories** — the file map is held in memory. For directories with millions of files (unlikely for a copy-verification use case), memory usage may be significant.
- **No remote / network paths** — `Directory.EnumerateFiles` works with mapped drives and UNC paths, but performance depends on network speed. Hashing large files over the network is slow.
- **Hash progress is approximate** — because files complete in non-deterministic order (thread pool), the `N/M` counter advances per completed hash, not per file position.

---

## Compatibility

| Aspect | Status |
|---|---|
| OS | Windows 7+ (requires .NET Framework 4.0+) |
| Architecture | x64 (`Framework64\csc.exe`; recompile for x86 if needed) |
| Folder picker | Modern Explorer-style (`OpenFileDialog`) |
| .NET version | Compiled against .NET Framework 4.0 (csc.exe v4.0.30319) |
| Dependencies | `System.Windows.Forms.dll` (ships with .NET Framework) |

### .NET Framework

The tool targets .NET Framework 4.0, which is included in Windows 8+ and available as an update for Windows 7. The compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` is installed as part of the .NET Framework SDK component of Windows.

For 32-bit systems, use `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe` instead. Edit `build.bat` to point to the correct path.

### Comparison to the Python predecessor

| Python version (archived `dirdiff_old`) | C# version |
|---|---|
| Requires Python 3.8+ | Standalone `.exe`, no runtime |
| Folder picker via PowerShell subprocess | Native `OpenFileDialog`, no subprocess |
| `os.walk` | `Directory.EnumerateFiles` |
| `concurrent.futures` (8 workers) | `Parallel.ForEach` (8 threads) |
| `hashlib.sha256` (1 MB chunks) | `SHA256.Create()` (1 MB chunks) |
| pip-installable | Copy `bin\` and run |
