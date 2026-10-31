"""Checks the importer on the real saved classhub pages and client export.

Opt-in: these tests read the user's game export and the saved site, so they run only with
``BDO_REAL_DATA=1`` (``mise run test-real-data``). ``mise run test`` never reads game data.
Nothing is written: the importer runs in memory.
"""

import os
import unittest
from pathlib import Path

from bdo_combo.guides.client import ClientExport
from bdo_combo.guides.importer import import_guides
from support import quiet_logs

REPO_ROOT = Path(__file__).resolve().parents[1]
CLASSHUB_DIR = REPO_ROOT / "data" / "classhub"
CLIENT_DIR = Path(os.environ.get("BDO_CLIENT_DIR", r"D:\extracted_bdo\client"))
ENABLED = os.environ.get("BDO_REAL_DATA") == "1"


@unittest.skipUnless(ENABLED, "real game data: run `mise run test-real-data`")
class RealDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with quiet_logs():
            cls.result = import_guides(CLASSHUB_DIR, ClientExport(CLIENT_DIR))

    def test_tamer_awakening_damage_chain_has_13_steps_from_1937_to_1932(self):
        guide = self.result.guides["tamer.awakening"]
        damage = next(chain for chain in guide["chains"] if chain["title"] == "Damage Combo (Dragon LBD)")

        self.assertEqual(13, len(damage["steps"]))
        self.assertEqual(1937, damage["steps"][0]["skills"][0]["skillNo"])
        self.assertEqual(1932, damage["steps"][-1]["skills"][0]["skillNo"])

    def test_1926_and_5467_resolve_through_skills_es(self):
        refs = {ref["skillNo"]: ref for ref in self.result.skill_refs()}

        self.assertEqual("Baile de la Bestia Legendaria IV", refs[1926]["nameEs"])
        self.assertEqual("Absorción de Heuklang", refs[5467]["nameEs"])

    def test_every_class_with_a_guide_is_in_the_catalog(self):
        self.assertEqual(32, len(self.result.catalog["classes"]))

    def test_no_skill_reference_is_left_without_a_number(self):
        self.assertEqual([], [problem for problem in self.result.problems if "without" in problem])


if __name__ == "__main__":
    unittest.main()
