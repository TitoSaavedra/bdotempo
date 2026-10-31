# BDO Combo Setup (app)

WinUI 3 app (Windows App SDK, .NET 10). Tasks: `mise run build`, `mise run test`, `mise run publish`, `mise run screenshots`.

## Preview page: game screen

Only the 20 cooldown slots and the 20 quick slots are drawn, each at its saved position, in game pixels
(`GameOption.txt` width x height, default 3440 x 1440), scaled as a whole by fit and zoom. Nothing else of the game
is drawn: no bars, no panels, no labels beyond the key legend.

- **Background**: the live capture of the game window (process `BlackDesert64`, Windows.Graphics.Capture, capture only).
  It is black when the game is not running, when the window is minimized, or when the capture cannot start. A minimized
  window is never shown frozen. Polling looks at the window once a second while the page is visible.
- **Layout selector**: General, Preset 1, Preset 2, Preset 3, read from `UserCache\<account>\gamevariable.xml`.
  - General: the top-level `UIData` rows, each with an anchor (`PendingType`) and offsets. Converted to game pixels by `UiPanelGeometry`.
  - Presets: `UISettingPreset0..2` rows, already `LeftTop` absolute.
  - **Assumption**: Preset N is `UISettingPreset(N-1)`. The only place that decides it is `GameUiLayoutBlocks` (Core).
- **Hidden slots** (toggle, off by default): panels the game hides (`IsShow=false`) are drawn dimmed with a dashed outline.
- **Skills**: from the combo being edited on the Apply page (`ComboPreviewSlotSource`). Empty combo: every slot is empty.
- **Quick keys**: the key bound by the character selected on the Apply page (`ShortcutKey QuickSlotN`), else the default.
- **Icons**: read from `assets.bdoa` next to the exe (the asset pack, see `docs/asset-pack-format.md`). A missing icon
  is drawn as a two-letter glyph. Icons are matched by skill number, never by file number.
- **Demo** (`--demo`): synthetic layouts and display, the black background (no capture), no game file read or written.

Core types: `IGameScreenSource` (`GameScreenSource` with `IGameWindowLocator` and `IGameFrameCapture`; the app implements
the locator and the capture; `BlackGameScreenSource` for the demo). The state machine is covered by `GameScreenSourceTests`
with fakes.

## Combo data and assets (contract)

Core types, in `BdoComboSetup.Core`:

- `ICombosCatalog`: `IReadOnlyList<ClassInfo> Classes` (classType order, only classes with a guide) and
  `Task<ComboGuide?> LoadGuideAsync(int classType, string specId)`. `JsonCombosCatalog` implements it over the embedded
  bundle, caches each guide, and logs (never throws) on a missing or invalid file.
- `ClassInfo(ClassType, Slug, Name, Specs)`, `SpecInfo(Id, DisplayName, GuideKey)`. Names are `BilingualText(En, Es)`;
  `Name.For(culture)` picks Spanish for `es` cultures and falls back to English.
- `ComboGuide(Title, Description, Source, Chains, Notes, Addons)`; `ComboChain(Section, Title, Notes, Steps)`;
  `ComboStep(Keys, Skills, Extra, Note)`; `ComboNote(Section, Text, Skills)`; `ComboAddon(Section, Skill, Description, Effects)`.
- `ComboSkill(SkillNo, Name, DescriptionEs, OpenIcon)`: `OpenIcon` is a `Func<Stream?>` for the icon (caller disposes).
- `IAssetProvider`: `Stream? OpenSkillIcon(int skillNo, string? className)` and `Stream? OpenClassIcon(int classType)`.
  `BundledAssetProvider` answers from the asset pack (`AssetPackResources` over `IAssetPack`), then null.
  Null means: draw the glyph.
- `IEmbeddedResources`: the entry names are in `BundleResourceNames`. `AssetPackResources` gives it the pack's entries.
- `AssetPack` / `AssetPackAssembler` / `AssetPackLoader`: read `assets.bdoa`, join the parts, open the file next to the exe.

`AppServices` builds `Combos` and `Assets` from the pack and keeps `AssetPackLoad` (the error page is shown when it
failed). The Apply and Preview pages still use `MockComboDataSource` (characters and the 20 cooldown slots).

Tests: `mise run test-real-data` is a Python test of the importer on the real data; the C# tests use synthetic bundle
fixtures (`Fixtures\bundle\`, embedded in the test assembly) and the synthetic asset pack fixtures
(`Fixtures\assetpack\`, written by `tests/assetpack_support.py`).

## Settings

`%LOCALAPPDATA%\BdoComboSetup\settings.json` (created on the first change). Keys: `SkillIconRoot`,
`ClassHubRoot`, `GameAccountFolder` (default `330`), `CooldownRows`/`CooldownColumns` (2 x 10), `SelectedLayout`.

## Command line

`--page apply|preview`, `--layout general|preset1|preset2|preset3`, `--hidden-slots`, `--cooldown-demo` (debug), `--zoom <n>`, `--class <slug> --spec <id>` (the Apply page starts
on that guide, for example `--class tamer --spec awakening`), `--demo` (synthetic characters and a demo applier that reads and
writes nothing), `--demo-review` (the same, and opens the review of the combo at startup). `mise run screenshots` uses `--demo`.
Options given on the command line are not saved.

## Logs

JSON lines in `%LOCALAPPDATA%\BdoComboSetup\logs` with W3C trace ids.
