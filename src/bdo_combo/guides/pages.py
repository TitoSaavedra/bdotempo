"""Reads one saved bdoclasshub guide page into plain dictionaries.

A guide page holds ``section.combo-guide`` blocks. Each block can contain combo chains
(``div.combo-chain`` with an ``ol.combo-steps``), an ``aside.combo-notes`` list and
``article.addon-card`` add-ons. Skills are identified by the numeric prefix of the
icon file name (``../skills/icons/1926_legendary_beast_dance_iv.webp`` is skill 1926).
The English names on the page are kept; Spanish names come from the client export.
"""

from __future__ import annotations

import posixpath
import re
from dataclasses import dataclass, field
from typing import Any

from .dom import Element

_SKILL_NUMBER_RE = re.compile(r"^(\d+)_")

# Annotations shown next to a step's skills: solo names, form and buff chips, class-specific tags.
_STEP_ANNOTATION_CLASSES = frozenset(
    {"combo-skill-name--solo", "combo-form", "skill-fx", "specifics-meta", "specifics-tag"}
)
# Parts of a step that are read separately (keys, skills, extra text) and never repeated as annotations.
_STEP_PART_CLASSES = frozenset({"combo-skill", "combo-keys", "combo-step-extra"})
# Prose paragraphs of a chain, kept in the chain's notes in document order.
_CHAIN_NOTE_CLASSES = frozenset({"combo-intro", "combo-repeat", "combo-loop-cue", "combo-chain-note"})


@dataclass
class ParsedGuide:
    """Everything read from one guide page. Skill references carry ``skillNo`` and ``nameEn`` only."""

    title: str
    description: str | None
    chains: list[dict[str, Any]] = field(default_factory=list)
    notes: list[dict[str, Any]] = field(default_factory=list)
    addons: list[dict[str, Any]] = field(default_factory=list)
    problems: list[str] = field(default_factory=list)

    @property
    def has_combo_content(self) -> bool:
        return bool(self.chains or self.addons)


def skill_number_from_icon(src: str) -> int | None:
    """The skillNo in an icon path: the digits before the first underscore of the file name."""
    file_name = posixpath.basename(src.split("?", 1)[0])
    match = _SKILL_NUMBER_RE.match(file_name)
    return int(match.group(1)) if match else None


def parse_guide(root: Element) -> ParsedGuide:
    sections = root.select(tag="section", cls="combo-guide")
    first = sections[0] if sections else None
    title_el = first.first(tag="h2", cls="combo-guide-title") if first else None
    kicker = first.first(tag="p", cls="combo-kicker") if first else None
    guide = ParsedGuide(
        title=title_el.text() if title_el else "",
        description=(kicker.text() or None) if kicker else None,
    )

    for section in sections:
        section_id = section.attrs.get("id", "")
        for chain in section.select(cls="combo-chain"):
            guide.chains.append(_parse_chain(chain, section_id, guide.problems))
        for aside in section.select(tag="aside", cls="combo-notes"):
            guide.notes.extend(_parse_notes(aside, section_id, guide.problems))
        for card in section.select(cls="addon-card"):
            addon = _parse_addon(card, section_id, guide.problems)
            if addon is not None:
                guide.addons.append(addon)
    return guide


def has_expert_link(root: Element, class_slug: str, spec_id: str) -> str | None:
    """The ``data-path`` of the expert link to ``experts/<class>-<spec>/``, or None."""
    marker = f"experts/{class_slug}-{spec_id}/"
    for element in root.descendants():
        if element.tag == "a" and marker in element.attrs.get("href", "") and element.attrs.get("data-path"):
            return element.attrs["data-path"]
    return None


def data_paths(root: Element) -> set[str]:
    """Every non-empty ``data-path`` on the page (the spec paths a class page covers)."""
    return {el.attrs["data-path"] for el in root.descendants() if el.attrs.get("data-path")}


def _parse_chain(chain: Element, section_id: str, problems: list[str]) -> dict[str, Any]:
    title_el = chain.first(tag="h3", cls="combo-chain-title")
    steps = [_parse_step(li, problems) for li in chain.select(tag="li", cls="combo-step")]
    notes = [
        text
        for paragraph in chain.select(tag="p")
        if paragraph.classes & _CHAIN_NOTE_CLASSES and (text := paragraph.text())
    ]
    return {
        "section": section_id,
        "title": title_el.text() if title_el else "",
        "notes": notes,
        "steps": steps,
    }


def _parse_step(li: Element, problems: list[str]) -> dict[str, Any]:
    keys_el = li.first(tag="kbd")
    extras = [text for el in li.select(tag="span", cls="combo-step-extra") if (text := el.text())]
    return {
        "keys": (keys_el.text() or None) if keys_el else None,
        "skills": _skill_refs(li, problems),
        "extra": " ".join(extras) or None,
        "note": " · ".join(_step_annotations(li)) or None,
    }


def _skill_refs(container: Element, problems: list[str]) -> list[dict[str, Any]]:
    refs: list[dict[str, Any]] = []
    for span in container.select(tag="span", cls="combo-skill"):
        icon = span.first(tag="img", cls="combo-skill-icon")
        if icon is None:
            continue  # a text-only skill: it is kept in the step note instead
        number = skill_number_from_icon(icon.attrs.get("src", ""))
        if number is None:
            problems.append("combo skill with an icon that has no skill number was skipped")
            continue
        name_el = span.first(tag="span", cls="combo-skill-name")
        refs.append({"skillNo": number, "nameEn": name_el.text() if name_el else ""})
    return refs


def _step_annotations(li: Element) -> list[str]:
    found: list[str] = []

    def walk(element: Element) -> None:
        for child in element.elements():
            if child.classes & _STEP_ANNOTATION_CLASSES or _is_text_only_skill(child):
                if text := child.text():
                    found.append(text)
            elif not child.classes & _STEP_PART_CLASSES:
                walk(child)

    walk(li)
    return found


def _is_text_only_skill(element: Element) -> bool:
    return element.has_class("combo-skill") and element.first(tag="img", cls="combo-skill-icon") is None


def _parse_notes(aside: Element, section_id: str, problems: list[str]) -> list[dict[str, Any]]:
    """Each list item and each paragraph of a tips box is one note, with the skills named inline."""
    items: list[dict[str, Any]] = []
    for entry in aside.descendants():
        if entry.tag not in ("li", "p"):
            continue
        inline_refs: list[dict[str, Any]] = []
        for span in entry.select(tag="span", cls="skill-inline"):
            icon = span.first(tag="img", cls="skill-inline-icon")
            number = skill_number_from_icon(icon.attrs.get("src", "")) if icon else None
            if number is None:
                problems.append("inline skill without a numbered icon was skipped")
                continue
            inline_refs.append({"skillNo": number, "nameEn": span.text()})
        if text := entry.text():
            items.append({"section": section_id, "text": text, "skills": inline_refs})
    return items


def _parse_addon(card: Element, section_id: str, problems: list[str]) -> dict[str, Any] | None:
    icon = card.first(tag="img", cls="addon-card-icon")
    number = skill_number_from_icon(icon.attrs.get("src", "")) if icon else None
    name_el = card.first(tag="h3", cls="addon-card-name")
    if number is None:
        problems.append("add-on without a numbered icon was skipped")
        return None
    description = card.first(tag="p", cls="addon-card-desc")
    effects: list[dict[str, str]] = []
    for effect in card.select(tag="li", cls="addon-effect"):
        label = effect.first(tag="span", cls="addon-effect-label")
        value = effect.first(tag="span", cls="addon-effect-value")
        effects.append({"label": label.text() if label else "", "value": value.text() if value else ""})
    return {
        "section": section_id,
        "skill": {"skillNo": number, "nameEn": name_el.text() if name_el else ""},
        "description": (description.text() or None) if description else None,
        "effects": effects,
    }
