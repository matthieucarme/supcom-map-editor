using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SupremeCommanderEditor.App.ViewModels;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireUpViewModel();
        WireUpSymmetryThumbnails();

        // Track the 3D tab placeholder's actual bounds and align GlViewport to them. This adapts
        // automatically to whatever height the TabControl gives its tab strip (theme-dependent),
        // so we don't hardcode a magic Margin and the strip never gets covered by GL.
        GlPlaceholder.LayoutUpdated += (_, _) => SyncGlToPlaceholder();
        SizeChanged += (_, _) => SyncGlToPlaceholder();
    }

    /// <summary>
    /// Match GlViewport's Margin to the actual layout slot of <see cref="GlPlaceholder"/> within
    /// the inner Grid that hosts both the TabControl and the GL overlay. Only runs when the 3D tab
    /// is active (otherwise the placeholder is detached from the visual tree).
    /// </summary>
    private void SyncGlToPlaceholder()
    {
        if (ViewportTabs == null || GlViewport == null || GlPlaceholder == null) return;
        if (ViewportTabs.SelectedIndex != 0) return;
        if (GlViewport.Parent is not Control parent) return;
        if (!GlPlaceholder.IsLoaded || GlPlaceholder.Bounds.Width < 1) return;
        try
        {
            var transform = GlPlaceholder.TransformToVisual(parent);
            if (transform == null) return;
            var topLeft = transform.Value.Transform(new Point(0, 0));
            var size = GlPlaceholder.Bounds.Size;
            var parentSize = parent.Bounds.Size;
            var margin = new Thickness(
                topLeft.X,
                topLeft.Y,
                parentSize.Width - topLeft.X - size.Width,
                parentSize.Height - topLeft.Y - size.Height);
            // Avoid LayoutUpdated feedback loop: only apply when something actually changed.
            if (GlViewport.Margin != margin)
                GlViewport.Margin = margin;
        }
        catch { /* transforms can fail mid-layout — next pass will retry */ }
    }

    private void WireUpSymmetryThumbnails()
    {
        ThumbV.Pattern = SymmetryPattern.Vertical;
        ThumbH.Pattern = SymmetryPattern.Horizontal;
        ThumbD1.Pattern = SymmetryPattern.DiagonalTLBR;
        ThumbD2.Pattern = SymmetryPattern.DiagonalTRBL;
        ThumbCross.Pattern = SymmetryPattern.QuadCross;
        ThumbDiag.Pattern = SymmetryPattern.QuadDiagonals;

        void OnRegion(SymmetryPattern p, SymmetryRegion r)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.ApplySymmetry(p, r);
        }
        ThumbV.RegionSelected += OnRegion;
        ThumbH.RegionSelected += OnRegion;
        ThumbD1.RegionSelected += OnRegion;
        ThumbD2.RegionSelected += OnRegion;
        ThumbCross.RegionSelected += OnRegion;
        ThumbDiag.RegionSelected += OnRegion;
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private void WireUpViewModel()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            GlViewport.ViewModel = vm;
            GlViewport.SetGameData(vm.GameData);
            SkiaViewport.MarkerIcons = new SupremeCommanderEditor.App.Services.MarkerIconService(vm.GameData);

            // 2D background is a top-down capture of the 3D scene
            GlViewport.TopDownReady += (rgba, w, h) => SkiaViewport.SetTopDownBitmap(rgba, w, h);

            // Marker selection: 2D view → ViewModel
            SkiaViewport.MarkerSelected += marker => vm.SelectedMarker = marker;
            // Prop selection: 2D view → ViewModel (for copy/paste targeting)
            SkiaViewport.PropSelected += prop => vm.SelectedProp = prop;

            // Prop brush: 2D pointer events stream into the VM, which accumulates a single op for undo.
            SkiaViewport.PropBrushStart += (x, z) => { vm.BeginPropBrush(); vm.ApplyPropBrush(x, z); };
            SkiaViewport.PropBrushDrag  += (x, z) => vm.ApplyPropBrush(x, z);
            SkiaViewport.PropBrushEnd   += () => vm.EndPropBrush();

            // Single-prop placement: fired when a prop is selected in the bottom icon menu and
            // the brush is off — each click drops one of the picked blueprint.
            SkiaViewport.SinglePlaceProp += (x, z) =>
            {
                vm.PlaceSelectedPropAt(x, z);
                SkiaViewport.RefreshMarkers();
            };

            // Marker drag: record an undoable move op (drag start pos → current pos)
            SkiaViewport.MarkerMoved += (marker, startPos) =>
            {
                vm.RecordMarkerMove(marker, startPos);
            };

            // Prop drag: same pattern. The VM clamps Y to terrain on release.
            SkiaViewport.PropMoved += (prop, startPos) =>
            {
                vm.RecordPropMove(prop, startPos);
            };

            // UnitSpawn selection and drag — same pattern as props.
            SkiaViewport.UnitSpawnSelected += unit => vm.SelectedUnitSpawn = unit;
            SkiaViewport.UnitSpawnMoved += (unit, startPos) => vm.RecordUnitSpawnMove(unit, startPos);

            // Group drag: pre-drag positions for each entity type, fed back so the VM can clamp Y
            // (props/units) or snap to build grid (resource markers) before pushing a single op.
            SkiaViewport.MultiSelectionMoved += (props, markers, units) =>
            {
                vm.RecordMultiSelectionMove(props, markers, units);
                SkiaViewport.RefreshMarkers();
            };

            // Texture-brush picker hit a full map → show the replacement chooser dialog.
            vm.TextureAssignmentNeedsUser += async albedoPath => await PromptTextureReplacement(vm, albedoPath);

            // Lighting palette tool clicked → open the corresponding popup editor.
            vm.LightingSettingActivated += async setting => await ShowLightingEditor(vm, setting);
            // Water palette tool clicked → toggle Enable directly, or open slider popup for tiers.
            vm.WaterSettingActivated += async setting => await ShowWaterEditor(vm, setting);

            // ViewModel → 2D view sync (when marker edited from panel)
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.HeightmapVersion))
                {
                    GlViewport.InvalidateMesh();
                    if (!vm.IsBrushActive)
                    {
                        SkiaViewport.RefreshHeightmap();
                        // Re-capture the 3D scene so the 2D background + symmetry thumbnails
                        // reflect the edit, even if GL is currently hidden behind another tab.
                        RequestSnapshotEvenIfHidden();
                    }
                }
                else if (args.PropertyName == nameof(MainWindowViewModel.TexturesVersion))
                {
                    GlViewport.InvalidateTextures();
                    RequestSnapshotEvenIfHidden();
                }
                else if (args.PropertyName == nameof(MainWindowViewModel.SelectedMarker))
                {
                    SkiaViewport.SelectedMarker = vm.SelectedMarker;
                    GlViewport.InvalidateMarkers(vm.SelectedMarker);
                }
                else if (args.PropertyName == nameof(MainWindowViewModel.SelectedUnitSpawn))
                {
                    SkiaViewport.SelectedUnitSpawn = vm.SelectedUnitSpawn;
                }
                else if (args.PropertyName == nameof(MainWindowViewModel.SelectedProp))
                {
                    SkiaViewport.SelectedProp = vm.SelectedProp;
                }
                else if (args.PropertyName is nameof(MainWindowViewModel.MarkerCount)
                         or nameof(MainWindowViewModel.MarkerVersion)
                         or nameof(MainWindowViewModel.PropVersion))
                {
                    SkiaViewport.RefreshMarkers();
                    GlViewport.InvalidateMarkers(vm.SelectedMarker);
                }
                else if (args.PropertyName is nameof(MainWindowViewModel.MapWidth)
                         or nameof(MainWindowViewModel.MapHeight))
                {
                    // Keep the Map Info tab's scale radios in sync with whatever size the map is.
                    RefreshScaleRadios();
                }
                else if (args.PropertyName == nameof(MainWindowViewModel.HasMap))
                {
                    // Welcome screen → editor (or vice-versa). GL must move on/offscreen accordingly,
                    // otherwise its raw GL pixels keep painting over the welcome screen / menu bar.
                    UpdateGlViewportPosition();
                }
            };
            // Initial sync (map may already be loaded by the time WireUpViewModel runs).
            RefreshScaleRadios();
        }
    }

    private void OnNewMap(object? sender, RoutedEventArgs e)
    {
        // Simple new map dialog - create a 256x256 2-player map for now
        // TODO: proper dialog in later phase
        Vm.NewMap(256, 2, "New Map");
    }

    private async void OnOpenMap(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Supreme Commander Map",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SC Map Files") { Patterns = ["*.scmap"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                Vm.LoadMap(path);
            }
        }
    }

    private void OnSaveMap(object? sender, RoutedEventArgs e)
    {
        Vm.SaveMap();
    }

    private async void OnSaveMapAs(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Supreme Commander Map",
            DefaultExtension = "scmap",
            FileTypeChoices =
            [
                new FilePickerFileType("SC Map Files") { Patterns = ["*.scmap"] }
            ]
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                Vm.SaveMapAs(path);
            }
        }
    }

    private void OnUndo(object? sender, RoutedEventArgs e) => Vm.Undo();
    private void OnRedo(object? sender, RoutedEventArgs e) => Vm.Redo();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not MainWindowViewModel vm) return;

        // Escape clears every selection: palette entry, single selections (marker/prop/unit),
        // and the multi-selection. One key brings the user back to a clean slate.
        if (e.Key == Key.Escape && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ClearAllSelections();
            SkiaViewport.ClearMultiSelection();
            SkiaViewport.RefreshMarkers();
            e.Handled = true;
            return;
        }

        // Delete: priority is multi-selection (props/markers/units in one batch) → single marker
        // → single prop → single unit. Multi-selection takes precedence even if it's only one type.
        if (e.Key == Key.Delete && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (SkiaViewport.HasMultiSelection)
            {
                vm.DeleteMultiSelection(
                    SkiaViewport.MultiSelectedProps.ToList(),
                    SkiaViewport.MultiSelectedMarkers.ToList(),
                    SkiaViewport.MultiSelectedUnits.ToList());
                SkiaViewport.ClearMultiSelection();
                SkiaViewport.RefreshMarkers();
            }
            else if (vm.SelectedMarker != null)
            {
                vm.DeleteSelectedMarker();
            }
            else if (vm.SelectedUnitSpawn != null)
            {
                vm.DeleteSelectedUnitSpawn();
                SkiaViewport.SelectedUnitSpawn = null;
                SkiaViewport.RefreshMarkers();
            }
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        // Ctrl+C / Ctrl+V on the 2D view: copy selected marker / prop / unit, paste at cursor.
        if (e.Key == Key.C)
        {
            // Sync the prop selection back to the VM before copying so it picks up clicks-since-last-sync.
            vm.SelectedProp = SkiaViewport.SelectedProp;
            vm.SelectedUnitSpawn = SkiaViewport.SelectedUnitSpawn;
            vm.CopySelection();
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            var pos = SkiaViewport.CursorWorldPos;
            if (pos == null) return;
            vm.PasteAt(pos.Value.X, pos.Value.Y);
            // The newly-added entity may be a marker, prop, or unit; refresh all views.
            SkiaViewport.SelectedMarker = vm.SelectedMarker;
            SkiaViewport.SelectedProp = vm.SelectedProp;
            SkiaViewport.SelectedUnitSpawn = vm.SelectedUnitSpawn;
            SkiaViewport.RefreshMarkers();
            GlViewport.InvalidateMarkers(vm.SelectedMarker);
            e.Handled = true;
        }
    }

    private void OnClampToGround(object? sender, RoutedEventArgs e)
    {
        Vm.ClampToGround();
        GlViewport.InvalidateMarkers(Vm.SelectedMarker);
        SkiaViewport.RefreshMarkers();
    }

private async void OnRenameMap(object? sender, RoutedEventArgs e)
    {
        if (Vm.CurrentMap == null || Vm.CurrentFilePath == null) return;
        var currentFolder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Vm.CurrentFilePath)!);
        var dialog = new RenameMapDialog(Vm.MapName, currentFolder);
        await dialog.ShowDialog(this);
        var newName = dialog.Result;
        if (string.IsNullOrWhiteSpace(newName) || newName == Vm.MapName) return;

        var error = Vm.RenameMap(newName);
        if (error != null)
            await new InfoDialog("Rename failed", error).ShowDialog(this);
    }

    /// <summary>
    /// Single click handler shared by the 5 scale radio buttons in the Map Info tab. The desired
    /// size is encoded in the RadioButton's Tag attribute (e.g. "256"). A no-op if the map is
    /// already at this size (prevents a destructive resample when the radio is set programmatically
    /// from RefreshScaleRadios).
    /// </summary>
    private void OnScaleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string s) return;
        if (!int.TryParse(s, out int size)) return;
        if (Vm.CurrentMap == null) return;
        if (Vm.CurrentMap.Heightmap.Width == size) return;
        ScaleTo(size);
    }

    /// <summary>Set the radio button matching the current map size to IsChecked=true so the UI
    /// reflects state after load / scale / undo. Programmatic IsChecked doesn't fire Click, so
    /// no risk of feedback loop.</summary>
    private void RefreshScaleRadios()
    {
        if (Size256 == null) return; // not laid out yet
        int current = Vm?.CurrentMap?.Heightmap.Width ?? 0;
        Size256.IsChecked  = current == 256;
        Size512.IsChecked  = current == 512;
        Size1024.IsChecked = current == 1024;
        Size2048.IsChecked = current == 2048;
        Size4096.IsChecked = current == 4096;
    }

    private void ScaleTo(int size)
    {
        Vm.ScaleMap(size);
        // Heightmap/textures/markers all changed; trigger the full refresh path. The camera also
        // needs to re-fit since the map dimensions changed — without it, the old target/zoom would
        // leave the resized terrain off-screen or at a wrong scale.
        GlViewport.InvalidateMesh();
        GlViewport.InvalidateTextures();
        GlViewport.InvalidateMarkers(Vm.SelectedMarker);
        GlViewport.RecenterCamera();
        GlViewport.RequestTopDownSnapshot();
        SkiaViewport.RefreshHeightmap();
        SkiaViewport.RefreshMarkers();
        RefreshScaleRadios();
    }

    private async void OnSetGameFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick the Supreme Commander install folder (the one containing gamedata/)",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (path == null)
        {
            await new InfoDialog("Error", "Couldn't resolve a local path from that location.").ShowDialog(this);
            return;
        }

        bool ok = Vm.SetGameInstallPath(path);
        if (ok && Vm.CurrentMap != null)
        {
            GlViewport.SetGameData(Vm.GameData);
            GlViewport.InvalidateTextures();
            GlViewport.RequestTopDownSnapshot();
            SkiaViewport.MarkerIcons = new SupremeCommanderEditor.App.Services.MarkerIconService(Vm.GameData);
            SkiaViewport.RefreshMarkers();
        }
        // Always show the diagnostic so the user can confirm what was detected (or what failed).
        await new InfoDialog(ok ? "Game folder OK" : "Game folder INVALIDE", Vm.BuildDiagnostics())
            .ShowDialog(this);
    }

    private async void OnDiagnostics(object? sender, RoutedEventArgs e)
    {
        await new InfoDialog("Diagnostics", Vm.BuildDiagnostics()).ShowDialog(this);
    }

    /// <summary>
    /// Procedural generation flow: open the dialog, on OK resolve textures-per-category from the
    /// scanned library (filtered to the picked biome), call <see cref="MapGenerator.Generate"/>,
    /// install the resulting map as the current one.
    /// </summary>
    /// <summary>True if the basename looks like a vanilla SC macro/overlay texture.
    /// Known names: macrotexture* (most biomes), macroice (tundra). Add more here if needed.</summary>
    private static bool IsMacroTexture(string albedoPath)
    {
        var basename = Core.Operations.TextureCategoryTable.ExtractBasename(albedoPath);
        return basename.StartsWith("macrotexture", StringComparison.OrdinalIgnoreCase)
            || basename.StartsWith("macroice", StringComparison.OrdinalIgnoreCase);
    }

    private async void OnGenerateMap(object? sender, RoutedEventArgs e)
    {
        var library = Vm.GetTextureLibrary();
        Core.Services.DebugLog.Write($"[GenHandler] Library entries available: {library.Entries.Count}");
        if (library.Entries.Count == 0)
        {
            await new InfoDialog("Cannot generate map",
                "The texture library is empty — game data not loaded. " +
                "Open Settings → Set game folder… and point to your Supreme Commander install.")
                .ShowDialog(this);
            return;
        }

        var dlg = new MapGeneratorDialog(library);
        await dlg.ShowDialog(this);
        if (dlg.Result is not Core.Services.MapGenerationOptions opts) return;

        // Resolve textures per smart category using the SAME resolver the dialog previewed.
        var resolved = Services.TextureSetResolver.Resolve(library, dlg.BiomeKey);
        int resolvedCount = 0;
        foreach (var (cat, entry) in resolved)
        {
            if (entry == null)
            {
                Core.Services.DebugLog.Write($"[GenHandler]   {cat} → (no library texture matches)");
                continue;
            }
            Core.Services.DebugLog.Write($"[GenHandler]   {cat} → {entry.AlbedoPath}");
            opts.TexturesByCategory[cat] = entry.AlbedoPath;
            opts.NormalsByCategory[cat] = entry.NormalPath;
            resolvedCount++;
        }
        Core.Services.DebugLog.Write($"[GenHandler] Biome={dlg.BiomeKey} resolvedCount={resolvedCount}");

        // Macro/upper layer (strata 9). Vanilla maps have a biome-specific macro overlay:
        // Evergreen-style maps use /env/evergreen/layers/macrotexture000_albedo.dds, Tundra maps
        // use /env/tundra/layers/macroice_albedo.dds, etc. Prefer the biome's own macro; if the
        // biome doesn't ship one, take any vanilla macro (still a real game texture, not a Grass
        // duplicate fallback).
        string biomeFragment = "/" + dlg.BiomeKey + "/";
        var macro = library.Entries.FirstOrDefault(en =>
            en.AlbedoPath.Contains(biomeFragment, StringComparison.OrdinalIgnoreCase)
            && IsMacroTexture(en.AlbedoPath));
        macro ??= library.Entries.FirstOrDefault(en => IsMacroTexture(en.AlbedoPath));
        if (macro != null)
        {
            opts.MacroTexturePath = macro.AlbedoPath;
            Core.Services.DebugLog.Write($"[GenHandler] Macro layer: {macro.AlbedoPath}");
        }
        else
        {
            await new InfoDialog("Cannot generate map",
                "No macro texture (macrotexture000 / macroice / …) found in the texture library. " +
                "Strata 9 would render as magenta. Check your game install.")
                .ShowDialog(this);
            return;
        }
        if (resolvedCount == 0)
        {
            await new InfoDialog("Cannot generate map",
                $"No textures could be resolved for biome '{dlg.BiomeKey}' or any fallback. " +
                "The library has entries but none classify into smart categories — TextureCategoryTable may need extending.")
                .ShowDialog(this);
            return;
        }

        var map = Core.Services.MapGenerator.Generate(opts);
        Vm.InstallGeneratedMap(map);

        // Refresh views — same path as loading a map.
        GlViewport.InvalidateMesh();
        GlViewport.InvalidateTextures();
        GlViewport.InvalidateMarkers(Vm.SelectedMarker);
        GlViewport.RecenterCamera();
        GlViewport.RequestTopDownSnapshot();
        SkiaViewport.RefreshHeightmap();
        SkiaViewport.RefreshMarkers();
        RefreshScaleRadios();
    }

    private void OnViewportTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        // OpenGL renders above Avalonia compositing, so the GL control must be hidden on the 2D tab.
        // On switch to 2D, request a fresh snapshot first and hide GL only after it arrives, so the
        // 2D background reflects any lighting/water changes made while on the 3D tab.
        if (GlViewport == null) return;
        UpdateGlViewportPosition();
        // Re-snapshot when leaving 3D so the 2D background / Symétrie thumbnails reflect any edits
        // the user made on the 3D tab (lighting, brush, etc.) — but only when we WERE on 3D before.
        if (ViewportTabs.SelectedIndex != 0)
            GlViewport.RequestTopDownSnapshot();
        else
            // Wake the loop in case invalidations were queued while we were offscreen.
            GlViewport.RequestRender();
    }

    /// <summary>
    /// Position the GL viewport: aligned to <see cref="GlPlaceholder"/> when the 3D tab is active,
    /// shoved offscreen via a large negative Margin otherwise. The control stays in the visual tree
    /// either way so <c>OnOpenGlRender</c> keeps firing on demand (snapshots work from any tab).
    /// Because GL renders above Avalonia compositing, "hide" cannot just mean IsVisible=false —
    /// that detaches the control and kills the render loop. Pushing offscreen keeps it alive but
    /// invisible.
    /// </summary>
    private void UpdateGlViewportPosition()
    {
        if (GlViewport == null || ViewportTabs == null) return;
        // No map loaded → keep GL parked offscreen so its pixels don't bleed over the welcome
        // screen / menu. (GL renders above Avalonia compositing, so IsVisible=false is not enough.)
        bool hasMap = DataContext is MainWindowViewModel vm && vm.HasMap;
        if (hasMap && ViewportTabs.SelectedIndex == 0)
        {
            // Let SyncGlToPlaceholder do the precise alignment once the placeholder is laid out.
            // Defer so the just-selected tab content has time to attach + measure.
            Dispatcher.UIThread.Post(SyncGlToPlaceholder, DispatcherPriority.Render);
        }
        else
        {
            GlViewport.Margin = new Thickness(-100000, -100000, 100000, 100000);
        }
    }


    /// <summary>
    /// Request a top-down snapshot. Works regardless of which tab is active because the GL viewport
    /// stays in the visual tree (parked offscreen via Margin) when the user is not on the 3D tab.
    /// </summary>
    private void RequestSnapshotEvenIfHidden()
    {
        if (GlViewport == null) return;
        GlViewport.RequestTopDownSnapshot();
    }

    /// <summary>
    /// Water palette dispatch — Enable toggles in place, the three elevation tiers each open a
    /// single-slider popup. All changes are bundled into a single undoable <c>WaterChangeOp</c>.
    /// </summary>
    private async System.Threading.Tasks.Task ShowWaterEditor(MainWindowViewModel vm, Services.WaterSetting setting)
    {
        if (vm.CurrentMap == null) return;
        var before = Core.Operations.WaterSnapshot.Of(vm.CurrentMap.Water);
        switch (setting)
        {
            case Services.WaterSetting.Enable:
                vm.HasWater = !vm.HasWater;
                break;
            case Services.WaterSetting.Surface:
            {
                var dlg = new SliderPopupDialog("Water Surface elevation", new[] { ("Surface", vm.WaterElevation, 0.0, 256.0) });
                await dlg.ShowDialog(this);
                if (dlg.Result is { Length: 1 } r) vm.WaterElevation = r[0];
                break;
            }
            case Services.WaterSetting.Deep:
            {
                var dlg = new SliderPopupDialog("Water Deep elevation", new[] { ("Deep", vm.WaterElevationDeep, 0.0, 256.0) });
                await dlg.ShowDialog(this);
                if (dlg.Result is { Length: 1 } r) vm.WaterElevationDeep = r[0];
                break;
            }
            case Services.WaterSetting.Abyss:
            {
                var dlg = new SliderPopupDialog("Water Abyss elevation", new[] { ("Abyss", vm.WaterElevationAbyss, 0.0, 256.0) });
                await dlg.ShowDialog(this);
                if (dlg.Result is { Length: 1 } r) vm.WaterElevationAbyss = r[0];
                break;
            }
        }
        var after = Core.Operations.WaterSnapshot.Of(vm.CurrentMap.Water);
        if (!before.Equals(after))
        {
            vm.UndoRedo.PushExecuted(new Core.Operations.WaterChangeOp(
                vm.CurrentMap.Water, before, after, $"Water: {setting}"));
        }
    }

    /// <summary>
    /// Open the appropriate popup editor for a Lighting palette tool click — color wheel for the
    /// two color settings, slider popup for everything else. Writes results back into the VM only
    /// on OK; Cancel leaves values untouched. Wraps the changes in a single undoable op so a
    /// dialog edit reverts atomically with Ctrl+Z.
    /// </summary>
    private async System.Threading.Tasks.Task ShowLightingEditor(MainWindowViewModel vm, Services.LightingSetting setting)
    {
        if (vm.CurrentMap == null) return;
        var before = Core.Operations.LightingSnapshot.Of(vm.CurrentMap.Lighting);
        await DispatchLightingDialog(vm, setting);
        var after = Core.Operations.LightingSnapshot.Of(vm.CurrentMap.Lighting);
        if (!before.Equals(after))
        {
            vm.UndoRedo.PushExecuted(new Core.Operations.LightingChangeOp(
                vm.CurrentMap.Lighting, before, after, $"Lighting: {setting}"));
        }
    }

    private async System.Threading.Tasks.Task DispatchLightingDialog(MainWindowViewModel vm, Services.LightingSetting setting)
    {
        switch (setting)
        {
            case Services.LightingSetting.SunColor:
            {
                byte r = (byte)Math.Clamp((int)(vm.SunColorR * 255), 0, 255);
                byte g = (byte)Math.Clamp((int)(vm.SunColorG * 255), 0, 255);
                byte b = (byte)Math.Clamp((int)(vm.SunColorB * 255), 0, 255);
                var dlg = new ColorPickerDialog("Sun Color", Avalonia.Media.Color.FromRgb(r, g, b));
                await dlg.ShowDialog(this);
                if (dlg.PickedColor is { } c)
                {
                    vm.SunColorR = c.R / 255.0;
                    vm.SunColorG = c.G / 255.0;
                    vm.SunColorB = c.B / 255.0;
                }
                break;
            }
            case Services.LightingSetting.Ambience:
            {
                byte r = (byte)Math.Clamp((int)(vm.SunAmbienceR * 255), 0, 255);
                byte g = (byte)Math.Clamp((int)(vm.SunAmbienceG * 255), 0, 255);
                byte b = (byte)Math.Clamp((int)(vm.SunAmbienceB * 255), 0, 255);
                var dlg = new ColorPickerDialog("Ambience", Avalonia.Media.Color.FromRgb(r, g, b));
                await dlg.ShowDialog(this);
                if (dlg.PickedColor is { } c)
                {
                    vm.SunAmbienceR = c.R / 255.0;
                    vm.SunAmbienceG = c.G / 255.0;
                    vm.SunAmbienceB = c.B / 255.0;
                }
                break;
            }
            case Services.LightingSetting.Multiplier:
            {
                var dlg = new SliderPopupDialog("Lighting Multiplier", new[]
                {
                    ("Mult", vm.LightingMultiplier, 0.0, 10.0),
                });
                await dlg.ShowDialog(this);
                if (dlg.Result is { Length: 1 } r) vm.LightingMultiplier = r[0];
                break;
            }
            case Services.LightingSetting.SunDirection:
            {
                var dlg = new SliderPopupDialog("Sun Direction", new[]
                {
                    ("X", vm.SunDirX, -1.0, 1.0),
                    ("Y", vm.SunDirY, -1.0, 1.0),
                    ("Z", vm.SunDirZ, -1.0, 1.0),
                });
                await dlg.ShowDialog(this);
                if (dlg.Result is { Length: 3 } r)
                {
                    vm.SunDirX = r[0];
                    vm.SunDirY = r[1];
                    vm.SunDirZ = r[2];
                }
                break;
            }
            case Services.LightingSetting.Bloom:
            {
                var dlg = new SliderPopupDialog("Bloom", new[]
                {
                    ("Bloom", vm.Bloom, 0.0, 1.0),
                });
                await dlg.ShowDialog(this);
                if (dlg.Result is { Length: 1 } r) vm.Bloom = r[0];
                break;
            }
            case Services.LightingSetting.FogStart:
            {
                var dlg = new SliderPopupDialog("Fog Start", new[]
                {
                    ("Start", vm.FogStart, 0.0, 2000.0),
                });
                await dlg.ShowDialog(this);
                if (dlg.Result is { Length: 1 } r) vm.FogStart = r[0];
                break;
            }
            case Services.LightingSetting.FogEnd:
            {
                var dlg = new SliderPopupDialog("Fog End", new[]
                {
                    ("End", vm.FogEnd, 0.0, 2000.0),
                });
                await dlg.ShowDialog(this);
                if (dlg.Result is { Length: 1 } r) vm.FogEnd = r[0];
                break;
            }
        }
    }

    /// <summary>
    /// Show the strata-replacement chooser when the user picks a library texture but the map has
    /// no free strata. Pre-populates the dialog with the map's current strata thumbnails, then
    /// forwards the user's choice back to the VM (or cancels cleanly).
    /// </summary>
    private async System.Threading.Tasks.Task PromptTextureReplacement(MainWindowViewModel vm, string newAlbedoPath)
    {
        var map = vm.CurrentMap;
        if (map == null) return;
        int maxPaintable = vm.MaxPaintableStrata();
        if (maxPaintable < 1) return;

        var library = vm.GetTextureLibrary();
        var thumbsByPath = library.Entries.ToDictionary(
            e => e.AlbedoPath,
            e => (entry: e, thumb: e.Thumbnail),
            StringComparer.OrdinalIgnoreCase);

        Avalonia.Media.Imaging.Bitmap? ResolveThumb(string? p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            var key = p.Replace('\\', '/').ToLowerInvariant();
            return thumbsByPath.TryGetValue(key, out var v) ? v.thumb : null;
        }

        var choices = new List<TextureReplaceDialog.StrataChoice>();
        for (int strata = 1; strata <= maxPaintable; strata++)
        {
            var path = map.TerrainTextures[strata].AlbedoPath ?? "";
            var thumb = ResolveThumb(path)
                        ?? Services.MapStrataPlaceholder.GetIcon(strata, !string.IsNullOrEmpty(path));
            var label = string.IsNullOrEmpty(path)
                ? "(empty)"
                : System.IO.Path.GetFileNameWithoutExtension(path);
            choices.Add(new TextureReplaceDialog.StrataChoice(
                strata, $"Strata {strata}", label, thumb));
        }

        var newKey = newAlbedoPath.Replace('\\', '/').ToLowerInvariant();
        var libEntry = thumbsByPath.TryGetValue(newKey, out var v2) ? v2.entry : null;
        var newName = libEntry?.Name ?? System.IO.Path.GetFileNameWithoutExtension(newAlbedoPath);
        var newThumb = libEntry?.Thumbnail
                       ?? Services.MapStrataPlaceholder.GetIcon(0, true);
        var dlg = new TextureReplaceDialog(newName, newThumb, choices, maxPaintable);
        await dlg.ShowDialog(this);
        if (dlg.SelectedStrata is int picked)
        {
            vm.ApplyStrataReplacement(picked, newAlbedoPath);
        }
        else
        {
            // User cancelled — clear the palette pick so a fresh click on the same texture fires again.
            vm.SelectedViewport3DTool = null;
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
