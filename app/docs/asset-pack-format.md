# BDOA asset pack format (version 1)

The asset pack carries every image and JSON document the app needs at run time: the skill icons, the class icons,
the slot art, the combo catalog and the skill class table (`skill_classes.json`). It is one proprietary, encrypted file (`assets.bdoa`) that the app opens from
the folder next to its exe. It is not zip, 7z or tar, and it has its own magic.

The file is **built from parts**. The parts are committed to git (`app/assets/parts/assets.partNN.bdoa`) and the
installer (or the portable build) joins them into the single `assets.bdoa` on the user's machine with the app's own
assembler. The app never reads the parts.

Code: Python builder and tools in `src/bdo_combo/assetpack/`; C# reader and assembler in
`app/src/BdoComboSetup.Core/Assets/Pack/`; the headless command in `app/src/BdoComboSetup/Cli/`.

It is not a security boundary (see [Obfuscation, not DRM](#obfuscation-not-drm)).

## Conventions

- All integers are **little-endian**. Offsets are absolute, counted from the first byte of the file.
- Text in keys is ASCII.
- **Deterministic output**: the same entries (keys and bytes) and the same key material always give the same bytes.
  Entries are sorted by key, there are no timestamps, the compressor settings are fixed, and nonces are derived from
  the content (see [Entry blobs](#entry-blobs)). The input order does not change the output.

## Layout of one pack (complete or part)

```
offset 0                80                          indexOffset                       fileLength
       | header (80)     | entry blob 1 | ... | entry blob N | index (indexLength bytes) |
```

### Header (80 bytes)

| Offset | Size | Field           | Value / meaning                                                                    |
|-------:|-----:|-----------------|------------------------------------------------------------------------------------|
|      0 |    4 | `magic`         | ASCII `BDOA` (`42 44 4F 41`)                                                       |
|      4 |    2 | `formatVersion` | `1`                                                                                |
|      6 |    2 | `headerSize`    | `80`                                                                               |
|      8 |    4 | `flags`         | bit 0 = `FLAG_PART` (this file is one part of a set); other bits must be 0          |
|     12 |    4 | `entryCount`    | number of index entries                                                            |
|     16 |    8 | `indexOffset`   | absolute offset of the index (the first byte after the last blob)                 |
|     24 |    8 | `indexLength`   | length of the index in bytes                                                       |
|     32 |    8 | `fileLength`    | total length of the file; must equal the real length (catches truncation)          |
|     40 |    2 | `partNumber`    | 1-based number of this part; `0` in a complete pack                                |
|     42 |    2 | `partCount`     | number of parts of the set (1 to 999); `0` in a complete pack                      |
|     44 |    4 | `setId`         | identifies the set; all parts of one set share it; `0` in a complete pack          |
|     48 |   32 | `indexMac`      | HMAC-SHA-256 with the index key over bytes `0..47` of the header and the index    |

Rules: with `FLAG_PART` clear, `partNumber`, `partCount` and `setId` must be `0`. With `FLAG_PART` set,
`1 <= partNumber <= partCount <= 999`. The default set id is `0x0A55E701`.

### Index

The index starts at `indexOffset` and holds `entryCount` records, back to back, in strictly increasing order of key
(byte-wise, ASCII). Each record:

| Size | Field            | Meaning                                                                     |
|-----:|------------------|-----------------------------------------------------------------------------|
|    2 | `keyLength`      | 1 to 128                                                                    |
|    n | `key`            | ASCII key, pattern `[a-z0-9][a-z0-9._-]*` (for example `skill-1926.png`, `guide-tamer.awakening.json`) |
|    1 | `compression`    | `0` none, `1` raw Deflate (RFC 1951), `2` Brotli (RFC 7932)                  |
|    1 | `flags`          | bit 0 = encrypted (always set in version 1); other bits must be 0            |
|    2 | `reserved`       | `0`                                                                         |
|    8 | `storedOffset`   | absolute offset of the entry blob; must lie inside the blob area            |
|    4 | `storedLength`   | length of the blob in bytes (at least 28)                                   |
|    4 | `originalLength` | length of the plaintext after decompression (at most 64 MiB)                |
|   32 | `sha256`         | SHA-256 of the original, uncompressed bytes                                 |

The index must end exactly at `fileLength`.

### Entry blobs

Each blob is `nonce (12 bytes) | AES-256-GCM ciphertext | tag (16 bytes)`. The plaintext of the AES-GCM is the
**compressed payload** (or the raw bytes for compression `0`).

- **Key**: the 256-bit `encryption` key of the key derivation below.
- **Nonce**: synthetic, `nonce = HMAC-SHA-256(nonceKey, keyBytes || 0x00 || sha256(original))[0..11]`. The nonce depends
  on the entry name and on the content, so one key never encrypts two different payloads with the same nonce, and the
  same input always produces the same blob (deterministic output, SIV-style).
- **Associated data** (authenticated, not encrypted): `keyBytes || compression (1 byte) || originalLength (uint32 LE)`.
  Changing the key name, the compression id or the length breaks authentication.

Compression is chosen per entry: the writer tries none, Brotli (quality 11, window 2^22) and raw Deflate (level 9),
keeps the smallest, and breaks ties in favour of the lower compression id. A caller may force one method.

## Key derivation

The secret is 32 bytes: `secret = PART_A || PART_B`, each part 16 bytes. Then HKDF-SHA-256 (RFC 5869):

```
prk              = HMAC-SHA-256(salt = "BDOA/v1", secret)                (extract)
encryptionKey    = HMAC-SHA-256(prk, "bdoa/v1/aes-256-gcm" || 0x01)      (32 bytes)
nonceKey         = HMAC-SHA-256(prk, "bdoa/v1/synthetic-nonce" || 0x01)  (32 bytes)
indexMacKey      = HMAC-SHA-256(prk, "bdoa/v1/index-mac" || 0x01)       (32 bytes)
```

The constants are split between the two sides of the code. `format.py` holds `PART_A` in plain form and `PART_B`
XOR-masked with `0x5A`. `AssetPackFormat.cs` holds `PART_B` in plain form and `PART_A` XOR-masked with `0x5A`. Both
sides need the full secret, so both carry both halves; the split only means that no single constant in one file is
the whole key. The parity fixtures (a pack written by Python and read by C#) fail if the halves drift apart. The
default key vector (encryption key `6ff99b0d...20e`) is checked by a C# test and by the Python tests.

## Parts

A complete asset set is split into parts so that a change to some icons rewrites only the parts that hold them.
Partitioning is by **key bucket, never by byte offset**:

| Part | Holds |
|-----:|-------|
| `01` | the core part: class icons (`class-N.png`), slot art (`slot-*.png`) and the JSON documents (`catalog.json`, `skills.json`, `skill_classes.json`, `guide-*.json`) |
| `2 + b` | the skill icons `skill-N.png` whose number satisfies `N // 4096 == b` (bucket size 4096, `b` from 0) |

- `partCount = 2 + (largest skill bucket)`. Every part from 1 to `partCount` is written, **even when empty**, so a
  missing part can be told from a part that never existed. An empty part is a valid part with zero entries.
- Each part is a complete mini-pack (same entry encoding, own header, own index and MAC) with `FLAG_PART` set and its
  `partNumber`, `partCount` and `setId`.
- Adding or changing icons inside existing buckets changes only those parts. A new highest bucket changes `partCount`,
  which is written in every part header, so all parts are rewritten: this is the only case where the whole set moves.
- File names: `assets.partNN.bdoa` (two digits, three from part 100). The file name must agree with the part number in
  the header.
- Joining all parts gives **the same bytes as packing all entries at once** (the complete pack). The parity fixtures
  check this in both languages.

Parts in the repository: `app/assets/parts/`. The complete `assets.bdoa` is a build output and is never committed.

## The assembler

`BdoComboSetup.exe --assemble-assets <partsDir> <outFile>` (headless: no window, no WinRT). It is the same code the
Python tool uses to check, implemented in `AssetPackAssembler`.

Checks, in this order (the first failure stops the run and nothing is written):

1. `partsDir` exists.
2. There is at least one file named `assets.partNN.bdoa`.
3. Every such file is a part (`FLAG_PART`, not a complete pack), and its part number equals the number in its name.
4. All parts belong to one set: same `partCount` and same `setId`.
5. Every part from 1 to `partCount` is present, once.
6. Every entry of every part is authenticated (AES-GCM), decompressed and hash-checked. Entries are copied as blobs.
7. No key appears in two parts.
8. The complete pack is written to a temporary file next to `outFile` and renamed over it (atomic replace). The written
   file is reopened and every entry is verified again before the command reports success.

The complete pack has `flags = 0` and zero part fields, and its bytes equal the Python complete pack for the same
entries.

### Exit codes

| Code | Name            | Meaning                                                                                          |
|-----:|-----------------|--------------------------------------------------------------------------------------------------|
|    0 | ok              | `outFile` was written and verified. One JSON line on stdout: `status`, `output`, `parts`, `entries`, `bytes`. |
|    1 | unexpected      | An error the command did not expect. It is logged with its stack; one JSON line on stderr.       |
|    2 | usage           | The arguments are not `--assemble-assets <partsDir> <outFile>`.                                   |
|    3 | missing parts   | The parts folder does not exist, has no part files, or a part from 1 to `partCount` is missing.   |
|    4 | wrong parts     | A file is a complete pack, holds another part number, belongs to another set, or an entry is in two parts. |
|    5 | damaged         | A check failed: index authentication (also a wrong key), entry authentication, hash, truncation, bad header or index. |
|    6 | write failed    | The output could not be written, or the written file failed its own verification.                 |

On failure one JSON line goes to stderr: `status = "error"`, `code` (the error code, for example `part_missing`) and
`message`. The structured log of the run (with `trace_id` and `span_id`) is in `%LOCALAPPDATA%\BdoComboSetup\logs`.
When the command is started from a console window the JSON is shown there; from an installer, the exit code is the
contract.

## Runtime (the app)

- The app opens **only** `assets.bdoa` from the folder that contains `BdoComboSetup.exe`
  (`AppContext.BaseDirectory`). It does not embed the pack and does not read loose images or JSON.
- It refuses a part (`is_part`). It does not assemble anything at run time.
- If the file is missing (`missing`) or invalid (`bad_magic`, `index_authentication`, ...), the app logs an error and
  shows the **asset pack error page** (Spanish and English texts in the `.resw` files) in place of the modules. The
  page names the error code and the file path. Nothing else is shown until the file is fixed.
- Icons are read entry by entry (decrypted and checked on first use) through a bounded in-memory cache, and drawn
  from an `InMemoryRandomAccessStream`.

## Installer and portable contract (for the packaging step)

- **Installer (Inno Setup)**: installs `BdoComboSetup.exe` and the parts into `{app}\assets\parts\`. The post-install
  step runs `"{app}\BdoComboSetup.exe" --assemble-assets "{app}\assets\parts" "{app}\assets.bdoa"`, aborts the install
  when the exit code is not 0, and then deletes `{app}\assets\parts\`. The result is `{app}\BdoComboSetup.exe` and
  `{app}\assets.bdoa`.
- **Portable zip**: contains `BdoComboSetup.exe` and `assets.bdoa` (assembled at packaging time with the same command).
- **Icon (Inno Setup)**: the installer uses `app\assets\icon\app-icon.ico` as `SetupIconFile` and as the uninstall icon
  (`UninstallDisplayIcon`). The file is original art, committed in the repo. The exe already embeds it (`ApplicationIcon`
  and an embedded copy for the window), so the installer does not need to copy any icon file next to the exe.

## Obfuscation, not DRM

This format keeps casual extraction out. It is **not** digital rights management and **not** a security boundary:

- The key is in the app. Anyone who has the exe can read the two constant halves, rebuild the key and decrypt the
  pack. The halves are split and masked only so that a text search of one file does not reveal the key.
- Icons are drawn on screen, so they can always be captured from the screen.
- The purpose is that the art is not a folder of loose files that can be copied or browsed. The parts are committed
  to git as encrypted pieces; the assembled file is a build output.
- The art is copyrighted game material. Committing the parts to a repository puts that material in the repository;
  the repository must stay private.

Do not describe the pack to users as protected or secret.

## Build and check

- `mise run build-assets`: writes the JSON staging bundle (`data/asset-staging`), then the parts in `app/assets/parts`.
- `mise run pack-assets`: writes the parts from the client export (`D:\extracted_bdo\client`: `icons/skills/*.png`,
  `icons/classes/class_*.png`, `ui_probe/slot_art/*`) and the staging JSON. Parts that are no longer in the set are
  removed.
- `mise run verify-assets`: checks the parts folder: one set, every part present, every entry authenticated and hashed.
- `python -m bdo_combo.assetpack verify <file>` and `... assemble <partsDir> <outFile>` do the same as the app for a
  single pack or for the join (useful without a build of the app).
- New art: add a line to `SLOT_ART` in `src/bdo_combo/assetpack/manifest.py` (key and source path). The key must match
  the name the C# side asks for.
- Synthetic fixtures (fake images only) are regenerated with `python tests/assetpack_support.py`:
  `Fixtures/assetpack/synthetic.bdoa`, `parts/assets.part01..04.bdoa`, `other-set/assets.part01.bdoa`.

## Changing the format

Any change to the header, index, blob, nonce, associated data, part layout or key derivation changes the bytes. Update
this document, `format.py`, `parts.py`, `reader.py`, `writer.py`, `AssetPackFormat.cs`, `AssetPack.cs` and
`AssetPackAssembler.cs` in the same change, regenerate the synthetic fixtures, and run both test suites. Parts built
with an older format are not readable; rebuild them with `mise run pack-assets`.
