# Supreme Commander Map Editor

Cross-platform map editor (Windows + Linux) for **Supreme Commander 1** (vanilla 2007).
Forged Alliance is NOT a target — every save is emitted in the strict vanilla v53 format so the
original SC1 engine loads it without crashing. The editor still reads FA maps (v56/v60), it just
writes them back as v53. See `## SC1 vanilla compatibility` below for the long list of edge cases.

## Tech Stack

- **.NET 9.0** / C#
- **Avalonia UI 12.0** (Fluent dark theme)
- **OpenGL** via Avalonia OpenGlControlBase + GlInterface
- **SkiaSharp** for the 2D view
- **MoonSharp** for Lua parsing
- **Pfim** for DDS decode
- **CommunityToolkit.Mvvm** for the MVVM pattern

## Project structure

```
SupremeCommanderEditor.sln
src/
  SupremeCommanderEditor.Core/          # Zero UI dependency
    Formats/Scmap/                      # ScmapReader, ScmapWriter, DdsHelper, BinaryStreamExtensions
    Formats/Lua/                        # LuaRuntime, ScenarioLuaReader/Writer, SaveLuaReader/Writer, ScriptLuaWriter
    Models/                             # ScMap, Heightmap, TerrainTexture, TextureMask, LightingSettings,
                                        # WaterSettings, WaveGenerator, Decal, DecalGroup, Prop, Marker,
                                        # Army, SkyBox, Planet, CirrusLayer, CartographicSettings, MapInfo, etc.
    Models/UnitSpawn.cs                 # Pre-placed unit (civilians, defenses, turrets…)
    Operations/                         # IMapOperation, HeightmapBrushOp, MarkerOps (Add/Remove/Move),
                                        # SymmetryApplyOp, MapScaleOp, ClampToGroundOp,
                                        # PropBatchOps (BatchAdd/BatchRemove), PropOps (Add/Remove)
    Services/                           # UndoRedoService, NewMapService, GameDataService,
                                        # AppSettingsService (JSON in %APPDATA%), DebugLog (file log),
                                        # SymmetryService (math + apply heightmap/splatmaps/markers),
                                        # MapScaleService (resampling heightmap/splatmaps/markers),
                                        # GroundClampService (snap props/markers/units to ground),
                                        # BuildGridSnap (Mass/Hydro snap to build grid),
                                        # ArmyReconciler (sync Armies.list ↔ ARMY_N markers),
                                        # PropBrushPresets (blueprint catalogues for the brush)
  SupremeCommanderEditor.Rendering/     # Depends on Core + Avalonia
    Shaders/                            # terrain/water/marker .vert/.frag — no #version, prefixed at compile time
                                        #   (embedded as resources, see Distribution)
    GlHelpers.cs                        # ShaderProgram, TerrainVertex, GlExtensions, GlFbo (FBO/RBO + ReadPixels)
    GlTextureCache.cs                   # GL texture cache, upload compressed/decompressed DDS, splatmaps
    TerrainRenderer.cs                  # Terrain mesh from heightmap, multi-strata textures + splatmaps
    WaterRenderer.cs                    # Semi-transparent water plane
    MarkerRenderer.cs                   # Point sprites for markers (3D view)
    Camera.cs                           # Orbit camera + orthographic top-down mode (for 2D capture)
  SupremeCommanderEditor.App/           # Depends on Core + Rendering
    Views/MainWindow.axaml(.cs)         # Main window, menus, property panels, 3 tabs (3D/2D/Symmetry)
    Views/InfoDialog.axaml(.cs)         # Diagnostics dialog + "Set game folder" result
    ViewModels/MainWindowViewModel.cs   # MVVM, brush, undo/redo, grid/snap, copy/paste, prop brush
    Controls/OpenGlMapControl.cs        # Wrapper Panel + GlTerrainControl, raycast, brush, snapshot FBO, compass
    Controls/SkiaMapControl.cs          # 2D view: 3D snapshot + markers/props/grid/snap/box-select/brush
    Controls/SymmetryThumbnail.cs       # Clickable thumbnail showing a symmetry pattern
    Services/MarkerIconService.cs       # Attempts to load game icons (vector fallback)
    App.axaml(.cs), Program.cs          # Avalonia entry point (resets DebugLog at startup)
tests/
  SupremeCommanderEditor.Core.Tests/
    TestData/                           # .scmap and .lua test files copied from Steam
    Formats/ScmapRoundTripTests.cs      # Binary-identical round-trip (v53)
    Formats/LuaReaderTests.cs           # Scenario + save Lua parsing
    Formats/NewMapTests.cs              # Map creation from scratch
```

## .scmap format

Targets **v53** (vanilla SupCom 1). Also reads **v54** (rare), **v56**, **v60** (FA) but always
writes v53.

### v53 vs v56+ differences
- No null byte after the heightmap (v56+ has one)
- Env cubemaps: simple string (not count + pairs). v54 follows v53 here too.
- Textures section: tileset name + variable count + layers (albedo_path + normal_path + albedo_scale + normal_scale). v56+ has a fixed 10 layers and a cartographic section.
- Texture masks: count=1 prefix before each DDS blob — **but not always** (see vanilla quirks)
- No water map DDS (just the auxiliary data)
- No skybox (v60 only)

### Vanilla edge cases (per-file quirks we discovered the hard way)

Real vanilla `.scmap` files don't match a single canonical layout. Concrete differences that
break naive readers/writers:

| Map(s) | Quirk | Fix |
|---|---|---|
| SCMP_018, _029 (v56) | 0x00 padding byte right after the heightmap | Reader skips it for VersionMinor > 53; writer emits it. |
| SCMP_021 (v54) | Cubemap section uses **v53 format** (single string), not v56's count+pairs | Branch on `VersionMinor < 56` for cubemap parsing. |
| SCMP_001, _009 (v53) | Texture masks prefixed with `count=1` | Read sniff: if first int is 1 → count-prefix mode. |
| SCMP_030, _040 (v53) | Texture masks NOT prefixed (just `length + DDS`) | Same sniff: int != 1 → rewind, plain DDS blob. Flag stored on ScMap so writer round-trips identically. |
| SCMP_018 (v56) | Water-map DDS prefixed with `count=1` | Sniff at the water-map section too. |
| SCMP_029 (v56) | Water-map DDS NOT prefixed (just `length + DDS`) | Sniff: int=0 means no water-map (skip), int=1 means count prefix, else rewind. Flag stored. |
| SCMP_029, _030 (4096-sized) | NormalMap has 4 DDS blobs (count=4) instead of 1 | Reader loops, writer honours stored extras list. |
| SCMP_029, _030 (4096-sized) | Undocumented ~25-37 MB blob between watermaps and props | Reader scans forward for the props count (int < 1M followed by '/' starting a BlueprintPath), captures the bytes in between as opaque `PostWatermapsExtra` and writes them back verbatim. |
| SCMP_039 | `SCMP_039.scmap` but companion lua files in lowercase (`scmp_039_save.lua`) on disk | Companion lookup walks the directory case-insensitively. |
| SCMP_021 (v54) | Slot 0 albedo = bare `snow_albedo.dds` (no path) | `MapStrataNormalizer.NormalizeRelativeTexturePaths` infers `/env/<tileset>/layers/<tileset>_` prefix from the other slots' paths. |

71/71 vanilla maps in `<Steam>/Supreme Commander/maps/` now read with binary-identical round-trip.

## SC1 vanilla compatibility — what the engine actually demands

Producing a `.scmap` SC1 will load is more about the **companion Lua** files than the binary. SC1's
engine crashes on missing fields it expects.

### `_scenario.lua` — required structure

| Field | Notes |
|---|---|
| `name`, `description`, `type`, `starts`, `preview`, `size`, `norushradius` | Standard. We round-trip them. |
| `norushoffsetX_<ARMY>_N`, `norushoffsetY_<ARMY>_N` | One pair per army. |
| `map`, `save`, `script` | Absolute `/maps/<folder>/<base>.<ext>` paths. We always regenerate from the current folder. |
| `Configurations.standard.teams[].armies` | Standard FFA team list — we round-trip. |
| `Configurations.standard.customprops` | **Mandatory to preserve verbatim.** Maps with civilians ship `['ExtraArmies'] = STRING('ARMY_9 NEUTRAL_CIVILIAN')`; without it SC1 crashes when it tries to spawn entities under the undeclared armies. Stored on `MapInfo.CustomProps`. |
| ~~`map_version`~~ | **Never emit.** It's a FAF extension. Vanilla SC1 shows it as `(N)` in the lobby and **crashes on selection**. |

### `_save.lua` — required structure

| Section | Notes |
|---|---|
| `Scenario.next_area_id` | Hardcoded `'1'` is fine. |
| `Scenario.Props = { }` | Empty — props live in the .scmap, not here. |
| `Scenario.Areas = { }` | Empty. |
| `Scenario.MasterChain['_MASTERCHAIN_'].Markers` | All scene markers (mass, hydro, spawns, AI, etc.). |
| `Scenario.Chains = { }` | Empty. |
| `Scenario.next_queue_id`, `Scenario.Orders = { }` | **Required.** Engine crashes if missing. |
| `Scenario.next_platoon_id`, `Scenario.Platoons = { }` | **Required.** Same crash mode. |
| `Scenario.next_army_id`, `next_group_id`, `next_unit_id` | Counters; hardcoded values are fine. |
| `Scenario.Armies['ARMY_N']` | One entry per declared army (+ ARMY_9 / NEUTRAL_CIVILIAN when ExtraArmies is present). |

### Per-marker required fields

| Field | When | Why |
|---|---|---|
| `['size']`, `['resource']`, `['amount']` | Mass, Hydrocarbon | Resource readout. |
| `['color']` | Always | Editor display + minimap. |
| `['type']` | Always | The marker type string ("Mass", "Naval Area", "Blank Marker", …). |
| `['prop']` | Always for the standard types | Engine spawns this prop blueprint at the marker position. **Missing it crashes SC1 on map load.** Defaults per type in `SaveLuaWriter.DefaultPropFor`. |
| `['hint'] = BOOLEAN(true)` | Strategic markers (CombatZone, DefensePoint, RallyPoint, NavalArea, ExpansionArea, ProtectedExperimentalConstruction) | AI module references these. Missing on a strategy marker → AI init crashes. Default `true` via `SaveLuaWriter.DefaultHintFor`. |
| `['orientation']`, `['position']` | Always | Vec3. |
| `['adjacentTo']` | AI path markers | Round-tripped from input. |

### Terrain texture layout

Vanilla v53 maps have exactly **6 TerrainTexture slots**: base(0) + 4 splatmap-blended(1..4) +
macro(5). The macro is alpha-blended on top by the shader, not splatmap-blended. SC1's engine
**crashes** when loading a v53 with 10 slots (we tried — that was the editor's old behaviour).

- `MapStrataNormalizer.EnsureVanillaSlots(map)` runs at load time:
  - v53 with `len < 6` → pad to 6 (push the original last slot to slot 5 = macro).
  - v53 with `len > 6` (10 from a previous editor build) → **compact back to 6** if slots 5..8 are
    empty and slot 9 carries the macro. Otherwise leave alone (user must have painted those slots).
  - v56+ untouched.
  - Always zero the mask channels for any empty paintable slot (anti-magenta).
- `MaxPaintableStrata`: `len <= 6` → `len - 2` (= 4 for vanilla). `len > 6` → `len - 1` (= 9 for v56).
- `TerrainRenderer._upperLayerIndex`: `_stratumCount <= 6 ? _stratumCount - 1 : 9`.
- `MapGenerator` always produces 6-slot v53. Smart categories used: Grass(base), Rock, Dirt, Snow,
  Plateau, macro. Beach and SeaFloor are dropped (only 4 splatmap channels available).
- `NewMapService.CreateBlankMap` allocates 6 slots for v53, 10 for v56+.

### Relative texture paths

SCMP_021 ships with `slot[0].AlbedoPath = "snow_albedo.dds"` (no `/env/...` prefix). SC1 resolves
this against `/env/<tileset>/layers/<tilesetPrefix>_<name>` (e.g. `/env/tundra/layers/Tund_snow_albedo.dds`).
`MapStrataNormalizer.NormalizeRelativeTexturePaths` does the same inference from the other slots'
paths before the texture cache tries to load anything. Without it the slot renders magenta wherever
the splatmap is zero.

### Round-trip
The reader/writer produces binary-identical files for the 3 test maps (SCMP_001, SCMP_009, SCCA_A01).

## Companion Lua files

- `_scenario.lua`: ScenarioInfo (name, description, size, armies, norush)
- `_save.lua`: Markers (mass, hydro, spawns, AI pathfinding, expansion, defense, etc.)
- `_script.lua`: Minimal template
- Parsed via MoonSharp with registered pseudo-Lua functions (STRING, FLOAT, BOOLEAN, VECTOR3, GROUP, RECTANGLE)

## Game Data

Textures are extracted from `.scd` archives (ZIP format) in the `gamedata/` folder of the Steam
install. Case-insensitive lookup because the paths in `.scmap` and `.scd` use different cases.
Pfim decompresses DDS (DXT1/DXT3/DXT5) — **caveat: Pfim output is BGRA, B↔R swap needed for GL_RGBA**.

## Terrain Rendering

### Splatmap channel mapping (from the FA source code)
- **mask0** (TextureMaskLow): `.r` = strata 1, `.g` = strata 2, `.b` = strata 3, `.a` = strata 4
- **mask1** (TextureMaskHigh): `.r` = strata 5, `.g` = strata 6, `.b` = strata 7, `.a` = strata 8
- Layer 0 = base (LowerAlbedo), always visible
- Layer 9 = upper (macro texture), blends via its own alpha
- Sequential blending via `mix()` (not weighted sum)

### Shader types
- **TTerrain** (vanilla): raw splatmap weights
- **TTerrainXP**: half-range transform `clamp(mask * 2.0 - 1.0, 0, 1)` before blending

### Splatmap upload
- Pfim to decompress (supports ARGB raw and DXT5)
- BGRA→RGBA swap
- Upload as GL_RGBA
- **GL_CLAMP_TO_EDGE** (not REPEAT)

## Mouse controls (3D view)
- **Left click**: terrain brush painting
- **Right click**: orbit camera
- **Middle click**: pan camera
- **Wheel**: zoom
- **Ctrl+Z / Ctrl+Y**: undo/redo

## 2D View = orthographic capture of the 3D scene

The 2D view is **not** a separate CPU render. It's a capture of the 3D viewport into an FBO with a
top-down orthographic camera, read back via `glReadPixels`, flipped vertically, and used as the
background in `SkiaMapControl`. Markers, grid and HUD are drawn in Skia on top.

- Capture triggered by `GlViewport.RequestTopDownSnapshot()` after: map load, end of brush stroke
  (`HeightmapVersion` changes), texture changes (`TexturesVersion`), switching to a tab other than
  3D (the GL stays visible until the snapshot arrives, then hides).
- The snapshot must happen during an `OnOpenGlRender` of the GL control (current GL context).
  So we defer `IsVisible=false` until the `TopDownReady` event fires.
- Ortho camera: `Camera.CreateTopDown(w, h, maxHeight)` with `Orthographic=true`; handles the
  degenerate up vector at Pitch=90 (uses `(0,0,-1)` instead of `(0,1,0)`).
- The image read by `glReadPixels` has its origin at bottom-left: vertical flip before Skia use.
- Don't do **anything** special for 2D (no lighting override, no water overlay). Fix lighting and
  water on the 3D side, the capture inherits.

## Symmetry / duplication (Phase 10)

`SymmetryService.Apply(map, pattern, source)` mirrors heightmap + splatmaps low/high + markers.
6 patterns: `Vertical`, `Horizontal`, `DiagonalTLBR`, `DiagonalTRBL` (mirror ×1) and `QuadCross`,
`QuadDiagonals` (mirror ×3 = 4 regions). Transformations in normalised coords `[0,1]²`.

- All transforms are **involutive** → `SourceOf(pattern, source, p)` also serves to find the
  destination from the source by swapping the arguments.
- For `QuadDiagonals` (4 triangles N/E/S/W), the group is the Klein 4-group `{id, D1, D2, rot180}`;
  explicit composition table in `SymmetryService.QuadDiag`.
- Splatmap DDS: edit the `W*H*4` bytes after the 128-byte header in place. Channel order
  (ARGB/BGRA) doesn't matter since we only permute pixel positions.
- Markers: wipe the list, identify seeds in the source region, duplicate into each region with
  auto-rename (`ARMY_N` incremented, `"Mass NN"` likewise).
- `SymmetryApplyOp` snapshots the complete state (heightmap data clone, splatmap blobs clone,
  markers list copy) for Undo. Memory-heavy but simple and correct.
- UI: "Symmetry" tab with 6 `SymmetryThumbnail` items sharing the snapshot via
  `SkiaMapControl.TopDownBackground`. Hover highlights the region, click = apply.

## Undo/Redo

- `HeightmapBrushOp` (brush stroke)
- `AddMarkerOp`, `RemoveMarkerOp` (reinserts at the original index), `MoveMarkerOp` (1 entry per
  drag, captures the position on `OnPointerPressed`)
- `MovePropOp` (drag a prop on the 2D view, same pattern as MoveMarker — captures position at
  pointer-pressed, pushes the op at pointer-released. `Vm.RecordPropMove` re-clamps Y to the
  ground at the end of the drag.)
- `MoveUnitSpawnOp` (drag a UnitSpawn on the 2D view — same pattern. 2D click on a cyan/orange
  square selects the unit (yellow highlight), re-click-drag to move.
  `Vm.RecordUnitSpawnMove` re-clamps Y to the ground.)
- `SymmetryApplyOp` (full state restore: heightmap data, splatmaps, markers cloned)
- `MapScaleOp` (full snapshot: heightmap data, masks, aux DDS, marker/decal/prop positions)
- `ClampToGroundOp` (Y snapshot of props + ground-bound markers)
- `BatchAddPropsOp` / `BatchRemovePropsOp` (brush stroke / box delete)
- VM bumps `HeightmapVersion` / `MarkerVersion` / `TexturesVersion` / `PropVersion` on Undo/Redo
  → MainWindow PropertyChanged handler re-issues mesh/markers/textures invalidations + snapshot
  capture.

## Map scaling (Edit → Scale map)

`MapScaleService.Scale(map, newSize)` bilinearly resamples the heightmap (vertices), splatmaps
(Pfim decode → resample → re-encode ARGB DDS), markers/decals/props (proportional positions).
Aux layers (normal map, water aux, terrain type) regenerated blank at the new size. SC standard
sizes: `256` = 5km, `512` = 10km, `1024` = 20km, `2048` = 40km, `4096` = 80km.
**Matches the in-game player cap** (256 → 2 players, 512 → 4, 1024 → 6, 2048+ → 8).
Menu Edit → Scale map → ticks `✓` on the current size via a `SubmenuOpened` handler.

**Bug fix**: `WaterRenderer` only rebuilds its quad when the elevation changes. After scale,
`OpenGlMapControl.MarkMeshDirty()` also resets `_lastWaterElev = NaN` to force the rebuild —
otherwise the water keeps its pre-scale size and overflows the map.

## Build grid snap (Mass / Hydrocarbon)

`BuildGridSnap.Snap(type, x, z)` aligns resource markers on the SC build grid, otherwise the
extractor doesn't snap exactly on the visual deposit. Verified on **3239 mass + 188 hydro from
75 vanilla maps**: the fractional part of X and Z is always `0.5`, regardless of the parity of
the integer part. So:
- **Mass** and **Hydrocarbon** → snap to `N + 0.5` (cell centre), via `Round(c - 0.5) + 0.5`.

Applied in `AddMarker`, `PasteAt`, `RecordMarkerMove` (end of drag), and `SaveMap.SnapAll` as a
safety net.

## Ground clamp (props / markers / units)

`GroundClampService` snaps Y values to the heightmap (bilinear sample). 3 categories:
- `ClampPropsToGround(map)` — always snapped (rocks, trees).
- `ClampGroundMarkers(map)` — excludes AirPathNode, CameraInfo, WeatherGenerator/Definition, Effect.
- `ClampInitialUnits(map)` — for pre-placements (UnitSpawn in Army.InitialUnits).

Called **automatically** at the start of `SaveMap` (the on-disk `.scmap` never has floating
elements after a brush stroke). Also exposed via menu **Edit → Clamp props/markers to ground**
(undoable via `ClampToGroundOp`).

## Prop icons (offline render)

`tools/IconGenerator/` is a console project that scans `env.scd` and generates a 96×96 PNG icon
for each `*_prop.bp`:
1. Parses the SCM format (MODL header, vertices with pos/normal/uv, uint16 indices) — ~100 lines.
2. Loads the `_albedo.dds` (LOD0 of the blueprint, fallback convention `<basename>_albedo.dds`).
3. C# software rasteriser: isometric view (yaw 35°, pitch -25°), Lambert + brightness boost,
   alpha-cut for tree foliage.
4. Saves into `src/SupremeCommanderEditor.Rendering/PropIcons/{biome}_{basename}.png`.

Output: 248 prop icons (`PropIcons/{biome}_{basename}.png`) + 304 unit icons
(`UnitIcons/{unitid}.png`) at **192×192** (2× the UI card size — friendly to HiDPI x2,
downscaled in bilinear on normal screens). Framing: `radius = max_extent * 0.5` → the model fills
~95% of the square, ~5% margin. Bilinear filtering enabled downstream:
- Skia 2D: `SKPaint { FilterQuality = SKFilterQuality.High }` on `DrawBitmap` calls.
- Avalonia XAML: `RenderOptions.BitmapInterpolationMode="HighQuality"` on the `<Image>` in the
  menu and the hover popup.

For vanilla SC. The remaining 9 props without an icon are editor markers (M_Mass, M_Camera,
M_Blank…) that don't get placed on real maps. The 23 units without a LOD0 mesh are wrecks /
special.

Important note: some `.bp` files (notably `Trees/Groups/*`) declare their albedo via a relative
path with `..` (e.g. `AlbedoName = '../Pine06_V1_albedo.dds'`). The generator normalises paths
(`NormalizePath`: collapses `..` and `.`) before the SCD lookup. For units, the `.bp` doesn't list
an explicit AlbedoName — convention is `<basename>_Albedo.dds` in the same folder.

Generator usage:
```bash
cd tools/IconGenerator
dotnet run -- "<Steam>/Supreme Commander/gamedata/env.scd" \
              "<Steam>/Supreme Commander/gamedata/units.scd" \
              ../../src/SupremeCommanderEditor.Rendering
```
Checked into the repo, embedded at compile time via `<EmbeddedResource Include="PropIcons\*.png"/>`.
No GL/EGL/context dependency — pure software, works everywhere.

Runtime loading: `PropIconService.LoadIconBytes(blueprintPath)` returns the PNG bytes.
Path → resource mapping: `/env/<biome>/.../<name>_prop.bp` → `PropIcons/{biome}_{name}.png`.

Re-run:
```bash
cd tools/IconGenerator
dotnet run -- "<SteamLibrary>/Supreme Commander/gamedata/env.scd" \
              ../../src/SupremeCommanderEditor.Rendering/PropIcons
```

## Prop menu (2-level bar at the bottom of the 2D view)

`PropCatalog.All` (Rendering) builds at startup by parsing the embedded resource names. For each
embedded icon, it derives (biome, basename, kind, displayName, BlueprintPath, Bitmap). Kind via
heuristic on the basename (`Rocks`/`Trees`/`Bushes`/`Logs`/`Wreckage`/`Geothermal`/`Misc`).

UI in MainWindow.axaml: `Border DockPanel.Dock="Bottom"` with two `ScrollViewer`s (fixed heights
106 + 128) + stacked horizontal `ListBox`es (Classes="propMenu"). Heights are fixed because the
Avalonia 12 scrollbar renders as an overlay and masks content if you rely only on padding.
`ListBox.SelectedItem` is bound directly to the VM → automatic **yellow border** on the selected
item via `ListBoxItem:selected /template/ ContentPresenter`. Sizes: level-1 cards = 86×78
(icon 56), level-2 = 86×100 (icon 72).

Preview on the 2D — two extra visual affordances:
1. **Above `_zoom >= 4.5`**: props and UnitSpawn render with their **SC icon** instead of a
   dot/square (48px on screen). Caches `_propIconCache`/`_unitIconCache` in `SkiaMapControl`,
   loaded on demand via `PropIconService.LoadIconBytes` or `LoadUnitIconBytes` + `SKBitmap.Decode`.
   Below the threshold: brown dots / cyan-orange squares.
2. **Hover popup**: `OnPointerMoved` hit-tests a prop or UnitSpawn (radius `28/zoom` map units in
   icon mode, otherwise 6) and exposes `HoveredProp` / `HoveredPropIcon` / `HoveredPropName` /
   `HasHoveredProp`. An Avalonia `Popup` with `Placement="Pointer"` follows the cursor. Disabled
   during pan/drag/box-select/brush.

**Zoom-coherent hit-test**: `HitRadius()` returns `24/zoom` map units in icon mode (= half the
icon side), otherwise a constant 6f. Used everywhere (click selection, hover, drag detection).

**Selection takes priority over placement**: with a menu entry selected, a left click on an
existing element selects it instead of placing on top. **Ctrl+click** forces placement on top.
**Esc** clears the current menu entry and category (back to plain selection mode).

Placement:
- **Brush off** + a prop selected → `IsSinglePlaceActive = true`. Left click on 2D = drop 1 prop
  at the position (`SinglePlaceProp` event → `Vm.PlaceSelectedPropAt`). Y clamped to ground.
- **Brush on** + a prop selected → the brush only drops that blueprint (no random pick from a
  preset). With no prop selected, falls back to the preset (PropBrushPresets) for backward compat.

## Props: multi-select + brush + 2D render

- **2D render**: small brown dots. Selection (click) = yellow halo, propagated to the VM via
  `PropSelected`.
- **Multi-select**: `Shift+drag` in the 2D view → translucent blue rectangle.
  `MultiSelectedProps: HashSet<Prop>`. The **Delete** key triggers `vm.DeleteProps(...)` which
  pushes a `BatchRemovePropsOp` (captures the original indices for exact reinsertion).
- **Props brush**: toggle in the 2D toolbar + preset ComboBox + radius/density sliders. Stamps
  distributed in the disc (rejection sampling), random Y rotation, random scale 0.85–1.15,
  Y clamped to heightmap. **Re-stamp only if movement > radius/2** to avoid stacking. Click-and-
  drag = a single `BatchAddPropsOp` for undo.
- **Presets** in `PropBrushPresets`: `RocksEvergreen`, `TreesTropical`. All blueprint paths are
  **verified from real maps** (e.g. `Ambush Pass v2`). NEVER guess — a non-existent blueprint is
  **silently dropped by the SC engine** at map load.
- **Brush cursor circle**: redrawn on every `OnPointerMoved` when `IsPropBrushActive=true`
  (otherwise it stays frozen until the click). Hidden on `OnPointerExited`.

## Pre-placed units (civilians, defenses, turrets)

`Army.InitialUnits: List<UnitSpawn>` (Name, BlueprintId, Position, Orientation, Orders, Platoon).
Stored in `_save.lua` under `Armies[name].Units.Units.INITIAL.Units`.

- `SaveLuaReader.ReadInitialUnitsByArmy(path)` parses and returns a dict army-name → list.
- `LoadMap` attaches them to `MapInfo.Armies` and creates stub `Army` objects for neutrals
  (`NEUTRAL_CIVILIAN`, `CIV*`, etc.) that don't appear in `scenario.lua`.
- `SaveLuaWriter.WriteUnit` serialises them — **without this, saving = losing every civilian**.
- Included in `MapScaleService.ScaleInitialUnits` and `GroundClampService.ClampInitialUnits`.
- 2D render: `cyan` squares (neutrals: `NEUTRAL*`, `CIV*`, `ARMY_9`) or `orange` (player-owned).
- Editing (selection, position, type, add): not implemented yet.

## Compass widget (3D)

Compass rose in the top-right corner of `OpenGlMapControl` (sibling overlay of the GL control,
`IsHitTestVisible = false` on the inner children, `true` on the host so it's clickable). Semi-
transparent black circle, red "N" arrow + grey "S" tail, built from Avalonia primitives
(`Ellipse` + `Polygon` + `TextBlock`) with a `RotateTransform`. Formula: `compass.Angle = 90°
- Camera.Yaw` (verified across the 4 cardinal directions). Updated on every camera orbit and
every map load. Clicking the compass snaps `Camera.Yaw` back to 90° (north).

## Camera: reset only on load

`Camera.FitToMap` is called ONLY when `_recenterCamera == true`. The flag is set in:
- `SetMap` (map load) — first FitToMap.
- `RecenterOnNextFrame` exposed on the wrapper — called after a `ScaleMap` since dimensions change.

`MarkMeshDirty` (brush, undo, redo, edits) does **not** reset the camera → the user keeps their
position/zoom/orientation while editing. Pre-fix bug: the camera reverted to yaw=-45 on every
brush stroke.

## Marker icons (2D)

`MarkerIconService` tries to load DDS textures from the game for each `MarkerType` via paths like
`/textures/ui/common/...`. The paths are best-guess based on FA strategicicons conventions. If no
candidate loads → fallback to a vector shape (Skia paths). The `[MarkerIcon]` lines in debug.log
list DDS files containing `marker` or `strategicicons` inside the SCD archives → handy for
adjusting paths against what actually exists.

**Mass = simple green dot**: excluded from the service (user preference), always vector.

## Copy / Paste (Ctrl+C / Ctrl+V)

Window-level handler in `OnKeyDown`. Ctrl+C snapshots the selected marker OR prop into
`_clipboard: object?`. Ctrl+V clones at `SkiaViewport.CursorWorldPos`:
- Marker: `MakeUniqueMarkerName` (increments `ARMY_N` or `Prefix NN`), Y clamped to ground,
  Mass/Hydro snapped to the build grid → `AddMarkerOp`.
- Prop: preserves BlueprintPath/Rotation/Scale, Y clamped to ground → `AddPropOp`.

Mouse tracking (`CursorWorldPos`) is updated on every `OnPointerMoved` in 2D.

## Windows distribution (self-contained)

```bash
dotnet publish src/SupremeCommanderEditor.App -p:PublishProfile=win-x64
```

Output: `src/SupremeCommanderEditor.App/bin/publish/win-x64/SupremeCommanderMapEditor.exe`
(~46 MB). One file, nothing on the side. Everything is embedded:

- .NET 9 runtime (CoreCLR + BCL)
- Avalonia + Skia + HarfBuzz + ANGLE (Windows natives)
- The 3 project DLLs (App + Rendering + Core)
- **The 6 shaders** as `EmbeddedResource` in the Rendering DLL (logical name `Shaders/<file>`),
  loaded via `Assembly.GetManifestResourceStream` (NOT `File.ReadAllText` because
  `Assembly.Location` returns `""` in single-file mode)
- PDBs `embedded` in the exe, separate `.pdb` files stripped in post-publish

Config in `src/SupremeCommanderEditor.App/SupremeCommanderEditor.App.csproj` (publish props
ignored by `dotnet build`) and `Properties/PublishProfiles/win-x64.pubxml`. Trimming disabled
(Avalonia + CommunityToolkit.Mvvm rely on reflection).

**OutputType = WinExe** (not `Exe`) → no console window pops up on launch under Windows.

**Distribution as a `.zip` to avoid SmartScreen**: a `Target AfterTargets="Publish"` in the
csproj automatically zips the `.exe` into `SupremeCommanderMapEditor.zip` alongside. The `.zip`
carries the Mark-of-the-Web on download, but the `.exe` extracted by Windows Explorer doesn't
inherit it → no SmartScreen warning. Avoids buying a code-signing cert (~$200-700/year for
friends-use). Implemented via a stage folder so that `ZipDirectory` doesn't recursively zip
itself.

## Linux distribution (self-contained + AppImage)

```bash
dotnet publish src/SupremeCommanderEditor.App -p:PublishProfile=linux-x64
tools/build-appimage.sh
```

Profile `linux-x64.pubxml` mirrors `win-x64.pubxml` (RID swapped, PDB strip target identical).
Produces a self-contained ELF `SupremeCommanderMapEditor` (~56 MB) under
`bin/publish/linux-x64/`.

`tools/build-appimage.sh` then packages that ELF into
`SupremeCommanderMapEditor-x86_64.AppImage` (~49 MB) — one-file, no install, double-clickable,
works on any glibc distro. The script:
1. Builds an AppDir layout (`AppRun` script, `.desktop` file, icon, `usr/bin/<binary>`).
2. Fetches `appimagetool-x86_64.AppImage` on the fly (cached under `bin/publish/linux-x64/.tools/`)
   if not installed system-wide.
3. Invokes it with `--appimage-extract-and-run` so it works on CI runners with no FUSE.

The desktop entry has `Categories=Game;Development;` so KDE/GNOME activities place it sensibly.
The icon (`supcom.png`, 48×48 from the SC game .ico) is mounted both at the AppDir root and
under `usr/share/icons/hicolor/48x48/apps/` to satisfy any launcher convention.

## Release pipeline (`.github/workflows/release.yml`)

CI workflow triggers on every `v*` tag push (e.g. `git tag v0.2.0 && git push --tags`). Three
parallel-ish jobs:
- `build-windows` (Windows runner) — runs the `win-x64` publish, uploads
  `SupremeCommanderMapEditor.zip` as an artifact.
- `build-linux` (Ubuntu runner) — runs the `linux-x64` publish + `tools/build-appimage.sh`,
  uploads the AppImage as an artifact.
- `release` (after the two) — downloads both artifacts, creates a GitHub Release with
  `softprops/action-gh-release@v2`, auto-generates release notes from the commits since the
  previous tag, attaches both binaries.

In the README, download links use the **`releases/latest/download/<filename>`** pattern (GitHub
auto-redirects to the most recent release), so the README never needs editing when a new tag
goes out.

Manual fallback: build locally, drag-drop the artifacts on github.com/.../releases/new.

## Settings + Diagnostics

- `AppSettingsService`: JSON in `%APPDATA%\SupremeCommanderMapEditor\settings.json`. Persists the
  `GameInstallPath` picked via **Settings → Set game folder…**.
- `GameDataService.FindGamePath()` searches Steam libraries (parsing `libraryfolders.vdf`),
  multi-drives, GOG, FA and vanilla, Linux paths.
- `DebugLog`: appends to `%APPDATA%\SupremeCommanderMapEditor\debug.log`. Reset at each startup
  (`Program.Main`). Used for remote bug reports (no attached console in single-file).
- Menu **Settings → Diagnostics…** opens `InfoDialog` showing: OS, settings file, loaded
  archives, per-strata status (LoadFile + Pfim decode), debug.log path.

## Controls

### 3D view
- **Left click**: pan (when no tool is selected) or brush painting (when a tool is active)
- **Right click**: orbit camera
- **Middle click**: pan camera
- **Wheel**: zoom
- **Ctrl+Z / Ctrl+Y**: undo/redo
- **Compass click**: snap yaw back to north

### 2D view
- **Left click**: selection (priority marker > prop > UnitSpawn) or placement (if a menu entry is
  selected in the prop menu)
- **Left-click drag** on a marker / prop / UnitSpawn already selected: move (undoable)
- **Shift + left-click drag**: box selection multi-props (blue rectangle)
- **Right / middle click**: pan
- **Wheel**: zoom — above `_zoom >= 4.5`, props and UnitSpawn render as their SC icon instead of
  dot/square
- **Hover**: popup with icon + name of the prop or unit under the cursor
- **Ctrl+C / Ctrl+V**: copy / paste of the selected marker or prop (paste = cursor position)
- **Ctrl+click**: forces placement (with a menu entry selected) on top of an existing element
- **Esc**: clears the prop menu entry + current category
- **Delete**: deletes the selection (multi-prop if any, otherwise the selected marker)
- Top bar: toggles `Grid` / `Diagonals` / `Snap` + `Step` + `Brush mode` toggle + Size/Density sliders
- Bottom bar (2-level prop menu): categories (Rocks/Trees/Bushes/Logs/Wreckage/UEF/Cybran/Aeon/
  Seraphim/Civilians/Other Units) then individual items with their SC icon. Yellow border on
  selection.

### Symmetry tab
- Click on a region of a thumbnail = apply that pattern with that region as the source

## Progress

### Completed phases
- [x] **Phase 1**: .scmap parser/writer (v53 + v56/v60, perfect round-trip)
- [x] **Phase 2**: Lua I/O (scenario, save, script) + create map from scratch
- [x] **Phase 3**: Avalonia shell (menus, property panels, dark theme)
- [x] **Phase 4**: 3D viewport (heightmap terrain, water, real-time lighting, brush cursor, compass)
- [x] **Phase 5**: Top-down 2D view (ortho capture of 3D, markers/grid/snap on top)
- [x] **Phase 6**: Undo/redo + heightmap editing (5 brush modes)
- [x] **Phase 7**: Texture painting (multi-strata, splatmaps, channel mapping)
- [x] **Phase 8**: Markers (select/create/drag/delete, property panel, filters, 3D point-sprite
  render, full undo/redo via MarkerOps, build-grid snap for Mass/Hydro, vector icons + attempt at
  loading game icons)
- [x] **Phase 9**: Props — 2D render (SC icon at zoom ≥ 4.5), Shift+drag multi-select, batch
  delete, prop brush (radius/density), copy/paste, ground clamp, scale resample, **undoable
  single-prop drag**, hover popup (icon + name). Icon menu at the bottom of the 2D view (248
  props embedded, 7 categories). Decals: positions resampled on scale, no editing.
- [x] **Phase 9b**: Placement + drag of pre-placed units (UnitSpawn) — 304 unit icons embedded,
  5 categories per faction, single-click placement into NEUTRAL_CIVILIAN, undoable drag via
  `MoveUnitSpawnOp`, hover popup. 3D icons at zoom ≥ 4.5 (with faction tint bar preserved).
- [x] **Phase 10**: Symmetry tools (6 patterns mirror ×1 and ×3, heightmap+splatmaps+markers,
  undoable, dedicated tab with clickable thumbnails, auto-rename Mass NN / ARMY_N)
- [x] **Phase 11 (partial)**: Map scaling (5 standard SC sizes, undoable). Skybox/validation: no.
- [x] **Phase 12 (partial)**: Windows self-contained packaging, copy/paste, keyboard shortcuts,
  persisted settings, diagnostics dialog, broader install auto-detection, ground clamp on save,
  army reconciliation, civilian units round-trip.
- [x] **Procedural map generator**: deterministic seed, N teams on a circle, flat plateaus carved
  at each spawn, smart texture set per biome, terrain-only symmetry (markers untouched). Outputs
  vanilla 6-slot v53 (base + Rock/Dirt/Snow/Plateau + macro) so generated maps load in the
  original SC1 without crashing.
- [x] **v1.0.0 — SC1 vanilla compatibility pass**: every quirk needed for saved maps to load in
  the original 2007 engine (see `## SC1 vanilla compatibility`). 71/71 vanilla maps in
  `<Steam>/Supreme Commander/maps` round-trip binary-identical through reader/writer.
- [x] **Welcome screen**: three big buttons (New / Open / Generate) shown before any map is
  loaded.

### Upcoming phases
- [ ] **Phase 9 (continued)**: Individual prop editing (single selection, drag, properties), add
  via blueprint picker, full decal editing.
- [ ] **Phase 11 (continued)**: Skybox editing, auto-regenerated map preview, lobby validation.
- [ ] **Phase 12 (continued)**: Linux/macOS packaging, dynamic terrain mesh LOD.
- [ ] **Civilian units editing**: 2D selection, position drag, type picker, delete.

## Build & Run

```bash
dotnet build SupremeCommanderEditor.sln
dotnet run --project src/SupremeCommanderEditor.App
dotnet test
dotnet publish src/SupremeCommanderEditor.App -p:PublishProfile=win-x64
```

## Technical notes

- **Avalonia 12 on Windows uses ANGLE** (D3D11 → OpenGL ES 3.0 + GLSL ES 300, not desktop GL).
  **Critical consequence**: shaders cannot contain `#version 330 core` — ANGLE rejects it.
  The `.vert`/`.frag` files contain **no** `#version` directive; `OpenGlMapControl.PrepareShader`
  prefixes one at compile time:
  - ES (Windows ANGLE): `#version 300 es\nprecision highp float;\nprecision highp sampler2D;`
  - Desktop (Linux/macOS): `#version 330 core`.
  Detection via `glGetString(GL_VERSION)` which contains `"OpenGL ES"` on ANGLE. All the
  constructs used (layout location, in/out, `texture()`, `mix()`, `gl_PointSize`, `gl_PointCoord`)
  sit in the intersection of GLSL 330 core ∩ GLSL ES 300.
- Avalonia 12's `OpenGlControlBase` renders above the compositor → the GL control must be hidden
  (`IsVisible=false`) when another tab is active. But to take a snapshot for the 2D view, it must
  stay visible until the capture finishes, then hide. Note: `IsVisible=false` kills the render
  loop — the codebase parks the control off-screen via a huge negative `Margin` instead (also
  used when no map is loaded so the welcome screen stays clean).
- Avalonia 12's `GlInterface` is low-level — many GL functions accessed via `GetProcAddress` +
  delegates (Uniform3f, BlendFunc, ActiveTexture, CompressedTexImage2D, glBindFramebuffer,
  glReadPixels, etc.). Wrappers grouped into `GlExtensions` and `GlFbo`.
- Avalonia's `Slider.Value` is `double` — ViewModel properties must be `double` (not `float`),
  otherwise the binding fails silently.
- `Panel.Render` is sealed in Avalonia 12 — use a child `Image` + `WriteableBitmap` for custom 2D
  rendering.
- During brush painting, only refresh the 2D view on click release (not every frame).
- `Assembly.Location` is empty in single-file publish — use `EmbeddedResource` +
  `GetManifestResourceStream`, not `File.ReadAllText` next to the exe.
- For undoable ops touching splatmaps (DDS blob), snapshot/restore the full `DdsData`; don't try
  to delta-encode — staying simple is more reliable.
- Install auto-detection: Steam's `libraryfolders.vdf` for libraries on other drives. Otherwise
  the user goes through Settings → Set game folder…
