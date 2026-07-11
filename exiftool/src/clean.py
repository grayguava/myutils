import os
import sys
import uuid
import shutil
import subprocess
from datetime import datetime

# ---------------------------
# Setup
# ---------------------------

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
LOG_DIR = os.path.join(SCRIPT_DIR, "logs", "clean")

# ANSI colors (Windows 10+ consoles support these natively)
CYAN = "\033[96m"
GRAY = "\033[90m"
RED = "\033[91m"
RESET = "\033[0m"

log_lines = []


def c(text, color):
    return f"{color}{text}{RESET}"


def add_log(line):
    log_lines.append(line)


def write_log(outcome):
    os.makedirs(LOG_DIR, exist_ok=True)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    log_file = os.path.join(LOG_DIR, f"clean_{timestamp}.log")

    header = [
        "ExifTool Metadata Clean Log",
        f"Timestamp : {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}",
        f"Outcome   : {outcome}",
        "----------------------------------------",
        "",
    ]

    with open(log_file, "w", encoding="utf-8") as f:
        f.write("\n".join(header + log_lines) + "\n")

    # Rotate: keep only 10 most recent logs
    all_logs = sorted(
        (f for f in os.listdir(LOG_DIR) if f.startswith("clean_") and f.endswith(".log")),
        reverse=True,
    )
    for old in all_logs[10:]:
        try:
            os.remove(os.path.join(LOG_DIR, old))
        except OSError:
            pass


def wait_any_key():
    print(c("   Press Enter or Space to exit...", GRAY))
    if os.name == "nt":
        import msvcrt
        while True:
            key = msvcrt.getch()
            if key in (b"\r", b" "):
                break
    else:
        input()


def exit_script(outcome):
    write_log(outcome)
    print()
    wait_any_key()
    sys.exit()


def divider():
    print(c("-----------------------------------------------", GRAY))


def pick_files_dialog():
    """Open Windows' native multi-select file dialog via PowerShell.
    Returns a list of selected file paths, or [] if cancelled."""
    ps_command = (
        "Add-Type -AssemblyName System.Windows.Forms; "
        "$d = New-Object System.Windows.Forms.OpenFileDialog; "
        "$d.Multiselect = $true; "
        "$d.Title = 'Select files to strip metadata from'; "
        "$d.Filter = "
        "'Supported Files|*.jpg;*.jpeg;*.png;*.webp;*.heic;*.tif;*.tiff;*.mp4;*.mov;*.pdf|All Files|*.*'; "
        "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { "
        "$d.FileNames | ForEach-Object { Write-Output $_ } "
        "}"
    )
    result = subprocess.run(
        ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_command],
        capture_output=True,
        text=True,
    )
    lines = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    return lines


# ---------------------------
# Header
# ---------------------------

print()
print(c("=========== ExifTool by Phil Harvey ==========", CYAN))
print()
print(c("   Loading ExifTool...", GRAY))

try:
    result = subprocess.run(
        ["exiftool", "-ver"],
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip())
    version = result.stdout.strip()
except (FileNotFoundError, RuntimeError) as e:
    print(c("   [ERROR] exiftool not found on PATH", RED))
    add_log(f"[ERROR] exiftool not found on PATH: {e}")
    exit_script("ABORT: exiftool not found on PATH")

print(c(f"   Found ExifTool {version}", GRAY))
add_log(f"ExifTool version: {version}")
print()
divider()
print()

# ---------------------------
# File selector
# ---------------------------

open_files = input("   Open file selector? (y/n): ").strip()
if open_files.lower() != "y":
    print()
    print(c("   Cancelled.", GRAY))
    add_log("User cancelled at file selector prompt.")
    exit_script("ABORT: user cancelled")

files = pick_files_dialog()

if not files:
    print()
    print(c("   No files selected.", GRAY))
    add_log("No files selected in dialog.")
    exit_script("ABORT: no files selected")

print()
print(c(f"   Opened {len(files)} file(s):", "\033[92m"))
add_log(f"Files selected ({len(files)}):")
for file in files:
    print(c(f"   {os.path.basename(file)}", GRAY))
    add_log(f"  {file}")

print()
divider()
print()

# ---------------------------
# Stage 1: Copy to temp workspace
# ---------------------------

temp_dir = os.path.join(SCRIPT_DIR, f"_exiftool_tmp_{uuid.uuid4().hex}")
os.makedirs(temp_dir)

print(c("   [1/4] Copying files to temp workspace...", GRAY))
add_log("")
add_log("[1/4] Copying files to temp workspace")

file_map = {}

for index, file in enumerate(files):
    file_name = os.path.basename(file)
    ext = os.path.splitext(file)[1]
    temp_file = os.path.join(temp_dir, f"{index}{ext}")

    try:
        shutil.copy2(file, temp_file)
    except OSError as e:
        print(c(f"   [ABORT] Copy failed for {file_name} : {e}", RED))
        add_log(f"[ABORT] Copy failed for {file} : {e}")
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: copy failed for {file_name}")

    orig_size = os.path.getsize(file)
    copy_size = os.path.getsize(temp_file)
    if orig_size != copy_size:
        print(c(f"   [ABORT] Size mismatch for {file_name} (orig: {orig_size}, copy: {copy_size})", RED))
        add_log(f"[ABORT] Size mismatch for {file} (orig: {orig_size}, copy: {copy_size})")
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: size mismatch for {file_name}")

    file_map[file] = temp_file

print(c("   OK - all copies verified", GRAY))
add_log("All copies verified OK")
print()
divider()
print()

# ---------------------------
# Stage 2: Wipe metadata on temp copies
# ---------------------------

print(c("   [2/4] Wiping metadata...", GRAY))
print()
add_log("")
add_log("[2/4] Wiping metadata")

for file in files:
    file_name = os.path.basename(file)
    temp_file = file_map[file]

    result = subprocess.run(
        ["exiftool", "-all=", "-overwrite_original", "-P", "-v", temp_file],
        capture_output=True,
        text=True,
    )
    raw_output = (result.stdout + result.stderr).splitlines()

    if result.returncode != 0:
        print(c(f"   [ABORT] ExifTool failed on {file_name}", RED))
        add_log(f"[ABORT] ExifTool failed on {file}")
        for line in raw_output:
            if line.strip():
                print(c(f"   {line}", GRAY))
                add_log(f"  {line}")
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: ExifTool failed on {file_name}")

    deleted = []
    for line in raw_output:
        stripped = line.strip()
        if stripped.startswith("Deleting "):
            deleted.append(stripped[len("Deleting "):].strip())

    add_log(f"  {file}")
    if deleted:
        for tag in deleted:
            print(c(f"   - {tag}", GRAY))
            add_log(f"    - {tag}")
    else:
        print(c(f"   {file_name} - (no metadata found)", GRAY))
        add_log("    (no metadata found)")

print()
divider()
print()

# ---------------------------
# Stage 3: Verify temp files
# ---------------------------

print(c("   [3/4] Verifying wiped files...", GRAY))
add_log("")
add_log("[3/4] Verifying wiped files")

for file in files:
    file_name = os.path.basename(file)
    temp_file = file_map[file]

    if not os.path.exists(temp_file):
        print(c(f"   [ABORT] Temp file missing: {file_name}", RED))
        add_log(f"[ABORT] Temp file missing after wipe: {file}")
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: temp file missing for {file_name}")

    if os.path.getsize(temp_file) == 0:
        print(c(f"   [ABORT] Temp file is empty: {file_name}", RED))
        add_log(f"[ABORT] Temp file is empty after wipe: {file}")
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: temp file empty for {file_name}")

    try:
        with open(temp_file, "rb") as f:
            f.read(1)
    except OSError:
        print(c(f"   [ABORT] Temp file unreadable: {file_name}", RED))
        add_log(f"[ABORT] Temp file unreadable after wipe: {file}")
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: temp file unreadable for {file_name}")

print(c("   OK - all files verified clean", GRAY))
add_log("All wiped files verified OK")
print()
divider()
print()

# ---------------------------
# Stage 4: Swap — rename original to .bak, move temp to original path
# ---------------------------

print(c("   [4/4] Replacing originals...", GRAY))
add_log("")
add_log("[4/4] Replacing originals")

bak_files = []


def restore_baks():
    for bak in bak_files:
        orig = bak[:-4] if bak.endswith(".bak") else bak
        if not os.path.exists(orig):
            try:
                os.rename(bak, orig)
            except OSError:
                pass


for file in files:
    file_name = os.path.basename(file)
    temp_file = file_map[file]
    bak_file = file + ".bak"

    try:
        os.rename(file, bak_file)
    except OSError as e:
        print(c(f"   [ABORT] Could not rename original to .bak: {file_name}", RED))
        add_log(f"[ABORT] Could not rename original to .bak: {file} : {e}")
        restore_baks()
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: could not rename to .bak for {file_name}")

    bak_files.append(bak_file)

    try:
        shutil.move(temp_file, file)
    except OSError as e:
        print(c(f"   [ABORT] Could not move temp file to original path: {file_name}", RED))
        add_log(f"[ABORT] Could not move temp file to original path: {file} : {e}")
        restore_baks()
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: could not move temp file for {file_name}")

    if not os.path.exists(file) or os.path.getsize(file) == 0:
        print(c(f"   [ABORT] Final file missing or empty after swap: {file_name}", RED))
        add_log(f"[ABORT] Final file missing or empty after swap: {file}")
        shutil.rmtree(temp_dir, ignore_errors=True)
        exit_script(f"ABORT: final file missing/empty for {file_name}")

    add_log(f"  Swapped OK: {file}")

# All swaps succeeded - delete .bak files
for bak in bak_files:
    try:
        os.remove(bak)
    except OSError:
        pass

# Clean up temp dir
shutil.rmtree(temp_dir, ignore_errors=True)

# ---------------------------
# Summary
# ---------------------------

print(c("   OK", GRAY))
print()
divider()
print()
print(c(f"   All done! {len(files)} file(s) cleaned in place.", CYAN))

add_log("")
add_log(f"All done. {len(files)} file(s) cleaned in place.")

exit_script("SUCCESS")