# =============================================================================
# collect-events.ps1
# Collects Critical, Error, and Warning events from System and Application
# event logs for the past 7 days. Filters to high-signal providers only.
#
# Output: events.txt in the dated snapshot directory.
# =============================================================================

. "$PSScriptRoot\helpers\common.ps1"

Ensure-SnapshotDir

$OutFile  = Join-Path $global:SnapshotDir "events.txt"
$DaysBack = 7
$Since    = (Get-Date).AddDays(-$DaysBack)

# ---------------------------------------------------------------------------
# High-signal providers — everything else is noise
# ---------------------------------------------------------------------------
$FocusProviders = @(
    "Microsoft-Windows-Kernel-Power",
    "Microsoft-Windows-Kernel-Disk",
    "Microsoft-Windows-Ntfs",
    "Microsoft-Windows-WHEA-Logger",
    "volmgr",
    "disk",
    "Microsoft-Windows-BugCheck",
    "Service Control Manager",
    "Microsoft-Windows-DriverFrameworks-UserMode",
    "Microsoft-Windows-WindowsUpdateClient",
    "Microsoft-Windows-WER-SystemErrorReporting",
    "Application Error",
    "Application Hang",
    "Windows Error Reporting"
)

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Set-Content -Path $OutFile -Value "PCHealth — Event Log Report"
Add-Content -Path $OutFile -Value "Collected : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Content -Path $OutFile -Value "Host      : $env:COMPUTERNAME"
Add-Content -Path $OutFile -Value "Period    : Last $DaysBack days (since $($Since.ToString('yyyy-MM-dd HH:mm')))"
Add-Content -Path $OutFile -Value ""

# ---------------------------------------------------------------------------
# Helper: query one log, filter levels and providers
# ---------------------------------------------------------------------------
function Get-FilteredEvents {
    param(
        [string]$LogName,
        [string[]]$Levels,     # "Critical","Error","Warning"
        [datetime]$After
    )

    $levelMap = @{ "Critical" = 1; "Error" = 2; "Warning" = 3 }
    $levelNums = $Levels | ForEach-Object { $levelMap[$_] }

    try {
        Get-WinEvent -LogName $LogName -ErrorAction Stop |
            Where-Object {
                $_.TimeCreated -ge $After -and
                $levelNums -contains $_.Level -and
                (
                    # Include if from a focus provider
                    ($FocusProviders | Where-Object { $_.ToLower() -eq $_.ProviderName.ToLower() }).Count -gt 0 -or
                    # Always include Critical regardless of provider
                    $_.Level -eq 1
                )
            } |
            Sort-Object TimeCreated -Descending
    } catch {
        Write-Warning "Could not read log '$LogName': $_"
        return @()
    }
}

# ---------------------------------------------------------------------------
# Helper: format and write events to file
# ---------------------------------------------------------------------------
function Write-Events {
    param(
        [object[]]$Events,
        [string]$SectionTitle,
        [string]$FilePath
    )

    Write-Section -Title $SectionTitle -FilePath $FilePath

    if (-not $Events -or $Events.Count -eq 0) {
        Add-Content -Path $FilePath -Value "  None found."
        return
    }

    Add-Content -Path $FilePath -Value ("  Total: {0} events" -f $Events.Count)
    Add-Content -Path $FilePath -Value ""

    foreach ($evt in $Events) {
        $level   = switch ($evt.Level) { 1{"CRITICAL"} 2{"ERROR"} 3{"WARNING"} default{"INFO"} }
        $time    = $evt.TimeCreated.ToString("yyyy-MM-dd HH:mm:ss")
        $source  = $evt.ProviderName
        $id      = $evt.Id
        $message = ($evt.Message -replace "`r`n|`n", " ").Trim()
        if ($message.Length -gt 300) { $message = $message.Substring(0, 300) + "..." }

        Add-Content -Path $FilePath -Value "  [$level] $time  |  ID: $id  |  $source"
        Add-Content -Path $FilePath -Value "  $message"
        Add-Content -Path $FilePath -Value ""
    }
}

# ---------------------------------------------------------------------------
# Collect: System log — Critical and Errors
# ---------------------------------------------------------------------------
$sysCritErr = Get-FilteredEvents -LogName "System" -Levels @("Critical","Error") -After $Since
Write-Events -Events $sysCritErr -SectionTitle "SYSTEM LOG — CRITICAL & ERRORS (last $DaysBack days)" -FilePath $OutFile

# ---------------------------------------------------------------------------
# Collect: System log — Warnings (separate, lower priority)
# ---------------------------------------------------------------------------
$sysWarn = Get-FilteredEvents -LogName "System" -Levels @("Warning") -After $Since
Write-Events -Events $sysWarn -SectionTitle "SYSTEM LOG — WARNINGS (last $DaysBack days)" -FilePath $OutFile

# ---------------------------------------------------------------------------
# Collect: Application log — Critical and Errors
# ---------------------------------------------------------------------------
$appCritErr = Get-FilteredEvents -LogName "Application" -Levels @("Critical","Error") -After $Since
Write-Events -Events $appCritErr -SectionTitle "APPLICATION LOG — CRITICAL & ERRORS (last $DaysBack days)" -FilePath $OutFile

# ---------------------------------------------------------------------------
# Collect: Application log — Warnings
# ---------------------------------------------------------------------------
$appWarn = Get-FilteredEvents -LogName "Application" -Levels @("Warning") -After $Since
Write-Events -Events $appWarn -SectionTitle "APPLICATION LOG — WARNINGS (last $DaysBack days)" -FilePath $OutFile

# ---------------------------------------------------------------------------
# Event count summary at top — go back and prepend it
# ---------------------------------------------------------------------------
$summary = @"

EVENT COUNT SUMMARY
-------------------
  System   Critical+Error : $($sysCritErr.Count)
  System   Warnings       : $($sysWarn.Count)
  Application Critical+Error : $($appCritErr.Count)
  Application Warnings    : $($appWarn.Count)
  Total                   : $(($sysCritErr.Count + $sysWarn.Count + $appCritErr.Count + $appWarn.Count))

"@

# Prepend summary to file
$existing = Get-Content -Path $OutFile -Raw
Set-Content -Path $OutFile -Value ($summary + $existing)

Write-Host "collect-events.ps1 complete -> $OutFile"
