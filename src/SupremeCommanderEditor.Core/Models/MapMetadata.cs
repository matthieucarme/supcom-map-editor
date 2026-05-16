using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupremeCommanderEditor.Core.Models;

/// <summary>
/// Sidecar file (<c>metadata.json</c>) written next to a map's .scmap on both the local and the
/// remote (shared GDrive) side. Lets the editor identify "the same map" across renames, compare
/// versions, and show enough info in the share dialog (thumbnail, description, player count)
/// without parsing the whole map.
///
/// First-upload bootstrap: <see cref="MapId"/> is generated as a fresh GUID and written to both
/// the local and the remote metadata so subsequent syncs match by stable identity rather than
/// folder name.
/// </summary>
public class MapMetadata
{
    /// <summary>Stable identity for this map across uploads, renames, and syncs. Generated on
    /// first upload and never rewritten. Tracked here as the GUID's default string form.</summary>
    [JsonPropertyName("map_id")]
    public string MapId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name as the user would search for it. Mirrors the editor's
    /// <c>MapName</c> field at upload time.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>ISO-8601 UTC timestamp at upload. Doubles as the "version" the user sees, and as
    /// the comparison key when the editor decides whether a local copy is outdated.</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>Heightmap dimension (256, 512, 1024, …). Useful for filtering and matches the SC1
    /// player-cap convention the editor already documents.</summary>
    [JsonPropertyName("size")]
    public int Size { get; set; }

    /// <summary>Player count derived from the scenario's army list at upload time.</summary>
    [JsonPropertyName("players")]
    public int Players { get; set; }

    /// <summary>One-line description shown in the browse list and as a tooltip in the dialog.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional changelog blurb provided by the uploader ("rebalanced mass spots"). Shown
    /// to users updating an existing local copy so they know what changed.</summary>
    [JsonPropertyName("changelog")]
    public string Changelog { get; set; } = string.Empty;

    /// <summary>SHA-256 of the .scmap file. Secondary check after <see cref="UpdatedAt"/>: even if
    /// timestamps drift, a hash mismatch confirms the binary actually changed.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Filename of the thumbnail PNG inside the same folder. Hardcoded to
    /// <c>thumbnail.png</c> in practice, exposed as a field so it's discoverable from JSON.</summary>
    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; set; } = "thumbnail.png";

    /// <summary>Last <see cref="UpdatedAt"/> the local copy was synced to. Stored only in the
    /// local metadata.json. Lets the dialog flag "remote has a newer version" without re-hashing
    /// every file every time.</summary>
    [JsonPropertyName("last_synced_at")]
    public string LastSyncedAt { get; set; } = string.Empty;

    public static MapMetadata? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MapMetadata>(json);
        }
        catch { return null; }
    }

    public void Write(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
