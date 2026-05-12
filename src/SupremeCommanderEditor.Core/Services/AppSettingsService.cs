using System.Text.Json;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Lightweight JSON-backed settings stored in the user's roaming app data
/// (Windows: %APPDATA%\SupremeCommanderMapEditor\settings.json).
/// </summary>
public class AppSettingsService
{
    public string? GameInstallPath { get; set; }

    private static string SettingsPath => GetPath();

    public static string GetPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SupremeCommanderMapEditor");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettingsService Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettingsService>(json) ?? new AppSettingsService();
            }
        }
        catch { /* fall through to defaults */ }
        return new AppSettingsService();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* non-fatal */ }
    }
}
