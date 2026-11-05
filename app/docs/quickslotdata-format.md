# QuickSlotData record format (as implemented)

Scope: the `<QuickSlotData>` section of a character's `gameVariable.xml`, read and written by
`BdoComboSetup.Core.GameData.QuickSlotData`. The section is edited as text, line by line. Examples here are synthetic.

## Section

- The file must hold exactly one `QuickSlotData` tag. Two forms are accepted:
  - Filled: `<QuickSlotData Version="3">`, the records, then `</QuickSlotData>`.
  - Self-closed: `<QuickSlotData Version="3"/>`. It has no records, so quick slot writes are refused until the game
    has a record for the bar.
- Only `Version="3"` is written. Any other version is refused.
- The body must hold exactly 20 records with `Key` 0 to 19, in that order. Blank lines are kept as they are.
  Any other line makes the section unreadable (the write is refused).

## Records

Each record is one self-closed line: indentation, a tag, attributes separated by a space, then `/>`.
Attribute values are raw text as stored (XML-escaped); the line ending (CRLF in the game files) is kept per line.

| Tag                  | Kind   | Meaning                                  | Written by the tool |
|----------------------|--------|------------------------------------------|---------------------|
| `QuickEmpty`         | Empty  | no skill in the position                 | yes (clearing)      |
| `QuickSlotSkillData` | Skill  | a skill; carries `SkillNo`               | yes                 |
| `QuickSlotItemData`  | Item   | an item in the position                  | never               |
| any other tag        | Other  | unknown                                  | never               |

Every record needs a numeric `Key`. A `QuickSlotSkillData` record needs a numeric `SkillNo`.
A position holding an Item or Other record is refused: the player must clear it in the game first.

## Positions

Quick slot position `P` (1 to 20) is the record with `Key = P - 1`. Each position may be assigned once per write.
A skill number must be positive; `null` clears the position. Positions outside 1 to 20 are refused.

The key legend of a position (`QuickSlotKeys`): positions 1 to 4 are M1 to M4, 5 and 6 are the keys 5 and 6, 7 and 8
are M5 and M6, 9 is the key 9, and 10 is the key 0 (`InputType` `QuickSlot0`). Positions 11 to 19 have `InputType`
`QuickSlot11` to `QuickSlot19`; position 20 has `QuickSlot10`. Positions 11 to 20 have no default key: their legend is
the key bound in the character file (`ShortcutKey_Action_Key`) when it is known, otherwise the plain number.

## Write rules

| Current record              | Assignment       | Result                                                                                    |
|-----------------------------|------------------|-------------------------------------------------------------------------------------------|
| Empty                       | skill N          | tag becomes `QuickSlotSkillData`; `SkillNo="N"` and `SkillLevel="1"` are added after `Key` |
| Skill M                     | skill N (N != M) | only the value of `SkillNo` changes; other attributes are kept                            |
| Skill N                     | skill N          | line unchanged                                                                            |
| Skill                       | clear            | tag becomes `QuickEmpty`; `SkillNo` and `SkillLevel` are removed; `KeyboardKey` becomes `"0"` |
| Empty                       | clear            | line unchanged                                                                            |

Example (synthetic), position 3 (`Key="2"`) set to skill 1926:

```
	<QuickEmpty Key="2" Position="0.000000 0.000000" KeyboardKey="0" Memo=""/>
	<QuickSlotSkillData Key="2" SkillNo="1926" SkillLevel="1" Position="0.000000 0.000000" KeyboardKey="0" Memo=""/>
```

Records that are not assigned keep their exact bytes. Nothing outside the section changes.

## Verification

After the write, `QuickSlotData.Verify` checks that:

- each assigned position holds the expected kind and skill number;
- every unassigned record has the same raw line as before;
- the text outside the section is unchanged (the section is masked and both texts compared).

`ComboApplier` also reads the file back and compares it with the computed bytes. Any problem triggers a rollback from the backup.

## Open points

- `SkillLevel="1"` and `KeyboardKey="0"` are the tool's choices for a placed or cleared skill. They are not confirmed
  in the game; check them in game before relying on them.
- The mapping from `Key` to the bar the player sees (and to the bound keys) is a game-side rule. This document does not cover it.
- Positions 11 to 20 are written and read back with the same rules as 1 to 9, but their `InputType` names (`QuickSlot11`
  to `QuickSlot19`, and `QuickSlot10` for position 20) and their default keys are not confirmed in a saved file. Check them
  in game before relying on the legends of those positions.
- The tests are in `app/tests/BdoComboSetup.Tests/QuickSlotDataTests.cs`; the synthetic section is `SyntheticSave.QuickSection`.
