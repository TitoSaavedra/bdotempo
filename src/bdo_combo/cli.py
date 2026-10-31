"""Command-line interface: ``python -m bdo_combo <command> [options]``."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Sequence

from . import __version__, apply, backup, gamevars, log
from .config import ToolError, Settings, character_file, format_mtime, list_pcids, resolve_account
from .skills import check_skill_refs, load_skill_index


def _common_options() -> argparse.ArgumentParser:
    common = argparse.ArgumentParser(add_help=False)
    suppress = argparse.SUPPRESS
    common.add_argument("--data-dir", default=suppress, help="Black Desert data folder (env BDO_DATA_DIR)")
    common.add_argument("--account", default=suppress, help="account folder under UserCache (env BDO_ACCOUNT)")
    common.add_argument("--skills-json", default=suppress, help="class_skills.json (env BDO_SKILLS_JSON)")
    common.add_argument("--backups-dir", default=suppress, help="backup folder (env BDO_BACKUPS_DIR)")
    common.add_argument("--log-file", default=suppress, help="also append JSON logs to this file (env BDO_LOG_FILE)")
    common.add_argument("--verbose", action="store_true", default=suppress, help="debug logs and unchanged entries")
    return common


def build_parser() -> argparse.ArgumentParser:
    common = _common_options()
    parser = argparse.ArgumentParser(
        prog="python -m bdo_combo",
        description="Configure Black Desert skill cooldown slots and memos from combo files.",
        parents=[common],
    )
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    sub = parser.add_subparsers(dest="command", required=True, metavar="command")

    sub.add_parser("list-characters", parents=[common], help="list characters and their first 5 cooldown SkillNos")

    p = sub.add_parser("validate", parents=[common], help="cross-check a combo against the skill table")
    p.add_argument("--combo", required=True, type=Path, help="combo JSON file")
    p.add_argument("--skill-override", action="append", default=[], metavar="SLOT=SKILLNO",
                   help="use another SkillNo for a game slot (0-based, repeatable)")

    p = sub.add_parser("backup", parents=[common], help="back up gameVariable.xml files")
    p.add_argument("--pcid", action="append", default=[], help="character PCID (repeatable)")
    p.add_argument("--all", action="store_true", help="back up every character of the account")

    p = sub.add_parser("apply", parents=[common], help="apply a combo (dry-run unless --write)")
    p.add_argument("--combo", required=True, type=Path, help="combo JSON file")
    p.add_argument("--pcid", action="append", default=[], help="character PCID (repeatable, required)")
    mode = p.add_mutually_exclusive_group()
    mode.add_argument("--write", action="store_true", help="really write (takes a backup first)")
    mode.add_argument("--dry-run", action="store_true", help="show the diff only (default)")
    p.add_argument("--force", action="store_true", help="write even if the game is running and the file is fresh")
    p.add_argument("--skip-optional", action="store_true", help="drop optional skills (later slots shift up)")
    p.add_argument("--skill-override", action="append", default=[], metavar="SLOT=SKILLNO",
                   help="use another SkillNo for a game slot (0-based, repeatable)")

    sub.add_parser("list-backups", parents=[common], help="list backups with their manifest summary")

    p = sub.add_parser("restore", parents=[common], help="restore files from a backup (dry-run unless --write)")
    p.add_argument("--backup", required=True, metavar="TIMESTAMP", help="backup folder name, e.g. 20261029T120000Z")
    p.add_argument("--pcid", action="append", default=[], help="restore only these PCIDs (default: all in the backup)")
    p.add_argument("--write", action="store_true", help="really restore (takes a pre-restore backup first)")
    p.add_argument("--force", action="store_true", help="restore even if the game is running and the file is fresh")
    return parser


def cmd_list_characters(settings: Settings) -> int:
    with log.span("list-characters"):
        account = resolve_account(settings)
        pcids = list_pcids(settings.data_dir, account)
        print(f"Account {account}: {len(pcids)} character(s)")
        print(f"{'PCID':<19} {'Last modified':<19}  First 5 cooldown SkillNos (slots 1-5)")
        for pcid in pcids:
            path = character_file(settings.data_dir, account, pcid)
            try:
                text = path.read_bytes().decode("utf-8")
                slots = gamevars.parse(text).slots[:5]
                shown = " ".join(str(s) for s in slots)
            except (OSError, UnicodeDecodeError, gamevars.GameVarsError) as exc:
                log.error("could not read character", pcid=pcid, error=str(exc))
                shown = "(unreadable)"
            print(f"{pcid:<19} {format_mtime(path):<19}  {shown}")
    return 0


def cmd_validate(settings: Settings, combo_path: Path, skill_overrides: Sequence[str]) -> int:
    with log.span("validate", combo=combo_path.stem):
        combo = apply.load_combo(combo_path)
        overrides = apply.parse_overrides(skill_overrides)
        index = load_skill_index(settings.skills_json)
        refs = apply.combo_refs(combo, overrides)
        findings = check_skill_refs(refs, combo.class_id, index)

        layout = ", ".join(f"{k} {v[0]}-{v[1]}" for k, v in combo.layout.items())
        print(f"Combo {combo.name}: {combo.class_name} (classId {combo.class_id}), {combo.spec}")
        print(f"Layout (0-based XML slots): {layout}")
        print()
        print(f"{'group':<14} {'slot':<12} {'guide name':<42} {'skillNo':>7}  {'table name':<44} status")
        for entry, finding in zip(combo.entries, findings):
            status = "OK" if finding.ok else "MISMATCH"
            print(
                f"{entry.group:<14} {entry.slot:>2} (#{entry.slot + 1:>2})  {entry.guide_name:<42} "
                f"{finding.skill_no:>7}  {finding.table_name:<44} {status}"
            )
        duplicates = {}
        for entry in combo.entries:
            duplicates.setdefault(entry.skill_no, []).append(entry.slot)
        for skill_no, slots in duplicates.items():
            if len(slots) > 1:
                print(f"Note: skillNo {skill_no} is used in slots {slots}")

        mismatches = [f for f in findings if not f.ok]
        print()
        print(f"Mismatches: {len(mismatches)}")
        for finding in mismatches:
            print(f"  slot {finding.slot} (skillNo {finding.skill_no}): {'; '.join(finding.problems)}")
    return 1 if mismatches else 0


def dispatch(args: argparse.Namespace, settings: Settings) -> int:
    command = args.command
    if command == "list-characters":
        return cmd_list_characters(settings)
    if command == "validate":
        return cmd_validate(settings, args.combo, args.skill_override)
    if command == "backup":
        return backup.run_backup(settings, args.pcid, args.all)
    if command == "apply":
        return apply.run_apply(
            settings,
            args.combo,
            args.pcid,
            write=args.write,
            force=args.force,
            skip_optional=args.skip_optional,
            overrides=apply.parse_overrides(args.skill_override),
        )
    if command == "list-backups":
        return backup.run_list_backups(settings)
    if command == "restore":
        return backup.run_restore(settings, args.backup, args.pcid, write=args.write, force=args.force)
    raise ToolError(f"unknown command {command}")


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    settings = Settings.from_args(args)
    log.configure(log_file=settings.log_file, verbose=settings.verbose, traceparent=log.env_traceparent())
    try:
        with log.span("cli", command=args.command, version=__version__):
            return dispatch(args, settings)
    except ToolError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return exc.exit_code
    except KeyboardInterrupt:
        print("interrupted", file=sys.stderr)
        return 130
    except Exception as exc:  # noqa: BLE001 - logged with traceback by the span; keep the CLI exit code
        print(f"unexpected error ({type(exc).__name__}): {exc}", file=sys.stderr)
        return 1
    finally:
        log.close()
