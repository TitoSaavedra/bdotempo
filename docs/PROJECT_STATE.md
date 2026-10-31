# bdo-combo-setup — project state and handoff (2026-10-31)

Resume from here. Everything below was decided with the user; verify paths before relying on them.

## Goal
WinUI 3 desktop app (single self-contained .exe) with TWO modules only:
1. **Apply combo**: pick class (grid of class icons) + guide (Succession / Awakening / Ascension / Purgatum / Shai's three), see the bdoclasshub combo (buffs + combos) with real icons, customize (own names, memos, order, slots, quick slots), save as "my combo", and apply it to a character (cooldown bar slots 1-20 + memos + quick slots), always with backup.
2. **Preview**: draw ONLY the 20 cooldown slots and the quick slots over a game screenshot, exactly like Black Desert, at their REAL saved positions (monitor 3440x1440, UI scale from files), layouts General / Preset 1 / 2 / 3, hidden slots toggle (default OFF), future real-time capture of the game window (stub today).
Focus now: **buffs and combos**. Mobility (`class-mobility`) skills are wanted too but only as a MANUAL pool (user adds them to cooldown slots by hand, never auto-applied). `class-cancels` (Wizard only) also wanted. Everything else (changelog, videos, etc.) later.

## User rules (must keep)
- Always back up before writing any BDO file; backups go in `C:\Users\TitoS\OneDrive\Documentos\Black Desert\_backups\<UTC stamp>Z\<account>\<pcid>\` (+ manifest.json, SHA-256 verified); abort if backup fails. Memory: `always-backup-bdo-files`.
- No before/after Product Owner flow in this repo (memory `bdo-combo-setup-no-po-flow`).
- WinUI 3 (not MAUI/WPF/WinForms), single-file self-contained exe, mise for all tooling (never `[env]` with `exec` templates: it caused infinite mise recursion and froze the PC), one dotnet process at a time, BelowNormal priority, no background loops.
- English for code/docs/logs/commits; UI via .resw es (neutral "tú", no voseo) + en-US; structured JSON logs with trace_id/span_id; commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Python stays ONLY for the downloader and data processing (guide importer, bundle builder). The apply/backup/restore logic is being ported to C#; the Python apply tool stays as reference until C# passes parity, then is retired.
- Do NOT apply anything to the user's live character files without their explicit go-ahead; dry-run on a scratch copy first and show the diff.

## Repos, branches, locations
- Main repo: `X:\Github\Personal\bdo-combo-setup` (GitHub private `TitoSaavedra/bdo-combo-setup`), branch `main`, commits: initial import, guide importer + bundle + catalog contract (`84e9a8f`), backup hardening (`7051f01`). Python tool in `src\bdo_combo`, importer in `src\bdo_combo\guides`, app in `app\` (Core + app + tests).
- Worktrees (parallel agents, NOT merged yet): `X:\Github\Personal\worktrees\bdo-apply` (branch `wip/apply-redesign`, Apply page), `...\bdo-preview` (`feature/preview-game-screen`), `...\bdo-tooltips` (`feature/skill-tooltips`). The port-to-C# agent works directly in `main`'s `app\` (uncommitted when this was written). Merge order suggestion: tooltips (additive data) -> port (IComboApplier) -> apply -> preview, resolving conflicts in IComboApplier/ApplyComboViewModel/AppServices/.resw (keys prefixed `Apply.` and `Preview.`).
- Extractor patch: `X:\Github\Personal\bdo-global-scraper`, branch `feature/client-assets-icons` pushed (commit `f885df2`, patch in `apps/backend/extractor_go_patches/local-patches.diff`). The Go extractor clone `apps\backend\.extractor-go` has `client-assets` command.
- Study of Garmoth (Electron app, for reference only, do not redistribute): `X:\Github\Personal\garmoth-app-study-2026-10` (fresh extraction + Ghidra overlay decompile `decompiled\overlay`; OCR decompile failed, not needed). Ghidra 12.2 + JDK 25 via mise in that folder.
- Game client (Spanish LATAM, read-only!): `G:\SteamLibrary\steamapps\common\Black Desert Online`. Never write there, never start the game from tooling.
- Data folders (not in git): `data\classhub\` (bdoclasshub HTML + `data\skills\<lang>\<class>.json` tooltips + images, 57 MB), `data\combos\` (importer output), `reports\`, `app\src\BdoComboSetup\Assets\generated\` (embedded bundle: 1491 skill icons, 32 class icons, 58 guides, catalog.json, skills.json; copyrighted art stays out of git). Rebuild: `mise run import-guides`, `mise run build-assets`, `mise run fetch-classhub`.
- Client export: `D:\extracted_bdo\client\` (icons\skills\<skillNo>.png 9181, icons\classes\class_<classType>.png 32 white-on-transparent, skills_es.json 29,459 names/descriptions, loc_es.json, ui_probe\ with slot art + `slot_layout.json` + lua disassembly). Older extractor data: `D:\extracted_bdo`. Older icon scraper output (fallback): `X:\Github\Personal\Scrappers\output`.
- Artifact mockup (private): https://claude.ai/artifact/HdKM9w8Vfh8bhRtjhFanfa (approved visual direction: window #101216, nav #14171c, page #171a20, cards #1b1e25, border #2b303a, text #e8e6e1, muted #a3a8b3, gold #d2b06a).
- Memory dir: `C:\Users\TitoS\.claude\projects\C--Users-TitoS-OneDrive-Documentos-Black-Desert\memory\`.

## Verified facts
- Game files: GameOption.txt (`C:\Users\TitoS\OneDrive\Documentos\Black Desert\GameOption.txt`): width 3440, height 1440, windowed 1, uiScale 1.0. Character file: `UserCache\330\11\<pcid>\gameVariable.xml` (not valid single-root XML; edit as text, UTF-8 no BOM, CRLF). Account file with UI positions: `UserCache\330\gamevariable.xml`. AhrinSong (Tamer) PCID `10000000006346933`; Guardian reference `10000000001891118`.
- `UIData Index` = value of the game's `PAGameUIPanel` enum. Quick slots = panels 91..110 (index = 90 + slot, 100 = key 0), each 50x65; skill cooldown slots = 128..137 and 148..157 (20 panels, 50x50); 111 is a different panel. Hidden panels (`IsShow=false`) still keep a saved position (user's file: quick 91-97 and 100 visible, 98/99/101-110 hidden; all 20 cooldown visible). Each slot is its own panel.
- Rectangle math: anchor by `PendingType` (Left/Right x Top/Bottom); x = PosX if Left else W-PosX; y = PosY if Top else H-PosY; center = RelativePos*W/H; size = 2*(center-corner).
- Layouts: General = top-level `<UIData>` rows; Presets = `<UISettingPreset0/1/2>` blocks (ASSUMPTION: Preset N = block N-1, keep in one constant; there is no preset 3 block).
- Quick slot keys (user-confirmed): positions 1-4 mouse buttons (keyCodes 124-127), 5 and 6 keyboard (53, 54), 7 and 8 extra mouse buttons M5/M6 (keyCodes 5, 6), 9 keyboard (57), slot 10 = key 0. Read the real bound key from `ShortcutKey QuickSlotN`.
- BDO slot art: quick slot 50x65: slot background 44x44 at (3,21) semi-transparent (max alpha 131), icon 42x42 at (4,22), key legend on top (#626262 empty, #40D7FD skill, #88DF00 item), handle strip only on hover. Cooldown slot 50x50: NO frame, only 42x42 icon at (4,7) (greyed if unusable), radial sweep + number (the real sweep art is an internal control; approximated).
- skillNo -> icon: client export `icons\skills\<skillNo>.png` (6007 of 6030 Scrappers references match; 313 skills have no DDS in the PAZ, listed in `D:\extracted_bdo\client\reports\`). Class icon mapping by atlas order verified visually for all 32 (Dosa 33 and Agent 35 confirmed by the user).
- Guides: 58 guides, 489 chains, 2404 steps, 1491 skills, 0 unresolved names; texts of guides are English (site), skill names/descriptions Spanish (client). The 5 "missing" guides (dosa/antido, guardian/delynth, musa/foresthunter, ninja/elmermol, valkyrie/canadiancorgii) are player profile pages, not guides: nothing missing. Not imported on purpose: class-changelog, videos, reforge panel (Dosa), prio-list text (Deadeye), external link lists.
- Python tooling status: 190 tests OK; apply writes only SkillCoolTimeSlot + SkillCoolTimeMemo (v2 only). QuickSlotData writing does NOT exist yet (being added in the C# port).
- Backups: only `20261029T160822Z` (AhrinSong, new format) remains in `Black Desert\_backups`; the 4 older folders were deleted by the user's request (they held pre-change originals of the 34 characters edited on 2026-10-29).

## Running work when this file was written (check each, results arrive as agent hand-backs)
1. C# port of apply/backup/restore + QuickSlotData writer + parity tests (main `app\`).
2. Apply page redesign (worktree bdo-apply): class grid with icons, guide pills, combo viewer with step cards, detail/customization panel, profiles (`%LOCALAPPDATA%\BdoComboSetup\profiles`), drag reorder, add skill, screenshots.
3. Preview rebuild (worktree bdo-preview): game-screen canvas with the 30 slots at real positions, layout selector, hidden toggle, background screenshot `C:\Users\TitoS\OneDrive\Documentos\Black Desert\ScreenShot\2026-02-15_13735792.PNG` (3440x1440, no HUD), key legends, zoom/fit.
4. Data/tooltips (worktree bdo-tooltips): chain `Kind` (buff, combo, mobility, cancels, other) + `ManualPool` for mobility, full skill tooltips (cooldown, cost, damage, PvP, features) from `data\classhub\data\skills`, import of `class-cancels`, check of `spiritforging`.
Note: a previous Apply-page agent was cancelled by the user by mistake; its partial work is on `wip/apply-redesign`.

## Next steps
1. Collect the four agent reports; read every screenshot yourself.
2. Merge the worktrees into `main` (order above), run `mise run test` (root) and `cd app && mise run check`, publish the single-file exe once (was 110 MB; +13.5 MB over content size not explained) and launch it for the user (they asked to see the app; there is no hot reload in WinUI 3).
3. Wire the edited combo to Preview (IPreviewSlotSource) and to the real applier; dry-run the Tamer awakening combo on a scratch COPY of AhrinSong and show the diff; only apply after explicit approval.
4. Retire the Python apply tool after parity; keep downloader/importer in Python.
5. Later: real-time preview (Windows.Graphics.Capture of BlackDesert64.exe), Spanish translation of guide texts, remaining sections (changelog, videos...), clean-machine test of the exe, confirm the preset-to-block mapping by moving a panel in a preset in game.
