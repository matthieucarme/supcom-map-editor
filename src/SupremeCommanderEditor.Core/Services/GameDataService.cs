using System.IO.Compression;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Locates the Supreme Commander installation and extracts assets from SCD (ZIP) archives.
/// </summary>
public class GameDataService
{
    private readonly Dictionary<string, ZipArchive> _archives = new();
    private string? _gamePath;

    public string? GamePath => _gamePath;
    public bool IsInitialized => _gamePath != null;
    public int ArchiveCount => _archives.Count;
    public IEnumerable<string> ArchiveNames => _archives.Keys;

    /// <summary>
    /// Enumerate game-data paths whose lowercase filename contains all the given substrings.
    /// Used to discover marker/icon assets without hard-coding paths.
    /// </summary>
    public IEnumerable<string> FindFiles(params string[] requireSubstrings)
    {
        foreach (var archive in _archives.Values)
        {
            foreach (var entry in archive.Entries)
            {
                var lower = entry.FullName.ToLowerInvariant();
                bool ok = true;
                foreach (var s in requireSubstrings)
                {
                    if (!lower.Contains(s.ToLowerInvariant())) { ok = false; break; }
                }
                if (ok) yield return "/" + entry.FullName.Replace('\\', '/');
            }
        }
    }

    public bool TryInitialize(string? gamePath = null)
    {
        _gamePath = gamePath ?? FindGamePath();
        if (_gamePath == null) return false;

        var gamedataDir = Path.Combine(_gamePath, "gamedata");
        if (!Directory.Exists(gamedataDir)) return false;

        foreach (var scd in Directory.GetFiles(gamedataDir, "*.scd"))
        {
            try
            {
                var archive = ZipFile.OpenRead(scd);
                _archives[Path.GetFileName(scd)] = archive;
            }
            catch { /* skip broken archives */ }
        }

        return _archives.Count > 0;
    }

    /// <summary>
    /// Load a file from the game data. Path format: /env/evergreen2/layers/foo.dds
    /// </summary>
    public byte[]? LoadFile(string gamePath)
    {
        if (!IsInitialized) return null;

        // Normalize path: remove leading slash, convert to forward slashes
        var normalized = gamePath.TrimStart('/').Replace('\\', '/');

        foreach (var archive in _archives.Values)
        {
            // Try exact match first
            var entry = archive.GetEntry(normalized);

            // Case-insensitive fallback (SCD archives may have different casing)
            if (entry == null)
            {
                var lower = normalized.ToLowerInvariant();
                entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.ToLowerInvariant() == lower);
            }

            if (entry != null)
            {
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        return null;
    }

    /// <summary>
    /// Load a DDS texture from game data and return raw RGBA pixels + dimensions.
    /// </summary>
    public (byte[] Pixels, int Width, int Height)? LoadTextureDds(string gamePath)
    {
        var ddsBytes = LoadFile(gamePath);
        if (ddsBytes == null || ddsBytes.Length < 128) return null;

        try
        {
            using var image = Pfim.Pfimage.FromStream(new MemoryStream(ddsBytes));
            if (image.Data == null) return null;

            int pixelCount = image.Width * image.Height;
            byte[] rgba = new byte[pixelCount * 4];

            if (image.Format == Pfim.ImageFormat.Rgb24)
            {
                for (int i = 0, j = 0; j < rgba.Length && i + 2 < image.Data.Length; i += 3, j += 4)
                {
                    rgba[j] = image.Data[i + 2];
                    rgba[j + 1] = image.Data[i + 1];
                    rgba[j + 2] = image.Data[i];
                    rgba[j + 3] = 255;
                }
            }
            else if (image.Data.Length >= pixelCount * 4)
            {
                // Pfim outputs BGRA — swap to RGBA
                for (int i = 0; i < pixelCount * 4; i += 4)
                {
                    rgba[i]     = image.Data[i + 2]; // R ← B
                    rgba[i + 1] = image.Data[i + 1]; // G
                    rgba[i + 2] = image.Data[i];     // B ← R
                    rgba[i + 3] = image.Data[i + 3]; // A
                }
            }
            else
            {
                return null;
            }

            return (rgba, image.Width, image.Height);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindGamePath()
    {
        foreach (var lib in SteamLibraryDirs())
        {
            // Steam folder names: vanilla "Supreme Commander", FA "Supreme Commander Forged Alliance"
            foreach (var name in new[] { "Supreme Commander Forged Alliance", "Supreme Commander" })
            {
                var candidate = Path.Combine(lib, "steamapps", "common", name);
                if (IsValidGameDir(candidate)) return candidate;
            }
        }

        // Non-Steam fallbacks (GOG, manual installs on common drive letters)
        var driveLetters = new[] { "C", "D", "E", "F", "G", "H" };
        var subpaths = new[]
        {
            @"Program Files (x86)\Steam\steamapps\common\Supreme Commander Forged Alliance",
            @"Program Files (x86)\Steam\steamapps\common\Supreme Commander",
            @"Program Files\Steam\steamapps\common\Supreme Commander Forged Alliance",
            @"Program Files\Steam\steamapps\common\Supreme Commander",
            @"GOG Games\Supreme Commander Forged Alliance",
            @"Games\Supreme Commander Forged Alliance",
            @"Games\Supreme Commander",
        };
        foreach (var letter in driveLetters)
            foreach (var sub in subpaths)
            {
                var p = $@"{letter}:\{sub}";
                if (IsValidGameDir(p)) return p;
            }

        // Linux Steam default
        var linuxSteam = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/Steam/steamapps/common/Supreme Commander Forged Alliance");
        if (IsValidGameDir(linuxSteam)) return linuxSteam;
        linuxSteam = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/Steam/steamapps/common/Supreme Commander");
        if (IsValidGameDir(linuxSteam)) return linuxSteam;

        return null;
    }

    private static bool IsValidGameDir(string path) =>
        Directory.Exists(path) && Directory.Exists(Path.Combine(path, "gamedata"));

    /// <summary>
    /// Enumerate Steam library roots by parsing libraryfolders.vdf in the main Steam install.
    /// On Windows the Steam install is looked up via registry; on Linux it's the default ~/.steam path.
    /// </summary>
    private static IEnumerable<string> SteamLibraryDirs()
    {
        string? steamRoot = FindSteamRoot();
        if (steamRoot == null) yield break;
        yield return steamRoot;

        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) yield break;

        string text;
        try { text = File.ReadAllText(vdfPath); } catch { yield break; }

        // libraryfolders.vdf uses "path"  "C:\\Some\\Lib" entries. Match each.
        var rx = new System.Text.RegularExpressions.Regex(
            "\"path\"\\s*\"([^\"]+)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
        {
            var p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(p)) yield return p;
        }
    }

    private static string? FindSteamRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            // Registry lookups via reflection-free p/invoke would add platform-specific deps. Instead
            // probe the well-known install locations directly; covers ~99% of setups.
            foreach (var candidate in new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                @"D:\Steam",
                @"D:\Program Files (x86)\Steam",
                @"E:\Steam",
                @"F:\Steam",
            })
            {
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var sub in new[] { ".local/share/Steam", ".steam/steam", ".steam/root" })
        {
            var p = Path.Combine(home, sub);
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var archive in _archives.Values)
            archive.Dispose();
        _archives.Clear();
        _gamePath = null;
    }
}
