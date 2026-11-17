using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public static class CsvWriter
{
    private static readonly Regex WindowsForbidden = new(@"[<>:""/\\|?*\x00-\x1F]", RegexOptions.Compiled);

    public static string Build(IReadOnlyList<PersonRecord> persons, AppConfig? config = null)
    {
        config ??= ConfigStore.Current;
        var delim = string.IsNullOrEmpty(config.Output.CsvDelimiter) ? ";" : config.Output.CsvDelimiter;
        var columns = config.Columns.Where(c => c.ExportEnabled).ToList();
        var sb = new StringBuilder();

        if (config.Output.Headers)
            sb.Append(string.Join(delim, columns.Select(c => SanitizeEntry(c.ExportName, delim))))
              .Append("\r\n");

        foreach (var p in persons
            .OrderBy(p => p.SchoolId ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.IsStudent ? 0 : 1)
            .ThenBy(p => p.ClassName ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase))
        {
            var values = new List<string>(columns.Count);
            foreach (var col in columns)
                values.Add(SanitizeEntry(FormatColumn(col.Field, p, config), delim));
            sb.Append(string.Join(delim, values)).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string FormatColumn(string field, PersonRecord p, AppConfig cfg) => field switch
    {
        "sid"           => p.SchoolId ?? "",
        "sgid"          => p.SgId ?? "",
        "soid"          => p.SoId ?? "",
        "school_name"   => p.SchoolName ?? "",
        "school_type"   => p.SchoolType ?? "",
        "class"         => string.Join(cfg.Output.ListSeparator, BuildClassLabels(p, cfg)),
        "grade"         => p.Grade ?? "",
        "lkid"          => p.Lkid,
        "last_name"     => p.LastName,
        "first_name"    => p.FirstName,
        "date_of_birth" => FormatDate(p.DateOfBirth, cfg.Output.DateFormat),
        "username"      => p.Username ?? "",
        "apple_id"      => p.AppleId ?? "",
        "email"         => p.Email ?? "",
        "system_email"  => p.SystemEmail ?? "",
        "vce_type"      => p.VceType?.ToString() ?? "",
        "roles"         => string.Join(cfg.Output.ListSeparator, BuildRoleLabels(p, cfg)),
        "flags"         => string.Join(cfg.Output.ListSeparator, BuildFlagsColumn(p, cfg)),
        _               => "",
    };

    public static IEnumerable<string> BuildClassLabels(PersonRecord p, AppConfig? config = null)
    {
        config ??= ConfigStore.Current;
        var soid = p.SoId ?? "";
        if (string.IsNullOrWhiteSpace(p.ClassName) || string.IsNullOrWhiteSpace(soid))
            yield break;

        foreach (var raw in p.ClassName.Split(new[] { ',', '|' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = raw.Contains('-') ? raw[(raw.IndexOf('-') + 1)..] : raw;
            if (!string.IsNullOrEmpty(name))
                yield return $"{soid}-{SanitizeClassName(name, config)}";
        }
    }

    public static IEnumerable<string> BuildRoleLabels(PersonRecord p, AppConfig? config = null)
    {
        config ??= ConfigStore.Current;
        var soid = p.SoId ?? "";
        if (string.IsNullOrWhiteSpace(soid) || p.Roles.Count == 0)
            yield break;

        foreach (var r in p.Roles.Distinct().OrderBy(x => x))
            yield return $"{soid}-{r}";
    }

    /// <summary>
    /// Flags column content. When <c>output.flags_include_roles</c> is true
    /// (default), the role ids (Schueler, Lehrer, Sonstige) are prepended
    /// before the flag ids. This is a temporary convenience for the
    /// downstream system — turn it off in the config when the web app
    /// carries roles in their own column.
    /// </summary>
    public static IEnumerable<string> BuildFlagsColumn(PersonRecord p, AppConfig? config = null)
    {
        config ??= ConfigStore.Current;
        var items = new List<string>();
        if (config.Output.FlagsIncludeRoles)
            items.AddRange(p.Roles.Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        items.AddRange(p.Flags.Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        return items;
    }

    private static string FormatDate(string? iso, string fmt)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParseExact(iso, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString(fmt);
        return iso;
    }

    /// <summary>
    /// Strips any character Windows forbids in a folder name. Applied to
    /// class names before they are emitted so downstream tools can safely
    /// use them as directory names.
    /// </summary>
    public static string SanitizeClassName(string name, AppConfig? config = null)
    {
        config ??= ConfigStore.Current;
        if (!config.Output.SanitizeFilenames) return name;
        var result = WindowsForbidden.Replace(name ?? "", "");
        result = result.TrimEnd('.', ' ');
        return string.IsNullOrEmpty(result) ? "_" : result;
    }

    /// <summary>
    /// Removes quotation marks and any occurrences of the CSV delimiter
    /// inside a cell, since the export is unquoted.
    /// </summary>
    private static string SanitizeEntry(string v, string delimiter)
    {
        if (string.IsNullOrEmpty(v)) return v;
        var s = v
            .Replace("\"", "")
            .Replace("\r", " ")
            .Replace("\n", " ");
        if (!string.IsNullOrEmpty(delimiter))
            s = s.Replace(delimiter, ",");
        return s;
    }
}
