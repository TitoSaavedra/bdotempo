"""Settings, defaults and discovery of accounts and characters.

Precedence: CLI flag > environment variable > built-in default.
"""

from __future__ import annotations

import argparse
import os
import re
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from . import log

DEFAULT_DATA_DIR = Path(r"C:\Users\TitoS\OneDrive\Documentos\Black Desert")
DEFAULT_SKILLS_JSON = Path(r"D:\extracted_bdo\class_skills.json")
REPO_ROOT = Path(__file__).resolve().parents[2]
BACKUPS_FOLDER_NAME = "_backups"

GAME_PROCESS_NAME = "BlackDesert64.exe"
RECENT_WRITE_WINDOW_SECONDS = 10
CHARACTER_FILE_NAME = "gameVariable.xml"

_DIGITS_RE = re.compile(r"^\d+$", re.ASCII)


class ToolError(Exception):
    """An expected, user-facing failure (bad input, refused operation, ...)."""

    exit_code: int = 1


def default_backups_dir(data_dir: Path) -> Path:
    """Backups go to ``_backups`` inside the Black Desert data folder."""
    return data_dir / BACKUPS_FOLDER_NAME


@dataclass(frozen=True)
class Settings:
    data_dir: Path
    account: str | None
    skills_json: Path
    backups_dir: Path
    log_file: str | None
    verbose: bool

    @classmethod
    def from_args(cls, args: argparse.Namespace) -> "Settings":
        def pick(attr: str, env: str, default: str | None) -> str | None:
            value = getattr(args, attr, None)
            if value is None:
                value = os.environ.get(env) or default
            return value

        data_dir = Path(pick("data_dir", "BDO_DATA_DIR", str(DEFAULT_DATA_DIR)))
        skills_json = pick("skills_json", "BDO_SKILLS_JSON", str(DEFAULT_SKILLS_JSON))
        backups_dir = pick("backups_dir", "BDO_BACKUPS_DIR", None)
        return cls(
            data_dir=data_dir,
            account=pick("account", "BDO_ACCOUNT", None),
            skills_json=Path(skills_json),
            backups_dir=Path(backups_dir) if backups_dir else default_backups_dir(data_dir),
            log_file=pick("log_file", "BDO_LOG_FILE", None),
            verbose=bool(getattr(args, "verbose", False)),
        )


@dataclass(frozen=True)
class AccountInfo:
    account: str
    pcid_count: int
    newest_mtime: float


def usercache_dir(data_dir: Path) -> Path:
    return data_dir / "UserCache"


def character_file(data_dir: Path, account: str, pcid: str) -> Path:
    return usercache_dir(data_dir) / account / "11" / pcid / CHARACTER_FILE_NAME


def list_pcids(data_dir: Path, account: str) -> list[str]:
    """PCID folders (digits only) under <account>/11 that contain gameVariable.xml."""
    root = usercache_dir(data_dir) / account / "11"
    if not root.is_dir():
        return []
    pcids: list[str] = []
    for entry in root.iterdir():
        if not (entry.is_dir() and _DIGITS_RE.match(entry.name)):
            continue
        if (entry / CHARACTER_FILE_NAME).is_file():
            pcids.append(entry.name)
        else:
            log.debug("skipped folder without gameVariable.xml", folder=entry.name)
    return sorted(pcids)


def find_accounts(data_dir: Path) -> list[AccountInfo]:
    """Numeric folders under UserCache that contain an '11' subfolder."""
    root = usercache_dir(data_dir)
    if not root.is_dir():
        raise ToolError("UserCache folder not found under the data directory")
    accounts: list[AccountInfo] = []
    for entry in sorted(root.iterdir()):
        if not (entry.is_dir() and _DIGITS_RE.match(entry.name) and (entry / "11").is_dir()):
            continue
        pcids = list_pcids(data_dir, entry.name)
        mtimes = [character_file(data_dir, entry.name, p).stat().st_mtime for p in pcids]
        accounts.append(AccountInfo(entry.name, len(pcids), max(mtimes, default=0.0)))
    return accounts


def resolve_account(settings: Settings) -> str:
    """Return the account to use: explicit --account, else the only one, else the most recently played."""
    if settings.account:
        if not _DIGITS_RE.match(settings.account):
            raise ToolError("account must be numeric")
        if not (usercache_dir(settings.data_dir) / settings.account / "11").is_dir():
            raise ToolError(f"account {settings.account} has no '11' folder under UserCache")
        log.info("account selected explicitly", account=settings.account)
        return settings.account

    accounts = find_accounts(settings.data_dir)
    if not accounts:
        raise ToolError("no account with a character folder found; pass --account")
    if len(accounts) == 1:
        log.info("account auto-detected", account=accounts[0].account)
        return accounts[0].account

    chosen = max(accounts, key=lambda a: a.newest_mtime)
    log.warn(
        "several accounts found; using the most recently played one (pass --account to choose)",
        candidates=[a.account for a in accounts],
        chosen=chosen.account,
    )
    return chosen.account


def format_mtime(path: Path) -> str:
    return datetime.fromtimestamp(path.stat().st_mtime).strftime("%Y-%m-%d %H:%M:%S")
