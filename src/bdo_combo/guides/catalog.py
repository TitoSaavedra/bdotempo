"""Maps the saved bdoclasshub pages to classes and specs.

The site's ``manifest.json`` lists one row per class and spec with the spec page
(``local_path``, or ``null`` when the site has none). Some specs have no page of their
own: the guide is then the class page (``classes/<class>/index.html``), and the spec is
the ``data-path`` of the expert link (``experts/<class>-<spec>/``) on that page. Shai's
three experts (Azminek, Talent, Wootaru) all link to the same Talent guide, so they share
one guide file.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .dom import Element, parse_html
from .pages import ParsedGuide, data_paths, has_expert_link, parse_guide

MANIFEST_FILE = "manifest.json"

# Spec ids and their names. Display names are as the site and the client use them; Shai's
# expert specs keep the expert name, as the site shows them.
SPEC_NAMES: dict[str, tuple[str, str]] = {
    # id: (English, Spanish)
    "awakening": ("Awakening", "Despertar"),
    "succession": ("Succession", "Sucesión"),
    "ascension": ("Ascension", "Ascensión"),
    "purgatum": ("Purgatum", "Purgatum"),
    "sacramentum": ("Sacramentum", "Sacramentum"),
    "talent": ("Talent", "Talent"),
    "azminek": ("Azminek", "Azminek"),
    "wootaru": ("Wootaru", "Wootaru"),
}
# Specs listed first, in this order; any other spec follows, alphabetically.
SPEC_ORDER = ("awakening", "succession", "ascension")
# Spanish class names: the Mexican Spanish site locale first, then the general Spanish one.
CLASS_NAME_LOCALES = ("es-mx", "es")


@dataclass(frozen=True)
class GuideRef:
    """A guide file: ``key`` is ``<class>.<spec>`` and names the output file; ``page`` is relative to the site root."""

    key: str
    page: str
    guide_spec: str


@dataclass(frozen=True)
class SpecPlan:
    class_slug: str
    spec_id: str
    guide: GuideRef


class PageCache:
    """Reads and parses each saved page once."""

    def __init__(self, classhub_dir: Path) -> None:
        self.classhub_dir = classhub_dir
        self._roots: dict[str, Element | None] = {}
        self._guides: dict[str, ParsedGuide] = {}

    def exists(self, page: str) -> bool:
        return (self.classhub_dir / page).is_file()

    def root(self, page: str) -> Element:
        if page not in self._roots:
            path = self.classhub_dir / page
            self._roots[page] = parse_html(path.read_text(encoding="utf-8")) if path.is_file() else None
        root = self._roots[page]
        if root is None:
            raise FileNotFoundError(page)
        return root

    def guide(self, page: str) -> ParsedGuide:
        if page not in self._guides:
            self._guides[page] = parse_guide(self.root(page))
        return self._guides[page]


def load_manifest(classhub_dir: Path) -> dict[str, Any]:
    path = classhub_dir / MANIFEST_FILE
    if not path.is_file():
        raise FileNotFoundError(f"{path} not found; run `mise run fetch-classhub` first")
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data.get("specs"), list):
        raise ValueError(f"{path} has no 'specs' list")
    return data


def plan_spec(pages: PageCache, class_slug: str, spec_id: str, local_path: str | None) -> GuideRef | None:
    """The guide of one manifest row, or None when no saved page holds a combo guide for it."""
    if local_path is not None:
        if not pages.exists(local_path):
            return None
        if not pages.guide(local_path).has_combo_content:
            return None
        return GuideRef(key=f"{class_slug}.{spec_id}", page=local_path, guide_spec=spec_id)

    page = f"classes/{class_slug}/index.html"
    if not pages.exists(page) or not pages.guide(page).has_combo_content:
        return None
    covered = data_paths(pages.root(page))
    if spec_id in covered:
        guide_spec = spec_id
    else:
        linked = has_expert_link(pages.root(page), class_slug, spec_id)
        if linked is None or linked not in covered:
            return None
        guide_spec = linked
    return GuideRef(key=f"{class_slug}.{guide_spec}", page=page, guide_spec=guide_spec)


def plan_specs(pages: PageCache, manifest: dict[str, Any], problems: list[str]) -> list[SpecPlan]:
    plans: list[SpecPlan] = []
    for row in manifest["specs"]:
        class_slug = str(row["class"])
        spec_id = str(row["spec"])
        guide = plan_spec(pages, class_slug, spec_id, row.get("local_path"))
        if guide is None:
            problems.append(f"{class_slug}/{spec_id}: no saved page with a combo guide")
            continue
        plans.append(SpecPlan(class_slug=class_slug, spec_id=spec_id, guide=guide))
    return plans


def spec_names(spec_id: str) -> tuple[str, str]:
    """(English, Spanish) display names. Unknown specs show their id capitalised in both languages."""
    if spec_id in SPEC_NAMES:
        return SPEC_NAMES[spec_id]
    capitalised = spec_id[:1].upper() + spec_id[1:]
    return capitalised, capitalised


def spec_sort_key(spec_id: str) -> tuple[int, str]:
    return (SPEC_ORDER.index(spec_id) if spec_id in SPEC_ORDER else len(SPEC_ORDER), spec_id)


def class_display_name(raw_name: str) -> str:
    """The English class name as shown to people: the client writes "Dark_Knight" for "Dark Knight"."""
    return " ".join(raw_name.replace("_", " ").split())


def class_slug(english_name: str) -> str:
    """The site's class slug: lower case, spaces as hyphens ("Dark Knight" is "dark-knight")."""
    return class_display_name(english_name).lower().replace(" ", "-")


def spanish_class_name(classhub_dir: Path, english_name: str) -> str | None:
    for locale in CLASS_NAME_LOCALES:
        path = classhub_dir / "i18n" / locale / "common.json"
        if not path.is_file():
            continue
        translations = json.loads(path.read_text(encoding="utf-8"))
        value = translations.get(english_name)
        if isinstance(value, str) and value.strip():
            return value
    return None


def build_catalog(
    *,
    site_url: str,
    classes: list[tuple[int, str, str | None]],
    plans: list[SpecPlan],
) -> dict[str, Any]:
    """The catalog JSON: classes with their names and the specs that have a guide.

    ``classes`` holds (classType, English name, Spanish name or None), for every class of the client export.
    Classes without a guide are left out.
    """
    by_slug: dict[str, list[SpecPlan]] = {}
    for plan in plans:
        by_slug.setdefault(plan.class_slug, []).append(plan)

    entries: list[dict[str, Any]] = []
    for class_type, english, spanish in sorted(classes, key=lambda item: item[0]):
        slug = class_slug(english)
        specs = by_slug.get(slug)
        if not specs:
            continue
        spec_entries = []
        for plan in sorted(specs, key=lambda item: spec_sort_key(item.spec_id)):
            en, es = spec_names(plan.spec_id)
            spec_entries.append({
                "id": plan.spec_id,
                "displayName": {"en": en, "es": es},
                "guide": plan.guide.key,
            })
        display = class_display_name(english)
        entries.append({
            "classType": class_type,
            "slug": slug,
            "name": {"en": display, "es": spanish or display},
            "specs": spec_entries,
        })
    return {"schema": 1, "source": site_url, "classes": entries}
