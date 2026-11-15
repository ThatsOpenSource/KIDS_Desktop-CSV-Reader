using System.Text;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public static class CsvWriter
{
    /// <summary>
    /// List separator inside a single cell. Must match the Node app's
    /// /api/export/csv output so the downstream consumer sees identical
    /// bytes regardless of which tool produced the file.
    /// </summary>
    public const char ListSeparator = ',';

    private static readonly string[] Headers =
    {
        "SID", "SGID", "SOID", "Schulname", "Schultyp",
        "Klasse", "Stufe", "LKID", "Name", "Vorname",
        "Benutzername", "Apple ID", "E-Mail", "System-E-Mail",
        "Rolle", "Flags",
    };

    public static string Build(IReadOnlyList<PersonRecord> persons)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(";", Headers.Select(Escape))).Append("\r\n");

        var sorted = persons
            .OrderBy(p => p.SchoolId ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.IsStudent ? 0 : 1)
            .ThenBy(p => p.ClassName ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var p in sorted)
        {
            var classes = BuildClassLabels(p);
            var roles = BuildRoleLabels(p);
            var flags = p.Flags.Distinct().OrderBy(x => x).ToList();

            var row = new[]
            {
                p.SchoolId ?? "",
                p.SgId ?? "",
                p.SoId ?? "",
                p.SchoolName ?? "",
                p.SchoolType ?? "",
                string.Join(ListSeparator, classes),
                p.Grade ?? "",
                p.Lkid,
                p.LastName,
                p.FirstName,
                p.Username ?? "",
                p.AppleId ?? "",
                p.Email ?? "",
                p.SystemEmail ?? "",
                string.Join(ListSeparator, roles),
                string.Join(ListSeparator, flags),
            };
            sb.Append(string.Join(";", row.Select(Escape))).Append("\r\n");
        }

        return sb.ToString();
    }

    public static IEnumerable<string> BuildClassLabels(PersonRecord p)
    {
        var soid = p.SoId ?? "";
        if (string.IsNullOrWhiteSpace(p.ClassName) || string.IsNullOrWhiteSpace(soid))
            yield break;

        // Accept both ',' (current) and '|' (any file written by the buggy
        // intermediate version) on the way back in.
        foreach (var raw in p.ClassName.Split(new[] { ',', '|' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = raw.Contains('-') ? raw[(raw.IndexOf('-') + 1)..] : raw;
            if (!string.IsNullOrEmpty(name))
                yield return $"{soid}-{name}";
        }
    }

    public static IEnumerable<string> BuildRoleLabels(PersonRecord p)
    {
        var soid = p.SoId ?? "";
        if (string.IsNullOrWhiteSpace(soid) || p.Roles.Count == 0)
            yield break;

        foreach (var r in p.Roles.Distinct().OrderBy(x => x))
            yield return $"{soid}-{r}";
    }

    /// <summary>
    /// Every field is always quoted. That is what makes ',' inside a list
    /// safe: quote-aware readers see it as data, not as a delimiter.
    /// </summary>
    private static string Escape(string? v)
    {
        v ??= "";
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
}
