"""Split the asset pack into stable parts (committed to git) and assemble them back into one ``assets.bdoa``.

Layout (fixed, so a change touches only the parts it changes):

* part 01: the core part: class icons, slot art and the JSON documents (any key that is not a skill icon);
* part 2 + b: the skill icons whose number ``n`` satisfies ``n // SKILL_BUCKET_SIZE == b`` (b from 0).

The part count is ``2 + (largest skill bucket)``, and every part from 1 to the count is written, even an empty one,
so the assembler can tell a missing part from a part that never existed. A count change (a new highest bucket)
rewrites every part header; adding icons inside existing buckets rewrites only those parts.

Each part is a complete, valid mini-pack (same entry encoding, own header and index) with the part flag set:
``number``, ``count`` and ``set_id`` in its header. Assembling all parts gives the same bytes as packing all entries
at once (see ``assemble_parts``), which is what the C# assembler checks.
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Iterable, Mapping, Sequence

from .format import AssetPackError, KeyMaterial, PartInfo, COMPRESSION_IDS, default_key_material
from .reader import AssetPackReader
from .writer import PackInput, PackResult, build_pack

CORE_PART_NUMBER = 1
SKILL_BUCKET_SIZE = 4096
FIRST_SKILL_PART_NUMBER = 2
DEFAULT_SET_ID = 0x0A55E701
PART_FILE_PATTERN = re.compile(r"^assets\.part(\d{2,3})\.bdoa$")
_SKILL_KEY = re.compile(r"^skill-(\d+)\.png$")


def part_file_name(number: int) -> str:
    return f"assets.part{number:02d}.bdoa"


def skill_number_of(key: str) -> int | None:
    match = _SKILL_KEY.match(key)
    return int(match.group(1)) if match else None


def part_number_for_key(key: str, bucket_size: int = SKILL_BUCKET_SIZE) -> int:
    number = skill_number_of(key)
    if number is None:
        return CORE_PART_NUMBER
    return FIRST_SKILL_PART_NUMBER + number // bucket_size


def part_count_for(keys: Iterable[str], bucket_size: int = SKILL_BUCKET_SIZE) -> int:
    numbers = [number for number in (skill_number_of(key) for key in keys) if number is not None]
    if not numbers:
        return CORE_PART_NUMBER
    return FIRST_SKILL_PART_NUMBER + max(numbers) // bucket_size


def build_parts(
    entries: Sequence[PackInput],
    set_id: int = DEFAULT_SET_ID,
    bucket_size: int = SKILL_BUCKET_SIZE,
    keys: KeyMaterial | None = None,
) -> dict[int, bytes]:
    """Returns {part number: part bytes} for parts 1..count. Deterministic for the same entries."""
    keys_material = keys or default_key_material()
    count = part_count_for((entry.key for entry in entries), bucket_size)
    groups: dict[int, list[PackInput]] = {number: [] for number in range(1, count + 1)}
    for entry in entries:
        groups[part_number_for_key(entry.key, bucket_size)].append(entry)
    return {
        number: build_pack(groups[number], keys_material, part=PartInfo(number, count, set_id)).data
        for number in range(1, count + 1)
    }


def write_parts(parts: Mapping[int, bytes], out_dir: Path) -> list[Path]:
    """Writes the parts (atomically, one by one) and removes part files of this name pattern that are not in the set."""
    out_dir.mkdir(parents=True, exist_ok=True)
    written: list[Path] = []
    wanted = {part_file_name(number) for number in parts}
    for number in sorted(parts):
        target = out_dir / part_file_name(number)
        temporary = target.with_name(target.name + ".tmp")
        temporary.write_bytes(parts[number])
        temporary.replace(target)
        written.append(target)
    for stale in out_dir.iterdir():
        if PART_FILE_PATTERN.match(stale.name) and stale.name not in wanted:
            stale.unlink()
    return written


def read_part_files(parts_dir: Path, keys: KeyMaterial | None = None) -> dict[int, AssetPackReader]:
    """Opens every part file in the folder, checks that the file name matches the part number in its header."""
    if not parts_dir.is_dir():
        raise AssetPackError(f"parts folder not found: {parts_dir}")
    readers: dict[int, AssetPackReader] = {}
    for path in sorted(parts_dir.iterdir()):
        match = PART_FILE_PATTERN.match(path.name)
        if match is None:
            continue
        reader = AssetPackReader.open(path, keys)
        if reader.part is None:
            raise AssetPackError(f"{path.name} is not a part (it is a complete pack or another file)")
        if reader.part.number != int(match.group(1)):
            raise AssetPackError(f"{path.name} holds part {reader.part.number}: wrong part for this file name")
        readers[reader.part.number] = reader
    return readers


def check_set(readers: Mapping[int, AssetPackReader]) -> tuple[int, int]:
    """All parts must belong to one set (same count and set id), and every part from 1 to the count must be present.

    Returns (part count, set id).
    """
    if not readers:
        raise AssetPackError("no part files found")
    infos = [reader.part for reader in readers.values()]
    count = infos[0].count
    set_id = infos[0].set_id
    for info in infos:
        if info.count != count or info.set_id != set_id:
            raise AssetPackError(f"part {info.number} belongs to another set (count {info.count}, set id {info.set_id})")
    missing = [number for number in range(1, count + 1) if number not in readers]
    if missing:
        raise AssetPackError(f"missing part(s) {', '.join(str(number) for number in missing)} of {count}")
    return count, set_id


def assemble_parts(parts: Sequence[bytes], keys: KeyMaterial | None = None) -> PackResult:
    """Joins part bytes into one complete pack. Every entry is decrypted and hash-checked; compression is kept."""
    keys_material = keys or default_key_material()
    readers: dict[int, AssetPackReader] = {}
    for data in parts:
        reader = AssetPackReader(data, keys_material)
        if reader.part is None:
            raise AssetPackError("an input is not a part")
        if reader.part.number in readers:
            raise AssetPackError(f"part {reader.part.number} given twice")
        readers[reader.part.number] = reader
    check_set(readers)
    entries: list[PackInput] = []
    seen: set[str] = set()
    for number in sorted(readers):
        reader = readers[number]
        for key in reader.keys():
            if key in seen:
                raise AssetPackError(f"entry {key} is in more than one part")
            seen.add(key)
            entry = reader.entry(key)
            entries.append(PackInput(key, reader.get(key), compression=COMPRESSION_IDS[entry.compression]))
    return build_pack(entries, keys_material)


def verify_parts(parts_dir: Path, keys: KeyMaterial | None = None) -> dict[str, int]:
    """Checks a parts folder: every part, the set, the completeness and every entry. Raises AssetPackError."""
    readers = read_part_files(parts_dir, keys)
    count, _ = check_set(readers)
    entries = 0
    for reader in readers.values():
        entries += reader.verify()
    return {"parts": count, "entries": entries}
