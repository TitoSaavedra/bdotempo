# Test plan (proposal)

The old suite was removed. This is the list of tests I consider useful for BDOTempo, to be rewritten from scratch.
Mark each line: **keep**, **drop**, or **merge with `<id>`**. Nothing is written until you answer.

Conventions: synthetic data only (never real game files); C# tests are xUnit in `app/tests`, Python tests are `unittest` in `tests/`.
Priority: **P0** protects the player's data (must exist), **P1** protects core behavior, **P2** nice to have.
Preview rendering is not tested (verified by eye), as agreed.

## A. Writing game files (P0)

| Id | Test | Lang |
| --- | --- | --- |
| A1 | A backup with a SHA-256 manifest exists before any write; if the backup cannot be made, nothing is written | C# |
| A2 | Rollback after a failed write restores the exact original bytes (cooldown file and quick slot file) | C# |
| A3 | Cooldown slots: write all 20 slots and read them back (skill numbers, empty slots) | C# |
| A4 | Memos: write and read back, including XML special characters, empty memo and the longest accepted memo | C# |
| A5 | Quick slots: write and read back positions 1 to 20 (Key = position - 1), and clearing a position | C# |
| A6 | Quick slots: a record that holds an item or an unknown tag is refused, with no write and no backup | C# |
| A7 | Read-back verification: a tampered result triggers rollback and a clear error | C# |
| A8 | Bytes outside the edited sections are identical after a write; CRLF line endings are kept | C# |
| A9 | A character of another class than the combo is blocked; an unknown skill only warns | C# |
| A10 | Dry run produces the diff and writes nothing (no backup, no file change) | C# |
| A11 | With the write-confirmation setting on, nothing is written until the review is confirmed | C# |
| A12 | The Python reference apply CLI produces the same bytes as the C# applier for the same input | Python |

## B. Settings and options (P0)

| Id | Test | Lang |
| --- | --- | --- |
| B1 | Defaults of every setting (language, game folder empty, write confirmation on, skill tooltips on, layout) | C# |
| B2 | Save and load round trip of every setting | C# |
| B3 | A corrupt or empty settings file falls back to defaults and keeps a log entry, without crashing | C# |
| B4 | Unknown or removed fields (for example the old menu auto-hide) are ignored and not re-written as errors | C# |
| B5 | Changing language and skill tooltips raises the change notifications the UI relies on | C# |

## C. Saved combos (the 10 slots per class and spec) (P1)

| Id | Test | Lang |
| --- | --- | --- |
| C1 | Save, list, load and delete slots 1 to 10; slot 11 and slot 0 are rejected | C# |
| C2 | A schema 1 file is read as slot 1 and upgraded on the next save | C# |
| C3 | Names are trimmed and cut at 60 characters; a blank name shows the default label | C# |
| C4 | A corrupt profile file is ignored (guide shown) and logged; the file is not overwritten by reading | C# |
| C5 | Two quick saves of the same class and spec do not lose each other's slots | C# |
| C6 | A saved step linked to a guide step that no longer exists is skipped with a log, the rest loads | C# |

## D. Combo editor logic (P1)

| Id | Test | Lang |
| --- | --- | --- |
| D1 | Default slots from a guide: main combo takes slots 1 to 20 in order, buffs follow, mobility stays manual | C# |
| D2 | Assigning a skill to an occupied cooldown or quick slot swaps as the Editor does; dragging back clears | C# |
| D3 | Quick positions accept 1 to 20 and reject 0 and 21 with a clear message | C# |
| D4 | The pending list contains exactly the skills of shown chains that no cooldown slot holds | C# |
| D5 | Import from game: a synthetic character file becomes steps linked to the guide when the skill is in the first combo chain, free steps otherwise, unknown skills skipped and counted | C# |
| D6 | Import from game never writes (file bytes and timestamps unchanged) and handles a missing character or folder with a message | C# |
| D7 | "Use guide" discards the working copy and reloads the untouched guide | C# |

## E. Guides and asset pack (P1)

| Id | Test | Lang |
| --- | --- | --- |
| E1 | Every catalog entry points to an existing guide; every guide skill resolves a name and an icon in the pack | Python |
| E2 | The "Self Buffs" overlay chain is appended to Nova Succession and classified as a buff | Python |
| E3 | Chain classification rules (buff, combo, mobility, cancels, other) on representative titles | Python |
| E4 | Asset pack build, assemble and verify round trip; a missing, truncated or tampered part is detected | Python and C# |
| E5 | The C# loader reads catalog, guides, skills, tooltips and the Spanish guide texts from a small synthetic pack | C# |
| E6 | Spanish guide texts: a translated string is returned in Spanish, a missing one falls back to English, a missing resource keeps English | C# |
| E7 | Skill tooltip text: description and lines are built, empty fields are omitted, no tooltip when there is nothing to show | C# |

## F. Characters (P1)

| Id | Test | Lang |
| --- | --- | --- |
| F1 | Roster parsing from synthetic account and character files (names, class by inference, memos never used as names) | C# |
| F2 | Class inference from the skills a character has | C# |
| F3 | User overrides (name and class) are stored, applied and reset per character | C# |
| F4 | OCR scan: when the number of read rows differs from the number of characters, the result says so and applies nothing | C# |

## G. Logging, tracing, security (P1)

| Id | Test | Lang |
| --- | --- | --- |
| G1 | W3C `traceparent` and `tracestate`: generate, parse, reject malformed values | C# |
| G2 | The outgoing HTTP handler adds the headers from the current operation and logs method, host, status and duration only | C# |
| G3 | Logs never contain tokens, URL paths with secrets or personal data (redaction cases) | C# |
| G4 | Every `catch` path that matters leaves a log entry with the error (spot checks on apply, settings, profile store) | C# |

## H. Updates (P2)

| Id | Test | Lang |
| --- | --- | --- |
| H1 | Release feed parsing: newer, same and older version; malformed feed | C# |
| H2 | The downloaded package must match its SHA-256 before it is offered; nothing is installed without a click | C# |

## I. UI contract (P2)

| Id | Test | Lang |
| --- | --- | --- |
| I1 | Spanish and English resource files have exactly the same keys | C# |
| I2 | Every `x:Uid` in XAML has its keys and they name valid properties of its element | C# |
| I3 | Spanish strings use "tú" and never voseo (a small word list) | C# |
| I4 | Searchable lists match accent-insensitively in Spanish and English names | C# |

## Suggested first batch

If you want to start small: **A1 to A11, B1 to B4, C1 to C4, D3 and D5**. That covers everything that can damage a character or lose the player's data.
