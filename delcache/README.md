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

---

## Configuration

Edit `bin\cacheDirs.ini` to add or remove target directory names. One name per line, `#` for comments.

```ini
__pycache__
node_modules
.bazel
.cache
.vs
```

If the file is missing or empty, defaults to `__pycache__` and `node_modules`.

---

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

```
build.bat
```

Compiles `src\delcache.cs` → `bin\delcache.exe`. No dependencies beyond the built-in .NET Framework compiler.
