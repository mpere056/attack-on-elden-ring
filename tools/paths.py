"""Where this PC's games are. Kept out of the repository: set the AOTTG2_DIR environment variable,
or put the path of AoTTG2's MainApp folder (the one with Aottg2.exe) on the first line of
.local/aottg2_dir.txt. Elden Ring needs no path: me3 finds it through Steam.
"""
from pathlib import Path
import os
import sys

ROOT = Path(__file__).resolve().parents[1]
LOCAL = ROOT / '.local' / 'aottg2_dir.txt'


def aottg2_dir() -> Path:
    value = os.environ.get('AOTTG2_DIR')
    if not value and LOCAL.exists():
        value = LOCAL.read_text(encoding='utf-8').splitlines()[0].strip()
    if not value:
        sys.exit("AoTTG2's folder is not set. Put the path of the folder containing Aottg2.exe on the first line "
                 f"of {LOCAL}, or set AOTTG2_DIR.")
    path = Path(value)
    if not (path / 'Aottg2.exe').exists():
        sys.exit(f'Aottg2.exe not found in {path} (from AOTTG2_DIR or {LOCAL})')
    return path
