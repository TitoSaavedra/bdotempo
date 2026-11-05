"""The inputs of the real pack: every skill icon, the class icons, the JSON bundle and the slot art.

To add an asset (for example the cooldown or quick slot art of the next task), add one ``SlotArt`` line to
``SLOT_ART`` or one more rule to ``collect_inputs``. Keys must match the names the C# reader asks for
(``skill-<no>.png``, ``class-<type>.png``, ``catalog.json``, ``skills.json``, ``guide-<key>.json``,
``guide-texts-es.json``). Every ``*.json`` of the staging folder is packed as it is, so a new JSON document needs no code here.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

from .. import log
from .format import AssetPackError
from .writer import PackInput

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
SKILL_ICON_RE = re.compile(r"^(\d+)\.png$")
CLASS_ICON_RE = re.compile(r"^class_(\d+)\.png$")


@dataclass(frozen=True)
class SlotArt:
    """A UI image from the client export, stored in the pack under ``key``. ``source`` is relative to the client folder."""

    key: str
    source: str


SLOT_ART: tuple[SlotArt, ...] = (
    SlotArt("slot-cooldown-bg.png", "ui_probe/slot_art/cooldown_slot_bg.png"),
    SlotArt("slot-cooldown-radial-mask.png", "ui_probe/slot_art/cooldown_candidate_radial_mask.png"),
    SlotArt("slot-quickslot-bg.png", "ui_probe/slot_art/quickslot_bg.png"),
    SlotArt("slot-quickslot-handle.png", "ui_probe/slot_art/quickslot_handle.png"),
)


def collect_inputs(client_dir: Path, staging_dir: Path) -> list[PackInput]:
    """Reads the pack inputs from the client export and the JSON staging folder. Refuses anything it cannot name."""
    with log.span("assetpack.collect", client_dir=str(client_dir), staging_dir=str(staging_dir)):
        inputs: list[PackInput] = []
        inputs += _skill_icons(client_dir / "icons" / "skills")
        inputs += _class_icons(client_dir / "icons" / "classes")
        inputs += _json_bundle(staging_dir)
        inputs += _slot_art(client_dir)
        log.info("assetpack inputs collected", entries=len(inputs), data_bytes=sum(len(item.data) for item in inputs))
        return inputs


def _skill_icons(folder: Path) -> list[PackInput]:
    files = _png_files(folder, SKILL_ICON_RE)
    return [PackInput(f"skill-{match.group(1)}.png", _read_png(path)) for path, match in files]


def _class_icons(folder: Path) -> list[PackInput]:
    files = _png_files(folder, CLASS_ICON_RE)
    if len(files) != 32:
        log.warn("expected 32 class icons", found=len(files))
    return [PackInput(f"class-{match.group(1)}.png", _read_png(path)) for path, match in files]


def _json_bundle(staging_dir: Path) -> list[PackInput]:
    if not staging_dir.is_dir():
        raise AssetPackError(f"staging folder not found: {staging_dir}; run build-assets first")
    names = ("catalog.json", "skills.json", "skill_classes.json", "skill-table.json")
    for name in names:
        if not (staging_dir / name).is_file():
            raise AssetPackError(f"{name} not found in {staging_dir}; run build-assets first")
    paths = sorted(staging_dir.glob("*.json"))
    return [PackInput(path.name, path.read_bytes()) for path in paths]


def _slot_art(client_dir: Path) -> list[PackInput]:
    inputs: list[PackInput] = []
    for art in SLOT_ART:
        path = client_dir / art.source
        if not path.is_file():
            raise AssetPackError(f"slot art not found: {path}")
        inputs.append(PackInput(art.key, _read_png(path)))
    return inputs


def _png_files(folder: Path, pattern: re.Pattern[str]) -> list[tuple[Path, re.Match[str]]]:
    if not folder.is_dir():
        raise AssetPackError(f"client folder not found: {folder}")
    matches: list[tuple[Path, re.Match[str]]] = []
    for path in folder.iterdir():
        match = pattern.match(path.name)
        if match is not None and path.is_file():
            matches.append((path, match))
    return sorted(matches, key=lambda item: item[0].name)


def _read_png(path: Path) -> bytes:
    data = path.read_bytes()
    if not data.startswith(PNG_SIGNATURE):
        raise AssetPackError(f"{path.name} is not a PNG file")
    return data
