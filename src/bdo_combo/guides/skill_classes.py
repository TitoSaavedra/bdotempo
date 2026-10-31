"""Builds ``skill_classes.json``: which classes can use each skill number, for the app's class inference.

Input is the Scrappers export ``class_skills.json`` (``{"groups": [...]}``). Each group lists its classes and its
ranks; every rank has an integer ``skillNo``. Only groups with at most ``MAX_CLASSES_PER_GROUP`` classes are used:
a group that many classes share (for example the generic escape skills) says nothing about which class a character is.

Output (flat JSON object, shipped in the app bundle):

``{"maxClassesPerGroup": 9, "skills": {"<skillNo>": [<classType>, ...]}}``

A skill number that appears in several kept groups gets the union of their classes, sorted. Keys are sorted so the
output is byte-stable.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .. import log

MAX_CLASSES_PER_GROUP = 9
RESOURCE_NAME = "skill_classes.json"


class SkillClassesError(ValueError):
    """The source table cannot be read or does not have the expected shape."""


def build_skill_class_table(source: dict[str, Any], max_classes: int = MAX_CLASSES_PER_GROUP) -> dict[str, Any]:
    """Maps each skill number to the classes of the kept groups that contain it."""
    groups = source.get("groups")
    if not isinstance(groups, list):
        raise SkillClassesError("the skill table has no 'groups' list")

    classes_by_skill: dict[int, set[int]] = {}
    kept_groups = 0
    for group in groups:
        if not isinstance(group, dict):
            raise SkillClassesError("a group is not an object")
        classes = group.get("classes")
        if not isinstance(classes, list) or not all(isinstance(c, int) and not isinstance(c, bool) for c in classes):
            raise SkillClassesError(f"group {group.get('key')!r} has no integer 'classes' list")
        if not classes or len(classes) > max_classes:
            continue
        kept_groups += 1
        for rank in group.get("ranks") or []:
            skill_no = rank.get("skillNo") if isinstance(rank, dict) else None
            if isinstance(skill_no, int) and not isinstance(skill_no, bool):
                classes_by_skill.setdefault(skill_no, set()).update(classes)

    skills = {str(skill_no): sorted(classes_by_skill[skill_no]) for skill_no in sorted(classes_by_skill)}
    log.info("skill classes built", groups_kept=kept_groups, skills=len(skills), max_classes=max_classes)
    return {"maxClassesPerGroup": max_classes, "skills": skills}


def load_skill_table(path: Path) -> dict[str, Any]:
    """Reads the source table. A missing file is an error: the bundle must not ship without the table."""
    if not path.is_file():
        raise SkillClassesError(f"skill table not found: {path}")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SkillClassesError(f"cannot read skill table {path}: {exc}") from exc
    if not isinstance(data, dict):
        raise SkillClassesError(f"skill table {path} must hold a JSON object")
    return data
