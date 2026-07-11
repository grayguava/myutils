# =============================================================================
# collect-software.ps1
# Collects installed software from the Windows registry (both 64-bit and
# 32-bit hives, system-wide and per-user). Useful for spotting drift —
# things installed, removed, or updated without you noticing.
#
# Output: software.txt in the dated snapshot directory.
# =============================================================================

. "$PSScriptRoot\helpers\common.ps1"

Ensure-SnapshotDir

$OutFile = Join-Path $global:SnapshotDir "software.txt"

# ---------------------------------------------------------------------------
# Registry paths that contain installed software
# ---------------------------------------------------------------------------
$RegistryPaths = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
)

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Set-Content -Path $OutFile -Value "PCHealth — Installed Software Report"
Add-Content -Path $OutFile -Value "Collected : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Content -Path $OutFile -Value "Host      : $env:COMPUTERNAME"
Add-Content -Path $OutFile -Value ""

# ---------------------------------------------------------------------------
# Gather all entries from all registry hives
# ---------------------------------------------------------------------------
$allSoftware = @()

foreach ($path in $RegistryPaths) {
    try {
        $entries = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue |
            Where-Object {
                $_.DisplayName -and
                $_.DisplayName.Trim() -ne "" -and
                # Filter out Windows internal components — they're noise
                $_.DisplayName -notmatch "^(KB\d+|Microsoft Visual C\+\+ \d{4} Redistributable|Microsoft .NET|Update for|Security Update for|Hotfix)"
            } |
            ForEach-Object {
                [PSCustomObject]@{
                    Name        = $_.DisplayName.Trim()
                    Version     = if ($_.DisplayVersion) { $_.DisplayVersion.Trim() } else { "N/A" }
                    Publisher   = if ($_.Publisher)      { $_.Publisher.Trim() }      else { "N/A" }
                    InstallDate = if ($_.InstallDate)    { $_.InstallDate }           else { "Unknown" }
                    InstallLocation = if ($_.InstallLocation) { $_.InstallLocation } else { "" }
                    Source      = $path -replace "HKLM:\\SOFTWARE\\WOW6432Node.*","[32-bit]" `
                                        -replace "HKLM:\\SOFTWARE\\Microsoft.*","[64-bit]" `
                                        -replace "HKCU:\\SOFTWARE.*","[User]"
                }
            }
        if ($entries) { $allSoftware += $entries }
    } catch {
        Add-Content -Path $OutFile -Value "WARNING: Could not read registry path: $path — $_"
    }
}

# Deduplicate by name + version (same app can appear in multiple hives)
$allSoftware = $allSoftware | Sort-Object Name, Version -Unique

# ---------------------------------------------------------------------------
# Full sorted list
# ---------------------------------------------------------------------------
Write-Section -Title "ALL INSTALLED SOFTWARE (alphabetical)" -FilePath $OutFile

Add-Content -Path $OutFile -Value ("  Total entries: {0}" -f $allSoftware.Count)
Add-Content -Path $OutFile -Value ""
Add-Content -Path $OutFile -Value ("  {0,-55} {1,-25} {2,-15} {3,-12} {4}" -f `
    "Name", "Publisher", "Version", "Install Date", "Arch")
Add-Content -Path $OutFile -Value ("  {0}" -f ("-" * 130))

foreach ($app in ($allSoftware | Sort-Object Name)) {
    Add-Content -Path $OutFile -Value ("  {0,-55} {1,-25} {2,-15} {3,-12} {4}" -f `
        ($app.Name -replace ".{56,}", ($app.Name.Substring(0,52) + "...")),
        ($app.Publisher -replace ".{26,}", ($app.Publisher.Substring(0,22) + "...")),
        $app.Version,
        $app.InstallDate,
        $app.Source)
}

# ---------------------------------------------------------------------------
# Recently installed (last 30 days) — most useful for drift detection
# ---------------------------------------------------------------------------
Write-Section -Title "RECENTLY INSTALLED (last 30 days)" -FilePath $OutFile

$cutoffDate = (Get-Date).AddDays(-30).ToString("yyyyMMdd")

$recentlyInstalled = $allSoftware | Where-Object {
    $_.InstallDate -ne "Unknown" -and
    $_.InstallDate -match "^\d{8}$" -and
    $_.InstallDate -ge $cutoffDate
} | Sort-Object InstallDate -Descending

if ($recentlyInstalled -and $recentlyInstalled.Count -gt 0) {
    Add-Content -Path $OutFile -Value ("  {0} application(s) installed in the last 30 days:" -f $recentlyInstalled.Count)
    Add-Content -Path $OutFile -Value ""
    foreach ($app in $recentlyInstalled) {
        $dateFormatted = if ($app.InstallDate -match "^(\d{4})(\d{2})(\d{2})$") {
            "$($Matches[1])-$($Matches[2])-$($Matches[3])"
        } else { $app.InstallDate }
        Add-Content -Path $OutFile -Value ("  [{0}]  {1}  v{2}  ({3})" -f `
            $dateFormatted, $app.Name, $app.Version, $app.Publisher)
    }
} else {
    Add-Content -Path $OutFile -Value "  No new installs detected in the last 30 days."
    Add-Content -Path $OutFile -Value "  (Note: InstallDate is not always set by installers.)"
}

# ---------------------------------------------------------------------------
# Software installed without a date (common — many installers skip this field)
# ---------------------------------------------------------------------------
Write-Section -Title "INSTALLS WITH NO DATE (informational)" -FilePath $OutFile

$noDate = $allSoftware | Where-Object { $_.InstallDate -eq "Unknown" } | Sort-Object Name

Add-Content -Path $OutFile -Value ("  {0} entries have no InstallDate recorded." -f $noDate.Count)
Add-Content -Path $OutFile -Value "  These cannot be reliably tracked by date — compare lists week-over-week."
Add-Content -Path $OutFile -Value ""

foreach ($app in $noDate) {
    Add-Content -Path $OutFile -Value ("  {0,-55} v{1}  ({2})" -f $app.Name, $app.Version, $app.Publisher)
}

# ---------------------------------------------------------------------------
# Windows Store apps (separate from registry installs)
# ---------------------------------------------------------------------------
Write-Section -Title "WINDOWS STORE APPS (Get-AppxPackage)" -FilePath $OutFile

try {
    $storeApps = Get-AppxPackage -ErrorAction Stop |
        Where-Object { $_.SignatureKind -ne "System" } |  # exclude built-in Windows packages
        Sort-Object Name |
        Select-Object Name, Version, Publisher, InstallLocation

    Add-Content -Path $OutFile -Value ("  Total Store apps: {0}" -f $storeApps.Count)
    Add-Content -Path $OutFile -Value ""

    foreach ($app in $storeApps) {
        Add-Content -Path $OutFile -Value ("  {0,-55} v{1}" -f $app.Name, $app.Version)
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading Store apps: $_"
}

Write-Host "collect-software.ps1 complete -> $OutFile"
