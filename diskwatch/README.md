# diskwatch — disk health monitor with change detection

Read-only disk health monitor that runs system checks, compares results against previous state, and alerts you when something changes. Silent when healthy.

- **Tool:** `diskwatch/bin/diskwatch.exe`
- **Source:** `diskwatch/src/`
- **Language:** C#, compiled via `csc.exe`
- **Role:** Detection only. Never runs DISM, SFC, chkdsk /f, or SMART self-tests.

---

## Usage

```
diskwatch
```

Runs all checks, prints a verdict to the console, and shows a popup with the summary.

```
diskwatch --remind
```

Shows the same popup from the last run without re-running checks. Useful for Task Scheduler reminders.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Healthy — no changes since last check |
| 1 | Something changed |

---

## What it checks

### fsutil dirty query

Reads the volume dirty bit for each configured drive. A set dirty bit means the filesystem detected corruption and will run chkdsk at next boot.

```
fsutil dirty query C:
```

Parsed for: "NOT Dirty" (clean), "is set" (dirty).

### chkdsk /scan

Performs a read-only scan of the filesystem metadata and reports problems without repairing anything.

```
chkdsk C: /scan
```

Parsed for:
- **Access Denied** — tool not elevated, result unknown.
- **found no problems / No further action** — filesystem is clean.
- **found problems / problems found** — issues detected.
- **KB in bad sectors** — exact count of bad sector reallocations.

### smartctl -x

Runs smartctl with full output for each configured device. Parsed for:
- **Device Model, Serial Number, Firmware Version** — drive identity.
- **SMART overall-health self-assessment test result** — PASSED/FAILED.
- **Percentage Used Endurance Indicator** — remaining endurance (NVMe).
- **Watched attributes** — only the SMART attribute IDs listed in config (raw value tracked per run).

### Windows Event Log

Scans up to 50 recent entries across three logs (Wininit/Operational, System, Application) for disk repair activity. Only flags:
- Wininit-sourced events with InstanceId 262 or 264.
- Any Warning event containing both "disk" and "repair" in the message.

---

## Configuration

### commands.ini

`bin/commands.ini` lists every command to run, grouped by section. Each section is a command category; each line under it is a full command:

```ini
[fsutil]
fsutil dirty query C:
fsutil dirty query D:

[chkdsk]
chkdsk C: /scan
chkdsk D: /scan

; For SMART monitoring:
; [smartctl]
; smartctl -x /dev/sda
```

The first word of each line is the executable, the rest are its arguments. Section names determine how the parser interprets output:
- `[fsutil]` — dirty bit check via fsutil. Drive letter extracted from output.
- `[chkdsk]` — read-only filesystem scan. Drive letter extracted from output.
- `[smartctl]` — SMART data. Device keyed by section+index.

The Event Log reader is built-in (uses .NET EventLog API, not an external command).

### smartAttributes.ini

`bin/smartAttributes.ini` lists SMART attribute IDs to track, one per line:

```ini
5
9
197
198
190
```

Only these IDs are tracked across runs and flagged on change. Lines starting with `#` are comments.

---

## State and change detection

### result.json

Pretty-printed JSON stored in `logs/<timestamp>/result.json` after every run. Contains parsed state for all drives, SMART devices, and the most recent repair timestamp. The previous run's `result.json` is loaded as the comparison baseline — no root-level `logs/result.json` duplicate. Loaded via `JavaScriptSerializer` for deserialization; written with a custom pretty-printer.

Structure:

```json
{
  "timestamp": "2026-07-17T14:30:00.0000000",
  "drives": {
    "C": {
      "dirty": false,
      "filesystem": "clean",
      "badSectorsKb": -1
    }
  },
  "smart": {
    "/dev/sda": {
      "model": "Samsung SSD 990 PRO",
      "serial": "S6P7NJ0W123456",
      "firmware": "5B2QGXA7",
      "health": "PASSED",
      "endurance": 95,
      "attrs": {
        "5": 0,
        "196": 0,
        "197": 0,
        "198": 0
      }
    }
  },
  "lastRepair": null
}
```

### Diff comparison

On every run, the current state is compared against the previous state loaded from the newest timestamped run directory's `result.json`. The following differences trigger a change:

- **Dirty bit** toggled.
- **Filesystem status** changed (clean / issues / unknown).
- **Bad sector count** changed.
- **SMART health** changed (PASSED / FAILED).
- **SMART endurance** changed.
- **Any watched SMART attribute raw value** changed.
- **Repair event timestamp** changed.

If no previous state exists (first run), no changes are reported.

### Raw output logging

Every run saves the raw command output to `logs/<timestamp>/runs/` directory:

```
logs/
├── 2026-07-17T14-30-00/
│   ├── result.json
│   └── runs/
│       ├── fsutil_C.json
│       ├── chkdsk_C.json
│       ├── smartctl_sda.json
│       └── wininit.json
└── 2026-07-10T09-15-00/
    └── ...
```

Each file contains:

```json
{"ExitCode":0,"Output":"..."}
```

Raw output files use inline JSON (compact, no pretty printing).

### Log retention

Only the 5 most recent timestamped run directories are kept. Older runs are pruned automatically on each execution.

---

## Popup

At the end of every normal run (and via `--remind`), a `MessageBox` shows a summary with an OK button. Two tiers:

| Condition | Icon | Title text |
|---|---|---|
| No changes | Information (i) | "Today's run is successful. No issues found." |
| Changes detected | Warning (!) | "Today's run is successful. Some values have changed since the last run." |

Both tiers show the same data: run date, per-drive filesystem status, bad sectors, dirty bit, SMART health, and endurance percentage.

---

## Building

### Prerequisites

- .NET Framework 4.0+ (ships with Windows 8+; available for Windows 7).
- `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` — part of the .NET Framework SDK.
- `System.Web.Extensions.dll`, `System.Windows.Forms.dll` — reference assemblies that ship with .NET Framework.

### Build

```
build.bat
```

Compiles all source files in `src/` to `bin/diskwatch.exe`. No Visual Studio, no dotnet CLI, no NuGet, no install step.

### Build output

```
diskwatch/
├── src/
│   ├── program.cs           ← Main(), --remind, runs commands, Event Log reader, popup
│   ├── commandrunner.cs     ← Process launcher (no timeout)
│   ├── parser.cs            ← State model, parsing, diff, pretty JSON
│   └── popup.cs            ← MessageBox popup with summary
├── bin/
│   ├── diskwatch.exe         ← compiled binary (build output)
│   ├── commands.ini          ← commands to run (edit this)
│   └── smartAttributes.ini   ← SMART attr IDs to track (edit this)
├── logs/                     ← auto-created, holds per-run dirs with result.json + runs/
├── build.bat
└── README.md
```

---

## Design decisions

### Why read-only

diskwatch never repairs, cleans, or modifies the system. It only reads diagnostic data and flags changes. The reasoning: automated repair tools (DISM, SFC, chkdsk /f) can cause more damage than they fix when triggered without human judgment. The tool's job is to tell you something changed — you decide what to do about it.

### Why change detection instead of logging

Logging every run creates noise — most runs are identical. Change detection suppresses the common case (healthy, no changes) and only surfaces deltas. The exit code (0 = clean, 1 = change) makes it scriptable: a Task Scheduler trigger on non-zero exit can send an alert.

### Why raw output is preserved

If the parser misinterprets a tool's output (new Windows version, locale differences), the raw output is still available in `logs/<timestamp>/` for manual inspection without re-running.

### Why smartctl device paths are manual

Auto-detecting drives via WMI or device enumeration adds complexity and can miss devices. A static config list is simpler and more predictable — you know exactly what the tool checks.

### Why no daemon mode

Disk health checks are IO-intensive (fsutil, chkdsk, smartctl all read from disk) and don't need sub-minute granularity. Task Scheduler with a weekly trigger is the right tool for periodic monitoring.

---

## Compatibility

| Aspect | Status |
|---|---|
| OS | Windows 7+ (requires .NET Framework 4.0+) |
| Architecture | x64 (recompile for x86 if needed) |
| File system checks | NTFS, ReFS (via fsutil + chkdsk) |
| SMART | Any drive supported by smartctl |
| Dependencies | None beyond Windows built-ins + optional smartctl |
| Admin required | Yes — for fsutil, chkdsk, and full smartctl data |

---

## Known limitations

- **Admin required** — run as Administrator. Without elevation, fsutil reports "Access Denied", chkdsk
  cannot scan, and smartctl may show limited data.
- **Windows-only** — uses fsutil, chkdsk, and Windows Event Log.
- **smartctl optional but manual** — must be installed and configured in commands.ini if you want SMART
  checks. Not bundled.
- **No drive discovery** — configure every drive in commands.ini and SMART attrs in smartAttributes.ini.
- **No daemon mode** — use Task Scheduler for periodic runs.
- **Event log filtering is heuristic** — the wininit/repair event detection is based on keyword
  matching and may miss or falsely flag events depending on Windows version and language.
