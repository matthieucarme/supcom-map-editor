using Avalonia;

namespace SupremeCommanderEditor.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        SupremeCommanderEditor.Core.Services.DebugLog.Reset();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
