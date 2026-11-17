using System.Text;

namespace KidsXmlToCsv.Services;

public sealed class TomlTable
{
    private readonly Dictionary<string, object> _data = new(StringComparer.Ordinal);

    public object? this[string key] { get => _data.GetValueOrDefault(key); set => _data[key] = value!; }
    public bool ContainsKey(string key) => _data.ContainsKey(key);
    public IEnumerable<string> Keys => _data.Keys;

    public string GetString(string key, string def = "") =>
        _data.TryGetValue(key, out var v) && v is string s ? s : def;
    public bool GetBool(string key, bool def = false) =>
        _data.TryGetValue(key, out var v) && v is bool b ? b : def;
    public int GetInt(string key, int def = 0) =>
        _data.TryGetValue(key, out var v) && v is int i ? i : def;
    public List<string> GetStringArray(string key) =>
        _data.TryGetValue(key, out var v) && v is List<string> l ? l : new();
    public List<TomlTable> GetTableArray(string key) =>
        _data.TryGetValue(key, out var v) && v is List<TomlTable> l ? l : new();
    public TomlTable? GetTable(string key) =>
        _data.TryGetValue(key, out var v) && v is TomlTable t ? t : null;
}

public static class TomlParser
{
    public static TomlTable Parse(string text)
    {
        var root = new TomlTable();
        TomlTable current = root;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("[[") && line.EndsWith("]]"))
            {
                var name = line[2..^2].Trim();
                List<TomlTable> list;
                if (root.ContainsKey(name) && root[name] is List<TomlTable> existing) list = existing;
                else { list = new(); root[name] = list; }
                var t = new TomlTable();
                list.Add(t);
                current = t;
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                var t = root.GetTable(name);
                if (t is null) { t = new(); root[name] = t; }
                current = t;
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim().Trim('"', '\'');
            var valStr = line[(eq + 1)..].Trim();
            current[key] = ParseValue(valStr);
        }

        return root;
    }

    private static string StripComment(string line)
    {
        bool inD = false, inS = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && !inS && (i == 0 || line[i - 1] != '\\')) inD = !inD;
            else if (c == '\'' && !inD) inS = !inS;
            else if (c == '#' && !inD && !inS) return line[..i];
        }
        return line;
    }

    private static object ParseValue(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return Unescape(s[1..^1]);
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'') return s[1..^1];
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.StartsWith('[') && s.EndsWith(']'))
        {
            var items = new List<string>();
            foreach (var part in SplitArray(s[1..^1]))
                if (part.Trim().Length > 0)
                    items.Add((string)ParseValue(part.Trim()));
            return items;
        }
        if (int.TryParse(s, out var i)) return i;
        return s;
    }

    private static IEnumerable<string> SplitArray(string inner)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool inD = false, inS = false;
        foreach (var c in inner)
        {
            if (c == '"' && !inS && (sb.Length == 0 || sb[^1] != '\\')) { inD = !inD; sb.Append(c); }
            else if (c == '\'' && !inD) { inS = !inS; sb.Append(c); }
            else if (c == ',' && !inD && !inS) { parts.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }

    private static string Unescape(string s) => s
        .Replace("\\n", "\n").Replace("\\t", "\t")
        .Replace("\\\"", "\"").Replace("\\\\", "\\");
}
