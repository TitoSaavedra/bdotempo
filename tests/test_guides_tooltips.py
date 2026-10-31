"""Tests for the skill tooltips read from the saved skill files. Synthetic files in a temporary folder only."""

import json
import tempfile
import unittest
from pathlib import Path

from bdo_combo.guides.tooltips import (
    GuideSource,
    build_tooltip,
    build_tooltips,
    find_entry,
    kind_for_page,
    load_class_skill_data,
    names_agree,
    plain_skill_name,
    tooltip_refs,
)

EN_TAMER = {
    "s": {
        "31": {
            "n": "Heuklang: Leaf Slash",
            "k": ["LMB", "LMB while sprinting"],
            "lv": "20",
            "cd": 4,
            "cost": [["MP", "Cost", "30"]],
            "d": [[1, 1347.0, 1, None], ["Dash attack damage 1347% x4", 1347.0, 4, 4]],
            "pvp": 49.2,
            "p": ["Super armor"],
            "x": ["Extra <PAColor0xffe9bd23>line<PAOldColor>"],
            "f": ["Maximum 10 targets", "Recover 40 MP per hit"],
        },
        "32": {"n": "Summon Heuklang XI", "lv": "5"},
        "33": {"n": "Succession: Falling Rock"},
    },
    "map": {
        "succession": {"31": "31", "32": "32"},
        "awakening": {"31": "32"},
    },
}
ES_MX_TAMER = {"s": {"31": {"n": "Tajo Ligero: Hoja", "lv": "20", "d": [[1, 1347.0, 1, None]]}}}
LABELS = {"en": {"attack": "Attack"}, "es-mx": {"attack": "Ataque"}}


def write_skill_files(classhub: Path, class_slug: str, en: dict, es_mx: dict | None = None) -> None:
    base = classhub / "data" / "skills"
    (base / "en").mkdir(parents=True, exist_ok=True)
    (base / "en" / f"{class_slug}.json").write_text(json.dumps(en, ensure_ascii=False), encoding="utf-8")
    if es_mx is not None:
        (base / "es-mx").mkdir(parents=True, exist_ok=True)
        (base / "es-mx" / f"{class_slug}.json").write_text(json.dumps(es_mx, ensure_ascii=False), encoding="utf-8")
    (base / "labels.json").write_text(json.dumps(LABELS, ensure_ascii=False), encoding="utf-8")


def ref(number: int, name: str) -> dict:
    return {"skillNo": number, "nameEn": name, "nameEs": None}


class SyntheticClasshubTestCase(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.classhub = Path(self._dir.name)
        write_skill_files(self.classhub, "tamer", EN_TAMER)

    def tearDown(self):
        self._dir.cleanup()


class PageKindTests(unittest.TestCase):
    def test_class_page_kind_is_the_map_kind_of_the_page(self):
        self.assertEqual("awakening", kind_for_page("classes/tamer/awakening/index.html"))
        self.assertEqual("succession", kind_for_page("classes\\tamer\\succession\\index.html"))

    def test_other_pages_use_the_overview_map(self):
        self.assertEqual("overview", kind_for_page("classes/tamer/index.html"))
        self.assertEqual("overview", kind_for_page("experts/tamer-x/index.html"))


class LookupTests(SyntheticClasshubTestCase):
    def test_entry_is_the_one_the_map_gives_for_the_page_kind(self):
        data = load_class_skill_data(self.classhub, "tamer")

        self.assertEqual("Summon Heuklang XI", find_entry(data, "classes/tamer/awakening/index.html", 31)[1]["n"])
        self.assertEqual("Heuklang: Leaf Slash", find_entry(data, "classes/tamer/succession/index.html", 31)[1]["n"])

    def test_skill_missing_from_the_map_uses_its_number_as_entry_id(self):
        data = load_class_skill_data(self.classhub, "tamer")

        self.assertEqual("Summon Heuklang XI", find_entry(data, "experts/tamer-x/index.html", 32)[1]["n"])

    def test_skill_without_an_entry_has_no_tooltip(self):
        data = load_class_skill_data(self.classhub, "tamer")

        self.assertIsNone(find_entry(data, "classes/tamer/succession/index.html", 999))
        self.assertIsNone(build_tooltip(data, "classes/tamer/succession/index.html", 999).tooltip)

    def test_missing_class_file_is_not_available(self):
        data = load_class_skill_data(self.classhub, "shai")

        self.assertFalse(data.available)
        self.assertIsNone(build_tooltip(data, "classes/shai/talent/index.html", 31).tooltip)

    def test_mexican_spanish_is_preferred_then_english_per_entry(self):
        write_skill_files(self.classhub, "tamer", EN_TAMER, ES_MX_TAMER)
        data = load_class_skill_data(self.classhub, "tamer")

        self.assertEqual("es-mx", find_entry(data, "classes/tamer/succession/index.html", 31)[0])
        self.assertEqual("en", find_entry(data, "classes/tamer/succession/index.html", 32)[0])


class TooltipContentTests(SyntheticClasshubTestCase):
    def setUp(self):
        super().setUp()
        self.data = load_class_skill_data(self.classhub, "tamer")

    def test_tooltip_has_every_field_of_the_skill_file(self):
        result = build_tooltip(self.data, "classes/tamer/succession/index.html", 31)

        self.assertEqual("Heuklang: Leaf Slash", result.name_en)
        self.assertEqual(
            {
                "name": "Heuklang: Leaf Slash",
                "lang": "en",
                "keys": ["LMB", "LMB while sprinting"],
                "level": 20,
                "cost": [{"type": "MP", "label": "Cost", "value": "30"}],
                "cooldownSec": 4.0,
                "damage": [
                    {"label": "Attack 1", "percent": 1347.0, "hits": 1, "maxHits": None},
                    {"label": "Dash attack damage 1347% x4", "percent": 1347.0, "hits": 4, "maxHits": 4},
                ],
                "pvpPercent": 49.2,
                "protection": ["Super armor"],
                "extras": ["Extra line"],
                "features": ["Maximum 10 targets", "Recover 40 MP per hit"],
            },
            result.tooltip,
        )

    def test_attack_label_of_a_numbered_hit_uses_the_language_of_the_entry(self):
        write_skill_files(self.classhub, "tamer", EN_TAMER, ES_MX_TAMER)
        data = load_class_skill_data(self.classhub, "tamer")

        tooltip = build_tooltip(data, "classes/tamer/succession/index.html", 31).tooltip

        self.assertEqual("es-mx", tooltip["lang"])
        self.assertEqual("Ataque 1", tooltip["damage"][0]["label"])
        self.assertEqual("Tajo Ligero: Hoja", tooltip["name"])
        self.assertEqual(20, tooltip["level"])
        self.assertIsNone(tooltip["cooldownSec"])

    def test_missing_fields_are_empty_or_none(self):
        # Entry 32 (the awakening page of skill 31) has a level and nothing else.
        result = build_tooltip(self.data, "classes/tamer/awakening/index.html", 31)

        self.assertEqual("Summon Heuklang XI", result.tooltip["name"])
        self.assertEqual(5, result.tooltip["level"])
        self.assertIsNone(result.tooltip["cooldownSec"])
        self.assertIsNone(result.tooltip["pvpPercent"])
        self.assertEqual([], result.tooltip["cost"])
        self.assertEqual([], result.tooltip["damage"])
        self.assertEqual([], result.tooltip["features"])


class NameComparisonTests(unittest.TestCase):
    def test_family_labels_and_ranks_are_ignored(self):
        self.assertTrue(names_agree("Crimson Gust", "Chaos: Crimson Gust"))
        self.assertTrue(names_agree("Rising Storm: Blaze", "Succession: Rising Storm: Blaze"))
        self.assertTrue(names_agree("Shadow Clone I", "Prime: Shadow Clone IV"))
        self.assertTrue(names_agree("Summon Heuklang", "Summon Heuklang XI"))

    def test_apostrophes_do_not_count(self):
        self.assertTrue(names_agree("Lights Trail", "Flow: Light's Trail"))

    def test_different_skills_do_not_agree(self):
        self.assertFalse(names_agree("Torpedo", "Spiral Torpedo III"))
        self.assertFalse(names_agree("Lightning Strike", "Summon Heuklang XI"))

    def test_missing_names_do_not_agree(self):
        self.assertFalse(names_agree("Leaf Slash", None))
        self.assertFalse(names_agree("", "Leaf Slash"))

    def test_plain_name_keeps_letters_and_digits_only(self):
        self.assertEqual("falling rock", plain_skill_name("Succession: Falling Rock"))
        self.assertEqual("garuda", plain_skill_name("Garuda III"))


class TooltipRefsTests(unittest.TestCase):
    def test_refs_cover_chains_of_every_kind_then_notes_then_add_ons(self):
        guide = {
            "chains": [
                {"kind": "combo", "steps": [{"skills": [ref(1, "A")]}]},
                {"kind": "other", "steps": [{"skills": [ref(2, "B")]}, {"skills": [ref(3, "C")]}]},
            ],
            "notes": [{"skills": [ref(4, "D")]}],
            "addons": [{"skill": ref(5, "E")}],
        }

        self.assertEqual([1, 2, 3, 4, 5], [item["skillNo"] for item in tooltip_refs(guide)])


class BuildTooltipsTests(SyntheticClasshubTestCase):
    def sources(self) -> list[GuideSource]:
        awakening = {
            "chains": [{"kind": "combo", "steps": [{"skills": [ref(31, "Summon Heuklang")]}]}],
            "notes": [],
            "addons": [],
        }
        succession = {
            "chains": [
                {"kind": "other", "steps": [{"skills": [ref(33, "Falling Rock")]}]},
                {"kind": "combo", "steps": [{"skills": [ref(31, "Leaf Slash")]}]},
            ],
            "notes": [{"skills": [ref(999, "Nothing")]}],
            "addons": [{"skill": ref(32, "Lightning Strike")}],
        }
        return [
            GuideSource("tamer.succession", "tamer", "classes/tamer/succession/index.html", succession),
            GuideSource("tamer.awakening", "tamer", "classes/tamer/awakening/index.html", awakening),
        ]

    def test_every_referenced_skill_gets_a_tooltip_in_key_order(self):
        result = build_tooltips(self.classhub, self.sources())

        self.assertEqual([31, 32, 33], sorted(result.tooltips))
        self.assertEqual("Succession: Falling Rock", result.tooltips[33]["name"])

    def test_first_guide_in_key_order_decides_the_tooltip_and_a_different_one_is_a_conflict(self):
        result = build_tooltips(self.classhub, self.sources())

        self.assertEqual("Summon Heuklang XI", result.tooltips[31]["name"])
        self.assertEqual(1, len(result.conflicts))
        self.assertIn("31", result.conflicts[0])
        self.assertIn("tamer.succession", result.conflicts[0])

    def test_skill_without_data_is_listed_with_its_guides(self):
        result = build_tooltips(self.classhub, self.sources())

        self.assertEqual({999: ["tamer.succession"]}, result.no_data)
        self.assertNotIn(999, result.tooltips)

    def test_name_that_differs_from_the_tooltip_is_reported_but_kept(self):
        result = build_tooltips(self.classhub, self.sources())

        self.assertEqual(1, len(result.name_mismatches))
        self.assertIn("skill 32", result.name_mismatches[0])
        self.assertIn("Summon Heuklang XI", result.tooltips[32]["name"])

    def test_class_without_a_skill_file_is_listed_as_missing(self):
        source = GuideSource("shai.talent", "shai", "classes/shai/talent/index.html", {
            "chains": [{"kind": "combo", "steps": [{"skills": [ref(31, "Leaf Slash")]}]}],
            "notes": [],
            "addons": [],
        })

        result = build_tooltips(self.classhub, [source])

        self.assertEqual(["shai"], result.missing_class_files)
        self.assertEqual({}, result.tooltips)
        self.assertEqual({31: ["shai.talent"]}, result.no_data)


if __name__ == "__main__":
    unittest.main()
