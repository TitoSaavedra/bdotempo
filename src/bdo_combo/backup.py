"""Backups, restore and write safety for gameVariable.xml files.

Backup layout::

    <backups-dir>/<UTC yyyymmddTHHMMSSZ>/manifest.json
    <backups-dir>/<UTC yyyymmddTHHMMSSZ>/<account>/<PCID>/gameVariable.xml

The default backups-dir is ``_backups`` inside the Black Desert data folder.
Backup folders are never overwritten: if a timestamp folder already exists the
code waits for the next second and tries again.

Write rule: a game file is replaced only through ``write_game_file``, and only
when a verified backup (every copy re-hashed, SHA-256 equal to the original)
holds exactly the bytes the file has at that moment. A backup that fails
verification is removed again, so no half-made backup is left behind.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
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


def _read_file(path: Path, what: str) -> bytes:
    try:
        return path.read_bytes()
    except OSError as exc:
        raise ToolError(f"cannot read {what}: {exc}") from exc


def atomic_write(path: Path, data: bytes) -> None:
    """Write through a temporary file in the same folder, then replace the target."""
    tmp = path.with_name(path.name + ".bdo-combo.tmp")
    try:
        with open(tmp, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
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


def _verified_copy_bytes(backup_dir: Path, account: str, entry: dict) -> bytes:
    """Read one backup copy and refuse it unless its SHA-256 matches the manifest."""
    pcid = entry["pcid"]
    data = _read_file(_backup_file(backup_dir, account, pcid), f"backup file for PCID {pcid}")
    if sha256_bytes(data) != entry["sha256"]:
        raise ToolError(f"backup file for PCID {pcid} does not match its manifest")
    return data


def verify_backup(backup_dir: Path) -> dict:
    """Re-read every copy of a backup and compare it with the manifest. Returns the manifest."""
    manifest = read_manifest(backup_dir)
    for entry in manifest["files"]:
        _verified_copy_bytes(backup_dir, manifest["account"], entry)
    return manifest


def _new_backup_folder(backups_dir: Path) -> Path:
    try:
        backups_dir.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        raise ToolError(f"cannot create the backups folder: {exc}") from exc
    for _ in range(_MAX_FOLDER_ATTEMPTS):
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        folder = backups_dir / stamp
        try:
            folder.mkdir()
        except FileExistsError:
            log.debug("backup folder already exists, waiting for the next second", backup=stamp)
            time.sleep(1.0)
            continue
        except OSError as exc:
            raise ToolError(f"cannot create a backup folder: {exc}") from exc
        return folder
    raise ToolError("could not create a new backup folder")


def _write_copy(dest: Path, data: bytes) -> None:
    """Create one backup copy. Mode 'x' makes sure an existing file is never overwritten."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    with open(dest, "xb") as handle:
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())


def _copy_target(folder: Path, target: BackupTarget, account: str) -> dict:
    """Copy one character file into the backup folder and check the copy's SHA-256 against the original."""
    source = _read_file(target.path, f"PCID {target.pcid} for backup")
    digest = sha256_bytes(source)
    dest = _backup_file(folder, account, target.pcid)
    try:
        _write_copy(dest, source)
    except OSError as exc:
        raise ToolError(f"cannot write backup copy of PCID {target.pcid}: {exc}") from exc
    if sha256_bytes(_read_file(dest, f"backup copy of PCID {target.pcid}")) != digest:
        raise ToolError(f"backup copy of PCID {target.pcid} does not match the original (SHA-256)")
    log.debug("backup copy verified", pcid=target.pcid, sha256=digest)
    return {
        "pcid": target.pcid,
        "path": f"{account}/{target.pcid}/{CHARACTER_FILE_NAME}",
        "sha256": digest,
        "size": len(source),
    }


def _discard_partial_backup(folder: Path) -> None:
    """Remove a backup folder that failed, so no incomplete backup looks like a real one."""
    try:
        shutil.rmtree(folder)
    except OSError as exc:
        log.error("could not remove the partial backup folder", backup=folder.name, error=str(exc))
        return
    log.warn("partial backup removed after a failed backup", backup=folder.name)


def create_backup(
    targets: Sequence[BackupTarget],
    *,
    account: str,
    backups_dir: Path,
    reason: str,
) -> Path:
    """Copy each target into a new timestamped folder, verify every copy, then write the manifest.

    Returns the backup folder only when it is complete and verified. Otherwise the
    partial folder is removed and a ToolError is raised, so the caller writes nothing.
    """
    with log.span("backup.create", reason=reason, files=len(targets)):
        folder: Path | None = None
        try:
            folder = _new_backup_folder(backups_dir)
            files = [_copy_target(folder, target, account) for target in targets]
            manifest = {
                "tool": TOOL_NAME,
                "tool_version": __version__,
                "created_utc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
                "reason": reason,
                "account": account,
                "files": files,
            }
            try:
                (folder / MANIFEST_NAME).write_text(
                    json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
                )
            except OSError as exc:
                raise ToolError(f"cannot write the manifest of backup {folder.name}: {exc}") from exc
            verify_backup(folder)
        except BaseException as exc:
            log.error("backup failed; nothing will be written", backup=folder.name if folder else None, error=str(exc))
            if folder is not None:
                _discard_partial_backup(folder)
            if not isinstance(exc, Exception):
                raise
            raise ToolError(f"backup failed, nothing was written: {exc}") from exc
        log.info("backup created and verified", backup=folder.name, files=len(files), reason=reason)
        return folder


def write_game_file(path: Path, data: bytes, *, guard: Path, account: str, pcid: str) -> None:
    """Replace a game file. This is the only write path for gameVariable.xml.

    ``guard`` is a backup folder created just before this write. The write is refused
    unless that backup holds an intact copy (SHA-256 checked against its manifest) of
    exactly the bytes the file has now, so the replaced content can always be recovered.
    """
    with log.span("backup.write_game_file", pcid=pcid, backup=guard.name, bytes=len(data)):
        manifest = read_manifest(guard)
        if manifest["account"] != account:
            raise ToolError(f"backup {guard.name} belongs to account {manifest['account']}, not {account}")
        copy = _verified_copy_bytes(guard, account, _manifest_entry(manifest, pcid))
        current = _read_file(path, f"PCID {pcid} before writing")
        if current != copy:
            raise ToolError(f"PCID {pcid} changed after backup {guard.name} was taken; nothing was written")
        try:
            atomic_write(path, data)
        except OSError as exc:
            raise ToolError(f"cannot write PCID {pcid}: {exc}") from exc
        log.info("game file written", pcid=pcid, backup=guard.name, bytes=len(data))


def restore_file(backup_dir: Path, account: str, pcid: str, dest: Path, *, guard: Path) -> None:
    """Copy one character's file from a backup back to ``dest`` (through the write gate) and verify it."""
    with log.span("backup.restore_file", pcid=pcid, backup=backup_dir.name, guard=guard.name):
        entry = _manifest_entry(read_manifest(backup_dir), pcid)
        data = _verified_copy_bytes(backup_dir, account, entry)
        write_game_file(dest, data, guard=guard, account=account, pcid=pcid)
        if sha256_bytes(_read_file(dest, f"restored file for PCID {pcid}")) != entry["sha256"]:
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

        items: list[tuple[str, Path]] = []
        for pcid in selected:
            data = _verified_copy_bytes(folder, account, entries[pcid])
            dest = character_file(settings.data_dir, account, pcid)
            if not dest.is_file():
                raise ToolError(f"character {pcid} not found under account {account}")
            same = _read_file(dest, f"PCID {pcid}") == data
            print(f"PCID {pcid}: current file {'identical to' if same else 'differs from'} the backup")
            items.append((pcid, dest))

        if not write:
            print("DRY RUN: nothing was restored. Re-run with --write to restore.")
            return 0

        guard_write(items, force)
        pre = create_backup(
            [BackupTarget(pcid, dest) for pcid, dest in items],
            account=account,
            backups_dir=settings.backups_dir,
            reason="pre-restore",
        )
        print(f"Pre-restore backup of current files: {pre}")
        for pcid, dest in items:
            try:
                restore_file(folder, account, pcid, dest, guard=pre)
            except ToolError as exc:
                raise ToolError(f"{exc}; the previous state is in backup {pre.name}") from exc
            print(f"PCID {pcid}: restored")
    return 0
