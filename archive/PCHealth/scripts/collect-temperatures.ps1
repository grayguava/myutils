# =============================================================================
# collect-temperatures.ps1
# Collects CPU, GPU, and motherboard temperature readings using
# LibreHardwareMonitor CLI. Requires elevation (Admin).
#
# Output: temperatures.txt in the dated snapshot directory.
# =============================================================================

. "$PSScriptRoot\helpers\common.ps1"

Ensure-SnapshotDir

$OutFile = Join-Path $global:SnapshotDir "temperatures.txt"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Set-Content -Path $OutFile -Value "PCHealth — Temperature Report"
Add-Content -Path $OutFile -Value "Collected : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Content -Path $OutFile -Value "Host      : $env:COMPUTERNAME"
Add-Content -Path $OutFile -Value ""

# ---------------------------------------------------------------------------
# Check elevation
# ---------------------------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)
if (-not $isAdmin) {
    Add-Content -Path $OutFile -Value "ERROR: Script must run as Administrator to read hardware sensors."
    Write-Warning "collect-temperatures.ps1 requires elevation. Skipping."
    exit 1
}

# ---------------------------------------------------------------------------
# Check tool availability
# ---------------------------------------------------------------------------
if (-not (Assert-Tool -ExePath $LHMExe -FriendlyName "LibreHardwareMonitor")) {
    Add-Content -Path $OutFile -Value "ERROR: LibreHardwareMonitor.exe not found at expected path."
    Add-Content -Path $OutFile -Value "Expected: $LHMExe"
    exit 1
}

# ---------------------------------------------------------------------------
# Idle reading
# Collect once immediately — system should be near idle at scheduled run time
# ---------------------------------------------------------------------------
Write-Section -Title "IDLE TEMPERATURE READING" -FilePath $OutFile

try {
    # LHM CLI: --no-gui dumps sensor data to stdout then exits
    $idleOutput = & $LHMExe --no-gui 2>&1

    if ($LASTEXITCODE -ne 0 -or -not $idleOutput) {
        Add-Content -Path $OutFile -Value "WARNING: LibreHardwareMonitor returned no output or exited with error."
    } else {
        # Filter to temperature sensors only
        $tempLines = $idleOutput | Where-Object {
            $_ -match "Temperature|°C|Temp|CPU|GPU|Core|Package|Junction|Hot Spot|Board|System"
        }

        if ($tempLines) {
            $tempLines | ForEach-Object { Add-Content -Path $OutFile -Value $_ }
        } else {
            # Fallback: dump full output so nothing is silently lost
            Add-Content -Path $OutFile -Value "Note: Could not isolate temperature lines. Full sensor output below."
            Add-Content -Path $OutFile -Value ""
            $idleOutput | ForEach-Object { Add-Content -Path $OutFile -Value $_ }
        }
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR running LibreHardwareMonitor: $_"
}

# ---------------------------------------------------------------------------
# Light load reading
# Start a brief CPU stress (using built-in math loops) then sample again
# This helps surface thermal throttling headroom without being destructive
# ---------------------------------------------------------------------------
Write-Section -Title "LIGHT LOAD TEMPERATURE READING (30s stress)" -FilePath $OutFile

Add-Content -Path $OutFile -Value "Note: Running 30-second light CPU load via PowerShell math loops."
Add-Content -Path $OutFile -Value "      This is intentionally mild — not a full benchmark."
Add-Content -Path $OutFile -Value ""

# Launch background jobs to occupy logical cores briefly
$stressJobs = 1..(([Environment]::ProcessorCount)) | ForEach-Object {
    Start-Job -ScriptBlock {
        $end = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $end) {
            [Math]::Sqrt([Math]::PI * 99999) | Out-Null
        }
    }
}

# Wait 25 seconds then sample (temps peak before the 30s mark)
Start-Sleep -Seconds 25

try {
    $loadOutput = & $LHMExe --no-gui 2>&1

    if ($LASTEXITCODE -ne 0 -or -not $loadOutput) {
        Add-Content -Path $OutFile -Value "WARNING: LibreHardwareMonitor returned no output during load test."
    } else {
        $tempLines = $loadOutput | Where-Object {
            $_ -match "Temperature|°C|Temp|CPU|GPU|Core|Package|Junction|Hot Spot|Board|System"
        }

        if ($tempLines) {
            $tempLines | ForEach-Object { Add-Content -Path $OutFile -Value $_ }
        } else {
            Add-Content -Path $OutFile -Value "Note: Could not isolate temperature lines. Full sensor output below."
            Add-Content -Path $OutFile -Value ""
            $loadOutput | ForEach-Object { Add-Content -Path $OutFile -Value $_ }
        }
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR running LibreHardwareMonitor during load test: $_"
}

# Clean up stress jobs
$stressJobs | Stop-Job -ErrorAction SilentlyContinue
$stressJobs | Remove-Job -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# Fan speeds (bonus — same tool, same run)
# ---------------------------------------------------------------------------
Write-Section -Title "FAN SPEEDS" -FilePath $OutFile

try {
    $fanOutput = & $LHMExe --no-gui 2>&1
    $fanLines = $fanOutput | Where-Object { $_ -match "Fan|RPM" }

    if ($fanLines) {
        $fanLines | ForEach-Object { Add-Content -Path $OutFile -Value $_ }
    } else {
        Add-Content -Path $OutFile -Value "No fan speed data reported by LibreHardwareMonitor."
        Add-Content -Path $OutFile -Value "(Some motherboards do not expose fan sensors via LHM.)"
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading fan data: $_"
}

# ---------------------------------------------------------------------------
# Thresholds reminder (for manual review)
# ---------------------------------------------------------------------------
Write-Section -Title "REFERENCE THRESHOLDS" -FilePath $OutFile

Add-Content -Path $OutFile -Value "  CPU Idle     : Normal < 50°C   |  Concern > 70°C at idle"
Add-Content -Path $OutFile -Value "  CPU Load     : Normal < 80°C   |  Concern > 90°C under load"
Add-Content -Path $OutFile -Value "  GPU Idle     : Normal < 50°C   |  Concern > 70°C at idle"
Add-Content -Path $OutFile -Value "  GPU Load     : Normal < 85°C   |  Concern > 95°C under load"
Add-Content -Path $OutFile -Value "  Motherboard  : Normal < 45°C   |  Concern > 60°C"
Add-Content -Path $OutFile -Value "  SSD (NVMe)   : Normal < 55°C   |  Concern > 70°C"
Add-Content -Path $OutFile -Value ""
Add-Content -Path $OutFile -Value "  Summer note: ambient temps elevate all readings by 3-8°C typically."
Add-Content -Path $OutFile -Value "  Compare week-over-week, not just against thresholds."

Write-Host "collect-temperatures.ps1 complete -> $OutFile"
