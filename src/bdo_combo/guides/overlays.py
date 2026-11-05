"""Hand-written chains added to the imported guides.

The bdoclasshub pages do not list every skill a player needs. An overlay adds a chain the site lacks to one guide,
after the page is read and before the chains are classified, so the chain is typed, resolved and packed like any other.
"""

from __future__ import annotations

from typing import Any

from .. import log

# guide key -> chains appended in order. A step's ``keys`` is None when the input is not known.
OVERLAYS: dict[str, list[dict[str, Any]]] = {
    "nova.succession": [
        {
            "section": "skill-rotation",
            "title": "Self Buffs",
            "notes": [],
            "steps": [
                {
                    "keys": None,
                    "skills": [{"skillNo": 5939, "nameEn": "Ancestral Contract"}],
                    "extra": None,
                    "note": "Summons Axian",
                },
            ],
        },
    ],
}


def apply_overlays(key: str, guide: dict[str, Any]) -> int:
    """Appends the overlay chains of a guide and returns how many were added."""
    chains = OVERLAYS.get(key, [])
    for chain in chains:
        guide["chains"].append({**chain, "steps": [dict(step) for step in chain["steps"]]})
    if chains:
        log.info("guide overlay applied", key=key, chains=len(chains))
    return len(chains)
