"""
dirdiff — Verify a directory copy by comparing filenames, sizes, and SHA256 hashes.

Workflow:
  1. Let user pick source and destination folders (native Windows dialog)
  2. Walk both directories and build a map of all files
  3. Compare file presence (missing / extra files)
  4. Compare file sizes
  5. Compare SHA256 hashes in parallel
  6. Print a report with any discrepancies found

Usage:
    python -m dirdiff          # if run from the src/ parent
    dirdiff                    # if installed via pip
"""

import os
import sys
import subprocess
import hashlib
import concurrent.futures
from pathlib import Path


# ── Folder picker ──────────────────────────────────────────────────────────

class DialogUnavailable(Exception):
    """Raised when the PowerShell folder-picker itself could not be run."""


def browse_folder(title):
    """Open the modern Explorer-style dialog, repurposed for folder selection.

    Returns the selected Path, or None if the user cancelled.
    Raises DialogUnavailable if the picker could not be launched at all.
    """
    ps_command = (
        "Add-Type -AssemblyName System.Windows.Forms; "
        "$d = New-Object System.Windows.Forms.OpenFileDialog; "
        "$d.Title = '" + title.replace("'", "''") + "'; "
        "$d.CheckFileExists = $false; "
        "$d.CheckPathExists = $true; "
        "$d.ValidateNames = $false; "
        "$d.Multiselect = $false; "
        "$d.FileName = 'Select this folder'; "
        "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { "
        "Write-Output (Split-Path $d.FileName -Parent) "
        "}"
    )
    try:
        r = subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps_command],
            capture_output=True, text=True, timeout=120,
        )
    except Exception as e:
        raise DialogUnavailable(str(e))
    if r.returncode != 0 and not r.stdout.strip():
        raise DialogUnavailable(r.stderr.strip().splitlines()[-1] if r.stderr.strip() else "unknown error")
    lines = [line.strip() for line in r.stdout.splitlines() if line.strip()]
    return Path(lines[0]) if lines else None


def get_directory(prompt):
    """Show the folder picker and return the selected Path.

    Exits the program if the user cancels, or if the dialog can't run.
    """
    try:
        p = browse_folder(prompt)
    except DialogUnavailable as e:
        print(f"\n  Folder picker could not be opened ({e}). Exiting.")
        sys.exit(1)

    if p is None:
        print("\n  No folder selected (cancelled). Exiting.")
        sys.exit(1)

    if not p.is_dir():
        print(f"\n  Selected path is not a directory: {p}. Exiting.")
        sys.exit(1)

    return p


# ── Directory scanning ─────────────────────────────────────────────────────

def build_file_map(root):
    """Walk root and return a dict: rel_path -> (abs_path, size)."""
    root = Path(root).resolve()
    file_map = {}
    for dirpath, _dirnames, filenames in os.walk(root, followlinks=False):
        rel_dir = Path(dirpath).relative_to(root)
        if not filenames:
            continue
        for fname in filenames:
            abs_path = Path(dirpath) / fname
            rel_path = rel_dir / fname if str(rel_dir) != '.' else Path(fname)
            try:
                size = abs_path.stat().st_size
                file_map[rel_path] = (abs_path, size)
            except OSError as e:
                print(f"  Warning: cannot stat {abs_path} \u2014 {e}")
    return file_map


# ── Hashing ────────────────────────────────────────────────────────────────

CHUNK_SIZE = 1024 * 1024  # 1 MB

def hash_file(filepath):
    """Return (hex_digest, None) on success, (None, error_msg) on failure."""
    try:
        h = hashlib.sha256()
        with open(filepath, 'rb') as f:
            for chunk in iter(lambda: f.read(CHUNK_SIZE), b''):
                h.update(chunk)
        return h.hexdigest(), None
    except Exception as e:
        return None, str(e)


# ── Output helpers ─────────────────────────────────────────────────────────

HR = "\u2500" * 50

def fmt(n, total):
    """Format a count/percentage like '  957 / 959      ( 99.8%)'."""
    if total == 0:
        return f"{'0':>6} / {'0':<6}      (  N/A  )"
    return f"{n:>6} / {total:<6}      ({n / total * 100:.1f}%)"


def _hash_pair(rel_path, src_abs, dst_abs):
    """Hash both sides of one file pair."""
    return (rel_path,) + hash_file(src_abs) + hash_file(dst_abs)


# ── Main ───────────────────────────────────────────────────────────────────

def main():
    print()
    print("  " + "=" * 48)
    print("  Directory Comparison Report")
    print("  " + "=" * 48)
    print()

    sys.stdout.write("  Source:     ")
    sys.stdout.flush()
    source_root = get_directory("SOURCE directory")
    sys.stdout.write(f"\r  Source:      {source_root}\n")
    sys.stdout.flush()

    sys.stdout.write("  Dest:       ")
    sys.stdout.flush()
    dest_root = get_directory("DESTINATION directory (the copy)")
    sys.stdout.write(f"\r  Dest:        {dest_root}\n")
    sys.stdout.flush()

    print()
    print("  " + HR)
    print()

    print("  Scanning directories...")
    src_map = build_file_map(source_root)
    dst_map = build_file_map(dest_root)

    src_paths = set(src_map.keys())
    dst_paths = set(dst_map.keys())

    in_both        = src_paths & dst_paths
    missing        = sorted(src_paths - dst_paths)
    extra          = sorted(dst_paths - src_paths)
    in_both_sorted = sorted(in_both)

    n_total = len(src_map)

    n_present = len(in_both)
    print()
    print(f"  Files present:   {fmt(n_present, n_total)}")
    print()

    if missing:
        print(f"  Missing files ({len(missing)}):")
        print()
        for i, p in enumerate(missing):
            if i >= 20:
                print(f"    ... and {len(missing) - 20} more")
                break
            print(f"    - {p}")
        print()
    if extra:
        print(f"  Extra files ({len(extra)}):")
        print()
        for i, p in enumerate(extra):
            if i >= 20:
                print(f"    ... and {len(extra) - 20} more")
                break
            print(f"    + {p}")
        print()

    if missing or extra:
        print("  " + HR)
        print()

    size_ok  = 0
    size_bad = []
    for rel_path in in_both_sorted:
        src_size = src_map[rel_path][1]
        dst_size = dst_map[rel_path][1]
        if src_size == dst_size:
            size_ok += 1
        else:
            size_bad.append((rel_path, src_size, dst_size))

    print(f"  Sizes matched:   {fmt(size_ok, n_present)}")
    print()

    if size_bad:
        print(f"  Size mismatches ({len(size_bad)}):")
        for p, s, d in size_bad[:20]:
            print(f"    ! {p}  ({s} vs {d} bytes)")
        if len(size_bad) > 20:
            print(f"    ... and {len(size_bad) - 20} more")
        print()

    print("  " + HR)
    print()

    hash_ok     = 0
    hash_bad    = []
    hash_errors = []

    files = in_both_sorted
    n_hash = len(files)

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        futs = [
            pool.submit(_hash_pair, rp, src_map[rp][0], dst_map[rp][0])
            for rp in files
        ]

        done = 0
        for f in concurrent.futures.as_completed(futs):
            done += 1
            rp, sh, serr, dh, derr = f.result()
            if serr or derr:
                if serr:
                    hash_errors.append(('src', rp, serr))
                if derr:
                    hash_errors.append(('dst', rp, derr))
            elif sh == dh:
                hash_ok += 1
            else:
                hash_bad.append(rp)
            print(f"  Computing SHA256 hashes ({done}/{n_hash})", end='\r')

    print(f"  Computing SHA256 hashes ({n_hash}/{n_hash})")
    print()
    print(f"  Hashes matched:  {fmt(hash_ok, n_hash)}")
    print()

    if hash_bad:
        print(f"  Hash mismatches ({len(hash_bad)}):")
        for p in hash_bad[:20]:
            print(f"    ! {p}")
        if len(hash_bad) > 20:
            print(f"    ... and {len(hash_bad) - 20} more")
        print()

    if hash_errors:
        print(f"  Errors ({len(hash_errors)}):")
        for side, p, msg in hash_errors[:10]:
            print(f"    !! {side}:{p} \u2014 {msg}")
        if len(hash_errors) > 10:
            print(f"    ... and {len(hash_errors) - 10} more")
        print()

    print("  " + HR)
    print()
    n_issues = len(missing) + len(extra) + len(size_bad) + len(hash_bad)
    if n_issues == 0 and not hash_errors:
        print(f"  All {n_total} files verified OK.")
    else:
        print(f"  Issue(s) found:")
        print()
        if missing:     print(f"    - {len(missing)} items missing")
        if extra:       print(f"    + {len(extra)} items extra")
        if size_bad:    print(f"    ! {len(size_bad)} items size mismatch")
        if hash_bad:    print(f"    ! {len(hash_bad)} items hash mismatch")
        if hash_errors: print(f"    !! {len(hash_errors)} items cannot hash")


if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n  Interrupted. Exiting.")
        sys.exit(1)
