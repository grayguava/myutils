# kdbxPushToRemote — deep-dive documentation

**Tool:** `bin\kdbxPushToRemote.exe`
**Source:** `src\push.cs`
**Language:** C#, compiled via `csc.exe /target:winexe`
**Role:** Run-to-completion. Pushes `databaseCopies\` to all configured
rclone remotes sequentially. Started by Task Scheduler on a schedule,
exits when done.

---

## Configuration reference

File: `bin\.conf`

| Key | Required | Default | Description |
|---|---|---|---|
| `PushSourceDir` | no | `..\databaseCopies` | Local folder to push. Relative paths resolve against the `.exe`'s own folder. |
| `RclonePath` | no | `rclone` | *(deprecated — ignored, hardcoded to `rclone`)* |
| `Remotes` | no | — | Comma-separated rclone remote names. Must match names in `rclone config` exactly (case-sensitive). |
| `RemotePath` | no | `kdbx-backup` | Folder name to create inside each remote. |
| `PushLogFile` | no | `..\logs\rclone.log` | Append-only log. Shared with the watcher's log folder at `kdbx-backup\logs\`. |

---

## Why rclone copy and not rclone sync

This is the most important design decision in this tool and worth being
explicit about.

`rclone sync` mirrors the source to the destination *exactly*, including
**deleting from the remote anything not present locally**. Since
`kdbxWatch` enforces `MaxSnapshots=15` locally, old snapshot folders get
pruned from `databaseCopies\` over time. If `rclone sync` were used:

1. kdbxWatch creates snapshot #16 → prunes snapshot #1 locally.
2. kdbxPushToRemote runs → sees snapshot #1 missing locally → deletes it
   from all three cloud remotes.

Result: the cloud retains exactly the same 15 snapshots as local disk,
making it worthless as a long-term archive. The entire point of cloud
backup is to retain history beyond what's kept locally.

`rclone copy` only uploads what's missing on the remote. It never
deletes. The cloud becomes an append-only archive — every snapshot ever
created locally exists on the remote forever, regardless of local pruning.

**Summary:**
- `rclone copy` → cloud grows indefinitely, local stays capped. ✅
- `rclone sync` → cloud mirrors local, pruning applies to both. ❌

---

## Why three providers

Three providers were chosen for genuine redundancy — meaning they fail
independently of each other. The three in use:

| Remote name | Provider | Type | Notes |
|---|---|---|---|
| `Google` | Google Drive | OAuth / drive | Already configured; most mature rclone backend |
| `Dropbox` | Dropbox | OAuth / dropbox | Already configured |
| `Koofr` | Koofr | koofr | Already configured |

All three were pre-existing rclone remotes, so no new OAuth setup was
needed. The `kdbx-backup` folder is created inside each remote on first
push.

Free tiers on all three are sufficient for `.kdbx` files — even with
unlimited cloud retention (no pruning on remotes), the total size of
thousands of snapshots of five small databases remains well within any
free tier.

---

## How the push works

Each remote is a separate `rclone copy` child process, launched via
`System.Diagnostics.Process`. stdout and stderr are captured
asynchronously (to avoid deadlocks if both buffers fill simultaneously)
and written to the log file after the process exits.

Sequential, not parallel — one remote at a time. Rationale: simplicity
over speed. A failed remote doesn't block the others; if Google fails,
Dropbox and Koofr still run. Upload time for small `.kdbx` files is
negligible, so parallelism buys nothing meaningful.

rclone is passed `--stats-one-line` to produce compact output suitable
for log files rather than a multi-line progress dashboard.

Exit codes:
- `0` → logged as `<remote>: OK`
- Non-zero → logged as `<remote>: FAILED (exit <code>)`
- Exception launching rclone → logged as `<remote>: ERROR launching rclone — <message>`

A failed remote does **not** stop execution — the loop continues to the
next remote regardless.

---

## Process lifecycle

Unlike `kdbxWatch`, this tool is **not** a daemon. It:

1. Loads config.
2. Validates `SourceDir` exists and `Remotes` is non-empty.
3. Loops over remotes, running one `rclone copy` per remote.
4. Logs completion.
5. Exits.

No `ManualResetEvent`, no `FileSystemWatcher`, no mutex. Task Scheduler
manages the schedule and handles the "don't stack instances" concern via
the "If task is already running → Do not start a new instance" setting.

---

## Task Scheduler setup

- **Trigger:** Schedule (e.g. hourly, or daily)
- **Action:** Start `bin\kdbxPushToRemote.exe`
- **Start in:** `D:\Tools\kdbx-backup\bin\`
- **Settings → If task is already running:** Do not start a new instance

The "Start in" field matters: without it, relative paths in `.conf`
(`PushSourceDir=..\databaseCopies`, `PushLogFile=..\logs\rclone.log`) would
resolve against Task Scheduler's own working directory rather than the
`.exe`'s folder. Setting "Start in" ensures they resolve correctly.

Alternatively, use absolute paths in `.conf` to make this
Task Scheduler dependency disappear entirely.

---

## Path resolution

Same pattern as `kdbxWatch`: relative paths in `.conf` are resolved
via `Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relPath))`.

`AppDomain.CurrentDomain.BaseDirectory` = the folder containing
`kdbxPushToRemote.exe`, not the process CWD. See `kdbxWatch.md` for the
full reasoning — same principle applies here.

---

## Log format

Entries in `logs\rclone.log`:

```
2026-07-02 15:00:01  --- Push started ---
2026-07-02 15:00:01  Syncing to Google:kdbx-backup ...
2026-07-02 15:00:08    [stderr] <rclone stats output>
2026-07-02 15:00:08    Google: OK
2026-07-02 15:00:08  Syncing to Dropbox:kdbx-backup ...
2026-07-02 15:00:14    Dropbox: OK
2026-07-02 15:00:14  Syncing to Koofr:kdbx-backup ...
2026-07-02 15:00:19    Koofr: OK
2026-07-02 15:00:19  --- Push complete ---
```

rclone's own output (stats, errors) appears as `[stdout]` / `[stderr]`
lines indented under the remote name. A failed upload shows rclone's
error message in `[stderr]` followed by `FAILED (exit 1)`.

---

## Adding or removing a remote

Edit `Remotes=` in `.conf`. No recompile needed. Example — add a
fourth remote:

```ini
Remotes=Google,Dropbox,Koofr,Backblaze
```

The new remote must already exist in `rclone config`. The tool will
create `kdbx-backup\` inside it on first push.

To temporarily disable a remote without removing it from rclone config,
just remove it from the `Remotes=` line.

---

## Known limitations

- **No retry logic.** If a remote fails, it's logged and skipped. The
  next scheduled run will retry (rclone copy picks up where it left off —
  already-uploaded folders are skipped, only missing ones are uploaded).
- **No network check before starting.** If the machine has no internet,
  all three remotes fail and log errors. Not a problem in practice — the
  next scheduled run will succeed when connectivity is restored, and
  rclone copy is idempotent.
- **rclone must be on PATH** — the executable path is hardcoded to `rclone`. If rclone is installed elsewhere, add its directory to PATH or use a symlink.
