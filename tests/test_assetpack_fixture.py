"""The committed C# parity fixtures must be exactly what this code produces, and must read back correctly."""

import json
import unittest

from bdo_combo.assetpack import AssetPackReader, assemble_parts
from bdo_combo.assetpack.parts import part_file_name
from assetpack_support import (
    FIXTURE_EXPECTED,
    FIXTURE_OTHER_SET,
    FIXTURE_PACK,
    FIXTURE_PARTS,
    build_fixture,
    build_fixture_parts,
    build_other_set_part,
)


class SyntheticFixtureTests(unittest.TestCase):
    def test_committed_pack_is_the_current_output(self):
        data, _ = build_fixture()
        self.assertEqual(
            data,
            FIXTURE_PACK.read_bytes(),
            "synthetic.bdoa is stale: run `python tests/assetpack_support.py` and commit the result",
        )

    def test_committed_expected_values_are_the_current_output(self):
        _, expected = build_fixture()
        self.assertEqual(json.loads(expected), json.loads(FIXTURE_EXPECTED.read_text(encoding="utf-8")))

    def test_committed_pack_reads_back_every_expected_entry(self):
        expected = json.loads(FIXTURE_EXPECTED.read_text(encoding="utf-8"))
        reader = AssetPackReader.open(FIXTURE_PACK)

        self.assertIsNone(reader.part)
        self.assertEqual([entry["key"] for entry in expected["entries"]], reader.keys())
        self.assertEqual(len(expected["entries"]), reader.verify())
        self.assertEqual({"none", "deflate", "brotli"}, {entry["compression"] for entry in expected["entries"]})

    def test_committed_parts_are_the_current_output(self):
        parts = build_fixture_parts()
        self.assertEqual(sorted(part_file_name(number) for number in parts), sorted(p.name for p in FIXTURE_PARTS.iterdir()))
        for number, data in parts.items():
            self.assertEqual(data, (FIXTURE_PARTS / part_file_name(number)).read_bytes(), f"part {number} is stale")

    def test_committed_other_set_part_is_the_current_output(self):
        self.assertEqual(build_other_set_part(), (FIXTURE_OTHER_SET / part_file_name(1)).read_bytes())

    def test_committed_parts_assemble_to_the_committed_pack(self):
        """The parity contract: parts joined by the Python assembler give the complete pack byte for byte."""
        datas = [(FIXTURE_PARTS / part_file_name(number)).read_bytes() for number in sorted(build_fixture_parts())]

        self.assertEqual(FIXTURE_PACK.read_bytes(), assemble_parts(datas).data)


if __name__ == "__main__":
    unittest.main()
