using System.IO;
using System.Text.Json;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public static class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string FilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KidsXmlToCsv");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "window.json");
    }

    public static AppState Load()
    {
        try
        {
            var p = FilePath();
            if (File.Exists(p))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(p)) ?? new AppState();
        }
        catch { /* fall through */ }
        return new AppState();
    }

    public static void Save(AppState state)
    {
        try { File.WriteAllText(FilePath(), JsonSerializer.Serialize(state, JsonOpts)); }
        catch { /* best effort */ }
    }
}
