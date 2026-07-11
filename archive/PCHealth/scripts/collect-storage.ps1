# =============================================================================
# collect-storage.ps1
# Collects SMART health data for all detected drives using smartmontools.
# Also collects Windows disk info via Get-PhysicalDisk as a fallback/supplement.
#
# Output: storage.txt in the dated snapshot directory.
# =============================================================================

. "$PSScriptRoot\helpers\common.ps1"

Ensure-SnapshotDir

$OutFile = Join-Path $global:SnapshotDir "storage.txt"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Set-Content -Path $OutFile -Value "PCHealth — Storage Health Report"
Add-Content -Path $OutFile -Value "Collected : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Content -Path $OutFile -Value "Host      : $env:COMPUTERNAME"
Add-Content -Path $OutFile -Value ""

# ---------------------------------------------------------------------------
# Windows built-in disk summary (always runs, no external tool needed)
# ---------------------------------------------------------------------------
Write-Section -Title "WINDOWS DISK SUMMARY (Get-PhysicalDisk)" -FilePath $OutFile

try {
    $disks = Get-PhysicalDisk | Select-Object `
        FriendlyName, MediaType, OperationalStatus, HealthStatus,
        @{N="Size";E={ "{0:N2} GB" -f ($_.Size / 1GB) }},
        @{N="Used";E={
            $part = Get-Disk | Where-Object { $_.FriendlyName -eq $_.FriendlyName }
            "N/A"  # placeholder; actual used space via Get-PSDrive below
        }}

    foreach ($disk in $disks) {
        Add-Content -Path $OutFile -Value ""
        Write-KV -Key "Name"               -Value $disk.FriendlyName   -FilePath $OutFile
        Write-KV -Key "Type"               -Value $disk.MediaType       -FilePath $OutFile
        Write-KV -Key "Size"               -Value $disk.Size            -FilePath $OutFile
        Write-KV -Key "Operational Status" -Value $disk.OperationalStatus -FilePath $OutFile
        Write-KV -Key "Health Status"      -Value $disk.HealthStatus    -FilePath $OutFile
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading physical disk info: $_"
}

# ---------------------------------------------------------------------------
# Volume / partition usage
# ---------------------------------------------------------------------------
Write-Section -Title "VOLUME USAGE (Get-PSDrive)" -FilePath $OutFile

try {
    Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Used -gt 0 } | ForEach-Object {
        $total = $_.Used + $_.Free
        $usedPct = if ($total -gt 0) { "{0:N1}%" -f ($_.Used / $total * 100) } else { "N/A" }
        Add-Content -Path $OutFile -Value ""
        Write-KV -Key "Drive"     -Value $_.Name                                          -FilePath $OutFile
        Write-KV -Key "Root"      -Value $_.Root                                          -FilePath $OutFile
        Write-KV -Key "Used"      -Value ("{0:N2} GB" -f ($_.Used / 1GB))                -FilePath $OutFile
        Write-KV -Key "Free"      -Value ("{0:N2} GB" -f ($_.Free / 1GB))                -FilePath $OutFile
        Write-KV -Key "Total"     -Value ("{0:N2} GB" -f ($total / 1GB))                 -FilePath $OutFile
        Write-KV -Key "Used %"    -Value $usedPct                                         -FilePath $OutFile
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading volume usage: $_"
}

# ---------------------------------------------------------------------------
# SMART data via smartctl
# ---------------------------------------------------------------------------
Write-Section -Title "SMART DATA (smartctl)" -FilePath $OutFile

if (-not (Assert-Tool -ExePath $SmartCtlExe -FriendlyName "smartctl")) {
    Add-Content -Path $OutFile -Value "smartctl not found — SMART data skipped."
    Add-Content -Path $OutFile -Value "Install smartmontools and place smartctl.exe in:"
    Add-Content -Path $OutFile -Value "  $SmartCtlExe"
} else {
    # Scan for all drives
    try {
        $scanOutput = & $SmartCtlExe --scan 2>&1
        $driveList  = $scanOutput | Where-Object { $_ -match "^/dev/" } |
                      ForEach-Object { ($_ -split "\s+")[0] }

        if (-not $driveList) {
            Add-Content -Path $OutFile -Value "No drives detected by smartctl --scan."
        } else {
            foreach ($drive in $driveList) {
                Add-Content -Path $OutFile -Value ""
                Add-Content -Path $OutFile -Value ("=" * 60)
                Add-Content -Path $OutFile -Value "  Drive: $drive"
                Add-Content -Path $OutFile -Value ("=" * 60)

                # Full health + attributes in one call
                $smartData = & $SmartCtlExe -a $drive 2>&1

                if ($LASTEXITCODE -gt 1) {
                    # Exit code 1 = some attributes flagged (still output data)
                    # Exit code > 1 = real error
                    Add-Content -Path $OutFile -Value "WARNING: smartctl exited with code $LASTEXITCODE for $drive"
                }

                $smartData | ForEach-Object { Add-Content -Path $OutFile -Value $_ }

                # Pull out key SMART attributes explicitly for easy scanning
                Add-Content -Path $OutFile -Value ""
                Add-Content -Path $OutFile -Value "  --- Key Attributes ---"

                $keyAttrs = @(
                    "Reallocated_Sector",
                    "Reallocated_Event",
                    "Current_Pending_Sector",
                    "Offline_Uncorrectable",
                    "UDMA_CRC_Error",
                    "Power_On_Hours",
                    "Temperature_Celsius",
                    "Media_Wearout",
                    "Available_Reservd",
                    "Wear_Leveling",
                    "Total_LBAs_Written",
                    "Host_Reads",
                    "Host_Writes",
                    "SMART overall-health"
                )

                foreach ($attr in $keyAttrs) {
                    $match = $smartData | Where-Object { $_ -match $attr }
                    if ($match) {
                        $match | ForEach-Object { Add-Content -Path $OutFile -Value "  $_" }
                    }
                }
            }
        }
    } catch {
        Add-Content -Path $OutFile -Value "ERROR running smartctl: $_"
    }
}

# ---------------------------------------------------------------------------
# Disk temperature via smartctl (summary table)
# ---------------------------------------------------------------------------
Write-Section -Title "DRIVE TEMPERATURE SUMMARY" -FilePath $OutFile

if (Test-Path $SmartCtlExe) {
    try {
        $scanOutput = & $SmartCtlExe --scan 2>&1
        $driveList  = $scanOutput | Where-Object { $_ -match "^/dev/" } |
                      ForEach-Object { ($_ -split "\s+")[0] }

        foreach ($drive in $driveList) {
            $tempLine = & $SmartCtlExe -A $drive 2>&1 |
                        Where-Object { $_ -match "Temperature_Celsius|Temperature_Internal|Airflow_Temperature" } |
                        Select-Object -First 1

            if ($tempLine) {
                $tempVal = ($tempLine -split "\s+") | Where-Object { $_ -match "^\d+$" } | Select-Object -Last 1
                Write-KV -Key $drive -Value "$tempVal °C" -FilePath $OutFile
            } else {
                Write-KV -Key $drive -Value "Temperature not available" -FilePath $OutFile
            }
        }
    } catch {
        Add-Content -Path $OutFile -Value "ERROR reading drive temperatures: $_"
    }
} else {
    Add-Content -Path $OutFile -Value "smartctl not available — drive temps skipped."
}

Write-Host "collect-storage.ps1 complete -> $OutFile"
