# delpyc — `__pycache__` directory cleaner

- **Tool:** `delpyc` (CLI command, installed via pip)
- **Source:** `delpyc/src/delpyc/core.py`, `delpyc/src/delpyc/__init__.py`
- **Language:** Python 3.8+
- **Dependencies:** `click` (for CLI interface)
- **Role:** Recursively finds and deletes `__pycache__` directories under a given root path. Single-purpose utility — no scan mode, no dry-run, no filter.

---

## Installation

```bash
# From the delpyc/ directory:
pip install .

# Or in editable mode (symlink for development):
pip install -e .
```

This installs the `delpyc` console command. The package uses a `src/` layout — `src/delpyc/` contains the package, `pyproject.toml` at the root configures setuptools. No `setup.py` involved.

### Package structure

```
delpyc/
├── src/
│   └── delpyc/
│       ├── __init__.py    — find_pycache_dirs(), delete_dirs()
│       └── core.py        — CLI entry point (click command)
├── pyproject.toml
└── README.md
```

The `[project.scripts]` entry in `pyproject.toml` maps `delpyc = "delpyc.core:main"`, so the installed command calls `core.py`'s `main()` function.

---

## Usage

```
delpyc [OPTIONS]
```

### Options

| Flag | Shorthand | Default | Description |
|---|---|---|---|
| `--path PATH` | `-p PATH` | `.` (current directory) | Root path to search for `__pycache__` directories. Accepts absolute and relative paths. |
| `--yes` | `-y` | `false` | Skip confirmation prompt. Useful for scripts, aliases, and automated cleanup. |

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success — ran to completion regardless of how many dirs were deleted |
| 0 (no dirs found) | No `__pycache__` found — prints message, exits cleanly |
| 0 (cancelled) | User declined confirmation prompt |

The tool always exits with code 0. There is no error exit code for partial deletion, permission errors, or locked files. This is intentional — `__pycache__` cleanup is best-effort housekeeping, not a critical operation.

### Examples

```bash
# Search and delete in current directory (with prompt)
delpyc

# Search and delete in a specific project
delpyc --path D:\Projects\myapp

# Skip the prompt — useful for scripts and aliases
delpyc --path D:\Projects\myapp --yes

# Combined: current directory, no prompt
delpyc -p . -y
```

---

## How it works

### Finding directories

Walks the directory tree starting from the given root using `os.walk`. At each directory level, checks whether `__pycache__` appears in the `dirnames` list. Collects the full `Path` object for each found directory.

The walk uses `os.walk`'s default top-down behaviour. Directories named `__pycache__` are identified by exact string match — case-sensitive on Linux, case-insensitive on Windows/macOS (handled by the filesystem, not by the tool).

The walk follows all subdirectories. There is no `--max-depth` or `--exclude` filter — every `__pycache__` at every depth is collected.

### Deleting

Each `__pycache__` directory is removed with `shutil.rmtree`. The tool counts successes by checking `dir_path.exists()` after the deletion call:
- If the directory no longer exists → counted as success.
- If it still exists (permission error, locked file, path-too-long) → silently skipped, not counted.

The count returned by `delete_dirs()` reflects only successful deletions. The tool prints "Successfully deleted N directory(ies)" where N is this count. Compare against "Found M directory(ies)" to detect skips.

### Confirmation prompt

Unless `--yes` is passed, the tool:
1. Prints the list of found `__pycache__` directories.
2. Asks "Do you want to delete these directories?" with a default of no.
3. On `y`, proceeds with deletion. On `n`, prints "Operation cancelled" and exits.

The prompt is a standard `click.confirm()` call — accepts `y/n/yes/no`.

---

## Compatibility

| Aspect | Status |
|---|---|
| Python version | 3.8+ |
| OS | Windows, Linux, macOS |
| Dependencies | `click` (installed automatically with pip) |
| Unicode paths | Supported |
| Very long paths | Windows: depends on long-path support (Python 3.8+ on recent Win10/11 with manifest). Linux/macOS: no limit. |
| Network drives | Works if accessible to Python |
| Permission errors | Silently skipped |
| Locked files | Silently skipped |
| Mounted volumes | Traversed if under the search root |

### Installation requirements

- Python 3.8 or later.
- pip (to install from `pyproject.toml`).
- No system-level dependencies — pure Python package.
- Works in virtual environments, conda environments, and system-wide installs.

---

## Architecture

```
CLI (click)
  │
  ▼
core.py::main()
  ├── parse --path, --yes
  └── call delpyc.find_pycache_dirs()
        │
        ▼
  __init__.py::find_pycache_dirs(root)
    └── os.walk(root) → collect Path objects
        │
        ▼
  core.py::main() continued
    ├── print list
    ├── confirm (if not --yes)
    └── call delpyc.delete_dirs()
          │
          ▼
  __init__.py::delete_dirs(dirs)
    └── shutil.rmtree each → count successes
```

The separation between `__init__.py` (core logic) and `core.py` (CLI glue) means the find/delete functions can be imported and used programmatically by other Python code without going through the CLI:

```python
from delpyc import find_pycache_dirs, delete_dirs

dirs = find_pycache_dirs("/some/project")
count = delete_dirs(dirs)
```

---

## Design decisions

### Why `shutil.rmtree` and not `os.remove` per file

`shutil.rmtree` handles nested subdirectories within `__pycache__` (e.g., `.pyc` files organised by Python version subdirectories like `__pycache__/foo.cpython-39.pyc`). Walking and deleting individual files would add complexity for no benefit — the entire `__pycache__` tree is disposable and contains nothing except `.pyc` files and potentially `.pyc`-namespaced subdirectories.

### Why `click` and not `argparse`

The only dependency is `click`, chosen for:
- Zero-boilerplate optional flags with defaults.
- Built-in confirmation prompt (`click.confirm`).
- Automatic `--help` generation.
- Consistent error formatting.

`argparse` would require more code for the same feature set. `click` is a common enough dependency that most Python environments already have it; for those that don't, `pip install delpyc` pulls it automatically.

### Why silent error handling

`__pycache__` deletion is a best-effort housekeeping task, not a critical operation. A locked `.pyc` file (e.g. from an active interpreter) should not abort the entire run or report loudly. Silent skip keeps the tool safe to run as a cron job, post-checkout hook, or CI step.

### Why no dry-run mode

`find . -type d -name __pycache__` already lists the directories — there is no need for the tool to duplicate that. The confirmation prompt serves as the safety check; once confirmed, the tool does what it says. Adding `--dry-run` would add code with no functionality beyond what a shell command already provides.

### Why always exit 0

A partial deletion (some dirs skipped due to permissions) is not an error from the user's perspective — the tool did its best, and the skipped dirs can be dealt with manually. An error exit code would break scripts and CI pipelines that use `set -e` or equivalent. The count comparison ("Found N, deleted M") gives the user enough information to detect issues.

---

## Known limitations

- **No recursive limit** — `__pycache__` directories at any depth are found and deleted. There is no `--max-depth` flag. On very deep trees (e.g. `node_modules` with nested Python packages), this can find more directories than expected.
- **Silent permission errors** — if `shutil.rmtree` fails (permissions, locked file, path too long), the failure is silently caught. Check the count against "Found N directory(ies)" to detect skips.
- **No filesystem-walk timeout** — on very large directory trees on slow storage (network drives), the initial `os.walk` can take noticeable time with no progress indicator.
- **Windows path-length limit** — on older Windows versions without long-path support, paths longer than 260 characters cause `shutil.rmtree` to fail silently. Python 3.8+ with long-path manifest on recent Win10/11 avoids this.
- **Hidden directories not skipped** — a `__pycache__` inside a hidden directory (`.venv`, `.git`, etc.) is still found and deleted. There is no `--exclude` flag.
- **No parallel deletion** — directories are deleted sequentially. For thousands of small dirs, this is fast enough that parallelism adds no value. For a single huge `__pycache__`, `shutil.rmtree` handles it internally.
