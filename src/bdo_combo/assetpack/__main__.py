"""Command-line entry point: ``python -m bdo_combo.assetpack pack|verify-parts|verify|assemble [options]``.

``pack`` reads the client export (icons, slot art) and the JSON staging folder written by ``build-assets``, and
writes the stable parts into ``app/assets/parts``. ``verify-parts`` checks a parts folder (set, completeness, every
entry). ``verify`` reads one pack file. ``assemble`` joins a parts folder into one ``assets.bdoa`` (the same job the
app's ``--assemble-assets`` does). Paths: flag, then environment, then default.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path
from typing import Sequence

from .. import log
from .format import AssetPackError
from .manifest import collect_inputs
from .parts import DEFAULT_SET_ID, assemble_parts, build_parts, part_file_name, read_part_files, verify_parts, write_parts
from .reader import AssetPackReader

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_CLIENT_DIR = Path(r"D:\extracted_bdo\client")
DEFAULT_STAGING_DIR = REPO_ROOT / "data" / "asset-staging"
DEFAULT_PARTS_DIR = REPO_ROOT / "app" / "assets" / "parts"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python -m bdo_combo.assetpack", description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True, metavar="command")

    pack = sub.add_parser("pack", help="write the asset parts from the client export and the JSON staging folder")
    pack.add_argument("--client-dir", type=Path, default=None, help="client export folder (env BDO_CLIENT_DIR)")
    pack.add_argument("--staging-dir", type=Path, default=None, help="JSON staging folder (default data/asset-staging)")
    pack.add_argument("--out-dir", type=Path, default=None, help="parts folder (default app/assets/parts)")

    verify_parts_cmd = sub.add_parser("verify-parts", help="check a parts folder: set, completeness, every entry")
    verify_parts_cmd.add_argument("parts_dir", type=Path, nargs="?", default=DEFAULT_PARTS_DIR, help="parts folder (default app/assets/parts)")

    verify = sub.add_parser("verify", help="read every entry of one complete pack and check it")
    verify.add_argument("pack", type=Path, help="the .bdoa file to check")

    assemble = sub.add_parser("assemble", help="join a parts folder into one complete assets.bdoa")
    assemble.add_argument("parts_dir", type=Path, help="parts folder")
    assemble.add_argument("out", type=Path, help="the complete pack to write")

    for command in sub.choices.values():
        command.add_argument("--log-file", default=None, help="also append JSON logs to this file (env BDO_LOG_FILE)")
        command.add_argument("--verbose", action="store_true", help="debug logs")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    log.configure(log_file=args.log_file or os.environ.get("BDO_LOG_FILE"), verbose=args.verbose, traceparent=log.env_traceparent())
    try:
        with log.span("assetpack", command=args.command):
            handlers = {
                "pack": _pack,
                "verify-parts": _verify_parts,
                "verify": _verify,
                "assemble": _assemble,
            }
            return handlers[args.command](args)
    except (AssetPackError, OSError) as exc:
        log.error("assetpack failed", error=str(exc))
        print(f"error: {exc}", file=sys.stderr)
        return 1
    finally:
        log.close()


def _client_dir(value: Path | None) -> Path:
    if value is not None:
        return value
    environment = os.environ.get("BDO_CLIENT_DIR")
    return Path(environment) if environment else DEFAULT_CLIENT_DIR


def _pack(args: argparse.Namespace) -> int:
    client_dir = _client_dir(args.client_dir)
    staging_dir = args.staging_dir or DEFAULT_STAGING_DIR
    out_dir = args.out_dir or DEFAULT_PARTS_DIR
    inputs = collect_inputs(client_dir, staging_dir)
    parts = build_parts(inputs, set_id=DEFAULT_SET_ID)
    written = write_parts(parts, out_dir)
    total = sum(len(data) for data in parts.values())
    print(f"parts {out_dir}: {len(parts)} parts, {total} bytes, {len(inputs)} entries")
    for path in written:
        print(f"  {path.name}: {path.stat().st_size} bytes")
    return 0


def _verify_parts(args: argparse.Namespace) -> int:
    summary = verify_parts(args.parts_dir)
    print(f"ok: {summary['parts']} parts, {summary['entries']} entries verified in {args.parts_dir}")
    return 0


def _verify(args: argparse.Namespace) -> int:
    reader = AssetPackReader.open(args.pack)
    count = reader.verify()
    print(f"ok: {count} entries verified in {args.pack}")
    return 0


def _assemble(args: argparse.Namespace) -> int:
    readers = read_part_files(args.parts_dir)
    result = assemble_parts([(args.parts_dir / part_file_name(number)).read_bytes() for number in sorted(readers)])
    args.out.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.out.with_name(args.out.name + ".tmp")
    temporary.write_bytes(result.data)
    temporary.replace(args.out)
    print(f"assembled {args.out}: {len(result.entries)} entries, {len(result.data)} bytes")
    return 0


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="backslashreplace")
    raise SystemExit(main())
