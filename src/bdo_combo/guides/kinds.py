"""Classifies each combo chain as buff, combo, mobility, cancels or other.

The kind decides where a chain goes in the app. Buffs and combos are shown first, and their steps are the
only ones applied automatically to the cooldown bar. Mobility and cancels are manual-only: the user places
those skills on the cooldown slots by hand. Other chains are shown collapsed.

Rules, first match wins (``classify_chain``):

1. ``class-cancels`` section: cancels.
2. ``class-mobility`` section: mobility. A buff-like title inside it (``Sprint Buff``, ``Swap to Awakening``) stays
   mobility: the site files it as movement, and mobility is manual only, so the safe side is not to apply it.
   High confidence when the title has a movement keyword, low otherwise.
3. A title that names a buff or a stance activation (``Self Buff``, ``Prebuff / Debuff``, ``Enter Red Blade``...):
   buff. High confidence in a combo section, medium elsewhere. In a reference section (``OTHER_SECTIONS``) it is
   buff only when its steps press keys; a list of buffs with no key presses (``Buffs & Debuffs`` in
   ``class-specifics``) is a reference, so it is other, high.
4. A combo section (``skill-rotation``, ``dps-priority``, ``alternative-combos``, ``easy-combo``,
   ``basic-combo``, ``skill-priority``, ``pack-to-pack``):
   a. a title about moving to or between packs (``Movement from pack-to-pack``, ``Teleport to Target``): mobility,
      medium. A movement skill named only as a trigger inside a combo title (``During E / Z Buff: After Every
      Moonlight Dash``) does not count: that chain is a combo;
   b. every step carries a buff marker (``E buff``, ``Dragon stance, lasts 1 hour``): buff, medium;
   c. otherwise combo.
5. ``class-specifics``, ``class-video``, ``advanced-information``, ``spiritforging``: other, high.
6. Any other section: other, low.

Confidence is ``low`` when the kind rests on the section alone and the title has no keyword for that kind,
when a combo-section chain has no title, when only some steps carry buff markers, and for unknown sections.
"""

from __future__ import annotations

import re
from collections.abc import Sequence
from dataclasses import dataclass

BUFF = "buff"
COMBO = "combo"
MOBILITY = "mobility"
CANCELS = "cancels"
OTHER = "other"

KINDS = (BUFF, COMBO, MOBILITY, CANCELS, OTHER)
# Chains whose steps are applied automatically to the cooldown bar.
AUTO_APPLY_KINDS = frozenset({BUFF, COMBO})
# Chains whose skills the user places by hand (never part of the automatic combo).
MANUAL_ONLY_KINDS = frozenset({MOBILITY, CANCELS})

HIGH = "high"
MEDIUM = "medium"
LOW = "low"

CANCELS_SECTION = "class-cancels"
MOBILITY_SECTION = "class-mobility"
COMBO_SECTIONS = frozenset({
    "skill-rotation",
    "dps-priority",
    "alternative-combos",
    "easy-combo",
    "basic-combo",
    "skill-priority",
    "pack-to-pack",
})
OTHER_SECTIONS = frozenset({"class-specifics", "class-video", "advanced-information", "spiritforging"})

# A whole title that names a buff or a stance/awakening activation. Matched on the normalised title.
_BUFF_TITLE_RE = re.compile(
    r"pre-?buffs?"
    r"|prebuff skills"
    r"|prebuffs? ?/ ?debuff"
    r"|self[- ]buffs?"
    r"|buffs?"
    r"|auto buffs?"
    r"|sprint buff"
    r"|buffs? (?:&|and) debuffs?"
    r"|pre-engage and buffs"
    r"|enter red blade"
    r"|fighting buddha state"
    r"|swap to (?:pre-)?awakening"
)
# A title about moving between packs or to a target. Only used to move a combo-section chain to mobility.
# "dash" is not here: "After Every Moonlight Dash" names the skill that triggers a combo, not a movement chain.
_MOBILITY_TITLE_RE = re.compile(r"movement|reposition|teleport|engage|distance|farther packs|pack[- ]to[- ]pack")
# A step extra that marks a buff activation: "E buff", "Z buff", "Dragon stance, lasts 1 hour", "prebuff",
# "3 minute buff". "During the BSR buff" is a timing inside a combo, not a buff, so it does not match.
_BUFF_EXTRA_RE = re.compile(
    r"^(?:e |z )?buffs?\b"
    r"|\bprebuffs?\b"
    r"|\bstance\b"
    r"|\blasts? (?:for )?\d+ ?(?:hours?|minutes?)\b"
    r"|\b\d+ minute (?:buff|cooldown)\b"
)
# Title words that match the kind a section suggests. A chain without one of them gets low confidence.
_COMBO_KEYWORD_RE = re.compile(
    r"combo|rotation|loop|filler|dps|damage|priority|core|burst|pve|opener|starter|infinite|lazy"
    r"|sustain|extension|single target|usual|skill|set \d|step \d|\bpart\b|\(alt\)|^\d\)|\d\) |rift|accel|bsr"
    r"|sacramentum|base form|fire|lightning|water|cooldown|pulling|ranged|standing|pack|everything|top|flexible"
    r"|prime|hotkey|quickslot|main|simple|fast|aoe|chain|prebuff"
)
_MOBILITY_KEYWORD_RE = re.compile(
    r"movement|move|reposition|teleport|dash|engage|distance|pack|jump|i-?frames?|back attacks?|behind|avoid"
    r"|chase|flying|ground|sprint|butterfly|breeze|extend|rift|example|cancel|collision|mobs|frontal|aoe|step"
    r"|opener|long|short|lazy|advance"
)


@dataclass(frozen=True)
class Verdict:
    """The kind of one chain, why it got that kind, and how sure the rule is."""

    kind: str
    reason: str
    confidence: str


def normalise_title(title: str) -> str:
    return " ".join(title.split()).lower()


def classify_chain(
    section: str,
    title: str,
    extras: Sequence[str | None] = (),
    keys: Sequence[str | None] = (),
) -> Verdict:
    """The kind of one chain from its section, its title, and the extra text and the keys of each of its steps."""
    text = normalise_title(title)

    if section == CANCELS_SECTION:
        return Verdict(CANCELS, f"section:{CANCELS_SECTION}", HIGH)

    if section == MOBILITY_SECTION:
        confidence = HIGH if _MOBILITY_KEYWORD_RE.search(text) else LOW
        return Verdict(MOBILITY, f"section:{MOBILITY_SECTION}", confidence)

    if _BUFF_TITLE_RE.fullmatch(text):
        if section in OTHER_SECTIONS and not any(keys):
            return Verdict(OTHER, f"section:{section}; title:{text}; no key presses", HIGH)
        confidence = HIGH if section in COMBO_SECTIONS else MEDIUM
        return Verdict(BUFF, f"title:{text}", confidence)

    if section in COMBO_SECTIONS:
        if text and _MOBILITY_TITLE_RE.search(text):
            return Verdict(MOBILITY, "title:movement", MEDIUM)

        marked = [bool(extra and _BUFF_EXTRA_RE.search(extra.lower())) for extra in extras]
        if marked and all(marked):
            return Verdict(BUFF, "steps:buff-markers", MEDIUM)
        if any(marked):
            return Verdict(COMBO, f"section:{section}; steps:partial-buff-markers", LOW)

        if not text:
            return Verdict(COMBO, f"section:{section}; no title", LOW)
        confidence = HIGH if _COMBO_KEYWORD_RE.search(text) else LOW
        return Verdict(COMBO, f"section:{section}", confidence)

    if section in OTHER_SECTIONS:
        return Verdict(OTHER, f"section:{section}", HIGH)

    return Verdict(OTHER, f"section:unknown:{section}", LOW)
