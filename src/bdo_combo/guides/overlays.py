"""Hand-written changes to the imported guides: chains added to a guide, and skill numbers swapped in a guide.

The bdoclasshub pages do not list every skill a player needs. An overlay adds a chain the site lacks to one guide,
after the page is read and before the chains are classified, so the chain is typed, resolved and packed like any other.

The Succession swap replaces the "Sucesión: ..." skills (which need the Ancestral Contract) by the "S: ..." or
"Suprema: ..." skill of the same name. The last rank of the chosen skill is then taken by the rank rule (skill_ranks.py).
"""

from __future__ import annotations

import re
from collections.abc import Mapping
from typing import Any

from .. import log

SUCCESSION_SPEC = "succession"
SUCCESSION_PREFIX = "Sucesión"
# A skill name the Succession swap may use: "S:" or "Suprema:" (also "S :" and "Suprema :" in the client text).
SUCCESSION_TARGET_RE = re.compile(r"^(S|Suprema)\s*:")

# guide key -> chains appended in order. A step's ``keys`` is None when the input is not known.
# No overlays are needed at the moment. The Nova Succession "Summons Axian" step used "Sucesión: Contrato Ancestral",
# which has no S: or Suprema: counterpart in the client, so it is not kept.
OVERLAYS: dict[str, list[dict[str, Any]]] = {}

# guide key -> {skill number the site shows: skill number to use}. The targets are the English "Prime:" (S:) and
# "Absolute:" (Suprema:) skills of the same name, checked by name against the English tooltip data. When both exist,
# the S: skill is used. A "Sucesión:" skill that is not listed here keeps its number and is reported by the import.
SUCCESSION_SKILLS: dict[str, dict[int, int]] = {
    "berserker.succession": {
        1813: 5421,  # Ultimate: Frenzied Destroyer -> Prime: Frenzied Destroyer IV (S: Destructor Frenético IV)
        5459: 2902,  # Weakling Hunt -> Absolute: Weakling Hunt (Suprema : Caza de Débiles)
    },
    "drakania.succession": {
        7067: 6932,  # Azure Onslaught -> Absolute: Azure Onslaught (Suprema: Agresión Azul)
    },
    "hashashin.succession": {
        5704: 5668,  # Sand Warp -> Prime: Sand Warp (S: Paso Arenal)
        7152: 5493,  # Hourglass of Death -> Absolute: Hourglass of Death (Suprema : Reloj de Arena de la Deshonra)
        5703: 5603,  # Haladie Assault -> Absolute: Haladie Assault (Suprema : Asalto de Haladie)
    },
    "ninja.succession": {
        5322: 3112,  # Ankle Cutter -> Absolute: Ankle Cutter (Suprema : Cortar Tobillo)
        5321: 3113,  # Crescent Slash -> Absolute: Crescent Slash (Suprema : Corte Lunar)
    },
    "nova.succession": {
        5932: 5830,  # Rime Ice -> Absolute: Command: Rime Ice (Suprema: Mandato: Escarcha de Nieve)
        5968: 5758,  # Face the Darkness -> Absolute: Face the Darkness (Suprema: Frente a la Oscuridad)
    },
    "sorceress.succession": {
        4604: 4568,  # Ultimate: Shadow Eruption -> Prime: Shadow Eruption IV (S : Erupción de Sombra IV)
        4603: 2883,  # Beak Kick -> Absolute: Beak Kick (Suprema : Patada de Pico)
    },
    "tamer.succession": {
        4843: 2919,  # Tree Climb -> Absolute: Tree Climb (Suprema : Trepar Árboles)
    },
    "valkyrie.succession": {
        4857: 3220,  # Ultimate Righteous Charge -> Absolute: Ultimate Righteous Charge (Suprema : Extremo Carga de la Justicia)
        6475: 3215,  # Succession: Absolute: Heaven's Echo -> Absolute: Heaven's Echo (Suprema : Eco del Cielo), in a note
    },
    "warrior.succession": {
        4831: 2835,  # Charging Thrust -> Absolute: Charging Thrust (Suprema : Estocada Cargada)
    },
}


def apply_overlays(key: str, guide: dict[str, Any]) -> int:
    """Appends the overlay chains of a guide and returns how many were added."""
    chains = OVERLAYS.get(key, [])
    for chain in chains:
        guide["chains"].append({**chain, "steps": [dict(step) for step in chain["steps"]]})
    if chains:
        log.info("guide overlay applied", key=key, chains=len(chains))
    return len(chains)


def succession_target(key: str, skill_no: int) -> int | None:
    """The S: or Suprema: skill that replaces a skill of a guide, or None when the skill keeps its number."""
    return SUCCESSION_SKILLS.get(key, {}).get(skill_no)


def succession_target_problems(client_names: Mapping[int, str]) -> list[str]:
    """Every swap whose target is not an S: or Suprema: skill of the client export (a wrong number in this table)."""
    problems: list[str] = []
    for key, swaps in SUCCESSION_SKILLS.items():
        for source, target in swaps.items():
            name = client_names.get(target)
            if name is None or not SUCCESSION_TARGET_RE.match(name):
                problems.append(f"{key}: skill {source} is swapped for {target}, which is not an S: or Suprema: skill ({name!r})")
    return problems
