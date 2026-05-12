using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupremeCommanderEditor.Core.Formats.Lua;
using SupremeCommanderEditor.Core.Formats.Scmap;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Operations;
using SupremeCommanderEditor.Core.Services;
using SupremeCommanderEditor.Rendering;

namespace SupremeCommanderEditor.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "Supreme Commander Map Editor";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private ScMap? _currentMap;
    [ObservableProperty] private string? _currentFilePath;

    // Map Info
    [ObservableProperty] private string _mapName = "";
    [ObservableProperty] private string _mapDescription = "";
    [ObservableProperty] private int _mapWidth;
    [ObservableProperty] private int _mapHeight;
    [ObservableProperty] private int _mapVersionMinor;

    // Lighting (double to match Slider.Value)
    [ObservableProperty] private double _lightingMultiplier;
    [ObservableProperty] private double _sunDirX, _sunDirY, _sunDirZ;
    [ObservableProperty] private double _sunAmbienceR, _sunAmbienceG, _sunAmbienceB;
    [ObservableProperty] private double _sunColorR, _sunColorG, _sunColorB;
    [ObservableProperty] private double _bloom;
    [ObservableProperty] private double _fogStart, _fogEnd;

    // Water
    [ObservableProperty] private bool _hasWater;
    [ObservableProperty] private double _waterElevation;
    [ObservableProperty] private double _waterElevationDeep;
    [ObservableProperty] private double _waterElevationAbyss;

    // Brush tool
    [ObservableProperty] private int _brushModeIndex; // 0=Raise,1=Lower,2=Smooth,3=Flatten,4=Plateau
    [ObservableProperty] private double _brushRadius = 15;
    [ObservableProperty] private double _brushStrength = 10;
    [ObservableProperty] private bool _isBrushActive;

    /// <summary>When true, every terrain-brush stroke is decorated with automatic texture painting:
    /// each affected pixel is classified by altitude/slope/water-proximity and the matching strata
    /// is painted into the splatmap. Missing categories are auto-assigned from the texture library
    /// if the map has free strata slots. Single combined undo per stroke.</summary>
    [ObservableProperty] private bool _isSmartTexturingEnabled;

    // Stats
    [ObservableProperty] private int _markerCount;
    [ObservableProperty] private int _propCount;
    [ObservableProperty] private int _decalCount;

    // Marker selection (placement is driven by the bottom palette now)
    [ObservableProperty] private Marker? _selectedMarker;

    // 2D grid overlay + snap
    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _showDiagonalGrid;
    [ObservableProperty] private bool _snapToGrid;
    [ObservableProperty] private int _gridStep = 32;

    /// <summary>Bumped whenever the marker list/positions change. Views observe this to refresh.</summary>
    [ObservableProperty] private int _markerVersion;

    /// <summary>Bumped whenever splatmap pixel data changes (e.g. after symmetry). GL re-uploads on next render.</summary>
    [ObservableProperty] private int _texturesVersion;

    /// <summary>Currently selected prop in the 2D view (mutually exclusive with SelectedMarker).</summary>
    [ObservableProperty] private Prop? _selectedProp;

    /// <summary>Latest copy() snapshot, paste() clones it at the cursor. Holds a Marker or a Prop.</summary>
    private object? _clipboard;

    /// <summary>Mass amount for the selected Mass marker — exposed for the floating overlay slider.
    /// Writes back to <see cref="SelectedMarker"/>.Amount on change.</summary>
    [ObservableProperty] private double _selectedMarkerAmount;

    /// <summary>True when the current selection is a Mass marker (drives the Amount overlay visibility).</summary>
    public bool IsMassMarkerSelected =>
        SelectedMarker is { Type: MarkerType.Mass };

    // Undo/Redo
    public UndoRedoService UndoRedo { get; } = new();
    public HeightmapBrushTool BrushTool { get; } = new();
    public GameDataService GameData { get; } = new();

    // Guard to prevent sync during PopulateFromMap
    private bool _isPopulating;

    /// <summary>Incremented each time the heightmap is modified, so renderers can rebuild meshes.</summary>
    [ObservableProperty] private int _heightmapVersion;

    public bool HasMap => CurrentMap != null;

    public AppSettingsService Settings { get; } = AppSettingsService.Load();

    public MainWindowViewModel()
    {
        // Try the user's saved path first; fall back to auto-detection; fall back to warning.
        if (!string.IsNullOrWhiteSpace(Settings.GameInstallPath) && GameData.TryInitialize(Settings.GameInstallPath))
        {
            StatusText = $"Game data: {GameData.GamePath}";
        }
        else if (GameData.TryInitialize())
        {
            StatusText = $"Game data: {GameData.GamePath}";
            Settings.GameInstallPath = GameData.GamePath;
            Settings.Save();
        }
        else
        {
            StatusText = "Supreme Commander not found — textures will use height-color fallback. Settings → Set game folder…";
        }
    }

    /// <summary>Reload textures from a user-chosen install folder. Returns true on success.</summary>
    public bool SetGameInstallPath(string folder)
    {
        GameData.Dispose();
        if (!GameData.TryInitialize(folder))
        {
            StatusText = $"Invalid folder: no gamedata/*.scd in {folder}";
            return false;
        }
        Settings.GameInstallPath = GameData.GamePath;
        Settings.Save();
        StatusText = $"Game data: {GameData.GamePath} ({GameData.ArchiveCount} .scd)";
        // Force texture re-upload on the GL view + rescan the texture library + rebuild categories.
        TexturesVersion++;
        _textureLibrary = null;
        RefreshViewport3DCategories();
        return true;
    }

    /// <summary>Multi-line report explaining the current GameData state and per-strata lookups.</summary>
    public string BuildDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"Settings file: {AppSettingsService.GetPath()}");
        sb.AppendLine($"Saved game path: {Settings.GameInstallPath ?? "(none)"}");
        sb.AppendLine();
        sb.AppendLine($"GameData initialized: {GameData.IsInitialized}");
        sb.AppendLine($"GameData.GamePath: {GameData.GamePath ?? "(null)"}");
        sb.AppendLine($"Archives loaded: {GameData.ArchiveCount}");
        if (GameData.IsInitialized)
        {
            sb.AppendLine("Archives:");
            foreach (var name in GameData.ArchiveNames)
                sb.AppendLine($"  - {name}");
        }
        sb.AppendLine();
        if (CurrentMap == null)
        {
            sb.AppendLine("No map loaded. Open a .scmap to test texture lookup.");
        }
        else
        {
            sb.AppendLine($"Map loaded. Strata count: {CurrentMap.TerrainTextures.Length}");
            for (int i = 0; i < CurrentMap.TerrainTextures.Length; i++)
            {
                var tx = CurrentMap.TerrainTextures[i];
                var path = tx.AlbedoPath;
                if (string.IsNullOrEmpty(path)) { sb.AppendLine($"  [{i}] -    (empty path)"); continue; }
                bool foundInArchive = GameData.LoadFile(path) != null;
                if (!foundInArchive) { sb.AppendLine($"  [{i}] MISS {path}"); continue; }
                // Pfim decode test: many Windows-only failures show up here, not in LoadFile.
                var decoded = GameData.LoadTextureDds(path);
                if (decoded == null) { sb.AppendLine($"  [{i}] DECODE-FAIL {path}"); continue; }
                sb.AppendLine($"  [{i}] OK   {decoded.Value.Width}x{decoded.Value.Height}  {path}");
            }
        }
        sb.AppendLine();
        sb.AppendLine($"Debug log: {DebugLog.Path}");
        return sb.ToString();
    }

    public void LoadMap(string scmapPath)
    {
        try
        {
            var map = ScmapReader.Read(scmapPath);
            var dir = Path.GetDirectoryName(scmapPath)!;
            var baseName = Path.GetFileNameWithoutExtension(scmapPath);

            var scenarioPath = Path.Combine(dir, $"{baseName}_scenario.lua");
            if (File.Exists(scenarioPath))
                map.Info = ScenarioLuaReader.Read(scenarioPath);

            var savePath = Path.Combine(dir, $"{baseName}_save.lua");
            if (File.Exists(savePath))
            {
                map.Markers = SaveLuaReader.ReadMarkers(savePath);
                // Load pre-placed civilian/neutral units and attach them to the matching armies in MapInfo.
                // Without this, saving back to disk would wipe shields/turrets/civilian buildings.
                var byArmy = SaveLuaReader.ReadInitialUnitsByArmy(savePath);
                foreach (var army in map.Info.Armies)
                    if (byArmy.TryGetValue(army.Name, out var units))
                        army.InitialUnits = units;
                // Some maps put neutrals under armies that aren't declared in scenario.lua (e.g.
                // NEUTRAL_CIVILIAN). Create stub Army entries for those so the data is preserved.
                foreach (var (name, units) in byArmy)
                {
                    if (map.Info.Armies.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                    map.Info.Armies.Add(new Army { Name = name, InitialUnits = units });
                }
            }

            CurrentMap = map;
            CurrentFilePath = scmapPath;
            PopulateFromMap(map);
            RefreshViewport3DCategories(); // "Map" palette category depends on map's strata.
            StatusText = $"Loaded: {baseName} ({map.Heightmap.Width}x{map.Heightmap.Height}, v{map.VersionMinor})";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading map: {ex.Message}";
        }
    }

    [RelayCommand]
    public void Save() => SaveMap();

    public void SaveMap()
    {
        if (CurrentMap == null || CurrentFilePath == null) return;

        try
        {
            ApplyToMap(CurrentMap);
            // Keep scenario.lua's Armies list in lockstep with the ARMY_N spawn markers in the map,
            // otherwise the game lobby will only show slots for the armies that were already declared.
            ArmyReconciler.Reconcile(CurrentMap);
            // Snap props & ground markers to the terrain surface so heightmap edits don't leave
            // rocks/trees floating or buried in the saved file. Silent — not pushed to the undo stack.
            GroundClampService.ClampPropsToGround(CurrentMap);
            GroundClampService.ClampGroundMarkers(CurrentMap);
            GroundClampService.ClampInitialUnits(CurrentMap);
            // Final safety net: align every Mass/Hydro marker to the SupCom build grid in case a
            // marker slipped in via an older save or hand-edited Lua.
            BuildGridSnap.SnapAll(CurrentMap);

            var dir = Path.GetDirectoryName(CurrentFilePath)!;
            var folderName = Path.GetFileName(dir);

            DebugLog.Write($"[Save] {CurrentFilePath}  props={CurrentMap.Props.Count}  markers={CurrentMap.Markers.Count}");
            ScmapWriter.Write(CurrentFilePath, CurrentMap);

            var baseName = Path.GetFileNameWithoutExtension(CurrentFilePath);
            ScenarioLuaWriter.Write(
                Path.Combine(dir, $"{baseName}_scenario.lua"),
                CurrentMap.Info, folderName, baseName);
            SaveLuaWriter.Write(
                Path.Combine(dir, $"{baseName}_save.lua"),
                CurrentMap.Markers, CurrentMap.Info.Armies);
            // Write a minimal _script.lua if absent, so the scenario reference resolves at game load.
            var scriptPath = Path.Combine(dir, $"{baseName}_script.lua");
            if (!File.Exists(scriptPath))
                ScriptLuaWriter.Write(scriptPath);

            StatusText = "Map saved successfully";
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving map: {ex.Message}";
        }
    }

    public void SaveMapAs(string scmapPath)
    {
        if (CurrentMap == null) return;
        CurrentFilePath = scmapPath;
        SaveMap();
    }

    /// <summary>
    /// Rename the map: update its display name (ScenarioInfo.Name → shown in the lobby) AND
    /// rename the on-disk folder to match. The four companion files (.scmap + *_scenario.lua +
    /// *_save.lua + *_script.lua) keep their existing base name, but their containing folder
    /// gets renamed, and the next save regenerates scenario.lua with the new `/maps/<folder>/…`
    /// paths automatically (ScenarioLuaWriter takes folderName from Path.GetFileName(dir)).
    /// </summary>
    /// <returns>null on success, or an error message to show the user.</returns>
    public string? RenameMap(string newName)
    {
        if (CurrentMap == null || CurrentFilePath == null) return "No map is loaded.";
        if (string.IsNullOrWhiteSpace(newName)) return "Name cannot be empty.";

        // Strip filesystem-hostile chars (`/`, `\`, `:`, `*`, `?`, `"`, `<`, `>`, `|`, control chars).
        // We allow spaces — vanilla SC maps use them (e.g. "260511-Ambush Pass v2").
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(newName.Where(c => Array.IndexOf(invalid, c) < 0)).Trim();
        if (string.IsNullOrEmpty(sanitized)) return "Name has no usable characters.";

        var oldDir = Path.GetDirectoryName(CurrentFilePath)!;
        var parentDir = Path.GetDirectoryName(oldDir);
        if (parentDir == null) return "Map is not inside a maps folder.";

        var newDir = Path.Combine(parentDir, sanitized);
        if (string.Equals(oldDir, newDir, StringComparison.Ordinal))
        {
            // No folder change, only the in-map display name.
            CurrentMap.Info.Name = newName;
            MapName = newName;
            SaveMap();
            return null;
        }

        if (Directory.Exists(newDir))
            return $"A folder named '{sanitized}' already exists next to the current one.";

        try
        {
            Directory.Move(oldDir, newDir);
        }
        catch (Exception ex)
        {
            return $"Could not rename folder: {ex.Message}";
        }

        var fileName = Path.GetFileName(CurrentFilePath);
        CurrentFilePath = Path.Combine(newDir, fileName);
        CurrentMap.Info.Name = newName;
        MapName = newName;

        SaveMap();
        StatusText = $"Renamed map to '{newName}' (folder: {sanitized})";
        return null;
    }

    public void NewMap(int size, int armyCount, string name)
    {
        var map = NewMapService.CreateBlankMap(size, armyCount, name);
        CurrentMap = map;
        CurrentFilePath = null;
        PopulateFromMap(map);
        RefreshViewport3DCategories();
        StatusText = $"New map: {name} ({size}x{size}, {armyCount} players)";
    }

    /// <summary>Install a procedurally-generated map as the active map. Resets all editor state
    /// (path, selections) so it behaves like a fresh New Map.</summary>
    public void InstallGeneratedMap(ScMap map)
    {
        CurrentMap = map;
        CurrentFilePath = null;
        UndoRedo.Clear();
        SelectedMarker = null;
        SelectedProp = null;
        SelectedUnitSpawn = null;
        PopulateFromMap(map);
        RefreshViewport3DCategories();
        StatusText = $"Generated: {map.Info.Name} ({map.Heightmap.Width}×{map.Heightmap.Height})";
    }

    // Cached "<LOC KEY>" prefixes from the .scmap so we display the readable text in the editor
    // but preserve the localization tag on save (vanilla maps would otherwise lose their in-game
    // lobby translation key).
    private string _mapNameLocPrefix = "";
    private string _mapDescLocPrefix = "";

    /// <summary>Split a string like "&lt;LOC SCMP_001&gt;Burial Mounds" into ("&lt;LOC SCMP_001&gt;", "Burial Mounds").
    /// Returns ("", raw) for plain strings without a LOC tag.</summary>
    private static (string locPrefix, string text) SplitLoc(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return ("", "");
        if (raw.StartsWith("<LOC ", StringComparison.OrdinalIgnoreCase))
        {
            int end = raw.IndexOf('>');
            if (end > 0) return (raw.Substring(0, end + 1), raw.Substring(end + 1));
        }
        return ("", raw);
    }

    private void PopulateFromMap(ScMap map)
    {
        _isPopulating = true;
        var (nameLoc, nameText) = SplitLoc(map.Info.Name);
        _mapNameLocPrefix = nameLoc;
        MapName = nameText;
        var (descLoc, descText) = SplitLoc(map.Info.Description);
        _mapDescLocPrefix = descLoc;
        MapDescription = descText;
        MapWidth = map.Heightmap.Width;
        MapHeight = map.Heightmap.Height;
        MapVersionMinor = map.VersionMinor;

        var l = map.Lighting;
        LightingMultiplier = l.LightingMultiplier;
        SunDirX = l.SunDirection.X;
        SunDirY = l.SunDirection.Y;
        SunDirZ = l.SunDirection.Z;
        SunAmbienceR = l.SunAmbience.X;
        SunAmbienceG = l.SunAmbience.Y;
        SunAmbienceB = l.SunAmbience.Z;
        SunColorR = l.SunColor.X;
        SunColorG = l.SunColor.Y;
        SunColorB = l.SunColor.Z;
        Bloom = l.Bloom;
        FogStart = l.FogStart;
        FogEnd = l.FogEnd;

        HasWater = map.Water.HasWater;
        WaterElevation = map.Water.Elevation;
        WaterElevationDeep = map.Water.ElevationDeep;
        WaterElevationAbyss = map.Water.ElevationAbyss;

        MarkerCount = map.Markers.Count;
        PropCount = map.Props.Count;
        DecalCount = map.Decals.Count;

        OnPropertyChanged(nameof(HasMap));
        _isPopulating = false;
    }

    private void ApplyToMap(ScMap map)
    {
        // Re-attach the original LOC prefix so vanilla maps keep their lobby translation key.
        map.Info.Name = _mapNameLocPrefix + MapName;
        map.Info.Description = _mapDescLocPrefix + MapDescription;
        SyncLightingToMap();
        SyncWaterToMap();
    }

    private void SyncLightingToMap()
    {
        if (CurrentMap == null || _isPopulating) return;
        var l = CurrentMap.Lighting;
        l.LightingMultiplier = (float)LightingMultiplier;
        l.SunDirection = new Vector3((float)SunDirX, (float)SunDirY, (float)SunDirZ);
        l.SunAmbience = new Vector3((float)SunAmbienceR, (float)SunAmbienceG, (float)SunAmbienceB);
        l.SunColor = new Vector3((float)SunColorR, (float)SunColorG, (float)SunColorB);
        l.Bloom = (float)Bloom;
        l.FogStart = (float)FogStart;
        l.FogEnd = (float)FogEnd;
    }

    private void SyncWaterToMap()
    {
        if (CurrentMap == null || _isPopulating) return;
        CurrentMap.Water.HasWater = HasWater;
        CurrentMap.Water.Elevation = (float)WaterElevation;
        CurrentMap.Water.ElevationDeep = (float)WaterElevationDeep;
        CurrentMap.Water.ElevationAbyss = (float)WaterElevationAbyss;
    }

    /// <summary>
    /// Reverse direction sync — pull lighting values from <see cref="CurrentMap"/>.Lighting back
    /// into VM observable properties. Called after Undo/Redo so the UI sliders + palette swatches
    /// reflect the restored map state.
    /// </summary>
    private void SyncLightingFromMap()
    {
        if (CurrentMap == null) return;
        _isPopulating = true;
        var l = CurrentMap.Lighting;
        LightingMultiplier = l.LightingMultiplier;
        SunDirX = l.SunDirection.X;
        SunDirY = l.SunDirection.Y;
        SunDirZ = l.SunDirection.Z;
        SunAmbienceR = l.SunAmbience.X;
        SunAmbienceG = l.SunAmbience.Y;
        SunAmbienceB = l.SunAmbience.Z;
        SunColorR = l.SunColor.X;
        SunColorG = l.SunColor.Y;
        SunColorB = l.SunColor.Z;
        Bloom = l.Bloom;
        FogStart = l.FogStart;
        FogEnd = l.FogEnd;
        _isPopulating = false;
    }

    // Smart-texturing stroke state — orchestration sits in the VM because it needs the texture
    // library to auto-assign categories that the map doesn't have yet.
    private bool _smartStrokeActive;
    private ushort[]? _smartBeforeHm;
    private byte[]? _smartBeforeLow;
    private byte[]? _smartBeforeHigh;
    private TerrainTextureSnapshot[]? _smartBeforeTextures;
    private Dictionary<SmartBrushTool.TerrainCategory, int>? _smartCategoryStrata;

    // Brush tool integration — routes between the heightmap brush, the splatmap (texture) brush,
    // and the Smart variant (heightmap + automatic texture painting).
    public void BeginBrushStroke(float x, float z)
    {
        if (CurrentMap == null) return;
        if (!IsBrush3DActive) return;
        if (ActiveBrushKind == Services.Viewport3DToolKind.TextureBrush)
        {
            SplatmapTool.Radius = (float)BrushRadius;
            SplatmapTool.Strength = (float)BrushStrength;
            SplatmapTool.BeginStroke(CurrentMap, x, z);
        }
        else
        {
            if (IsSmartTexturingEnabled) BeginSmartStroke();
            BrushTool.Mode = (BrushMode)BrushModeIndex;
            BrushTool.Radius = (float)BrushRadius;
            BrushTool.Strength = (float)BrushStrength;
            BrushTool.BeginStroke(CurrentMap.Heightmap, x, z);
        }
        IsBrushActive = true;
    }

    public void ApplyBrush(float x, float z)
    {
        if (!IsBrushActive) return;
        if (ActiveBrushKind == Services.Viewport3DToolKind.TextureBrush)
        {
            SplatmapTool.ApplyBrush(x, z);
            TexturesVersion++;
        }
        else
        {
            BrushTool.ApplyBrush(x, z);
            HeightmapVersion++;
            if (_smartStrokeActive && _smartCategoryStrata != null && CurrentMap != null)
            {
                SmartBrushTool.ApplyTexturePass(CurrentMap, _smartCategoryStrata,
                    x, z, (float)BrushRadius, (float)BrushStrength);
                TexturesVersion++;
            }
        }
    }

    public void EndBrushStroke()
    {
        if (!IsBrushActive) return;
        if (ActiveBrushKind == Services.Viewport3DToolKind.TextureBrush)
        {
            var op = SplatmapTool.EndStroke();
            if (op != null) UndoRedo.PushExecuted(op);
            TexturesVersion++;
        }
        else if (_smartStrokeActive)
        {
            BrushTool.EndStroke(); // discard heightmap-only op; we'll push a composite one
            EndSmartStroke();
            HeightmapVersion++;
            TexturesVersion++;
        }
        else
        {
            var op = BrushTool.EndStroke();
            if (op != null) UndoRedo.PushExecuted(op);
            HeightmapVersion++;
        }
        IsBrushActive = false;
    }

    /// <summary>Pre-flight for a Smart stroke: snapshot heightmap + splatmaps + texture slots,
    /// auto-assign missing categories from the library, resolve final category-to-strata mapping.</summary>
    private void BeginSmartStroke()
    {
        if (CurrentMap == null) return;
        _smartStrokeActive = true;
        _smartBeforeHm = (ushort[])CurrentMap.Heightmap.Data.Clone();
        _smartBeforeLow = (byte[])CurrentMap.TextureMaskLow.DdsData.Clone();
        _smartBeforeHigh = (byte[])CurrentMap.TextureMaskHigh.DdsData.Clone();
        _smartBeforeTextures = SmartBrushTool.SnapshotTextures(CurrentMap);
        AutoAssignSmartCategories();
        _smartCategoryStrata = SmartBrushTool.ResolveCategoryStrata(CurrentMap);
    }

    /// <summary>For each Smart category not yet present in the map's strata, find a library
    /// texture explicitly classified to that category in <see cref="TextureCategoryTable"/> and
    /// assign it to a free strata slot. Skipped if the map already has 8 textures or the library
    /// has no matching entry for that category.</summary>
    private void AutoAssignSmartCategories()
    {
        if (CurrentMap == null) return;
        var existing = SmartBrushTool.ResolveCategoryStrata(CurrentMap);
        var library = GetTextureLibrary();
        int upper = Math.Min(8, CurrentMap.TerrainTextures.Length - 1);

        // Iterate every category the table knows about; assign one library texture per missing.
        foreach (var cat in Enum.GetValues<SmartBrushTool.TerrainCategory>())
        {
            if (existing.ContainsKey(cat)) continue; // map already covers this category
            var match = library.Entries.FirstOrDefault(e =>
                TextureCategoryTable.Classify(e.AlbedoPath) == cat);
            if (match == null) continue;
            int freeSlot = -1;
            for (int i = 1; i <= upper; i++)
            {
                if (string.IsNullOrEmpty(CurrentMap.TerrainTextures[i].AlbedoPath))
                {
                    freeSlot = i; break;
                }
            }
            if (freeSlot < 1) continue; // map full
            var slot = CurrentMap.TerrainTextures[freeSlot];
            slot.AlbedoPath = match.AlbedoPath;
            if (!string.IsNullOrEmpty(match.NormalPath)) slot.NormalPath = match.NormalPath;
            if (slot.AlbedoScale <= 0f) slot.AlbedoScale = 10f;
            if (slot.NormalScale <= 0f) slot.NormalScale = 10f;
        }
    }

    /// <summary>Build the final SmartBrushOp covering heightmap + splatmaps + texture-slot changes
    /// and push it onto the undo stack. Refreshes the palette so the new strata appear in "Map".</summary>
    private void EndSmartStroke()
    {
        if (CurrentMap == null || _smartBeforeHm == null || _smartBeforeLow == null
            || _smartBeforeHigh == null || _smartBeforeTextures == null) { _smartStrokeActive = false; return; }

        var afterHm = (ushort[])CurrentMap.Heightmap.Data.Clone();
        var afterLow = (byte[])CurrentMap.TextureMaskLow.DdsData.Clone();
        var afterHigh = (byte[])CurrentMap.TextureMaskHigh.DdsData.Clone();
        var afterTextures = SmartBrushTool.SnapshotTextures(CurrentMap);

        bool changed = !SmartBrushTool.ArraysEqual(_smartBeforeHm, afterHm)
                    || !SmartBrushTool.ArraysEqual(_smartBeforeLow, afterLow)
                    || !SmartBrushTool.ArraysEqual(_smartBeforeHigh, afterHigh)
                    || !SmartBrushTool.TexturesEqual(_smartBeforeTextures, afterTextures);
        if (changed)
        {
            var op = new SmartBrushOp(CurrentMap,
                _smartBeforeHm, _smartBeforeLow, _smartBeforeHigh, _smartBeforeTextures,
                afterHm, afterLow, afterHigh, afterTextures);
            UndoRedo.PushExecuted(op);
            RefreshViewport3DCategories();
        }
        _smartStrokeActive = false;
    }

    [RelayCommand]
    public void Undo()
    {
        UndoRedo.Undo();
        AfterUndoRedo();
        StatusText = UndoRedo.CanUndo ? $"Undo: {UndoRedo.UndoDescription}" : "Nothing to undo";
    }

    [RelayCommand]
    public void Redo()
    {
        UndoRedo.Redo();
        AfterUndoRedo();
        StatusText = UndoRedo.CanRedo ? $"Redo: {UndoRedo.RedoDescription}" : "Nothing to redo";
    }

    /// <summary>Shared post-step for Undo and Redo: bump every change-version (cheap idempotent
    /// re-uploads, no per-op-type detection needed), sync VM lighting props back from map, refresh
    /// palette categories so live thumbnails follow strata/lighting changes.</summary>
    private void AfterUndoRedo()
    {
        HeightmapVersion++;
        MarkerVersion++;
        PropVersion++;
        TexturesVersion++;
        if (CurrentMap != null)
        {
            MarkerCount = CurrentMap.Markers.Count;
            PropCount = CurrentMap.Props.Count;
        }
        SyncLightingFromMap();
        SyncWaterFromMap();
        RefreshViewport3DCategories();
    }

    // Marker selection sync: keep the Amount overlay in sync with the selected marker.
    partial void OnSelectedMarkerChanged(Marker? value)
    {
        _isPopulating = true;
        SelectedMarkerAmount = value?.Amount ?? 0;
        _isPopulating = false;
        OnPropertyChanged(nameof(IsMassMarkerSelected));
    }

    partial void OnSelectedMarkerAmountChanged(double value)
    {
        if (_isPopulating || SelectedMarker == null) return;
        SelectedMarker.Amount = (float)value;
    }

    [RelayCommand]
    public void DeleteSelectedMarker()
    {
        if (CurrentMap == null || SelectedMarker == null) return;
        var op = new RemoveMarkerOp(CurrentMap.Markers, SelectedMarker);
        op.Execute();
        UndoRedo.PushExecuted(op);
        SelectedMarker = null;
        MarkerCount = CurrentMap.Markers.Count;
        MarkerVersion++;
        StatusText = "Marker deleted";
    }

    public void AddMarker(MarkerType type, float x, float z)
    {
        if (CurrentMap == null) return;
        // Resource markers must align with the build grid so the in-game extractor footprint
        // covers full cells; without this, mass deposits look offset from the build ring.
        (x, z) = BuildGridSnap.Snap(type, x, z);
        float y = CurrentMap.Heightmap.GetWorldHeight(
            Math.Clamp((int)x, 0, CurrentMap.Heightmap.Width),
            Math.Clamp((int)z, 0, CurrentMap.Heightmap.Height));

        // Find the first free slot for this naming scheme. Using Count+1 was buggy after a
        // deletion (left a gap then re-issued a colliding number).
        string seed = type switch
        {
            MarkerType.Mass => "Mass 00",
            MarkerType.Hydrocarbon => "Hydro 00",
            MarkerType.BlankMarker => "ARMY_1",
            _ => $"{type} 00",
        };
        string name = MakeUniqueMarkerName(seed);

        var marker = new Marker
        {
            Name = name,
            Type = type,
            Position = new Vector3(x, y, z),
            Resource = type is MarkerType.Mass or MarkerType.Hydrocarbon,
            Amount = type == MarkerType.Mass ? 100f : type == MarkerType.Hydrocarbon ? 100f : 0f,
            Color = "ff800080"
        };
        var op = new AddMarkerOp(CurrentMap.Markers, marker);
        op.Execute();
        UndoRedo.PushExecuted(op);
        MarkerCount = CurrentMap.Markers.Count;
        MarkerVersion++;
        SelectedMarker = marker;
        StatusText = $"Added {type}: {name}";
    }

    /// <summary>Bumped when the prop list changes so views can refresh (parallel to MarkerVersion).</summary>
    [ObservableProperty] private int _propVersion;

    // === Prop brush ===
    [ObservableProperty] private bool _isPropBrushActive;
    [ObservableProperty] private int _propBrushPresetIndex;
    [ObservableProperty] private double _propBrushRadius = 8;
    [ObservableProperty] private double _propBrushDensity = 4;

    public IReadOnlyList<PropBrushPresets.Preset> PropBrushPresetList => PropBrushPresets.All;

    /// <summary>Two-level palette displayed at the bottom of the 3D viewport. Level 1 categories,
    /// level 2 tools. Selecting a tool routes to the appropriate brush implementation.
    /// Rebuilt whenever <see cref="GameData"/> is (re)initialized so per-biome texture categories
    /// reflect what's available in the current install.</summary>
    public IReadOnlyList<Services.Viewport3DCategory> Viewport3DCategories =>
        _viewport3DCategories ??= BuildViewport3DCategories();

    private IReadOnlyList<Services.Viewport3DCategory>? _viewport3DCategories;
    private Services.TextureLibraryService? _textureLibrary;

    /// <summary>Public access to the scanned texture library — used by the View to resolve
    /// thumbnails for the "Map full" replacement dialog.</summary>
    public Services.TextureLibraryService GetTextureLibrary()
        => _textureLibrary ??= new Services.TextureLibraryService(GameData);

    private IReadOnlyList<Services.Viewport3DCategory> BuildViewport3DCategories()
    {
        var list = new List<Services.Viewport3DCategory>
        {
            Services.Viewport3DCatalog.TerrainCategory,
            BuildLightingCategory(),
            BuildWaterCategory(),
        };
        // "Map" category: the strata currently assigned to this map. Lets the user pick which
        // strata to paint without needing to navigate the library (and shows what's actually used).
        var mapCategory = BuildMapStrataCategory();
        if (mapCategory != null) list.Add(mapCategory);

        _textureLibrary ??= new Services.TextureLibraryService(GameData);
        // One palette category per biome found in env.scd. Categories appear in alphabetical biome
        // order; within each, textures are sorted by their base name for predictability.
        foreach (var biome in _textureLibrary.Entries.GroupBy(e => e.Biome).OrderBy(g => g.Key))
        {
            var tools = biome
                .OrderBy(e => e.Name)
                .Select(e => new Services.Viewport3DTool(
                    Services.Viewport3DToolKind.TextureBrush,
                    e.Name,
                    e.Thumbnail,
                    e.AlbedoPath))
                .ToList();
            list.Add(new Services.Viewport3DCategory(Services.TextureLibraryService.PrettyBiome(biome.Key), tools));
        }
        return list;
    }

    /// <summary>
    /// Build the "Map" palette category — always 8 slots so the user sees the full picture. Slots
    /// beyond the map's paintable range (v53 maps cap below 8) are clearly marked unavailable.
    /// Payload is the strata index (int) for direct selection; clicking unavailable slots is a no-op.
    /// </summary>
    private Services.Viewport3DCategory? BuildMapStrataCategory()
    {
        if (CurrentMap == null) return null;
        _textureLibrary ??= new Services.TextureLibraryService(GameData);
        var thumbsByPath = _textureLibrary.Entries.ToDictionary(
            e => e.AlbedoPath, e => e.Thumbnail,
            StringComparer.OrdinalIgnoreCase);

        int maxPaintable = MaxPaintableStrata();
        var tools = new List<Services.Viewport3DTool>();
        for (int strata = 1; strata <= 8; strata++)
        {
            if (strata > maxPaintable)
            {
                // v53 / array-length limit: show the slot but mark it unreachable.
                tools.Add(new Services.Viewport3DTool(
                    Services.Viewport3DToolKind.TextureBrush,
                    $"v53 — N/A",
                    Services.MapStrataPlaceholder.GetUnavailableIcon(strata),
                    -strata)); // negative payload = unavailable marker
                continue;
            }

            var path = CurrentMap.TerrainTextures[strata].AlbedoPath ?? "";
            var label = $"Strata {strata}";
            var normalized = path.Replace('\\', '/').ToLowerInvariant();
            Avalonia.Media.Imaging.Bitmap? thumb = null;
            if (!string.IsNullOrEmpty(normalized) && thumbsByPath.TryGetValue(normalized, out var t))
            {
                thumb = t;
                label = $"#{strata} " + System.IO.Path.GetFileNameWithoutExtension(normalized);
            }
            thumb ??= Services.MapStrataPlaceholder.GetIcon(strata, !string.IsNullOrEmpty(path));
            tools.Add(new Services.Viewport3DTool(
                Services.Viewport3DToolKind.TextureBrush, label, thumb, strata));
        }
        return new Services.Viewport3DCategory("Map", tools);
    }

    /// <summary>
    /// Build the "Lighting" palette category. Color tools (Sun Color / Ambience) carry a live
    /// swatch reflecting the current values; everything else uses a static themed icon. Click
    /// routing is handled in <see cref="OnSelectedViewport3DToolChanged"/>.
    /// </summary>
    private Services.Viewport3DCategory BuildLightingCategory()
    {
        Services.Viewport3DTool MakeLightingTool(Services.LightingSetting setting, string label, Avalonia.Media.Imaging.Bitmap icon)
            => new(Services.Viewport3DToolKind.LightingSetting, label, icon, setting);

        var sunSwatch = Services.LightingIcons.RenderColorSwatch((float)SunColorR, (float)SunColorG, (float)SunColorB);
        var ambSwatch = Services.LightingIcons.RenderColorSwatch((float)SunAmbienceR, (float)SunAmbienceG, (float)SunAmbienceB);
        return new Services.Viewport3DCategory("Lighting", new[]
        {
            MakeLightingTool(Services.LightingSetting.SunColor,     "Sun Color",     sunSwatch),
            MakeLightingTool(Services.LightingSetting.Ambience,     "Ambience",      ambSwatch),
            MakeLightingTool(Services.LightingSetting.Multiplier,   "Multiplier",    Services.LightingIcons.Sun),
            MakeLightingTool(Services.LightingSetting.SunDirection, "Sun Direction", Services.LightingIcons.Arrow3D),
            MakeLightingTool(Services.LightingSetting.Bloom,        "Bloom",         Services.LightingIcons.Glow),
            MakeLightingTool(Services.LightingSetting.FogStart,     "Fog Start",     Services.LightingIcons.Fog),
            MakeLightingTool(Services.LightingSetting.FogEnd,       "Fog End",       Services.LightingIcons.Fog),
        });
    }

    /// <summary>Fired when the user clicks a Lighting tool in the 3D palette. The View handles
    /// the popup (color picker or slider) and writes new values back to VM properties.</summary>
    public event Action<Services.LightingSetting>? LightingSettingActivated;

    /// <summary>
    /// Build the "Water" palette category — toggle + 3 elevation tiers (Surface / Deep / Abyss).
    /// The Enable card shows a state-aware icon (filled droplet when on, struck-through outline
    /// when off) so the user sees the current state without opening anything.
    /// </summary>
    private Services.Viewport3DCategory BuildWaterCategory()
    {
        Services.Viewport3DTool Make(Services.WaterSetting s, string label, Avalonia.Media.Imaging.Bitmap icon)
            => new(Services.Viewport3DToolKind.WaterSetting, label, icon, s);

        return new Services.Viewport3DCategory("Water", new[]
        {
            Make(Services.WaterSetting.Enable,  HasWater ? "Water: On" : "Water: Off", Services.WaterIcons.GetEnable(HasWater)),
            Make(Services.WaterSetting.Surface, "Surface", Services.WaterIcons.Surface),
            Make(Services.WaterSetting.Deep,    "Deep",    Services.WaterIcons.Deep),
            Make(Services.WaterSetting.Abyss,   "Abyss",   Services.WaterIcons.Abyss),
        });
    }

    /// <summary>Fired when the user clicks a Water tool in the 3D palette.</summary>
    public event Action<Services.WaterSetting>? WaterSettingActivated;

    /// <summary>
    /// Highest strata index that can be painted into the splatmap on this map. v53 maps reserve
    /// the last strata as the macro/upper layer (not blendable), so for an N-strata v53 map only
    /// 1..N-2 are paintable. v56+ caps at 8 regardless.
    /// </summary>
    public int MaxPaintableStrata()
    {
        if (CurrentMap == null) return 0;
        int len = CurrentMap.TerrainTextures.Length;
        // v53 macro convention: stratumCount ≤ 6 → last is upper/macro. v56+ uses 10 strata.
        if (len <= 6 && len > 1) return Math.Min(8, len - 2);
        return Math.Min(8, len - 1);
    }

    /// <summary>Force the palette to rebuild — call after loading a map (strata changed) or after
    /// game data is reinitialized (library content changed). Preserves which category AND tool are
    /// selected (matched by Name and by Kind+Payload) so a category/tool rebuild doesn't visually
    /// drop the user's brush selection mid-session.</summary>
    public void RefreshViewport3DCategories()
    {
        var prevCategoryName = SelectedViewport3DCategory?.Name;
        var prevToolKind = SelectedViewport3DTool?.Kind;
        var prevToolPayload = SelectedViewport3DTool?.Payload;

        _viewport3DCategories = null;
        OnPropertyChanged(nameof(Viewport3DCategories));
        if (prevCategoryName == null) return;

        var matchCat = Viewport3DCategories.FirstOrDefault(c => c.Name == prevCategoryName);
        if (matchCat == null) return;
        if (!ReferenceEquals(matchCat, SelectedViewport3DCategory))
        {
            _preserveToolDuringCategoryChange = true;
            SelectedViewport3DCategory = matchCat;
            _preserveToolDuringCategoryChange = false;
        }

        // Restore the tool reference for persistent brush kinds (terrain / texture). Lighting and
        // Water tools are intentionally one-shot — they auto-deselect after firing their popup,
        // so restoring them across a refresh would re-trigger the popup.
        if (prevToolKind is Services.Viewport3DToolKind.TerrainBrush
                          or Services.Viewport3DToolKind.TextureBrush)
        {
            var matchTool = matchCat.Tools.FirstOrDefault(t =>
                t.Kind == prevToolKind && Equals(t.Payload, prevToolPayload));
            if (matchTool != null && !ReferenceEquals(matchTool, SelectedViewport3DTool))
            {
                _isRestoringTool = true;
                SelectedViewport3DTool = matchTool;
                _isRestoringTool = false;
            }
        }
    }

    private bool _preserveToolDuringCategoryChange;
    /// <summary>True while <see cref="RefreshViewport3DCategories"/> is restoring the tool reference
    /// after a category rebuild — short-circuits OnSelectedViewport3DToolChanged so the brush isn't
    /// re-armed with side effects (auto-assign, popup, etc.).</summary>
    private bool _isRestoringTool;

    [ObservableProperty] private Services.Viewport3DCategory? _selectedViewport3DCategory;
    [ObservableProperty] private Services.Viewport3DTool? _selectedViewport3DTool;

    public IReadOnlyList<Services.Viewport3DTool> ToolsInSelected3DCategory =>
        SelectedViewport3DCategory?.Tools ?? Array.Empty<Services.Viewport3DTool>();

    /// <summary>True when a palette tool is picked — left-click in the 3D viewport only paints when
    /// this is true. Prevents "I navigated to Textures but no specific texture, and a click painted
    /// something" surprises.</summary>
    public bool IsBrush3DActive => SelectedViewport3DTool != null;

    /// <summary>Which brush kind is currently active in the 3D view. Routes <see cref="BeginBrushStroke"/>
    /// to either the heightmap or splatmap brush implementation.</summary>
    [ObservableProperty] private Services.Viewport3DToolKind _activeBrushKind = Services.Viewport3DToolKind.TerrainBrush;

    /// <summary>Splatmap brush — painted strata is resolved from the texture path via
    /// <see cref="AssignStrataForTexture"/> when a texture tool is picked.</summary>
    public SplatmapBrushTool SplatmapTool { get; } = new();

    partial void OnSelectedViewport3DCategoryChanged(Services.Viewport3DCategory? value)
    {
        OnPropertyChanged(nameof(ToolsInSelected3DCategory));
        if (_preserveToolDuringCategoryChange) return;
        // Reset the per-tool selection when switching categories so the brush state is unambiguous.
        SelectedViewport3DTool = null;
    }

    partial void OnSelectedViewport3DToolChanging(Services.Viewport3DTool? value)
    {
        // Property notification for the computed IsBrush3DActive happens AFTER the new value is set;
        // we listen to "Changed" below to fire the second notification too.
    }

    /// <summary>
    /// Fired when the user picks a library texture but the map has no free strata to put it in.
    /// The View handles this by showing a chooser dialog and calling <see cref="ApplyStrataReplacement"/>
    /// (or doing nothing if the user cancels).
    /// </summary>
    public event Action<string>? TextureAssignmentNeedsUser;

    partial void OnSelectedViewport3DToolChanged(Services.Viewport3DTool? value)
    {
        OnPropertyChanged(nameof(IsBrush3DActive));
        if (_isRestoringTool) return; // restoration after rebuild — brush state already correct
        if (value == null) return;

        // Lighting / Water tools are NOT brushes — they pop up an editor (or toggle a state) and
        // immediately deselect so the 3D view isn't accidentally armed.
        if (value.Kind == Services.Viewport3DToolKind.LightingSetting)
        {
            if (value.Payload is Services.LightingSetting setting)
                LightingSettingActivated?.Invoke(setting);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectedViewport3DTool = null);
            return;
        }
        if (value.Kind == Services.Viewport3DToolKind.WaterSetting)
        {
            if (value.Payload is Services.WaterSetting wsetting)
                WaterSettingActivated?.Invoke(wsetting);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectedViewport3DTool = null);
            return;
        }

        ActiveBrushKind = value.Kind;
        switch (value.Kind)
        {
            case Services.Viewport3DToolKind.TerrainBrush:
                if (value.Payload is int modeIndex) BrushModeIndex = modeIndex;
                break;
            case Services.Viewport3DToolKind.TextureBrush:
                if (value.Payload is int directStrata)
                {
                    if (directStrata < 0)
                    {
                        // Negative payload = unreachable slot (v53 limit).
                        StatusText = $"Strata {-directStrata} not available on this map (v53 limit).";
                        return;
                    }
                    SplatmapTool.StrataIndex = directStrata;
                    var path = CurrentMap?.TerrainTextures[directStrata].AlbedoPath ?? "";
                    var label = string.IsNullOrEmpty(path) ? "(empty)" : System.IO.Path.GetFileNameWithoutExtension(path);
                    StatusText = $"Painting strata {directStrata} ({label})";
                }
                else if (value.Payload is string albedoPath)
                {
                    int strata = TryAssignWithoutOverwrite(albedoPath);
                    if (strata >= 1)
                    {
                        SplatmapTool.StrataIndex = strata;
                    }
                    else
                    {
                        // Map full → ask the user which strata to replace via the View dialog.
                        TextureAssignmentNeedsUser?.Invoke(albedoPath);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Non-destructive texture assignment. Returns the strata index to paint, or 0 if the map has
    /// no free strata AND no existing match (caller must surface a chooser dialog).
    /// </summary>
    private int TryAssignWithoutOverwrite(string albedoPath)
    {
        if (CurrentMap == null) return 0;
        var textures = CurrentMap.TerrainTextures;
        int upper = MaxPaintableStrata();

        string Normalize(string? p) => (p ?? "").Replace('\\', '/').ToLowerInvariant();
        var target = Normalize(albedoPath);

        for (int i = 1; i <= upper; i++)
        {
            if (Normalize(textures[i].AlbedoPath) == target)
            {
                StatusText = $"Switched to strata {i} ({System.IO.Path.GetFileNameWithoutExtension(albedoPath)})";
                return i;
            }
        }
        for (int i = 1; i <= upper; i++)
        {
            if (string.IsNullOrEmpty(textures[i].AlbedoPath))
            {
                AssignTextureToStrata(textures, i, albedoPath);
                StatusText = $"Assigned {System.IO.Path.GetFileNameWithoutExtension(albedoPath)} → strata {i}";
                RefreshViewport3DCategories();
                return i;
            }
        }
        return 0;
    }

    /// <summary>
    /// Called by the View after the user picks a strata in the "Map full" replacement dialog.
    /// Overwrites the chosen strata with the new texture and switches active painting to it.
    /// </summary>
    public void ApplyStrataReplacement(int strata, string albedoPath)
    {
        if (CurrentMap == null) return;
        int upper = MaxPaintableStrata();
        if (strata < 1 || strata > upper) return;
        var textures = CurrentMap.TerrainTextures;
        var replaced = System.IO.Path.GetFileNameWithoutExtension(textures[strata].AlbedoPath ?? "");
        AssignTextureToStrata(textures, strata, albedoPath);
        SplatmapTool.StrataIndex = strata;
        StatusText = $"Strata {strata}: {replaced} → {System.IO.Path.GetFileNameWithoutExtension(albedoPath)}";
        RefreshViewport3DCategories();
    }

    private void AssignTextureToStrata(Core.Models.TerrainTexture[] textures, int strata, string albedoPath)
    {
        var slot = textures[strata];
        // Snapshot before mutating so the op can restore exact prior state on Undo.
        string beforeAlbedo = slot.AlbedoPath ?? "";
        string beforeNormal = slot.NormalPath ?? "";
        float beforeAlbedoScale = slot.AlbedoScale;
        float beforeNormalScale = slot.NormalScale;

        // Compute after-state without mutating yet — the op's Execute() will apply it.
        string afterAlbedo = albedoPath;
        string afterNormal = beforeNormal;
        var derivedNormal = albedoPath.Replace("_albedo.dds", "_normal.dds", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(derivedNormal, albedoPath, StringComparison.OrdinalIgnoreCase)
            && GameData.IsInitialized && GameData.LoadFile(derivedNormal) != null)
        {
            afterNormal = derivedNormal;
        }
        float afterAlbedoScale = beforeAlbedoScale <= 0f ? 1f : beforeAlbedoScale;
        float afterNormalScale = beforeNormalScale <= 0f ? 1f : beforeNormalScale;

        var op = new TerrainTextureAssignOp(slot,
            beforeAlbedo, beforeNormal, beforeAlbedoScale, beforeNormalScale,
            afterAlbedo, afterNormal, afterAlbedoScale, afterNormalScale,
            $"Assign strata {strata}");
        op.Execute();
        UndoRedo.PushExecuted(op);
        TexturesVersion++;
    }

    // === Palette (icon-based menu in the bottom of the 2D view) ===
    // The displayed list bundles three sources: decoration props (env.scd icons), placeable units
    // (units.scd icons), and procedurally-rendered markers. "Markers" goes first so the most
    // common map-building task (placing Mass / Hydro / spawns) is one click away.
    public IReadOnlyList<PropCategory> PropCategories { get; } = BuildCombinedCategories();

    private static IReadOnlyList<PropCategory> BuildCombinedCategories()
    {
        var combined = new List<PropCategory>();
        combined.AddRange(Services.MarkerCatalog.All);
        combined.AddRange(PropCatalog.All);
        return combined;
    }

    [ObservableProperty] private PropCategory? _selectedPropCategory;
    [ObservableProperty] private PropEntry? _selectedPropEntry;

    public IReadOnlyList<PropEntry> ItemsInSelectedCategory =>
        SelectedPropCategory?.Items ?? System.Array.Empty<PropEntry>();

    /// <summary>True when single-click placement is the appropriate action: a palette entry is
    /// picked AND the brush isn't effectively painting (the brush only paints decoration props).</summary>
    public bool IsSinglePlaceActive => SelectedPropEntry != null && !IsBrushEffectivelyActive;

    /// <summary>True when the brush is on AND the active palette entry (if any) is a decoration
    /// prop the brush can stamp. Non-Prop entries (markers, units) bypass the brush regardless of
    /// the toggle — so the cursor circle doesn't mislead the user into thinking it'll paint them.</summary>
    public bool IsBrushEffectivelyActive => IsPropBrushActive
        && (SelectedPropEntry == null || SelectedPropEntry.EntryKind == PaletteEntryKind.Prop);

    partial void OnSelectedPropCategoryChanged(PropCategory? value)
    {
        OnPropertyChanged(nameof(ItemsInSelectedCategory));
        // Reset the per-item selection when switching categories so the placement state is unambiguous.
        SelectedPropEntry = null;
    }

    partial void OnSelectedPropEntryChanged(PropEntry? value)
    {
        OnPropertyChanged(nameof(IsSinglePlaceActive));
        OnPropertyChanged(nameof(IsBrushEffectivelyActive));
    }

    partial void OnIsPropBrushActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSinglePlaceActive));
        OnPropertyChanged(nameof(IsBrushEffectivelyActive));
    }

    /// <summary>
    /// Drop the currently-selected catalog entry at (x, z). Routes by EntryKind:
    ///   Prop   → adds a Prop to ScMap.Props
    ///   Unit   → adds a UnitSpawn under NEUTRAL_CIVILIAN
    ///   Marker → adds a Marker to ScMap.Markers (resource markers snapped to build grid)
    /// Y is clamped to terrain in every case.
    /// </summary>
    public void PlaceSelectedPropAt(float x, float z)
    {
        if (CurrentMap == null || SelectedPropEntry == null) return;
        switch (SelectedPropEntry.EntryKind)
        {
            case PaletteEntryKind.Marker when SelectedPropEntry.MarkerKind.HasValue:
                AddMarker(SelectedPropEntry.MarkerKind.Value, x, z);
                break;
            case PaletteEntryKind.Unit:
                PlaceUnitAt(SelectedPropEntry, x, z);
                break;
            default:
                PlacePropAt(SelectedPropEntry, x, z);
                break;
        }
    }

    private void PlacePropAt(Rendering.PropEntry entry, float x, float z)
    {
        if (CurrentMap == null) return;
        float y = GroundClampService.SampleHeight(CurrentMap.Heightmap, x, z);
        var prop = new Prop
        {
            BlueprintPath = entry.BlueprintPath,
            Position = new System.Numerics.Vector3(x, y, z),
            RotationX = new System.Numerics.Vector3(1, 0, 0),
            RotationY = new System.Numerics.Vector3(0, 1, 0),
            RotationZ = new System.Numerics.Vector3(0, 0, 1),
            Scale = new System.Numerics.Vector3(1, 1, 1),
        };
        var op = new AddPropOp(CurrentMap.Props, prop);
        op.Execute();
        UndoRedo.PushExecuted(op);
        PropCount = CurrentMap.Props.Count;
        PropVersion++;
        StatusText = $"Placed {entry.DisplayName}";
    }

    private void PlaceUnitAt(Rendering.PropEntry entry, float x, float z)
    {
        if (CurrentMap == null) return;
        // Unit blueprintPath is "/units/UEB1101/UEB1101_unit.bp"; we save the bare ID ("UEB1101")
        // — that's the value stored in UnitSpawn.BlueprintId / written into _save.lua.
        var idStart = entry.BlueprintPath.LastIndexOf('/') + 1;
        var idEnd = entry.BlueprintPath.IndexOf("_unit.bp", StringComparison.OrdinalIgnoreCase);
        var unitId = (idStart > 0 && idEnd > idStart)
            ? entry.BlueprintPath.Substring(idStart, idEnd - idStart)
            : entry.DisplayName;

        // Default army for editor-placed units = NEUTRAL_CIVILIAN. Create it if the map doesn't have it yet.
        var army = CurrentMap.Info.Armies.FirstOrDefault(a =>
            string.Equals(a.Name, "NEUTRAL_CIVILIAN", StringComparison.OrdinalIgnoreCase));
        if (army == null)
        {
            army = new Army { Name = "NEUTRAL_CIVILIAN" };
            CurrentMap.Info.Armies.Add(army);
        }

        float y = GroundClampService.SampleHeight(CurrentMap.Heightmap, x, z);
        // Unique local name within the INITIAL units list. UNIT_NN convention matches vanilla.
        int n = 0;
        while (army.InitialUnits.Any(u => u.Name == $"UNIT_{n}")) n++;

        var spawn = new UnitSpawn
        {
            Name = $"UNIT_{n}",
            BlueprintId = unitId,
            Position = new System.Numerics.Vector3(x, y, z),
            Orientation = System.Numerics.Vector3.Zero,
        };
        var op = new AddUnitSpawnOp(army.InitialUnits, spawn);
        op.Execute();
        UndoRedo.PushExecuted(op);
        MarkerVersion++; // triggers 2D refresh (DrawInitialUnits reads from Armies)
        StatusText = $"Placed unit {unitId} in NEUTRAL_CIVILIAN";
    }

    private readonly Random _propBrushRng = new();
    private System.Numerics.Vector2? _lastBrushStamp;
    private List<Prop>? _activeBrushStroke;

    /// <summary>Begin a brush stroke (left click in brush mode). Subsequent ApplyPropBrush calls accumulate
    /// into the same stroke so a single Undo removes everything painted during this drag.
    /// The brush only operates on decoration props — if a marker or unit is the active palette entry,
    /// the stroke is a no-op (clicks still happen but stamp nothing).</summary>
    public void BeginPropBrush()
    {
        if (CurrentMap == null) return;
        if (SelectedPropEntry is { EntryKind: not PaletteEntryKind.Prop }) return;
        _activeBrushStroke = new List<Prop>();
        _lastBrushStamp = null;
    }

    /// <summary>Stamp props inside the brush radius at (mapX, mapZ). Called on press + on every
    /// drag movement that exceeds half the brush radius (to avoid stacking thousands of props).</summary>
    public void ApplyPropBrush(float mapX, float mapZ)
    {
        if (CurrentMap == null || _activeBrushStroke == null) return;
        // Two valid configurations: (a) a single Prop blueprint picked from the icon menu, or (b) a
        // legacy preset for random picks. Markers/units are filtered out at Begin so we never reach
        // this path with a non-Prop entry — guard anyway.
        var entry = SelectedPropEntry;
        string? singleBp = entry is { EntryKind: PaletteEntryKind.Prop } ? entry.BlueprintPath : null;
        var preset = (singleBp == null && PropBrushPresetIndex >= 0 && PropBrushPresetIndex < PropBrushPresets.All.Count)
            ? PropBrushPresets.All[PropBrushPresetIndex]
            : null;
        if (singleBp == null && preset == null) return;
        float r = (float)PropBrushRadius;
        if (_lastBrushStamp.HasValue)
        {
            float dx = mapX - _lastBrushStamp.Value.X;
            float dz = mapZ - _lastBrushStamp.Value.Y;
            if (dx * dx + dz * dz < (r * 0.5f) * (r * 0.5f)) return;
        }
        _lastBrushStamp = new System.Numerics.Vector2(mapX, mapZ);

        int count = Math.Max(1, (int)PropBrushDensity);
        for (int i = 0; i < count; i++)
        {
            // Random point in disc via rejection sampling (simple and uniform-enough for scatter).
            float ox, oz;
            do
            {
                ox = (float)(_propBrushRng.NextDouble() * 2 - 1);
                oz = (float)(_propBrushRng.NextDouble() * 2 - 1);
            } while (ox * ox + oz * oz > 1f);
            float px = mapX + ox * r;
            float pz = mapZ + oz * r;
            // Clamp to map bounds
            px = Math.Clamp(px, 0f, CurrentMap.Heightmap.Width);
            pz = Math.Clamp(pz, 0f, CurrentMap.Heightmap.Height);
            float py = GroundClampService.SampleHeight(CurrentMap.Heightmap, px, pz);

            // Random Y rotation (atomic rotation matrix is rebuilt from a yaw angle).
            double yaw = _propBrushRng.NextDouble() * Math.PI * 2;
            float cos = (float)Math.Cos(yaw);
            float sin = (float)Math.Sin(yaw);
            string bp = singleBp ?? preset!.Blueprints[_propBrushRng.Next(preset.Blueprints.Count)];
            float scale = 0.85f + (float)_propBrushRng.NextDouble() * 0.3f; // 0.85 .. 1.15

            var prop = new Prop
            {
                BlueprintPath = bp,
                Position = new System.Numerics.Vector3(px, py, pz),
                RotationX = new System.Numerics.Vector3(cos, 0, -sin),
                RotationY = new System.Numerics.Vector3(0, 1, 0),
                RotationZ = new System.Numerics.Vector3(sin, 0, cos),
                Scale = new System.Numerics.Vector3(scale, scale, scale),
            };
            CurrentMap.Props.Add(prop);
            _activeBrushStroke.Add(prop);
        }
        PropCount = CurrentMap.Props.Count;
        PropVersion++;
    }

    /// <summary>Finalize the active stroke and push a single undoable op covering everything painted.</summary>
    public void EndPropBrush()
    {
        if (CurrentMap == null || _activeBrushStroke == null) return;
        var stroke = _activeBrushStroke;
        _activeBrushStroke = null;
        _lastBrushStamp = null;
        if (stroke.Count == 0) return;
        // The props are already in the map; wrap them in an already-applied op for undo.
        // BatchAddPropsOp.Execute() would re-add → need a variant that captures without re-adding.
        // Simplest: remove from map (after capturing) then push (which will re-Execute and re-add).
        foreach (var p in stroke) CurrentMap.Props.Remove(p);
        var op = new BatchAddPropsOp(CurrentMap.Props, stroke);
        op.Execute();
        UndoRedo.PushExecuted(op);
        PropCount = CurrentMap.Props.Count;
        PropVersion++;
        StatusText = $"Brushed {stroke.Count} props";
    }

    /// <summary>Delete every prop in <paramref name="props"/> in one undoable batch.</summary>
    public int DeleteProps(IEnumerable<Prop> props)
    {
        if (CurrentMap == null) return 0;
        var toRemove = props.ToList();
        if (toRemove.Count == 0) return 0;
        var op = new BatchRemovePropsOp(CurrentMap.Props, toRemove);
        op.Execute();
        UndoRedo.PushExecuted(op);
        PropCount = CurrentMap.Props.Count;
        PropVersion++;
        StatusText = $"Deleted {toRemove.Count} props";
        return toRemove.Count;
    }

    /// <summary>
    /// Delete a heterogeneous selection (props + markers + units) in a single undoable bundle.
    /// Each type pushes its own batch op so per-type Undo semantics stay clean; one Ctrl+Z reverses
    /// the most recent type batch — call it again to walk back the rest. Status reports the total.
    /// </summary>
    public int DeleteMultiSelection(
        IReadOnlyCollection<Prop> props,
        IReadOnlyCollection<Marker> markers,
        IReadOnlyCollection<UnitSpawn> units)
    {
        if (CurrentMap == null) return 0;
        int total = 0;
        if (props.Count > 0)
        {
            var op = new BatchRemovePropsOp(CurrentMap.Props, props);
            op.Execute();
            UndoRedo.PushExecuted(op);
            PropCount = CurrentMap.Props.Count;
            PropVersion++;
            total += props.Count;
        }
        if (markers.Count > 0)
        {
            var op = new BatchRemoveOp<Marker>(CurrentMap.Markers, markers, "markers");
            op.Execute();
            UndoRedo.PushExecuted(op);
            MarkerCount = CurrentMap.Markers.Count;
            MarkerVersion++;
            total += markers.Count;
        }
        if (units.Count > 0)
        {
            // Group each selected unit with its owning army so Undo restores ownership.
            var pairs = new List<(Army army, UnitSpawn unit)>();
            foreach (var u in units)
            {
                var owner = CurrentMap.Info.Armies.FirstOrDefault(a => a.InitialUnits.Contains(u));
                if (owner != null) pairs.Add((owner, u));
            }
            if (pairs.Count > 0)
            {
                var op = new BatchRemoveUnitSpawnsOp(pairs);
                op.Execute();
                UndoRedo.PushExecuted(op);
                MarkerVersion++; // 2D redraws InitialUnits when MarkerVersion bumps
                total += pairs.Count;
            }
        }
        if (total > 0) StatusText = $"Deleted {total} elements";
        return total;
    }

    /// <summary>
    /// Snap props and ground-bound markers to the current heightmap surface. Undoable.
    /// Also called automatically before each save so the .scmap on disk never has floating props.
    /// </summary>
    public void ClampToGround()
    {
        if (CurrentMap == null) return;
        var op = new ClampToGroundOp(CurrentMap, includeMarkers: true);
        op.Execute();
        UndoRedo.PushExecuted(op);
        MarkerVersion++;
        StatusText = "Props and markers clamped to ground";
    }

    /// <summary>
    /// Resize the current map to a standard SupCom heightmap dimension (256 = 5km, 512 = 10km,
    /// 1024 = 20km, 2048 = 40km, 4096 = 80km). Heightmap, splatmaps and entity positions are
    /// resampled; normal/water aux DDS are reset to defaults. Undoable as a single op.
    /// </summary>
    public void ScaleMap(int newSize)
    {
        if (CurrentMap == null) return;
        if (CurrentMap.Heightmap.Width == newSize && CurrentMap.Heightmap.Height == newSize)
        {
            StatusText = $"Map already {newSize}×{newSize}";
            return;
        }
        var op = new MapScaleOp(CurrentMap, newSize);
        op.Execute();
        UndoRedo.PushExecuted(op);
        HeightmapVersion++;
        MarkerVersion++;
        TexturesVersion++;
        StatusText = $"Map scaled to {newSize}×{newSize}";
    }

    /// <summary>
    /// Apply a one-shot symmetry pattern: the chosen source region is replicated into the others,
    /// overwriting heightmap, splatmaps, markers, props, and per-army pre-placed units.
    /// </summary>
    public void ApplySymmetry(SymmetryPattern pattern, SymmetryRegion source)
    {
        if (CurrentMap == null) return;
        var op = new SymmetryApplyOp(CurrentMap, pattern, source);
        op.Execute();
        UndoRedo.PushExecuted(op);
        MarkerCount = CurrentMap.Markers.Count;
        PropCount = CurrentMap.Props.Count;
        MarkerVersion++;
        PropVersion++;
        TexturesVersion++;
        HeightmapVersion++;
        SelectedMarker = null;
        SelectedProp = null;
        SelectedUnitSpawn = null;
        StatusText = $"Symmetry applied: {pattern} from {source}";
    }

    /// <summary>Snapshot the current selection into the internal clipboard. No-op if nothing is selected.</summary>
    public void CopySelection()
    {
        if (SelectedMarker != null) { _clipboard = CloneMarker(SelectedMarker); StatusText = $"Copied marker {SelectedMarker.Name}"; }
        else if (SelectedProp != null) { _clipboard = CloneProp(SelectedProp); StatusText = "Copied prop"; }
        else if (SelectedUnitSpawn != null)
        {
            // Remember which army owns the unit so paste lands in the same list (else neutrals would
            // silently migrate to NEUTRAL_CIVILIAN).
            var owner = CurrentMap?.Info.Armies.FirstOrDefault(a => a.InitialUnits.Contains(SelectedUnitSpawn));
            _clipboard = new ClipboardUnit(CloneUnitSpawn(SelectedUnitSpawn), owner?.Name);
            StatusText = $"Copied unit {SelectedUnitSpawn.BlueprintId}";
        }
        else { StatusText = "Nothing selected to copy"; }
    }

    /// <summary>
    /// Paste the clipboard entry into the current map at world coords (mapX, mapZ). The Y is clamped
    /// to the heightmap surface. Markers get a fresh unique name; props are cloned as-is.
    /// </summary>
    public void PasteAt(float mapX, float mapZ)
    {
        if (CurrentMap == null || _clipboard == null) return;
        float y = GroundClampService.SampleHeight(CurrentMap.Heightmap, mapX, mapZ);

        if (_clipboard is Marker srcMarker)
        {
            var copy = CloneMarker(srcMarker);
            var (sx, sz) = BuildGridSnap.Snap(srcMarker.Type, mapX, mapZ);
            copy.Position = new Vector3(sx, GroundClampService.SampleHeight(CurrentMap.Heightmap, sx, sz), sz);
            copy.Name = MakeUniqueMarkerName(srcMarker.Name);
            var op = new AddMarkerOp(CurrentMap.Markers, copy);
            op.Execute();
            UndoRedo.PushExecuted(op);
            MarkerCount = CurrentMap.Markers.Count;
            MarkerVersion++;
            SelectedMarker = copy;
            StatusText = $"Pasted marker {copy.Name}";
        }
        else if (_clipboard is Prop srcProp)
        {
            var copy = CloneProp(srcProp);
            copy.Position = new Vector3(mapX, y, mapZ);
            var op = new AddPropOp(CurrentMap.Props, copy);
            op.Execute();
            UndoRedo.PushExecuted(op);
            PropCount = CurrentMap.Props.Count;
            SelectedProp = copy;
            StatusText = "Pasted prop";
        }
        else if (_clipboard is ClipboardUnit srcUnit)
        {
            // Resolve target army by name; default to NEUTRAL_CIVILIAN if the original army no longer exists.
            var army = CurrentMap.Info.Armies.FirstOrDefault(a =>
                string.Equals(a.Name, srcUnit.ArmyName ?? "NEUTRAL_CIVILIAN", StringComparison.OrdinalIgnoreCase));
            if (army == null)
            {
                army = new Army { Name = "NEUTRAL_CIVILIAN" };
                CurrentMap.Info.Armies.Add(army);
            }
            var copy = CloneUnitSpawn(srcUnit.Unit);
            copy.Position = new Vector3(mapX, y, mapZ);
            // Bump UNIT_N within the destination army to stay unique.
            int n = 0;
            while (army.InitialUnits.Any(u => u.Name == $"UNIT_{n}")) n++;
            copy.Name = $"UNIT_{n}";
            var op = new AddUnitSpawnOp(army.InitialUnits, copy);
            op.Execute();
            UndoRedo.PushExecuted(op);
            MarkerVersion++; // 2D redraws InitialUnits when MarkerVersion bumps
            SelectedUnitSpawn = copy;
            StatusText = $"Pasted unit {copy.BlueprintId} in {army.Name}";
        }
    }

    /// <summary>Internal clipboard payload for unit spawns. Carries the source army name so paste
    /// lands back in the same army when possible.</summary>
    private sealed record ClipboardUnit(UnitSpawn Unit, string? ArmyName);

    private static Marker CloneMarker(Marker src) => new()
    {
        Name = src.Name,
        Type = src.Type,
        Position = src.Position,
        Orientation = src.Orientation,
        Resource = src.Resource,
        Amount = src.Amount,
        Color = src.Color,
        Hint = src.Hint,
        AdjacentMarkers = [..src.AdjacentMarkers],
        Graph = src.Graph,
        Zoom = src.Zoom,
        CanSetCamera = src.CanSetCamera,
        CanSyncCamera = src.CanSyncCamera,
        EffectTemplate = src.EffectTemplate,
        Scale = src.Scale,
        WeatherType = src.WeatherType,
    };

    private static Prop CloneProp(Prop src) => new()
    {
        BlueprintPath = src.BlueprintPath,
        Position = src.Position,
        RotationX = src.RotationX,
        RotationY = src.RotationY,
        RotationZ = src.RotationZ,
        Scale = src.Scale,
    };

    private static UnitSpawn CloneUnitSpawn(UnitSpawn src) => new()
    {
        Name = src.Name,
        BlueprintId = src.BlueprintId,
        Position = src.Position,
        Orientation = src.Orientation,
        Platoon = src.Platoon,
        Orders = src.Orders,
    };

    /// <summary>Delete the currently-selected UnitSpawn. Undoable. No-op if no unit is selected.</summary>
    public void DeleteSelectedUnitSpawn()
    {
        if (CurrentMap == null || SelectedUnitSpawn == null) return;
        var owner = CurrentMap.Info.Armies.FirstOrDefault(a => a.InitialUnits.Contains(SelectedUnitSpawn));
        if (owner == null) return;
        var op = new RemoveUnitSpawnOp(owner.InitialUnits, SelectedUnitSpawn);
        op.Execute();
        UndoRedo.PushExecuted(op);
        var name = SelectedUnitSpawn.Name;
        SelectedUnitSpawn = null;
        MarkerVersion++;
        StatusText = $"Deleted unit {name}";
    }

    private string MakeUniqueMarkerName(string baseName)
    {
        if (CurrentMap == null) return baseName;
        // Numeric ARMY_N → increment
        if (baseName.StartsWith("ARMY_", StringComparison.OrdinalIgnoreCase))
        {
            for (int n = 1; n < 256; n++)
            {
                var c = $"ARMY_{n}";
                if (!CurrentMap.Markers.Any(m => string.Equals(m.Name, c, StringComparison.OrdinalIgnoreCase)))
                    return c;
            }
        }
        // "Prefix NN" pattern (Mass 03, Hydro 02…) → bump suffix
        string trimmed = baseName.TrimEnd();
        int lastSpace = trimmed.LastIndexOf(' ');
        string prefix = lastSpace >= 0 ? trimmed[..lastSpace] : trimmed;
        for (int i = 1; i < 1000; i++)
        {
            var c = $"{prefix} {i:D2}";
            if (!CurrentMap.Markers.Any(m => string.Equals(m.Name, c, StringComparison.OrdinalIgnoreCase)))
                return c;
        }
        return baseName + "_copy";
    }

    /// <summary>Currently selected pre-placed unit (drag/edit target). Synced from SkiaMapControl click.</summary>
    [ObservableProperty] private UnitSpawn? _selectedUnitSpawn;

    /// <summary>Record a UnitSpawn drag as a single undoable move. Y clamped to terrain on release.</summary>
    public void RecordUnitSpawnMove(UnitSpawn unit, Vector3 from)
    {
        if (CurrentMap == null) return;
        float y = GroundClampService.SampleHeight(CurrentMap.Heightmap, unit.Position.X, unit.Position.Z);
        unit.Position = new Vector3(unit.Position.X, y, unit.Position.Z);
        var to = unit.Position;
        if (from == to) return;
        UndoRedo.PushExecuted(new MoveUnitSpawnOp(unit, from, to));
        MarkerVersion++; // 2D redraws InitialUnits when MarkerVersion bumps
    }

    /// <summary>Record a prop drag as a single undoable move from oldPos to its current position.
    /// Y is clamped to the terrain at the drop point so the prop doesn't float/bury.</summary>
    public void RecordPropMove(Prop prop, Vector3 from)
    {
        if (CurrentMap == null) return;
        float y = GroundClampService.SampleHeight(CurrentMap.Heightmap, prop.Position.X, prop.Position.Z);
        prop.Position = new Vector3(prop.Position.X, y, prop.Position.Z);
        var to = prop.Position;
        if (from == to) return;
        UndoRedo.PushExecuted(new MovePropOp(prop, from, to));
        PropVersion++;
    }

    /// <summary>
    /// Finalize a group drag: ground-clamp each prop/unit Y, build-grid-snap each resource marker,
    /// and push one <see cref="BatchMoveOp"/> covering all the moves. Caller passes the snapshots
    /// taken at drag start so Undo restores exact starting positions.
    /// </summary>
    public void RecordMultiSelectionMove(
        IReadOnlyDictionary<Prop, Vector3> propsFrom,
        IReadOnlyDictionary<Marker, Vector3> markersFrom,
        IReadOnlyDictionary<UnitSpawn, Vector3> unitsFrom)
    {
        if (CurrentMap == null) return;
        var moves = new List<(Action<Vector3>, Vector3, Vector3)>();

        foreach (var (p, from) in propsFrom)
        {
            float y = GroundClampService.SampleHeight(CurrentMap.Heightmap, p.Position.X, p.Position.Z);
            var to = new Vector3(p.Position.X, y, p.Position.Z);
            p.Position = to;
            if (from != to) { var local = p; moves.Add((v => local.Position = v, from, to)); }
        }
        foreach (var (m, from) in markersFrom)
        {
            var (sx, sz) = BuildGridSnap.Snap(m.Type, m.Position.X, m.Position.Z);
            var to = new Vector3(sx, m.Position.Y, sz);
            m.Position = to;
            if (from != to) { var local = m; moves.Add((v => local.Position = v, from, to)); }
        }
        foreach (var (u, from) in unitsFrom)
        {
            float y = GroundClampService.SampleHeight(CurrentMap.Heightmap, u.Position.X, u.Position.Z);
            var to = new Vector3(u.Position.X, y, u.Position.Z);
            u.Position = to;
            if (from != to) { var local = u; moves.Add((v => local.Position = v, from, to)); }
        }

        if (moves.Count == 0) return;
        UndoRedo.PushExecuted(new BatchMoveOp(moves));
        if (propsFrom.Count > 0) PropVersion++;
        if (markersFrom.Count > 0 || unitsFrom.Count > 0) MarkerVersion++;
        StatusText = $"Moved {moves.Count} elements";
    }

    /// <summary>Clear every selection — single (marker/prop/unit), 2D palette entry, 3D palette
    /// tool, AND deactivates the 3D brush. Multi-selection is cleared by the view. Called by Escape.</summary>
    public void ClearAllSelections()
    {
        SelectedMarker = null;
        SelectedProp = null;
        SelectedUnitSpawn = null;
        SelectedPropEntry = null;
        SelectedPropCategory = null;
        SelectedViewport3DTool = null;     // deactivates the 3D brush (heightmap or texture)
        SelectedViewport3DCategory = null; // collapses the 3D palette level-2 list
    }

    /// <summary>Record a marker drag as a single undoable move from oldPos to its current position.</summary>
    public void RecordMarkerMove(Marker marker, Vector3 from)
    {
        if (CurrentMap == null) return;
        // For resource markers, snap the drag end-point onto the build grid before we record
        // the op — so a single Undo restores the pre-drag pos rather than an intermediate snapped state.
        var (sx, sz) = BuildGridSnap.Snap(marker.Type, marker.Position.X, marker.Position.Z);
        marker.Position = new Vector3(sx, marker.Position.Y, sz);
        var to = marker.Position;
        if (from == to) return; // no-op drag
        UndoRedo.PushExecuted(new MoveMarkerOp(marker, from, to));
        MarkerVersion++;
    }

    partial void OnLightingMultiplierChanged(double value) => SyncLightingToMap();
    partial void OnSunDirXChanged(double value) => SyncLightingToMap();
    partial void OnSunDirYChanged(double value) => SyncLightingToMap();
    partial void OnSunDirZChanged(double value) => SyncLightingToMap();
    partial void OnSunAmbienceRChanged(double value) { SyncLightingToMap(); RefreshLightingSwatches(); }
    partial void OnSunAmbienceGChanged(double value) { SyncLightingToMap(); RefreshLightingSwatches(); }
    partial void OnSunAmbienceBChanged(double value) { SyncLightingToMap(); RefreshLightingSwatches(); }
    partial void OnSunColorRChanged(double value) { SyncLightingToMap(); RefreshLightingSwatches(); }
    partial void OnSunColorGChanged(double value) { SyncLightingToMap(); RefreshLightingSwatches(); }
    partial void OnSunColorBChanged(double value) { SyncLightingToMap(); RefreshLightingSwatches(); }
    partial void OnBloomChanged(double value) => SyncLightingToMap();
    partial void OnFogStartChanged(double value) => SyncLightingToMap();
    partial void OnFogEndChanged(double value) => SyncLightingToMap();

    /// <summary>Force the Lighting palette category to rebuild so its color swatches reflect new
    /// Sun Color / Ambience values. Cheap enough to do on every channel change.</summary>
    private void RefreshLightingSwatches()
    {
        if (_isPopulating) return;
        RefreshViewport3DCategories();
    }
    partial void OnHasWaterChanged(bool value) { SyncWaterToMap(); if (!_isPopulating) RefreshViewport3DCategories(); }
    partial void OnWaterElevationChanged(double value) => SyncWaterToMap();
    partial void OnWaterElevationDeepChanged(double value) => SyncWaterToMap();
    partial void OnWaterElevationAbyssChanged(double value) => SyncWaterToMap();

    /// <summary>Reverse direction sync — pull water values from <see cref="CurrentMap"/>.Water back
    /// into VM observable properties. Called after Undo/Redo so the UI reflects restored state.</summary>
    private void SyncWaterFromMap()
    {
        if (CurrentMap == null) return;
        _isPopulating = true;
        HasWater = CurrentMap.Water.HasWater;
        WaterElevation = CurrentMap.Water.Elevation;
        WaterElevationDeep = CurrentMap.Water.ElevationDeep;
        WaterElevationAbyss = CurrentMap.Water.ElevationAbyss;
        _isPopulating = false;
    }
}
