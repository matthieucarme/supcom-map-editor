using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Map-sharing primitives backed by a plain shared folder (e.g. a synced GDrive directory). One
/// folder per map on both sides; identity is the <see cref="MapMetadata.MapId"/> GUID written
/// into <c>metadata.json</c> on first upload. Atomic copies via a <c>.tmp</c> stage avoid other
/// users reading a partially-synced map.
/// </summary>
public static class MapShareService
{
    public enum SyncStatus { LocalOnly, RemoteOnly, UpToDate, Outdated }

    /// <summary>A remote map as discovered in <see cref="ListRemote"/>: where it lives + the
    /// metadata it ships with + the absolute thumbnail path (when present on disk).</summary>
    public record RemoteMap(string FolderPath, MapMetadata Metadata, string? ThumbnailPath);

    /// <summary>Pairing of a remote map with the matching local copy (if any) and the resulting
    /// status. Drives the dialog's Browse list — one row per remote map plus rows for local-only
    /// maps that have a metadata.json (so the user sees what they could publish).</summary>
    public record SyncEntry(MapMetadata? Local, MapMetadata? Remote, string? LocalFolder, string? RemoteFolder, string? ThumbnailPath, SyncStatus Status);

    // === LIST ==================================================================================

    /// <summary>Scan the remote root and return one entry per subfolder that has a metadata.json.
    /// Empty list when the path is null/missing/unreadable — never throws.</summary>
    public static List<RemoteMap> ListRemote(string? remoteRoot)
    {
        var result = new List<RemoteMap>();
        if (string.IsNullOrWhiteSpace(remoteRoot) || !Directory.Exists(remoteRoot)) return result;

        foreach (var dir in Directory.EnumerateDirectories(remoteRoot))
        {
            // Skip in-flight upload stages — they're not complete maps yet.
            if (dir.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            var metaPath = Path.Combine(dir, "metadata.json");
            var meta = MapMetadata.TryRead(metaPath);
            if (meta == null) continue;
            string? thumb = null;
            var thumbPath = Path.Combine(dir, meta.Thumbnail);
            if (File.Exists(thumbPath)) thumb = thumbPath;
            result.Add(new RemoteMap(dir, meta, thumb));
        }
        return result;
    }

    /// <summary>List local maps under <c>&lt;game&gt;/maps</c> that carry a metadata.json (i.e.
    /// have been shared at least once). Maps without metadata are local-only drafts and don't
    /// appear here — they're discoverable from the upload picker instead.</summary>
    public static List<(string Folder, MapMetadata Meta)> ListLocalShared(string? gameInstallPath)
    {
        var result = new List<(string, MapMetadata)>();
        if (string.IsNullOrWhiteSpace(gameInstallPath)) return result;
        var mapsRoot = Path.Combine(gameInstallPath, "maps");
        if (!Directory.Exists(mapsRoot)) return result;

        foreach (var dir in Directory.EnumerateDirectories(mapsRoot))
        {
            var metaPath = Path.Combine(dir, "metadata.json");
            var meta = MapMetadata.TryRead(metaPath);
            if (meta == null) continue;
            result.Add((dir, meta));
        }
        return result;
    }

    /// <summary>Cross-reference remote and local-shared maps by <see cref="MapMetadata.MapId"/>.
    /// Returns one row per distinct map: paired (remote + local), remote-only (downloadable),
    /// local-only (publishable). Status reflects update direction.</summary>
    public static List<SyncEntry> BuildSyncTable(string? gameInstallPath, string? remoteRoot)
    {
        var remotes = ListRemote(remoteRoot);
        var locals = ListLocalShared(gameInstallPath);
        var byId = new Dictionary<string, SyncEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in remotes)
        {
            byId[r.Metadata.MapId] = new SyncEntry(
                Local: null, Remote: r.Metadata,
                LocalFolder: null, RemoteFolder: r.FolderPath,
                ThumbnailPath: r.ThumbnailPath,
                Status: SyncStatus.RemoteOnly);
        }
        foreach (var (folder, meta) in locals)
        {
            if (byId.TryGetValue(meta.MapId, out var pair) && pair.Remote != null)
            {
                // Outdated = local hasn't synced to the remote's current updated_at. Falls back to
                // string comparison of ISO timestamps (lex-ordered = chronologically ordered).
                bool outdated = !string.IsNullOrEmpty(pair.Remote.UpdatedAt)
                    && string.CompareOrdinal(meta.LastSyncedAt, pair.Remote.UpdatedAt) < 0;
                byId[meta.MapId] = pair with
                {
                    Local = meta,
                    LocalFolder = folder,
                    Status = outdated ? SyncStatus.Outdated : SyncStatus.UpToDate,
                };
            }
            else
            {
                byId[meta.MapId] = new SyncEntry(
                    Local: meta, Remote: null,
                    LocalFolder: folder, RemoteFolder: null,
                    ThumbnailPath: null,
                    Status: SyncStatus.LocalOnly);
            }
        }
        return byId.Values.ToList();
    }

    // === UPLOAD ================================================================================

    /// <summary>Publish (or update) a local map to the remote share. Writes a fresh local
    /// metadata.json (creating <see cref="MapMetadata.MapId"/> on first upload), bumps
    /// <c>updated_at</c> to the current UTC time, stamps the SHA-256, and copies every file
    /// atomically into <c>&lt;remoteRoot&gt;/&lt;folderName&gt;/</c>. Returns the freshly written
    /// metadata for the caller to surface in the UI.</summary>
    public static MapMetadata Upload(
        string localMapFolder,
        string remoteRoot,
        string author,
        string description,
        string changelog,
        byte[]? thumbnailPng,
        int mapSize,
        int playerCount)
    {
        if (!Directory.Exists(localMapFolder)) throw new DirectoryNotFoundException(localMapFolder);
        if (string.IsNullOrWhiteSpace(remoteRoot)) throw new ArgumentException("Remote path not configured.", nameof(remoteRoot));
        Directory.CreateDirectory(remoteRoot);

        string folderName = new DirectoryInfo(localMapFolder).Name;
        var scmap = Directory.EnumerateFiles(localMapFolder, "*.scmap").FirstOrDefault()
                    ?? throw new FileNotFoundException("No .scmap in " + localMapFolder);

        // Read or create the local metadata.json — the GUID minted here is the cross-side identity.
        var localMetaPath = Path.Combine(localMapFolder, "metadata.json");
        var meta = MapMetadata.TryRead(localMetaPath) ?? new MapMetadata();
        meta.Name = Path.GetFileNameWithoutExtension(scmap);
        meta.Author = string.IsNullOrWhiteSpace(author) ? Environment.UserName : author;
        meta.UpdatedAt = DateTime.UtcNow.ToString("o");
        meta.Size = mapSize;
        meta.Players = playerCount;
        meta.Description = description ?? string.Empty;
        meta.Changelog = changelog ?? string.Empty;
        meta.Sha256 = MapMetadata.ComputeSha256(scmap);
        meta.LastSyncedAt = meta.UpdatedAt; // a fresh upload is, by definition, up-to-date locally
        meta.Write(localMetaPath);

        // Stage every payload file in a sibling .tmp folder, then atomic-mv to the final name.
        // Using folderName.tmp instead of an in-place write means partially-uploaded maps never
        // appear in ListRemote (we skip .tmp dirs there).
        string finalFolder = Path.Combine(remoteRoot, folderName);
        string tmpFolder = finalFolder + ".tmp";
        if (Directory.Exists(tmpFolder)) Directory.Delete(tmpFolder, recursive: true);
        Directory.CreateDirectory(tmpFolder);

        // Copy the four SC files + metadata + thumbnail.
        foreach (var f in Directory.EnumerateFiles(localMapFolder))
        {
            var name = Path.GetFileName(f);
            // Skip thumbnail.png from the source — we re-write a fresh one below from the editor.
            if (string.Equals(name, meta.Thumbnail, StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(f, Path.Combine(tmpFolder, name), overwrite: true);
        }
        if (thumbnailPng != null)
            File.WriteAllBytes(Path.Combine(tmpFolder, meta.Thumbnail), thumbnailPng);
        // Re-write metadata.json on the remote side from the freshly-stamped object so the local
        // copy and the remote copy match byte-for-byte.
        meta.Write(Path.Combine(tmpFolder, "metadata.json"));

        if (Directory.Exists(finalFolder)) Directory.Delete(finalFolder, recursive: true);
        Directory.Move(tmpFolder, finalFolder);

        return meta;
    }

    // === DOWNLOAD ==============================================================================

    /// <summary>Pull a remote map to <c>&lt;gameInstallPath&gt;/maps/&lt;folderName&gt;/</c>.
    /// Atomic via .tmp staging on the local side as well, so an interrupted download doesn't
    /// leave SC1 with a half-written map. Updates the local metadata's <c>last_synced_at</c> so
    /// the next sync table flags it as up-to-date.</summary>
    public static string Download(string remoteFolder, string gameInstallPath)
    {
        if (!Directory.Exists(remoteFolder)) throw new DirectoryNotFoundException(remoteFolder);
        if (string.IsNullOrWhiteSpace(gameInstallPath)) throw new ArgumentException("Game install path missing.", nameof(gameInstallPath));

        string folderName = new DirectoryInfo(remoteFolder).Name;
        string mapsRoot = Path.Combine(gameInstallPath, "maps");
        Directory.CreateDirectory(mapsRoot);
        string finalLocal = Path.Combine(mapsRoot, folderName);
        string tmpLocal = finalLocal + ".tmp";
        if (Directory.Exists(tmpLocal)) Directory.Delete(tmpLocal, recursive: true);
        Directory.CreateDirectory(tmpLocal);

        foreach (var f in Directory.EnumerateFiles(remoteFolder))
        {
            var name = Path.GetFileName(f);
            File.Copy(f, Path.Combine(tmpLocal, name), overwrite: true);
        }

        // Update last_synced_at = remote updated_at so the sync table flips to "Up-to-date" until
        // the remote moves on again.
        var localMetaPath = Path.Combine(tmpLocal, "metadata.json");
        var meta = MapMetadata.TryRead(localMetaPath);
        if (meta != null)
        {
            meta.LastSyncedAt = meta.UpdatedAt;
            meta.Write(localMetaPath);
        }

        if (Directory.Exists(finalLocal)) Directory.Delete(finalLocal, recursive: true);
        Directory.Move(tmpLocal, finalLocal);
        return finalLocal;
    }
}
