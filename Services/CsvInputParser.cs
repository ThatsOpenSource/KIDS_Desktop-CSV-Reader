using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public static class CsvInputParser
{
    private static readonly Regex AppleIdRx = new(@"^[^\s@]+@appleid\.ds-greiz\.de$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SystemEmailRx = new(@"^[^\s@]+@\d+\.schulen-greiz\.de$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmailRx = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled);

    private enum Schema { Step1, KidsExport }

    public static List<PersonRecord> Parse(string csvText, string sourceFile, List<string> warnings)
    {
        if (csvText.Length > 0 && csvText[0] == '\uFEFF') csvText = csvText[1..];

        var lines = SplitCsvLines(csvText);
        if (lines.Count < 2)
            throw new InvalidOperationException("CSV enthält keinen Header oder keine Daten.");

        var delimiter = DetectDelimiter(lines[0]);
        var headers = ParseCsvLine(lines[0], delimiter).Select(h => h.Trim()).ToList();
        var schema = DetectSchema(headers);
        var result = new List<PersonRecord>();
        var config = ConfigStore.Current;

        for (int i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = ParseCsvLine(lines[i], delimiter);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < headers.Count && j < values.Count; j++)
                dict[headers[j]] = values[j].Trim();

            var p = schema == Schema.KidsExport
                ? ConvertRowKids(dict, warnings, i + 1, sourceFile)
                : ConvertRowStep1(dict, warnings, i + 1, sourceFile, config);
            if (p != null) result.Add(p);
        }
        return result;
    }

    private static char DetectDelimiter(string headerLine)
    {
        int sc = 0, cc = 0; bool inQuote = false;
        foreach (var c in headerLine)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (inQuote) continue;
            if (c == ';') sc++; else if (c == ',') cc++;
        }
        return sc >= cc ? ';' : ',';
    }

    private static Schema DetectSchema(List<string> headers)
    {
        bool Has(string k) => headers.Any(h => h.Equals(k, StringComparison.OrdinalIgnoreCase));
        if (Has("SID") && Has("LKID") && Has("Rolle")) return Schema.KidsExport;
        return Schema.Step1;
    }

    private static bool IsTruthy(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return false;
        var v = val.Trim();
        return v.Equals("j", StringComparison.OrdinalIgnoreCase)
            || v.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || v.Equals("y", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v == "1";
    }

    private static bool IsValidEmail(string? s) =>
        !string.IsNullOrWhiteSpace(s) && EmailRx.IsMatch(s.Trim());

    private static List<string> SplitCsvLines(string text)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (var c in text)
        {
            if (c == '"') { inQuote = !inQuote; sb.Append(c); }
            else if (c == '\n' && !inQuote)
            {
                var line = sb.ToString();
                if (line.EndsWith('\r')) line = line[..^1];
                lines.Add(line);
                sb.Clear();
            }
            else sb.Append(c);
        }
        if (sb.Length > 0)
        {
            var line = sb.ToString();
            if (line.EndsWith('\r')) line = line[..^1];
            lines.Add(line);
        }
        return lines;
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
                { sb.Append('"'); i += 2; continue; }
                inQuote = !inQuote; i++;
            }
            else if (c == delimiter && !inQuote) { fields.Add(sb.ToString()); sb.Clear(); i++; }
            else { sb.Append(c); i++; }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static List<string> SplitList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw
            .Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    /// <summary>Looks up a flag alias in the config and returns its data_id.</summary>
    private static string? ResolveFlag(string raw, AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var f in config.Flags)
        {
            if (f.DataId.Equals(raw, StringComparison.OrdinalIgnoreCase)) return f.DataId;
            if (f.Aliases.Any(a => a.Equals(raw, StringComparison.OrdinalIgnoreCase))) return f.DataId;
        }
        return raw;
    }

    private static int? ResolveRoleVceType(string roleId, AppConfig config)
    {
        var r = config.Roles.FirstOrDefault(x => x.Id.Equals(roleId, StringComparison.OrdinalIgnoreCase));
        return r?.VceType > 0 ? r.VceType : null;
    }

    // ---------------- Step1 ----------------

    private static PersonRecord? ConvertRowStep1(Dictionary<string, string> d, List<string> warnings,
                                                 int lineNo, string sourceFile, AppConfig config)
    {
        string G(string key) => d.TryGetValue(key, out var v) ? (v ?? "").Trim() : "";

        var lkid = G("LK Greiz - ID");
        if (string.IsNullOrWhiteSpace(lkid))
        {
            warnings.Add($"{sourceFile} Zeile {lineNo}: LKID fehlt – übersprungen.");
            return null;
        }

        var schulnummer = G("Schulnummer");
        var kzSnr = G("KZ-SNr");
        var kzOu = G("KZ-OU");
        var schule = G("Schule");
        var klasse = G("Klasse");
        var nachname = G("Nachname");
        var vorname = G("Vorname");
        var geburtsdatum = G("Geburtsdatum");
        var benutzername = G("Benutzername");
        var benutzergruppe = G("Benutzergruppe");
        var mdmArt = G("MDM - Art");
        var dvv = G("DVV");
        var kontentyp = G("Kontentyp");
        var sgPrt = G("sg-prt");
        var mdmEmailRaw = G("MDM - E-Mail");
        var appleIdRaw = G("AppleID");

        var isLehrer = benutzergruppe.Equals("Lehrer", StringComparison.OrdinalIgnoreCase)
                    || kontentyp == "411"
                    || mdmArt.Equals("l", StringComparison.OrdinalIgnoreCase);
        var isSchueler = !isLehrer && (
                          benutzergruppe.Equals("Schueler", StringComparison.OrdinalIgnoreCase)
                       || benutzergruppe.Equals("Schüler", StringComparison.OrdinalIgnoreCase)
                       || kontentyp == "431"
                       || mdmArt.Equals("s", StringComparison.OrdinalIgnoreCase));

        var roles = new List<string>();
        if (isSchueler) roles.Add("Schueler");
        if (isLehrer) roles.Add("Lehrer");
        if (roles.Count == 0) roles.Add("Sonstige");

        int? vceType = null;
        if (int.TryParse(kontentyp, out var vt)) vceType = vt;
        if (vceType is null && roles.Count > 0)
            vceType = ResolveRoleVceType(roles[0], config);

        var flags = new List<string>();
        if (IsTruthy(dvv))
        {
            var resolved = ResolveFlag("dvv", config);
            if (resolved != null) flags.Add(resolved);
        }
        if (IsTruthy(sgPrt))
        {
            var resolved = ResolveFlag("print", config);
            if (resolved != null) flags.Add(resolved);
        }

        var candidates = new List<string>();
        foreach (var part in (mdmEmailRaw + ";" + appleIdRaw).Split(new[] { ';', ',' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim().ToLowerInvariant();
            if (IsValidEmail(t) && !candidates.Contains(t))
                candidates.Add(t);
        }

        string? appleId = null, systemEmail = null, privateEmail = null;
        foreach (var e in candidates)
        {
            if (appleId is null && AppleIdRx.IsMatch(e)) { appleId = e; continue; }
            if (systemEmail is null && SystemEmailRx.IsMatch(e)) { systemEmail = e; continue; }
            if (privateEmail is null) { privateEmail = e; }
        }

        return new PersonRecord
        {
            Lkid = lkid,
            FirstName = vorname,
            LastName = nachname,
            DateOfBirth = ParseGermanDate(geburtsdatum),
            Username = string.IsNullOrWhiteSpace(benutzername) ? null : benutzername.ToLowerInvariant(),
            ClassName = string.IsNullOrWhiteSpace(klasse) ? null : klasse,
            SchoolId = schulnummer,
            SgId = null,
            SoId = kzSnr,
            SchoolName = schule,
            SchoolType = ResolveSchoolType(kzOu, schule),
            SourceGuid = lkid,
            SourceFile = sourceFile,
            Email = privateEmail,
            SystemEmail = systemEmail,
            AppleId = appleId,
            VceType = vceType,
            IsStudent = isSchueler,
            IsTeacher = isLehrer,
            Roles = roles,
            Flags = flags,
            Grade = ExtractGrade(klasse),
        };
    }

    // ---------------- KIDS export round-trip ----------------

    private static PersonRecord? ConvertRowKids(Dictionary<string, string> d, List<string> warnings,
                                                 int lineNo, string sourceFile)
    {
        string G(string key) => d.TryGetValue(key, out var v) ? (v ?? "").Trim() : "";

        var lkid = G("LKID");
        if (string.IsNullOrWhiteSpace(lkid))
        {
            warnings.Add($"{sourceFile} Zeile {lineNo}: LKID fehlt – übersprungen.");
            return null;
        }

        var soid = G("SOID");
        List<string> StripSoId(IEnumerable<string> items) =>
            items.Select(s => !string.IsNullOrEmpty(soid)
                              && s.StartsWith(soid + "-", StringComparison.OrdinalIgnoreCase)
                                ? s[(soid.Length + 1)..] : s)
                 .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        var classes = StripSoId(SplitList(G("Klasse")));
        var roles = StripSoId(SplitList(G("Rolle")));
        var flags = SplitList(G("Flags")).Where(f => !roles.Contains(f)).ToList();

        int? vceType = null;
        if (int.TryParse(G("vceType"), out var vt)) vceType = vt;

        return new PersonRecord
        {
            Lkid = lkid,
            FirstName = G("Vorname"),
            LastName = G("Name"),
            DateOfBirth = ParseGermanDate(G("Geburtsdatum")),
            Username = string.IsNullOrWhiteSpace(G("Benutzername")) ? null : G("Benutzername").ToLowerInvariant(),
            ClassName = classes.Count > 0 ? string.Join('|', classes) : null,
            SchoolId = string.IsNullOrWhiteSpace(G("SID")) ? null : G("SID"),
            SoId = string.IsNullOrWhiteSpace(soid) ? null : soid,
            SgId = string.IsNullOrWhiteSpace(G("SGID")) ? null : G("SGID"),
            SchoolName = string.IsNullOrWhiteSpace(G("Schulname")) ? null : G("Schulname"),
            SchoolType = string.IsNullOrWhiteSpace(G("Schultyp")) ? null : G("Schultyp"),
            SourceGuid = lkid,
            SourceFile = sourceFile,
            Email = string.IsNullOrWhiteSpace(G("E-Mail")) ? null : G("E-Mail").ToLowerInvariant(),
            SystemEmail = string.IsNullOrWhiteSpace(G("System-E-Mail")) ? null : G("System-E-Mail").ToLowerInvariant(),
            AppleId = string.IsNullOrWhiteSpace(G("Apple ID")) ? null : G("Apple ID").ToLowerInvariant(),
            VceType = vceType,
            Roles = roles,
            Flags = flags,
            Grade = string.IsNullOrWhiteSpace(G("Stufe")) ? null : G("Stufe"),
            IsStudent = roles.Contains("Schueler"),
            IsTeacher = roles.Contains("Lehrer"),
        };
    }

    // ---------------- helpers ----------------

    private static string? ResolveSchoolType(string kzOu, string schule)
    {
        var k = kzOu.ToLowerInvariant();
        if (k.StartsWith("gym")) return "Gymnasium";
        if (k.StartsWith("gs"))  return "Grundschule";
        if (k.StartsWith("rs"))  return "Regelschule";
        if (k.StartsWith("bfs") || k.StartsWith("bs")) return "Berufsschule";
        if (k.StartsWith("fz") || k.StartsWith("föz")) return "Förderschule";

        var u = schule.ToUpperInvariant();
        if (u.Contains("GYM")) return "Gymnasium";
        if (u.Contains("GRUNDSCHULE")) return "Grundschule";
        if (u.Contains("REGELSCHULE")) return "Regelschule";
        if (u.Contains("BERUFSSCHULE") || u.Contains("BERUFSBILD")) return "Berufsschule";
        if (u.Contains("FÖRDERSCHULE") || u.Contains("FÖRDERZENTRUM")) return "Förderschule";
        return null;
    }

    private static string? ExtractGrade(string klasse)
    {
        if (string.IsNullOrWhiteSpace(klasse)) return null;
        var m = Regex.Match(klasse, @"^(\d{1,2})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? ParseGermanDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (DateTime.TryParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
            return d1.ToString("yyyy-MM-dd");
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
            return d2.ToString("yyyy-MM-dd");
        return null;
    }
}
