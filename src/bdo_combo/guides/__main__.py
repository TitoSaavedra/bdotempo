"""Command-line entry point: ``python -m bdo_combo.guides import|bundle [options]``.

Precedence for paths: command-line flag, then environment variable, then the built-in default.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path
from typing import Sequence

from .. import log
from .bundle import BundleError, build_bundle
from .client import ClientExport, ClientExportError
from .importer import import_guides, write_outputs
from .skill_classes import SkillClassesError, load_skill_table

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_CLASSHUB_DIR = REPO_ROOT / "data" / "classhub"
DEFAULT_CLIENT_DIR = Path(r"D:\extracted_bdo\client")
DEFAULT_COMBOS_DIR = REPO_ROOT / "data" / "combos"
DEFAULT_REPORT = REPO_ROOT / "reports" / "unresolved_skills.txt"
DEFAULT_KINDS_REPORT = REPO_ROOT / "reports" / "chain_kinds.txt"
DEFAULT_BUNDLE_DIR = REPO_ROOT / "app" / "src" / "BdoComboSetup" / "Assets" / "generated"
DEFAULT_SKILL_TABLE = Path(r"D:\extracted_bdo\class_skills.json")
DEFAULT_SLOT_ART_DIR = Path(r"D:\extracted_bdo\client\ui_probe\slot_art")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python -m bdo_combo.guides", description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True, metavar="command")

    p = sub.add_parser("import", help="read the saved class pages into catalog.json and guides/*.json")
    p.add_argument("--classhub-dir", type=Path, default=None, help="saved classhub folder (env BDO_CLASSHUB_DIR)")
    p.add_argument("--client-dir", type=Path, default=None, help="client export folder (env BDO_CLIENT_DIR)")
    p.add_argument("--combos-dir", type=Path, default=None, help="output folder (default data/combos)")
    p.add_argument("--report", type=Path, default=None, help="unresolved skills report (default reports/unresolved_skills.txt)")
    p.add_argument("--kinds-report", type=Path, default=None, help="chain kinds report (default reports/chain_kinds.txt)")

    p = sub.add_parser("bundle", help="copy the referenced icons and JSON into the app's Assets/generated folder")
    p.add_argument("--combos-dir", type=Path, default=None, help="output folder of import (default data/combos)")
    p.add_argument("--client-dir", type=Path, default=None, help="client export folder (env BDO_CLIENT_DIR)")
    p.add_argument("--out-dir", type=Path, default=None, help="bundle folder (default app/src/BdoComboSetup/Assets/generated)")
    p.add_argument("--skill-table", type=Path, default=None, help="Scrappers class_skills.json (default: the file in the extracted_bdo folder on D:)")
    p.add_argument("--slot-art-dir", type=Path, default=None, help="client slot art folder (env BDO_SLOT_ART_DIR; default ui_probe/slot_art on D:)")

    for command in sub.choices.values():
        command.add_argument("--log-file", default=None, help="also append JSON logs to this file (env BDO_LOG_FILE)")
        command.add_argument("--verbose", action="store_true", help="debug logs")
    return parser


def _path(value: Path | None, env: str, default: Path) -> Path:
    if value is not None:
        return value
    return Path(os.environ[env]) if os.environ.get(env) else default


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    log_file = args.log_file or os.environ.get("BDO_LOG_FILE")
    log.configure(log_file=log_file, verbose=args.verbose, traceparent=log.env_traceparent())
    try:
        with log.span("guides", command=args.command):
            if args.command == "import":
                return _import(args)
            return _bundle(args)
    except (ClientExportError, BundleError, SkillClassesError, FileNotFoundError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    finally:
        log.close()


def _import(args: argparse.Namespace) -> int:
    classhub_dir = _path(args.classhub_dir, "BDO_CLASSHUB_DIR", DEFAULT_CLASSHUB_DIR)
    client_dir = _path(args.client_dir, "BDO_CLIENT_DIR", DEFAULT_CLIENT_DIR)
    combos_dir = args.combos_dir or DEFAULT_COMBOS_DIR
    report = args.report or DEFAULT_REPORT
    kinds_report = args.kinds_report or DEFAULT_KINDS_REPORT

    result = import_guides(classhub_dir, ClientExport(client_dir))
    write_outputs(result, combos_dir, report, kinds_report)
    stats = result.stats()
    print(f"classes {stats['classes']}, specs {stats['specs']}, guides {stats['guides']}")
    print(f"chains {stats['chains']}, steps {stats['steps']}, unique skills {stats['unique_skills']}")
    print(
        "chain kinds: "
        + ", ".join(f"{kind} {stats[f'chains_{kind}']}" for kind in ("buff", "combo", "mobility", "cancels", "other"))
        + f", low confidence {stats['chains_low_confidence']}, manual pool skills {stats['manual_pool_skills']}"
    )
    print(
        f"tooltips {stats['tooltips']}, without tooltip data {stats['tooltips_no_data']}, "
        f"name mismatches {stats['tooltip_name_mismatches']}"
    )
    print(f"unresolved skills {stats['unresolved_skills']}, icons missing {stats['icons_missing']}, problems {stats['problems']}")
    print(f"written to {combos_dir}, {report} and {kinds_report}")
    return 0


def _bundle(args: argparse.Namespace) -> int:
    client_dir = _path(args.client_dir, "BDO_CLIENT_DIR", DEFAULT_CLIENT_DIR)
    combos_dir = args.combos_dir or DEFAULT_COMBOS_DIR
    out_dir = args.out_dir or DEFAULT_BUNDLE_DIR

    skill_table = load_skill_table(args.skill_table or DEFAULT_SKILL_TABLE)
    slot_art_dir = _path(args.slot_art_dir, "BDO_SLOT_ART_DIR", DEFAULT_SLOT_ART_DIR)
    stats = build_bundle(combos_dir, ClientExport(client_dir), out_dir, skill_table, slot_art_dir=slot_art_dir)
    print(f"bundle {out_dir}: {stats.files} files, {stats.total_bytes} bytes")
    print(f"skill icons {stats.skill_icons}, class icons {stats.class_icons}, guides {stats.guides}")
    print(f"skills described {stats.skills_described}, missing skill icons {len(stats.missing_skill_icons)}")
    print(f"slot art {stats.slot_art}, missing slot art {len(stats.missing_slot_art)}")
    return 0


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="backslashreplace")
    raise SystemExit(main())
