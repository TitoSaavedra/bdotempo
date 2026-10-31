"""Asset pack format: round trip, determinism, tamper detection, wrong key, and the entry rules. Synthetic data only."""

import random
import tempfile
import unittest
from pathlib import Path

from bdo_combo.assetpack import (
    AssetPackError,
    AssetPackReader,
    PackInput,
    build_pack,
    default_key_material,
    derive_key_material,
    write_pack,
)
from bdo_combo.assetpack.format import HEADER_STRUCT, MAGIC, validate_key
from assetpack_support import synthetic_inputs


def sample_inputs() -> list[PackInput]:
    return [
        PackInput("skill-1.png", b"\x89PNG\r\n\x1a\n" + b"A" * 200, compression="none"),
        PackInput("skill-2.png", b"B" * 500, compression="deflate"),
        PackInput("skill-3.png", bytes(range(256)) * 4, compression="brotli"),
        PackInput("catalog.json", b'{"schema": 1, "classes": []}' * 40),
        PackInput("empty.bin", b""),
    ]


class RoundTripTests(unittest.TestCase):
    def test_every_entry_reads_back_identical(self):
        inputs = sample_inputs()
        reader = AssetPackReader(build_pack(inputs).data)

        self.assertEqual(sorted(item.key for item in inputs), reader.keys())
        for item in inputs:
            self.assertEqual(item.data, reader.get(item.key), item.key)
        self.assertEqual(len(inputs), reader.verify())

    def test_auto_compression_keeps_the_smallest_candidate(self):
        data = b"Z" * 10_000
        result = build_pack([PackInput("skill-9.png", data)])

        entry = result.entries[0]
        self.assertIn(entry.compression, ("none", "deflate", "brotli"))
        self.assertLess(entry.stored_length, len(data))
        self.assertEqual(data, AssetPackReader(result.data).get("skill-9.png"))

    def test_write_pack_creates_the_file_atomically(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / "nested" / "assets.bdoa"
            result = write_pack(sample_inputs(), target)

            self.assertEqual(result.data, target.read_bytes())
            self.assertFalse((target.with_name("assets.bdoa.tmp")).exists())
            self.assertEqual(5, AssetPackReader.open(target).verify())


class DeterminismTests(unittest.TestCase):
    def test_same_inputs_give_the_same_bytes(self):
        self.assertEqual(build_pack(sample_inputs()).data, build_pack(sample_inputs()).data)

    def test_input_order_does_not_change_the_output(self):
        inputs = sample_inputs()
        shuffled = list(inputs)
        random.Random(7).shuffle(shuffled)

        self.assertEqual(build_pack(inputs).data, build_pack(shuffled).data)

    def test_different_content_changes_the_bytes(self):
        changed = sample_inputs()
        changed[0] = PackInput("skill-1.png", changed[0].data + b"!", compression="none")

        self.assertNotEqual(build_pack(sample_inputs()).data, build_pack(changed).data)


class TamperTests(unittest.TestCase):
    def setUp(self):
        self.result = build_pack(sample_inputs())

    def test_flipped_byte_in_an_entry_blob_is_refused(self):
        data = bytearray(self.result.data)
        stored_offset = AssetPackReader(self.result.data).entry("skill-1.png").stored_offset
        data[stored_offset + 20] ^= 0x01

        reader = AssetPackReader(bytes(data))
        with self.assertRaisesRegex(AssetPackError, "failed authentication"):
            reader.get("skill-1.png")
        # Entries that were not touched still read correctly.
        self.assertEqual(sample_inputs()[1].data, reader.get("skill-2.png"))

    def test_flipped_byte_in_the_index_is_refused_on_open(self):
        data = bytearray(self.result.data)
        data[-5] ^= 0x80  # inside the index, which is at the end of the file

        with self.assertRaisesRegex(AssetPackError, "index authentication failed"):
            AssetPackReader(bytes(data))

    def test_truncated_pack_is_refused(self):
        with self.assertRaisesRegex(AssetPackError, "file length"):
            AssetPackReader(self.result.data[:-1])

    def test_appended_bytes_are_refused(self):
        with self.assertRaisesRegex(AssetPackError, "file length"):
            AssetPackReader(self.result.data + b"\x00")

    def test_bad_magic_is_refused(self):
        data = b"NOPE" + self.result.data[4:]
        with self.assertRaisesRegex(AssetPackError, "bad magic"):
            AssetPackReader(data)

    def test_unknown_version_is_refused(self):
        fields = list(HEADER_STRUCT.unpack_from(self.result.data, 0))
        fields[1] = 2
        data = HEADER_STRUCT.pack(*fields) + self.result.data[HEADER_STRUCT.size :]
        with self.assertRaisesRegex(AssetPackError, "unsupported pack version"):
            AssetPackReader(data)

    def test_too_short_file_is_refused(self):
        with self.assertRaisesRegex(AssetPackError, "shorter than the pack header"):
            AssetPackReader(MAGIC)

    def test_unknown_key_is_refused(self):
        with self.assertRaisesRegex(AssetPackError, "no entry named"):
            AssetPackReader(self.result.data).get("skill-404.png")


class WrongKeyTests(unittest.TestCase):
    def test_wrong_secret_fails_on_open(self):
        data = build_pack(sample_inputs()).data
        other = derive_key_material(bytes(32))

        with self.assertRaisesRegex(AssetPackError, "index authentication failed"):
            AssetPackReader(data, other)

    def test_default_key_opens_the_pack(self):
        data = build_pack(sample_inputs(), default_key_material()).data
        self.assertEqual(5, AssetPackReader(data, default_key_material()).verify())

    def test_key_material_needs_a_32_byte_secret(self):
        with self.assertRaises(AssetPackError):
            derive_key_material(b"short")


class EntryRuleTests(unittest.TestCase):
    def test_duplicate_keys_are_refused(self):
        with self.assertRaisesRegex(AssetPackError, "duplicate"):
            build_pack([PackInput("skill-1.png", b"a"), PackInput("skill-1.png", b"b")])

    def test_invalid_keys_are_refused(self):
        for key in ("", "Skill-1.png", "../x.png", "a/b.png", "x" * 129, "ñ.png"):
            with self.subTest(key=key), self.assertRaises(AssetPackError):
                validate_key(key)

    def test_unknown_compression_is_refused(self):
        with self.assertRaisesRegex(AssetPackError, "unknown compression"):
            build_pack([PackInput("a.bin", b"x", compression="lzma")])

    def test_the_synthetic_inputs_round_trip(self):
        inputs = synthetic_inputs()
        reader = AssetPackReader(build_pack(inputs).data)
        for item in inputs:
            self.assertEqual(item.data, reader.get(item.key))


if __name__ == "__main__":
    unittest.main()
