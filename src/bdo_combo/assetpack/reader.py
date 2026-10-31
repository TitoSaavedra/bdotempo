"""Reads and verifies a ``.bdoa`` pack. Used by the tests; the app has its own reader in C#.

Every failure raises ``AssetPackError`` with a message that says which check failed. The index MAC is checked on
open, so a wrong key or a damaged index is reported before any entry is read. Each entry is authenticated by
AES-GCM and its SHA-256 is checked after decompression.
"""

from __future__ import annotations

import hashlib
import hmac
import zlib
from dataclasses import dataclass

import brotli
from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

from .format import (
    COMPRESSION_BROTLI,
    COMPRESSION_DEFLATE,
    COMPRESSION_IDS,
    COMPRESSION_NONE,
    ENTRY_FIXED_STRUCT,
    ENTRY_KEY_LENGTH,
    FLAG_ENCRYPTED,
    FLAG_PART,
    FORMAT_VERSION,
    HEADER_FIELDS_SIZE,
    HEADER_SIZE,
    HEADER_STRUCT,
    KNOWN_ENTRY_FLAGS,
    KNOWN_HEADER_FLAGS,
    MAGIC,
    MAX_ENTRY_BYTES,
    MAX_ENTRY_COUNT,
    NONCE_SIZE,
    TAG_SIZE,
    AssetPackError,
    KeyMaterial,
    PartInfo,
    associated_data,
    default_key_material,
    validate_key,
)


@dataclass(frozen=True)
class IndexEntry:
    key: str
    compression: int
    flags: int
    stored_offset: int
    stored_length: int
    original_length: int
    sha256: bytes


class AssetPackReader:
    """A parsed pack. Construction validates the header, the index MAC and the index itself."""

    def __init__(self, data: bytes, keys: KeyMaterial | None = None) -> None:
        self._data = data
        self._keys = keys or default_key_material()
        self.part: PartInfo | None = None
        self._entries = self._parse()

    @classmethod
    def open(cls, path, keys: KeyMaterial | None = None) -> "AssetPackReader":
        with open(path, "rb") as handle:
            return cls(handle.read(), keys)

    def keys(self) -> list[str]:
        return list(self._entries)

    def entry(self, key: str) -> IndexEntry:
        if key not in self._entries:
            raise AssetPackError(f"no entry named {key!r}")
        return self._entries[key]

    def get(self, key: str) -> bytes:
        """Decrypts, decompresses and checks one entry. Raises AssetPackError on any failure."""
        entry = self.entry(key)
        key_bytes = validate_key(key)
        blob = self._data[entry.stored_offset : entry.stored_offset + entry.stored_length]
        nonce, sealed = blob[:NONCE_SIZE], blob[NONCE_SIZE:]
        aad = associated_data(key_bytes, entry.compression, entry.original_length)
        try:
            payload = AESGCM(self._keys.encryption).decrypt(nonce, sealed, aad)
        except InvalidTag as exc:
            raise AssetPackError(f"entry {key!r} failed authentication (wrong key or tampered data)") from exc
        data = _decompress(payload, entry.compression, key)
        if len(data) != entry.original_length:
            raise AssetPackError(f"entry {key!r} has the wrong length after decompression")
        if hashlib.sha256(data).digest() != entry.sha256:
            raise AssetPackError(f"entry {key!r} failed its SHA-256 check")
        return data

    def verify(self) -> int:
        """Reads every entry. Returns the number of entries; raises on the first failure."""
        for key in self._entries:
            self.get(key)
        return len(self._entries)

    def _parse(self) -> dict[str, IndexEntry]:
        data = self._data
        if len(data) < HEADER_SIZE:
            raise AssetPackError("the file is shorter than the pack header")
        (
            magic,
            version,
            header_size,
            flags,
            count,
            index_offset,
            index_length,
            file_length,
            part_number,
            part_count,
            set_id,
        ) = HEADER_STRUCT.unpack_from(data, 0)
        if magic != MAGIC:
            raise AssetPackError("not a BDOA pack (bad magic)")
        if version != FORMAT_VERSION:
            raise AssetPackError(f"unsupported pack version {version}")
        if header_size != HEADER_SIZE or flags & ~KNOWN_HEADER_FLAGS:
            raise AssetPackError("unexpected header size or reserved flags")
        if flags & FLAG_PART:
            try:
                self.part = PartInfo(part_number, part_count, set_id)
            except AssetPackError as exc:
                raise AssetPackError(f"the part fields are invalid: {exc}") from exc
        elif part_number or part_count or set_id:
            raise AssetPackError("a complete pack must not carry part fields")
        if file_length != len(data):
            raise AssetPackError("the file length does not match the header (truncated or appended)")
        if not (HEADER_SIZE <= index_offset and index_offset + index_length == file_length):
            raise AssetPackError("the index bounds are inconsistent")
        if count > MAX_ENTRY_COUNT:
            raise AssetPackError("the entry count is out of range")

        index = data[index_offset:file_length]
        stored_mac = data[HEADER_FIELDS_SIZE:HEADER_SIZE]
        expected_mac = hmac.new(self._keys.index_mac, data[:HEADER_FIELDS_SIZE] + index, hashlib.sha256).digest()
        if not hmac.compare_digest(stored_mac, expected_mac):
            raise AssetPackError("index authentication failed (wrong key or damaged index)")

        entries: dict[str, IndexEntry] = {}
        position = 0
        previous: bytes | None = None
        for _ in range(count):
            if position + ENTRY_KEY_LENGTH.size > len(index):
                raise AssetPackError("the index ends inside an entry key length")
            (key_length,) = ENTRY_KEY_LENGTH.unpack_from(index, position)
            position += ENTRY_KEY_LENGTH.size
            key_bytes = index[position : position + key_length]
            position += key_length
            if len(key_bytes) != key_length or position + ENTRY_FIXED_STRUCT.size > len(index):
                raise AssetPackError("the index ends inside an entry")
            compression, entry_flags, reserved, stored_offset, stored_length, original_length, sha256 = (
                ENTRY_FIXED_STRUCT.unpack_from(index, position)
            )
            position += ENTRY_FIXED_STRUCT.size
            key = key_bytes.decode("ascii")
            validate_key(key)
            if previous is not None and key_bytes <= previous:
                raise AssetPackError("the index keys are not unique and sorted")
            previous = key_bytes
            if compression not in COMPRESSION_IDS:
                raise AssetPackError(f"entry {key!r} uses an unknown compression id {compression}")
            if entry_flags & ~KNOWN_ENTRY_FLAGS or not entry_flags & FLAG_ENCRYPTED or reserved != 0:
                raise AssetPackError(f"entry {key!r} has unexpected flags")
            if original_length > MAX_ENTRY_BYTES or stored_length < NONCE_SIZE + TAG_SIZE:
                raise AssetPackError(f"entry {key!r} has an invalid size")
            if stored_offset < HEADER_SIZE or stored_offset + stored_length > index_offset:
                raise AssetPackError(f"entry {key!r} points outside the blob area")
            entries[key] = IndexEntry(key, compression, entry_flags, stored_offset, stored_length, original_length, sha256)
        if position != len(index):
            raise AssetPackError("the index has bytes after the last entry")
        return entries


def _decompress(payload: bytes, compression: int, key: str) -> bytes:
    try:
        if compression == COMPRESSION_NONE:
            return payload
        if compression == COMPRESSION_BROTLI:
            return brotli.decompress(payload)
        if compression == COMPRESSION_DEFLATE:
            return zlib.decompress(payload, -15)
    except (brotli.error, zlib.error) as exc:
        raise AssetPackError(f"entry {key!r} does not decompress") from exc
    raise AssetPackError(f"entry {key!r} uses an unknown compression id {compression}")
