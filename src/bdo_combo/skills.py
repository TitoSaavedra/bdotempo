"""Skill table (class_skills.json) access and cross-checking of combo skills."""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from . import log
from .config import ToolError

_MARKUP_RE = re.compile(r"<[^>]+>")
_SPACES_RE = re.compile(r"\s+")
_ROMAN_SUFFIX_RE = re.compile(r"\s+[IVXLCDM]+$")


def clean_name(text: str | None) -> str:
    """Strip game markup such as <PAColor0xff2bbbff> and collapse whitespace."""
    if not text:
        return ""
    return _SPACES_RE.sub(" ", _MARKUP_RE.sub("", text)).strip()


def strip_rank_suffix(name: str) -> str:
    """'Garuda I' -> 'Garuda' (trailing roman numeral removed)."""
    return _ROMAN_SUFFIX_RE.sub("", clean_name(name))


def names_match(expected: str, group_name: str) -> bool:
    """Case-insensitive prefix comparison after removing markup and roman numerals."""
    want = strip_rank_suffix(expected).casefold()
    have = strip_rank_suffix(group_name).casefold()
    if not want or not have:
        return False
    return have.startswith(want) or want.startswith(have)


@dataclass(frozen=True)
class SkillInfo:
    skill_no: int
    class_ids: frozenset[int]
    group_name: str
    display_name: str


class SkillIndex:
    def __init__(self, by_no: dict[int, SkillInfo]) -> None:
        self._by_no = by_no

    def __len__(self) -> int:
        return len(self._by_no)

    def get(self, skill_no: int) -> SkillInfo | None:
        return self._by_no.get(skill_no)

    def describe(self, skill_no: int) -> str:
        if skill_no == 0:
            return "(empty)"
        info = self._by_no.get(skill_no)
        return info.display_name if info else "(unknown skill)"


def load_skill_index(path: Path) -> SkillIndex:
    if not path.is_file():
        raise ToolError(f"skills file not found: {path.name}")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise ToolError(f"cannot read skills file {path.name}: {exc}") from exc
    if not isinstance(data, dict) or not isinstance(data.get("groups"), list):
        raise ToolError("skills file has no 'groups' list")

    by_no: dict[int, SkillInfo] = {}
    for group in data["groups"]:
        group_name = clean_name(group.get("name"))
        classes = frozenset(int(c) for c in group.get("classes", []))
        for rank in group.get("ranks", []):
            skill_no = rank.get("skillNo")
            if not isinstance(skill_no, int):
                log.debug("skipped rank without integer skillNo", group=group_name)
                continue
            if skill_no in by_no:
                log.debug("duplicate skillNo in skills file, keeping the first", skill_no=skill_no)
                continue
            display = clean_name(rank.get("name")) or group_name
            by_no[skill_no] = SkillInfo(skill_no, classes, group_name, display)
    log.info("skill table loaded", skills=len(by_no))
    return SkillIndex(by_no)


@dataclass(frozen=True)
class SkillRef:
    """A combo slot's skill, as needed for cross-checking."""

    slot: int
    guide_name: str
    skill_name: str
    skill_no: int


@dataclass(frozen=True)
class Finding:
    slot: int
    skill_no: int
    table_name: str
    problems: tuple[str, ...]

    @property
    def ok(self) -> bool:
        return not self.problems


def check_skill_refs(refs: Iterable[SkillRef], class_id: int, index: SkillIndex) -> list[Finding]:
    findings: list[Finding] = []
    for ref in refs:
        info = index.get(ref.skill_no)
        if info is None:
            findings.append(Finding(ref.slot, ref.skill_no, "(unknown skill)", ("skillNo not found in the skill table",)))
            continue
        problems: list[str] = []
        if class_id not in info.class_ids:
            problems.append(f"skillNo belongs to classes {sorted(info.class_ids)}, not classId {class_id}")
        if not names_match(ref.skill_name, info.group_name):
            problems.append(f"table group '{info.group_name}' does not match skillName '{ref.skill_name}'")
        findings.append(Finding(ref.slot, ref.skill_no, info.display_name, tuple(problems)))
    return findings
