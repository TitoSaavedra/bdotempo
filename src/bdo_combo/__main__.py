"""Entry point for ``python -m bdo_combo``."""

import sys

from .cli import main

if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="backslashreplace")
    raise SystemExit(main())
