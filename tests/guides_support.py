"""Synthetic classhub and client-export trees for the guide tests. Never reads the real game data."""

from __future__ import annotations

import json
import struct
from pathlib import Path
from typing import Any, Iterable

SITE_URL = "https://bdoclasshub.com/"


def png_bytes(width: int = 44, height: int = 44) -> bytes:
    """A PNG with a valid signature and IHDR chunk. Enough for the bundle's size check, not a drawable image."""
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    chunk = struct.pack(">I", len(ihdr)) + b"IHDR" + ihdr + struct.pack(">I", 0)
    return b"\x89PNG\r\n\x1a\n" + chunk


def skill_step(keys: str | None, skills: Iterable[tuple[int, str]], extra: str | None = None, annotation: str | None = None) -> str:
    """One ``li.combo-step`` shaped like the site's markup."""
    key_html = f'<div class="combo-keys"><kbd>{keys}</kbd></div>' if keys is not None else ""
    skill_html = "".join(
        f'<span class="combo-skill"><img class="combo-skill-icon" src="../skills/icons/{number}_skill.webp" '
        f'alt="" width="40" height="40" loading="lazy" /><span class="combo-skill-name">{name}</span></span>'
        for number, name in skills
    )
    extra_html = f'<span class="combo-step-extra">{extra}</span>' if extra else ""
    note_html = f'<span class="combo-form combo-form--dragonblood">{annotation}</span>' if annotation else ""
    return f'<li class="combo-step">{key_html}<div class="combo-skills">{skill_html}{note_html}{extra_html}</div></li>'


def chain(title: str, steps: Iterable[str], notes: Iterable[str] = ()) -> str:
    note_html = "".join(f'<p class="combo-repeat">{text}</p>' for text in notes)
    return (
        f'<div class="combo-chain"><h3 class="combo-chain-title">{title}</h3>'
        f'<ol class="combo-steps">{"".join(steps)}</ol>{note_html}</div>'
    )


def combo_section(section_id: str, title: str, body: str, kicker: str = "PvE Combo Guide") -> str:
    return (
        f'<section class="combo-guide" id="{section_id}" aria-labelledby="combo-guide-title" data-guide-panel="pve">'
        f'<div class="combo-guide-inner"><p class="combo-kicker">{kicker}</p>'
        f'<h2 id="combo-guide-title" class="combo-guide-title">{title}</h2>{body}</div></section>'
    )


def tips_aside(items: Iterable[str]) -> str:
    rows = "".join(f"<li>{text}</li>" for text in items)
    return f'<aside class="combo-notes"><h3 class="combo-notes-title">Tips</h3><ul>{rows}</ul></aside>'


def addon_section(cards: Iterable[tuple[int, str, str, list[tuple[str, str]]]]) -> str:
    html_cards = []
    for number, name, description, effects in cards:
        effect_html = "".join(
            f'<li class="addon-effect"><span class="addon-effect-label">{label}</span> '
            f'<span class="addon-effect-value">{value}</span></li>'
            for label, value in effects
        )
        html_cards.append(
            f'<article class="addon-card"><div class="addon-card-skill">'
            f'<img class="addon-card-icon" src="../skills/icons/{number}_addon.webp" alt="" width="56" height="56" />'
            f'<h3 class="addon-card-name">{name}</h3><p class="addon-card-desc">{description}</p></div>'
            f'<ul class="addon-card-effects-list">{effect_html}</ul></article>'
        )
    return combo_section("class-addons", "Class Addons", f'<div class="addon-list">{"".join(html_cards)}</div>', kicker="Skill Add-ons")


def page(*sections: str, head: str = "") -> str:
    return f"<!doctype html><html><head><title>Guide</title>{head}</head><body><main>{''.join(sections)}</main></body></html>"


def tamer_awakening_page() -> str:
    """A synthetic Tamer Awakening page: 13 steps in the damage chain, first 1937 and last 1932."""
    damage_names = [
        (1937, "Flow: Intimidation", "Hotkey", "opener"),
        (2555, "Legendary Beast Dance: Echo", "Shift+Q", None),
        (1921, "Allround Spinner", "Shift+RMB", "loop starts here"),
        (1939, "Flow: Soaring Strike", "RMB", None),
        (1940, "Flow: Full Moon", "LMB", None),
        (1912, "Skybound Stab", "F (hold)", None),
        (1934, "Flow: Ascension", "F", None),
        (6454, "Celestial Slam", "Shift+F", None),
        (1926, "Legendary Beast Dance", "A+RMB", None),
        (2555, "Legendary Beast Dance: Echo", "Shift+Q", None),
        (2829, "Jolt Stab", "Shift+Z", None),
        (1306, "Dark Gaoler", "F", "after Jolt Stab"),
        (1932, "Moonlight Strike", "Hotkey", None),
    ]
    damage_steps = [skill_step(keys, [(number, name)], extra=extra) for number, name, keys, extra in damage_names]
    self_buff = [
        skill_step("Hotkey", [(1347, "Summon Heuklang")]),
        skill_step("Hotkey", [(5467, "Absorb Heuklang")], extra="E buff"),
        skill_step("A+RMB", [(1926, "Legendary Beast Dance")], extra="Dragon stance, lasts 1 hour"),
    ]
    rotation = combo_section(
        "skill-rotation",
        "Tamer Awakening",
        chain("Self Buff", self_buff)
        + chain("Damage Combo (Dragon LBD)", damage_steps, notes=["After Moonlight Strike, go back to Allround Spinner."])
        + tips_aside(["Core DPS first: <span class=\"skill-inline\"><img class=\"skill-inline-icon\" src=\"../skills/icons/1921_allround_spinner.webp\" alt=\"\" />Allround Spinner</span>."]),
    )
    return page(rotation, addon_section([(1910, "Skybound Stab I", "Thrust the staff.", [("Attack Speed", "+10% for 12 sec")])]))


def write_classhub(
    root: Path,
    *,
    specs: list[dict[str, Any]],
    pages: dict[str, str],
    i18n: dict[str, dict[str, str]] | None = None,
) -> Path:
    """A classhub folder: manifest.json with the given spec rows, the given pages, and optional i18n files."""
    root.mkdir(parents=True, exist_ok=True)
    manifest = {"schema": 2, "base_url": SITE_URL, "specs": specs, "files": []}
    (root / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
    for rel, html in pages.items():
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(html, encoding="utf-8")
    for locale, values in (i18n or {}).items():
        path = root / "i18n" / locale / "common.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(values, ensure_ascii=False), encoding="utf-8")
    return root


def spec_row(class_slug: str, spec: str, local_path: str | None) -> dict[str, Any]:
    return {
        "class": class_slug,
        "spec": spec,
        "expert_path": f"experts/{class_slug}-{spec}/index.html",
        "local_path": local_path,
    }


CLIENT_CLASSES = {
    # classType: English name as the client writes it (Dark Knight is "Dark_Knight").
    16: "Tamer",
    27: "Dark_Knight",
}


def write_client(
    root: Path,
    *,
    skills: dict[int, tuple[str, str]],
    skill_icons: Iterable[int] | None = None,
    classes: dict[int, str] | None = None,
) -> Path:
    """A client export: skills_es.json, skills_icons.json, classes_icons.json and the PNG files they point to."""
    root.mkdir(parents=True, exist_ok=True)
    skill_icon_numbers = list(skills) if skill_icons is None else list(skill_icons)
    (root / "icons" / "skills").mkdir(parents=True, exist_ok=True)
    (root / "icons" / "classes").mkdir(parents=True, exist_ok=True)

    (root / "skills_es.json").write_text(
        json.dumps({str(no): {"name": name, "description": desc} for no, (name, desc) in skills.items()}, ensure_ascii=False),
        encoding="utf-8",
    )
    icon_entries = {}
    for number in skill_icon_numbers:
        file = f"icons/skills/{number}.png"
        (root / file).write_bytes(png_bytes(44, 44))
        icon_entries[str(number)] = {"file": file, "sourcePath": f"New_Icon/{number}.dds"}
    (root / "skills_icons.json").write_text(json.dumps(icon_entries), encoding="utf-8")

    class_names = CLIENT_CLASSES if classes is None else classes
    class_entries = {}
    for class_type, name in class_names.items():
        file = f"icons/classes/class_{class_type}.png"
        (root / file).write_bytes(png_bytes(50, 50))
        class_entries[str(class_type)] = {"id": f"Combine_Class_Icon_{name}", "name": name, "file": file, "rect": [1, 1, 51, 51]}
    (root / "classes_icons.json").write_text(json.dumps(class_entries), encoding="utf-8")
    return root
