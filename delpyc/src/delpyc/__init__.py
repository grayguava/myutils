import shutil
import os
from pathlib import Path


def find_pycache_dirs(root_path: str = "."):
    root = Path(root_path).resolve()
    pycache_dirs = []

    for dirpath, dirnames, _ in os.walk(root):
        if "__pycache__" in dirnames:
            pycache_path = Path(dirpath) / "__pycache__"
            pycache_dirs.append(pycache_path)

    return pycache_dirs


def delete_dirs(dirs: list) -> int:
    deleted = 0
    for dir_path in dirs:
        try:
            shutil.rmtree(dir_path)
            if not dir_path.exists():
                deleted += 1
        except Exception:
            pass
    return deleted