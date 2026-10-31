# bdo-combo-setup

Configures a Black Desert Online character's skill cooldown bar and skill memos
from a declarative combo file (one JSON file per class and spec, derived from
bdoclasshub.com guides). Standard library only, Python 3.12, managed by
[mise](https://mise.jdx.dev).

## What it does

It edits two sections of each character's `gameVariable.xml`:

- `SkillCoolTimeSlot`: the 20 cooldown slots.
- `SkillCoolTimeMemo`: the memo text shown for each skill.

Everything else in the file is left byte-for-byte unchanged. The file is edited
as text (it is not valid single-root XML), keeping UTF-8 without BOM and CRLF.

## Usage

```bash
mise run list-characters                         # read-only: characters and first 5 slots
mise run validate                                # cross-check combo vs class_skills.json
mise run dry-run -- --pcid 10000000001891118     # show the diff, write nothing
mise run apply -- --pcid 10000000001891118 --write   # backup first, then write
mise run list-backups
mise run backup -- --pcid 10000000001891118      # manual backup (or --all)
mise run restore -- --backup 20261029T120000Z    # dry-run; add --write to restore
```

Equivalent direct calls: `mise exec -- python -m bdo_combo <command> ...`.

Common options (CLI flag, or the environment variable in brackets):

| Option | Default |
| --- | --- |
| `--data-dir` [`BDO_DATA_DIR`] | `C:\Users\TitoS\OneDrive\Documentos\Black Desert` |
| `--account` [`BDO_ACCOUNT`] | auto-detected (see below) |
| `--skills-json` [`BDO_SKILLS_JSON`] | `D:\extracted_bdo\class_skills.json` |
| `--backups-dir` [`BDO_BACKUPS_DIR`] | `backups/` in this repo |
| `--log-file` [`BDO_LOG_FILE`] | none (logs always go to stderr) |
| `--verbose` | off (adds debug logs and unchanged entries to the diff) |

Account auto-detection: if there is one account folder with an `11` subfolder,
it is used. If there are several, the one whose character files were modified
most recently is used and a warning is logged; pass `--account` to choose
explicitly.

## Slot numbers

Slot numbers in combo files, `--skill-override` and the diff output are the
0-based numbers stored in the XML (0 to 19). When talking about the game UI,
the user counts slots 1 to 20, so XML slot 0 is the first slot (#1) and XML
slot 19 is the twentieth (#20). The diff output shows both.

## Combo file format (schema 2)

`combos/<class>.<spec>.json`:

```json
{
  "schema": 2,
  "class": "tamer",
  "classId": 16,
  "spec": "awakening",
  "source": "https://bdoclasshub.com/classes/tamer/awakening/index.html",
  "description": "LBD Dragon damage combo",
  "layout": {"combo": [0, 11], "priorityBuffs": [12, 14], "otherBuffs": [15, 19]},
  "slots":          [ {"skill": "Flow: Intimidation", "skillName": "Flujo: Intimidación", "skillNo": 1937, "memo": "R 1"} ],
  "priorityBuffs":  [ {"skill": "Summon Heuklang", "skillName": "Invocar a Heuklang", "skillNo": 1347, "memo": ""} ],
  "otherBuffs":     []
}
```

- `layout` gives an inclusive range of XML slots for each group. Ranges must
  lie within 0 to 19 and must not overlap. Each array must fit in its range.
- Entries fill their range from the first slot. Unused slots stay 0 (empty).
- Each entry has `skill` (English name, for reference), `skillName` (Spanish
  game name, cross-checked against the skill table), `skillNo` (positive
  integer), `memo` (string, or `null`), and optional `optional: true`.
- An empty `memo` (`""`) creates no memo entry. A slot without a memo keeps
  whatever memo the character already has for that skill.
- The same `skillNo` may appear in more than one slot (a warning is logged).
  Only one memo is written per skill: the first occurrence wins, and a later
  occurrence with a different non-empty memo is ignored with a warning.
- A slot's skill is written as-is. Use `--skill-override SLOT=SKILLNO` to swap
  in another rank of the same skill (for example a lower rank for a character
  who has not learned the max rank). The memo follows the new SkillNo.

`validate` checks every skill against the skill table: the SkillNo must exist,
belong to the combo's `classId`, and its group name must match `skillName`
(case-insensitive prefix comparison, markup and roman numerals ignored).
It exits non-zero on any mismatch.

## Safety

- `apply` is a dry run unless `--write` is given. `--dry-run` is accepted and
  is the default. Applying to every character is not allowed: `--pcid` is
  required.
- With `--write`, a backup of the targeted files is taken first, into
  `backups/<UTC timestamp>/` with a `manifest.json` (tool version, account,
  reason, SHA-256 of each original). Backup folders are never overwritten.
- Writes are refused while `BlackDesert64.exe` is running and the file was
  modified in the last 10 seconds (the game rewrites it). `--force` overrides
  this.
- After writing, the file is read back and validated (only the two sections
  changed, 20 slots, no duplicate memos, CRLF kept). On failure the original
  is restored from the backup and the command exits with code 3.
- `restore` is a dry run unless `--write`. Before restoring, it takes a
  `pre-restore` backup of the current files, and it applies the same game
  guard.
- Exit codes: 0 success, 1 error or refusal (including a validation mismatch),
  3 post-write validation failed and the original was restored.

## Logs

Structured JSON lines on stderr (and appended to `--log-file` if given). Each
line has `ts`, `level`, `msg`, `trace_id` and `span_id` (W3C Trace Context
style). An incoming `TRACEPARENT` environment variable continues a trace.
Logs contain PCIDs, account folders and SHA-256 hashes, but no tokens or
personal data. Tracebacks may include local file paths.

## Adding a combo

1. Copy `combos/tamer.awakening.json` to `combos/<class>.<spec>.json`.
2. Set `class`, `classId`, `spec`, `source`, `description`.
3. Choose `layout` ranges so that the arrays fit (main combo, priority buffs,
   other buffs).
4. Fill the arrays. Get each `skillNo` from the skill table (the max rank is
   usually right; use `--skill-override` for lower ranks).
5. Run `mise exec -- python -m bdo_combo validate --combo combos/<class>.<spec>.json`
   until it reports `Mismatches: 0`, then
   `mise exec -- python -m bdo_combo apply --combo combos/<class>.<spec>.json --pcid <PCID>`
   for a dry run.

## Known limitations

- Only the layout with 20 `SkillCoolTimeSlot` entries (`Version="2"`) is
  supported. Characters with an older `Version="1"` slot section (10 entries)
  are refused: `list-characters` shows them as `(unreadable)` and `apply`
  stops with an error. Nothing is written for them.
- The file does not say whether a character has learned a skill, so optional
  skills can only be dropped with `--skip-optional`, not detected automatically.

## Class guides (bdoclasshub.com)

`scripts/fetch_classhub.py` saves the guide files of bdoclasshub.com that the
site shows into `data/classhub/` (git-ignored), with the same relative paths as
the site. It starts from the class list on the warrior class page and follows
only what the site links or loads:

- each class page and the expert and spec pages it links to, plus the pages
  listed in the site's own `js/experts-index.json` and `data/survey-classes.json`
  (some spec pages are linked only from there);
- the images, scripts and stylesheets those pages reference (`img`, `srcset`,
  `data-src`, `link`, `script`, CSS `url()` and `@import`), and the references
  inside stylesheets and JSON data;
- the skill tooltip data (`data/skills/labels.json` and `data/skills/<lang>/<class>.json`
  for each language of `js/skill-tooltips.js`) and the translation files that
  `js/i18n.js` loads for each page.

Live services the scripts call at runtime (Twitch, decapi, Google Apps Script,
YouTube, Google Analytics and the site's `/api/` route) are not archived.

```bash
mise run fetch-classhub                # download the files not on disk yet
mise run fetch-classhub -- --force     # download every file again
```

`manifest.json` lists every file of the run with its kind (`page`, `tooltip`,
`icon`, `image`, `script`, `style` or `data`), status, size, SHA-256 and the page
that linked to it. It also holds a `specs` table: one row per class and spec with
its expert page and its spec page (`null` when the site has none).

Requests run one at a time with a 1 second pause and a User-Agent that identifies
the tool; no trace context is sent to the site. 429 and 5xx responses are retried
with backoff. A 404 on a linked file is a broken link on the site: it is logged as
a warning and recorded as not available. Files the scripts may request without a
link (for example a translation in another language) that answer 404 are only
recorded. Exit code 1 means a file failed (other than 404) or the class list could
not be read.

## Tests and checks

```bash
mise run test     # unit tests on synthetic fixtures only (never reads game data)
mise run check    # byte-compiles sources and checks combo files are valid JSON
```
