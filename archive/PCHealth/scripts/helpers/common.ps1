# =============================================================================
# helpers/common.ps1
# Shared paths, constants, and utility functions used by all collect scripts.
# =============================================================================

# ---------------------------------------------------------------------------
# Root paths (all derived from script location — fully portable)
# ---------------------------------------------------------------------------
$PCHealthRoot   = Resolve-Path "$PSScriptRoot\.."
$ToolsRoot      = Join-Path $PCHealthRoot "tools"
$LogsRoot       = Join-Path $PCHealthRoot "logs"
$BaselineRoot   = Join-Path $PCHealthRoot "baseline"
$SummariesRoot  = Join-Path $PCHealthRoot "summaries"

# ---------------------------------------------------------------------------
# Tool paths
# ---------------------------------------------------------------------------
$LHMExe         = Join-Path $ToolsRoot "LibreHardwareMonitor\LibreHardwareMonitor.exe"
$SmartCtlExe    = Join-Path $ToolsRoot "smartmontools\smartctl.exe"

# ---------------------------------------------------------------------------
# Dated snapshot directory for this run
# Set once by run-weekly.ps1, then dot-sourced by all collect scripts.
# ---------------------------------------------------------------------------
if (-not $global:SnapshotDate) {
    $global:SnapshotDate = Get-Date -Format "yyyy-MM-dd"
}

if (-not $global:SnapshotDir) {
    $global:SnapshotDir = Join-Path $LogsRoot $global:SnapshotDate
}

# ---------------------------------------------------------------------------
# Utility: Write a section header to a log file
# ---------------------------------------------------------------------------
function Write-Section {
    param(
        [string]$Title,
        [string]$FilePath
    )
    $separator = "=" * 72
    $header = @"

$separator
  $Title
  Collected: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
$separator

"@
    Add-Content -Path $FilePath -Value $header
}

# ---------------------------------------------------------------------------
# Utility: Ensure the snapshot directory exists
# ---------------------------------------------------------------------------
function Ensure-SnapshotDir {
    if (-not (Test-Path $global:SnapshotDir)) {
        New-Item -ItemType Directory -Path $global:SnapshotDir -Force | Out-Null
    }
}

# ---------------------------------------------------------------------------
# Utility: Check if a tool exe exists, warn and return $false if not
# ---------------------------------------------------------------------------
function Assert-Tool {
    param(
        [string]$ExePath,
        [string]$FriendlyName
    )
    if (-not (Test-Path $ExePath)) {
        Write-Warning "$FriendlyName not found at: $ExePath"
        Write-Warning "Skipping $FriendlyName collection."
        return $false
    }
    return $true
}

# ---------------------------------------------------------------------------
# Utility: Append a key-value pair as a readable line
# ---------------------------------------------------------------------------
function Write-KV {
    param(
        [string]$Key,
        [string]$Value,
        [string]$FilePath
    )
    Add-Content -Path $FilePath -Value ("  {0,-35} {1}" -f "$Key :", $Value)
}
