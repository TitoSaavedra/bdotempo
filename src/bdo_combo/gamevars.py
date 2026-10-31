"""Surgical text edits of a character's gameVariable.xml.

The file is NOT a single-root XML document (it is a concatenation of top-level
fragments), so it is never parsed with an XML library and never re-serialized.
Only two sections are touched:

* ``<SkillCoolTimeSlot Version="2">``: exactly 20 entries, SlotNo 0..19; SkillNo 0 means empty.
* ``<SkillCoolTimeMemo Version="1">``: memo entries keyed by SkillNo. The section
  may be absent (created right after the slot section) or self-closed (expanded).

Everything else must stay byte-identical; ``verify`` checks that.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Mapping, Sequence

SLOT_COUNT = 20
DEFAULT_MEMO_VERSION = "1"

_SLOT_SECTION_RE = re.compile(r'<SkillCoolTimeSlot Version="(\d+)">(.*?)</SkillCoolTimeSlot>', re.DOTALL)
_MEMO_OPEN_RE = re.compile(r'<SkillCoolTimeMemo Version="(\d+)">(.*?)</SkillCoolTimeMemo>', re.DOTALL)
_MEMO_EMPTY_RE = re.compile(r'<SkillCoolTimeMemo Version="(\d+)"\s*/>')
_SLOT_ENTRY_RE = re.compile(r'<SkillCoolTimeSlot SlotNo="(\d+)" SkillNo="(\d+)"/>')
_MEMO_ENTRY_RE = re.compile(r'<SkillCoolTimeMemo SkillNo="(\d+)" Memo="([^"]*)"/>')

_SLOT_PLACEHOLDER = "\x00SLOT\x00"
_MEMO_PLACEHOLDER = "\x00MEMO\x00"


class GameVarsError(ValueError):
    """The file does not have the layout this tool understands."""


@dataclass(frozen=True)
class MemoEntry:
    skill_no: int
    memo: str  # attribute value as stored in the file (XML-escaped)
    raw: str  # the whole element, without indentation or line break


@dataclass(frozen=True)
class GameVars:
    slot_version: str
    slots: tuple[int, ...]  # SLOT_COUNT skill numbers, 0 = empty
    memo_version: str | None  # None when the memo section is absent
    memos: tuple[MemoEntry, ...]


@dataclass(frozen=True)
class _Span:
    start: int
    end: int
    version: str
    body: str | None  # None for a self-closed element


@dataclass(frozen=True)
class _Layout:
    slot: _Span
    memo: _Span | None
    newline: str


def xml_escape_attr(value: str) -> str:
    return (
        value.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def xml_unescape_attr(value: str) -> str:
    return (
        value.replace("&quot;", '"')
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&amp;", "&")
    )


def _body_lines(body: str) -> list[str]:
    return [line.strip() for line in body.split("\n") if line.strip()]


def _locate(text: str) -> _Layout:
    slot_matches = list(_SLOT_SECTION_RE.finditer(text))
    if len(slot_matches) != 1:
        raise GameVarsError(f"expected exactly one SkillCoolTimeSlot section, found {len(slot_matches)}")
    m = slot_matches[0]
    slot = _Span(m.start(), m.end(), m.group(1), m.group(2))

    memo_candidates = [
        _Span(x.start(), x.end(), x.group(1), x.group(2)) for x in _MEMO_OPEN_RE.finditer(text)
    ] + [_Span(x.start(), x.end(), x.group(1), None) for x in _MEMO_EMPTY_RE.finditer(text)]
    if len(memo_candidates) > 1:
        raise GameVarsError("expected at most one SkillCoolTimeMemo section")
    newline = "\r\n" if "\r\n" in text else "\n"
    return _Layout(slot, memo_candidates[0] if memo_candidates else None, newline)


def _parse_slot_body(body: str) -> tuple[int, ...]:
    entries: list[tuple[int, int]] = []
    for line in _body_lines(body):
        m = _SLOT_ENTRY_RE.fullmatch(line)
        if not m:
            raise GameVarsError("unexpected line inside SkillCoolTimeSlot")
        entries.append((int(m.group(1)), int(m.group(2))))
    if [slot for slot, _ in entries] != list(range(SLOT_COUNT)):
        raise GameVarsError(f"SkillCoolTimeSlot must contain exactly SlotNo 0..{SLOT_COUNT - 1} in order")
    return tuple(skill for _, skill in entries)


def _parse_memo_body(body: str) -> tuple[MemoEntry, ...]:
    entries: list[MemoEntry] = []
    for line in _body_lines(body):
        m = _MEMO_ENTRY_RE.fullmatch(line)
        if not m:
            raise GameVarsError("unexpected line inside SkillCoolTimeMemo")
        entries.append(MemoEntry(int(m.group(1)), m.group(2), line))
    return tuple(entries)


def parse(text: str) -> GameVars:
    """Read the slot and memo sections without changing anything."""
    layout = _locate(text)
    slots = _parse_slot_body(layout.slot.body or "")
    memo_version = None
    memos: tuple[MemoEntry, ...] = ()
    if layout.memo is not None:
        memo_version = layout.memo.version
        memos = _parse_memo_body(layout.memo.body) if layout.memo.body is not None else ()
    return GameVars(layout.slot.version, slots, memo_version, memos)


def _splice(text: str, edits: Sequence[tuple[int, int, str]]) -> str:
    out = text
    for start, end, replacement in sorted(edits, key=lambda e: e[0], reverse=True):
        out = out[:start] + replacement + out[end:]
    return out


def _pad(skills: Sequence[int]) -> list[int]:
    if len(skills) > SLOT_COUNT:
        raise GameVarsError(f"a combo can have at most {SLOT_COUNT} slots, got {len(skills)}")
    if any(s < 0 for s in skills):
        raise GameVarsError("skill numbers must be >= 0")
    return list(skills) + [0] * (SLOT_COUNT - len(skills))


def apply_combo(text: str, skills: Sequence[int], memos: Mapping[int, str]) -> str:
    """Return ``text`` with the slot list replaced and the combo's memos written.

    ``skills`` fills slots 0..N-1; the remaining slots become 0 (empty).
    ``memos`` maps SkillNo -> memo text. Existing memo entries for those SkillNos
    are replaced; all other existing entries are kept as they are. The memo
    section is sorted by SkillNo. The output is deterministic, so applying the
    same combo twice yields identical bytes.
    """
    padded = _pad(skills)
    layout = _locate(text)
    current = parse(text)
    newline = layout.newline

    kept = [m for m in current.memos if m.skill_no not in memos]
    added = [
        MemoEntry(skill_no, xml_escape_attr(value), f'<SkillCoolTimeMemo SkillNo="{skill_no}" Memo="{xml_escape_attr(value)}"/>')
        for skill_no, value in memos.items()
    ]
    entries = sorted(kept + added, key=lambda e: e.skill_no)

    slot_lines = [f'<SkillCoolTimeSlot Version="{layout.slot.version}">']
    slot_lines += [f'\t<SkillCoolTimeSlot SlotNo="{i}" SkillNo="{s}"/>' for i, s in enumerate(padded)]
    slot_lines.append("</SkillCoolTimeSlot>")
    slot_text = newline.join(slot_lines)

    if layout.memo is None:
        # Memo section absent: create it right after the slot section.
        memo_text = _render_memo_section(DEFAULT_MEMO_VERSION, entries, newline)
        edits = [
            (layout.slot.start, layout.slot.end, slot_text),
            (layout.slot.end, layout.slot.end, newline + memo_text),
        ]
    else:
        memo_text = _render_memo_section(layout.memo.version, entries, newline)
        edits = [
            (layout.slot.start, layout.slot.end, slot_text),
            (layout.memo.start, layout.memo.end, memo_text),
        ]
    return _splice(text, edits)


def _render_memo_section(version: str, entries: Sequence[MemoEntry], newline: str) -> str:
    if not entries:
        return f'<SkillCoolTimeMemo Version="{version}"/>'
    lines = [f'<SkillCoolTimeMemo Version="{version}">']
    lines += [f"\t{entry.raw}" for entry in entries]
    lines.append("</SkillCoolTimeMemo>")
    return newline.join(lines)


def skeleton(text: str) -> str:
    """The file with both managed sections replaced by placeholders.

    A missing memo section is represented as if it had been inserted right after
    the slot section, so an absent and a freshly created memo section compare equal.
    """
    layout = _locate(text)
    edits = [(layout.slot.start, layout.slot.end, _SLOT_PLACEHOLDER)]
    if layout.memo is None:
        edits.append((layout.slot.end, layout.slot.end, layout.newline + _MEMO_PLACEHOLDER))
    else:
        edits.append((layout.memo.start, layout.memo.end, _MEMO_PLACEHOLDER))
    return _splice(text, edits)


def verify(
    old_text: str,
    new_text: str,
    expected_skills: Sequence[int],
    expected_memos: Mapping[int, str],
) -> list[str]:
    """Check a written result against the original. Returns problems (empty list = OK)."""
    problems: list[str] = []
    try:
        old = parse(old_text)
        new = parse(new_text)
    except GameVarsError as exc:
        return [f"result does not parse: {exc}"]

    if list(new.slots) != _pad(expected_skills):
        problems.append("slot list does not match the expected list")

    new_ids = [m.skill_no for m in new.memos]
    if len(new_ids) != len(set(new_ids)):
        problems.append("duplicate SkillNo memo entries")

    new_memo = {m.skill_no: m.memo for m in new.memos}
    for skill_no, value in expected_memos.items():
        if new_memo.get(skill_no) != xml_escape_attr(value):
            problems.append(f"memo for SkillNo {skill_no} is missing or different")

    kept_old = {m.skill_no: m.memo for m in old.memos if m.skill_no not in expected_memos}
    for skill_no, memo in kept_old.items():
        if new_memo.get(skill_no) != memo:
            problems.append(f"unrelated memo for SkillNo {skill_no} was altered")
    if len(new.memos) != len(kept_old) + len(expected_memos):
        problems.append("unexpected number of memo entries")

    if skeleton(old_text) != skeleton(new_text):
        problems.append("bytes outside the slot and memo sections changed")

    old_crlf_only = old_text.count("\n") == old_text.count("\r\n")
    if old_crlf_only and new_text.count("\n") != new_text.count("\r\n"):
        problems.append("line endings are no longer CRLF")
    return problems
