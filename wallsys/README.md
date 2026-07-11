# wallswitch — wallpaper randomizer

- **Tool:** `wallswitch/bin/wallswitch.exe`
- **Source:** `wallswitch/src/wallswitch.cs`
- **Language:** C#, compiled via `csc.exe /target:exe`
- **Role:** On-demand wallpaper randomizer. Picks a random image from `assets/`, applies it as the desktop wallpaper via `SystemParametersInfo`, and persists the selection across reboots via the registry. Tracks a shuffle queue in `state.json` so images are cycled without repeats until the queue is exhausted.

---

## Usage

Run `bin/wallswitch.exe` directly — double-click in Explorer, invoke from a command prompt, or bind to a hotkey via HotKeyKiller / AutoHotkey.

The tool:
1. Scans `assets/` for images.
2. Loads `bin/state.json` for the current shuffle queue.
3. Syncs state with current file listing (detects added/removed images).
4. Pops the front of the queue, sets it as wallpaper, appends it to the shown list.
5. Saves state back to `bin/state.json`.

There is no UI, no output, and no configuration file. The tool is silent on success — failure is also silent (returns without action if `assets/` is missing or empty).

---

## Building

### Prerequisites

- .NET Framework 4.0+ (ships with Windows 8+; available for Windows 7).
- The C# compiler `csc.exe` at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.

### Build

```
build.bat
```

Compiles `src/wallswitch.cs` → `bin/wallswitch.exe`. The tool uses `/target:exe` (console mode), though it produces no console output — the window appears briefly and exits.

The build script uses the system compiler. No Visual Studio, no `dotnet` CLI, no NuGet, no install step. This is the same toolchain used by `kdbx-backup` tools.

### Build output

```
wallswitch/
├── src/
│   └── wallswitch.cs     ← source (edit this)
├── bin/
│   └── wallswitch.exe    ← compiled binary (build output)
├── build.bat
└── assets/                ← your image collection
```

---

## How it works

### Startup sequence

1. `Assembly.GetExecutingAssembly().Location` resolves the `.exe` directory.
2. `assets/` directory scanned for `*.jpg`, `*.jpeg`, `*.png`, `*.bmp`.
3. `bin/state.json` loaded containing two lists: `queue` and `shown`.
4. Image sync — compares current files against known set.
5. If queue is empty, reshuffle all images.
6. Pop first image from queue, append to shown.
7. Save state.
8. Apply wallpaper (registry + SystemParametersInfo).

### Shuffle queue

Maintains two JSON arrays:

- **`queue`** — images scheduled to be shown, in order. The front of the queue is the next wallpaper. After it's shown, it moves to `shown`.
- **`shown`** — images that have already been shown this cycle.

When the queue is empty (all images shown), `shown` and `queue` are merged, Fisher-Yates shuffled, and the cycle starts fresh. This guarantees every image is shown exactly once before any repeats.

The shuffle uses `new Random(Guid.NewGuid().GetHashCode())` for seeding — `Guid.NewGuid()` provides a non-deterministic seed that changes on every reshuffle, avoiding the default `Environment.TickCount`-based seed that can produce identical shuffles if called rapidly.

### Image sync

On each run, the current list of files in `assets/` is compared against the union of `queue` and `shown`:
- **Removed images** (in state but not on disk) are silently dropped from both lists.
- **Added images** (on disk but not in state) are Fisher-Yates shuffled and appended to the end of the queue.

This means you can add or remove images from `assets/` at any time without corrupting the cycle. New images appear eventually (after the current queue drains), and deleted images stop being scheduled without error.

### Wallpaper application

Uses two independent mechanisms:

1. **Registry** — writes to `HKCU\Control Panel\Desktop\Wallpaper`, sets `WallpaperStyle=10` (Fill) and `TileWallpaper=0`. Windows reads this key on login, so the wallpaper survives reboots without any startup helper script.

2. **Win32 API** — calls `SystemParametersInfo(SPI_SETDESKWALLPAPER=20, 0, path, 3)` to apply the change immediately. The `3` flag means `SPIF_UPDATEINIFILE | SPIF_SENDCHANGE` — updates the registry and notises Explorer to redraw.

Both are needed because neither alone covers all scenarios:
- Registry alone requires logoff/logon to take effect.
- SystemParametersInfo alone doesn't persist across reboots.

---

## State file reference

**Location:** `wallswitch/bin/state.json`

```json
{
  "queue": ["assets\\5.png", "assets\\7.png", "assets\\4.png"],
  "shown": ["assets\\1.png", "assets\\3.png"]
}
```

### Schema

| Key | Type | Description |
|---|---|---|
| `queue` | array of strings | Relative paths of images waiting to be shown |
| `shown` | array of strings | Relative paths of images already shown this cycle |

### Path format

All paths are relative to the `.exe` directory, using backslash separators (Windows convention). The state file is hand-serialised — the tool writes JSON manually via `StringBuilder`, not through a JSON parser. Only these two arrays are persisted; all other state is derived.

### Cycle behaviour

```
Start:  queue=[1,2,3,4,5]  shown=[]
Run 1:  pop 1 → shown=[1]       queue=[2,3,4,5]
Run 2:  pop 2 → shown=[1,2]     queue=[3,4,5]
...
Run 5:  pop 5 → shown=[1,2,3,4,5]  queue=[]
        → reshuffle into queue=[3,1,5,2,4]  shown=[]
```

### Manual reset

Delete `bin/state.json` to reset the cycle. The tool recreates it with a fresh shuffle on the next run.

---

## HtK integration

`wallswitch.htk` is a [HotKeyKiller](https://www.ampsoft.net/utilities/HotKeyKiller/) (also known as HotKeyP) configuration file that launches `randomwall.exe` on a hotkey press.

```
hotKeys7 1 1
wallswitch
D:\Tools\myutils\wallswitch\bin\wallswitch.exe

3735553 32 4 0 3 0 -1 0 0
```

- **Line 1:** Protocol version (`hotKeys7`) and enabled flag (`1 1`).
- **Line 2:** Display name in the HotKeyKiller window.
- **Line 3:** Full path to the executable.
- **Line 7:** Numeric hotkey binding — `3735553` decodes to Alt+Ctrl+Win+Shift+W, `32` = `ACTION_RUN`.

To rebind, edit the numeric line in a HotKeyKiller-compatible editor or regenerate via the HotKeyKiller GUI.

---

## Configuration

There is no configuration file. All settings are determined by the file structure or hardcoded in `wallswitch.cs`:

| Setting | How to change | Default |
|---|---|---|
| Image pool | Add/remove files in `assets/` | Empty |
| Supported formats | Edit `exts` array in source and recompile | `jpg`, `jpeg`, `png`, `bmp` |
| Wallpaper style | Edit registry or change `WallpaperStyle` in source | Fill (10) |
| Tiling | Edit `TileWallpaper` in source | Off (0) |
| Queue persistence | Delete `state.json` to reset | Auto-managed |
| Shuffle seed | Hardcoded `Guid.NewGuid().GetHashCode()` | Random per reshuffle |

To change wallpaper style from Fill to Fit, Center, Stretch, or Tile:

| Style | `WallpaperStyle` | `TileWallpaper` |
|---|---|---|
| Fill | 10 | 0 |
| Fit | 6 | 0 |
| Stretch | 2 | 0 |
| Center | 0 | 0 |
| Tile | 0 | 1 |
| Span (multi-monitor) | 22 | 0 |

Edit the values in `src/wallswitch.cs` and recompile with `build.bat`.

---

## Compatibility

| Aspect | Status |
|---|---|
| OS | Windows 7+ (requires .NET Framework 4.0+) |
| Architecture | x64 (`Framework64\csc.exe`; recompile for x86 if needed) |
| Image formats | JPEG, PNG, BMP (native Windows support); WebP with extension |
| Multi-monitor | Single wallpaper spans all monitors (Fill/Span style) |
| .NET version | Compiled against .NET Framework 4.0 (csc.exe v4.0.30319) |
| Dependencies | None beyond Windows built-ins |
| Hotkey launcher | Tested with HotKeyKiller 7.x |

### Windows version notes

- **Windows 10/11:** Full support. Registry and `SystemParametersInfo` both work as expected.
- **Windows 8/8.1:** Full support.
- **Windows 7:** Requires .NET Framework 4.0+ (may need manual install). `SystemParametersInfo` works.

### .NET Framework

The tool targets .NET Framework 4.0, which is included in Windows 8+ and available as an update for Windows 7/XP. The compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` is installed as part of the .NET Framework SDK component of Windows.

For 32-bit systems, use `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe` instead. Edit `build.bat` to point to the correct path.

---

## Design decisions

### Why C# and not PowerShell or Python

Wallpaper setting requires P/Invoke (`SystemParametersInfo`) and registry access. C# provides both in the standard library without any runtime dependency beyond what Windows already ships (.NET Framework). The resulting `.exe` shows up by name in Task Manager, has no console window, and launches in under 100 ms.

### Why relative paths in state

Storing paths relative to the `.exe` directory makes the entire `wallswitch/` folder portable — you can move it to another drive or machine and the state file remains valid as long as `assets/` is present alongside `bin/`.

### Why a shuffle queue instead of simple random

Simple random selection can repeat the same image multiple times before showing others. The shuffle queue guarantees every image is shown once before any repeats, which is the expected behaviour for a wallpaper rotator.

### Why two wallpaper-setting mechanisms

- Registry write alone requires logoff/logon to take effect.
- `SystemParametersInfo` alone doesn't persist across reboots.

Together they cover both scenarios: immediate visual change + persistence without a startup helper.

### Why silent operation

The tool is meant to be triggered by a hotkey — you press a key, the wallpaper changes, you continue working. Console output or message boxes would defeat the purpose. Failure cases (missing assets, empty folder) are silently ignored because there is no user-facing UI to report them to.

---

## Comparison to the old version (`archive/wallsys_old/`)

| Old (`nature.cs` / `tech.cs`) | New (`wallswitch.cs`) |
|---|---|
| Two separate binaries for nature/tech | Single binary, single `assets/` folder |
| No shuffle — pure random | Shuffle queue — no repeats until cycle exhausted |
| No state file — no cycle tracking | `state.json` persists across runs |
| No image-add detection | New images detected and merged into queue |
| No build script | `build.bat` for easy recompilation |
| Source at root | Source in `src/`, binary in `bin/` |

The old nature/tech split was replaced by a single tool because the distinction is purely about which images are in the folder — a single tool with a single `assets/` directory is simpler to maintain.

---

## Known limitations

- **No logging** — the tool produces no output on success or failure. Diagnosing issues requires attaching a debugger or checking `state.json` manually.
- **Single-monitor only** — `SystemParametersInfo` sets the wallpaper for the entire virtual desktop. On multi-monitor setups, the image spans all monitors with the chosen style. For per-monitor wallpapers, use `IMultiMonitorDocking` API or a different tool.
- **No image format conversion** — Windows must natively support the file format. `jpg`, `jpeg`, `png`, and `bmp` all work. `webp` requires the [WebP Codec for Windows](https://www.microsoft.com/store/productId/9PG2DK2V6M7P).
- **No exclusion paths** — all images in `assets/` are included. There is no way to exclude individual files without removing them from the folder.
- **No scheduling** — the tool is event-driven (hotkey), not time-driven. For automatic periodic rotation, use Task Scheduler or a separate daemon.
- **State file can get stale** — if `assets/` is modified on a system without `.exe` write access (e.g. read-only mount), `state.json` cannot be updated and the cycle may repeat or skip.
