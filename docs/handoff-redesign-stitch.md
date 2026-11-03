# Handoff: full redesign of BDOTempo (Stitch prompts, then artifact)

Status (2026-11-03): nothing implemented in the app. The user did not like the ten earlier mockups in `docs/design/`
(made by another assistant). A draft HTML artifact exists but is NOT published.

## Goal

Redesign the whole app to use space better (three side-by-side panels instead of one long column), in two variants:

- **A · WinUI 3** (current framework): Windows 11 look, Mica, left NavigationView, Fluent cards, 8px radius.
- **B · .NET + WebView2** (HTML/CSS/JS front end): the visual language of `X:\Github\Personal\bdo_album\album`
  (Tauri + Svelte there; here it would be a .NET host with WebView2).

Screens: **Aplicar combo**, **Vista previa**, **Configuración**.

## Album visual language (source: `bdo_album/album/src/styles/_tokens.scss`, `_themes.scss`, `_fonts.scss`)

- Dark: base `#070a0e`, surface `#0f141a`, elevated `#141c25`, border `#1a232c`, text `#fff`.
- Light: base `#f6f5f1`, surface `#fff`, elevated `#f0eee8`, border `#e4e1da`, text `#16181d`.
- Accent amber `#b8720f` (light) / ~`#e8a33d` (dark). Fonts: Manrope (UI), Fraunces (display), IBM Plex Mono (counters).
- Pill module switcher in the title bar, mono count badges, 12px radius, floating blurred pills.

## Layout decided for "Aplicar combo"

Left 270px: character picker + searchable class list. Center: guide title, guide switcher, tabs Combos/Buffs, rows with
checkbox + monogram + name + keycaps (A → B → C) + memo + slot number. Right 340px: 20 cooldown slots (5x4), 20 quick
slots (10x2), sticky footer with confirm switch, "Respaldo listo" and the primary "Aplicar a <personaje>" button.
Preview: large 16:9 capture area, 20 + 20 slots over it, right sidebar with status/size/opacity/keys. Settings: card
grid, with a full-width Characters table and the "Escanear pantalla de selección" button.

## Files

- Draft artifact (unpublished): `C:\Users\TitoS\AppData\Local\Temp\claude\X--Github-Personal-bdo-combo-setup\77fd0628-ea33-4097-ab17-9f91df6794a1\scratchpad\redesign.html`
  (design A/B toggle, three screens, light/dark, interactive; example data only). Copy it into `docs/design/` if kept.
- Current screens: `app/src/BdoComboSetup/Views/{ApplyComboPage,PreviewPage,SettingsPage}.xaml`, shell in `MainWindow.xaml`.
- Old mockups (rejected): `docs/design/`.

## Next steps

1. The Stitch MCP (`stitch`, HTTP, key in `~/.claude.json`, local scope) is configured. After a Claude Code restart its tools
   should be available; list them and generate the screens with the prompts below (base prompt first).
2. Show the user the Stitch results; take what they like.
3. Build the final artifact (both variants) with the Artifact tool, using the draft as the base, then iterate.
4. Only after the user picks a direction: implement in the app (SDD / coder subagents per global rules).

## Stitch prompts

### Base (paste first)
Desktop app for Windows, "BDOTempo": a tool for the game Black Desert Online that writes combo cooldown slots and quick
slots into a character's files, and shows a live overlay preview of those slots. Window size 1280x760, dense, no wasted
space, no long scrolling pages: use three panels side by side. Visual language: dark navy-black base (#070a0e), surface
#0f141a, elevated #141c25, hairline borders #1f2a35, warm amber accent (#e8a33d), serif display titles (Fraunces), Manrope
for UI, IBM Plex Mono for counters and keycaps, pill-shaped module switcher in the title bar, small mono count badges, 12px
radius. Also provide a light variant (base #f6f5f1, surface #ffffff, accent #b8720f). UI text in Spanish. Use example data
only, no real game art: class monograms in colored circles.

### Screen 1: Aplicar combo
Screen "Aplicar combo". Left panel (270px): character picker (monogram + name + class), searchable class list with count
badge. Center panel: guide title in serif, author, rating, source; segmented control to switch between 3 guides; tabs
"Combos" / "Buffs"; list of rows, each with checkbox, monogram, combo name, keycaps joined by arrows (Shift → LMB → F), a
short memo, and the assigned slot number on the right. Right panel (340px): 20 cooldown slots in a 5x4 grid with numbers, a
hint line for the selected slot, 20 quick slots in 10x2 small grid, and a sticky footer with a "confirm before writing"
switch, a green "Respaldo listo" status and a primary amber button "Aplicar a Zeoclot".

### Screen 2: Vista previa
Screen "Vista previa". Large 16:9 game-capture area (dark blue game scene placeholder) filling most of the window, with a
floating pill top-left "Capturando BlackDesert64.exe · 60 fps" and a green dot. Over the bottom of the scene: a row of 20
cooldown slots (two groups of 10) at left and a 5x2 quick-slot block at right, semi-transparent. Right sidebar (280px) with
cards: Estado (detected, resolution, pause button), Ajustes (size slider, opacity slider, show keys switch), Personaje.

### Screen 3: Configuración
Screen "Configuración". Two-column card grid: Cuenta (account select, data folder), Juego (game folder, language segmented
Español/English), Personajes (full-width card with a table: monogram + name, class, level, reading status
"Confirmada"/"Revisar", and a primary button "Escanear pantalla de selección"), Actualizaciones (version, "Al día", check
button), Seguridad al escribir (confirmation switch with explanation).

### Native variant (design A)
Same three screens but as a native Windows 11 WinUI 3 app: Mica title bar, left NavigationView (Aplicar combo, Vista previa,
Configuración at the bottom), Fluent cards with 8px radius, Segoe UI Variable, ToggleSwitch and ComboBox controls, same
amber accent. Content area keeps the three-panel layout.

## Rules to respect (AGENTS.md / global)

- Never touch the game; no code/diffs/logs in chat; report in short milestones.
- English in code/docs; UI strings in `.resw` (es neutral "tú", plus en-US). Desktop apps: WinUI 3, single-file self-contained.
- Do not mix the unrelated working-tree changes (mockup HTMLs, `anotaciones.md`, OCR work) into the redesign commits.
- The user's API key for Stitch was pasted in chat once; do not echo it.
