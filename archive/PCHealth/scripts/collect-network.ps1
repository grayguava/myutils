# =============================================================================
# collect-network.ps1
# Collects a minimal network snapshot: adapter config, active connections,
# listening ports, ARP table, and routing table.
#
# Goal: identify unexpected listeners and persistence patterns.
# NOT for obsessive outbound connection tracking.
#
# Output: network.txt in the dated snapshot directory.
# =============================================================================

. "$PSScriptRoot\helpers\common.ps1"

Ensure-SnapshotDir

$OutFile = Join-Path $global:SnapshotDir "network.txt"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Set-Content -Path $OutFile -Value "PCHealth — Network Snapshot"
Add-Content -Path $OutFile -Value "Collected : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Content -Path $OutFile -Value "Host      : $env:COMPUTERNAME"
Add-Content -Path $OutFile -Value ""
Add-Content -Path $OutFile -Value "NOTE: This is a point-in-time snapshot."
Add-Content -Path $OutFile -Value "      Look for persistent anomalies across weeks, not random transient connections."
Add-Content -Path $OutFile -Value "      Modern OSes constantly contact CDNs, telemetry, update, and sync endpoints."
Add-Content -Path $OutFile -Value ""

# ---------------------------------------------------------------------------
# Adapter configuration
# ---------------------------------------------------------------------------
Write-Section -Title "ADAPTER CONFIGURATION (ipconfig /all)" -FilePath $OutFile

try {
    $ipconfigOutput = ipconfig /all 2>&1
    $ipconfigOutput | ForEach-Object { Add-Content -Path $OutFile -Value "  $_" }
} catch {
    Add-Content -Path $OutFile -Value "ERROR running ipconfig: $_"
}

# ---------------------------------------------------------------------------
# Active connections and listening ports
# ---------------------------------------------------------------------------
Write-Section -Title "ACTIVE CONNECTIONS & LISTENING PORTS (netstat -ano)" -FilePath $OutFile

Add-Content -Path $OutFile -Value "  Focus: LISTENING entries and ESTABLISHED connections to non-obvious hosts."
Add-Content -Path $OutFile -Value ""

try {
    $netstatRaw = netstat -ano 2>&1
    $netstatRaw | ForEach-Object { Add-Content -Path $OutFile -Value "  $_" }
} catch {
    Add-Content -Path $OutFile -Value "ERROR running netstat: $_"
}

# ---------------------------------------------------------------------------
# Listening ports with process names — more actionable than raw PIDs
# ---------------------------------------------------------------------------
Write-Section -Title "LISTENING PORTS WITH PROCESS NAMES" -FilePath $OutFile

try {
    $listeners = Get-NetTCPConnection -State Listen -ErrorAction Stop |
        Sort-Object LocalPort |
        ForEach-Object {
            $proc = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
            [PSCustomObject]@{
                LocalAddress = $_.LocalAddress
                LocalPort    = $_.LocalPort
                PID          = $_.OwningProcess
                ProcessName  = if ($proc) { $proc.Name } else { "Unknown" }
                ProcessPath  = if ($proc) { $proc.Path } else { "N/A" }
            }
        }

    if ($listeners) {
        Add-Content -Path $OutFile -Value ("  {0,-22} {1,-8} {2,-8} {3,-25} {4}" -f "Address","Port","PID","Process","Path")
        Add-Content -Path $OutFile -Value ("  {0}" -f ("-" * 100))
        foreach ($l in $listeners) {
            Add-Content -Path $OutFile -Value ("  {0,-22} {1,-8} {2,-8} {3,-25} {4}" -f `
                $l.LocalAddress, $l.LocalPort, $l.PID, $l.ProcessName, $l.ProcessPath)
        }
    } else {
        Add-Content -Path $OutFile -Value "  No listening TCP connections found."
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading TCP listeners: $_"
}

# ---------------------------------------------------------------------------
# UDP listeners (also worth tracking)
# ---------------------------------------------------------------------------
Write-Section -Title "UDP LISTENERS WITH PROCESS NAMES" -FilePath $OutFile

try {
    $udpListeners = Get-NetUDPEndpoint -ErrorAction Stop |
        Sort-Object LocalPort |
        ForEach-Object {
            $proc = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
            [PSCustomObject]@{
                LocalAddress = $_.LocalAddress
                LocalPort    = $_.LocalPort
                PID          = $_.OwningProcess
                ProcessName  = if ($proc) { $proc.Name } else { "Unknown" }
            }
        }

    if ($udpListeners) {
        Add-Content -Path $OutFile -Value ("  {0,-22} {1,-8} {2,-8} {3}" -f "Address","Port","PID","Process")
        Add-Content -Path $OutFile -Value ("  {0}" -f ("-" * 60))
        foreach ($u in $udpListeners) {
            Add-Content -Path $OutFile -Value ("  {0,-22} {1,-8} {2,-8} {3}" -f `
                $u.LocalAddress, $u.LocalPort, $u.PID, $u.ProcessName)
        }
    } else {
        Add-Content -Path $OutFile -Value "  No UDP endpoints found."
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading UDP endpoints: $_"
}

# ---------------------------------------------------------------------------
# ARP table
# ---------------------------------------------------------------------------
Write-Section -Title "ARP TABLE (arp -a)" -FilePath $OutFile

try {
    $arpOutput = arp -a 2>&1
    $arpOutput | ForEach-Object { Add-Content -Path $OutFile -Value "  $_" }
} catch {
    Add-Content -Path $OutFile -Value "ERROR running arp: $_"
}

# ---------------------------------------------------------------------------
# Routing table
# ---------------------------------------------------------------------------
Write-Section -Title "ROUTING TABLE (route print)" -FilePath $OutFile

try {
    $routeOutput = route print 2>&1
    $routeOutput | ForEach-Object { Add-Content -Path $OutFile -Value "  $_" }
} catch {
    Add-Content -Path $OutFile -Value "ERROR running route print: $_"
}

# ---------------------------------------------------------------------------
# DNS client cache (quick anomaly check)
# ---------------------------------------------------------------------------
Write-Section -Title "DNS CLIENT CACHE (top 50 entries)" -FilePath $OutFile

try {
    $dnsCache = Get-DnsClientCache -ErrorAction Stop |
        Sort-Object Entry |
        Select-Object -First 50

    if ($dnsCache) {
        Add-Content -Path $OutFile -Value ("  {0,-50} {1,-10} {2}" -f "Entry","Type","Data")
        Add-Content -Path $OutFile -Value ("  {0}" -f ("-" * 80))
        foreach ($d in $dnsCache) {
            Add-Content -Path $OutFile -Value ("  {0,-50} {1,-10} {2}" -f $d.Entry, $d.Type, $d.Data)
        }
    } else {
        Add-Content -Path $OutFile -Value "  DNS cache is empty or could not be read."
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading DNS cache: $_"
}

# ---------------------------------------------------------------------------
# Network adapters summary (cleaner than ipconfig for baseline comparison)
# ---------------------------------------------------------------------------
Write-Section -Title "NETWORK ADAPTERS SUMMARY" -FilePath $OutFile

try {
    $adapters = Get-NetAdapter | Sort-Object Name
    foreach ($a in $adapters) {
        Add-Content -Path $OutFile -Value ""
        Write-KV -Key "Name"         -Value $a.Name             -FilePath $OutFile
        Write-KV -Key "Description"  -Value $a.InterfaceDescription -FilePath $OutFile
        Write-KV -Key "MAC Address"  -Value $a.MacAddress        -FilePath $OutFile
        Write-KV -Key "Status"       -Value $a.Status            -FilePath $OutFile
        Write-KV -Key "Link Speed"   -Value $a.LinkSpeed         -FilePath $OutFile
        Write-KV -Key "Media Type"   -Value $a.MediaType         -FilePath $OutFile
    }
} catch {
    Add-Content -Path $OutFile -Value "ERROR reading adapter summary: $_"
}

Write-Host "collect-network.ps1 complete -> $OutFile"
