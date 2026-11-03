# Handoff: improve the character selection screen OCR

Status: DONE and measured on a real frame, uncommitted (2026-11-03). Result of the investigation:

- **Real culprit was the parser, not the engine.** When the engine returned "Nv." and "69" as two words, the number leaked
  into the name ("69 Clodetthe"). Fixed in `CharacterScreenRowParser` (`FindLevel` now returns the last level word).
  With only that fix, the raw full-frame read already got 14/15 names.
- **Variants measured** (15 visible rows, truth checked by eye; es-MX engine): list crop at 1x 14/15 names; crop at 2x and 3x,
  grayscale, contrast and inverted all 15/15 names but lose or garble the small memo lines (FARM, KUTUM); 4x loses rows.
  Grayscale/contrast/invert gave nothing over plain 2x, so no color preprocessing.
- **Shipped:** two passes over the list side (x >= 75% of the width): 2x bilinear for names/levels/boxes, 1x for the memo text
  (`OcrImage.CropScaled`, `CharacterScreenRowParser.Combine`, `CharacterNameOcr.ReadAsync`). Real code path on three real frames:
  15/15 names, 15/15 levels, 15/15 memos, about 200 ms per page.
- **Accumulator now votes:** a known row gets one vote per later reading and the most voted reading wins (earliest on a tie).
  A scroll-bar placement that contradicts known rows is rejected before anything is changed.
- **Full scan verified with the user scrolling by hand (2026-11-03):** 35/35 characters in 16 pages, every name and level
  correct against the screen. Two memo faults found and fixed generically (memos are per account: never add a memo
  dictionary or "KARAN" -> "KARANDA" style fallbacks): (1) a warm location line right under a level line was taken as
  the next row's memo ("Holbon"), now rejected (`IsLocationOf`); (2) the accumulator votes the memo across readings. The text of a memo is NEVER judged
  (no symbol heuristics either: players may write "AFK!"); a garbled memo such as `"RANDA` for KARANDA is fixed by hand in the
  review table.
- Not verified: other resolutions / UI scales.
- The throwaway harness lives in the session scratchpad, not in the repo.

(Original notes below, kept for context.)

## Problem

The user reports that the OCR on the character selection screen is "not very good" (names/levels misread).
The user will open the game and leave it on the selection screen so the real screen can be captured and tested.

## Relevant code

- `app/src/BdoComboSetup/Capture/CharacterNameOcr.cs` — runs Windows OCR (`Windows.Media.Ocr`) over the whole captured
  frame (`GameScreenFrame`, BGRA), groups words with `CharacterScreenRowParser.Read`, matches the class icon.
- `app/src/BdoComboSetup.Core/Characters/Scan/CharacterScreenRows.cs` — word -> row parsing.
- `app/src/BdoComboSetup.Core/Characters/Scan/CharacterRowAccumulator.cs` — merges pages while the player scrolls;
  keeps the FIRST reading of each row and never corrects it; rows match with level + Levenshtein (<= max(1, len/4)).
- `app/src/BdoComboSetup/Capture/CharacterScreenVisuals.cs` — scroll bar fraction, selection-screen check.
- `app/src/BdoComboSetup/Services/CharacterScanService.cs`, `Core/ViewModels/CharacterScanViewModel.cs` — scan flow/UI.
- Recent related commits: `20d14bc` (read only the list side of the screen), `db3ae70`, `071d0c3`.
- Context: `docs/PROJECT_STATE.md` item 5 (character names by OCR).

## Findings so far (hypotheses, not yet measured)

1. No preprocessing: the full frame goes to the OCR engine as is. The game text is small, outlined, on a varying
   background.
2. The accumulator keeps the first reading of a row, so one early OCR mistake sticks.

## Plan

1. Capture a real frame of the selection screen (needs the game open on that screen; capture only, never touch the game).
2. Build a small throwaway harness (scratchpad, not in the repo, no tests per AGENTS.md) that runs the OCR on the raw
   frame vs. variants and prints what it reads:
   - crop to the character list only,
   - upscale 2x-3x,
   - grayscale + contrast / threshold (try inverted too).
3. Pick the best variant, wire it into `CharacterNameOcr.ReadAsync` (remember to map the word boxes back to the
   original frame coordinates so `AverageColor` and `ClassIconMatcher` keep working).
4. Optionally make `CharacterRowAccumulator` vote across several readings of the same row instead of keeping the first.
5. Build with `mise run build` (app/). Do not extend the existing tests; no Preview tests.

## Rules to respect (from AGENTS.md)

- Never touch the game (`BlackDesert64.exe`); capture is read-only.
- No window/app loops, no screenshot loops, no `mise run screenshots`, no `mise run check`; one `dotnet` process at a time.
- No code, diffs or build logs in the chat: report outcomes in one line.
- English in code/docs/commits; no AI trailers in commits listed by AGENTS.md; no `git push`.
- Working tree already has unrelated changes (mockup HTMLs, `anotaciones.md`); do not mix them into this work.
