import contextlib
import hashlib
import io
import json
import unittest
from pathlib import Path
from unittest import mock

from bdo_combo import backup
from bdo_combo.config import ToolError
from support import ACCOUNT, PCID_A, PCID_B, CharacterFixture, corrupting_copy, quiet_logs, temp_root


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class BackupRoundTripTest(unittest.TestCase):
    def test_backup_manifest_and_restore_round_trip(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            original = path.read_bytes()

            folder = backup.create_backup(
                [backup.BackupTarget(PCID_A, path)],
                account=ACCOUNT,
                backups_dir=fx.backups_dir,
                reason="test",
            )
            manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(ACCOUNT, manifest["account"])
            self.assertEqual("test", manifest["reason"])
            self.assertEqual(backup.TOOL_NAME, manifest["tool"])
            self.assertEqual(sha(original), manifest["files"][0]["sha256"])
            self.assertEqual(original, (folder / ACCOUNT / PCID_A / "gameVariable.xml").read_bytes())

            path.write_bytes(b"changed")
            pre = backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                       backups_dir=fx.backups_dir, reason="pre-restore")
            backup.restore_file(folder, ACCOUNT, PCID_A, path, guard=pre)
            self.assertEqual(original, path.read_bytes())

    def test_backup_folders_are_never_overwritten(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            first = backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                         backups_dir=fx.backups_dir, reason="one")
            second = backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                          backups_dir=fx.backups_dir, reason="two")
            self.assertNotEqual(first, second)
            self.assertEqual("one", json.loads((first / "manifest.json").read_text(encoding="utf-8"))["reason"])

    def test_corrupted_backup_is_refused(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            folder = backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                          backups_dir=fx.backups_dir, reason="test")
            (folder / ACCOUNT / PCID_A / "gameVariable.xml").write_bytes(b"corrupt")
            pre = backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                       backups_dir=fx.backups_dir, reason="pre-restore")
            before = path.read_bytes()
            with self.assertRaisesRegex(ToolError, "does not match its manifest"):
                backup.restore_file(folder, ACCOUNT, PCID_A, path, guard=pre)
            self.assertEqual(before, path.read_bytes())

    def test_list_backups_reports_empty_and_existing(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                backup.run_list_backups(fx.settings())
            self.assertIn("No backups", out.getvalue())

            path = fx.add_character(PCID_A)
            backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                 backups_dir=fx.backups_dir, reason="listed")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                backup.run_list_backups(fx.settings())
            self.assertIn("reason=listed", out.getvalue())
            self.assertIn(f"account={ACCOUNT}", out.getvalue())


class WriteGuardTest(unittest.TestCase):
    def test_refuses_when_game_running_and_file_is_fresh(self) -> None:
        with temp_root() as root, quiet_logs(), mock.patch.object(backup, "is_game_running", return_value=True):
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            with self.assertRaisesRegex(ToolError, "close the game or pass --force"):
                backup.guard_write([(PCID_A, path)], force=False)
            backup.guard_write([(PCID_A, path)], force=True)

    def test_allows_when_game_not_running(self) -> None:
        with temp_root() as root, quiet_logs(), mock.patch.object(backup, "is_game_running", return_value=False):
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            backup.guard_write([(PCID_A, path)], force=False)

    def test_old_file_is_not_blocked_even_if_game_runs(self) -> None:
        with temp_root() as root, quiet_logs(), mock.patch.object(backup, "is_game_running", return_value=True) as running:
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_B)
            old = path.stat().st_mtime - 600
            import os
            os.utime(path, (old, old))
            backup.guard_write([(PCID_B, path)], force=False)
            running.assert_not_called()


class RestoreTest(unittest.TestCase):
    def _make_backup(self, fx: CharacterFixture) -> tuple[str, bytes]:
        path = fx.add_character(PCID_A)
        original = path.read_bytes()
        folder = backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                      backups_dir=fx.backups_dir, reason="pre-apply")
        return folder.name, original

    def test_dry_run_restore_writes_nothing(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            name, _ = self._make_backup(fx)
            path = fx.account_dir / PCID_A / "gameVariable.xml"
            modified = b"modified"
            path.write_bytes(modified)
            before_backups = sorted(p.name for p in fx.backups_dir.iterdir())
            with contextlib.redirect_stdout(io.StringIO()) as out:
                code = backup.run_restore(fx.settings(), name, [], write=False, force=False)
            self.assertEqual(0, code)
            self.assertIn("DRY RUN", out.getvalue())
            self.assertEqual(modified, path.read_bytes())
            self.assertEqual(before_backups, sorted(p.name for p in fx.backups_dir.iterdir()))

    def test_write_restore_takes_pre_restore_backup_and_restores(self) -> None:
        with temp_root() as root, quiet_logs(), mock.patch.object(backup, "is_game_running", return_value=False):
            fx = CharacterFixture(root)
            name, original = self._make_backup(fx)
            path = fx.account_dir / PCID_A / "gameVariable.xml"
            path.write_bytes(b"modified")
            with contextlib.redirect_stdout(io.StringIO()):
                backup.run_restore(fx.settings(), name, [PCID_A], write=True, force=False)
            self.assertEqual(original, path.read_bytes())
            reasons = []
            for folder in fx.backups_dir.iterdir():
                manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
                reasons.append(manifest["reason"])
            self.assertIn("pre-restore", reasons)

    def test_restore_rejects_unknown_pcid_and_bad_timestamp(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            name, _ = self._make_backup(fx)
            with self.assertRaisesRegex(ToolError, "not in backup"):
                backup.run_restore(fx.settings(), name, [PCID_B], write=False, force=False)
            with self.assertRaisesRegex(ToolError, "must be a backup timestamp"):
                backup.run_restore(fx.settings(), "../etc", [], write=False, force=False)


class FailedBackupTest(unittest.TestCase):
    """A backup that cannot be verified must abort, and no game file may be written."""

    def test_copy_that_does_not_match_aborts_and_removes_partial_folder(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            with mock.patch.object(backup, "_write_copy", side_effect=corrupting_copy):
                with self.assertRaisesRegex(ToolError, "does not match the original"):
                    backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                         backups_dir=fx.backups_dir, reason="test")
            self.assertEqual([], list(fx.backups_dir.iterdir()))

    def test_io_error_while_copying_aborts_and_removes_partial_folder(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            with mock.patch.object(backup, "_write_copy", side_effect=OSError("disk full")):
                with self.assertRaisesRegex(ToolError, "cannot write backup copy"):
                    backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                         backups_dir=fx.backups_dir, reason="test")
            self.assertEqual([], list(fx.backups_dir.iterdir()))

    def test_backups_folder_that_cannot_be_created_aborts(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            original = path.read_bytes()
            blocker = root / "blocker"
            blocker.write_bytes(b"a file, not a folder")
            with self.assertRaisesRegex(ToolError, "cannot create the backups folder"):
                backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                     backups_dir=blocker / "_backups", reason="test")
            self.assertEqual(original, path.read_bytes())


class WriteGateTest(unittest.TestCase):
    """Every game-file write needs an intact backup of the bytes it replaces."""

    def _backup_of(self, fx: CharacterFixture, path: Path) -> Path:
        return backup.create_backup([backup.BackupTarget(PCID_A, path)], account=ACCOUNT,
                                    backups_dir=fx.backups_dir, reason="pre-apply")

    def test_write_proceeds_when_backup_matches_current_file(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            folder = self._backup_of(fx, path)
            backup.write_game_file(path, b"new content", guard=folder, account=ACCOUNT, pcid=PCID_A)
            self.assertEqual(b"new content", path.read_bytes())

    def test_write_refused_when_backup_copy_is_corrupt(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            folder = self._backup_of(fx, path)
            (folder / ACCOUNT / PCID_A / "gameVariable.xml").write_bytes(b"corrupt")
            before = path.read_bytes()
            with mock.patch.object(backup, "atomic_write") as write:
                with self.assertRaisesRegex(ToolError, "does not match its manifest"):
                    backup.write_game_file(path, b"new", guard=folder, account=ACCOUNT, pcid=PCID_A)
                write.assert_not_called()
            self.assertEqual(before, path.read_bytes())

    def test_write_refused_when_file_changed_after_backup(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            folder = self._backup_of(fx, path)
            path.write_bytes(b"changed by the game")
            with mock.patch.object(backup, "atomic_write") as write:
                with self.assertRaisesRegex(ToolError, "changed after backup"):
                    backup.write_game_file(path, b"new", guard=folder, account=ACCOUNT, pcid=PCID_A)
                write.assert_not_called()
            self.assertEqual(b"changed by the game", path.read_bytes())

    def test_write_refused_for_character_not_in_backup(self) -> None:
        with temp_root() as root, quiet_logs():
            fx = CharacterFixture(root)
            path = fx.add_character(PCID_A)
            folder = self._backup_of(fx, path)
            other = fx.add_character(PCID_B)
            with mock.patch.object(backup, "atomic_write") as write:
                with self.assertRaisesRegex(ToolError, "not in this backup"):
                    backup.write_game_file(other, b"new", guard=folder, account=ACCOUNT, pcid=PCID_B)
                write.assert_not_called()


if __name__ == "__main__":
    unittest.main()
