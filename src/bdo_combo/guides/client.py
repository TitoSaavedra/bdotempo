"""Reads the Spanish client export (names, descriptions and icon files of skills and classes).

Layout used (all UTF-8 JSON):

* ``skills_es.json``: ``{"<skillNo>": {"name": ..., "description": ...}}`` with game markup in descriptions.
* ``skills_icons.json``: ``{"<skillNo>": {"file": "icons/skills/<skillNo>.png", ...}}``, relative to the export root.
* ``classes_icons.json``: ``{"<classType>": {"name": ..., "file": "icons/classes/class_<classType>.png", ...}}``.
"""

from __future__ import annotations

import html
import json
import re
from dataclasses import dataclass
from pathlib import Path

SKILL_NAMES_FILE = "skills_es.json"
SKILL_ICONS_FILE = "skills_icons.json"
CLASS_ICONS_FILE = "classes_icons.json"
# The Latin American Spanish client text that holds the class names: {"<classType>": {"0": name, "16777216": description}}.
# Some classes keep their English name there ("Ranger", "Sage"), which is what the LATAM game shows.
CLASS_NAMES_LOC_FILE = "loc/locs/21.json"

# Game markup: colour tags such as <PAColor0xffe9bd23> and <PAOldColor>, and key placeholders such as
# {TextBind:CASTING_CLICK_RMB}. The text between the tags is kept. Other angle-bracket words, such as
# <Jefe> ("boss"), are game text and stay.
_MARKUP_RE = re.compile(r"</?PA[^<>]*>|\{TextBind:[^{}]*\}")


class ClientExportError(ValueError):
    """The client export is missing a file or a file has an unexpected shape."""


@dataclass(frozen=True)
class ClientSkill:
    name: str
    description: str


@dataclass(frozen=True)
class ClientClass:
    name: str
    icon: Path


class ClientExport:
    def __init__(self, root: Path) -> None:
        self.root = root
        self._skills: dict[int, ClientSkill] | None = None
        self._skill_icons: dict[int, Path] | None = None
        self._classes: dict[int, ClientClass] | None = None

    def skill_names(self) -> dict[int, ClientSkill]:
        if self._skills is None:
            raw = _load_object(self.root / SKILL_NAMES_FILE)
            self._skills = {
                _int_key(key, SKILL_NAMES_FILE): ClientSkill(
                    name=_text_field(value, "name", SKILL_NAMES_FILE, key),
                    description=_text_field(value, "description", SKILL_NAMES_FILE, key),
                )
                for key, value in raw.items()
            }
        return self._skills

    def skill_icon(self, skill_no: int) -> Path | None:
        """Path of the skill's PNG, or None when the export has no icon entry for it."""
        if self._skill_icons is None:
            raw = _load_object(self.root / SKILL_ICONS_FILE)
            self._skill_icons = {
                _int_key(key, SKILL_ICONS_FILE): self.root / _text_field(value, "file", SKILL_ICONS_FILE, key)
                for key, value in raw.items()
            }
        return self._skill_icons.get(skill_no)

    def class_names_es(self) -> dict[int, str]:
        """Class names as the Spanish (LATAM) client shows them, by classType. Empty when the export has no such text."""
        path = self.root / CLASS_NAMES_LOC_FILE
        if not path.is_file():
            return {}
        names: dict[int, str] = {}
        for key, value in _load_object(path).items():
            name = value.get("0") if isinstance(value, dict) else None
            if key.isdigit() and isinstance(name, str) and name.strip():
                names[int(key)] = name.strip()
        return names

    def classes(self) -> dict[int, ClientClass]:
        if self._classes is None:
            raw = _load_object(self.root / CLASS_ICONS_FILE)
            self._classes = {
                _int_key(key, CLASS_ICONS_FILE): ClientClass(
                    name=_text_field(value, "name", CLASS_ICONS_FILE, key),
                    icon=self.root / _text_field(value, "file", CLASS_ICONS_FILE, key),
                )
                for key, value in raw.items()
            }
        return self._classes


def clean_description(text: str) -> str:
    """Game text to plain text: markup tags removed, entities decoded, one line per source line.

    Spaces are collapsed inside each line, blank lines are kept at most one in a row, and the
    result has no leading or trailing blank lines.
    """
    without_tags = html.unescape(_MARKUP_RE.sub("", text))
    lines = [" ".join(line.split()) for line in without_tags.replace("\r\n", "\n").replace("\r", "\n").split("\n")]
    cleaned: list[str] = []
    for line in lines:
        if line == "" and (not cleaned or cleaned[-1] == ""):
            continue
        cleaned.append(line)
    while cleaned and cleaned[-1] == "":
        cleaned.pop()
    return "\n".join(cleaned)


def clean_name(text: str) -> str:
    """A one-line game name (a skill name) with the markup removed and spaces collapsed."""
    return " ".join(clean_description(text).split())


def _load_object(path: Path) -> dict:
    if not path.is_file():
        raise ClientExportError(f"client export file not found: {path.name}")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ClientExportError(f"{path.name} is not valid JSON: {exc}") from exc
    if not isinstance(data, dict):
        raise ClientExportError(f"{path.name} must hold a JSON object")
    return data


def _int_key(key: str, file_name: str) -> int:
    if not key.isdigit():
        raise ClientExportError(f"{file_name} has a non-numeric key: {key!r}")
    return int(key)


def _text_field(value: object, field: str, file_name: str, key: str) -> str:
    if not isinstance(value, dict) or not isinstance(value.get(field), str):
        raise ClientExportError(f"{file_name} entry {key} has no string field {field!r}")
    return value[field]
