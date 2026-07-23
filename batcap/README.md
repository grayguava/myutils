# batcap — battery capacity logger

Logs battery stats via WMI to `logs/batcap.log`. Runs silently — built as a Windows application (`/target:winexe`), no console window. Designed for Task Scheduler.

- **Tool:** `batcap/bin/batcap.exe`
- **Source:** `batcap/src/program.cs`
- **Language:** C#, compiled via `csc.exe`

## Usage

Compiled as a Windows application (`/target:winexe`) — no console window. Run via Task Scheduler (daily or weekly trigger) or double-click to log silently.

Appends one line to `logs/batcap.log`:

```
[2026-07-23 15:01:15] Design=44021mWh Full=44494mWh Remaining=39555mWh Voltage=11794mV ChargeRate=0mW DischargeRate=12949mW Cycles=0 Charging=False
```

### Fields

| Field | Source | Unit |
|---|---|---|
| Design | `bin/.conf` (44021 default) | mWh |
| Full | BatteryFullChargedCapacity WMI | mWh |
| Remaining | BatteryStatus WMI | mWh |
| Voltage | BatteryStatus WMI | mV |
| ChargeRate | BatteryStatus WMI | mW |
| DischargeRate | BatteryStatus WMI | mW |
| Cycles | BatteryCycleCount WMI | count |
| Charging | BatteryStatus WMI | bool |

## Building

```
build.bat
```

Uses Windows' built-in C# compiler. No Visual Studio, no NuGet.

## Configuration

`bin/.conf` — one-line file with the battery's design capacity in mWh. Default:

```ini
# Design capacity in mWh
44021
```

Edit this if your battery has a different design capacity. Lines starting with `#` are comments.

## Why not powercfg /batteryreport
`powercfg /batteryreport` generates a battery report at `%USERPROFILE%\battery-report.html`, but on my machine (EliteBook 840 G2) the report shows `-` for every battery capacity field — design capacity, full charge capacity, cycle count, all blank. The Windows "Battery capacity history" and "Battery life estimates" sections are entirely empty.

`powercfg /energy` was also tested as an alternative and does run a valid 60-second trace, but it pulls Design Capacity from the same broken source — its report showed Design and Full Charge as identical values, meaning it's silently falling back to Full Charge for both rather than reading a true separate design figure.

The root cause is the `BatteryStaticData` WMI class (which exposes `DesignedCapacity`) intermittently returning a `Generic failure` error on this hardware — likely a driver/ACPI quirk where that specific counter isn't reliably surfaced, even though every other battery counter works fine. Since design capacity never changes, this tool sidesteps the broken class entirely: the true nameplate value (confirmed once via the original working `powercfg /batteryreport`) is hardcoded in `bin/.conf` instead of queried each run. `BatteryFullChargedCapacity`, `BatteryStatus`, and `BatteryCycleCount` all return valid data reliably, so this tool polls those directly each run and appends to a running log for manual trend tracking over time — the historical view `powercfg` was supposed to provide but doesn't on this machine.

## Build output

```
batcap/
├── src/
│   └── program.cs            ← source
├── bin/
│   ├── batcap.exe           ← compiled binary
│   └── .conf                 ← design capacity (edit this)
├── logs/
│   └── batcap.log           ← append-only log file
├── build.bat
└── README.md
```

Requires .NET Framework 4.0+ and `System.Management.dll` (ships with Windows).
