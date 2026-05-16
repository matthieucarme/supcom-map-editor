using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SupremeCommanderEditor.App.Controls;
using SupremeCommanderEditor.App.ViewModels;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.App.Views;

/// <summary>
/// Map-sharing dialog. Browse tab lists every remote map and lets the user download/update; the
/// Upload tab publishes the currently-open map. The remote root and author handle are surfaced
/// at the top of the dialog so they can be configured without diving into Settings — first-time
/// users hit a single dialog to get going.
/// </summary>
public partial class MapShareDialog : Window
{
    private readonly MainWindowViewModel? _vm;
    private readonly SkiaMapControl? _skiaViewport;

    private ObservableCollection<MapShareEntryVm> _allEntries = new();
    private ObservableCollection<MapShareEntryVm> _visibleEntries = new();

    public MapShareDialog() : this(null, null) { }

    public MapShareDialog(MainWindowViewModel? vm, SkiaMapControl? skiaViewport)
    {
        InitializeComponent();
        _vm = vm;
        _skiaViewport = skiaViewport;

        // Initialise from current settings — the user can edit and Save() runs on close.
        if (_vm != null)
        {
            RemotePathBox.Text = _vm.Settings.RemoteMapsPath ?? string.Empty;
            HandleBox.Text = _vm.Settings.AuthorHandle ?? string.Empty;
        }

        EntriesList.ItemsSource = _visibleEntries;

        Loaded += (_, _) =>
        {
            PopulateUploadTab();
            RefreshList();
        };
    }

    // ============================================================================================
    // List / filters
    // ============================================================================================

    private void RefreshList()
    {
        SaveSettingsFromBoxes();
        _allEntries.Clear();
        _visibleEntries.Clear();

        if (_vm == null) return;
        var table = MapShareService.BuildSyncTable(_vm.GameData.GamePath, _vm.Settings.RemoteMapsPath);
        foreach (var entry in table)
            _allEntries.Add(MapShareEntryVm.From(entry));

        ApplyFilters();
        UpdateStatus();
    }

    private void ApplyFilters()
    {
        _visibleEntries.Clear();
        bool wantDl = FilterDownloadable.IsChecked == true;
        bool wantOu = FilterOutdated.IsChecked == true;
        bool wantUp = FilterUpToDate.IsChecked == true;
        bool wantLo = FilterLocalOnly.IsChecked == true;
        foreach (var e in _allEntries)
        {
            bool show = e.Status switch
            {
                MapShareService.SyncStatus.RemoteOnly => wantDl,
                MapShareService.SyncStatus.Outdated   => wantOu,
                MapShareService.SyncStatus.UpToDate   => wantUp,
                MapShareService.SyncStatus.LocalOnly  => wantLo,
                _ => true,
            };
            if (show) _visibleEntries.Add(e);
        }
    }

    private void UpdateStatus()
    {
        int total = _allEntries.Count;
        int outdated = _allEntries.Count(e => e.Status == MapShareService.SyncStatus.Outdated);
        int downloadable = _allEntries.Count(e => e.Status == MapShareService.SyncStatus.RemoteOnly);
        if (_vm == null || string.IsNullOrWhiteSpace(_vm.Settings.RemoteMapsPath))
            StatusBlock.Text = "Set a remote folder above to start sharing maps.";
        else if (total == 0)
            StatusBlock.Text = "No shared maps found at the remote folder.";
        else
            StatusBlock.Text = $"{total} map(s) — {downloadable} to download, {outdated} update(s) available.";
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => RefreshList();
    private void OnFilterChanged(object? sender, RoutedEventArgs e) => ApplyFilters();

    // ============================================================================================
    // Browse → Download / Update
    // ============================================================================================

    private void OnEntryAction(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not Button btn || btn.Tag is not string mapId) return;
        var entry = _allEntries.FirstOrDefault(x => x.MapId == mapId);
        if (entry == null) return;

        try
        {
            if (entry.Status == MapShareService.SyncStatus.LocalOnly)
            {
                // Local-only rows expose an "Upload now" action — switch to the upload tab pre-filled.
                // We can only upload the currently-open map (we need a thumbnail), so steer the user
                // there instead of uploading silently from the wrong map.
                StatusBlock.Text = $"Open '{entry.Title}' and use the Upload tab to publish it.";
                return;
            }

            if (string.IsNullOrWhiteSpace(_vm.GameData.GamePath))
            {
                StatusBlock.Text = "Game install path unknown — set it via Settings → Set game folder first.";
                return;
            }
            if (entry.RemoteFolder == null) return;

            var local = MapShareService.Download(entry.RemoteFolder, _vm.GameData.GamePath!);
            StatusBlock.Text = $"Downloaded '{entry.Title}' to {local}";
            RefreshList();
        }
        catch (Exception ex)
        {
            StatusBlock.Text = "Error: " + ex.Message;
        }
    }

    // ============================================================================================
    // Upload tab
    // ============================================================================================

    private void PopulateUploadTab()
    {
        if (_vm == null || _vm.CurrentMap == null)
        {
            UploadMapNameBlock.Text = "No map open — load a map to upload it.";
            UploadMapDetailsBlock.Text = string.Empty;
            UploadButton.IsEnabled = false;
            return;
        }
        UploadMapNameBlock.Text = _vm.MapName;
        UploadMapDetailsBlock.Text =
            $"{_vm.CurrentMap.Heightmap.Width}×{_vm.CurrentMap.Heightmap.Height} — {_vm.CurrentMap.Info.Armies.Count} player(s)";

        // Pre-fill description from the scenario.lua so the user can tweak rather than start blank.
        if (string.IsNullOrEmpty(DescriptionBox.Text))
            DescriptionBox.Text = _vm.CurrentMap.Info.Description ?? string.Empty;
    }

    private async void OnUpload(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || _vm.CurrentMap == null)
        {
            StatusBlock.Text = "No map open to upload.";
            return;
        }
        SaveSettingsFromBoxes();
        if (string.IsNullOrWhiteSpace(_vm.Settings.RemoteMapsPath))
        {
            StatusBlock.Text = "Set a remote folder above before uploading.";
            return;
        }

        // The map needs to be saved at least once so its canonical folder exists on disk.
        var (folder, err) = _vm.GetCanonicalSaveFolder();
        if (folder == null || !System.IO.Directory.Exists(folder))
        {
            StatusBlock.Text = err ?? "Save the map first — its folder doesn't exist yet.";
            return;
        }

        try
        {
            byte[]? thumb = _skiaViewport?.CaptureThumbnailPng();
            var meta = MapShareService.Upload(
                localMapFolder: folder,
                remoteRoot: _vm.Settings.RemoteMapsPath!,
                author: _vm.Settings.AuthorHandle ?? Environment.UserName,
                description: DescriptionBox.Text ?? string.Empty,
                changelog: ChangelogBox.Text ?? string.Empty,
                thumbnailPng: thumb,
                mapSize: _vm.CurrentMap.Heightmap.Width,
                playerCount: _vm.CurrentMap.Info.Armies.Count);
            StatusBlock.Text = $"Uploaded '{meta.Name}' at {meta.UpdatedAt}";
            ChangelogBox.Text = string.Empty;
            RefreshList();
        }
        catch (Exception ex)
        {
            StatusBlock.Text = "Upload failed: " + ex.Message;
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    // ============================================================================================
    // Settings (remote path + handle)
    // ============================================================================================

    private async void OnPickRemoteFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the shared maps folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (path != null) RemotePathBox.Text = path;
        SaveSettingsFromBoxes();
        RefreshList();
    }

    private void SaveSettingsFromBoxes()
    {
        if (_vm == null) return;
        _vm.Settings.RemoteMapsPath = string.IsNullOrWhiteSpace(RemotePathBox.Text) ? null : RemotePathBox.Text;
        _vm.Settings.AuthorHandle = string.IsNullOrWhiteSpace(HandleBox.Text) ? null : HandleBox.Text;
        _vm.Settings.Save();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        SaveSettingsFromBoxes();
        Close();
    }

    // Row view-model lives in MapShareEntryVm.cs — kept as a separate top-level class because
    // Avalonia 12's compiled bindings can't reference nested types from a DataTemplate.
}
