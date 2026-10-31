# BDO Combo Setup (app)

WinUI 3 app (Windows App SDK, .NET 10). Tasks: `mise run build`, `mise run test`, `mise run publish`, `mise run screenshots`.

## Preview page: game screen

The left side draws the game screen in game pixels (`GameOption.txt` width x height, default 3440 x 1440) inside a `Viewbox`,
so positions are exact at any window size.

- **Background**: the default screenshot `Documents\Black Desert\ScreenShot\2026-02-15_13735792.PNG` is read in place (never copied).
  "Change background" stores your choice in the settings. Without any image a dark gradient is shown.
- **Layout selector**: General, Preset 1, Preset 2, Preset 3, read from `UserCache\<account>\gamevariable.xml`.
  - General: the top-level `UIData` rows, each with an anchor (`PendingType`) and offsets. Converted to game pixels by `UiPanelGeometry`.
  - Presets: `UISettingPreset0..2` rows, already `LeftTop` absolute.
  - **Assumption**: Preset N is `UISettingPreset(N-1)`. The only place that decides it is `GameUiLayoutBlocks` (Core). It is also in the UI tooltip.
- **Panel overlay** ("Show UI panels"): outlines every visible `UIData` panel with its index (chat windows show `index.window`).
  Candidate quick-bar panels are gold. Use it with the game to find which index is which bar.
- **Bars**:
  - Quick bar: candidates `100, 91-97` (`GameLayoutIndices.DefaultQuickSlotCandidates`) give the 9-slot row. This is an estimate.
  - Cooldown bar: no index yet (`GameLayoutIndices.CooldownBarIndex = null`), so it sits in a generic place above the quick bar.
  - A bar without a verified index can be dragged. The position is stored per layout as a fraction of the screen
    (`ManualBarPositions` in the settings), and "Reset bar positions" removes it.
- **Icons**: `skills.json` / `classes.json` of the Scrappers export (`SkillIconRoot`), then the bdoclasshub folder
  (`ClassHubRoot`), then a two-letter glyph. Icons are matched by skill number, never by file number.
- **Live**: disabled ("coming next").

## Settings

`%LOCALAPPDATA%\BdoComboSetup\settings.json` (created on the first change). Keys: `BackgroundImagePath`, `SkillIconRoot`,
`ClassHubRoot`, `GameAccountFolder` (default `330`), `CooldownRows`/`CooldownColumns` (2 x 10), `SelectedLayout`, `ManualBarPositions`.

## Command line

`--page apply|preview`, `--layout general|preset1|preset2|preset3`, `--panels`. Used by `mise run screenshots`.
Options given on the command line are not saved.

## Logs

JSON lines in `%LOCALAPPDATA%\BdoComboSetup\logs` with W3C trace ids.
