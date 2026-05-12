# Supreme Commander Map Editor

Cross-platform map editor for **Supreme Commander 1** and **Supreme Commander: Forged Alliance**.
Built with Avalonia + OpenGL on .NET 9.

## Features

- Read & write `.scmap` v53 / v56 / v60 (binary-identical round-trip)
- Read & write `_scenario.lua` / `_save.lua` / `_script.lua`
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

On first launch the editor tries to auto-detect a Steam install (parsing `libraryfolders.vdf`).
If detection fails, use **Settings → Set game folder…** to point it manually.

Without a game install the editor still runs but terrain will render with a height-color
fallback and many texture / icon features will be unavailable.

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

MIT — see [LICENSE](LICENSE). The editor is a fan project; Supreme Commander and Supreme
Commander: Forged Alliance are trademarks of their respective owners.
