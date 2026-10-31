"""Synthetic pack inputs and the committed C# parity fixtures. Never reads game data.

``python tests/assetpack_support.py`` rewrites the fixtures the C# tests read, under
``app/tests/BdoComboSetup.Tests/Fixtures/assetpack/``:

* ``synthetic.bdoa`` and ``synthetic.expected.json``: one complete pack and the expected values of its entries;
* ``parts/assets.part01.bdoa``, ``parts/assets.part02.bdoa``: the same entries as two parts of one set;
* ``other-set/assets.part01.bdoa``: a part of a different set (for the "wrong set" tests).

The Python test ``test_assetpack_fixture`` fails when a committed file is not what this code produces.
"""

from __future__ import annotations

import hashlib
import json
import struct
import zlib
from pathlib import Path

from bdo_combo.assetpack import PackInput, build_pack
from bdo_combo.assetpack.parts import DEFAULT_SET_ID, build_parts, part_file_name

REPO_ROOT = Path(__file__).resolve().parents[1]
FIXTURE_DIR = REPO_ROOT / "app" / "tests" / "BdoComboSetup.Tests" / "Fixtures" / "assetpack"
FIXTURE_PACK = FIXTURE_DIR / "synthetic.bdoa"
FIXTURE_EXPECTED = FIXTURE_DIR / "synthetic.expected.json"
FIXTURE_PARTS = FIXTURE_DIR / "parts"
FIXTURE_OTHER_SET = FIXTURE_DIR / "other-set"
OTHER_SET_ID = 0x0BADC0DE
# A small bucket size so the synthetic skill icons span more than one skill part.
FIXTURE_BUCKET_SIZE = 8


def synthetic_png(width: int, height: int, seed: int) -> bytes:
    """A small RGBA PNG with a made-up pattern. Deterministic: the same arguments give the same bytes."""
    rows = []
    for y in range(height):
        row = bytearray(b"\x00")  # filter type 0
        for x in range(width):
            row += bytes(((x * 37 + seed) & 0xFF, (y * 53 + seed) & 0xFF, ((x + y) * 11 + seed) & 0xFF, 0xFF))
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(kind: bytes, payload: bytes) -> bytes:
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def synthetic_inputs() -> list[PackInput]:
    """Fake icons and JSON that cover all three compressions and two skill buckets (with the fixture bucket size)."""
    catalog = {"schema": 1, "classes": [{"classType": 99, "slug": "synthetic", "specs": []}]}
    skills = {"1": {"nameEn": "Synthetic Strike", "nameEs": "Golpe sintético", "descriptionEs": "Un golpe falso."}}
    guide = {"schema": 1, "title": "Synthetic guide", "chains": []}
    return [
        PackInput("skill-1.png", synthetic_png(4, 4, seed=1), compression="none"),
        PackInput("skill-2.png", synthetic_png(6, 6, seed=2), compression="deflate"),
        PackInput("skill-10.png", synthetic_png(8, 8, seed=10), compression="brotli"),
        PackInput("skill-17.png", synthetic_png(5, 5, seed=17)),
        PackInput("class-99.png", synthetic_png(8, 8, seed=99)),
        PackInput("slot-cooldown-bg.png", synthetic_png(12, 12, seed=7)),
        PackInput("catalog.json", _json_bytes(catalog)),
        PackInput("skills.json", _json_bytes(skills)),
        PackInput("guide-synthetic.awakening.json", _json_bytes(guide)),
    ]


def build_fixture() -> tuple[bytes, str]:
    """The complete fixture pack bytes and the expected-values file (keys, lengths, SHA-256) the C# tests read."""
    inputs = synthetic_inputs()
    result = build_pack(inputs)
    expected = {
        "format": 1,
        "keyMaterial": "default",
        "entries": [
            {
                "key": packed.key,
                "compression": packed.compression,
                "originalLength": packed.original_length,
                "sha256": hashlib.sha256(_data_of(inputs, packed.key)).hexdigest(),
            }
            for packed in result.entries
        ],
    }
    return result.data, json.dumps(expected, indent=2) + "\n"


def build_fixture_parts() -> dict[int, bytes]:
    return build_parts(synthetic_inputs(), set_id=DEFAULT_SET_ID, bucket_size=FIXTURE_BUCKET_SIZE)


def build_other_set_part() -> bytes:
    parts = build_parts(synthetic_inputs(), set_id=OTHER_SET_ID, bucket_size=FIXTURE_BUCKET_SIZE)
    return parts[1]


def write_fixture() -> None:
    data, expected = build_fixture()
    FIXTURE_DIR.mkdir(parents=True, exist_ok=True)
    FIXTURE_PACK.write_bytes(data)
    FIXTURE_EXPECTED.write_text(expected, encoding="utf-8", newline="\n")
    FIXTURE_PARTS.mkdir(parents=True, exist_ok=True)
    for number, part in build_fixture_parts().items():
        (FIXTURE_PARTS / part_file_name(number)).write_bytes(part)
    FIXTURE_OTHER_SET.mkdir(parents=True, exist_ok=True)
    (FIXTURE_OTHER_SET / part_file_name(1)).write_bytes(build_other_set_part())


def _data_of(inputs: list[PackInput], key: str) -> bytes:
    return next(item.data for item in inputs if item.key == key)


def _json_bytes(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")


if __name__ == "__main__":
    write_fixture()
    print(f"wrote fixtures under {FIXTURE_DIR}")
