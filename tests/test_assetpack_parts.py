"""Asset pack parts: layout, stability, assembly into one pack, and the refusals (missing, wrong, tampered parts)."""

import tempfile
import unittest
from pathlib import Path

from bdo_combo.assetpack import AssetPackError, AssetPackReader, PackInput, assemble_parts, build_pack, verify_parts, write_parts
from bdo_combo.assetpack.format import PartInfo
from bdo_combo.assetpack.parts import (
    CORE_PART_NUMBER,
    DEFAULT_SET_ID,
    FIRST_SKILL_PART_NUMBER,
    build_parts,
    part_count_for,
    part_file_name,
    part_number_for_key,
    read_part_files,
)
from assetpack_support import OTHER_SET_ID, synthetic_inputs
from support import quiet_logs

BUCKET = 8


def sample_inputs() -> list[PackInput]:
    inputs = list(synthetic_inputs())
    inputs.append(PackInput("skill-9.png", b"\x89PNG\r\n\x1a\n" + b"9" * 300))
    return inputs


class LayoutTests(unittest.TestCase):
    def test_core_keys_go_to_part_one(self):
        for key in ("class-16.png", "slot-cooldown-bg.png", "catalog.json", "skills.json", "guide-tamer.awakening.json"):
            self.assertEqual(CORE_PART_NUMBER, part_number_for_key(key, BUCKET), key)

    def test_skill_numbers_go_to_fixed_buckets(self):
        self.assertEqual(FIRST_SKILL_PART_NUMBER, part_number_for_key("skill-0.png", BUCKET))
        self.assertEqual(FIRST_SKILL_PART_NUMBER, part_number_for_key("skill-7.png", BUCKET))
        self.assertEqual(FIRST_SKILL_PART_NUMBER + 1, part_number_for_key("skill-8.png", BUCKET))

    def test_count_is_the_highest_skill_bucket_plus_the_core_part(self):
        keys = ["catalog.json", "skill-1.png", "skill-17.png"]
        self.assertEqual(4, part_count_for(keys, BUCKET))
        self.assertEqual(1, part_count_for(["catalog.json"], BUCKET))

    def test_every_part_from_one_to_count_is_written_even_when_empty(self):
        parts = build_parts([PackInput("skill-1.png", b"a"), PackInput("skill-17.png", b"b")], bucket_size=BUCKET)

        self.assertEqual([1, 2, 3, 4], sorted(parts))
        self.assertEqual([], AssetPackReader(parts[3]).keys())

    def test_each_part_is_a_valid_part_with_its_number_and_count(self):
        parts = build_parts(sample_inputs(), bucket_size=BUCKET)
        for number, data in parts.items():
            reader = AssetPackReader(data)
            self.assertEqual(PartInfo(number, len(parts), DEFAULT_SET_ID), reader.part)
            reader.verify()

    def test_parts_hold_each_entry_exactly_once(self):
        inputs = sample_inputs()
        parts = build_parts(inputs, bucket_size=BUCKET)

        keys = [key for data in parts.values() for key in AssetPackReader(data).keys()]

        self.assertEqual(sorted(item.key for item in inputs), sorted(keys))


class AssemblyTests(unittest.TestCase):
    def test_assembled_parts_equal_the_complete_pack_byte_for_byte(self):
        inputs = sample_inputs()
        parts = build_parts(inputs, bucket_size=BUCKET)

        assembled = assemble_parts(list(parts.values()))

        self.assertEqual(build_pack(inputs).data, assembled.data)

    def test_assembly_does_not_depend_on_part_order(self):
        parts = list(build_parts(sample_inputs(), bucket_size=BUCKET).values())

        self.assertEqual(assemble_parts(parts).data, assemble_parts(list(reversed(parts))).data)

    def test_a_missing_part_is_refused(self):
        parts = list(build_parts(sample_inputs(), bucket_size=BUCKET).values())
        without_second = [parts[0]] + parts[2:]

        with self.assertRaisesRegex(AssetPackError, "missing part"):
            assemble_parts(without_second)

    def test_a_part_from_another_set_is_refused(self):
        ours = build_parts(sample_inputs(), set_id=DEFAULT_SET_ID, bucket_size=BUCKET)
        theirs = build_parts(sample_inputs(), set_id=OTHER_SET_ID, bucket_size=BUCKET)
        mixed = [theirs[1]] + [ours[number] for number in sorted(ours) if number != 1]

        with self.assertRaisesRegex(AssetPackError, "another set"):
            assemble_parts(mixed)

    def test_a_complete_pack_is_not_a_part(self):
        complete = build_pack(sample_inputs()).data

        with self.assertRaisesRegex(AssetPackError, "not a part"):
            assemble_parts([complete])

    def test_the_same_part_twice_is_refused(self):
        parts = list(build_parts(sample_inputs(), bucket_size=BUCKET).values())

        with self.assertRaisesRegex(AssetPackError, "given twice"):
            assemble_parts(parts + [parts[0]])

    def test_a_tampered_entry_in_a_part_is_refused(self):
        parts = build_parts(sample_inputs(), bucket_size=BUCKET)
        damaged = bytearray(parts[2])
        stored = AssetPackReader(parts[2]).entry(AssetPackReader(parts[2]).keys()[0]).stored_offset
        damaged[stored + 20] ^= 0x01

        with self.assertRaisesRegex(AssetPackError, "failed authentication"):
            assemble_parts([parts[1], bytes(damaged), parts[3], parts[4]])


class PartStabilityTests(unittest.TestCase):
    def test_changing_one_icon_rewrites_only_its_part(self):
        inputs = sample_inputs()
        before = build_parts(inputs, bucket_size=BUCKET)
        changed = [PackInput("skill-9.png", b"\x89PNG\r\n\x1a\n" + b"X" * 300) if item.key == "skill-9.png" else item for item in inputs]

        after = build_parts(changed, bucket_size=BUCKET)

        self.assertEqual(sorted(before), sorted(after))
        touched = [number for number in before if before[number] != after[number]]
        self.assertEqual([part_number_for_key("skill-9.png", BUCKET)], touched)

    def test_same_inputs_give_the_same_parts(self):
        self.assertEqual(build_parts(sample_inputs(), bucket_size=BUCKET), build_parts(sample_inputs(), bucket_size=BUCKET))


class PartFolderTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.folder = Path(self._tmp.name) / "parts"
        self.parts = build_parts(sample_inputs(), bucket_size=BUCKET)
        write_parts(self.parts, self.folder)

    def tearDown(self):
        self._tmp.cleanup()

    def test_the_written_folder_verifies(self):
        summary = verify_parts(self.folder)

        self.assertEqual({"parts": len(self.parts), "entries": len(sample_inputs())}, summary)

    def test_a_part_under_the_wrong_file_name_is_refused(self):
        (self.folder / part_file_name(2)).replace(self.folder / part_file_name(9))

        with quiet_logs(), self.assertRaisesRegex(AssetPackError, "wrong part"):
            read_part_files(self.folder)

    def test_a_missing_part_file_is_refused(self):
        (self.folder / part_file_name(3)).unlink()

        with quiet_logs(), self.assertRaisesRegex(AssetPackError, "missing part"):
            verify_parts(self.folder)

    def test_a_damaged_part_file_is_refused(self):
        path = self.folder / part_file_name(1)
        data = bytearray(path.read_bytes())
        data[-5] ^= 0x80
        path.write_bytes(bytes(data))

        with quiet_logs(), self.assertRaisesRegex(AssetPackError, "index authentication"):
            verify_parts(self.folder)

    def test_writing_a_smaller_set_removes_stale_part_files(self):
        smaller = build_parts([PackInput("catalog.json", b"{}")], bucket_size=BUCKET)

        write_parts(smaller, self.folder)

        self.assertEqual([part_file_name(1)], [path.name for path in self.folder.iterdir()])

    def test_part_info_validates_its_numbers(self):
        with self.assertRaises(AssetPackError):
            PartInfo(3, 2, DEFAULT_SET_ID)
        with self.assertRaises(AssetPackError):
            PartInfo(0, 2, DEFAULT_SET_ID)


if __name__ == "__main__":
    unittest.main()
