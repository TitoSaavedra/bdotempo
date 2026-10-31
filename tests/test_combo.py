import unittest
from pathlib import Path

from bdo_combo import apply
from bdo_combo.config import REPO_ROOT, ToolError
from bdo_combo.skills import SkillRef, check_skill_refs, load_skill_index, names_match, strip_rank_suffix
from support import (
    combo_document,
    entry,
    log_messages,
    quiet_logs,
    tamer_combo_document,
    temp_root,
    write_combo,
    write_skill_table,
)


def load(root: Path, document: dict) -> apply.Combo:
    return apply.load_combo(write_combo(root / "combo.json", document))


class ComboFileTest(unittest.TestCase):
    def test_repo_tamer_combo_loads_and_plans_expected_slots(self) -> None:
        with quiet_logs():
            combo = apply.load_combo(REPO_ROOT / "combos" / "tamer.awakening.json")
            plan = apply.build_plan(combo, {}, skip_optional=False)
        self.assertEqual(16, combo.class_id)
        self.assertEqual(15, len(combo.entries))
        self.assertEqual(1347, plan.skills[12])
        self.assertEqual(231, plan.skills[13])
        self.assertEqual(1926, plan.skills[14])
        self.assertEqual([0, 0, 0, 0, 0], list(plan.skills[15:]))
        self.assertEqual("R 1", plan.memos[1937])
        self.assertEqual("SH + DER", plan.memos[1921])
        self.assertNotIn(1347, plan.memos)  # memo "" creates no entry
        self.assertEqual("E", plan.memos[231])
        self.assertEqual("A + DER", plan.memos[1926])  # first occurrence (combo slot 8) wins

    def test_overlapping_layout_ranges_rejected(self) -> None:
        with temp_root() as root:
            document = combo_document(
                combo=[entry("a", "A", 1937)],
                layout={"combo": [0, 11], "priorityBuffs": [11, 14], "otherBuffs": [15, 19]},
            )
            with self.assertRaisesRegex(ToolError, "overlap"):
                load(root, document)

    def test_layout_outside_0_19_rejected(self) -> None:
        with temp_root() as root:
            document = combo_document(
                combo=[entry("a", "A", 1937)],
                layout={"combo": [0, 20], "priorityBuffs": [12, 14], "otherBuffs": [15, 19]},
            )
            with self.assertRaisesRegex(ToolError, "0 <= first <= last <= 19"):
                load(root, document)

    def test_list_longer_than_its_range_rejected(self) -> None:
        with temp_root() as root:
            document = combo_document(
                combo=[entry("a", "A", 1), entry("b", "B", 2), entry("c", "C", 3)],
                layout={"combo": [0, 1], "priorityBuffs": [12, 14], "otherBuffs": [15, 19]},
            )
            with self.assertRaisesRegex(ToolError, "holds 2"):
                load(root, document)

    def test_slot_key_in_entry_rejected(self) -> None:
        with temp_root() as root:
            bad = entry("a", "A", 1937)
            bad["slot"] = 0
            with self.assertRaisesRegex(ToolError, "must not have 'slot'"):
                load(root, combo_document(combo=[bad]))

    def test_duplicate_skill_is_warned_not_rejected(self) -> None:
        with temp_root() as root:
            document = combo_document(
                combo=[entry("a", "A", 1926, "A + DER")],
                priority=[entry("a again", "A", 1926, "")],
            )
            with quiet_logs() as buffer:
                combo = load(root, document)
            self.assertEqual(2, len(combo.entries))
            warnings = [m for m in log_messages(buffer) if m["level"] == "warn"]
            self.assertTrue(any("same skillNo" in m["msg"] for m in warnings))

    def test_conflicting_memos_keep_first_and_warn(self) -> None:
        with temp_root() as root:
            document = combo_document(
                combo=[entry("a", "A", 1926, "A + DER")],
                priority=[entry("a again", "A", 1926, "B")],
            )
            combo = load(root, document)
            with quiet_logs() as buffer:
                plan = apply.build_plan(combo, {}, skip_optional=False)
            self.assertEqual({1926: "A + DER"}, plan.memos)
            self.assertTrue(any("conflicting memos" in m["msg"] for m in log_messages(buffer)))

    def test_optional_skip_shifts_later_entries_up(self) -> None:
        with temp_root() as root:
            document = combo_document(
                combo=[
                    entry("a", "A", 1937, "R 1"),
                    entry("b", "B", 1921, "SH + DER", optional=True),
                    entry("c", "C", 1912, "F"),
                ],
                layout={"combo": [0, 4], "priorityBuffs": [12, 14], "otherBuffs": [15, 19]},
            )
            combo = load(root, document)
            with quiet_logs():
                kept = apply.build_plan(combo, {}, skip_optional=False)
                skipped = apply.build_plan(combo, {}, skip_optional=True)
            self.assertEqual([1937, 1921, 1912, 0, 0], list(kept.skills[:5]))
            self.assertEqual([1937, 1912, 0, 0, 0], list(skipped.skills[:5]))
            self.assertNotIn(1921, skipped.memos)

    def test_override_replaces_skill_and_memo_follows_new_skill(self) -> None:
        with temp_root() as root:
            combo = load(root, tamer_combo_document())
            with quiet_logs():
                plan = apply.build_plan(combo, apply.parse_overrides(["0=1939"]), skip_optional=False)
            self.assertEqual(1939, plan.skills[0])
            self.assertEqual("R 1", plan.memos[1939])
            self.assertNotIn(1937, plan.skills)
            self.assertNotIn(1937, plan.memos)

    def test_override_on_empty_slot_rejected(self) -> None:
        with temp_root() as root:
            combo = load(root, tamer_combo_document())
            with self.assertRaisesRegex(ToolError, "does not use"):
                apply.build_plan(combo, {14: 1921}, skip_optional=False)

    def test_override_argument_parsing(self) -> None:
        self.assertEqual({0: 1921, 12: 5}, apply.parse_overrides(["0=1921", "12=5"]))
        for bad in ["0", "x=1", "20=1921", "0=0"]:
            with self.assertRaises(ToolError, msg=bad):
                apply.parse_overrides([bad])
        with self.assertRaisesRegex(ToolError, "twice"):
            apply.parse_overrides(["0=1", "0=2"])


class ValidatorTest(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = temp_root()
        self.root = self._tmp.__enter__()
        self.index = load_skill_index(write_skill_table(self.root / "skills.json"))

    def tearDown(self) -> None:
        self._tmp.__exit__(None, None, None)

    def test_correct_refs_pass(self) -> None:
        refs = [SkillRef(0, "Flow: Intimidation", "Flujo: Intimidación", 1937)]
        self.assertTrue(all(f.ok for f in check_skill_refs(refs, 16, self.index)))

    def test_wrong_skill_no_is_caught(self) -> None:
        refs = [SkillRef(0, "Flow: Intimidation", "Flujo: Intimidación", 99999)]
        findings = check_skill_refs(refs, 16, self.index)
        self.assertFalse(findings[0].ok)
        self.assertIn("not found", findings[0].problems[0])

    def test_skill_of_other_class_is_caught(self) -> None:
        refs = [SkillRef(0, "Guardian Only", "Guardian Only", 5011)]
        findings = check_skill_refs(refs, 16, self.index)
        self.assertFalse(findings[0].ok)
        self.assertIn("classId 16", findings[0].problems[0])

    def test_name_mismatch_is_caught(self) -> None:
        refs = [SkillRef(0, "Flow: Intimidation", "Garuda", 1937)]
        findings = check_skill_refs(refs, 16, self.index)
        self.assertFalse(findings[0].ok)
        self.assertIn("does not match", findings[0].problems[0])

    def test_rank_suffix_and_markup_handling(self) -> None:
        self.assertEqual("Garuda", strip_rank_suffix("Garuda I"))
        self.assertTrue(names_match("Garuda", "Garuda I"))
        self.assertTrue(names_match("aguja giratoria completa", "Aguja giratoria completa I"))
        info = self.index.get(9001)
        self.assertEqual("Markup Skill", strip_rank_suffix(info.group_name))
        self.assertNotIn("<", info.group_name)

    def test_rank_without_name_falls_back_to_group_name(self) -> None:
        info = self.index.get(2555)
        self.assertEqual("Baile de la Bestia Legendaria: Resonancia", info.display_name)

    def test_wrong_skill_in_combo_file_is_caught_by_validator(self) -> None:
        good_path = write_combo(self.root / "good.json", tamer_combo_document())
        bad_document = tamer_combo_document()
        bad_document["slots"][0]["skillNo"] = 99999
        bad_path = write_combo(self.root / "bad.json", bad_document)
        with quiet_logs():
            good = apply.load_combo(good_path)
            bad = apply.load_combo(bad_path)
        good_findings = check_skill_refs(apply.combo_refs(good), good.class_id, self.index)
        bad_findings = check_skill_refs(apply.combo_refs(bad), bad.class_id, self.index)
        self.assertTrue(all(f.ok for f in good_findings))
        self.assertFalse(bad_findings[0].ok)
        self.assertTrue(all(f.ok for f in bad_findings[1:]))


if __name__ == "__main__":
    unittest.main()
