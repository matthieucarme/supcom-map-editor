namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Append-only debug log written to %APPDATA%\SupremeCommanderMapEditor\debug.log so we can
/// diagnose Windows-only rendering issues without a console attached to a published exe.
/// </summary>
public static class DebugLog
{
    private static readonly object _lock = new();
    private static string? _path;

    public static string Path
    {
        get
        {
            if (_path != null) return _path;
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SupremeCommanderMapEditor");
            Directory.CreateDirectory(dir);
            _path = System.IO.Path.Combine(dir, "debug.log");
            return _path;
        }
    }

    public static void Reset()
    {
        try { File.WriteAllText(Path, $"=== Session start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n"); }
        catch { /* non-fatal */ }
    }

    public static void Write(string line)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch { /* non-fatal */ }
    }
}
