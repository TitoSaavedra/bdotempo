"""Render the app icon from its SVG source.

Reads app/assets/icon/app-icon.svg and writes, deterministically:
  - app/assets/icon/app-icon.ico   multi-size ICO, every frame a PNG (16, 24, 32, 48, 64, 128, 256)
  - app/assets/icon/app-icon-<N>.png  PNG exports (256, 128, 48, 32, 16)

Sizes up to 32 px use the reduced design: the "detail" groups (outlined slots, spark) are dropped and the
arrow is drawn heavier so it stays readable. Larger sizes use the full design.

The rasterizer is resvg-py (no native cairo needed on Windows). Its output is deterministic for the same input.
Run with: mise run build-icon
"""

from __future__ import annotations

import re
import struct
import sys
from pathlib import Path

import resvg_py

ICON_DIR = Path(__file__).resolve().parents[1] / "assets" / "icon"
SVG_PATH = ICON_DIR / "app-icon.svg"
ICO_PATH = ICON_DIR / "app-icon.ico"

ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)
PNG_SIZES = (256, 128, 48, 32, 16)
REDUCED_MAX_SIZE = 32  # sizes at or below this use the reduced design

ARROW_FULL_WIDTH = 'stroke-width="14"'
ARROW_REDUCED_WIDTH = 'stroke-width="26"'


def full_design(svg: str) -> str:
    return svg


def reduced_design(svg: str) -> str:
    without_detail = re.sub(r'<g id="detail[^"]*">.*?</g>', "", svg, flags=re.DOTALL)
    heavier_arrow, count = re.subn(
        r'(<g id="arrow"[^>]*?)' + re.escape(ARROW_FULL_WIDTH),
        lambda match: match.group(1) + ARROW_REDUCED_WIDTH,
        without_detail,
        count=1,
    )
    if count != 1:
        raise ValueError("app-icon.svg: the arrow group must declare " + ARROW_FULL_WIDTH)
    return heavier_arrow


def render_png(svg: str, size: int) -> bytes:
    return resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size)


def render_for_size(svg: str, size: int) -> bytes:
    design = reduced_design(svg) if size <= REDUCED_MAX_SIZE else full_design(svg)
    return render_png(design, size)


def build_ico(frames: list[tuple[int, bytes]]) -> bytes:
    """ICO container with PNG frames. Width/height 256 are stored as 0, as the format requires."""
    frames = sorted(frames, key=lambda frame: frame[0])
    header = struct.pack("<HHH", 0, 1, len(frames))
    directory = bytearray()
    blobs = bytearray()
    offset = len(header) + 16 * len(frames)
    for size, png in frames:
        stored = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", stored, stored, 0, 0, 1, 32, len(png), offset)
        blobs += png
        offset += len(png)
    return header + bytes(directory) + bytes(blobs)


def main() -> int:
    svg = SVG_PATH.read_text(encoding="utf-8")

    frames: list[tuple[int, bytes]] = []
    for size in ICO_SIZES:
        frames.append((size, render_for_size(svg, size)))
    ICO_PATH.write_bytes(build_ico(frames))
    print(f"wrote {ICO_PATH.name} ({len(frames)} frames: {', '.join(str(s) for s, _ in frames)})")

    for size in PNG_SIZES:
        target = ICON_DIR / f"app-icon-{size}.png"
        target.write_bytes(render_for_size(svg, size))
        print(f"wrote {target.name}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
