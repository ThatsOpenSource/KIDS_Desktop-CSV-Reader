using System;
using System.IO;
using System.Text;
using Avalonia;

namespace KidsXmlToCsv;

internal static class Program
{
    private static string CrashLogPath =>
        Path.Combine(Path.GetTempPath(), "kids-sanitise-crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        // Windows-1252 (ANSI) requires the code-pages provider on .NET.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
