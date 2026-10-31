"""The skill class table: groups with more than nine classes are left out; synthetic input only."""

from __future__ import annotations

import json
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from bdo_combo.guides.skill_classes import SkillClassesError, build_skill_class_table, load_skill_table

SYNTHETIC = {
    "groups": [
        {"key": 1, "name": "Synthetic A", "classes": [0, 1], "ranks": [{"rank": 1, "skillNo": 900}]},
        {"key": 2, "name": "Synthetic B", "classes": [1, 2, 3, 4, 5, 6, 7, 8, 9], "ranks": [{"rank": 1, "skillNo": 901}]},
        {"key": 3, "name": "Synthetic C", "classes": list(range(10)), "ranks": [{"rank": 1, "skillNo": 902}]},
        {"key": 4, "name": "Synthetic D", "classes": [2], "ranks": [{"rank": 1, "skillNo": 900}, {"rank": 2, "skillNo": 903}]},
        {"key": 5, "name": "Synthetic E", "classes": [7], "ranks": [{"rank": 1}, {"rank": 2, "skillNo": "x"}]},
    ]
}


class SkillClassesTests(unittest.TestCase):
    def test_groups_with_more_than_nine_classes_are_left_out(self) -> None:
        table = build_skill_class_table(SYNTHETIC)
        self.assertNotIn("902", table["skills"])
        self.assertEqual(table["maxClassesPerGroup"], 9)

    def test_nine_classes_is_kept(self) -> None:
        table = build_skill_class_table(SYNTHETIC)
        self.assertEqual(table["skills"]["901"], [1, 2, 3, 4, 5, 6, 7, 8, 9])

    def test_a_skill_in_several_groups_gets_the_union_of_classes(self) -> None:
        table = build_skill_class_table(SYNTHETIC)
        self.assertEqual(table["skills"]["900"], [0, 1, 2])
        self.assertEqual(table["skills"]["903"], [2])

    def test_ranks_without_an_integer_skill_number_are_ignored(self) -> None:
        table = build_skill_class_table(SYNTHETIC)
        self.assertEqual(sorted(table["skills"]), ["900", "901", "903"])

    def test_output_is_sorted_and_stable(self) -> None:
        first = json.dumps(build_skill_class_table(SYNTHETIC), sort_keys=False)
        second = json.dumps(build_skill_class_table(SYNTHETIC), sort_keys=False)
        self.assertEqual(first, second)
        self.assertEqual(list(build_skill_class_table(SYNTHETIC)["skills"]), ["900", "901", "903"])

    def test_bad_shape_is_refused(self) -> None:
        with self.assertRaises(SkillClassesError):
            build_skill_class_table({"groups": "nope"})
        with self.assertRaises(SkillClassesError):
            build_skill_class_table({"groups": [{"key": 9, "classes": ["0"], "ranks": []}]})

    def test_missing_source_file_is_refused(self) -> None:
        with TemporaryDirectory() as folder:
            with self.assertRaises(SkillClassesError):
                load_skill_table(Path(folder) / "missing.json")

    def test_source_file_is_read(self) -> None:
        with TemporaryDirectory() as folder:
            path = Path(folder) / "table.json"
            path.write_text(json.dumps(SYNTHETIC), encoding="utf-8")
            self.assertEqual(load_skill_table(path), SYNTHETIC)


if __name__ == "__main__":
    unittest.main()
