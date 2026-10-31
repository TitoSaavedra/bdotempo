"""Synthetic fixtures for the tests. Never reads the user's real game data."""

from __future__ import annotations

import io
import json
import tempfile
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator, Sequence
from unittest import mock

from bdo_combo import backup, log
from bdo_combo.config import Settings

SLOT_SKILLS = [6457, 5157, 5167, 5179, 5161, 0, 5172, 5177, 5164, 5014, 5124, 0, 5153, 5181, 0, 5152, 5180, 0, 0, 0]
MEMO_ENTRIES = [
    '<SkillCoolTimeMemo SkillNo="5011" Memo="DIR + DER"/>',
    '<SkillCoolTimeMemo SkillNo="5103" Memo=" R 2"/>',
    '<SkillCoolTimeMemo SkillNo="5124" Memo="S + F"/>',
    '<SkillCoolTimeMemo SkillNo="5154" Memo="S + IZQ"/>',
    '<SkillCoolTimeMemo SkillNo="5200" Memo="A &amp; B &quot;x&quot; &lt;y&gt;"/>',
    '<SkillCoolTimeMemo SkillNo="5162" Memo="SH + DER "/>',
]
PCID_A = "10000000001891118"
PCID_B = "10000000001453084"
ACCOUNT = "330"


def game_file_text(memo_mode: str = "present", slots: Sequence[int] = SLOT_SKILLS) -> str:
    """A synthetic gameVariable.xml: CRLF, tabs, unrelated fragments, no BOM.

    memo_mode: "present" (full section), "missing" (no section), "self_closed".
    """
    lines = [
        '<CheckQuestList Version="1">',
        '\t<Quest Id="1" State="2"/>',
        "</CheckQuestList>",
        '<QuestSortType Version="1">',
        "</QuestSortType>",
        '<ShortcutKey Version="15">',
        '\t<Key Index="0" Code="81" Note="Q &amp; W"/>',
        "</ShortcutKey>",
        '<ChattingMacro Version="2"/>',
        '<SkillCoolTimeSlot Version="2">',
    ]
    lines += [f'\t<SkillCoolTimeSlot SlotNo="{i}" SkillNo="{s}"/>' for i, s in enumerate(slots)]
    lines += [
        "</SkillCoolTimeSlot>",
        '<ItemLockedInInventory Version="3"/>',
        '<QuestOption Version="2">',
        '\t<Option Name="x &amp; y"/>',
        "</QuestOption>",
    ]
    if memo_mode == "present":
        lines += ['<SkillCoolTimeMemo Version="1">']
        lines += [f"\t{entry}" for entry in MEMO_ENTRIES]
        lines += ["</SkillCoolTimeMemo>"]
    elif memo_mode == "self_closed":
        lines += ['<SkillCoolTimeMemo Version="1"/>']
    elif memo_mode != "missing":
        raise ValueError(memo_mode)
    lines += ['<UISettingPreset Version="1"/>', '<PetOrderList Version="3"/>']
    return "\r\n".join(lines) + "\r\n\r\n"


def skill_table_rows() -> list[tuple[int, str, list[int], str | None]]:
    """(skillNo, group name, classes, rank name or None)."""
    return [
        (1937, "Flujo: Intimidación", [16], "Flujo: Intimidación"),
        (2555, "Baile de la Bestia Legendaria: Resonancia", [16], None),
        (1921, "Aguja giratoria completa I", [16], "Aguja giratoria completa IV"),
        (1918, "Aguja giratoria completa I", [16], "Aguja giratoria completa I"),
        (1939, "Flujo: Golpe Inmenso", [16], None),
        (1940, "Flujo: Luna Llena", [16], None),
        (1912, "Garuda I", [16], "Garuda III"),
        (1934, "Flujo: Ascenso", [16], None),
        (6454, "Nube Destrozada", [16], None),
        (1926, "Baile de la Bestia Legendaria I", [16], "Baile de la Bestia Legendaria IV"),
        (17, "Onda de Tumbo I", [16], "Onda de Tumbo III"),
        (1306, "Guardiacárcel Negro", [16], None),
        (1932, "Golpe de la Luna I", [16], "Golpe de la Luna III"),
        (1347, "Invocar a Heuklang I", [16], "Invocar a Heuklang XI"),
        (231, "Absorción de Heuklang I", [16], "Absorción de Heuklang VI"),
        (5011, "Guardian Only I", [5], "Guardian Only III"),
        (9001, "<PAColor0xff2bbbff>Markup Skill<PAOldColor> I", [16], None),
        (5124, "Tamer Test Skill I", [16], None),
    ]


def write_skill_table(path: Path) -> Path:
    groups = []
    for skill_no, group_name, classes, rank_name in skill_table_rows():
        rank: dict = {"rank": 1, "skillKey": skill_no * 10, "skillNo": skill_no, "skillLevel": 1, "kind": 1}
        if rank_name is not None:
            rank["name"] = rank_name
        groups.append({"key": skill_no, "name": group_name, "classes": classes, "ranks": [rank]})
    path.write_text(json.dumps({"groups": groups, "trees": [], "treeMetadata": {}}, ensure_ascii=False), encoding="utf-8")
    return path


def combo_document(
    *,
    combo: list[dict] | None = None,
    priority: list[dict] | None = None,
    other: list[dict] | None = None,
    layout: dict | None = None,
) -> dict:
    return {
        "schema": 2,
        "class": "tamer",
        "classId": 16,
        "spec": "awakening",
        "source": "test",
        "description": "test combo",
        "layout": layout or {"combo": [0, 11], "priorityBuffs": [12, 14], "otherBuffs": [15, 19]},
        "slots": combo if combo is not None else [],
        "priorityBuffs": priority if priority is not None else [],
        "otherBuffs": other if other is not None else [],
    }


def entry(skill: str, skill_name: str, skill_no: int, memo: str | None = None, optional: bool | None = None) -> dict:
    data: dict = {"skill": skill, "skillName": skill_name, "skillNo": skill_no}
    if memo is not None:
        data["memo"] = memo
    if optional is not None:
        data["optional"] = optional
    return data


def write_combo(path: Path, document: dict) -> Path:
    path.write_text(json.dumps(document, ensure_ascii=False, indent=2), encoding="utf-8")
    return path


def tamer_combo_document() -> dict:
    return combo_document(
        combo=[
            entry("Flow: Intimidation", "Flujo: Intimidación", 1937, "R 1"),
            entry("Allround Spinner", "Aguja giratoria completa", 1921, "SH + DER"),
            entry("Skybound Stab", "Garuda", 1912, "F (mantener)"),
            entry("Legendary Beast Dance", "Baile de la Bestia Legendaria", 1926, "A + DER"),
            entry("Jolt Stab", "Onda de Tumbo", 17, "SH + Z"),
            entry("Dark Gaoler", "Guardiacárcel Negro", 1306, "F", optional=True),
        ],
        priority=[
            entry("Summon Heuklang", "Invocar a Heuklang", 1347, ""),
            entry("Absorb Heuklang", "Absorción de Heuklang", 231, "E"),
        ],
        layout={"combo": [0, 5], "priorityBuffs": [12, 14], "otherBuffs": [15, 19]},
    )


class CharacterFixture:
    """A temporary data dir, skills file, backups dir and a character for one account."""

    def __init__(self, root: Path) -> None:
        self.root = root
        self.data_dir = root / "data"
        self.account_dir = self.data_dir / "UserCache" / ACCOUNT / "11"
        self.skills_json = write_skill_table(root / "class_skills.json")
        self.backups_dir = root / "backups"

    def add_character(self, pcid: str, text: str | None = None) -> Path:
        folder = self.account_dir / pcid
        folder.mkdir(parents=True, exist_ok=True)
        path = folder / "gameVariable.xml"
        path.write_bytes((text if text is not None else game_file_text()).encode("utf-8"))
        return path

    def settings(self, **overrides) -> Settings:
        values = dict(
            data_dir=self.data_dir,
            account=None,
            skills_json=self.skills_json,
            backups_dir=self.backups_dir,
            log_file=None,
            verbose=False,
        )
        values.update(overrides)
        return Settings(**values)

    def cli_args(self) -> list[str]:
        return [
            "--data-dir", str(self.data_dir),
            "--skills-json", str(self.skills_json),
            "--backups-dir", str(self.backups_dir),
        ]


@contextmanager
def temp_root() -> Iterator[Path]:
    with tempfile.TemporaryDirectory() as tmp:
        yield Path(tmp)


@contextmanager
def quiet_logs() -> Iterator[io.StringIO]:
    """Send JSON logs to a buffer and return it, so tests can inspect warnings."""
    buffer = io.StringIO()
    log.configure(stream=buffer, verbose=True)
    try:
        yield buffer
    finally:
        log.configure(stream=io.StringIO())


@contextmanager
def game_not_running() -> Iterator[None]:
    with mock.patch.object(backup, "is_game_running", return_value=False):
        yield


def log_messages(buffer: io.StringIO) -> list[dict]:
    return [json.loads(line) for line in buffer.getvalue().splitlines() if line.strip()]
