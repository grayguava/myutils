# =============================================================================
# collect-integrity.ps1
# Runs Windows system integrity checks: DISM ScanHealth and SFC /verifyonly.
# These are read-only scans — nothing is repaired automatically.
#
# Requires elevation (Admin).
# Note: DISM ScanHealth contacts Windows Update to verify component store.
#       Run time: 2-10 minutes depending on system state.
#
# Output: integrity.txt in the dated snapshot directory.
# =============================================================================

. "$PSScriptRoot\helpers\common.ps1"

Ensure-SnapshotDir

$OutFile = Join-Path $global:SnapshotDir "integrity.txt"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Set-Content -Path $OutFile -Value "PCHealth — System Integrity Report"
Add-Content -Path $OutFile -Value "Collected : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Content -Path $OutFile -Value "Host      : $env:COMPUTERNAME"
Add-Content -Path $OutFile -Value ""
Add-Content -Path $OutFile -Value "IMPORTANT: These are scan-only operations. Nothing is repaired."
Add-Content -Path $OutFile -Value "           Only run repairs manually if symptoms are present."
Add-Content -Path $OutFile -Value ""

# ---------------------------------------------------------------------------
# Elevation check
# ---------------------------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdmin) {
    Add-Content -Path $OutFile -Value "ERROR: Script must run as Administrator for DISM and SFC."
    Write-Warning "collect-integrity.ps1 requires elevation. Skipping."
    exit 1
}

# ---------------------------------------------------------------------------
# DISM /ScanHealth
# Checks the component store (WinSxS) for corruption.
# Does NOT repair — use /RestoreHealth only if ScanHealth reports issues.
# ---------------------------------------------------------------------------
Write-Section -Title "DISM — COMPONENT STORE SCAN (/ScanHealth)" -FilePath $OutFile

Add-Content -Path $OutFile -Value "  Running: DISM /Online /Cleanup-Image /ScanHealth"
Add-Content -Path $OutFile -Value "  This may take several minutes..."
Add-Content -Path $OutFile -Value ""

$dismStart = Get-Date

try {
    $dismOutput = DISM /Online /Cleanup-Image /ScanHealth 2>&1
    $dismEnd    = Get-Date
    $dismElapsed = [math]::Round(($dismEnd - $dismStart).TotalSeconds, 0)

    Add-Content -Path $OutFile -Value ("  Completed in: {0} seconds" -f $dismElapsed)
    Add-Content -Path $OutFile -Value ""

    $dismOutput | ForEach-Object { Add-Content -Path $OutFile -Value "  $_" }

    # Extract result line for summary
    $dismResult = $dismOutput | Where-Object { $_ -match "No component store corruption|component store is repairable|operation completed" }
    if ($dismResult) {
        Add-Content -Path $OutFile -Value ""
        Add-Content -Path $OutFile -Value "  RESULT: $dismResult"
    }

    # Flag if issues found
    if ($dismOutput -match "corruption" -and $dismOutput -notmatch "No component store corruption") {
        Add-Content -Path $OutFile -Value ""
        Add-Content -Path $OutFile -Value "  *** ATTENTION: DISM detected component store issues. ***"
        Add-Content -Path $OutFile -Value "  *** Consider running: DISM /Online /Cleanup-Image /RestoreHealth ***"
    }

} catch {
    Add-Content -Path $OutFile -Value "ERROR running DISM: $_"
}

# ---------------------------------------------------------------------------
# SFC /verifyonly
# Scans protected system files for integrity violations.
# Does NOT repair — use sfc /scannow only if verifyonly reports violations.
# ---------------------------------------------------------------------------
Write-Section -Title "SFC — SYSTEM FILE CHECK (/verifyonly)" -FilePath $OutFile

Add-Content -Path $OutFile -Value "  Running: sfc /verifyonly"
Add-Content -Path $OutFile -Value "  This may take several minutes..."
Add-Content -Path $OutFile -Value ""

$sfcStart = Get-Date

try {
    $sfcOutput = sfc /verifyonly 2>&1
    $sfcEnd    = Get-Date
    $sfcElapsed = [math]::Round(($sfcEnd - $sfcStart).TotalSeconds, 0)

    Add-Content -Path $OutFile -Value ("  Completed in: {0} seconds" -f $sfcElapsed)
    Add-Content -Path $OutFile -Value ""

    # SFC outputs Unicode — decode properly
    $sfcOutput | ForEach-Object {
        $line = if ($_ -is [System.Management.Automation.ErrorRecord]) {
            $_.Exception.Message
        } else { $_ }
        Add-Content -Path $OutFile -Value "  $line"
    }

    # Flag result
    if ($sfcOutput -match "did not find any integrity violations") {
        Add-Content -Path $OutFile -Value ""
        Add-Content -Path $OutFile -Value "  RESULT: No integrity violations found."
    } elseif ($sfcOutput -match "found corrupt files") {
        Add-Content -Path $OutFile -Value ""
        Add-Content -Path $OutFile -Value "  *** ATTENTION: SFC found corrupt files. ***"
        Add-Content -Path $OutFile -Value "  *** Consider running: sfc /scannow (to attempt repair) ***"
        Add-Content -Path $OutFile -Value "  *** Check CBS.log at: C:\Windows\Logs\CBS\CBS.log ***"
    }

} catch {
    Add-Content -Path $OutFile -Value "ERROR running SFC: $_"
}

# ---------------------------------------------------------------------------
# CBS log — last 20 lines (SFC writes detail here)
# ---------------------------------------------------------------------------
Write-Section -Title "CBS LOG — LAST 20 LINES" -FilePath $OutFile

$cbsLog = "C:\Windows\Logs\CBS\CBS.log"

if (Test-Path $cbsLog) {
    try {
        $lastLines = Get-Content -Path $cbsLog -Tail 20 -ErrorAction Stop
        $lastLines | ForEach-Object { Add-Content -Path $OutFile -Value "  $_" }
    } catch {
        Add-Content -Path $OutFile -Value "ERROR reading CBS.log: $_"
    }
} else {
    Add-Content -Path $OutFile -Value "  CBS.log not found at expected path: $cbsLog"
}

# ---------------------------------------------------------------------------
# Reliability Monitor data via WMI (high signal, no GUI needed)
# ---------------------------------------------------------------------------
Write-Section -Title "RELIABILITY EVENTS (last 14 days)" -FilePath $OutFile

$reliabilityCutoff = (Get-Date).AddDays(-14)

try {
    $reliabilityEvents = Get-CimInstance -Namespace "root\cimv2" -ClassName "Win32_ReliabilityRecords" -ErrorAction Stop |
        Where-Object { $_.TimeGenerated -ge $reliabilityCutoff } |
        Sort-Object TimeGenerated -Descending

    if ($reliabilityEvents -and $reliabilityEvents.Count -gt 0) {
        Add-Content -Path $OutFile -Value ("  {0} reliability events in the last 14 days." -f $reliabilityEvents.Count)
        Add-Content -Path $OutFile -Value ""
        Add-Content -Path $OutFile -Value ("  {0,-20} {1,-15} {2,-30} {3}" -f "Time","Type","Source","Message")
        Add-Content -Path $OutFile -Value ("  {0}" -f ("-" * 110))

        foreach ($evt in $reliabilityEvents) {
            $time    = $evt.TimeGenerated.ToString("yyyy-MM-dd HH:mm")
            $type    = $evt.EventIdentifier
            $source  = if ($evt.SourceName)  { $evt.SourceName }  else { "N/A" }
            $message = if ($evt.Message)     { ($evt.Message -replace "`r`n|`n"," ").Substring(0, [Math]::Min(60,$evt.Message.Length)) } else { "N/A" }

            Add-Content -Path $OutFile -Value ("  {0,-20} {1,-15} {2,-30} {3}" -f $time, $type, $source, $message)
        }
    } else {
        Add-Content -Path $OutFile -Value "  No reliability events found in the last 14 days."
    }
} catch {
    Add-Content -Path $OutFile -Value "  Reliability records not available via WMI on this system."
    Add-Content -Path $OutFile -Value "  Use: perfmon /rel — to view Reliability Monitor manually."
}

Write-Host "collect-integrity.ps1 complete -> $OutFile"
