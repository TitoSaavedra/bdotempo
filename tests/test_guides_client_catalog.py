"""Tests for the client export reader, description cleaning and the classhub-to-class/spec mapping."""

import json
import tempfile
import unittest
from pathlib import Path

from bdo_combo.guides.catalog import (
    GuideRef,
    PageCache,
    SpecPlan,
    build_catalog,
    class_display_name,
    class_slug,
    plan_spec,
    spanish_class_name,
    spec_names,
    spec_sort_key,
)
from bdo_combo.guides.client import ClientExport, ClientExportError, clean_description, clean_name
from guides_support import chain, combo_section, page, skill_step, write_classhub, spec_row, write_client


class CleanDescriptionTests(unittest.TestCase):
    def test_colour_markup_is_removed_and_its_text_kept(self):
        text = "Habilidad Previa: <PAColor0xffe9bd23>[Destrucción de la Bestia]<PAOldColor>"

        self.assertEqual("Habilidad Previa: [Destrucción de la Bestia]", clean_description(text))

    def test_line_breaks_are_kept_and_spaces_collapsed(self):
        text = "Primera   línea\r\n\r\n\r\n\r\n  Segunda\tlínea  \n"

        self.assertEqual("Primera línea\n\nSegunda línea", clean_description(text))

    def test_key_placeholders_are_removed(self):
        text = "<PAColor0xffe9bd23>{TextBind:CASTING_CLICK_RMB} con la habilidad aprendida<PAOldColor>"

        self.assertEqual("con la habilidad aprendida", clean_description(text))

    def test_other_angle_bracket_words_are_game_text_and_stay(self):
        self.assertEqual("Elimina a <Jefe> Puturum", clean_description("Elimina a <Jefe> Puturum"))

    def test_entities_are_decoded(self):
        self.assertEqual("Tom & Jerry", clean_description("Tom &amp; Jerry"))

    def test_skill_name_is_one_line_without_markup(self):
        self.assertEqual("Sucesión: Caída de Rocas", clean_name("<PAColor0xffeb9261>Sucesión:   Caída\nde Rocas<PAOldColor>"))


class ClientExportTests(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.root = Path(self._dir.name) / "client"
        write_client(
            self.root,
            skills={1926: ("Baile de la Bestia Legendaria IV", "Descripción <PAColor0x1>x<PAOldColor>")},
            classes={16: "Tamer", 27: "Dark_Knight"},
        )

    def tearDown(self):
        self._dir.cleanup()

    def test_skill_names_and_descriptions_are_read_by_number(self):
        skill = ClientExport(self.root).skill_names()[1926]

        self.assertEqual("Baile de la Bestia Legendaria IV", skill.name)
        self.assertEqual("Descripción <PAColor0x1>x<PAOldColor>", skill.description)

    def test_skill_icon_path_is_relative_to_the_export_root(self):
        path = ClientExport(self.root).skill_icon(1926)

        self.assertEqual(self.root / "icons" / "skills" / "1926.png", path)
        self.assertIsNone(ClientExport(self.root).skill_icon(5))

    def test_class_entries_carry_the_client_name_and_icon(self):
        classes = ClientExport(self.root).classes()

        self.assertEqual("Dark_Knight", classes[27].name)
        self.assertEqual(self.root / "icons" / "classes" / "class_16.png", classes[16].icon)

    def test_missing_file_is_reported_by_name(self):
        (self.root / "skills_es.json").unlink()

        with self.assertRaisesRegex(ClientExportError, "skills_es.json"):
            ClientExport(self.root).skill_names()

    def test_non_numeric_key_is_rejected(self):
        (self.root / "skills_es.json").write_text(json.dumps({"abc": {"name": "x", "description": "y"}}), encoding="utf-8")

        with self.assertRaisesRegex(ClientExportError, "non-numeric"):
            ClientExport(self.root).skill_names()

    def test_entry_without_a_name_is_rejected(self):
        (self.root / "skills_es.json").write_text(json.dumps({"1": {"description": "y"}}), encoding="utf-8")

        with self.assertRaisesRegex(ClientExportError, "'name'"):
            ClientExport(self.root).skill_names()


class ClassNameTests(unittest.TestCase):
    def test_client_underscores_become_spaces_in_display_names(self):
        self.assertEqual("Dark Knight", class_display_name("Dark_Knight"))
        self.assertEqual("dark-knight", class_slug("Dark_Knight"))
        self.assertEqual("tamer", class_slug("Tamer"))

    def test_spanish_name_prefers_mexican_locale_then_general_spanish(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_classhub(root, specs=[], pages={}, i18n={
                "es-mx": {"Tamer": "Domadora"},
                "es": {"Tamer": "Domadora general", "Dark Knight": "Adalid oscura"},
            })

            self.assertEqual("Domadora", spanish_class_name(root, "Tamer"))
            self.assertEqual("Adalid oscura", spanish_class_name(root, "Dark Knight"))
            self.assertIsNone(spanish_class_name(root, "Musa"))


class SpecNameTests(unittest.TestCase):
    def test_known_specs_have_both_names(self):
        self.assertEqual(("Succession", "Sucesión"), spec_names("succession"))
        self.assertEqual(("Ascension", "Ascensión"), spec_names("ascension"))
        self.assertEqual(("Azminek", "Azminek"), spec_names("azminek"))

    def test_unknown_spec_is_capitalised_in_both_languages(self):
        self.assertEqual(("Newthing", "Newthing"), spec_names("newthing"))

    def test_awakening_and_succession_sort_before_others(self):
        ids = ["wootaru", "ascension", "succession", "awakening", "azminek"]

        self.assertEqual(["awakening", "succession", "ascension", "azminek", "wootaru"], sorted(ids, key=spec_sort_key))


class PlanSpecTests(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.root = Path(self._dir.name)
        guide = combo_section("skill-rotation", "Tamer", chain("Loop", [skill_step("F", [(1, "One")])]))
        write_classhub(
            self.root,
            specs=[],
            pages={
                "classes/tamer/awakening/index.html": page(guide),
                "classes/tamer/succession/index.html": page("<p>Coming soon</p>"),
                "classes/shai/index.html": page(
                    '<div><a href="../../experts/shai-azminek/index.html?from=class" data-path="talent">A</a>'
                    '<a href="../../experts/shai-jnny/index.html?from=class" data-path="talent">J</a></div>',
                    combo_section("skill-rotation", "Shai", chain("DPS", [skill_step("F", [(2, "Two")])])),
                ),
                "classes/archer/index.html": page(
                    '<a href="../../experts/archer-ascension/index.html" data-path="ascension">C</a>',
                    combo_section("skill-rotation", "Archer", chain("DPS", [skill_step("F", [(3, "Three")])])),
                ),
            },
        )
        self.pages = PageCache(self.root)

    def tearDown(self):
        self._dir.cleanup()

    def test_spec_with_its_own_page_uses_that_page(self):
        ref = plan_spec(self.pages, "tamer", "awakening", "classes/tamer/awakening/index.html")

        self.assertEqual(GuideRef("tamer.awakening", "classes/tamer/awakening/index.html", "awakening"), ref)

    def test_page_without_combo_content_gives_no_guide(self):
        self.assertIsNone(plan_spec(self.pages, "tamer", "succession", "classes/tamer/succession/index.html"))

    def test_missing_local_page_gives_no_guide(self):
        self.assertIsNone(plan_spec(self.pages, "tamer", "wukong", "classes/tamer/wukong/index.html"))

    def test_class_page_serves_the_spec_named_by_its_data_path(self):
        ref = plan_spec(self.pages, "archer", "ascension", None)

        self.assertEqual(GuideRef("archer.ascension", "classes/archer/index.html", "ascension"), ref)

    def test_expert_spec_uses_the_path_its_expert_link_names(self):
        ref = plan_spec(self.pages, "shai", "azminek", None)

        self.assertEqual(GuideRef("shai.talent", "classes/shai/index.html", "talent"), ref)

    def test_spec_with_no_page_and_no_expert_link_gives_no_guide(self):
        self.assertIsNone(plan_spec(self.pages, "dosa", "antido", None))
        self.assertIsNone(plan_spec(self.pages, "shai", "wootaru", None))


class BuildCatalogTests(unittest.TestCase):
    def test_classes_and_specs_are_sorted_and_named(self):
        plans = [
            SpecPlan("tamer", "succession", GuideRef("tamer.succession", "p", "succession")),
            SpecPlan("tamer", "awakening", GuideRef("tamer.awakening", "p", "awakening")),
            SpecPlan("shai", "azminek", GuideRef("shai.talent", "p", "talent")),
        ]
        classes = [
            (17, "Shai", None),
            (16, "Tamer", "Domadora"),
            (0, "Warrior", "Guerrero"),
        ]

        catalog = build_catalog(site_url="https://example.test/", classes=classes, plans=plans)

        self.assertEqual(1, catalog["schema"])
        self.assertEqual(["tamer", "shai"], [entry["slug"] for entry in catalog["classes"]])
        tamer = catalog["classes"][0]
        self.assertEqual({"en": "Tamer", "es": "Domadora"}, tamer["name"])
        self.assertEqual(["awakening", "succession"], [spec["id"] for spec in tamer["specs"]])
        self.assertEqual("tamer.awakening", tamer["specs"][0]["guide"])
        shai = catalog["classes"][1]
        self.assertEqual({"en": "Shai", "es": "Shai"}, shai["name"])
        self.assertEqual({"en": "Azminek", "es": "Azminek"}, shai["specs"][0]["displayName"])

    def test_class_without_a_guide_is_left_out(self):
        catalog = build_catalog(site_url="x", classes=[(0, "Warrior", None)], plans=[])

        self.assertEqual([], catalog["classes"])


if __name__ == "__main__":
    unittest.main()
