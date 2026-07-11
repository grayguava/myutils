# =============================================================================
# collect-drivers.ps1
# Collects installed driver information: name, version, date, provider,
# and flags any unsigned or problematic drivers.
#
# Output: drivers.txt in the dated snapshot directory.
# =============================================================================

. "$PSScriptRoot\helpers\common.ps1"

Ensure-SnapshotDir

$OutFile = Join-Path $global:SnapshotDir "drivers.txt"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Set-Content -Path $OutFile -Value "PCHealth — Driver Report"
Add-Content -Path $OutFile -Value "Collected : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Content -Path $OutFile -Value "Host      : $env:COMPUTERNAME"
Add-Content -Path $OutFile -Value ""

# ---------------------------------------------------------------------------
# All installed drivers via Get-WindowsDriver (full detail)
# Requires elevation
# ---------------------------------------------------------------------------
Write-Section -Title "INSTALLED DRIVERS (Get-WindowsDriver)" -FilePath $OutFile

try {
    $drivers = Get-WindowsDriver -Online -ErrorAction Stop |
        Sort-Object DriverDate -Descending

    Add-Content -Path $OutFile -Value ("  Total drivers found: {0}" -f $drivers.Count)
    Add-Content -Path $OutFile -Value ""
    Add-Content -Path $OutFile -Value ("  {0,-55} {1,-20} {2,-12} {3}" -f "Driver","Version","Date","Provider")
    Add-Content -Path $OutFile -Value ("  {0}" -f ("-" * 120))

    foreach ($d in $drivers) {
        $date     = if ($d.DriverDate) { $d.DriverDate.ToString("yyyy-MM-dd") } else { "Unknown" }
        $version  = if ($d.Version)    { $d.Version }    else { "N/A" }
        $provider = if ($d.ProviderName) { $d.ProviderName } else { "N/A" }
        $inf      = if ($d.Driver)     { $d.Driver }     else { "N/A" }

        Add-Content -Path $OutFile -Value ("  {0,-55} {1,-20} {2,-12} {3}" -f `
            $inf, $version, $date, $provider)
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading drivers via Get-WindowsDriver: $_"
    Add-Content -Path $OutFile -Value "Note: This cmdlet requires elevation and may not be available on all systems."
}

# ---------------------------------------------------------------------------
# Signed driver status via Get-WmiObject Win32_PnPSignedDriver
# More human-readable names + signature status
# ---------------------------------------------------------------------------
Write-Section -Title "PNP SIGNED DRIVERS WITH DEVICE NAMES" -FilePath $OutFile

try {
    $pnpDrivers = Get-CimInstance Win32_PnPSignedDriver -ErrorAction Stop |
        Where-Object { $_.DeviceName -ne $null } |
        Sort-Object DeviceName

    Add-Content -Path $OutFile -Value ("  {0,-50} {1,-20} {2,-12} {3}" -f "Device","Version","Date","Signer")
    Add-Content -Path $OutFile -Value ("  {0}" -f ("-" * 110))

    foreach ($d in $pnpDrivers) {
        $date   = if ($d.DriverDate)    { ([datetime]$d.DriverDate).ToString("yyyy-MM-dd") } else { "Unknown" }
        $ver    = if ($d.DriverVersion) { $d.DriverVersion } else { "N/A" }
        $signer = if ($d.Signer)        { $d.Signer }        else { "UNSIGNED" }
        $name   = if ($d.DeviceName)    { $d.DeviceName }    else { "N/A" }

        Add-Content -Path $OutFile -Value ("  {0,-50} {1,-20} {2,-12} {3}" -f `
            $name, $ver, $date, $signer)
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading PnP drivers: $_"
}

# ---------------------------------------------------------------------------
# Unsigned drivers — flag these explicitly
# ---------------------------------------------------------------------------
Write-Section -Title "UNSIGNED DRIVERS (attention required)" -FilePath $OutFile

try {
    $unsigned = Get-CimInstance Win32_PnPSignedDriver -ErrorAction Stop |
        Where-Object { -not $_.Signer -or $_.Signer -eq "" } |
        Where-Object { $_.DeviceName -ne $null }

    if ($unsigned -and $unsigned.Count -gt 0) {
        Add-Content -Path $OutFile -Value ("  WARNING: {0} unsigned driver(s) found." -f $unsigned.Count)
        Add-Content -Path $OutFile -Value ""
        foreach ($d in $unsigned) {
            Write-KV -Key "Device"  -Value $d.DeviceName    -FilePath $OutFile
            Write-KV -Key "INF"     -Value $d.InfName       -FilePath $OutFile
            Write-KV -Key "Version" -Value $d.DriverVersion -FilePath $OutFile
            Add-Content -Path $OutFile -Value ""
        }
    } else {
        Add-Content -Path $OutFile -Value "  All detected drivers are signed."
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR checking unsigned drivers: $_"
}

# ---------------------------------------------------------------------------
# Recently installed or updated drivers (last 30 days)
# ---------------------------------------------------------------------------
Write-Section -Title "RECENTLY CHANGED DRIVERS (last 30 days)" -FilePath $OutFile

$cutoff = (Get-Date).AddDays(-30)

try {
    $recent = Get-CimInstance Win32_PnPSignedDriver -ErrorAction Stop |
        Where-Object {
            $_.DriverDate -ne $null -and
            ([datetime]$_.DriverDate) -ge $cutoff -and
            $_.DeviceName -ne $null
        } |
        Sort-Object DriverDate -Descending

    if ($recent -and $recent.Count -gt 0) {
        Add-Content -Path $OutFile -Value ("  {0} driver(s) installed or updated in the last 30 days:" -f $recent.Count)
        Add-Content -Path $OutFile -Value ""
        foreach ($d in $recent) {
            $date = ([datetime]$d.DriverDate).ToString("yyyy-MM-dd")
            Add-Content -Path $OutFile -Value ("  [{0}]  {1}  —  v{2}  ({3})" -f `
                $date, $d.DeviceName, $d.DriverVersion, $d.Signer)
        }
    } else {
        Add-Content -Path $OutFile -Value "  No driver changes detected in the last 30 days."
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading recent driver changes: $_"
}

# ---------------------------------------------------------------------------
# Problem devices (error code != 0 in Device Manager)
# ---------------------------------------------------------------------------
Write-Section -Title "PROBLEM DEVICES (Device Manager errors)" -FilePath $OutFile

try {
    $problemDevices = Get-CimInstance Win32_PnPEntity -ErrorAction Stop |
        Where-Object { $_.ConfigManagerErrorCode -ne 0 }

    if ($problemDevices -and $problemDevices.Count -gt 0) {
        Add-Content -Path $OutFile -Value ("  WARNING: {0} device(s) with errors found." -f $problemDevices.Count)
        Add-Content -Path $OutFile -Value ""
        foreach ($dev in $problemDevices) {
            Write-KV -Key "Name"       -Value $dev.Name                     -FilePath $OutFile
            Write-KV -Key "Error Code" -Value $dev.ConfigManagerErrorCode   -FilePath $OutFile
            Write-KV -Key "Status"     -Value $dev.Status                   -FilePath $OutFile
            Add-Content -Path $OutFile -Value ""
        }
    } else {
        Add-Content -Path $OutFile -Value "  No problem devices detected."
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading problem devices: $_"
}

Write-Host "collect-drivers.ps1 complete -> $OutFile"
