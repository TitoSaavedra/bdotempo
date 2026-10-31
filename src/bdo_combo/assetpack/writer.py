"""Writes a ``.bdoa`` pack: the same entries and key material always give the same bytes.

Layout (all integers little-endian, offsets absolute from the first byte of the pack)::

    header (72 bytes) | entry blobs, in key order | index

Every entry blob is ``nonce (12) || AES-256-GCM ciphertext || tag (16)`` over the compressed payload. The nonce is
synthetic (a truncated HMAC of the entry key and the SHA-256 of the plaintext), so the output is deterministic and
two different payloads under one key never share a nonce. The entry key, the compression id and the original length
are authenticated as associated data.
"""

from __future__ import annotations

import hashlib
import hmac
import os
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

import brotli
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

from .format import (
    COMPRESSION_BROTLI,
    COMPRESSION_DEFLATE,
    COMPRESSION_IDS,
    COMPRESSION_NAMES,
    COMPRESSION_NONE,
    ENTRY_FIXED_STRUCT,
    ENTRY_KEY_LENGTH,
    FLAG_ENCRYPTED,
    FLAG_PART,
    FORMAT_VERSION,
    HEADER_FIELDS_SIZE,
    HEADER_SIZE,
    HEADER_STRUCT,
    MAGIC,
    MAX_ENTRY_BYTES,
    MAX_ENTRY_COUNT,
    AssetPackError,
    KeyMaterial,
    PartInfo,
    associated_data,
    default_key_material,
    synthetic_nonce,
    validate_key,
)

BROTLI_QUALITY = 11
BROTLI_WINDOW = 22
DEFLATE_LEVEL = 9


@dataclass(frozen=True)
class PackInput:
    """One entry to pack. ``compression`` is "auto" (the smallest of none, brotli and deflate) or a fixed choice."""

    key: str
    data: bytes
    compression: str = "auto"


@dataclass(frozen=True)
class PackedEntry:
    key: str
    compression: str
    original_length: int
    stored_length: int


@dataclass(frozen=True)
class PackResult:
    data: bytes
    entries: tuple[PackedEntry, ...]


def build_pack(entries: Sequence[PackInput], keys: KeyMaterial | None = None, part: PartInfo | None = None) -> PackResult:
    """Returns the pack bytes. Entries are sorted by key, so the input order does not change the output.

    With ``part`` the file is one part of a set: the header flag is set and carries the part number, count and set id.
    """
    keys = keys or default_key_material()
    if len(entries) > MAX_ENTRY_COUNT:
        raise AssetPackError(f"too many entries: {len(entries)}")

    ordered = sorted(entries, key=lambda entry: validate_key(entry.key))
    previous: str | None = None
    for entry in ordered:
        if entry.key == previous:
            raise AssetPackError(f"duplicate pack key: {entry.key}")
        if len(entry.data) > MAX_ENTRY_BYTES:
            raise AssetPackError(f"entry {entry.key} is larger than {MAX_ENTRY_BYTES} bytes")
        previous = entry.key

    blobs: list[bytes] = []
    index_parts: list[bytes] = []
    packed: list[PackedEntry] = []
    blob_offset = HEADER_SIZE
    for entry in ordered:
        key_bytes = validate_key(entry.key)
        original_sha = hashlib.sha256(entry.data).digest()
        payload, compression = _compress(entry.data, entry.compression)
        nonce = synthetic_nonce(keys.nonce, key_bytes, original_sha)
        sealed = AESGCM(keys.encryption).encrypt(
            nonce,
            payload,
            associated_data(key_bytes, compression, len(entry.data)),
        )
        blob = nonce + sealed
        index_parts.append(
            ENTRY_KEY_LENGTH.pack(len(key_bytes))
            + key_bytes
            + ENTRY_FIXED_STRUCT.pack(
                compression,
                FLAG_ENCRYPTED,
                0,
                blob_offset,
                len(blob),
                len(entry.data),
                original_sha,
            )
        )
        blobs.append(blob)
        packed.append(PackedEntry(entry.key, COMPRESSION_IDS[compression], len(entry.data), len(blob)))
        blob_offset += len(blob)

    index = b"".join(index_parts)
    header_fields = HEADER_STRUCT.pack(
        MAGIC,
        FORMAT_VERSION,
        HEADER_SIZE,
        FLAG_PART if part is not None else 0,
        len(ordered),
        blob_offset,
        len(index),
        blob_offset + len(index),
        part.number if part is not None else 0,
        part.count if part is not None else 0,
        part.set_id if part is not None else 0,
    )
    index_mac = hmac.new(keys.index_mac, header_fields + index, hashlib.sha256).digest()
    data = header_fields + index_mac + b"".join(blobs) + index
    if len(header_fields) != HEADER_FIELDS_SIZE or len(data) != blob_offset + len(index):
        raise AssetPackError("internal error: the pack layout does not add up")
    return PackResult(data=data, entries=tuple(packed))


def write_pack(entries: Sequence[PackInput], out_path: Path, keys: KeyMaterial | None = None) -> PackResult:
    """Builds the pack and writes it through a temporary file, so a failed build never leaves a half pack."""
    result = build_pack(entries, keys)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = out_path.with_name(out_path.name + ".tmp")
    temporary.write_bytes(result.data)
    os.replace(temporary, out_path)
    return result


def _compress(data: bytes, choice: str) -> tuple[bytes, int]:
    """Returns (payload, compression id). "auto" keeps the smallest candidate; a tie goes to the lower id."""
    if choice not in COMPRESSION_NAMES and choice != "auto":
        raise AssetPackError(f"unknown compression choice: {choice}")
    candidates: dict[int, bytes] = {}
    if choice in ("auto", "none"):
        candidates[COMPRESSION_NONE] = data
    if choice in ("auto", "brotli"):
        candidates[COMPRESSION_BROTLI] = brotli.compress(data, quality=BROTLI_QUALITY, lgwin=BROTLI_WINDOW)
    if choice in ("auto", "deflate"):
        compressor = zlib.compressobj(DEFLATE_LEVEL, zlib.DEFLATED, -15)
        candidates[COMPRESSION_DEFLATE] = compressor.compress(data) + compressor.flush()
    best = min(candidates, key=lambda compression_id: (len(candidates[compression_id]), compression_id))
    return candidates[best], best
