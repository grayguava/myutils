Add-Type -AssemblyName System.Windows.Forms

if ($MyInvocation.MyCommand.Path) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
    $scriptDir = Split-Path -Parent ([System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName)
}
$exiftool = (Get-Command "exiftool.exe" -ErrorAction SilentlyContinue).Source
if (-not $exiftool) {
    $exiftool = Join-Path $scriptDir "exiftool.exe"
}

# --- helpers ---

$script:logLines = @()
$script:logDir   = Join-Path $scriptDir "logs"
$script:logFile  = ""

function Add-Log {
    param([string]$line)
    $script:logLines += $line
}

function Write-Log {
    param([string]$outcome)
    if (!(Test-Path $script:logDir)) {
        New-Item -ItemType Directory -Path $script:logDir | Out-Null
    }
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $script:logFile = Join-Path $script:logDir ("clean_" + $timestamp + ".log")
    $header = @(
        "ExifTool Metadata Clean Log"
        "Timestamp : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Outcome   : $outcome"
        "----------------------------------------"
        ""
    )
    $header + $script:logLines | Set-Content -Path $script:logFile -Encoding UTF8
    $allLogs = Get-ChildItem -Path $script:logDir -Filter "clean_*.log" |
               Sort-Object Name -Descending
    if ($allLogs.Count -gt 10) {
        $allLogs | Select-Object -Skip 10 | Remove-Item -Force -ErrorAction SilentlyContinue
    }
}

$H = [string][char]0x2500

function Exit-Script {
    param([string]$outcome, [switch]$skipLog)
    if ($skipLog) {
        Write-Host "  Cancelled operation."
        exit
    }
    Write-Log -outcome $outcome
    Write-Host ""
    Write-Host (" " + $H * 46) -ForegroundColor DarkGray
    Write-Host ""
    if ($script:logFile -and (Test-Path $script:logFile)) {
        Write-Host "  Detailed log of this run is available at:" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "  $($script:logFile)" -ForegroundColor DarkGray
        Write-Host ""
    }
    Write-Host (" " + $H * 46) -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  Press enter or spacebar to exit." -ForegroundColor DarkGray
    do {
        $key = $host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    } while ($key.VirtualKeyCode -ne 13 -and $key.VirtualKeyCode -ne 32)
    exit
}

function Write-Step {
    param([int]$num, [int]$total, [string]$label, [string]$status)
    Write-Host "  [$num/$total] - $label$status" -ForegroundColor DarkGray
}

# --- check exiftool ---

if (!(Test-Path $exiftool)) {
    Write-Host (" " + $H * 46) -ForegroundColor DarkGray
    Write-Host "  [ERROR] exiftool.exe not found at:" -ForegroundColor Red
    Write-Host "  $exiftool" -ForegroundColor DarkGray
    Add-Log "[ERROR] exiftool.exe not found at: $exiftool"
    Exit-Script -outcome "ABORT: exiftool.exe not found"
}

$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exiftool).FileVersion

# --- UI: header ---

Write-Host ""
Write-Host (" " + [string][char]0x250C + $H + " 🐾 ETSU   |   Clean   |   Exiftool: v$version")
Write-Host (" " + $H * 46) -ForegroundColor DarkGray
Write-Host ""

Add-Log "ExifTool path: $exiftool"
Add-Log "ExifTool version: $version"

# --- file selector prompt ---

$openFiles = Read-Host "  Open file picker? (Y/N)"
if ($openFiles -notmatch "^[Yy]$") {
    Exit-Script -outcome "ABORT: user cancelled" -skipLog
}

$dialog             = New-Object System.Windows.Forms.OpenFileDialog
$dialog.Multiselect = $true
$dialog.Title       = "Select files to strip metadata from"
$dialog.Filter      = "Supported Files|*.jpg;*.jpeg;*.png;*.webp;*.heic;*.tif;*.tiff;*.mp4;*.mov;*.pdf|All Files|*.*"

if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
    Exit-Script -outcome "ABORT: no files selected" -skipLog
}

$files = $dialog.FileNames

Write-Host ""
Write-Host "  Selected files: " -ForegroundColor DarkGray
Write-Host ""
foreach ($file in $files) {
    Write-Host "      $([System.IO.Path]::GetFileName($file))"
}
Add-Log "Files selected ($($files.Count)):"
foreach ($file in $files) { Add-Log "  $file" }

Write-Host ""
Write-Host (" " + $H * 46) -ForegroundColor DarkGray
Write-Host ""

# --- Stage 1: Copy to temp ---

$tempDir = Join-Path $scriptDir ("_exiftool_tmp_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir | Out-Null

Write-Step 1 5 "Copying files to temp workspace..." ""
Add-Log ""
Add-Log "[1/5] Copying files to temp workspace"

$fileMap = @{}
$index = 0
foreach ($file in $files) {
    $fileName = [System.IO.Path]::GetFileName($file)
    $ext      = [System.IO.Path]::GetExtension($file)
    $tempFile = Join-Path $tempDir ($index.ToString() + $ext)
    $index++

    try {
        Copy-Item $file $tempFile -Force -ErrorAction Stop
    } catch {
        Write-Host "  [ABORT] Copy failed for $fileName : $_" -ForegroundColor Red
        Add-Log "[ABORT] Copy failed for $file : $_"
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: copy failed for $fileName"
    }

    $origSize = (Get-Item $file).Length
    $copySize = (Get-Item $tempFile).Length
    if ($origSize -ne $copySize) {
        Write-Host "  [ABORT] Size mismatch for $fileName (orig: $origSize, copy: $copySize)" -ForegroundColor Red
        Add-Log "[ABORT] Size mismatch for $file (orig: $origSize, copy: $copySize)"
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: size mismatch for $fileName"
    }

    $fileMap[$file] = $tempFile
}

Add-Log "All copies verified OK"

# --- Stage 2: Clean metadata ---

Write-Step 2 5 "Cleaning metadata..." ""
Add-Log ""
Add-Log "[2/5] Cleaning metadata"

foreach ($file in $files) {
    $fileName = [System.IO.Path]::GetFileName($file)
    $tempFile = $fileMap[$file]

    $rawOutput = & $exiftool "-all=" "-overwrite_original" "-P" "-v" $tempFile 2>&1
    $exitCode  = $LASTEXITCODE

    if ($exitCode -ne 0) {
        Write-Host "  [ABORT] ExifTool failed on $fileName" -ForegroundColor Red
        Add-Log "[ABORT] ExifTool failed on $file"
        foreach ($line in $rawOutput) {
            if ($line.Trim() -ne "") {
                Write-Host "    $line" -ForegroundColor DarkGray
                Add-Log "  $line"
            }
        }
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: ExifTool failed on $fileName"
    }

    $deleted = $rawOutput |
        Where-Object { $_ -match "^\s+Deleting\s+(.+)$" } |
        ForEach-Object { $matches[1].Trim() }

    Add-Log "  $file"
    if ($deleted.Count -gt 0) {
        foreach ($tag in $deleted) {
            Add-Log "    - $tag"
        }
    } else {
        Add-Log "    (no metadata found)"
    }
}

# --- Stage 3: Verify ---

Write-Step 3 5 "Verifying cleaned files..." ""
Add-Log ""
Add-Log "[3/5] Verifying cleaned files"

foreach ($file in $files) {
    $fileName = [System.IO.Path]::GetFileName($file)
    $tempFile = $fileMap[$file]

    if (!(Test-Path $tempFile)) {
        Write-Host "  [ABORT] Temp file missing: $fileName" -ForegroundColor Red
        Add-Log "[ABORT] Temp file missing after wipe: $file"
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: temp file missing for $fileName"
    }

    if ((Get-Item $tempFile).Length -eq 0) {
        Write-Host "  [ABORT] Temp file is empty: $fileName" -ForegroundColor Red
        Add-Log "[ABORT] Temp file is empty after wipe: $file"
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: temp file empty for $fileName"
    }

    try {
        $stream = [System.IO.File]::OpenRead($tempFile)
        $stream.Close()
    } catch {
        Write-Host "  [ABORT] Temp file unreadable: $fileName" -ForegroundColor Red
        Add-Log "[ABORT] Temp file unreadable after wipe: $file"
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: temp file unreadable for $fileName"
    }
}

Add-Log "All wiped files verified OK"

# --- Stage 4: Swap ---

Write-Step 4 5 "Replacing originals..." ""
Add-Log ""
Add-Log "[4/5] Replacing originals"

$bakFiles = @()

foreach ($file in $files) {
    $fileName = [System.IO.Path]::GetFileName($file)
    $tempFile = $fileMap[$file]
    $bakFile  = $file + ".bak"

    try {
        Rename-Item $file $bakFile -ErrorAction Stop
    } catch {
        Write-Host "  [ABORT] Could not rename original to .bak: $fileName" -ForegroundColor Red
        Add-Log "[ABORT] Could not rename original to .bak: $file"
        foreach ($bak in $bakFiles) {
            $orig = $bak -replace '\.bak$', ''
            Rename-Item $bak $orig -ErrorAction SilentlyContinue
        }
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: could not rename to .bak for $fileName"
    }

    $bakFiles += $bakFile

    try {
        Move-Item $tempFile $file -ErrorAction Stop
    } catch {
        Write-Host "  [ABORT] Could not move temp file to original path: $fileName" -ForegroundColor Red
        Add-Log "[ABORT] Could not move temp file to original path: $file"
        foreach ($bak in $bakFiles) {
            $orig = $bak -replace '\.bak$', ''
            if (!(Test-Path $orig)) {
                Rename-Item $bak $orig -ErrorAction SilentlyContinue
            }
        }
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: could not move temp file for $fileName"
    }

    if (!(Test-Path $file) -or (Get-Item $file).Length -eq 0) {
        Write-Host "  [ABORT] Final file missing or empty after swap: $fileName" -ForegroundColor Red
        Add-Log "[ABORT] Final file missing or empty after swap: $file"
        Remove-Item $tempDir -Recurse -Force
        Exit-Script -outcome "ABORT: final file missing/empty for $fileName"
    }

    Add-Log "  Swapped OK: $file"
}

foreach ($bak in $bakFiles) {
    Remove-Item $bak -Force -ErrorAction SilentlyContinue
}
Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

Add-Log ""
Add-Log "All done. $($files.Count) file(s) cleaned in place."

# --- Stage 5: Done ---

Write-Step 5 5 "Done!" " - $($files.Count) file(s) cleaned in place."

Exit-Script -outcome "SUCCESS"
