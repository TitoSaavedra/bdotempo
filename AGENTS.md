# AGENTS.md — BDOTempo

Instructions for any coding agent working in this repository. Follow them; they come from the
user and are not negotiable.

## What this is

Black Desert Online desktop tool, two modules plus settings:

1. **Apply combo** — pick a class, pick a guide (bdoclasshub data), review the buffs/combos with
   real game icons, customize names/memos/slots/quick slots, then write the 20 cooldown slots,
   memos and quick slots of a character.
2. **Preview** — draws only the 20 cooldown slots and 20 quick slots over the **live** game window
   (Windows.Graphics.Capture of `BlackDesert64.exe`), at the real saved positions and layouts,
   black when the game is closed/minimized/capture fails.
3. **Settings** — account, game folder, language es/en, characters (name/class), updates,
   write-confirmation switch.

Stack: WinUI 3 (C#, .NET 10, framework-dependent) in `app/`; Python 3.12 (guides importer, asset
packer, reference apply CLI) in `src/bdo_combo`. Everything runs through **mise**.

## Start here

`docs/PROJECT_STATE.md` is the handoff document: goal, decisions, architecture, known problems,
deferred TODOs. Read it and `git status` / `git log -10 --oneline` before planning anything.

## Hard rules (user-mandated)

- **No code in the chat (user, 2026-11-01).** Never print diffs, file contents, build logs or test output. Run the
  command and report only the outcome: one line ("compila", "falla por X"), with the single relevant error if any.
  Use filters (`Select-Object -Last N`, `Select-String`) so the terminal output stays short.
- **Status by milestones (user, 2026-11-01).** Report progress every few items in one or two lines, without being
  asked; the user does not want to ask "how is it going". Do not narrate every file.
- **Backups first.** Never write a BDO file without a verified backup
  (`<game data>\_backups\<UTC stamp>Z\<account>\<pcid>\` + SHA-256 manifest); abort if it fails.
- **Do not apply to real character files** without an explicit go-ahead in the current session.
- **Tests:** write and run tests ONLY for (1) writing game files (backup + write/read-back) and
  (2) settings/options. Never write Preview tests (the user verifies Preview by eye). Do not touch,
  fix or prune the existing ~400 C# / ~300 Python tests unless asked.
- **Never touch the game.** `BlackDesert64.exe` may be running: do not close, restart or inject
  into it. The game client at `G:\SteamLibrary\steamapps\common\Black Desert Online` and the game
  data in `C:\Users\TitoS\OneDrive\Documentos\Black Desert` are read-only; writes go only through
  the app's own backup-and-write path.
- **No window/app loops.** Do not launch the app in a loop, do not run screenshot loops, never run
  `mise run screenshots` unless the user asks. One `dotnet` process at a time. Never
  `mise run check` (it runs build and tests in parallel).
- **mise for everything.** No `[env]` blocks with exec templates in `mise.toml`.
- **UI conventions:** every selector/list with more than ~6 items is a searchable dropdown
  (type-to-filter, accent-insensitive, matching Spanish **and** English names); never show pcids
  in the UI (names + class icon only).
- **Language:** English in code, comments, docs and commits. UI strings live in `.resw` resources
  (`es` with neutral "tú", no voseo; plus `en-US`).
- **Logging:** structured JSON logs with `trace_id` / `span_id`.
- **Never commit game art.** `dist/`, screenshots and loose images are git-ignored; the only game
  art in git is the encrypted asset pack parts (`app/assets/parts/assets.part*.bdoa`).
- **No `git push`.** The repo stays private and unpushed until the user says otherwise; never
  rewrite `origin/main` history (no force-push).

## Commands

Root (`X:\Github\Personal\bdo-combo-setup`):

| Command | What it does |
| --- | --- |
| `mise run test` | Python unit tests (synthetic fixtures) |
| `mise run import-guides` | Saved class pages + client export -> `data/combos/` |
| `mise run build-assets` | Build the asset parts in `app/assets/parts/` |
| `mise run verify-assets` | Check the committed asset parts |
| `mise run list-characters` | Read-only character roster |
| `mise run dry-run -- --pcid <pcid>` | Diff a combo write, writes nothing |
| `mise run apply -- --pcid <pcid>` | Apply (dry run unless `--write`) |

App (`app/`):

| Command | What it does |
| --- | --- |
| `mise run build` | Build the WinUI app + core (Debug, x64) |
| `mise run run` | Build and launch the app (only when asked) |
| `mise run test` | Run the existing xUnit tests (do not extend unless allowed) |
| `mise run publish` | Release, framework-dependent `win-x64` |
| `mise run package` | Installer + portable zip into `dist/` (needs Inno Setup 6) |

Useful paths: app logs `%LOCALAPPDATA%\BDOTempo\logs`, app settings
`%LOCALAPPDATA%\BDOTempo\settings.json`, runnable build `dist\BDOTempo-run\BDOTempo.exe`, client
export `D:\extracted_bdo` (`class_skills.json`), asset pack format
`app/docs/asset-pack-format.md`, slot file format `app/docs/quickslotdata-format.md`.

## Layout

- `app/src/BdoComboSetup.Core/` — no-UI logic: `Game`, `Combo`, `Characters`, `Backup`, `Files`,
  `Settings`, `Updates`, `ViewModels`, `Logging`, `Search`, `Operations`, `Assets`.
- `app/src/BdoComboSetup/` — WinUI app: `Views`, `Capture` (live game capture), `Services`,
  `Cli` (`--assemble-assets`), `Strings` (.resw).
- `src/bdo_combo/` — Python: guide importer, asset packer, reference apply/backup CLI.
- `combos/` — legacy combo JSON files; `data/`, `reports/`, `dist/`, `backups/` are generated.

## Commits

Conventional Commits in English: `feat(app): ...`, `fix(core): ...`, `docs: ...`, `build(app): ...`.
Old history carries a `Co-Authored-By: Claude ...` trailer; do not add AI trailers to new commits.
