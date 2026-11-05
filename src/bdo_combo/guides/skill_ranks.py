"""Maps every skill number to the highest rank of its skill: the version a guide step always uses.

A guide names a skill at the rank the site shows, which is not always the last one ("Icy Thorns I" where "Icy Thorns IV"
exists). The rule is fixed: a step uses the last version of its skill, for every class and spec, including the skills
added by overlays.

The ranks come from the Scrappers export ``class_skills.json``. Each group is one skill with its ``ranks``; the highest
``rank`` field is the last version. A skill that no group holds (a newer skill) falls back to the client export names: a
Roman-numeral family such as "Name I", "Name II", "Name III" is one skill, and its highest level is the last version.
When that highest level has more than one skill number, the family is not guessed and the skill keeps its number.
"""

from __future__ import annotations

import re
from collections.abc import Mapping
from typing import Any

from .. import log

_ROMAN = {"I": 1, "II": 2, "III": 3, "IV": 4, "V": 5, "VI": 6, "VII": 7, "VIII": 8}
_RANK_SUFFIX_RE = re.compile(r"^(?P<base>.*\S) (?P<roman>I|II|III|IV|V|VI|VII|VIII)$")


def build_top_ranks(skill_table: Mapping[str, Any], client_names: Mapping[int, str]) -> dict[int, int]:
    """Maps the skill number of every skill with a higher rank to the skill number of its highest rank.

    Skills that are already the highest rank (or have only one rank) are not in the map: ``top_ranks.get(n, n)``
    gives the last version of any skill number.
    """
    top: dict[int, int] = {}
    grouped: set[int] = set()
    for group in skill_table.get("groups") or []:
        ranks = [rank for rank in group.get("ranks") or [] if _is_skill_no(rank.get("skillNo"))]
        grouped.update(rank["skillNo"] for rank in ranks)
        if len(ranks) < 2:
            continue
        last = max(ranks, key=lambda rank: (_rank_number(rank), rank["skillNo"]))["skillNo"]
        for rank in ranks:
            top[rank["skillNo"]] = last

    from_names = _family_tops(client_names, grouped)
    top.update(from_names)
    log.info("skill ranks built", lifted=len(top), from_names=len(from_names), grouped_skills=len(grouped))
    return top


def _family_tops(client_names: Mapping[int, str], grouped: set[int]) -> dict[int, int]:
    """Top rank of the Roman-numeral families of skills that no group holds."""
    levels: dict[str, dict[int, list[int]]] = {}
    for skill_no, name in client_names.items():
        match = _RANK_SUFFIX_RE.match(name or "")
        if match:
            levels.setdefault(match["base"], {}).setdefault(_ROMAN[match["roman"]], []).append(skill_no)

    tops: dict[int, int] = {}
    for skill_no, name in client_names.items():
        match = _RANK_SUFFIX_RE.match(name or "")
        if not match or skill_no in grouped:
            continue
        family = levels[match["base"]]
        highest = max(family)
        if len(family[highest]) != 1:
            continue
        last = family[highest][0]
        if last != skill_no:
            tops[skill_no] = last
    return tops


def _is_skill_no(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _rank_number(rank: Mapping[str, Any]) -> int:
    value = rank.get("rank")
    return value if isinstance(value, int) and not isinstance(value, bool) else 0
