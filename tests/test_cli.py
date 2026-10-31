import contextlib
import io
import json
import os
import unittest
from unittest import mock

from bdo_combo import backup, cli, gamevars
from bdo_combo.config import REPO_ROOT, ToolError, resolve_account
from support import (
    ACCOUNT,
    PCID_A,
    PCID_B,
    CharacterFixture,
    combo_document,
    entry,
    game_file_text,
    game_not_running,
    quiet_logs,
    tamer_combo_document,
    temp_root,
    write_combo,
)


def run_cli(argv: list[str]) -> tuple[int, str, str]:
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = cli.main(argv)
    return code, out.getvalue(), err.getvalue()


def slots_of(path) -> tuple[int, ...]:
    return gamevars.parse(path.read_bytes().decode("utf-8")).slots


class ApplyCliTest(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = temp_root()
        self.root = self._tmp.__enter__()
        self.fx = CharacterFixture(self.root)
        self.path = self.fx.add_character(PCID_A)
        self.original = self.path.read_bytes()
        self.combo = write_combo(self.root / "tamer.json", tamer_combo_document())
        self.quiet = quiet_logs()
        self.quiet.__enter__()

    def tearDown(self) -> None:
        self.quiet.__exit__(None, None, None)
        self._tmp.__exit__(None, None, None)

    def apply_args(self, *extra: str) -> list[str]:
        return ["apply", "--combo", str(self.combo), "--pcid", PCID_A, *self.fx.cli_args(), *extra]

    def test_dry_run_writes_nothing_and_creates_no_backups(self) -> None:
        code, out, _ = run_cli(self.apply_args())
        self.assertEqual(0, code)
        self.assertIn("DRY RUN", out)
        self.assertIn("~ slot", out)
        self.assertEqual(self.original, self.path.read_bytes())
        self.assertFalse(self.fx.backups_dir.exists())

    def test_explicit_dry_run_flag_is_accepted(self) -> None:
        code, _, _ = run_cli(self.apply_args("--dry-run"))
        self.assertEqual(0, code)
        self.assertEqual(self.original, self.path.read_bytes())

    def test_write_and_dry_run_together_are_refused(self) -> None:
        with self.assertRaises(SystemExit):
            run_cli(self.apply_args("--write", "--dry-run"))

    def test_refuses_without_pcid(self) -> None:
        code, _, err = run_cli(["apply", "--combo", str(self.combo), *self.fx.cli_args()])
        self.assertEqual(1, code)
        self.assertIn("at least one --pcid", err)
        self.assertEqual(self.original, self.path.read_bytes())

    def test_write_takes_backup_then_is_idempotent(self) -> None:
        with game_not_running():
            code, out, _ = run_cli(self.apply_args("--write"))
            self.assertEqual(0, code)
            self.assertIn("written and validated", out)
            written = self.path.read_bytes()
            self.assertNotEqual(self.original, written)
            self.assertEqual(1937, slots_of(self.path)[0])

            backups = list(self.fx.backups_dir.iterdir())
            self.assertEqual(1, len(backups))
            manifest = json.loads((backups[0] / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual("pre-apply", manifest["reason"])
            self.assertEqual(self.original, (backups[0] / ACCOUNT / PCID_A / "gameVariable.xml").read_bytes())

            code, out, _ = run_cli(self.apply_args("--write"))
            self.assertEqual(0, code)
            self.assertIn("already identical", out)
            self.assertEqual(written, self.path.read_bytes())

    def test_refused_while_game_runs_and_file_is_fresh(self) -> None:
        with mock.patch.object(backup, "is_game_running", return_value=True):
            code, _, err = run_cli(self.apply_args("--write"))
        self.assertEqual(1, code)
        self.assertIn("close the game", err)
        self.assertEqual(self.original, self.path.read_bytes())
        self.assertFalse(self.fx.backups_dir.exists())

    def test_force_allows_write_while_game_runs(self) -> None:
        with mock.patch.object(backup, "is_game_running", return_value=True):
            code, _, _ = run_cli(self.apply_args("--write", "--force"))
        self.assertEqual(0, code)
        self.assertNotEqual(self.original, self.path.read_bytes())

    def test_skill_override_swaps_to_lower_rank_of_same_skill(self) -> None:
        with game_not_running():
            code, out, _ = run_cli(self.apply_args("--write", "--skill-override", "1=1918"))
        self.assertEqual(0, code)
        self.assertEqual(1918, slots_of(self.path)[1])
        self.assertIn("1918", out)

    def test_skill_override_to_other_skill_is_refused_by_name_check(self) -> None:
        code, _, err = run_cli(self.apply_args("--skill-override", "0=1939"))
        self.assertEqual(1, code)
        self.assertIn("does not match the skill table", err)
        self.assertEqual(self.original, self.path.read_bytes())

    def test_skip_optional_drops_dark_gaoler_and_shifts(self) -> None:
        with game_not_running():
            code, _, _ = run_cli(self.apply_args("--write", "--skip-optional"))
        self.assertEqual(0, code)
        slots = slots_of(self.path)
        self.assertNotIn(1306, slots)
        self.assertEqual([1937, 1921, 1912, 1926, 17, 0], list(slots[:6]))
        self.assertEqual([1347, 231, 0, 0, 0], list(slots[12:17]))

    def test_post_write_failure_restores_original(self) -> None:
        real_write = backup.atomic_write
        calls: list[int] = []

        def corrupt_first_write(path, data):
            if not calls:
                calls.append(1)
                real_write(path, data + b"X")  # simulate a bad write
                return
            real_write(path, data)

        with game_not_running(), mock.patch.object(backup, "atomic_write", side_effect=corrupt_first_write):
            code, _, err = run_cli(self.apply_args("--write"))
        self.assertEqual(3, code)
        self.assertIn("restored from the backup", err)
        self.assertEqual(self.original, self.path.read_bytes())

    def test_full_cycle_apply_then_restore(self) -> None:
        with game_not_running():
            run_cli(self.apply_args("--write"))
            backups = sorted(p.name for p in self.fx.backups_dir.iterdir())
            self.assertEqual(1, len(backups))
            code, out, _ = run_cli(["restore", "--backup", backups[0], *self.fx.cli_args(), "--write"])
        self.assertEqual(0, code)
        self.assertEqual(self.original, self.path.read_bytes())
        self.assertIn("Pre-restore backup", out)

    def test_unknown_pcid_is_refused(self) -> None:
        code, _, err = run_cli(["apply", "--combo", str(self.combo), "--pcid", PCID_B, *self.fx.cli_args()])
        self.assertEqual(1, code)
        self.assertIn("not found", err)


class ValidateCliTest(unittest.TestCase):
    def test_repo_tamer_combo_validates_with_zero_mismatches_against_fixture_table(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            code, out, _ = run_cli(["validate", "--combo", str(REPO_ROOT / "combos" / "tamer.awakening.json"),
                                    "--skills-json", str(fx.skills_json)])
        self.assertIn("Mismatches: 0", out)
        self.assertEqual(0, code)
        self.assertIn("Note: skillNo 1926", out)

    def test_wrong_skill_number_exits_non_zero(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            document = combo_document(combo=[entry("Flow: Intimidation", "Flujo: Intimidación", 99999)])
            path = write_combo(root / "bad.json", document)
            code, out, _ = run_cli(["validate", "--combo", str(path), "--skills-json", str(fx.skills_json)])
        self.assertEqual(1, code)
        self.assertIn("Mismatches: 1", out)
        self.assertIn("not found", out)


class ListCharactersTest(unittest.TestCase):
    def test_lists_pcids_with_first_five_skills(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            fx.add_character(PCID_A)
            fx.add_character(PCID_B, game_file_text("missing"))
            code, out, _ = run_cli(["list-characters", *fx.cli_args()])
        self.assertEqual(0, code)
        self.assertIn(f"Account {ACCOUNT}: 2 character(s)", out)
        self.assertIn(PCID_A, out)
        self.assertIn("6457 5157 5167 5179 5161", out)


class AccountDiscoveryTest(unittest.TestCase):
    def test_single_account_is_auto_detected(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            fx.add_character(PCID_A)
            self.assertEqual(ACCOUNT, resolve_account(fx.settings()))

    def test_several_accounts_pick_most_recent_unless_explicit(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            old_file = fx.add_character(PCID_A)
            other_folder = fx.data_dir / "UserCache" / "1203923" / "11" / PCID_B
            other_folder.mkdir(parents=True)
            (other_folder / "gameVariable.xml").write_bytes(game_file_text().encode("utf-8"))
            os.utime(old_file, (1_000_000, 1_000_000))
            self.assertEqual("1203923", resolve_account(fx.settings()))
            self.assertEqual(ACCOUNT, resolve_account(fx.settings(account=ACCOUNT)))

    def test_explicit_account_without_folder_is_refused(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            fx.add_character(PCID_A)
            with self.assertRaisesRegex(ToolError, "no '11' folder"):
                resolve_account(fx.settings(account="999"))


if __name__ == "__main__":
    unittest.main()
