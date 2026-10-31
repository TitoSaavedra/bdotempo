"""Combo files, the apply plan, and the apply command (dry-run by default).

A combo file (schema 2) declares an explicit slot layout. Each of the three
groups occupies an inclusive range of the 20 game slots:

* ``combo``: the main damage combo (``slots`` array).
* ``priorityBuffs``: priority self buffs (``priorityBuffs`` array).
* ``otherBuffs``: other buffs (``otherBuffs`` array).

Entries fill their range from its first slot; unused slots stay 0 (empty).
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

from . import backup, gamevars, log
from .config import ToolError, Settings, character_file, resolve_account
from .gamevars import GameVars, GameVarsError
from .skills import SkillIndex, SkillRef, check_skill_refs, load_skill_index

EXIT_OK = 0
EXIT_VALIDATION_FAILED = 3
COMBO_SCHEMA = 2

# layout key -> JSON array holding its entries (in the same order as the layout)
GROUP_ARRAYS: dict[str, str] = {
    "combo": "slots",
    "priorityBuffs": "priorityBuffs",
    "otherBuffs": "otherBuffs",
}
GROUP_ORDER = tuple(GROUP_ARRAYS)

_OVERRIDE_RE = re.compile(r"^(\d+)=(\d+)$")


class PostWriteValidationError(ToolError):
    """A write failed validation; the original file was restored from the backup."""

    exit_code = EXIT_VALIDATION_FAILED


# --- combo file -------------------------------------------------------------


@dataclass(frozen=True)
class ComboEntry:
    group: str
    slot: int  # game slot 0..19, before optional skipping
    guide_name: str
    skill_name: str
    skill_no: int
    memo: str | None
    optional: bool


@dataclass(frozen=True)
class Combo:
    name: str
    class_name: str
    class_id: int
    spec: str
    source: str
    description: str
    layout: dict[str, tuple[int, int]]
    entries: tuple[ComboEntry, ...]


def _parse_range(layout: dict, key: str) -> tuple[int, int]:
    value = layout.get(key)
    if (
        not isinstance(value, list)
        or len(value) != 2
        or not all(isinstance(v, int) and not isinstance(v, bool) for v in value)
    ):
        raise ToolError(f"layout.{key} must be a [first, last] pair of integers")
    first, last = value
    if not (0 <= first <= last < gamevars.SLOT_COUNT):
        raise ToolError(f"layout.{key} must satisfy 0 <= first <= last <= {gamevars.SLOT_COUNT - 1}")
    return first, last


def _parse_layout(raw: object) -> dict[str, tuple[int, int]]:
    if not isinstance(raw, dict):
        raise ToolError("combo must have a 'layout' object")
    if set(raw) != set(GROUP_ORDER):
        raise ToolError(f"layout must have exactly the keys {list(GROUP_ORDER)}")
    layout = {key: _parse_range(raw, key) for key in GROUP_ORDER}
    ranges = sorted((first, last, key) for key, (first, last) in layout.items())
    for (a_first, a_last, a_key), (b_first, b_last, b_key) in zip(ranges, ranges[1:]):
        if b_first <= a_last:
            raise ToolError(f"layout ranges overlap: {a_key} {a_first}-{a_last} and {b_key} {b_first}-{b_last}")
    return layout


def _parse_entries(group: str, raw_list: object, layout: dict[str, tuple[int, int]]) -> list[ComboEntry]:
    array_name = GROUP_ARRAYS[group]
    if not isinstance(raw_list, list):
        raise ToolError(f"'{array_name}' must be an array")
    first, last = layout[group]
    capacity = last - first + 1
    if len(raw_list) > capacity:
        raise ToolError(
            f"'{array_name}' has {len(raw_list)} entries but its layout range {first}-{last} holds {capacity}"
        )
    entries: list[ComboEntry] = []
    for index, raw in enumerate(raw_list):
        where = f"{array_name}[{index}]"
        if not isinstance(raw, dict):
            raise ToolError(f"{where} must be an object")
        if "slot" in raw:
            raise ToolError(f"{where} must not have 'slot': slot numbers come from the layout")
        skill_no = raw.get("skillNo")
        if not isinstance(skill_no, int) or isinstance(skill_no, bool) or skill_no <= 0:
            raise ToolError(f"{where}: skillNo must be a positive integer")
        memo = raw.get("memo")
        if memo is not None and not isinstance(memo, str):
            raise ToolError(f"{where}: memo must be a string or null")
        if memo is not None and ("\n" in memo or "\r" in memo):
            raise ToolError(f"{where}: memo must be a single line")
        if memo == "":
            memo = None  # empty memo: do not create a memo entry
        optional = raw.get("optional", False)
        if not isinstance(optional, bool):
            raise ToolError(f"{where}: optional must be true or false")
        entries.append(
            ComboEntry(
                group=group,
                slot=first + index,
                guide_name=str(raw.get("skill", "")),
                skill_name=str(raw.get("skillName", "")),
                skill_no=skill_no,
                memo=memo,
                optional=optional,
            )
        )
    return entries


def load_combo(path: Path) -> Combo:
    if not path.is_file():
        raise ToolError(f"combo file not found: {path}")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise ToolError(f"cannot read combo file {path.name}: {exc}") from exc
    if not isinstance(data, dict):
        raise ToolError("combo file must contain a JSON object")
    if data.get("schema") != COMBO_SCHEMA:
        raise ToolError(f"unsupported combo schema: {data.get('schema')!r} (expected {COMBO_SCHEMA})")
    class_id = data.get("classId")
    if not isinstance(class_id, int) or isinstance(class_id, bool):
        raise ToolError("combo classId must be an integer")

    layout = _parse_layout(data.get("layout"))
    entries: list[ComboEntry] = []
    for group in GROUP_ORDER:
        entries += _parse_entries(group, data.get(GROUP_ARRAYS[group]), layout)
    if not any(e.group == "combo" for e in entries):
        raise ToolError("combo must have at least one entry in 'slots'")

    seen: dict[int, int] = {}
    for entry in entries:
        if entry.skill_no in seen:
            log.warn(
                "same skillNo used in more than one slot",
                skill_no=entry.skill_no,
                first_slot=seen[entry.skill_no],
                slot=entry.slot,
            )
        else:
            seen[entry.skill_no] = entry.slot

    combo = Combo(
        name=path.stem,
        class_name=str(data.get("class", "")),
        class_id=class_id,
        spec=str(data.get("spec", "")),
        source=str(data.get("source", "")),
        description=str(data.get("description", "")),
        layout=layout,
        entries=tuple(entries),
    )
    log.info(
        "combo loaded",
        combo=combo.name,
        classId=class_id,
        entries=len(entries),
        layout={k: list(v) for k, v in layout.items()},
    )
    return combo


def combo_refs(combo: Combo, overrides: dict[int, int] | None = None) -> list[SkillRef]:
    """Every entry of the combo as written (before optional skipping), with overrides applied."""
    overrides = overrides or {}
    return [
        SkillRef(e.slot, e.guide_name, e.skill_name, overrides.get(e.slot, e.skill_no))
        for e in combo.entries
    ]


# --- plan -------------------------------------------------------------------


def parse_overrides(values: Sequence[str]) -> dict[int, int]:
    """Parse repeated SLOT=SKILLNO options (SLOT is a game slot, 0..19)."""
    overrides: dict[int, int] = {}
    for value in values:
        match = _OVERRIDE_RE.match(value.strip())
        if not match:
            raise ToolError(f"invalid --skill-override {value!r}; expected SLOT=SKILLNO")
        slot, skill_no = int(match.group(1)), int(match.group(2))
        if not 0 <= slot < gamevars.SLOT_COUNT:
            raise ToolError(f"--skill-override {value!r}: SLOT must be 0..{gamevars.SLOT_COUNT - 1}")
        if skill_no <= 0:
            raise ToolError(f"--skill-override {value!r}: SKILLNO must be positive")
        if slot in overrides:
            raise ToolError(f"--skill-override given twice for slot {slot}")
        overrides[slot] = skill_no
    return overrides


@dataclass(frozen=True)
class PlannedSlot:
    slot: int  # game slot 0..19
    group: str
    skill_no: int
    guide_name: str
    skill_name: str
    memo: str | None


@dataclass(frozen=True)
class Plan:
    slots: tuple[PlannedSlot, ...]
    skills: tuple[int, ...]  # all 20 game slots, 0 = empty
    memos: dict[int, str]  # one memo per SkillNo

    def skill_refs(self) -> list[SkillRef]:
        return [SkillRef(p.slot, p.guide_name, p.skill_name, p.skill_no) for p in self.slots]


def _dedupe_memos(slots: Sequence[PlannedSlot]) -> dict[int, str]:
    """One memo per SkillNo, taken from the first occurrence (lowest slot)."""
    memos: dict[int, str] = {}
    for planned in sorted(slots, key=lambda p: p.slot):
        if not planned.memo:
            continue
        if planned.skill_no not in memos:
            memos[planned.skill_no] = planned.memo
        elif memos[planned.skill_no] != planned.memo:
            log.warn(
                "conflicting memos for the same SkillNo; keeping the first",
                skill_no=planned.skill_no,
                slot=planned.slot,
                kept=memos[planned.skill_no],
                ignored=planned.memo,
            )
    return memos


def build_plan(combo: Combo, overrides: dict[int, int], skip_optional: bool) -> Plan:
    entry_slots = {e.slot for e in combo.entries}
    for slot in overrides:
        if slot not in entry_slots:
            raise ToolError(f"--skill-override refers to slot {slot}, which the combo does not use")

    planned: list[PlannedSlot] = []
    for group in GROUP_ORDER:
        first, _ = combo.layout[group]
        position = 0
        for entry in (e for e in combo.entries if e.group == group):
            skill_no = overrides.get(entry.slot, entry.skill_no)
            if entry.slot in overrides:
                log.info("skill override applied", slot=entry.slot, from_skill=entry.skill_no, to_skill=skill_no)
            if skip_optional and entry.optional:
                log.info("skipped optional skill", slot=entry.slot, group=group, skill_name=entry.skill_name)
                continue
            planned.append(
                PlannedSlot(
                    slot=first + position,
                    group=group,
                    skill_no=skill_no,
                    guide_name=entry.guide_name,
                    skill_name=entry.skill_name,
                    memo=entry.memo,
                )
            )
            position += 1

    numbers = [0] * gamevars.SLOT_COUNT
    for item in planned:
        numbers[item.slot] = item.skill_no
    counts: dict[int, int] = {}
    for item in planned:
        counts[item.skill_no] = counts.get(item.skill_no, 0) + 1
    for skill_no, count in counts.items():
        if count > 1:
            log.warn("same skillNo used in more than one slot of the plan", skill_no=skill_no, slots=count)
    memos = _dedupe_memos(planned)
    log.info("plan built", slots=len(planned), skip_optional=skip_optional, overrides=len(overrides), memos=len(memos))
    return Plan(tuple(planned), tuple(numbers), memos)


def check_plan(combo: Combo, plan: Plan, index: SkillIndex) -> None:
    bad = [f for f in check_skill_refs(plan.skill_refs(), combo.class_id, index) if not f.ok]
    if bad:
        details = "; ".join(f"slot {f.slot} (skillNo {f.skill_no}): {', '.join(f.problems)}" for f in bad)
        raise ToolError(f"combo does not match the skill table: {details}")


# --- per-character computation ---------------------------------------------


@dataclass(frozen=True)
class MemoChange:
    skill_no: int
    old: str | None
    new: str
    status: str  # "added", "changed" or "unchanged"


@dataclass(frozen=True)
class CharacterResult:
    pcid: str
    path: Path
    old_bytes: bytes
    new_bytes: bytes
    old: GameVars
    new_skills: tuple[int, ...]
    memo_changes: tuple[MemoChange, ...]
    problems: tuple[str, ...] = field(default=())

    @property
    def changed(self) -> bool:
        return self.old_bytes != self.new_bytes


def compute_result(plan: Plan, pcid: str, path: Path) -> CharacterResult:
    raw = path.read_bytes()
    try:
        text = raw.decode("utf-8")
        old = gamevars.parse(text)
        new_text = gamevars.apply_combo(text, plan.skills, plan.memos)
    except UnicodeDecodeError as exc:
        raise ToolError(f"PCID {pcid}: gameVariable.xml is not valid UTF-8: {exc}") from exc
    except GameVarsError as exc:
        raise ToolError(f"PCID {pcid}: unexpected gameVariable.xml layout: {exc}") from exc

    problems = gamevars.verify(text, new_text, plan.skills, plan.memos)
    new_bytes = new_text.encode("utf-8")

    old_memo = {m.skill_no: gamevars.xml_unescape_attr(m.memo) for m in old.memos}
    memo_changes: list[MemoChange] = []
    for skill_no, value in plan.memos.items():
        previous = old_memo.get(skill_no)
        if previous is None:
            status = "added"
        elif previous == value:
            status = "unchanged"
        else:
            status = "changed"
        memo_changes.append(MemoChange(skill_no, previous, value, status))

    result = CharacterResult(
        pcid=pcid,
        path=path,
        old_bytes=raw,
        new_bytes=new_bytes,
        old=old,
        new_skills=tuple(plan.skills),
        memo_changes=tuple(memo_changes),
        problems=tuple(problems),
    )
    log.info(
        "character computed",
        pcid=pcid,
        changed=result.changed,
        slots_changed=sum(1 for a, b in zip(old.slots, result.new_skills) if a != b),
        memos_added=sum(1 for m in memo_changes if m.status == "added"),
        memos_changed=sum(1 for m in memo_changes if m.status == "changed"),
        problems=len(problems),
    )
    return result


def format_result(result: CharacterResult, index: SkillIndex, verbose: bool) -> list[str]:
    lines = [f"PCID {result.pcid}", "  Slots:"]
    slot_lines = 0
    for slot, (old_no, new_no) in enumerate(zip(result.old.slots, result.new_skills)):
        if old_no == new_no:
            if not verbose:
                continue
            marker = "="
        else:
            marker = "~"
        slot_lines += 1
        lines.append(
            f"    {marker} slot {slot:>2} (#{slot + 1:>2}): {old_no:>6} {index.describe(old_no)}  ->  {new_no:>6} {index.describe(new_no)}"
        )
    if slot_lines == 0:
        lines.append("    (no slot changes)")

    lines.append("  Memos:")
    shown = 0
    for change in result.memo_changes:
        if change.status == "unchanged" and not verbose:
            continue
        shown += 1
        marker = {"added": "+", "changed": "~", "unchanged": "="}[change.status]
        before = "(none)" if change.old is None else f'"{change.old}"'
        lines.append(
            f'    {marker} SkillNo {change.skill_no} ({index.describe(change.skill_no)}): {before} -> "{change.new}"'
        )
    if shown == 0:
        lines.append("    (no memo changes)")

    added = sum(1 for m in result.memo_changes if m.status == "added")
    changed_memos = sum(1 for m in result.memo_changes if m.status == "changed")
    slots_changed = sum(1 for a, b in zip(result.old.slots, result.new_skills) if a != b)
    if result.changed:
        lines.append(f"  Result: {slots_changed} slot(s) differ, {added} memo(s) added, {changed_memos} memo(s) changed")
    else:
        lines.append("  Result: already identical, nothing to write")
    return lines


# --- command ----------------------------------------------------------------


def run_apply(
    settings: Settings,
    combo_path: Path,
    pcids: Sequence[str],
    *,
    write: bool,
    force: bool,
    skip_optional: bool,
    overrides: dict[int, int],
) -> int:
    if not pcids:
        raise ToolError("apply needs at least one --pcid (applying to every character is not allowed)")
    unique = list(dict.fromkeys(pcids))

    with log.span("apply", combo=combo_path.stem, characters=len(unique), write=write, force=force):
        combo = load_combo(combo_path)
        plan = build_plan(combo, overrides, skip_optional)
        with log.span("apply.load_skill_table"):
            index = load_skill_index(settings.skills_json)
        check_plan(combo, plan, index)

        account = resolve_account(settings)
        targets: list[tuple[str, Path]] = []
        for pcid in unique:
            path = character_file(settings.data_dir, account, pcid)
            if not path.is_file():
                raise ToolError(f"character {pcid} not found under account {account}")
            targets.append((pcid, path))

        results: list[CharacterResult] = []
        for pcid, path in targets:
            with log.span("apply.character", pcid=pcid, account=account):
                results.append(compute_result(plan, pcid, path))

        print(f"Combo: {combo.name} ({combo.description or combo.spec}), {len(plan.slots)} slot(s) used")
        for result in results:
            print("\n".join(format_result(result, index, settings.verbose)))
            print()

        refused = [r for r in results if r.problems]
        if refused:
            for r in refused:
                for problem in r.problems:
                    log.error("validation failed before writing", pcid=r.pcid, problem=problem, exc_info=False)
            raise ToolError("validation failed for " + ", ".join(r.pcid for r in refused) + "; nothing was written")

        if not write:
            print("DRY RUN: nothing was written. Re-run with --write to apply.")
            return EXIT_OK

        backup.guard_write(targets, force)
        backup_dir = backup.create_backup(
            [backup.BackupTarget(pcid, path) for pcid, path in targets],
            account=account,
            backups_dir=settings.backups_dir,
            reason="pre-apply",
        )
        print(f"Backup taken: {backup_dir}")

        for result in results:
            if not result.changed:
                print(f"PCID {result.pcid}: already identical, not written")
                continue
            with log.span("apply.write", pcid=result.pcid):
                backup.atomic_write(result.path, result.new_bytes)
                written = result.path.read_bytes()
                if written != result.new_bytes:
                    problems = ["file on disk differs from the computed result"]
                else:
                    problems = gamevars.verify(
                        result.old_bytes.decode("utf-8"),
                        written.decode("utf-8"),
                        plan.skills,
                        plan.memos,
                    )
                if problems:
                    for problem in problems:
                        log.error("post-write validation failed", pcid=result.pcid, problem=problem, exc_info=False)
                    backup.restore_file(backup_dir, account, result.pcid, result.path)
                    raise PostWriteValidationError(
                        f"post-write validation failed for PCID {result.pcid}; "
                        "the original file was restored from the backup"
                    )
                print(f"PCID {result.pcid}: written and validated")
    return EXIT_OK
