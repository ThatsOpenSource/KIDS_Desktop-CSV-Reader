using System;
using System.IO;
using Avalonia;

namespace KidsXmlToCsv;

internal static class Program
{
    private static string CrashLogPath =>
        Path.Combine(Path.GetTempPath(), "kids-sanitise-crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { File.WriteAllText(CrashLogPath, e.ExceptionObject?.ToString() ?? "unknown"); }
            catch { /* best effort */ }
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(CrashLogPath, ex.ToString()); } catch { }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
