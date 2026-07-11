# kdbxWatch — deep-dive documentation

**Tool:** `backgroundWatcher\bin\kdbxWatch.exe`
**Source:** `backgroundWatcher\src\watcher.cs`
**Language:** C#, compiled via `csc.exe /target:winexe`
**Role:** Always-running daemon. Watches source directory for `.kdbx`
changes, creates local timestamped snapshots in `databaseCopies\`.

---

## Configuration reference

File: `backgroundWatcher\bin\config.ini`

| Key | Required | Default | Description |
|---|---|---|---|
| `SourceDir` | yes | — | Directory to watch for `.kdbx` files. Spaces and special characters (e.g. `&`) work fine — no quoting needed, no trailing backslash needed. |
| `DestDir` | no | `snapshots` | Snapshot destination. Relative paths resolve against the `.exe`'s own folder. In production: `D:\Tools\kdbx-backup\databaseCopies` (absolute) or `..\..\databaseCopies` (relative). |
| `HashAlgo` | no | `SHA256` | Hash algorithm. One of `SHA256`, `SHA1`, `MD5`. Controls both change detection and the manifest filename (`SHA256SUMS.txt`, etc.). |
| `DebounceSeconds` | no | `5` | Seconds to wait after the last filesystem event on a file before processing. Absorbs multi-event saves. |
| `LogFile` | no | `logs\watch.log` | Append-only log. In production: `..\logs\watch.log` (shared log folder at project root). |
| `MaxSnapshots` | no | `15` | Maximum snapshot folders to keep in `DestDir`. Oldest deleted first after each new snapshot. `0` = unlimited. |

---

## Core logic walkthrough

### Startup sequence

1. Acquire named mutex `Global\kdbxWatchSingleInstance`. If already
   held → exit immediately, silently (another instance is running).
2. Load `config.ini` from the `.exe`'s own directory.
3. Create `DestDir` and `LogFile` parent directories if missing.
4. Log `Started. Watching: <SourceDir>`.
5. Call `TakeBaselineSnapshot()`.
6. Start `FileSystemWatcher` on `SourceDir`, filter `*.kdbx`.
7. Block forever on `ManualResetEvent` — Task Scheduler ends the process
   on logoff.

### Baseline snapshot

On every startup, the watcher checks whether anything has changed since
the last run *before* deciding to copy:

1. Hash all `.kdbx` files currently in `SourceDir`.
2. Read the most recent snapshot folder's `*SUMS.txt` manifest (found by
   sorting `DestDir` subdirectory names — the `yyyy-MM-dd_HH-mm-ss_`
   prefix makes string sort = chronological sort, no date parsing needed).
3. Compare current hashes against manifest hashes.
4. **If identical:** load hashes into memory, log "Baseline unchanged,
   skipping snapshot", do not copy. This prevents redundant duplicate
   snapshots when the watcher is restarted without any database changes
   having occurred.
5. **If different (or no prior snapshot):** copy all files, write
   manifest, log "Baseline snapshot created".

This design choice — using the manifest written by the *previous* run as
the cross-restart state mechanism — came from an observed bug: before
this check existed, every restart created a new snapshot regardless of
whether anything had changed, because all state was in-memory only and
lost on exit.

### Change detection

`FileSystemWatcher` fires `Changed`, `Created`, and `Renamed` events for
`*.kdbx` files. All three route to `ScheduleDebounce(fileName)`.

**Why `Renamed`?** KeePassXC saves by writing to a temp file then
renaming it into place. Without the `Renamed` handler, saves would be
missed entirely.

### Debounce

Each filename gets its own `System.Threading.Timer`. On each event:
- If a timer already exists for that file → reset it to fire
  `DebounceSeconds` from now (`timer.Change(DebounceMs, Timeout.Infinite)`).
- If no timer exists → create one.

This means rapid saves (multiple filesystem events for one logical save
operation) collapse into a single `OnDebounceElapsed` call, firing once
after the last event settles. Example: save at t=0 and t=3 with a 5s
debounce → timer fires at t=8, not twice.

### Hash comparison and snapshot decision

When `OnDebounceElapsed` fires for a file:

1. Acquire `StateLock`.
2. Remove the timer entry for this file (so future events create a fresh
   timer).
3. Verify the file still exists (could have been deleted in the debounce
   window).
4. Hash the file. If it's still locked (IOException) → reschedule debounce
   rather than failing.
5. Compare against in-memory `LastHashes[fileName]`.
6. **If identical:** log "Hash unchanged, skipping". Do nothing. This
   handles the rare case where a write occurs but content is unchanged
   (e.g. a backup or AV tool touching the file without modifying it).
   KeePassXC itself won't trigger this — it doesn't write the file at all
   unless content changed.
7. **If different:** call `TakeSnapshot(newHash)`.

### Snapshot creation

`TakeSnapshot` (called while holding `StateLock`):

1. Create a new folder: `DestDir\yyyy-MM-dd_HH-mm-ss_<first8charsOfTriggeringHash>\`
2. Copy **all** `.kdbx` files from `SourceDir` into it — not just the
   triggering file. Every snapshot is a complete point-in-time backup of
   the whole set.
3. For each copied file, hash **the copy** (not the original). This is
   deliberate: the manifest proves what actually landed in the snapshot
   folder, not what was in the source at the time of copy. A copy
   corrupted in transit (disk error, AV interference) would produce a
   different hash and be caught.
4. Write `SHA256SUMS.txt` (or `SHA1SUMS.txt` / `MD5SUMS.txt`) with
   `filename: hash` per line, sorted by filename for stable diffs.
5. Update `LastHashes` for every file in the snapshot (not just the
   triggering file) — since the snapshot just captured all of them, the
   baseline for all should reflect the snapshot's state.
6. Call `PruneOldSnapshots()`.

### Pruning

After every snapshot (baseline or change-triggered):

1. List all subdirectories in `DestDir`.
2. Sort by name (= chronological order).
3. If count > `MaxSnapshots`, delete the oldest `count - MaxSnapshots`
   folders.
4. Log each deletion. Catch `IOException` per folder (locked folder is
   skipped and logged, not a fatal error).

`MaxSnapshots=0` skips pruning entirely.

---

## Concurrency model

All mutable state (`LastHashes`, `DebounceTimers`) is guarded by a single
`StateLock` object. `FileSystemWatcher` events fire on background threads;
`Timer` callbacks also fire on threadpool threads. The lock ensures:

- Two filesystem events can't both see "no timer exists" and create
  duplicate timers.
- Two debounce callbacks can't both decide "hash changed" and race to
  create overlapping snapshots before `LastHashes` updates.

`ScheduleDebounce` is called from inside `OnDebounceElapsed` (in the
file-locked reschedule path) while `StateLock` is already held. This
doesn't deadlock because .NET's `Monitor` (used by `lock`) is
re-entrant on the same thread.

Logging uses a separate `LogLock` so timer callbacks logging concurrently
don't interleave partial lines in the log file.

---

## Single-instance enforcement

Two layers:

1. **Named mutex** (`Global\kdbxWatchSingleInstance`) — checked as the
   very first act in `Main()`, before config load or log write. A second
   instance exits before touching anything. The mutex field is kept as a
   static variable to prevent the GC from collecting it and releasing the
   lock while the process runs.
2. **Task Scheduler setting** — "If task is already running → Do not start
   a new instance." Prevents Task Scheduler itself from spawning a second
   process (e.g. on logoff/logon cycles or RDP reconnects).

Both layers are needed: the mutex handles double-clicks and manual
launches; the Task Scheduler setting handles automated re-triggers.

---

## Path resolution

All relative paths in `config.ini` resolve against
`AppDomain.CurrentDomain.BaseDirectory` — the directory containing the
`.exe` file, not the process's current working directory.

This matters because Task Scheduler's working directory is not guaranteed
to match the `.exe` location. Using CWD (`Directory.GetCurrentDirectory()`)
would break silently if "Start in" isn't set in the task definition.
`AppDomain.CurrentDomain.BaseDirectory` is always the `.exe`'s own folder,
regardless of launch context.

---

## Verified behavior

Tested 2026-06-29 / 2026-07-02:

- ✅ Baseline snapshot fires on first run, all `.kdbx` files copied.
- ✅ Restart without changes → "Baseline unchanged, skipping snapshot".
- ✅ Real edit in KeePassXC → debounce → snapshot of all files.
- ✅ Two separate real edits → two separate snapshots.
- ✅ No-op open/close in KeePassXC → no filesystem write, no log entry.
- ✅ Source path with spaces and `&` works unquoted in `config.ini`.
- ✅ Double-clicking `.exe` while already running → second instance exits
  silently, first instance unaffected.
- ✅ `MaxSnapshots` pruning — oldest folders deleted after limit exceeded.
- ⬜ Hash-unchanged skip — not naturally triggered by KeePassXC; only
  fires if another tool writes to the source folder without changing content.

---

## Known edge cases

- **File deleted from source mid-debounce:** `OnDebounceElapsed` checks
  `File.Exists` before hashing and skips with a log entry if missing.
- **File still locked after debounce:** `IOException` on hash → reschedule
  debounce. The file will be retried after another `DebounceSeconds`.
- **Multiple files change near-simultaneously:** Each file has an
  independent debounce timer. Two files' timers firing close together
  produce two separate snapshots (both containing all files), not one
  merged snapshot. This keeps the logic simple at the cost of occasional
  near-duplicate snapshots during multi-file edit sessions.
- **`databaseCopies\` contains non-snapshot subdirectories:** `PruneOldSnapshots`
  sorts and counts all subdirectories, not just ones matching the snapshot
  naming pattern. Don't manually create subfolders inside `databaseCopies\`.