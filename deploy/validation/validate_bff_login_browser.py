#!/usr/bin/env python3
"""Run the repository's installed-browser gate with private inherited credentials."""
import os
from pathlib import Path
import subprocess
import sys

if __name__ == '__main__':
    assert len(sys.argv) == 2 and os.environ.get('VALIDATION_USERNAME') and os.environ.get('VALIDATION_PASSWORD')
    script = Path(__file__).with_suffix('.mjs')
    raise SystemExit(subprocess.run(['node', str(script), sys.argv[1]], check=False).returncode)
