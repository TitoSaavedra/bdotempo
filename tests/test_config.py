import argparse
import os
import unittest
from pathlib import Path
from unittest import mock

from bdo_combo.config import DEFAULT_DATA_DIR, Settings, default_backups_dir
from support import temp_root


def settings_for(**values: str) -> Settings:
    """Settings built as the CLI does it, with no BDO_* environment variables set."""
    with mock.patch.dict(os.environ):
        for name in ("BDO_DATA_DIR", "BDO_BACKUPS_DIR"):
            os.environ.pop(name, None)
        return Settings.from_args(argparse.Namespace(**values))


class DefaultBackupsDirTest(unittest.TestCase):
    def test_default_is_backups_folder_inside_the_data_dir(self) -> None:
        self.assertEqual(DEFAULT_DATA_DIR / "_backups", settings_for().backups_dir)

    def test_default_follows_the_data_dir_override(self) -> None:
        with temp_root() as root:
            data = root / "elsewhere"
            self.assertEqual(data / "_backups", settings_for(data_dir=str(data)).backups_dir)

    def test_backups_dir_flag_overrides_the_default(self) -> None:
        with temp_root() as root:
            flag = root / "mine"
            self.assertEqual(flag, settings_for(backups_dir=str(flag)).backups_dir)

    def test_backups_dir_environment_variable_overrides_the_default(self) -> None:
        with temp_root() as root, mock.patch.dict(os.environ, {"BDO_BACKUPS_DIR": str(root / "env")}):
            self.assertEqual(root / "env", Settings.from_args(argparse.Namespace()).backups_dir)

    def test_helper_returns_the_underscore_backups_folder(self) -> None:
        self.assertEqual(Path("X:/data") / "_backups", default_backups_dir(Path("X:/data")))


if __name__ == "__main__":
    unittest.main()
