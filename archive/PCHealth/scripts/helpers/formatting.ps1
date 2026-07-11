# =============================================================================
# helpers/formatting.ps1
# Formatting helpers used primarily by generate-summary.ps1
# =============================================================================

# ---------------------------------------------------------------------------
# Format a markdown section heading
# ---------------------------------------------------------------------------
function Format-MDHeading {
    param(
        [string]$Text,
        [int]$Level = 2
    )
    $prefix = "#" * $Level
    return "`n$prefix $Text`n"
}

# ---------------------------------------------------------------------------
# Format a simple markdown bullet list from an array of strings
# ---------------------------------------------------------------------------
function Format-MDBullets {
    param([string[]]$Items)
    if (-not $Items -or $Items.Count -eq 0) {
        return "  - None`n"
    }
    return ($Items | ForEach-Object { "  - $_" }) -join "`n"
}

# ---------------------------------------------------------------------------
# Format a markdown table from an array of PSObjects
# Columns are derived from the first object's properties
# ---------------------------------------------------------------------------
function Format-MDTable {
    param([object[]]$Rows)

    if (-not $Rows -or $Rows.Count -eq 0) {
        return "_No data._`n"
    }

    $props = $Rows[0].PSObject.Properties.Name
    $header = "| " + ($props -join " | ") + " |"
    $divider = "| " + (($props | ForEach-Object { "---" }) -join " | ") + " |"
    $body = $Rows | ForEach-Object {
        $row = $_
        "| " + (($props | ForEach-Object { $row.$_ }) -join " | ") + " |"
    }

    return ($header, $divider) + $body | Out-String
}

# ---------------------------------------------------------------------------
# Pad or truncate a string to a fixed width (for aligned plain-text output)
# ---------------------------------------------------------------------------
function Format-Fixed {
    param(
        [string]$Text,
        [int]$Width
    )
    if ($Text.Length -ge $Width) {
        return $Text.Substring(0, $Width)
    }
    return $Text.PadRight($Width)
}

# ---------------------------------------------------------------------------
# Format bytes into a human-readable string (KB / MB / GB)
# ---------------------------------------------------------------------------
function Format-Bytes {
    param([long]$Bytes)
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

# ---------------------------------------------------------------------------
# Return a simple status tag based on a boolean
# ---------------------------------------------------------------------------
function Format-Status {
    param([bool]$OK)
    return if ($OK) { "OK" } else { "ATTENTION" }
}
