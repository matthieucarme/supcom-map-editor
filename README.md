# Supreme Commander Map Editor

Cross-platform map editor for **Supreme Commander 1** (vanilla 2007 / SupCom 1).
Built with Avalonia + OpenGL on .NET 9.

Forged Alliance is **not** a target — saved maps stay in the vanilla v53 format (6 strata slots,
single-byte high splatmap, no FA-specific scenario fields). Maps load fine in the original SupCom 1
without crashes. If you want to use FA-saved maps as a starting point you can open them in the
editor, but the saved output will be vanilla-formatted.

It is 100% vibe coded...

## Install

Grab the latest build from the [**Releases page**](https://github.com/matthieucarme/supcom-map-editor/releases/latest) — no installer, no dependencies, just unzip / `chmod` and run.

- **Windows** : [`SupremeCommanderMapEditor.zip`](https://github.com/matthieucarme/supcom-map-editor/releases/latest/download/SupremeCommanderMapEditor.zip) — unzip, double-click `SupremeCommanderMapEditor.exe`.
- **Linux** : [`SupremeCommanderMapEditor-x86_64.AppImage`](https://github.com/matthieucarme/supcom-map-editor/releases/latest/download/SupremeCommanderMapEditor-x86_64.AppImage) — `chmod +x SupremeCommanderMapEditor-x86_64.AppImage` then double-click. Works on any distro, no install needed.

Or build from source — see [Build & Run](#build--run).

## Features

- Read & write `.scmap` (vanilla v53 target — also reads v56/v60 from FA maps)
- Read & write `_scenario.lua` / `_save.lua` / `_script.lua` in SC1's exact format (marker
  `prop` and `hint` fields, customprops, ExtraArmies, Orders/Platoons sections — all the
  things SC1's engine needs or it crashes on load)
- Real-time 3D viewport (OpenGL, terrain mesh + water + lighting + markers)
- Top-down 2D viewport with grid, snap, box-select, prop / unit menus
- Heightmap brush (raise, lower, smooth, flatten, set-height)
- Texture painting with smart strata classification and splatmap channels
- Markers (mass, hydro, spawns, AI pathfinding, defense, etc.) — full CRUD + drag
- Props brush (rocks, trees, wreckage…) — multi-select, batch delete, presets
- Civilian / pre-placed units (`InitialUnits`) — drag-edit
- Symmetry tools (6 patterns: mirror ×1 and 4-fold)
- Map scaling (256→4096, bilinear resample of heightmap, splatmaps, markers, props)
- Procedural map generator with deterministic seed, biome selection, team configuration
- Undo / Redo on every op
- Settings persisted in `%APPDATA%` (Windows) / `~/.config` (Linux)

## Build & Run

```bash
dotnet build SupremeCommanderEditor.sln
dotnet run --project src/SupremeCommanderEditor.App
dotnet test
```

### Windows self-contained publish

```bash
dotnet publish src/SupremeCommanderEditor.App -p:PublishProfile=win-x64
```

Outputs a single ~46 MB `SupremeCommanderMapEditor.exe` with everything embedded.
Wrapped in a `.zip` automatically to avoid SmartScreen on first launch.

## Game data

The editor needs to read texture archives (`.scd` files) from your Supreme Commander install:
`<Steam library>/Supreme Commander/gamedata/`

On launch the editor auto-detects a Steam install (parses `libraryfolders.vdf`), preferring the
vanilla `Supreme Commander` folder over `Supreme Commander Forged Alliance` if both are installed
since vanilla is the target.

Without a game install the editor still runs but terrain will render with a height-color
fallback and many texture / icon features will be unavailable.

Maps are read from / saved to `<game>/maps/<map name>/`. The Open dialog defaults to that folder
and Save derives the folder name from the in-game map title (rename in Map Info tab to move it).

## Repository layout

```
src/
  SupremeCommanderEditor.Core/        # No UI dependency — formats, models, operations, services
  SupremeCommanderEditor.Rendering/   # Core + Avalonia GL — shaders, renderers, camera, prop icons
  SupremeCommanderEditor.App/         # Avalonia UI — windows, controls, view-models
tests/
  SupremeCommanderEditor.Core.Tests/  # xUnit — round-trip, Lua parsing, generator, etc.
tools/
  IconGenerator/                      # Console: rebuilds embedded prop / unit icon PNGs
```

`CLAUDE.md` is a long technical document tracking design decisions and gotchas.
Useful if you want to understand the architecture or contribute.

## Regenerating prop / unit icons

The 248 prop + 307 unit icon PNGs under `src/SupremeCommanderEditor.Rendering/PropIcons` and
`UnitIcons` are pre-rendered from the vanilla SC `.scd` archives. To rebuild them:

```bash
cd tools/IconGenerator
dotnet run -- "<SteamLibrary>/Supreme Commander/gamedata/env.scd" \
              "<SteamLibrary>/Supreme Commander/gamedata/units.scd" \
              ../../src/SupremeCommanderEditor.Rendering
```

## Tech stack

- .NET 9 / C# 12
- Avalonia UI 12 (Fluent dark theme)
- OpenGL via `OpenGlControlBase` + `GlInterface` (ANGLE on Windows, native on Linux)
- SkiaSharp for the 2D viewport
- MoonSharp for Lua parsing
- Pfim for DDS decode
- CommunityToolkit.Mvvm for MVVM

## License

MIT — see [LICENSE](LICENSE). The editor is a fan project; Supreme Commander is a trademark of
its respective owner.
