"""Builds the app-neutral combo data from the saved classhub pages and the client export.

Outputs (JSON, UTF-8, LF):

* ``<out>/catalog.json``: classes (classType, slug, names) and their specs (id, display names, guide key).
* ``<out>/guides/<class>.<spec>.json``: one guide per key, with chains, notes and add-ons.
* ``<report>``: skills that the guides reference but the client export does not name, and skills
  whose icon is missing.

Every skill reference is ``{"skillNo", "nameEn", "nameEs"}``. ``nameEs`` is null when the client export
has no entry for the skill; the report lists those skills.
"""

from __future__ import annotations

import json
from collections.abc import Iterator
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .. import log
from .catalog import (
    PageCache,
    SpecPlan,
    build_catalog,
    class_display_name,
    class_slug,
    load_manifest,
    plan_specs,
    spanish_class_name,
)
from .client import ClientExport, clean_name
from .pages import ParsedGuide

SITE_URL = "https://bdoclasshub.com/"
GUIDE_SCHEMA = 1


@dataclass
class UnresolvedSkill:
    name_en: str
    guides: list[str] = field(default_factory=list)


@dataclass
class ImportResult:
    catalog: dict[str, Any]
    guides: dict[str, dict[str, Any]]
    unresolved: dict[int, UnresolvedSkill]
    icon_missing: dict[int, list[str]]
    problems: list[str]

    def skill_refs(self) -> Iterator[dict[str, Any]]:
        for key in sorted(self.guides):
            yield from iter_skill_refs(self.guides[key])

    def stats(self) -> dict[str, int]:
        chains = sum(len(guide["chains"]) for guide in self.guides.values())
        steps = sum(len(chain["steps"]) for guide in self.guides.values() for chain in guide["chains"])
        return {
            "classes": len(self.catalog["classes"]),
            "specs": sum(len(entry["specs"]) for entry in self.catalog["classes"]),
            "guides": len(self.guides),
            "chains": chains,
            "steps": steps,
            "unique_skills": len({ref["skillNo"] for ref in self.skill_refs()}),
            "unresolved_skills": len(self.unresolved),
            "icons_missing": len(self.icon_missing),
            "problems": len(self.problems),
        }


def iter_skill_refs(guide: dict[str, Any]) -> Iterator[dict[str, Any]]:
    """Every skill reference in a guide, in document order: steps, inline note skills, then add-ons."""
    for chain in guide["chains"]:
        for step in chain["steps"]:
            yield from step["skills"]
    for note in guide["notes"]:
        yield from note["skills"]
    for addon in guide["addons"]:
        yield addon["skill"]


def import_guides(classhub_dir: Path, client: ClientExport) -> ImportResult:
    with log.span("guides.import.build", classhub_dir=str(classhub_dir), client_dir=str(client.root)):
        manifest = load_manifest(classhub_dir)
        client_classes = client.classes()
        client_skills = client.skill_names()

        problems: list[str] = []
        pages = PageCache(classhub_dir)
        plans = plan_specs(pages, manifest, problems)
        for problem in problems:
            log.warn("spec skipped: no saved combo guide", detail=problem)

        class_type_by_slug = {class_slug(cls.name): class_type for class_type, cls in client_classes.items()}
        guides: dict[str, dict[str, Any]] = {}
        for plan in plans:
            if plan.guide.key in guides:
                continue
            class_type = class_type_by_slug.get(plan.class_slug)
            if class_type is None:
                problems.append(f"{plan.class_slug}: class is not in the client export")
                log.warn("class is not in the client export", class_slug=plan.class_slug)
                continue
            parsed = pages.guide(plan.guide.page)
            guide = _guide_json(plan, class_type, parsed)
            _record_problems(plan, parsed, problems)
            guides[plan.guide.key] = guide
            log.info(
                "guide read",
                key=plan.guide.key,
                chains=len(guide["chains"]),
                steps=sum(len(chain["steps"]) for chain in guide["chains"]),
                notes=len(guide["notes"]),
                addons=len(guide["addons"]),
            )

        unresolved = _resolve_skill_names(guides, client_skills, problems)
        icon_missing = _check_icons(guides, client)

        classes = [
            (class_type, class_display_name(cls.name), spanish_class_name(classhub_dir, class_display_name(cls.name)))
            for class_type, cls in client_classes.items()
        ]
        catalog = build_catalog(site_url=SITE_URL, classes=classes, plans=_used_plans(plans, guides))
        for entry in catalog["classes"]:
            for spec in entry["specs"]:
                if spec["guide"] not in guides:
                    raise ValueError(f"catalog points to a guide that was not built: {spec['guide']}")

        return ImportResult(
            catalog=catalog,
            guides=guides,
            unresolved=unresolved,
            icon_missing=icon_missing,
            problems=problems,
        )


def write_outputs(result: ImportResult, out_dir: Path, report_path: Path) -> None:
    with log.span("guides.import.write", out_dir=str(out_dir), report=str(report_path)):
        guides_dir = out_dir / "guides"
        guides_dir.mkdir(parents=True, exist_ok=True)
        for stale in guides_dir.glob("*.json"):
            stale.unlink()
        for key, guide in result.guides.items():
            _write_json(guides_dir / f"{key}.json", guide)
        _write_json(out_dir / "catalog.json", result.catalog)

        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(render_report(result), encoding="utf-8", newline="\n")
        log.info("unresolved skills report written", unresolved=len(result.unresolved))


def render_report(result: ImportResult) -> str:
    lines = [
        "Skills referenced by the combo guides that need attention",
        "Generated by `python -m bdo_combo.guides import`. Do not edit by hand.",
        "",
        f"Skills missing from skills_es.json: {len(result.unresolved)}",
        "The guide files keep nameEs as null for these; the English name from the guide is used.",
        "skillNo | nameEn | guides",
    ]
    for skill_no in sorted(result.unresolved):
        entry = result.unresolved[skill_no]
        lines.append(f"{skill_no} | {entry.name_en} | {', '.join(entry.guides)}")

    lines += [
        "",
        f"Skills whose icon is missing from the client export: {len(result.icon_missing)}",
        "skillNo | guides",
    ]
    for skill_no in sorted(result.icon_missing):
        lines.append(f"{skill_no} | {', '.join(sorted(set(result.icon_missing[skill_no])))}")

    lines += ["", f"Other problems: {len(result.problems)}"]
    lines += [f"- {problem}" for problem in result.problems]
    return "\n".join(lines) + "\n"


def _guide_json(plan: SpecPlan, class_type: int, parsed: ParsedGuide) -> dict[str, Any]:
    return {
        "schema": GUIDE_SCHEMA,
        "classType": class_type,
        "classSlug": plan.class_slug,
        "spec": plan.guide.guide_spec,
        "title": parsed.title,
        "description": parsed.description,
        "source": SITE_URL + plan.guide.page,
        "chains": parsed.chains,
        "notes": parsed.notes,
        "addons": parsed.addons,
    }


def _record_problems(plan: SpecPlan, parsed: ParsedGuide, problems: list[str]) -> None:
    for problem in parsed.problems:
        problems.append(f"{plan.guide.key}: {problem}")
        log.warn("guide parse problem", detail=problem, key=plan.guide.key)


def _resolve_skill_names(
    guides: dict[str, dict[str, Any]],
    client_skills: dict[int, Any],
    problems: list[str],
) -> dict[int, UnresolvedSkill]:
    unresolved: dict[int, UnresolvedSkill] = {}
    for key in sorted(guides):
        for ref in iter_skill_refs(guides[key]):
            skill = client_skills.get(ref["skillNo"])
            ref["nameEs"] = clean_name(skill.name) if skill is not None else None
            if skill is None:
                entry = unresolved.setdefault(ref["skillNo"], UnresolvedSkill(name_en=ref["nameEn"]))
                if key not in entry.guides:
                    entry.guides.append(key)
    for skill_no, entry in sorted(unresolved.items()):
        problems.append(f"skill {skill_no} ({entry.name_en}) is not in skills_es.json")
        log.warn("skill is not in skills_es.json", skill_no=skill_no)
    return unresolved


def _check_icons(guides: dict[str, dict[str, Any]], client: ClientExport) -> dict[int, list[str]]:
    missing: dict[int, list[str]] = {}
    for key in sorted(guides):
        for ref in iter_skill_refs(guides[key]):
            icon = client.skill_icon(ref["skillNo"])
            if icon is None or not icon.is_file():
                missing.setdefault(ref["skillNo"], []).append(key)
    for skill_no in sorted(missing):
        log.warn("skill icon is not in the client export", skill_no=skill_no)
    return missing


def _used_plans(plans: list[SpecPlan], guides: dict[str, dict[str, Any]]) -> list[SpecPlan]:
    return [plan for plan in plans if plan.guide.key in guides]


def _write_json(path: Path, data: Any) -> None:
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
