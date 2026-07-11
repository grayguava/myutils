# kdbx-backup — KeePass `.kdbx` backup pipeline

- **Tools:** `kdbxWatch.exe` (always-on daemon) + `kdbxPushToRemote.exe` (scheduled runner)
- **Source:** `backgroundWatcher/src/watcher.cs`, `pushToRemote/src/push.cs`
- **Language:** C#, compiled via `csc.exe /target:winexe`
- **Dependencies:** rclone (for cloud push)
- **Role:** Two-tool pipeline that snapshots KeePass databases on file change and pushes them to three cloud providers.

---

## How it works

```
KeePassXC saves a .kdbx file
        ↓
kdbxWatch.exe (always running, event-driven)
  detects the change via FileSystemWatcher
  verifies it's a real change via SHA256 hash
  copies ALL .kdbx files into a new timestamped snapshot folder
        ↓
databaseCopies/
  YYYY-MM-DD_HH-mm-ss_<hash>/
    *.kdbx + SHA256SUMS.txt
        ↓
kdbxPushToRemote.exe (scheduled, run-to-completion)
  runs rclone copy to Google, Dropbox, Koofr sequentially
  exits when done
        ↓
3 cloud remotes — append-only (nothing ever deleted)
```

The two tools are independent — neither depends on the other — but they form a deliberate pipeline.

---

## Building

### Prerequisites

- .NET Framework 4.0+ (ships with Windows 8+).
- The C# compiler `csc.exe` at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.

### Build

```
build.bat
```

A single `build.bat` at the root compiles both `.cs` files into their respective `bin/` directories. No Visual Studio, no `dotnet` CLI, no NuGet, no install step.

Both use `/target:winexe` — no console window at any point. The process appears in Task Manager under its own name with no taskbar entry.

### Build output

```
kdbx-backup/
├── build.bat                    ← builds both tools
├── backgroundWatcher/
│   ├── src/watcher.cs          ← source (edit this)
│   ├── bin/kdbxWatch.exe       ← compiled binary
│   └── bin/config.ini          ← config
├── pushToRemote/
│   ├── src/push.cs             ← source (edit this)
│   ├── bin/kdbxPushToRemote.exe
│   └── bin/config.ini
├── databaseCopies/              ← local snapshot destination (auto-created)
├── logs/                        ← shared log folder (auto-created)
└── README.md
```

---

## Setup: Task Scheduler

### kdbxWatch (always-on daemon)

- **Trigger:** At log on
- **Action:** Start `backgroundWatcher\bin\kdbxWatch.exe`
- **Start in:** `<kdbx-backup>\backgroundWatcher\bin\`
- **Settings:** If task is already running → Do not start a new instance

The single-instance constraint in Task Scheduler is a second layer of defence — the `.exe` also holds a named mutex (`Global\kdbxWatchSingleInstance`) that causes any duplicate launch to exit immediately.

### kdbxPushToRemote (scheduled, run-to-completion)

- **Trigger:** On a schedule (e.g. hourly, or at logon + repeat)
- **Action:** Start `pushToRemote\bin\kdbxPushToRemote.exe`
- **Start in:** `<kdbx-backup>\pushToRemote\bin\`
- **Settings:** If task is already running → Do not start a new instance

Unlike the watcher, this tool exits on its own when done. The "do not start a new instance" setting prevents a slow upload run from stacking with the next scheduled trigger.

### Config files

Both tools use flat `key=value` INI files in their `bin/` directories — no TOML, JSON, or XML parser. Paths are relative to the `.exe` location (`AppDomain.CurrentDomain.BaseDirectory`), not CWD, so they work reliably regardless of how the process is launched.

---

## Design decisions

### Why local snapshots first, cloud second

Cloud sync tools (rclone, OneDrive, Google Drive desktop) operate on their own schedule and can introduce conflicts, partial-write races, or version history that's harder to inspect than a plain timestamped folder. By keeping the authoritative local snapshot pipeline entirely separate from cloud upload, the two concerns don't interfere: the watcher never waits on a network, and the cloud push never races with an active save.

### Why three providers

Genuine redundancy requires providers that fail independently. Three providers (Google Drive, Dropbox, Koofr) were chosen because any single provider can have outages, policy changes, or account suspension. Two is better than one; three covers a simultaneous outage of any single provider without losing access.

### Why rclone copy and not rclone sync

`rclone sync` mirrors source to destination and **deletes from the remote anything not present locally**. Since `kdbxWatch` enforces a `MaxSnapshots` limit locally (default 15), old snapshot folders get pruned from `databaseCopies/` over time. If `rclone sync` were used, it would delete those same folders from the cloud — defeating the purpose of cloud backup, which is to retain history even after local pruning. `rclone copy` only uploads what's missing on the remote and never deletes anything. The cloud becomes an append-only archive of every snapshot that ever existed locally.

### The bootstrap problem

The cloud accounts (Google, Dropbox, Koofr) originally had their login credentials stored inside the `.kdbx` databases being backed up. This creates a circular dependency: losing local access to KeePass locks you out of the cloud accounts holding the KeePass backups.

The resolution relies on the structure of the database set:

- **5 databases total.** One master database, four subordinate databases.
- **Master database** is protected by a memorised password (unchanged for years, held by one person). It contains the long unmemorable keys for the four subordinate databases.
- **Subordinate databases** contain working credentials including cloud account logins.

The master database password being memorised — not stored anywhere — is the actual root of trust. It has no physical form to lose, no device dependency, and isn't discoverable by someone accessing a safe or USB.

Two independent recovery paths exist after total local loss:

1. **Physical copies** — the master database on USB drives kept separately. Staleness doesn't matter: the master database never changes (password and sub-db keys are fixed), so any old copy is as good as the latest one.
2. **Identity-based recovery** — one of the three cloud providers (Google) is recoverable via phone-based OTP across two independent phone numbers, without needing the stored password at all.

The four subordinate databases are **not** kept on USB because they _do_ change and a stale copy could be misleading.

### The irreducible single point of failure

Every backup strategy has a stopping point. The residual risk here is the simultaneous failure of: both USB copies, all three cloud providers, and all three phone numbers tied to Google account recovery. This compound scenario is accepted as the stopping point.

### Key principles

- **No unnecessary dependencies.** Only Windows built-ins and rclone (which is already required for cloud sync regardless).
- **No databases, no dashboards.** Plain text append-only logs.
- **Config via flat files.** `key=value` INI, no parser library needed.
- **Paths anchored to `.exe` location**, not CWD.
- **Source files never modified.** The watcher only reads and copies. rclone only uploads. Neither tool touches the original `.kdbx` files.
- **Independent runnability.** Either tool can be run, stopped, or rebuilt without affecting the other.

---

## Compatibility

| Aspect | Status |
|---|---|
| OS | Windows 7+ (requires .NET Framework 4.0+) |
| Architecture | x64 (recompile for x86 if needed) |
| Dependencies | rclone on PATH (for push tool only) |
| Cloud providers | Google Drive, Dropbox, Koofr (via rclone) |
| Log format | Plain text, append-only |
| Config format | Flat INI (`key=value`) |

---

## Known limitations

- **Windows-only** — compiled with `csc.exe` against .NET Framework 4.0. Both tools use Windows-specific APIs (`FileSystemWatcher`, named mutexes, `winexe`).
- **rclone must be on PATH** for `kdbxPushToRemote`. No fallback if rclone is missing or misconfigured.
- **No web UI or dashboard** — all monitoring is via log files. No alerting on push failure.
- **MaxSnapshots is local-only** — the cloud archive grows unbounded. No remote retention policy is enforced.
- **kdbxWatch can miss rapid saves** — debouncing prevents storming, but very fast successive saves (under the debounce window) are coalesced into one snapshot.
- **No encryption at rest in logs** — log files contain filenames and timestamps but no credentials. Review access controls if logs are stored on shared volumes.
