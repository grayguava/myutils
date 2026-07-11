# delcache — find and delete cache/temp directories

- **Tool:** `bin\delcache.exe`
- **Source:** `src\delcache.cs`
- **Language:** C#, compiled via `csc.exe /target:exe`
- **Role:** Recursively finds directories matching names in `cacheDirs.ini` (e.g. `__pycache__`, `node_modules`), lists them, and prompts for deletion.

---

## Usage

```
delcache [path]
```

- **`path`** — root directory to search (default: current directory)

### Examples

```
delcache                     search cwd, prompt before deleting
delcache D:\Projects         search specific path, prompt before deleting
```

### What happens

1. Loads `bin\cacheDirs.ini` — a list of directory names to find (blank lines and `#` comments ignored; defaults to `__pycache__` + `node_modules` if missing or empty).
2. Recursively walks `path` looking for directories with matching names.
3. Prints the full path of every match.
4. Prompts `[y/N]` — only deletes on explicit `y` or `yes`.

---

## How it works

### Startup

1. `Assembly.GetExecutingAssembly().Location` resolves the `.exe` directory.
2. Loads `cacheDirs.ini` from the `.exe` folder — one directory name per line.
3. Resolves the search root (argument or current directory).
4. For each target name, calls `Directory.EnumerateDirectories(root, target, SearchOption.AllDirectories)` — recursive, case-insensitive (Windows filesystem).
5. Collates all matches into a single list, sorted by path.
6. Lists results and prompts for confirmation.
7. On confirmation, iterates the list and calls `Directory.Delete(path, recursive: true)` on each.
8. Reports success count and logs any failures (permission errors, locked directories).

### Error handling

- **UnauthorizedAccessException** during search — silently skipped (can't read a subdirectory, skip it).
- **IOException** during delete — printed to stderr with the path and error message, other deletions continue.
- **Missing root path** — prints error and exits.
- **Empty config** — falls back to defaults (`__pycache__`, `node_modules`).

---

## Configuration

**Location:** `bin\cacheDirs.ini`

Edit to add or remove target directory names. One name per line, `#` for comments:

```ini
__pycache__
node_modules
.bazel
.cache
.vs
```

If the file is missing or empty, defaults to `__pycache__` and `node_modules`.

> [!WARNING]
>
> `delcache` will delete every directory whose name matches an entry in `cacheDirs.ini`. A typo, a misplaced `..`, or a malicious entry (e.g. an attacker who gains write access to the config file) can cause data loss. **Always read the list of found directories carefully before typing `y`.** Only list directory names you are certain you want to recurse into and delete.

---

## Adding to PATH

Add `bin\` to your `PATH` so `delcache` works from anywhere:

```
setx PATH "%PATH%;D:\Tools\myutils\delcache\bin"
```

(Replace the path with your actual location, then restart your terminal.)

---

## Building

### Prerequisites

- .NET Framework 4.0+ (ships with Windows 8+).
- The C# compiler `csc.exe` at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.

### Build

```
build.bat
```

Compiles `src\delcache.cs` → `bin\delcache.exe`. No Visual Studio, no `dotnet` CLI, no NuGet, no install step.

### Build output

```
delcache/
├── src/
│   └── delcache.cs        ← source (edit this)
├── bin/
│   ├── delcache.exe        ← compiled binary (build output)
│   └── cacheDirs.ini       ← configuration
├── build.bat
└── README.md
```

---

## Design decisions

### Why C# and not Python (delpyc)

The original `delpyc` (now archived) was a Python CLI using `click`. It required Python 3.8+, pip, and the `click` package. `delcache` is a standalone `.exe` with no runtime dependencies — copy `bin/` anywhere, add to PATH, done.

### Why a config file instead of hardcoded names

`cacheDirs.ini` lets you add targets without recompiling. `__pycache__` and `node_modules` are defaults, but you can add `.vs`, `.bazel`, `.cache`, `build/`, `dist/`, or any other directory name you want to clean up across projects.

### Why always prompt, never auto-delete

Cache directories are safe to delete in theory, but a typo in `cacheDirs.ini` or a mistaken root path can delete the wrong data. Forcing a Y/N confirmation on every run means you see exactly what will be deleted before committing. There is no `-y` flag.

### Why no preview mode / dry-run

The tool prints the full list of found directories before prompting. If you type `n`, nothing is deleted — that is the dry run. Adding a separate `--dry-run` flag would duplicate the existing prompt behaviour.

---

## Known limitations

- **No exclusions** — all matches are listed and deleted together. You cannot skip specific paths within a run.
- **No parallel deletion** — directories are deleted sequentially. Large `node_modules` trees may take a moment.
- **No network paths** — `Directory.EnumerateDirectories` works with UNC paths, but performance over a network is unpredictable.
- **Follows symlinks** — `SearchOption.AllDirectories` follows directory junctions and symlinks. A symlink pointing outside the search root will be deleted (removes the link, not the target).

---

## Compatibility

| Aspect | Status |
|---|---|
| OS | Windows 7+ (requires .NET Framework 4.0+) |
| Architecture | x64 (`Framework64\csc.exe`; recompile for x86 if needed) |
| .NET version | Compiled against .NET Framework 4.0 (csc.exe v4.0.30319) |
| Dependencies | None beyond Windows built-ins |

### .NET Framework

The tool targets .NET Framework 4.0, which is included in Windows 8+ and available as an update for Windows 7. The compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` is installed as part of the .NET Framework SDK component of Windows.

For 32-bit systems, use `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe` instead. Edit `build.bat` to point to the correct path.
