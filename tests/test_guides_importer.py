"""End-to-end tests for the importer, its outputs, the bundle and the CLI, on a synthetic classhub and client export."""

import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path

from bdo_combo.guides.bundle import BundleError, build_bundle, guide_resource
from bdo_combo.guides.client import ClientExport
from bdo_combo.guides.importer import import_guides, iter_skill_refs, write_outputs
from bdo_combo.guides.__main__ import main as guides_main
from guides_support import (
    chain,
    combo_section,
    page,
    png_bytes,
    skill_step,
    spec_row,
    tamer_awakening_page,
    write_classhub,
    write_client,
)
from support import quiet_logs

SKILLS = {
    1937: ("<PAColor0xffe9bd23>Flujo: Intimidación<PAOldColor>", "Habilidad Previa: <PAColor0xffe9bd23>[Destrucción]<PAOldColor>\nProyecta el último ataque."),
    2555: ("Baile de la Bestia Legendaria: Resonancia", "Resonancia."),
    1921: ("Aguja giratoria completa IV", "Gira."),
    1939: ("Flujo: Golpe Inmenso", "Golpe."),
    1940: ("Flujo: Luna Llena", "Luna."),
    1912: ("Garuda III", "Garuda."),
    1934: ("Flujo: Ascenso", "Asciende."),
    6454: ("Nube Destrozada", "Nube."),
    1926: ("Baile de la Bestia Legendaria IV", "Reúne a los espíritus.\nSegunda línea."),
    2829: ("Embestida de Tumbo", "Embiste."),
    1306: ("Guardiacárcel Negro", "Encarcela."),
    1932: ("Golpe de la Luna III", "Golpe lunar."),
    1347: ("Invocar a Heuklang XI", "Llama a Heuklang."),
    5467: ("Absorción de Heuklang", "Absorbe la fuerza de Heuklang."),
    1910: ("Garuda I", "Addon skill."),
}
NO_ICON = 1910  # named in the export, but its icon file is missing
CLIENT_CLASSES = {16: "Tamer", 17: "Shai", 27: "Dark_Knight", 29: "Archer", 34: "Deadeye"}


def tamer_succession_page() -> str:
    return page(combo_section("skill-rotation", "Tamer Succession", chain("Short", [skill_step("F", [(1347, "Summon Heuklang")])])))


def shai_class_page() -> str:
    links = (
        '<div class="class-experts">'
        '<a href="../../experts/shai-azminek/index.html?from=class" data-class="shai" data-path="talent">A</a>'
        '<a href="../../experts/shai-wootaru/index.html?from=class" data-class="shai" data-path="talent">W</a>'
        "</div>"
    )
    return page(links, combo_section("skill-rotation", "Shai", chain("DPS Combo", [skill_step("F", [(1347, "Summon Heuklang")])])))


def archer_class_page() -> str:
    link = '<a href="../../experts/archer-ascension/index.html" data-path="ascension">C</a>'
    return page(link, combo_section("skill-rotation", "Archer", chain("DPS", [skill_step("F", [(1912, "Garuda III")])])))


class SyntheticTreeTestCase(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.tmp = Path(self._dir.name)
        self.classhub = write_classhub(
            self.tmp / "classhub",
            specs=[
                spec_row("tamer", "awakening", "classes/tamer/awakening/index.html"),
                spec_row("tamer", "succession", "classes/tamer/succession/index.html"),
                spec_row("archer", "ascension", None),
                spec_row("shai", "azminek", None),
                spec_row("shai", "talent", None),
                spec_row("shai", "wootaru", None),
                spec_row("dosa", "antido", None),
            ],
            pages={
                "classes/tamer/awakening/index.html": tamer_awakening_page(),
                "classes/tamer/succession/index.html": tamer_succession_page(),
                "classes/shai/index.html": shai_class_page(),
                "classes/archer/index.html": archer_class_page(),
            },
            i18n={"es-mx": {"Tamer": "Domadora"}},
        )
        self.client = ClientExport(write_client(
            self.tmp / "client",
            skills=SKILLS,
            skill_icons=[no for no in SKILLS if no != NO_ICON],
            classes=CLIENT_CLASSES,
        ))
        self.combos = self.tmp / "combos"
        self.report = self.tmp / "reports" / "unresolved_skills.txt"

    def tearDown(self):
        self._dir.cleanup()

    def import_with_client(self, client_dir: Path):
        return import_guides(self.classhub, ClientExport(client_dir))


class ImportGuidesTests(SyntheticTreeTestCase):
    def setUp(self):
        super().setUp()
        self.result = import_guides(self.classhub, self.client)

    def test_guides_are_keyed_by_class_and_spec_and_shared_specs_use_one_guide(self):
        self.assertEqual(
            ["archer.ascension", "shai.talent", "tamer.awakening", "tamer.succession"],
            sorted(self.result.guides),
        )

    def test_tamer_awakening_has_the_13_step_damage_chain_from_1937_to_1932(self):
        guide = self.result.guides["tamer.awakening"]
        damage = next(chain_ for chain_ in guide["chains"] if chain_["title"] == "Damage Combo (Dragon LBD)")

        self.assertEqual(13, len(damage["steps"]))
        self.assertEqual(1937, damage["steps"][0]["skills"][0]["skillNo"])
        self.assertEqual(1932, damage["steps"][-1]["skills"][0]["skillNo"])

    def test_skills_resolve_spanish_names_from_the_client_export(self):
        self_buff = self.result.guides["tamer.awakening"]["chains"][0]

        self.assertEqual(
            {"skillNo": 5467, "nameEn": "Absorb Heuklang", "nameEs": "Absorción de Heuklang"},
            self_buff["steps"][1]["skills"][0],
        )
        self.assertEqual("Baile de la Bestia Legendaria IV", self_buff["steps"][2]["skills"][0]["nameEs"])

    def test_spanish_names_are_cleaned_of_colour_markup(self):
        flow = self.result.guides["tamer.awakening"]["chains"][1]["steps"][0]["skills"][0]

        self.assertEqual("Flujo: Intimidación", flow["nameEs"])

    def test_skill_missing_from_the_export_keeps_english_name_and_is_reported(self):
        empty_export = write_client(self.tmp / "client-empty", skills={}, classes={16: "Tamer"})

        result = self.import_with_client(empty_export)

        ref = result.guides["tamer.succession"]["chains"][0]["steps"][0]["skills"][0]
        self.assertEqual({"skillNo": 1347, "nameEn": "Summon Heuklang", "nameEs": None}, ref)
        self.assertIn(1347, result.unresolved)
        self.assertIn("tamer.succession", result.unresolved[1347].guides)

    def test_catalog_lists_classes_with_their_specs_and_guide_keys(self):
        catalog = self.result.catalog
        classes = {entry["slug"]: entry for entry in catalog["classes"]}

        self.assertEqual(["tamer", "shai", "archer"], [entry["slug"] for entry in catalog["classes"]])
        self.assertEqual(["awakening", "succession"], [spec["id"] for spec in classes["tamer"]["specs"]])
        self.assertEqual({"en": "Tamer", "es": "Domadora"}, classes["tamer"]["name"])
        self.assertEqual(["shai.talent"] * 3, [spec["guide"] for spec in classes["shai"]["specs"]])
        self.assertEqual(["azminek", "talent", "wootaru"], [spec["id"] for spec in classes["shai"]["specs"]])
        self.assertEqual(["ascension"], [spec["id"] for spec in classes["archer"]["specs"]])

    def test_class_without_guides_is_not_in_the_catalog(self):
        self.assertNotIn("dark-knight", [entry["slug"] for entry in self.result.catalog["classes"]])

    def test_specs_without_a_saved_guide_are_listed_as_problems(self):
        self.assertIn("dosa/antido: no saved page with a combo guide", self.result.problems)

    def test_stats_count_classes_specs_guides_chains_steps_and_skills(self):
        stats = self.result.stats()

        self.assertEqual(3, stats["classes"])
        self.assertEqual(6, stats["specs"])
        self.assertEqual(4, stats["guides"])
        self.assertEqual(13, len(self.result.guides["tamer.awakening"]["chains"][1]["steps"]))
        self.assertGreater(stats["unique_skills"], 10)
        self.assertEqual(0, stats["unresolved_skills"])

    def test_iter_skill_refs_covers_steps_notes_and_addons(self):
        numbers = [ref["skillNo"] for ref in iter_skill_refs(self.result.guides["tamer.awakening"])]

        self.assertIn(1921, numbers)  # tips box
        self.assertIn(NO_ICON, numbers)  # add-on


class WriteOutputsTests(SyntheticTreeTestCase):
    def test_writes_catalog_guides_and_report(self):
        result = import_guides(self.classhub, self.client)

        with quiet_logs():
            write_outputs(result, self.combos, self.report)

        catalog = json.loads((self.combos / "catalog.json").read_text(encoding="utf-8"))
        self.assertEqual(3, len(catalog["classes"]))
        guide = json.loads((self.combos / "guides" / "tamer.awakening.json").read_text(encoding="utf-8"))
        self.assertEqual("tamer", guide["classSlug"])
        self.assertEqual(16, guide["classType"])
        self.assertEqual("https://bdoclasshub.com/classes/tamer/awakening/index.html", guide["source"])
        self.assertIn("dosa/antido: no saved page", self.report.read_text(encoding="utf-8"))

    def test_stale_guide_files_are_removed(self):
        (self.combos / "guides").mkdir(parents=True)
        (self.combos / "guides" / "old.gone.json").write_text("{}", encoding="utf-8")

        with quiet_logs():
            write_outputs(import_guides(self.classhub, self.client), self.combos, self.report)

        self.assertFalse((self.combos / "guides" / "old.gone.json").exists())

    def test_report_lists_unresolved_skills_with_their_guides(self):
        empty_export = write_client(self.tmp / "client-empty", skills={}, classes={16: "Tamer"})
        result = self.import_with_client(empty_export)

        with quiet_logs():
            write_outputs(result, self.combos, self.report)

        text = self.report.read_text(encoding="utf-8")
        self.assertIn("Skills missing from skills_es.json:", text)
        self.assertIn("1347 | Summon Heuklang | tamer.awakening, tamer.succession", text)


class BundleTests(SyntheticTreeTestCase):
    def setUp(self):
        super().setUp()
        self.out = self.tmp / "generated"
        with quiet_logs():
            write_outputs(import_guides(self.classhub, self.client), self.combos, self.report)

    def build(self, client: ClientExport | None = None):
        with quiet_logs():
            return build_bundle(self.combos, client or self.client, self.out)

    def test_bundle_contains_the_referenced_skills_and_all_class_icons(self):
        stats = self.build()

        names = {path.name for path in self.out.iterdir()}
        self.assertTrue({"catalog.json", "skills.json", guide_resource("tamer.awakening")} <= names)
        self.assertIn("skill-1937.png", names)
        self.assertIn("class-16.png", names)
        self.assertIn("class-34.png", names)
        self.assertNotIn("skill-9999.png", names)
        self.assertEqual(len(CLIENT_CLASSES), stats.class_icons)
        self.assertEqual(4, stats.guides)

    def test_skills_lookup_has_english_spanish_and_clean_description(self):
        self.build()

        lookup = json.loads((self.out / "skills.json").read_text(encoding="utf-8"))
        self.assertEqual("Flow: Intimidation", lookup["1937"]["nameEn"])
        self.assertEqual("Flujo: Intimidación", lookup["1937"]["nameEs"])
        self.assertEqual("Habilidad Previa: [Destrucción]\nProyecta el último ataque.", lookup["1937"]["descriptionEs"])
        self.assertEqual("Garuda I", lookup[str(NO_ICON)]["nameEs"])

    def test_skill_without_an_icon_is_left_out_and_reported(self):
        stats = self.build()

        self.assertEqual([NO_ICON], stats.missing_skill_icons)
        self.assertFalse((self.out / f"skill-{NO_ICON}.png").exists())

    def test_icon_larger_than_64_pixels_is_refused(self):
        self.client.skill_icon(1937).write_bytes(png_bytes(100, 100))

        with self.assertRaisesRegex(BundleError, r"1937\.png is 100x100"):
            self.build()

    def test_non_png_icon_is_refused(self):
        self.client.skill_icon(1937).write_bytes(b"this is text, not a PNG file at all")

        with self.assertRaisesRegex(BundleError, "not a PNG"):
            self.build()

    def test_missing_class_icon_is_refused(self):
        (self.client.root / "icons" / "classes" / "class_16.png").unlink()

        with self.assertRaisesRegex(BundleError, "class icon for classType 16"):
            self.build()

    def test_stale_files_from_an_earlier_bundle_are_removed(self):
        self.out.mkdir(parents=True)
        (self.out / "skill-424242.png").write_bytes(png_bytes())

        self.build()

        self.assertFalse((self.out / "skill-424242.png").exists())

    def test_catalog_guide_without_a_file_is_refused(self):
        (self.combos / "guides" / "tamer.awakening.json").unlink()

        with self.assertRaisesRegex(BundleError, "tamer.awakening has no file"):
            self.build()

    def test_size_is_counted_for_every_written_file(self):
        stats = self.build()

        self.assertEqual(len(list(self.out.iterdir())), stats.files)
        self.assertEqual(sum(path.stat().st_size for path in self.out.iterdir()), stats.total_bytes)


class GuidesCliTests(SyntheticTreeTestCase):
    def run_cli(self, argv: list[str]) -> tuple[int, str]:
        out = io.StringIO()
        with quiet_logs(), contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = guides_main(argv)
        return code, out.getvalue()

    def test_import_then_bundle_run_end_to_end(self):
        generated = self.tmp / "generated"
        code_import, printed = self.run_cli([
            "import",
            "--classhub-dir", str(self.classhub),
            "--client-dir", str(self.client.root),
            "--combos-dir", str(self.combos),
            "--report", str(self.report),
        ])
        code_bundle, _ = self.run_cli([
            "bundle",
            "--combos-dir", str(self.combos),
            "--client-dir", str(self.client.root),
            "--out-dir", str(generated),
        ])

        self.assertEqual(0, code_import)
        self.assertEqual(0, code_bundle)
        self.assertIn("classes 3, specs 6, guides 4", printed)
        self.assertTrue((generated / "catalog.json").is_file())

    def test_missing_classhub_is_an_error_exit_not_a_traceback(self):
        code, _ = self.run_cli([
            "import",
            "--classhub-dir", str(self.tmp / "nope"),
            "--client-dir", str(self.client.root),
            "--combos-dir", str(self.combos),
            "--report", str(self.report),
        ])

        self.assertEqual(1, code)


if __name__ == "__main__":
    unittest.main()
