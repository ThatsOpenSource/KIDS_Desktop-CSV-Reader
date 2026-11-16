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
        // Clean slate — no crash log for a clean run. Prevents accumulation.
        try { if (File.Exists(CrashLogPath)) File.Delete(CrashLogPath); } catch { }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(CrashLogPath,
                    $"Utc: {DateTime.UtcNow:O}\n{ex}\n");
            }
            catch { /* best effort */ }
            // No rethrow: the log is the artifact. The debug script reports its path.
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
