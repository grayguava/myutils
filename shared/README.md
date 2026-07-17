# shared — unified CLI tools

Single directory housing all portable CLI tools. One `bin/` for PATH, one `conf/` for config files, one `build.bat` to compile everything.

Each tool is fully independent — removing one .exe and its config file won't affect any other tool in the directory. The entire shared/ folder is portable: copy it anywhere, add its `bin/` to PATH, and all tools work.

---

## delcache

Finds and deletes cache/temp directories (`__pycache__`, `node_modules`, etc.) by recursively scanning a root path and matching directory names against a config file.

```
delcache [path]
```

- **path** — root directory to search (default: current directory)

### Configuration

**Location:** `conf/cacheDirs.ini`

One directory name per line, `#` for comments:

```ini
__pycache__
node_modules
.bazel
.cache
.vs
```

If the file is missing or empty, defaults to `__pycache__` and `node_modules`.

> [!WARNING]
> A typo or malicious entry can cause data loss. Always read the found list before typing y.

### How it works

1. Resolves `conf/cacheDirs.ini` relative to the .exe location (`shared/bin/delcache.exe` -> `shared/conf/cacheDirs.ini`).
2. Reads target directory names from the file — one per line, blank lines and `#` comments ignored.
3. For each target, calls `Directory.EnumerateDirectories(root, target, SearchOption.AllDirectories)` to find every matching subdirectory at any depth.
4. Prints the full path of every match, numbered by count.
5. Prompts [y/N] — only proceeds on explicit y or yes.
6. Iterates the list and deletes each directory with `Directory.Delete(path, true)`.
7. Reports success count and prints failures to stderr (permission errors, locked files).

**Error handling:**
- Directories that can't be read during search (permission denied) are silently skipped.
- Directories that fail to delete are logged to stderr with the reason; remaining deletions continue.
- If the root path doesn't exist, prints an error and exits.

### Design decisions

- **Why C# over Python (delpyc):** The original delpyc required Python 3.8+ and the `click` package. delcache is a standalone .exe with zero runtime dependencies — copy and run.
- **Why always prompt:** Cache directories are safe to delete in theory, but a typo in `cacheDirs.ini` or a wrong root path can delete the wrong data. Forcing Y/N confirmation on every run ensures you see exactly what will be deleted.
- **Why a config file:** Adding or removing targets (node_modules, .cache, .vs) doesn't require recompilation. The config file is editable by any text editor.

### Known limitations

- No exclusions — you can't skip specific paths within a single run.
- No parallel deletion — directories are removed sequentially.
- Follows symlinks — `SearchOption.AllDirectories` traverses junctions and symlinks.

---

## dirdiff

Compares two directories by filename, size, and SHA256. Opens native Explorer-style folder pickers and prints a detailed report.

```
dirdiff [<source> <destination>]
```

### Modes

| Args | Behavior |
|---|---|
| `dirdiff "D:\src" "D:\dst"` | Compare the two paths directly — works on any OS |
| `dirdiff` | Opens two Explorer-style folder pickers (Windows only) |

No config file.

### How it works

#### Source and destination

If two arguments are provided, they're used directly as source and destination paths — this works on any OS. If no arguments are given (Windows only), two Explorer-style folder pickers pop up sequentially using `OpenFileDialog` with `ValidateNames = false` and `CheckFileExists = false` — the same trick the old Python version used through PowerShell, but called directly from C# without a subprocess.

#### Directory scanning

`Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)` recursively walks each selected directory. Each file's relative path is computed by stripping the root prefix. Files that can't be stat'd (permission, locked) are silently skipped.

#### Three comparisons

| Check | Method | What's reported |
|---|---|---|
| **Presence** | `HashSet` difference on relative paths | Missing (in source only) and extra (in dest only) files |
| **Size** | `FileInfo.Length` comparison | Path + both byte counts when they differ |
| **SHA256** | `Parallel.ForEach` (8 threads), 1 MB chunks | Count of mismatched or unreadable files |

#### Example output

```
  ================================================
  Directory Comparison Report
  ================================================

  Source:      D:\source
  Dest:        D:\dest

  Files present:      957 / 959        ( 99.8%)

  Missing files (2):

    - file_a.txt
    - file_b.txt

  Sizes matched:      957 / 957        (100.0%)

  Computing SHA256 hashes (957/957)

  Hashes matched:     957 / 957        (100.0%)

  All 959 files verified OK.
```

### Design decisions

- **Why C# over Python:** The original Python dirdiff launched a PowerShell subprocess to show a folder picker. That meant two runtimes (Python + PowerShell) and a fragile command-line construction. C# calls `System.Windows.Forms.OpenFileDialog` directly — no subprocess, no runtime dependencies.
- **Why a folder picker instead of CLI arguments:** Directory comparison is inherently interactive. A folder dialog is faster, eliminates typos, and shows the actual filesystem tree.
- **Why parallel hashing:** SHA256 of large files is CPU-bound. Hashing sequentially can take minutes for many large files. `Parallel.ForEach` with 8 threads saturates modern CPUs.
- **Why OpenFileDialog repurposed as a folder picker:** The classic `FolderBrowserDialog` is an XP-era tree widget with no address bar, search, or quick access. The OpenFileDialog trick gives the full modern Explorer dialog.

### Known limitations

- No single-file diff — only presence/size/hash comparison, no line-by-line diff.
- No filtering — all files are included. Use `dirdiff | grep` at the shell level.
- In-memory file map — directories with millions of files will use significant memory.
- Hash progress counter is approximate — files complete in non-deterministic order.

---

## catsort

Sorts files into category folders by extension. Copies each matched file into its category subfolder, verifies the copy via SHA256, then deletes the original. Unmatched files are left untouched.

```
catsort [directory] [--dry-run]
```

| Arg | Default | Description |
|---|---|---|
| `directory` | current dir | Directory to scan and sort |
| `--dry-run` / `-n` | off | Preview only — no copies or deletes |

### Configuration

**Location:** `conf/catsort.ini`

```ini
[Images]
ext=.jpg,.jpeg,.png,.gif,.bmp,.webp,.svg,.ico

[Videos]
ext=.mp4,.mkv,.avi,.mov,.wmv,.flv,.webm

[Documents]
ext=.pdf,.doc,.docx,.txt,.md,.csv,.json,.xml,.js,.ts,.html,.css,.py,.cs,.rs,.yaml,.yml,.ini,.cfg,.log,.sql,.bat,.ps1,.sh

[Archives]
ext=.zip,.rar,.7z,.tar,.gz,.bz2,.xz

[Audio]
ext=.mp3,.wav,.flac,.aac,.ogg,.wma,.m4a
```

Each [Category] section has an `ext=` line with comma-separated extensions. Add or remove categories freely — no recompilation needed.

### How it works

1. Reads `conf/catsort.ini` relative to the .exe location (`shared/bin/catsort.exe` -> `shared/conf/catsort.ini`).
2. Scans the target directory for files (non-recursive).
3. For each file, matches its extension against every category.
4. Creates the category subfolder if it doesn't exist.
5. Copies the file into the category folder.
6. Computes SHA256 of both original and copy — if they match, deletes the original.
7. Reports moved count, verified count, and any failures.

### Design decisions

- **Copy then delete (not move):** Moving preserves the file but doesn't verify the destination is readable. Copy-verify-delete ensures the file landed intact before removing the source.
- **SHA256 verification:** Catches silent corruption from disk errors or copy failures. The hash is computed on both sides and compared byte-by-byte.
- **Non-recursive by design:** Sorting is typically a one-time cleanup for a flat download folder. Recursive sorting would also move files within already-sorted subfolders, creating confusion.

### Known limitations

- No recursion — subdirectories are not scanned.
- Overwrite prevention — if a file with the same name already exists in the target category folder, the copy fails and the original is left in place.

---

## reindex

Reindexes all files in a directory to sequential numbers (1.jpg, 2.png, 3.pdf, etc.) while preserving extensions. Handles collisions by renaming through temp GUIDs — safe to run on directories with existing numbered files.

```
reindex [directory] [--dry-run]
```

| Arg | Default | Description |
|---|---|---|
| `directory` | current dir | Directory whose files to rename |
| `--dry-run` / `-n` | off | Preview only — no actual renames |

Padding adjusts automatically: 1-9 files -> `01.ext`, 10-99 -> `001.ext`, etc.

No config file.

### How it works

1. Scans the directory for files (non-recursive), sorted alphabetically.
2. Renames each file to a random GUID temp name (avoids collisions with final names).
3. Renames each temp file to the sequential name.
4. If any step fails, temp files are cleaned up and originals are preserved.

### Design decisions

- **Two-phase rename (not rename-in-place):** If file `3.jpg` already exists and we want to rename `zzz.jpg` to `3.jpg`, a direct rename would overwrite. The two-phase approach (original -> guid -> final) avoids all collisions.
- **Alphabetical order:** Provides a deterministic, reproducible sequence. Sorting by date or size would make the order depend on filesystem metadata.

### Known limitations

- No recursion — only files in the specified directory.
- Order is alphabetical by filename — not by date or any other property.

---

## PATH setup

```
setx PATH "%PATH%;D:\DevEnv\custom_utils\shared\bin"
```

One entry covers delcache, dirdiff, catsort, reindex, and any future CLI tools added to `shared/bin/`. Restart your terminal after setting.

---

## Building

```
build.bat
```

Uses Windows' built-in C# compiler (`csc.exe`). No Visual Studio, no NuGet, no dotnet CLI, no install step.

### Prerequisites

- .NET Framework 4.0+ (ships with Windows 8+; available for Windows 7).
- `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` — part of the .NET Framework SDK component of Windows.
- `System.Windows.Forms.dll` — referenced only by `dirdiff.cs`, ships with .NET Framework.

### Build output

```
shared/
├── src/
│   ├── dirdiff.cs           ← source (edit this)
│   ├── delcache.cs          ← source (edit this)
│   ├── catsort.cs           ← source (edit this)
│   └── reindex.cs           ← source (edit this)
├── bin/
│   ├── dirdiff.exe           ← compiled binary (build output)
│   ├── delcache.exe          ← compiled binary (build output)
│   ├── catsort.exe           ← compiled binary (build output)
│   └── reindex.exe           ← compiled binary (build output)
├── conf/
│   ├── cacheDirs.ini        ← delcache configuration
│   └── catsort.ini          ← catsort configuration
├── build.bat
└── README.md
```

### Adding a new tool

1. Write your .cs file in `src/`.
2. Add a compile line to `build.bat` (same pattern as the existing tools).
3. If the tool needs a config file, add it to `conf/` and reference it from code as `../conf/<filename>`.
4. Run `build.bat` — the .exe lands in `bin/` automatically.

### 32-bit systems

For 32-bit Windows, edit `build.bat` to use `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe` (no `64`).

---

## Compatibility

| Aspect | delcache | dirdiff | catsort | reindex |
|---|---|---|---|---|---|---|
| OS | Windows 7+ | Same | Same | Same |
| .NET version | Compiled against .NET Framework 4.0 | Same | Same | Same |
| Dependencies | None | `System.Windows.Forms.dll` | None | None |
| Architecture | x64 (recompile for x86) | Same | Same | Same |
