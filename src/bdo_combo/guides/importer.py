"""Builds the app-neutral combo data from the saved classhub pages and the client export.

Outputs (JSON, UTF-8, LF):

* ``<out>/catalog.json``: classes (classType, slug, names) and their specs (id, display names, guide key).
* ``<out>/guides/<class>.<spec>.json``: one guide per key, with chains (each with a ``kind``), notes, add-ons
  and ``manualPool`` (the skills of the mobility and cancels chains, placed by hand, never applied automatically).
* ``<out>/tooltips.json``: the skill tooltips of every skill the guides reference (chains, notes and add-ons),
  keyed by skillNo. Read by the bundle.
* ``<report>``: skills that the guides reference but the client export does not name, skills
  whose icon is missing, and skills without tooltip data.
* ``<kinds report>`` (optional): every chain with its kind, the rule that gave it and its confidence.

Every skill reference is ``{"skillNo", "nameEn", "nameEs"}``. ``nameEs`` is null when the client export
has no entry for the skill; the report lists those skills.

Each skill reference then gets its final number, in this order: the Succession swap (overlays.py, Succession guides
only), then the top rank of the skill (skill_ranks.py, every guide). A changed reference takes the English name of its
new number from the class's English tooltip data.
"""

from __future__ import annotations

import json
import re
from collections.abc import Iterator, Mapping
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
from .overlays import (
    SUCCESSION_PREFIX,
    SUCCESSION_SPEC,
    apply_overlays,
    succession_target,
    succession_target_problems,
)
from .kinds import KINDS, LOW, MANUAL_ONLY_KINDS, Verdict, classify_chain
from .pages import ParsedGuide
from .skill_ranks import build_top_ranks
from .tooltips import (
    PAGE_SKILL_KEY,
    ClassSkillData,
    GuideSource,
    TooltipBuild,
    build_tooltip,
    build_tooltips,
    load_class_skill_data,
)

SITE_URL = "https://bdoclasshub.com/"
GUIDE_SCHEMA = 1
TOOLTIPS_FILE = "tooltips.json"
_ROMAN_SUFFIX_RE = re.compile(r"\s(I|II|III|IV|V|VI|VII|VIII)$")


@dataclass
class UnresolvedSkill:
    name_en: str
    guides: list[str] = field(default_factory=list)


@dataclass(frozen=True)
class ChainRow:
    """One chain of one guide with the kind the rules gave it, for the kinds report."""

    guide_key: str
    section: str
    title: str
    steps: int
    verdict: Verdict


@dataclass
class ImportResult:
    catalog: dict[str, Any]
    guides: dict[str, dict[str, Any]]
    unresolved: dict[int, UnresolvedSkill]
    icon_missing: dict[int, list[str]]
    problems: list[str]
    chain_rows: list[ChainRow] = field(default_factory=list)
    tooltips: TooltipBuild = field(default_factory=TooltipBuild)
    rule_counts: dict[str, int] = field(default_factory=dict)

    def skill_refs(self) -> Iterator[dict[str, Any]]:
        for key in sorted(self.guides):
            yield from iter_skill_refs(self.guides[key])

    def stats(self) -> dict[str, int]:
        chains = sum(len(guide["chains"]) for guide in self.guides.values())
        steps = sum(len(chain["steps"]) for guide in self.guides.values() for chain in guide["chains"])
        counts = {f"chains_{kind}": 0 for kind in KINDS}
        for row in self.chain_rows:
            counts[f"chains_{row.verdict.kind}"] += 1
        return {
            **self.rule_counts,
            "classes": len(self.catalog["classes"]),
            "specs": sum(len(entry["specs"]) for entry in self.catalog["classes"]),
            "guides": len(self.guides),
            "chains": chains,
            "steps": steps,
            "unique_skills": len({ref["skillNo"] for ref in self.skill_refs()}),
            "unresolved_skills": len(self.unresolved),
            "icons_missing": len(self.icon_missing),
            "problems": len(self.problems),
            **counts,
            "chains_low_confidence": sum(1 for row in self.chain_rows if row.verdict.confidence == LOW),
            "manual_pool_skills": sum(len(guide["manualPool"]) for guide in self.guides.values()),
            "tooltips": len(self.tooltips.tooltips),
            "tooltips_no_data": len(self.tooltips.no_data),
            "tooltip_name_mismatches": len(self.tooltips.name_mismatches),
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


def import_guides(classhub_dir: Path, client: ClientExport, skill_table: Mapping[str, Any]) -> ImportResult:
    with log.span("guides.import.build", classhub_dir=str(classhub_dir), client_dir=str(client.root)):
        manifest = load_manifest(classhub_dir)
        client_classes = client.classes()
        client_skills = client.skill_names()
        client_names = {skill_no: clean_name(skill.name) for skill_no, skill in client_skills.items()}

        problems: list[str] = []
        problems.extend(succession_target_problems(client_names))
        top_ranks = build_top_ranks(skill_table, client_names)
        english: dict[str, ClassSkillData] = {}
        rule_counts = {"succession_swapped": 0, "succession_kept": 0, "rank_lifted": 0}
        pages = PageCache(classhub_dir)
        plans = plan_specs(pages, manifest, problems)
        for problem in problems:
            log.warn("spec skipped: no saved combo guide", detail=problem)

        class_type_by_slug = {class_slug(cls.name): class_type for class_type, cls in client_classes.items()}
        guides: dict[str, dict[str, Any]] = {}
        chain_rows: list[ChainRow] = []
        sources: list[GuideSource] = []
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
            apply_overlays(plan.guide.key, guide)
            if plan.class_slug not in english:
                english[plan.class_slug] = load_class_skill_data(classhub_dir, plan.class_slug)
            swapped, kept, lifted = _apply_skill_rules(
                plan.guide.key,
                plan.guide.page,
                guide,
                top_ranks,
                client_names,
                english[plan.class_slug],
                problems,
            )
            rule_counts["succession_swapped"] += swapped
            rule_counts["succession_kept"] += kept
            rule_counts["rank_lifted"] += lifted
            chain_rows.extend(_classify_guide(plan.guide.key, guide))
            _record_problems(plan, parsed, problems)
            guides[plan.guide.key] = guide
            sources.append(GuideSource(plan.guide.key, plan.class_slug, plan.guide.page, guide))
            log.info(
                "guide read",
                key=plan.guide.key,
                chains=len(guide["chains"]),
                steps=sum(len(chain["steps"]) for chain in guide["chains"]),
                notes=len(guide["notes"]),
                addons=len(guide["addons"]),
            )

        unresolved = _resolve_skill_names(guides, client_skills, problems)
        for guide in guides.values():
            guide["manualPool"] = manual_pool(guide)
        icon_missing = _check_icons(guides, client)
        tooltips = build_tooltips(classhub_dir, sources)
        for guide in guides.values():
            for ref in iter_skill_refs(guide):
                ref.pop(PAGE_SKILL_KEY, None)

        # The player's game is the Latin American client, so its class names win; the site's Spanish is only a fallback.
        client_names_es = client.class_names_es()
        classes = [
            (
                class_type,
                class_display_name(cls.name),
                client_names_es.get(class_type) or spanish_class_name(classhub_dir, class_display_name(cls.name)),
            )
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
            chain_rows=chain_rows,
            tooltips=tooltips,
            rule_counts=rule_counts,
        )


def write_outputs(
    result: ImportResult,
    out_dir: Path,
    report_path: Path,
    kinds_report_path: Path | None = None,
) -> None:
    with log.span("guides.import.write", out_dir=str(out_dir), report=str(report_path)):
        guides_dir = out_dir / "guides"
        guides_dir.mkdir(parents=True, exist_ok=True)
        for stale in guides_dir.glob("*.json"):
            stale.unlink()
        for key, guide in result.guides.items():
            _write_json(guides_dir / f"{key}.json", guide)
        _write_json(out_dir / "catalog.json", result.catalog)
        tooltips = {str(skill_no): tooltip for skill_no, tooltip in sorted(result.tooltips.tooltips.items())}
        _write_json(out_dir / TOOLTIPS_FILE, tooltips)

        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(render_report(result), encoding="utf-8", newline="\n")
        log.info("unresolved skills report written", unresolved=len(result.unresolved))

        if kinds_report_path is not None:
            kinds_report_path.parent.mkdir(parents=True, exist_ok=True)
            kinds_report_path.write_text(render_kinds_report(result), encoding="utf-8", newline="\n")
            log.info("chain kinds report written", chains=len(result.chain_rows))


def render_kinds_report(result: ImportResult) -> str:
    """Every chain with its kind, grouped so the low-confidence ones are read first."""
    rows = result.chain_rows
    counts = {kind: sum(1 for row in rows if row.verdict.kind == kind) for kind in KINDS}
    low = [row for row in rows if row.verdict.confidence == LOW]
    lines = [
        "Combo chains by kind",
        "Generated by `python -m bdo_combo.guides import`. Do not edit by hand.",
        "",
        f"Chains: {len(rows)}",
        "Kinds: " + ", ".join(f"{kind} {counts[kind]}" for kind in KINDS),
        "buff and combo are applied automatically to the cooldown bar; mobility and cancels are manual-only;",
        "other is shown collapsed. Rules: src/bdo_combo/guides/kinds.py (classify_chain).",
        "",
        f"LOW CONFIDENCE ({len(low)}): review these first",
        "guide | section | kind | reason | steps | title",
    ]
    lines += [_kind_line(row) for row in low]
    lines += ["", "ALL CHAINS", "guide | section | kind | confidence | reason | steps | title"]
    lines += [_kind_line(row, with_confidence=True) for row in rows]
    return "\n".join(lines) + "\n"


def _kind_line(row: ChainRow, with_confidence: bool = False) -> str:
    title = row.title or "(no title)"
    head = [row.guide_key, row.section, row.verdict.kind]
    if with_confidence:
        head.append(row.verdict.confidence)
    head += [row.verdict.reason, str(row.steps), title]
    return " | ".join(head)


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

    tooltips = result.tooltips
    lines += [
        "",
        f"Skills without tooltip data: {len(tooltips.no_data)}",
        "Skills in buff, combo, mobility or cancels chains (and add-ons) that the saved skill files do not have.",
        "skillNo | guides",
    ]
    for skill_no in sorted(tooltips.no_data):
        lines.append(f"{skill_no} | {', '.join(tooltips.no_data[skill_no])}")

    lines += ["", f"Tooltip names that differ from the guide: {len(tooltips.name_mismatches)}"]
    lines += [f"- {mismatch}" for mismatch in tooltips.name_mismatches]
    lines += ["", f"Tooltip conflicts between guides: {len(tooltips.conflicts)}"]
    lines += [f"- {conflict}" for conflict in tooltips.conflicts]
    lines += ["", f"Class skill files missing: {len(tooltips.missing_class_files)}"]
    lines += [f"- {slug}" for slug in tooltips.missing_class_files]

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


def _classify_guide(key: str, guide: dict[str, Any]) -> list[ChainRow]:
    """Gives every chain its kind (written into the chain) and returns the rows for the kinds report."""
    rows: list[ChainRow] = []
    for chain in guide["chains"]:
        steps = chain["steps"]
        verdict = classify_chain(
            chain["section"],
            chain["title"],
            [step["extra"] for step in steps],
            [step["keys"] for step in steps],
        )
        chain["kind"] = verdict.kind
        rows.append(ChainRow(key, chain["section"], chain["title"], len(steps), verdict))
    return rows


def manual_pool(guide: dict[str, Any]) -> list[dict[str, Any]]:
    """The skills of the mobility and cancels chains, each once, in document order.

    This is the pool the user places on the cooldown bar by hand. Its skills are never part of the automatic
    combo, even when the same skill also appears in a buff or combo chain.
    """
    pool: dict[int, dict[str, Any]] = {}
    for chain in guide["chains"]:
        if chain["kind"] not in MANUAL_ONLY_KINDS:
            continue
        for step in chain["steps"]:
            for ref in step["skills"]:
                pool.setdefault(ref["skillNo"], {"skillNo": ref["skillNo"], "nameEn": ref["nameEn"], "nameEs": ref["nameEs"]})
    return list(pool.values())


def _record_problems(plan: SpecPlan, parsed: ParsedGuide, problems: list[str]) -> None:
    for problem in parsed.problems:
        problems.append(f"{plan.guide.key}: {problem}")
        log.warn("guide parse problem", detail=problem, key=plan.guide.key)


def _apply_skill_rules(
    key: str,
    page: str,
    guide: dict[str, Any],
    top_ranks: Mapping[int, int],
    client_names: Mapping[int, str],
    english: ClassSkillData,
    problems: list[str],
) -> tuple[int, int, int]:
    """Gives every skill reference of a guide its final number and returns (swapped, kept, lifted).

    First the Succession swap, for Succession guides only. Then the top rank of the skill, for every guide. The site's
    tooltip and English name data is keyed by the number the page shows, so a changed reference keeps that number under
    ``PAGE_SKILL_KEY`` until the tooltips are built. ``kept`` counts the "Sucesión:" skills that have no S: or Suprema:
    counterpart; they keep their number and are reported.
    """
    is_succession = guide["spec"] == SUCCESSION_SPEC
    swapped = lifted = 0
    kept: set[int] = set()
    for ref in iter_skill_refs(guide):
        original = ref["skillNo"]
        target = succession_target(key, original) if is_succession else None
        if target is not None:
            swapped += 1
        elif is_succession and client_names.get(original, "").startswith(SUCCESSION_PREFIX):
            kept.add(original)
        page_no = original if target is None else target
        final = top_ranks.get(page_no, page_no)
        if final != page_no:
            lifted += 1
        if final == original:
            continue
        ref["skillNo"] = final
        if final != page_no:
            ref[PAGE_SKILL_KEY] = page_no
        name_en = build_tooltip(english, page, page_no).name_en
        if name_en is None and target is not None:
            problems.append(f"{key}: swapped skill {page_no} has no English name in the class data")
        ref["nameEn"] = name_en or _name_with_rank(ref["nameEn"], client_names.get(final, ""))

    for skill_no in sorted(kept):
        problems.append(f"{key}: skill {skill_no} ({client_names[skill_no]}) has no S: or Suprema: skill; it is kept as it is")
    if swapped or kept or lifted:
        log.info("skill rules applied", key=key, swapped=swapped, kept=len(kept), lifted=lifted)
    return swapped, len(kept), lifted


def _name_with_rank(name_en: str, name_es: str) -> str:
    """The old English name with the rank of the new skill, when the old name ends with a rank ("Counter I" -> "Counter IV")."""
    if not _ROMAN_SUFFIX_RE.search(name_en):
        return name_en
    match = _ROMAN_SUFFIX_RE.search(name_es)
    if match is None:
        return name_en
    return _ROMAN_SUFFIX_RE.sub(f" {match.group(1)}", name_en)


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
