"""Constants, header and index layout, and key derivation of the ``.bdoa`` asset pack.

The format is specified in ``app/docs/asset-pack-format.md``. Everything here must stay in step with that
document and with ``app/src/BdoComboSetup.Core/Assets/Pack/AssetPackFormat.cs`` (the C# reader and assembler).
The parity tests (a pack written here, read and assembled by the C# tests) are the guard against drift.

Obfuscation, not DRM: the 32-byte secret is the concatenation of two halves. Each side keeps its own half in
plain form and the other half XOR-masked with ``_MASK``, so no single constant in one file is the whole key.
Anyone holding the app can still recover the key; the pack only keeps casual extraction out.
"""

from __future__ import annotations

import hashlib
import hmac
import re
import struct
from dataclasses import dataclass

MAGIC = b"BDOA"
FORMAT_VERSION = 1
HEADER_SIZE = 80
HEADER_FIELDS_SIZE = 48  # the header bytes covered by the index MAC (everything before the MAC itself)
# magic, version, header size, flags, entry count, index offset, index length, file length,
# part number, part count, set id
HEADER_STRUCT = struct.Struct("<4sHHIIQQQHHI")
ENTRY_FIXED_STRUCT = struct.Struct("<BBHQII32s")  # compression, flags, reserved, stored offset, stored length, original length, sha-256
ENTRY_KEY_LENGTH = struct.Struct("<H")

NONCE_SIZE = 12
TAG_SIZE = 16
MAX_KEY_BYTES = 128
MAX_ENTRY_BYTES = 64 * 1024 * 1024
MAX_ENTRY_COUNT = 1_000_000
MAX_PART_COUNT = 999

COMPRESSION_NONE = 0
COMPRESSION_DEFLATE = 1
COMPRESSION_BROTLI = 2
COMPRESSION_NAMES = {"none": COMPRESSION_NONE, "deflate": COMPRESSION_DEFLATE, "brotli": COMPRESSION_BROTLI}
COMPRESSION_IDS = {value: key for key, value in COMPRESSION_NAMES.items()}

FLAG_ENCRYPTED = 0x01  # entry flag
KNOWN_ENTRY_FLAGS = FLAG_ENCRYPTED
FLAG_PART = 0x0001  # header flag: this file is one part of a set (part number, count and set id are set)
KNOWN_HEADER_FLAGS = FLAG_PART

# Key halves. _PART_A is this side's plain half; _PART_B_MASKED is the other half XOR-masked with _MASK.
# The C# reader holds the mirror image (its plain half is B, its masked half is A).
_PART_A = bytes.fromhex("4c33513c783c0c03b0a635946aca14ef")
_PART_B_MASKED = bytes.fromhex("091163a6a264b760c4e5acd092e9965b")
_MASK = 0x5A

_SALT = b"BDOA/v1"
_INFO_ENCRYPTION = b"bdoa/v1/aes-256-gcm"
_INFO_NONCE = b"bdoa/v1/synthetic-nonce"
_INFO_INDEX_MAC = b"bdoa/v1/index-mac"

_KEY_RE = re.compile(r"^[a-z0-9][a-z0-9._-]*$")


class AssetPackError(ValueError):
    """A pack cannot be written, or a pack fails validation (bad header, bad index, wrong key, tampered entry)."""


@dataclass(frozen=True)
class KeyMaterial:
    """The three 32-byte subkeys derived from the secret: entry encryption, synthetic nonces, index MAC."""

    encryption: bytes
    nonce: bytes
    index_mac: bytes


@dataclass(frozen=True)
class PartInfo:
    """Where a part sits in its set: 1-based number, the set's part count, and the set id shared by all its parts."""

    number: int
    count: int
    set_id: int

    def __post_init__(self) -> None:
        if not 1 <= self.number <= self.count <= MAX_PART_COUNT:
            raise AssetPackError(f"invalid part {self.number} of {self.count}")
        if not 0 <= self.set_id <= 0xFFFFFFFF:
            raise AssetPackError("the set id must fit in 32 bits")


def default_key_material() -> KeyMaterial:
    return derive_key_material(_PART_A + bytes(b ^ _MASK for b in _PART_B_MASKED))


def derive_key_material(secret: bytes) -> KeyMaterial:
    """HKDF-SHA256 (RFC 5869) over the 32-byte secret, with one 32-byte output per label."""
    if len(secret) != 32:
        raise AssetPackError("the key secret must be 32 bytes")
    prk = hmac.new(_SALT, secret, hashlib.sha256).digest()

    def expand(info: bytes) -> bytes:
        return hmac.new(prk, info + b"\x01", hashlib.sha256).digest()

    return KeyMaterial(
        encryption=expand(_INFO_ENCRYPTION),
        nonce=expand(_INFO_NONCE),
        index_mac=expand(_INFO_INDEX_MAC),
    )


def validate_key(key: str) -> bytes:
    """Checks a pack key (lowercase ASCII, digits, '.', '_', '-'; at most 128 bytes) and returns it as UTF-8."""
    encoded = key.encode("ascii", errors="strict") if key.isascii() else b""
    if not encoded or len(encoded) > MAX_KEY_BYTES or not _KEY_RE.match(key):
        raise AssetPackError(f"invalid pack key: {key!r}")
    return encoded


def synthetic_nonce(nonce_key: bytes, key_bytes: bytes, original_sha256: bytes) -> bytes:
    """Deterministic 96-bit nonce: a truncated HMAC of the entry name and the plaintext hash (SIV-style)."""
    return hmac.new(nonce_key, key_bytes + b"\x00" + original_sha256, hashlib.sha256).digest()[:NONCE_SIZE]


def associated_data(key_bytes: bytes, compression: int, original_length: int) -> bytes:
    """What AES-GCM authenticates without encrypting: the entry key, its compression and its original length."""
    return key_bytes + bytes([compression]) + struct.pack("<I", original_length)
