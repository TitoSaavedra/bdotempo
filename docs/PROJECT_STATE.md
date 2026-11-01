# BDOTempo (repo `bdotempo`, formerly bdo-combo-setup) — project state and handoff (end of 2026-10-31)

Resume from here. Verify paths before relying on them. The user said the app is "very bad" and will send a bug report: WAIT FOR THAT REPORT before changing anything big.

## Goal
WinUI 3 desktop app (framework-dependent, needs .NET 10 Desktop Runtime installed; Windows App SDK bundled) with TWO modules + Settings:
1. **Apply combo**: pick class (searchable class grid with icons) -> pick guide (first one preselected: Succession / Awakening / Ascension / Purgatum / Shai's three) -> see bdoclasshub combo (buffs + combos, mobility/cancels only as a MANUAL pool) with real icons -> customize (names, memos, order, slots, quick slots) -> save "my combo" -> apply to a character (20 cooldown slots + memos + quick slots), always with verified backup.
2. **Preview**: draws ONLY the 20 cooldown slots and the 20 quick slots over the LIVE game window (Windows.Graphics.Capture of BlackDesert64.exe; black if the game is closed/minimized/capture fails), at the REAL saved positions, layouts General / Preset 1-3, hidden-slots toggle (default OFF), real BDO slot art.
3. **Settings** (bottom of the nav): account selector (auto-detected, user is account 330), game folder, language es/en, "Personajes" section (edit name/class of each live character), update check, "show confirmation before writing" switch.
Name decided by the user: **BDOTempo** (accepted the Pearl Abyss trademark risk of "BDO" in the name).

## User rules (must keep)
- ALWAYS back up before writing any BDO file (`C:\Users\TitoS\OneDrive\Documentos\Black Desert\_backups\<UTC stamp>Z\<account>\<pcid>\` + manifest.json, SHA-256 verified; abort if backup fails).
- Do NOT apply to real character files without explicit go-ahead (it was given once, see "Applied to the real game").
- TESTS: the user is angry about tests. Write/run ONLY tests for (1) writing game files and (2) settings. Nothing else. Existing ~400 C# + ~300 Python tests stay untouched until the user asks to prune (target ~60-80). NEVER write synthetic Preview tests (user verifies Preview by eye).
- NEVER launch the app/windows in loops, no screenshot loops, no `mise run screenshots` without being asked; stop agents with TaskStop and kill their process trees if they misbehave (it happened 4 times). The game (BlackDesert64.exe) may be running: never touch it. One dotnet process at a time, never `mise run check` (runs build+test in parallel).
- Searchable dropdowns (type-to-filter, es+en matching of class names) for every list > ~6 items; never show pcids in the UI (names + class icon only).
- English in code/docs/commits; UI via .resw es (neutral "tú", no voseo) + en-US; JSON logs with trace_id/span_id. Commit trailer: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` (some agents wrote Haiku 5.5).
- mise for everything (never `[env]` with exec templates). Python only for downloader/importer/asset packer.
- Screenshots (`app/docs/screenshots`) are NOT versioned (gitignored, they contain game art).

## Repo / git state
- Main repo `X:\Github\Personal\bdo-combo-setup`, branch `main`, many commits ahead of `origin/main`, NOT pushed. GitHub repo renamed to `TitoSaavedra/bdotempo` (PRIVATE) and local `origin` points to it. Remote history contains 6 old UI screenshots; the user said NOT to touch the remote (no force-push). Plan: before publishing, rewrite only the unpushed local commits so none adds game-art PNGs (verify with a history search); repo goes public only when the user says so (free CI with jdx/mise-action, and the in-app updater needs a public repo).
- All feature branches are merged into main (tooltips, port, apply, preview, settings-accounts, asset-pack, slot-visuals, app-icon, installer-updater). Worktrees under `X:\Github\Personal\worktrees\` (bdo-apply, bdo-preview, bdo-tooltips, bdo-settings, bdo-assetpack, bdo-slotvisuals, bdo-icon, bdo-installer) can be removed (`git worktree remove`).
- Last commits on main (newest first, approx.): `8b0e94f` choose-guide-after-class + first guide preselected (committed), `c815073` Inno registry lookup, `04bf3e1` class->guide flow, `dcc9063` fix ComboBox write-back loop (the 0xc00000fd stack overflow), `a49eb8f` rename to BDOTempo, merges `a0ce60d` `fce6d30` `e3a23b8`, `bd4183c` asset-pack merge.

## Uncommitted work at handoff (2026-10-31 20:28, not committed, not verified in game)

The Preview went black because every captured frame failed with
`System.InvalidCastException: Invalid cast from 'WinRT.IInspectable' to '...IDirect3DDxgiInterfaceAccess'`
(see `%LOCALAPPDATA%\BDOTempo\logs\app-20261031.log`, ~23:21 UTC, `capture.frame_failed` retried every second).
Working-tree changes touch only two files:

- `app/src/BdoComboSetup/Capture/WindowsGraphicsCapture.cs`: the surface's DXGI access is now obtained with the
  CsWinRT COM query (`frame.Surface.As<IDirect3DDxgiInterfaceAccess>()`, `GetInterface(ref iid, out nint)`, interface
  declared as IUnknown) instead of a managed cast; the first successfully read frame is logged with its size and mean
  luminance (`capture.first_frame`, useful to tell "black frame captured" from "capture broken").
- `app/src/BdoComboSetup.Core/Game/GameScreenSource.cs`: after a frame failure the capture is black and retried after
  5 s, at most 3 times in a row; the old `StateChanged` event moved up with the fields, failure memory is cleared on
  minimize/restore, window change and `Stop()`.

`mise run build` (Debug, x64) succeeds with 0 warnings. NOT committed and NOT run against the game. Decide with the
user whether to commit it before other work.

## OpenCode setup (2026-11-01)

`AGENTS.md` (repo root) holds the working rules for any agent; `opencode.json` denies edits of the game folders and
`git push`, asks before killing processes, and adds the `/estado` and `/build` commands. OpenCode sessions started in
this repository read both automatically. The Claude memories in `C:\Users\TitoS\.claude\projects\C--Users-TitoS\memory\`
are already summarized here; nothing else from that session is needed.

## Architecture
- Python (`src\bdo_combo`): guide importer + bundle (`mise run import-guides`, `build-assets`), chain kinds (buff/combo/mobility/cancels/other) and manual pool, full skill tooltips (`tooltip.cooldownSec`, cost, damage, PvP...) in skills.json, `skill_classes.json` (skillNo -> classType for class inference), asset packer (`mise run pack-assets`, `verify-assets`), the reference apply/backup CLI (`python -m bdo_combo apply --combo combos/tamer.awakening.json --pcid <pcid> [--write]`, needs `PYTHONPATH=src`, `--skills-json D:/extracted_bdo/class_skills.json`).
- Assets: ALL game icons (9,181 skill + 32 class + slot art + JSON) packed in our proprietary format **BDOA** (AES-256-GCM per entry, deterministic) as 17 stable PARTS `app\assets\parts\assets.partNN.bdoa` (35.6 MB total, committed to git by decision). The installer ships the parts and post-install runs `BDOTempo.exe --assemble-assets <partsDir> <installDir>\assets.bdoa` (headless CLI), then deletes the parts. The app reads ONLY `<exe folder>\assets.bdoa`; missing/invalid -> AssetPackErrorPage. Format doc: `app\docs\asset-pack-format.md`. Pack is obfuscation, not DRM.
- C# app `app\` (Core + WinUI app + tests): ComboApplier/BackupService/QuickSlotData writer ported from Python (layout and manifest keys identical to Python; rollback takes a pre-rollback backup first; doc `app\docs\quickslotdata-format.md`), character roster (live characters = pcids in `CharacterOrderList` of `UserCache\<account>\gamevariable.xml` with a 17-digit folder under `UserCache\<account>\11\`; name = user-assigned (`%LOCALAPPDATA%\BDOTempo\characters.json`) -> in-game label from `CharacterMemo` in `gameVariable_lobby.xml` -> "<Class> N"; class = user override -> vote of SkillNo in cooldown/memo/quick slots against skill_classes.json -> unknown), write-confirmation dialog ("be on the character selection screen", with "don't show again"), updater (GitHub Releases `TitoSaavedra/bdotempo`, SHA-256 verified, installer silent run; portable only opens the release page), BDO slot visuals (quick slot 50x65 bg 44x44 at (3,21), icon 42x42; cooldown 50x50 icon at (4,7), radial sweep from the 100-frame mask `cooltimer_mask`, time Ns/Nm/Nh), live capture (`IGameScreenSource`), app icon (`app\assets\icon`, SVG + ICO, original design).
- Packaging: `cd app && mise run package` -> `dist\BDOTempo-Setup-0.1.0.exe` (Inno Setup, 76 MB, NOT run/verified), `dist\BDOTempo-Portable-0.1.0.zip` (95 MB), `dist\SHA256SUMS.txt`; `dist\BDOTempo-run\` = unpacked runnable folder with `BDOTempo.exe` + assembled `assets.bdoa` (run it for manual tests). Inno Setup 6.7.3 currently lives in a TEMP scratchpad folder (path lost when the session temp is cleaned): reinstall permanently (`winget install JRSoftware.InnoSetup`; winget claimed it was already installed — locate ISCC; `package.ps1` `Find-Iscc` also searches registry/PATH/`INNO_SETUP_COMPILER`).

## Applied to the real game (with explicit user approval)
- 2026-10-31 ~19:58 local: Tamer Awakening (`combos/tamer.awakening.json`, 14 cooldown slots + 6 memos, duplicates Baile de la Bestia IV in slots 9/15 and Onda de Tumbo III in 3/10 accepted by the user) written to AhrinSong (pcid 10000000006346933) through the Python tool. Backup: `Black Desert\_backups\20261031T225836Z` (also older `20261029T160822Z`). The user must enter the character from the character selection screen to see it. Result in game NOT reported yet.

## Known problems / unverified (the user will send a report)
- User verdict: the app is "super mala" — expect many UI/behavior bugs; the user tests everything by hand.
- The app was launched with real files only by the user; nobody verified: roster correctness for account 330, class inference (about 19/35 inferable from bars; 16 have empty bars -> unknown class, edit in Settings > Personajes), Preview positions vs real game, live capture (never tested against the real game), Preset 1/2/3 mapping (ASSUMED Preset N = `UISettingPreset` block N-1), update flow, installer (never executed).
- Fixed crash: native stack overflow 0xc00000fd when selecting a class (re-entrancy loop in the cooldown/quick-slot ComboBox write-back, `ComboEditorViewModel`). Re-test.
- Existing tests are out of date in places (e.g. `ApplyComboViewModelTests` expected auto-selection of a guide); they will be pruned, don't fix them unless asked.
- `QuickSlotMap` (Apply) has a fixed 9-position map = the user's own bar; Preview draws 20. TODO: read each character's bindings (`ShortcutKey QuickSlotN`) and cover all 20 quick slots.
- Quick slot `SkillLevel="1"` and `KeyboardKey="0"` written by the tool are decisions, not confirmed in game.
- W3C gap: C# logs have trace_id/span_id (ActivitySource) but no `traceparent` header on outgoing HTTP (updater) and no OpenTelemetry SDK.
- Default game folder is the user's OneDrive Documentos path; detect per machine.
- Skill names in guides come from the Spanish LATAM client; guide texts are English.

## Ideas (deferred, noted by the user)

### Rotation overlay ("Hekili Priority Helper")

**Decision (user, 2026-11-01): only if it can work from real key presses.** If the presses cannot be detected, the
feature is dropped; do not build a timer-guessing version.

Goal: an overlay that shows which skill to press next, in the style of WeakAuras/Hekili, and that must look polished.
The rotation comes from the user's own skill configuration (the skills they placed in their combo / cooldown bar), not
from the raw guide: they configure the order, the overlay follows it.

- Suggested design: a priority list per class/spec (the user's configured combo order, editable), filtered by which
  skills are usable (cooldown finished, resource affordable).
- Input: it must stay **display-only**. Never send input, never inject into the game. Garmoth App already ships an
  overlay, so the user considers display-only overlays acceptable (see the Garmoth study in
  `X:\Github\Personal\garmoth-app-study-2026-10`).
- Key presses: **deferred, do not implement yet (user, 2026-11-01).** Ways to read them without touching the game, all
  passive, best first: Raw Input (`RegisterRawInputDevices` with `RIDEV_INPUTSINK`, receives `WM_INPUT` without focus, no
  hooking, no injection); a low-level keyboard hook (`WH_KEYBOARD_LL`, also passive but the kind of hook an anti-cheat
  watches); polling (`GetAsyncKeyState`, cheap but drops fast presses). Decision: implement with Raw Input, test on a
  throwaway character with an on/off switch; if the anti-cheat complains, drop it. It never synthesizes input.
- Cooldown data: `skills.json` tooltips already carry `tooltip.cooldownSec`; coverage per skill still has to be counted.
  Cooldown *state* would come from the key presses plus that duration (no OCR needed).
- Needs a first-run "review configuration" mode like Garmoth's: checklist, and a preview of what the overlay shows.
- Depends on: the full per-class skill catalog (max rank) in the asset pack, and the custom combos.

## Next work batch (user, 2026-11-01: "haz todo menos el overlay de rotación")

Order agreed with the user. Do not launch the app; the user tests at the end and reports errors.

1. **Skill catalog, wire-up (in progress).** `class-skills.json` is generated (`7d0c107`), packed and verified
   (`4b77894`, 9.279 entries) and read by `Core/GameData/SkillCatalog.cs` (`632c2e0`). Missing: load it in
   `Services/AppServices.cs` next to `SkillClassesResource` (same loader pattern) and expose it to the page view models.
2. **Slot editor: pick a skill for a slot.** Click a slot in "Editor de slots" -> searchable list with icon + name
   (only the top rank; the catalog already contains exactly that) -> the chosen skill replaces the slot's skill in the
   combo being edited. Never touches the guide.
3. **Custom combos 1..10, per class + spec** (`ComboProfileStore` today keeps one profile per class+spec). Add ten
   named slots, a selector in "Aplicar combo", and save/load/delete. The bdoclasshub guide always stays intact; a custom
   combo is always a copy.
4. **Class add-ons in "Aplicar combo".** `guide.addons` is already loaded (`ComboEditorViewModel` line ~716), where it
   only feeds the skill pool. Show a read-only section "Adicionales de la clase": skill icon + name + description and
   the effects in two columns.
5. **Character names by OCR** (idea, user asked): at the character selection screen, the rows are `class icon + Nv. NN + name`
   (real screen checked 2026-11-01). The list order is exactly `CharacterOrderList` sorted by `Order` (0-based); verified
   with four known pairs: order 0 = Clodetthe (…891118), 1 = Zeodoum (…000169), 2 = AhrinSong (…346933),
   3 = Zeoclot (…3309831). So the pcid comes from the order and the OCR only supplies name and level.
   The list has ~35 characters and only ~15 rows fit on screen, so it must be read in pages: the user scrolls the list
   himself (never send input to the game), the app captures the visible rows and accumulates them, detecting the overlap
   of the last row, until the user says done; then the accumulated list is matched to the pcid order and shown in a
   review table (name, level, class) where OCR mistakes are fixed before saving to
   `%LOCALAPPDATA%\BDOTempo\characters.json`. Class comes from the row icon against the 32 class icons in the pack.
   If the number of read rows does not match the pcids, the table must say so instead of guessing.
   **Live feedback (user, 2026-11-01):** as each row is read, draw a green box over it **on top of the game** (a separate
   overlay window: borderless, topmost, click-through and never focused — `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`, placed on
   the game window rectangle from `GetWindowRect`, one rounded green rectangle per detected row in game pixels), so the user
   sees what was captured while scrolling. The overlay must also show the running count ("12 de 35") and must never send
   input to the game. Same overlay host can later draw the rotation helper, but that feature stays out for now.
6. **Not now:** the rotation overlay (see the decision above: only with real key-press detection).

## Deferred TODO list (user: "note it, do it later")
README in English, marketing style, no build instructions (documenter agent); rewrite unpushed commits (no game art) then decide public repo; prune tests; seed names Clodetthe (10000000001891118), Zeodoum (10000000000000169), Zeoclot (10000000003309831), AhrinSong (10000000006346933) once the roster is confirmed; unify QuickSlotMap/QuickSlotKeys; `traceparent` on outgoing requests; WoW-style display-only rotation overlay ("which skill to press next", idea using `cooldownSec`; Garmoth App already ships an overlay, study in `X:\Github\Personal\garmoth-app-study-2026-10\decompiled\overlay`; must never send input); real-time Preview polish (sweep/number fonts/colors unknown: candidates `pearl.ttf`); clean-machine test of the installer; Spanish translation of guide texts; remaining sections (changelog, videos).

## Key paths
Game data (read-mostly): `C:\Users\TitoS\OneDrive\Documentos\Black Desert` (GameOption.txt 3440x1440 windowed, uiScale 1.0; `UserCache\330\11\<pcid>\gameVariable.xml`; account file `UserCache\330\gamevariable.xml`). Game client (read-only!): `G:\SteamLibrary\steamapps\common\Black Desert Online`. Client export: `D:\extracted_bdo\client` (icons, ui_probe, class_skills.json at `D:\extracted_bdo`). Slot UI spec: scratchpad `slotui\slot-ui-spec.md` (temporary; summary above). App logs: `%LOCALAPPDATA%\BDOTempo\logs`. Memory dir: `C:\Users\TitoS\.claude\projects\C--Users-TitoS\memory\`.
