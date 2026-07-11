# =============================================================================
# collect-system.ps1
# Collects general system information: OS, CPU, RAM, uptime, pagefile,
# power plan, pending reboots, and Windows Update history.
#
# Output: system.txt in the dated snapshot directory.
# =============================================================================

. "$PSScriptRoot\helpers\common.ps1"

Ensure-SnapshotDir

$OutFile = Join-Path $global:SnapshotDir "system.txt"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Set-Content -Path $OutFile -Value "PCHealth — System Information Report"
Add-Content -Path $OutFile -Value "Collected : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Content -Path $OutFile -Value "Host      : $env:COMPUTERNAME"
Add-Content -Path $OutFile -Value ""

# ---------------------------------------------------------------------------
# OS & machine identity
# ---------------------------------------------------------------------------
Write-Section -Title "OPERATING SYSTEM" -FilePath $OutFile

try {
    $os = Get-CimInstance Win32_OperatingSystem
    Write-KV -Key "OS Name"          -Value $os.Caption              -FilePath $OutFile
    Write-KV -Key "Version"          -Value $os.Version              -FilePath $OutFile
    Write-KV -Key "Build"            -Value $os.BuildNumber          -FilePath $OutFile
    Write-KV -Key "Architecture"     -Value $os.OSArchitecture       -FilePath $OutFile
    Write-KV -Key "Install Date"     -Value ($os.InstallDate.ToString("yyyy-MM-dd")) -FilePath $OutFile
    Write-KV -Key "Registered User"  -Value $os.RegisteredUser       -FilePath $OutFile
    Write-KV -Key "Serial Number"    -Value $os.SerialNumber         -FilePath $OutFile
    Write-KV -Key "Windows Dir"      -Value $os.WindowsDirectory     -FilePath $OutFile
    Write-KV -Key "System Dir"       -Value $os.SystemDirectory      -FilePath $OutFile
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading OS info: $_"
}

# ---------------------------------------------------------------------------
# Uptime
# ---------------------------------------------------------------------------
Write-Section -Title "UPTIME" -FilePath $OutFile

try {
    $os        = Get-CimInstance Win32_OperatingSystem
    $bootTime  = $os.LastBootUpTime
    $uptime    = (Get-Date) - $bootTime
    Write-KV -Key "Last Boot"   -Value $bootTime.ToString("yyyy-MM-dd HH:mm:ss") -FilePath $OutFile
    Write-KV -Key "Uptime"      -Value ("{0}d {1}h {2}m" -f $uptime.Days, $uptime.Hours, $uptime.Minutes) -FilePath $OutFile
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading uptime: $_"
}

# ---------------------------------------------------------------------------
# CPU
# ---------------------------------------------------------------------------
Write-Section -Title "CPU" -FilePath $OutFile

try {
    $cpus = Get-CimInstance Win32_Processor
    foreach ($cpu in $cpus) {
        Write-KV -Key "Name"             -Value $cpu.Name.Trim()           -FilePath $OutFile
        Write-KV -Key "Manufacturer"     -Value $cpu.Manufacturer          -FilePath $OutFile
        Write-KV -Key "Cores (Physical)" -Value $cpu.NumberOfCores         -FilePath $OutFile
        Write-KV -Key "Logical Procs"    -Value $cpu.NumberOfLogicalProcessors -FilePath $OutFile
        Write-KV -Key "Max Clock (MHz)"  -Value $cpu.MaxClockSpeed         -FilePath $OutFile
        Write-KV -Key "Current Load %"   -Value $cpu.LoadPercentage        -FilePath $OutFile
        Write-KV -Key "Socket"           -Value $cpu.SocketDesignation     -FilePath $OutFile
        Write-KV -Key "L2 Cache (KB)"    -Value $cpu.L2CacheSize           -FilePath $OutFile
        Write-KV -Key "L3 Cache (KB)"    -Value $cpu.L3CacheSize           -FilePath $OutFile
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading CPU info: $_"
}

# ---------------------------------------------------------------------------
# RAM
# ---------------------------------------------------------------------------
Write-Section -Title "MEMORY (RAM)" -FilePath $OutFile

try {
    $os        = Get-CimInstance Win32_OperatingSystem
    $totalRAM  = [math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
    $freeRAM   = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
    $usedRAM   = [math]::Round($totalRAM - $freeRAM, 2)
    $usedPct   = [math]::Round(($usedRAM / $totalRAM) * 100, 1)

    Write-KV -Key "Total RAM"    -Value "$totalRAM GB"    -FilePath $OutFile
    Write-KV -Key "Used RAM"     -Value "$usedRAM GB ($usedPct%)" -FilePath $OutFile
    Write-KV -Key "Free RAM"     -Value "$freeRAM GB"     -FilePath $OutFile
    Add-Content -Path $OutFile -Value ""

    # Physical DIMM slots
    $dimms = Get-CimInstance Win32_PhysicalMemory
    Add-Content -Path $OutFile -Value "  Physical DIMMs:"
    foreach ($dimm in $dimms) {
        $sizeGB = [math]::Round($dimm.Capacity / 1GB, 0)
        Add-Content -Path $OutFile -Value ("    Slot {0,-10} {1} GB  {2} MHz  {3}" -f `
            $dimm.DeviceLocator, $sizeGB, $dimm.Speed, $dimm.PartNumber.Trim())
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading RAM info: $_"
}

# ---------------------------------------------------------------------------
# Pagefile
# ---------------------------------------------------------------------------
Write-Section -Title "PAGEFILE" -FilePath $OutFile

try {
    $pf = Get-CimInstance Win32_PageFileUsage
    foreach ($p in $pf) {
        Write-KV -Key "Path"           -Value $p.Name                -FilePath $OutFile
        Write-KV -Key "Allocated (MB)" -Value $p.AllocatedBaseSize   -FilePath $OutFile
        Write-KV -Key "Current Use (MB)" -Value $p.CurrentUsage      -FilePath $OutFile
        Write-KV -Key "Peak Use (MB)"  -Value $p.PeakUsage           -FilePath $OutFile
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading pagefile info: $_"
}

# ---------------------------------------------------------------------------
# Power plan
# ---------------------------------------------------------------------------
Write-Section -Title "POWER PLAN" -FilePath $OutFile

try {
    $powerOutput = powercfg /getactivescheme 2>&1
    Add-Content -Path $OutFile -Value "  $powerOutput"
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading power plan: $_"
}

# ---------------------------------------------------------------------------
# Pending reboot detection
# ---------------------------------------------------------------------------
Write-Section -Title "PENDING REBOOT CHECK" -FilePath $OutFile

$pendingReboot = $false
$rebootReasons = @()

$rebootChecks = @{
    "Windows Update"        = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"
    "CBS (Component Store)" = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"
    "Pending File Rename"   = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager"
}

foreach ($check in $rebootChecks.GetEnumerator()) {
    if (Test-Path $check.Value) {
        if ($check.Key -eq "Pending File Rename") {
            $pfro = Get-ItemProperty -Path $check.Value -Name "PendingFileRenameOperations" -ErrorAction SilentlyContinue
            if ($pfro) {
                $pendingReboot = $true
                $rebootReasons += $check.Key
            }
        } else {
            $pendingReboot = $true
            $rebootReasons += $check.Key
        }
    }
}

if ($pendingReboot) {
    Add-Content -Path $OutFile -Value "  STATUS: REBOOT PENDING"
    $rebootReasons | ForEach-Object { Add-Content -Path $OutFile -Value "    - $_" }
} else {
    Add-Content -Path $OutFile -Value "  STATUS: No pending reboot detected."
}

# ---------------------------------------------------------------------------
# Windows Update history (last 20 updates)
# ---------------------------------------------------------------------------
Write-Section -Title "WINDOWS UPDATE HISTORY (last 20)" -FilePath $OutFile

try {
    $updates = Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 20
    foreach ($upd in $updates) {
        $date = if ($upd.InstalledOn) { $upd.InstalledOn.ToString("yyyy-MM-dd") } else { "Unknown" }
        Add-Content -Path $OutFile -Value ("  {0,-12}  {1,-15}  {2}" -f $date, $upd.HotFixID, $upd.Description)
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading update history: $_"
}

# ---------------------------------------------------------------------------
# Motherboard / BIOS
# ---------------------------------------------------------------------------
Write-Section -Title "MOTHERBOARD & BIOS" -FilePath $OutFile

try {
    $board = Get-CimInstance Win32_BaseBoard
    $bios  = Get-CimInstance Win32_BIOS
    Write-KV -Key "Board Manufacturer" -Value $board.Manufacturer    -FilePath $OutFile
    Write-KV -Key "Board Product"      -Value $board.Product         -FilePath $OutFile
    Write-KV -Key "Board Serial"       -Value $board.SerialNumber    -FilePath $OutFile
    Write-KV -Key "BIOS Version"       -Value $bios.SMBIOSBIOSVersion -FilePath $OutFile
    Write-KV -Key "BIOS Date"          -Value $bios.ReleaseDate.ToString("yyyy-MM-dd") -FilePath $OutFile
    Write-KV -Key "BIOS Manufacturer"  -Value $bios.Manufacturer     -FilePath $OutFile
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading board/BIOS info: $_"
}

Write-Host "collect-system.ps1 complete -> $OutFile"
