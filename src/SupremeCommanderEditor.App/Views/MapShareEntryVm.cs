using Avalonia.Media;
using Avalonia.Media.Imaging;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.App.Views;

/// <summary>Row-level view-model for <see cref="MapShareDialog"/>'s Browse list. Flattens a
/// <see cref="MapShareService.SyncEntry"/> into bindable fields the DataTemplate consumes. Lives
/// in its own file because Avalonia 12's compiled bindings need a top-level type to reference
/// — nested classes can't be addressed from XAML.</summary>
public class MapShareEntryVm
{
    public string MapId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string SubTitle { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ChangelogDisplay { get; init; } = string.Empty;
    public bool HasChangelog => !string.IsNullOrWhiteSpace(ChangelogDisplay);
    public Bitmap? Thumbnail { get; init; }
    public MapShareService.SyncStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public IBrush StatusColor { get; init; } = Brushes.Gray;
    public string ActionLabel { get; init; } = string.Empty;
    public bool ActionEnabled { get; init; } = true;
    public string? RemoteFolder { get; init; }

    public static MapShareEntryVm From(MapShareService.SyncEntry e)
    {
        var meta = e.Remote ?? e.Local!;
        var (label, color) = e.Status switch
        {
            MapShareService.SyncStatus.RemoteOnly => ("Available", (IBrush)Brushes.LightSkyBlue),
            MapShareService.SyncStatus.Outdated   => ("Update available", Brushes.Goldenrod),
            MapShareService.SyncStatus.UpToDate   => ("Up-to-date", Brushes.LightGreen),
            MapShareService.SyncStatus.LocalOnly  => ("Local only", Brushes.LightGray),
            _ => ("", Brushes.Gray),
        };
        string action = e.Status switch
        {
            MapShareService.SyncStatus.RemoteOnly => "Download",
            MapShareService.SyncStatus.Outdated   => "Update",
            MapShareService.SyncStatus.UpToDate   => "Re-download",
            MapShareService.SyncStatus.LocalOnly  => "Show how to upload",
            _ => "—",
        };
        Bitmap? thumb = null;
        if (e.ThumbnailPath != null)
        {
            try { thumb = new Bitmap(e.ThumbnailPath); } catch { /* keep null */ }
        }
        return new MapShareEntryVm
        {
            MapId = meta.MapId,
            Title = string.IsNullOrEmpty(meta.Name) ? "(unnamed)" : meta.Name,
            SubTitle = FormatSubTitle(meta),
            Description = meta.Description,
            ChangelogDisplay = string.IsNullOrEmpty(meta.Changelog) ? "" : "Changelog: " + meta.Changelog,
            Thumbnail = thumb,
            Status = e.Status,
            StatusLabel = label,
            StatusColor = color,
            ActionLabel = action,
            ActionEnabled = true,
            RemoteFolder = e.RemoteFolder,
        };
    }

    private static string FormatSubTitle(MapMetadata m)
    {
        string author = string.IsNullOrEmpty(m.Author) ? "anonymous" : m.Author;
        string when = string.IsNullOrEmpty(m.UpdatedAt)
            ? "?"
            : (DateTime.TryParse(m.UpdatedAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : m.UpdatedAt);
        return $"by {author} • {m.Size}×{m.Size} • {m.Players} player(s) • {when}";
    }
}
