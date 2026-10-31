"""The real pack inputs (manifest, folders, CLI) on a synthetic client export in a temporary folder."""

import contextlib
import io
import tempfile
import unittest
from pathlib import Path

from bdo_combo.assetpack import AssetPackError, AssetPackReader, build_pack
from bdo_combo.assetpack.__main__ import main as assetpack_main
from bdo_combo.assetpack.manifest import SLOT_ART, collect_inputs
from bdo_combo.assetpack.parts import part_file_name
from assetpack_support import synthetic_png
from support import quiet_logs


def write_client(root: Path, skill_numbers=(1, 2, 10), class_types=(16,)) -> None:
    for number in skill_numbers:
        _write(root / "icons" / "skills" / f"{number}.png", synthetic_png(4, 4, seed=number))
    _write(root / "icons" / "skills" / "readme.txt", b"not an icon")
    for class_type in class_types:
        _write(root / "icons" / "classes" / f"class_{class_type}.png", synthetic_png(8, 8, seed=class_type))
    for art in SLOT_ART:
        _write(root / art.source, synthetic_png(12, 12, seed=len(art.key)))


def write_staging(root: Path, names=("catalog.json", "skills.json", "skill_classes.json", "guide-tamer.awakening.json")) -> None:
    for name in names:
        _write(root / name, b'{"schema": 1}\n')


def _write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


class CollectInputsTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.client = self.root / "client"
        self.staging = self.root / "staging"
        write_client(self.client)
        write_staging(self.staging)

    def tearDown(self):
        self._tmp.cleanup()

    def test_every_icon_slot_art_and_json_becomes_one_entry(self):
        keys = sorted(item.key for item in collect_inputs(self.client, self.staging))

        expected = sorted(
            ["skill-1.png", "skill-2.png", "skill-10.png", "class-16.png", "catalog.json", "skills.json", "skill_classes.json", "guide-tamer.awakening.json"]
            + [art.key for art in SLOT_ART]
        )
        self.assertEqual(expected, keys)

    def test_the_pack_built_from_the_inputs_reads_back(self):
        inputs = collect_inputs(self.client, self.staging)

        reader = AssetPackReader(build_pack(inputs).data)

        self.assertEqual(len(inputs), reader.verify())
        self.assertEqual((self.client / "icons" / "skills" / "10.png").read_bytes(), reader.get("skill-10.png"))

    def test_a_missing_slot_art_file_is_refused(self):
        (self.client / SLOT_ART[0].source).unlink()
        with quiet_logs(), self.assertRaisesRegex(AssetPackError, "slot art not found"):
            collect_inputs(self.client, self.staging)

    def test_a_skill_icon_that_is_not_a_png_is_refused(self):
        (self.client / "icons" / "skills" / "3.png").write_bytes(b"GIF89a...")
        with quiet_logs(), self.assertRaisesRegex(AssetPackError, "is not a PNG"):
            collect_inputs(self.client, self.staging)

    def test_missing_staging_json_is_refused(self):
        (self.staging / "skills.json").unlink()
        with quiet_logs(), self.assertRaisesRegex(AssetPackError, "skills.json not found"):
            collect_inputs(self.client, self.staging)

    def test_missing_skill_classes_json_is_refused(self):
        (self.staging / "skill_classes.json").unlink()
        with quiet_logs(), self.assertRaisesRegex(AssetPackError, "skill_classes.json not found"):
            collect_inputs(self.client, self.staging)

    def test_missing_client_folder_is_refused(self):
        with quiet_logs(), self.assertRaisesRegex(AssetPackError, "client folder not found"):
            collect_inputs(self.root / "nope", self.staging)


class CommandLineTests(unittest.TestCase):
    def _run(self, *args):
        output = io.StringIO()
        errors = io.StringIO()
        with quiet_logs(), contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
            code = assetpack_main(list(args))
        return code, output.getvalue(), errors.getvalue()

    def test_pack_then_verify_parts_then_assemble_round_trip(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_client(root / "client")
            write_staging(root / "staging")
            parts = root / "parts"
            assembled = root / "out" / "assets.bdoa"

            packed = self._run("pack", "--client-dir", str(root / "client"), "--staging-dir", str(root / "staging"), "--out-dir", str(parts))
            verified = self._run("verify-parts", str(parts))
            joined = self._run("assemble", str(parts), str(assembled))
            single = self._run("verify", str(assembled))

            self.assertEqual(0, packed[0])
            self.assertIn(part_file_name(1), packed[1])
            self.assertEqual(0, verified[0])
            self.assertEqual(0, joined[0])
            self.assertEqual(0, single[0])
            self.assertTrue(assembled.is_file())

    def test_pack_reports_a_missing_input_with_exit_code_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            code, _, errors = self._run("pack", "--client-dir", str(Path(tmp) / "none"), "--staging-dir", tmp, "--out-dir", tmp)

            self.assertEqual(1, code)
            self.assertIn("client folder not found", errors)

    def test_verify_parts_reports_a_missing_part(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_client(root / "client")
            write_staging(root / "staging")
            parts = root / "parts"
            self._run("pack", "--client-dir", str(root / "client"), "--staging-dir", str(root / "staging"), "--out-dir", str(parts))
            (parts / part_file_name(1)).unlink()

            code, _, errors = self._run("verify-parts", str(parts))

            self.assertEqual(1, code)
            self.assertIn("missing part", errors)


if __name__ == "__main__":
    unittest.main()
