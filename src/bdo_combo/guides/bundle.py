"""Builds the staging folder of the asset pack: only what the combo guides reference, plus the skill class table.

Output folder, the JSON staging of the asset pack (flat: each file becomes one pack entry with the same name):

* ``catalog.json``: the combo catalog (copy of ``<combos>/catalog.json``).
* ``guide-<class>.<spec>.json``: one per guide, copied from ``<combos>/guides``.
* ``skills.json``: ``{"<skillNo>": {"nameEn", "nameEs", "descriptionEs", "tooltip"?}}`` for the referenced skills.
  Descriptions are plain text with line breaks (game markup removed). ``tooltip`` is optional: it is written
  for every skill that ``<combos>/tooltips.json`` has (cooldown, cost, damage, PvP and features).
* ``skill-<skillNo>.png``: the skill icons, copied as they are (the client icons are 40 to 50 px).
* ``class-<classType>.png``: the 32 class icons.
* ``skill_classes.json``: skill number -> classes that can use it (see ``skill_classes.py``), when a skill table is given.
* ``guide-texts-es.json``: the Spanish text of the English guide prose (``{english: spanish}``), copied from
  ``guide_texts_es.json`` next to this file. Optional: without it the app shows the guides in English.
* ``slot-quickslot-bg.png`` and ``slot-cooldown-sweep.png``: the slot art of the game UI, copied from the client's
  ``ui_probe/slot_art`` folder when a slot art folder is given. Their sizes are checked exactly (44 x 44 and 520 x 520).
  A missing file is reported and left out: the app then draws no background and the procedural sweep.

Icons larger than 64 px are refused: there is no image library in the standard library to
resize them. A referenced skill without an icon in the client export is reported and left out;
the app then draws a glyph for it.
"""

from __future__ import annotations

import json
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .. import log
from .client import ClientExport, clean_description, clean_name
from .importer import TOOLTIPS_FILE, iter_skill_refs
from .skill_classes import RESOURCE_NAME as SKILL_CLASSES_RESOURCE
from .skill_classes import build_skill_class_table

CATALOG_RESOURCE = "catalog.json"
SKILLS_RESOURCE = "skills.json"
GUIDE_TEXTS_RESOURCE = "guide-texts-es.json"
GUIDE_TEXTS_SOURCE = Path(__file__).with_name("guide_texts_es.json")
ICON_MAX_SIDE = 64
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"

# Slot art: bundle name -> (file in the client's slot_art folder, the exact size the app expects).
SLOT_ART: dict[str, tuple[str, tuple[int, int]]] = {
    "slot-quickslot-bg.png": ("quickslot_bg.png", (44, 44)),
    "slot-cooldown-sweep.png": ("cooldown_candidate_radial_mask.png", (520, 520)),
}


class BundleError(ValueError):
    """The bundle cannot be built: a required file is missing, or an icon is not usable."""


@dataclass
class BundleStats:
    files: int = 0
    total_bytes: int = 0
    skill_icons: int = 0
    class_icons: int = 0
    guides: int = 0
    skills_described: int = 0
    skills_with_tooltip: int = 0
    missing_skill_icons: list[int] = field(default_factory=list)
    slot_art: int = 0
    missing_slot_art: list[str] = field(default_factory=list)
    guide_texts: int = 0


def guide_resource(key: str) -> str:
    return f"guide-{key}.json"


def skill_resource(skill_no: int) -> str:
    return f"skill-{skill_no}.png"


def class_resource(class_type: int) -> str:
    return f"class-{class_type}.png"


def build_bundle(
    combos_dir: Path,
    client: ClientExport,
    out_dir: Path,
    skill_table: dict[str, Any] | None = None,
    slot_art_dir: Path | None = None,
) -> BundleStats:
    with log.span("guides.bundle", combos_dir=str(combos_dir), out_dir=str(out_dir), skill_table=skill_table is not None):
        catalog = _read_json(combos_dir / "catalog.json")
        guides = {
            path.stem: _read_json(path)
            for path in sorted((combos_dir / "guides").glob("*.json"))
        }
        _check_catalog_guides(catalog, guides)

        out_dir.mkdir(parents=True, exist_ok=True)
        _remove_stale(out_dir)
        stats = BundleStats()

        _write_json(out_dir / CATALOG_RESOURCE, catalog, stats)
        for key, guide in guides.items():
            _write_json(out_dir / guide_resource(key), guide, stats)
            stats.guides += 1

        skill_refs = _collect_skill_refs(guides)
        tooltips = _read_optional_tooltips(combos_dir)
        _write_skills_lookup(out_dir, skill_refs, client, stats, tooltips)
        _copy_skill_icons(out_dir, sorted(skill_refs), client, stats)
        _copy_class_icons(out_dir, client, stats)
        if slot_art_dir is not None:
            _copy_slot_art(out_dir, slot_art_dir, stats)
        if skill_table is not None:
            _write_json(out_dir / SKILL_CLASSES_RESOURCE, build_skill_class_table(skill_table), stats)
        _write_guide_texts(out_dir, stats)

        log.info(
            "asset bundle written",
            files=stats.files,
            total_bytes=stats.total_bytes,
            skill_icons=stats.skill_icons,
            class_icons=stats.class_icons,
            guides=stats.guides,
            missing_skill_icons=len(stats.missing_skill_icons),
            slot_art=stats.slot_art,
            missing_slot_art=len(stats.missing_slot_art),
            guide_texts=stats.guide_texts,
        )
        return stats


def _check_catalog_guides(catalog: dict[str, Any], guides: dict[str, dict[str, Any]]) -> None:
    for entry in catalog["classes"]:
        for spec in entry["specs"]:
            if spec["guide"] not in guides:
                raise BundleError(f"catalog guide {spec['guide']} has no file; run import-guides first")


def _collect_skill_refs(guides: dict[str, dict[str, Any]]) -> dict[int, dict[str, Any]]:
    """skillNo -> the first reference seen (sorted by guide key), which supplies the English name."""
    refs: dict[int, dict[str, Any]] = {}
    for key in sorted(guides):
        for ref in iter_skill_refs(guides[key]):
            refs.setdefault(ref["skillNo"], ref)
    return refs


def _read_optional_tooltips(combos_dir: Path) -> dict[str, Any]:
    """The tooltips the import wrote, or nothing: the bundle is still valid without them (no tooltip shown)."""
    path = combos_dir / TOOLTIPS_FILE
    if not path.is_file():
        log.warn("no tooltips file; skills get no tooltip", path=str(path))
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise BundleError(f"{path} must hold a JSON object")
    return data


def _write_guide_texts(out_dir: Path, stats: BundleStats) -> None:
    """Copies the Spanish guide texts. A missing file is logged and left out: the app then shows the guides in English."""
    if not GUIDE_TEXTS_SOURCE.is_file():
        log.warn("no guide texts file; the guides stay in English", path=str(GUIDE_TEXTS_SOURCE))
        return
    texts = _read_json(GUIDE_TEXTS_SOURCE)
    _write_json(out_dir / GUIDE_TEXTS_RESOURCE, texts, stats)
    stats.guide_texts = len(texts)


def _write_skills_lookup(
    out_dir: Path,
    skill_refs: dict[int, dict[str, Any]],
    client: ClientExport,
    stats: BundleStats,
    tooltips: dict[str, Any] | None = None,
) -> None:
    client_skills = client.skill_names()
    tooltips = tooltips or {}
    lookup: dict[str, dict[str, Any]] = {}
    for skill_no in sorted(skill_refs):
        skill = client_skills.get(skill_no)
        description = clean_description(skill.description) if skill is not None else ""
        if description:
            stats.skills_described += 1
        entry: dict[str, Any] = {
            "nameEn": skill_refs[skill_no]["nameEn"],
            "nameEs": clean_name(skill.name) if skill is not None else None,
            "descriptionEs": description or None,
        }
        if str(skill_no) in tooltips:
            entry["tooltip"] = tooltips[str(skill_no)]
            stats.skills_with_tooltip += 1
        lookup[str(skill_no)] = entry
    _write_json(out_dir / SKILLS_RESOURCE, lookup, stats)


def _copy_skill_icons(out_dir: Path, skill_nos: list[int], client: ClientExport, stats: BundleStats) -> None:
    for skill_no in skill_nos:
        source = client.skill_icon(skill_no)
        if source is None or not source.is_file():
            stats.missing_skill_icons.append(skill_no)
            log.warn("skill icon missing from the client export; the app draws a glyph", skill_no=skill_no)
            continue
        _copy_png(source, out_dir / skill_resource(skill_no), stats)
        stats.skill_icons += 1


def _copy_class_icons(out_dir: Path, client: ClientExport, stats: BundleStats) -> None:
    classes = client.classes()
    for class_type in sorted(classes):
        source = classes[class_type].icon
        if not source.is_file():
            raise BundleError(f"class icon for classType {class_type} is missing: {source.name}")
        _copy_png(source, out_dir / class_resource(class_type), stats)
        stats.class_icons += 1


def _copy_slot_art(out_dir: Path, slot_art_dir: Path, stats: BundleStats) -> None:
    for name, (source_name, expected) in SLOT_ART.items():
        source = slot_art_dir / source_name
        if not source.is_file():
            stats.missing_slot_art.append(source_name)
            log.warn("slot art missing; the app draws without it", file=source_name)
            continue
        data = source.read_bytes()
        size = _png_size(data, source)
        if size != expected:
            raise BundleError(f"{source_name} is {size[0]}x{size[1]}; the app expects {expected[0]}x{expected[1]}")
        (out_dir / name).write_bytes(data)
        _count(stats, len(data))
        stats.slot_art += 1


def _copy_png(source: Path, target: Path, stats: BundleStats) -> None:
    data = source.read_bytes()
    width, height = _png_size(data, source)
    if max(width, height) > ICON_MAX_SIDE:
        raise BundleError(f"{source.name} is {width}x{height}; icons above {ICON_MAX_SIDE} px need resizing")
    target.write_bytes(data)
    _count(stats, len(data))


def _png_size(data: bytes, source: Path) -> tuple[int, int]:
    if len(data) < 24 or data[:8] != PNG_SIGNATURE or data[12:16] != b"IHDR":
        raise BundleError(f"{source.name} is not a PNG file")
    width, height = struct.unpack(">II", data[16:24])
    return width, height


def _write_json(path: Path, data: Any, stats: BundleStats) -> None:
    text = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
    path.write_text(text, encoding="utf-8", newline="\n")
    _count(stats, path.stat().st_size)


def _count(stats: BundleStats, size: int) -> None:
    stats.files += 1
    stats.total_bytes += size


def _remove_stale(out_dir: Path) -> None:
    """Remove files this bundle wrote before, so renamed or dropped skills do not stay embedded."""
    for pattern in ("skill-*.png", "class-*.png", "slot-*.png", "guide-*.json", CATALOG_RESOURCE, SKILLS_RESOURCE, SKILL_CLASSES_RESOURCE):
        for path in out_dir.glob(pattern):
            path.unlink()


def _read_json(path: Path) -> dict[str, Any]:
    if not path.is_file():
        raise BundleError(f"{path} not found; run import-guides first")
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise BundleError(f"{path} must hold a JSON object")
    return data
