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

$H = [string][char]0x2500
$paw = [char]::ConvertFromUtf32(0x1F43E)

if (!(Test-Path $exiftool)) {
    Write-Host (" " + $H * 46) -ForegroundColor DarkGray
    Write-Host "  [ERROR] exiftool.exe not found." -ForegroundColor Red
    Write-Host "  Place it in $scriptDir or add to PATH." -ForegroundColor DarkGray
    Write-Host (" " + $H * 46) -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  Press enter or spacebar to exit." -ForegroundColor DarkGray
    do {
        $key = $host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    } while ($key.VirtualKeyCode -ne 13 -and $key.VirtualKeyCode -ne 32)
    exit
}

$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exiftool).FileVersion

Write-Host ""
Write-Host (" " + [string][char]0x250C + $H + " $paw ETSU   |   Read   |   Exiftool: v$version")
Write-Host (" " + $H * 46) -ForegroundColor DarkGray
Write-Host ""

$openFiles = Read-Host "  Open file picker? (Y/N)"
if ($openFiles -notmatch "^[Yy]$") {
    Write-Host "  Cancelled operation."
    exit
}

$dialog             = New-Object System.Windows.Forms.OpenFileDialog
$dialog.Multiselect = $false
$dialog.Title       = "Select a file to read metadata from"
$dialog.Filter      = "Supported Files|*.jpg;*.jpeg;*.png;*.webp;*.heic;*.tif;*.tiff;*.mp4;*.mov;*.pdf|All Files|*.*"

if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
    Write-Host "  Cancelled operation."
    exit
}

$file = $dialog.FileName

Write-Host "  Selected: $([System.IO.Path]::GetFileName($file))" -ForegroundColor DarkGray
Write-Host (" " + $H * 46) -ForegroundColor DarkGray
Write-Host ""

$rawOutput = & $exiftool $file 2>&1
foreach ($line in $rawOutput) {
    $parts = $line -split ': ', 2
    if ($parts.Count -eq 2) {
        Write-Host "  $($parts[0]): " -NoNewline
        Write-Host $parts[1] -ForegroundColor DarkGray
    } else {
        Write-Host "  $line"
    }
}

Write-Host ""
Write-Host (" " + $H * 46) -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Press enter or spacebar to exit." -ForegroundColor DarkGray
do {
    $key = $host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
} while ($key.VirtualKeyCode -ne 13 -and $key.VirtualKeyCode -ne 32)
