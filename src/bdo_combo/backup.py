"""Backups, restore and write safety for gameVariable.xml files.

Backup layout::

    <backups-dir>/<UTC yyyymmddTHHMMSSZ>/manifest.json
    <backups-dir>/<UTC yyyymmddTHHMMSSZ>/<account>/<PCID>/gameVariable.xml

Backup folders are never overwritten: if a timestamp folder already exists the
code waits for the next second and tries again.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Sequence

from . import __version__, log
from .config import (
    CHARACTER_FILE_NAME,
    GAME_PROCESS_NAME,
    RECENT_WRITE_WINDOW_SECONDS,
    Settings,
    ToolError,
    character_file,
    list_pcids,
    resolve_account,
)

MANIFEST_NAME = "manifest.json"
TOOL_NAME = "bdo-combo"
_TIMESTAMP_RE = re.compile(r"^\d{8}T\d{6}Z$")
_DIGITS_RE = re.compile(r"^\d+$", re.ASCII)
_MAX_FOLDER_ATTEMPTS = 10


@dataclass(frozen=True)
class BackupTarget:
    pcid: str
    path: Path


# --- file helpers -----------------------------------------------------------


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def atomic_write(path: Path, data: bytes) -> None:
    """Write through a temporary file in the same folder, then replace the target."""
    tmp = path.with_name(path.name + ".bdo-combo.tmp")
    try:
        tmp.write_bytes(data)
        os.replace(tmp, path)
    except BaseException:
        log.error("atomic write failed", file=path.name)
        tmp.unlink(missing_ok=True)
        raise


# --- write safety -----------------------------------------------------------


def is_game_running() -> bool:
    if os.name != "nt":
        log.warn("game process check only works on Windows; assuming the game is not running")
        return False
    try:
        proc = subprocess.run(
            ["tasklist", "/FI", f"IMAGENAME eq {GAME_PROCESS_NAME}", "/NH"],
            capture_output=True,
            text=True,
            errors="replace",
            timeout=15,
            check=False,
        )
    except (OSError, subprocess.SubprocessError):
        log.error("could not query running processes; assuming the game is running")
        return True
    running = GAME_PROCESS_NAME.lower() in proc.stdout.lower()
    log.info("game process check", running=running)
    return running


def guard_write(targets: Sequence[tuple[str, Path]], force: bool) -> None:
    """Refuse to write while the game is running and a target was modified moments ago."""
    running: bool | None = None
    now = time.time()
    for pcid, path in targets:
        age = now - path.stat().st_mtime
        if age > RECENT_WRITE_WINDOW_SECONDS:
            continue
        if running is None:
            running = is_game_running()
        if not running:
            continue
        if not force:
            raise ToolError(
                f"PCID {pcid} was modified {age:.0f}s ago while {GAME_PROCESS_NAME} is running; "
                "close the game or pass --force"
            )
        log.warn("writing while the game runs (forced)", pcid=pcid)


# --- manifests and backups --------------------------------------------------


def _backup_file(backup_dir: Path, account: str, pcid: str) -> Path:
    return backup_dir / account / pcid / CHARACTER_FILE_NAME


def read_manifest(backup_dir: Path) -> dict:
    manifest_path = backup_dir / MANIFEST_NAME
    try:
        data = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise ToolError(f"cannot read manifest of backup {backup_dir.name}: {exc}") from exc
    if not isinstance(data, dict) or not isinstance(data.get("account"), str) or not _DIGITS_RE.match(data["account"]):
        raise ToolError(f"manifest of backup {backup_dir.name} has no valid account")
    files = data.get("files")
    if not isinstance(files, list):
        raise ToolError(f"manifest of backup {backup_dir.name} has no 'files' list")
    for entry in files:
        if (
            not isinstance(entry, dict)
            or not isinstance(entry.get("pcid"), str)
            or not _DIGITS_RE.match(entry["pcid"])
            or not isinstance(entry.get("sha256"), str)
        ):
            raise ToolError(f"manifest of backup {backup_dir.name} has an invalid file entry")
    return data


def _manifest_entry(manifest: dict, pcid: str) -> dict:
    for entry in manifest["files"]:
        if entry["pcid"] == pcid:
            return entry
    raise ToolError(f"PCID {pcid} is not in this backup")


def _new_backup_folder(backups_dir: Path) -> Path:
    backups_dir.mkdir(parents=True, exist_ok=True)
    for _ in range(_MAX_FOLDER_ATTEMPTS):
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        folder = backups_dir / stamp
        try:
            folder.mkdir()
        except FileExistsError:
            log.debug("backup folder already exists, waiting for the next second", backup=stamp)
            time.sleep(1.0)
            continue
        return folder
    raise ToolError("could not create a new backup folder")


def create_backup(
    targets: Sequence[BackupTarget],
    *,
    account: str,
    backups_dir: Path,
    reason: str,
) -> Path:
    """Copy each target file into a new timestamped folder and write its manifest."""
    with log.span("backup.create", reason=reason, files=len(targets)):
        folder = _new_backup_folder(backups_dir)
        files: list[dict] = []
        for target in targets:
            data = target.path.read_bytes()
            dest = _backup_file(folder, account, target.pcid)
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(data)
            if dest.read_bytes() != data:
                raise ToolError(f"backup copy of PCID {target.pcid} does not match the original")
            files.append(
                {
                    "pcid": target.pcid,
                    "path": f"{account}/{target.pcid}/{CHARACTER_FILE_NAME}",
                    "sha256": sha256_bytes(data),
                    "size": len(data),
                }
            )
        manifest = {
            "tool": TOOL_NAME,
            "tool_version": __version__,
            "created_utc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "reason": reason,
            "account": account,
            "files": files,
        }
        (folder / MANIFEST_NAME).write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
        )
        log.info("backup created", backup=folder.name, files=len(files), reason=reason)
        return folder


def restore_file(backup_dir: Path, account: str, pcid: str, dest: Path) -> None:
    """Copy one character's file from a backup back to ``dest`` and verify it."""
    with log.span("backup.restore_file", pcid=pcid, backup=backup_dir.name):
        manifest = read_manifest(backup_dir)
        entry = _manifest_entry(manifest, pcid)
        data = _backup_file(backup_dir, account, pcid).read_bytes()
        if sha256_bytes(data) != entry["sha256"]:
            raise ToolError(f"backup file for PCID {pcid} does not match its manifest")
        atomic_write(dest, data)
        if sha256_bytes(dest.read_bytes()) != entry["sha256"]:
            raise ToolError(f"restored file for PCID {pcid} does not match the backup")


# --- commands ---------------------------------------------------------------


def run_backup(settings: Settings, pcids: Sequence[str], all_characters: bool) -> int:
    if all_characters and pcids:
        raise ToolError("use either --pcid or --all, not both")
    if not all_characters and not pcids:
        raise ToolError("backup needs --pcid X (repeatable) or --all")
    with log.span("backup", all=all_characters, requested=len(pcids)):
        account = resolve_account(settings)
        selected = list_pcids(settings.data_dir, account) if all_characters else list(dict.fromkeys(pcids))
        if not selected:
            raise ToolError(f"no characters found under account {account}")
        targets: list[BackupTarget] = []
        for pcid in selected:
            path = character_file(settings.data_dir, account, pcid)
            if not path.is_file():
                raise ToolError(f"character {pcid} not found under account {account}")
            targets.append(BackupTarget(pcid, path))
        folder = create_backup(targets, account=account, backups_dir=settings.backups_dir, reason="manual")
        print(f"Backup created: {folder}")
        print(f"Characters: {len(targets)}")
    return 0


def run_list_backups(settings: Settings) -> int:
    with log.span("list-backups"):
        root = settings.backups_dir
        folders = sorted(p for p in root.iterdir() if p.is_dir()) if root.is_dir() else []
        folders = [p for p in folders if _TIMESTAMP_RE.match(p.name)]
        if not folders:
            print("No backups found.")
            return 0
        for folder in folders:
            try:
                manifest = read_manifest(folder)
            except ToolError as exc:
                log.warn("unreadable backup manifest", backup=folder.name, error=str(exc))
                print(f"{folder.name}  (unreadable manifest)")
                continue
            print(
                f"{folder.name}  reason={manifest.get('reason', '?')}  account={manifest['account']}  "
                f"characters={len(manifest['files'])}  tool={manifest.get('tool_version', '?')}"
            )
    return 0


def run_restore(settings: Settings, timestamp: str, pcids: Sequence[str], *, write: bool, force: bool) -> int:
    if not _TIMESTAMP_RE.match(timestamp):
        raise ToolError("--backup must be a backup timestamp such as 20261029T120000Z")
    folder = settings.backups_dir / timestamp
    if not folder.is_dir():
        raise ToolError(f"backup {timestamp} not found")

    with log.span("restore", backup=timestamp, write=write, force=force):
        manifest = read_manifest(folder)
        account = manifest["account"]
        if settings.account and settings.account != account:
            raise ToolError(f"backup {timestamp} belongs to account {account}, not {settings.account}")
        entries = {entry["pcid"]: entry for entry in manifest["files"]}
        selected = list(dict.fromkeys(pcids)) or sorted(entries)
        for pcid in selected:
            if pcid not in entries:
                raise ToolError(f"PCID {pcid} is not in backup {timestamp}")

        items: list[tuple[str, Path, bytes]] = []
        for pcid in selected:
            data = _backup_file(folder, account, pcid).read_bytes()
            if sha256_bytes(data) != entries[pcid]["sha256"]:
                raise ToolError(f"backup file for PCID {pcid} does not match its manifest")
            dest = character_file(settings.data_dir, account, pcid)
            if not dest.is_file():
                raise ToolError(f"character {pcid} not found under account {account}")
            same = dest.read_bytes() == data
            print(f"PCID {pcid}: current file {'identical to' if same else 'differs from'} the backup")
            items.append((pcid, dest, data))

        if not write:
            print("DRY RUN: nothing was restored. Re-run with --write to restore.")
            return 0

        guard_write([(pcid, dest) for pcid, dest, _ in items], force)
        pre = create_backup(
            [BackupTarget(pcid, dest) for pcid, dest, _ in items],
            account=account,
            backups_dir=settings.backups_dir,
            reason="pre-restore",
        )
        print(f"Pre-restore backup of current files: {pre}")
        for pcid, dest, data in items:
            atomic_write(dest, data)
            if sha256_bytes(dest.read_bytes()) != entries[pcid]["sha256"]:
                raise ToolError(
                    f"restore verification failed for PCID {pcid}; "
                    f"the previous state is in backup {pre.name}"
                )
            print(f"PCID {pcid}: restored")
    return 0
