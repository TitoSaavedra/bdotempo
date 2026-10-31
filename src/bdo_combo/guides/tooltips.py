"""Skill tooltips for the combo guides, read from the saved bdoclasshub skill data.

The site's tooltip script (``js/skill-tooltips.js``) reads ``data/skills/<lang>/<class>.json``. Each file has
``s``: skill entries keyed by an id, and the English file also has ``map``: for each page kind (``awakening``,
``succession``, otherwise ``overview``), the icon number of a skill on a page mapped to the entry id. The entry
is ``lo.s[id] || en.s[id]``. This module does the same lookup for the skills of the guides, preferring the
Mexican Spanish text, then Spanish, then English, one entry at a time.

A tooltip is a plain dictionary with the keys of the bundle (see ``build_tooltip``). Text is cleaned of game
markup. Numbers are kept as numbers; the app formats them.
"""

from __future__ import annotations

import json
import re
from collections.abc import Iterator
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .. import log
from .client import clean_description

# Languages in preference order. The English file is always read: it holds the map and the fallback entries.
TOOLTIP_LANGS = ("es-mx", "es", "en")
SKILL_DATA_DIR = Path("data") / "skills"
LABELS_FILE = "labels.json"
OVERVIEW_KIND = "overview"

_INT_RE = re.compile(r"^\d+$")
# Family labels the site puts in front of a name, one or more: "Prime: Eviscerate", "Flow: Spear of Sylvia",
# "Succession: Falling Rock", "Chaos: Crimson Gust", "Prime: Mareca: Sea Stroll". A label is short and has no digits.
_LABEL_PREFIX_RE = re.compile(r"^(?:[A-Za-z][A-Za-z' ]{0,20}:\s*)+")
_RANK_SUFFIX_RE = re.compile(r"\s+[ivxlc]+$", re.IGNORECASE)
_APOSTROPHE_RE = re.compile(r"['’]")
_NON_ALNUM_RE = re.compile(r"[^0-9a-z]+")


@dataclass
class ClassSkillData:
    """The saved skill files of one class. ``docs`` maps a language to its parsed file (missing languages are absent)."""

    slug: str
    docs: dict[str, dict[str, Any]]
    labels: dict[str, dict[str, Any]]

    @property
    def available(self) -> bool:
        return "en" in self.docs


@dataclass(frozen=True)
class TooltipResult:
    tooltip: dict[str, Any] | None
    name_en: str | None


@dataclass
class TooltipBuild:
    """The tooltips of every skill the guides reference (chain steps, notes and add-ons)."""

    tooltips: dict[int, dict[str, Any]] = field(default_factory=dict)
    no_data: dict[int, list[str]] = field(default_factory=dict)
    name_mismatches: list[str] = field(default_factory=list)
    conflicts: list[str] = field(default_factory=list)
    missing_class_files: list[str] = field(default_factory=list)


@dataclass(frozen=True)
class GuideSource:
    """One guide with the class slug and the saved page it came from (the page decides the map kind)."""

    key: str
    class_slug: str
    page: str
    guide: dict[str, Any]


def load_class_skill_data(classhub_dir: Path, class_slug: str) -> ClassSkillData:
    base = classhub_dir / SKILL_DATA_DIR
    docs: dict[str, dict[str, Any]] = {}
    for lang in TOOLTIP_LANGS:
        path = base / lang / f"{class_slug}.json"
        if path.is_file():
            docs[lang] = json.loads(path.read_text(encoding="utf-8"))
    labels_path = base / LABELS_FILE
    labels = json.loads(labels_path.read_text(encoding="utf-8")) if labels_path.is_file() else {}
    return ClassSkillData(slug=class_slug, docs=docs, labels=labels)


def kind_for_page(page: str) -> str:
    """The map kind the site uses for a page: ``classes/<class>/<kind>/...`` gives the kind, anything else overview."""
    segments = page.replace("\\", "/").split("/")
    if "classes" in segments:
        rest = segments[segments.index("classes") + 1:]
        if len(rest) > 2 and rest[1]:
            return rest[1]
    return OVERVIEW_KIND


def tooltip_refs(guide: dict[str, Any]) -> Iterator[dict[str, Any]]:
    """Every skill reference of a guide, in document order: chain steps (of any kind), notes, then add-ons.

    Every referenced skill gets a tooltip, so the bar, the manual pool and the collapsed sections all show the
    same game text.
    """
    for chain in guide["chains"]:
        for step in chain["steps"]:
            yield from step["skills"]
    for note in guide["notes"]:
        yield from note["skills"]
    for addon in guide["addons"]:
        yield addon["skill"]


def entry_id_for(data: ClassSkillData, page: str, skill_no: int) -> str | None:
    """The entry id of a skill on a page, through the English map. None when the class has no English file."""
    en = data.docs.get("en")
    if en is None:
        return None
    id_map = en.get("map") or {}
    kind = kind_for_page(page)
    if kind in id_map:
        kind_map = id_map[kind]
    else:
        kind_map = id_map.get(OVERVIEW_KIND) or {}
    return kind_map.get(str(skill_no), str(skill_no))


def find_entry(data: ClassSkillData, page: str, skill_no: int) -> tuple[str, dict[str, Any]] | None:
    """(language, entry) for a skill on a page, preferring Mexican Spanish, then Spanish, then English."""
    entry_id = entry_id_for(data, page, skill_no)
    if entry_id is None:
        return None
    for lang in TOOLTIP_LANGS:
        doc = data.docs.get(lang)
        entry = doc.get("s", {}).get(entry_id) if doc else None
        if entry:
            return lang, entry
    return None


def build_tooltip(data: ClassSkillData, page: str, skill_no: int) -> TooltipResult:
    found = find_entry(data, page, skill_no)
    if found is None:
        return TooltipResult(tooltip=None, name_en=None)
    lang, entry = found
    en_entry = data.docs["en"].get("s", {}).get(entry_id_for(data, page, skill_no) or "")
    name_en = clean_text(en_entry.get("n")) if en_entry else None
    return TooltipResult(tooltip=_tooltip_from_entry(entry, lang, data.labels), name_en=name_en)


def _tooltip_from_entry(entry: dict[str, Any], lang: str, labels: dict[str, Any]) -> dict[str, Any]:
    attack = _label(labels, lang, "attack") or "Attack"
    level = entry.get("lv")
    cooldown = entry.get("cd")
    pvp = entry.get("pvp")
    return {
        "name": clean_text(entry.get("n")),
        "lang": lang,
        "keys": _texts(entry.get("k")),
        "level": int(level) if isinstance(level, str) and _INT_RE.match(level) else None,
        "cost": [
            {"type": clean_text(row[0]), "label": clean_text(row[1]), "value": clean_text(row[2])}
            for row in entry.get("cost") or []
        ],
        "cooldownSec": float(cooldown) if cooldown is not None else None,
        "damage": [_damage_row(row, attack) for row in entry.get("d") or []],
        "pvpPercent": float(pvp) if pvp is not None else None,
        "protection": _texts(entry.get("p")),
        "extras": _texts(entry.get("x")),
        "features": _texts(entry.get("f")),
    }


def _damage_row(row: list[Any], attack: str) -> dict[str, Any]:
    label, percent, hits = row[0], row[1], row[2]
    max_hits = row[3] if len(row) > 3 else None
    if isinstance(label, (int, float)):
        text = f"{attack} {int(label)}" if label else attack
    else:
        text = clean_text(label)
    return {
        "label": text,
        "percent": float(percent),
        "hits": int(hits),
        "maxHits": int(max_hits) if max_hits else None,
    }


def _label(labels: dict[str, Any], lang: str, key: str) -> str | None:
    for candidate in (lang, "en"):
        value = (labels.get(candidate) or {}).get(key)
        if isinstance(value, str) and value:
            return value
    return None


def clean_text(value: object) -> str:
    """Game text as plain text: markup removed, one line per source line. Empty when not text."""
    return clean_description(value) if isinstance(value, str) else ""


def _texts(values: object) -> list[str]:
    if not isinstance(values, list):
        return []
    return [text for text in (clean_text(value) for value in values) if text]


def plain_skill_name(name: str) -> str:
    """A skill name for comparison: family labels and the rank removed, apostrophes dropped, lower-case letters
    and digits only ("Succession: Falling Rock" and "Falling Rock" both give "falling rock")."""
    text = _LABEL_PREFIX_RE.sub("", name.strip())
    text = _RANK_SUFFIX_RE.sub("", text)
    text = _APOSTROPHE_RE.sub("", text.lower())
    return _NON_ALNUM_RE.sub(" ", text).strip()


def names_agree(guide_name: str, tooltip_name_en: str | None) -> bool:
    """True when the guide's English name and the tooltip's English name are the same skill.

    The tooltip name may carry a family label ("Succession: Falling Rock", "Prime: Eviscerate") and a rank
    ("Summon Heuklang XI"); the guide name is the base name. One must start with the other after both are
    reduced by ``plain_skill_name``.
    """
    if not tooltip_name_en:
        return False
    guide = plain_skill_name(guide_name)
    tip = plain_skill_name(tooltip_name_en)
    if not guide or not tip:
        return False
    return tip == guide or tip.startswith(guide) or guide.startswith(tip)


def build_tooltips(classhub_dir: Path, sources: list[GuideSource]) -> TooltipBuild:
    """Tooltips for every skill that a chain of a tooltip kind or an add-on references, in guide key order.

    The first guide that references a skill decides its tooltip. A later guide whose tooltip differs is a
    conflict (the same skill number reached through another page kind), and only the first is kept.
    """
    result = TooltipBuild()
    class_data: dict[str, ClassSkillData] = {}
    resolved: dict[int, TooltipResult] = {}
    no_data_guides: dict[int, list[str]] = {}

    for source in sorted(sources, key=lambda item: item.key):
        data = class_data.get(source.class_slug)
        if data is None:
            data = load_class_skill_data(classhub_dir, source.class_slug)
            class_data[source.class_slug] = data
            if not data.available:
                result.missing_class_files.append(source.class_slug)
                log.warn("skill tooltip file is missing for the class", class_slug=source.class_slug)

        for ref in tooltip_refs(source.guide):
            skill_no = ref["skillNo"]
            found = build_tooltip(data, source.page, skill_no) if data.available else TooltipResult(None, None)
            if found.tooltip is None:
                guides = no_data_guides.setdefault(skill_no, [])
                if source.key not in guides:
                    guides.append(source.key)
                continue
            if skill_no in resolved:
                if resolved[skill_no].tooltip != found.tooltip:
                    result.conflicts.append(f"skill {skill_no}: {source.key} has another tooltip than the first guide")
                continue
            resolved[skill_no] = found
            if found.name_en is not None and not names_agree(ref["nameEn"], found.name_en):
                result.name_mismatches.append(
                    f"skill {skill_no}: guide says {ref['nameEn']!r}, tooltip says {found.name_en!r} ({source.key})"
                )
                log.warn("tooltip name differs from the guide", skill_no=skill_no, guide=source.key)

    for skill_no, found in sorted(resolved.items()):
        if found.tooltip is not None:
            result.tooltips[skill_no] = found.tooltip
    for skill_no in sorted(no_data_guides):
        if skill_no not in result.tooltips:
            result.no_data[skill_no] = no_data_guides[skill_no]
    log.info(
        "skill tooltips built",
        tooltips=len(result.tooltips),
        no_data=len(result.no_data),
        name_mismatches=len(result.name_mismatches),
        conflicts=len(result.conflicts),
    )
    return result
