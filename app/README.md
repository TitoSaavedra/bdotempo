# BDO Combo Setup (app)

WinUI 3 app (Windows App SDK, .NET 10). Tasks: `mise run build`, `mise run test`, `mise run publish`, `mise run screenshots`.

## Preview page: game screen

Only the 20 cooldown slots and the 20 quick slots are drawn, each at its saved position, in game pixels
(`GameOption.txt` width x height, default 3440 x 1440). Nothing else of the game is drawn: no bars, no panels, no labels
beyond the key legend and the remaining time.

- **Framing**: the preview opens on the frame of the drawn slots (the slots plus a 12 px margin, `PreviewViewModel.Frame`),
  scaled to fill the viewport, so the slots are as large as the window allows. "Fit to slots" returns to that frame; zooming
  out with the wheel reaches the whole game screen (zoom 0.1 of the frame at the lowest).
- **Background**: the live capture of the game window (process `BlackDesert64`, Windows.Graphics.Capture, capture only).
  It is black when the game is not running, when the window is minimized, or when the capture cannot start. A minimized
  window is never shown frozen. Polling looks at the window once a second while the page is visible.
- **Layout selector**: General, Preset 1, Preset 2, Preset 3, read from `UserCache\<account>\gamevariable.xml`.
  - General: the top-level `UIData` rows, each with an anchor (`PendingType`) and offsets. Converted to game pixels by `UiPanelGeometry`.
  - Presets: `UISettingPreset0..2` rows, already `LeftTop` absolute.
  - **Assumption**: Preset N is `UISettingPreset(N-1)`. The only place that decides it is `GameUiLayoutBlocks` (Core).
- **Quick slot** (50 x 65): the game's semi-transparent background (44 x 44 at (3, 21), real art), the 42 x 42 icon at (4, 22),
  and the key legend centred on top (text top at y 10): `#626262` when empty, `#40D7FD` for a skill. No frame. The hover
  handle strip is not drawn (the game shows it only on hover).
- **Cooldown slot** (50 x 50): no background, the 42 x 42 icon at (4, 7). While cooling down, the game's radial sweep at
  (3, 6): the 100-frame mask (`slot-cooldown-sweep.png`, frame = round(remaining x 99), clockwise from 12 o'clock, dark part
  = remaining time). Without the art a procedural pie with the same meaning is drawn. The remaining time is centred:
  `Nh` over an hour, `Nm` over a minute, else `Ns` (`CooldownTimeText`, unit letters in the resources).
- **Hidden slots** (toggle, off by default): panels the game hides (`IsShow=false`) are drawn dimmed with a dashed outline.
- **Skills**: from the combo being edited on the Apply page (`ComboPreviewSlotSource`). Empty combo: every slot is empty.
- **Quick keys**: the key bound by the character selected on the Apply page (`ShortcutKey QuickSlotN`), else the default.
- **Icons**: `skills.json` / `classes.json` of the Scrappers export (`SkillIconRoot`), then the bdoclasshub folder
  (`ClassHubRoot`), then a two-letter glyph. Icons are matched by skill number, never by file number.
- **Demo** (`--demo`): synthetic layouts and display, the black background (no capture), no game file read or written.

Not drawn yet, because the data does not exist in the app: the grey state of a skill that cannot be used (the game
computes it live), the item count box (combos hold skills only), the skill toggle glow, and the ready flash.

### Fonts

The legend and the remaining time use **Segoe UI Semibold**, a Windows system font: it is not bundled, so nothing is
redistributed. The game's own fonts (`ui_data/font/*.ttf`) were not mapped to its styles (`BaseFont_8_Bold` is unresolved),
so their exact look is approximated. Sizes (14 px legend, 16 px time) are estimates, not measured in the game.

### Slot art (asset bundle)

`mise run build-assets` copies two files of the client export's `ui_probe\slot_art` into the bundle, checked for their exact
size: `quickslot_bg.png` (44 x 44) and `cooldown_candidate_radial_mask.png` (520 x 520). They are game art: git-ignored,
never committed. The app reads them through `IAssetProvider.OpenSlotArt(SlotArt)`, so a future asset pack can supply them
without changing the views.

## Searchable selectors

Any selector with more than about six items is searchable. The rule is one: `SearchText` (Core) matches case- and
accent-insensitively, "contains", on the fields of each item (`SearchFields`):

| Selector | Searched by |
|---|---|
| Character picker (Apply page) | character name, class name in Spanish and in English |
| Character editor: class list | class name in Spanish and in English |
| Class grid quick filter (Apply page) | class name in Spanish and in English |
| "Add skill" list | skill name in English and in Spanish, skill number |
| Account (Settings) | account number |

Controls: `SearchablePicker` (a button and a flyout with the list) and `SearchableList` (the search box and the list). Keys:
type to filter, Down moves to the list, Enter picks the first match (in the box) or the focused item, Esc closes the flyout.
A pcid is never searched and never shown.

Short lists stay plain: the layout selector (4 items) and the language selector (2 items).

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
  `BundledAssetProvider` answers from the embedded bundle, then from `IconCatalog` (Scrappers, bdoclasshub), then null.
  Null means: draw the glyph.
- `IEmbeddedResources` / `AssemblyEmbeddedResources`: the bundle names are in `BundleResourceNames`.

`AppServices` builds `Combos` and `Assets`. The Apply and Preview pages still use `MockComboDataSource` (characters and
the 20 cooldown slots) and the `IconCatalog` path contract; they do not read `Combos` or `Assets` yet.

Tests: `mise run test-real-data` is a Python test of the importer on the real data; the C# tests use synthetic bundle
fixtures (`Fixtures\bundle\`, embedded in the test assembly).

## Settings

The **Settings** page (footer of the navigation) changes `%LOCALAPPDATA%\BdoComboSetup\settings.json`, created on the first change:

- **Account** (`GameAccount`): the numeric folders under `UserCache` that have an `11` folder. `-1` and non-numeric folders
  are ignored. The chosen account is remembered; with only one account it is used without asking.
- **Black Desert folder** (`GameDataRoot`): browse or type it, then save. A folder without `UserCache` is refused.
- **Language** (`Language`): `es` or `en-US`. The app restarts to apply it (the button starts a new copy of the app).
- **Reminder before writing** (`SkipWriteHint`): "Don't show again" turns the reminder off; the toggle turns it on again.

Other keys: `SkillIconRoot`, `ClassHubRoot`, `CooldownRows`/`CooldownColumns` (2 x 10), `SelectedLayout`, `BackupsRoot`,
`SkillsJsonPath`. The Preview page reads its layouts and quick keys of the active account at startup, so a change of
account or folder shows there after a restart.

## Characters (Apply page)

The character row lists only the live characters of the active account: the pcids of `CharacterOrderList` in
`UserCache\<account>\gamevariable.xml` (in game order) that also have a 17-digit folder with a `gameVariable.xml` under `11`.
Sidecar files (`gamevariable-*.xml`) and stale folders are ignored. Only the name and the class icon are shown, never the pcid.

- **Name**: the name you set, then the in-game label (`CharacterMemo` in `gameVariable_lobby.xml`), then `<Class> N`.
- **Class**: the class you set, then a vote over the skill numbers of the cooldown bar, the cooldown memos and the quick bar,
  against `Assets\generated\skill_classes.json`, then unknown (a neutral icon). The table holds only skill groups with at most
  nine classes; `mise run build-assets` builds it from `D:\extracted_bdo\class_skills.json`.
- **Pencil**: sets the name and the class once, and they are remembered per account in
  `%LOCALAPPDATA%\BdoComboSetup\characters.json`. An empty name and "Class decided by the game files" hand the choice back.

The class grid is never filtered by the account. Game files are only read; a file that the game is writing is retried
up to three times.

## Writing to a character

The review comes first. "Write to character" then asks to be on the character selection screen (the reminder can be
turned off). Cancel writes nothing. An open game does not block the write: the state is logged (`write.game_decision`)
and a verified backup is taken first, as always.

## Command line

`--page apply|preview|settings`, `--layout general|preset1|preset2|preset3`, `--hidden-slots`, `--cooldown-demo` (debug), `--zoom <n>`, `--class <slug> --spec <id>` (the Apply page starts
on that guide, for example `--class tamer --spec awakening`), `--demo` (synthetic characters and a demo applier that reads and
writes nothing), `--demo-review` (the same, and opens the review of the combo at startup), `--demo-write-hint` (the same, and
asks to write, so the reminder before a write is shown). `--demo-characters <n>` (the same, with n extra synthetic characters, up to 200) and `--demo-character-picker` (the same,
and opens the character picker on the Apply page). `mise run screenshots` uses `--demo`.
Options given on the command line are not saved.

## Logs

JSON lines in `%LOCALAPPDATA%\BdoComboSetup\logs` with W3C trace ids.
