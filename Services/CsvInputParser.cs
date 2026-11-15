using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public static class CsvInputParser
{
    private static readonly Regex AppleIdRx = new(
        @"^[^\s@]+@appleid\.ds-greiz\.de$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SystemEmailRx = new(
        @"^[^\s@]+@\d+\.schulen-greiz\.de$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailRx = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
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

        for (int i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = ParseCsvLine(lines[i], delimiter);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < headers.Count && j < values.Count; j++)
                dict[headers[j]] = values[j].Trim();

            var p = schema == Schema.KidsExport
                ? ConvertRowKids(dict, warnings, i + 1, sourceFile)
                : ConvertRowStep1(dict, warnings, i + 1, sourceFile);
            if (p != null) result.Add(p);
        }
        return result;
    }

    private static char DetectDelimiter(string headerLine)
    {
        int sc = 0, cc = 0;
        bool inQuote = false;
        foreach (var c in headerLine)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (inQuote) continue;
            if (c == ';') sc++;
            else if (c == ',') cc++;
        }
        return sc >= cc ? ';' : ',';
    }

    private static Schema DetectSchema(List<string> headers)
    {
        bool Has(string k) => headers.Any(h => h.Equals(k, StringComparison.OrdinalIgnoreCase));

        // KIDS export signature: SID + LKID + Rolle (not present in Step1)
        if (Has("SID") && Has("LKID") && Has("Rolle")) return Schema.KidsExport;

        // Fallback: Step1 (original school export)
        return Schema.Step1;
    }

    // ---------------- Shared helpers ----------------

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
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }
                inQuote = !inQuote;
                i++;
            }
            else if (c == delimiter && !inQuote)
            {
                fields.Add(sb.ToString());
                sb.Clear();
                i++;
            }
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
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    // ---------------- Step1 (school raw export) ----------------

    private static PersonRecord? ConvertRowStep1(Dictionary<string, string> d, List<string> warnings, int lineNo, string sourceFile)
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

        var flags = new List<string>();
        if (IsTruthy(dvv)) flags.Add("dvv");
        if (IsTruthy(sgPrt)) flags.Add("print");

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
            IsStudent = isSchueler,
            IsTeacher = isLehrer,
            Roles = roles,
            Flags = flags,
            Grade = ExtractGrade(klasse),
        };
    }

    // ---------------- KIDS export (round-trip) ----------------

    private static PersonRecord? ConvertRowKids(Dictionary<string, string> d, List<string> warnings, int lineNo, string sourceFile)
    {
        string G(string key) => d.TryGetValue(key, out var v) ? (v ?? "").Trim() : "";

        var lkid = G("LKID");
        if (string.IsNullOrWhiteSpace(lkid))
        {
            warnings.Add($"{sourceFile} Zeile {lineNo}: LKID fehlt – übersprungen.");
            return null;
        }

        var sid = G("SID");
        var sgid = G("SGID");
        var soid = G("SOID");
        var schulname = G("Schulname");
        var schultyp = G("Schultyp");
        var klasse = G("Klasse");
        var stufe = G("Stufe");
        var vorname = G("Vorname");
        var nachname = G("Name");
        var username = G("Benutzername");
        var appleId = G("Apple ID");
        var email = G("E-Mail");
        var systemEmail = G("System-E-Mail");
        var rolle = G("Rolle");
        var flagsRaw = G("Flags");

        // Strip "{soid}-" prefixes so the internal model holds bare names/ids.
        List<string> StripPrefix(IEnumerable<string> items) =>
            items.Select(s =>
            {
                if (!string.IsNullOrEmpty(soid) &&
                    s.StartsWith(soid + "-", StringComparison.OrdinalIgnoreCase))
                    return s[(soid.Length + 1)..];
                return s;
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var classes = StripPrefix(SplitList(klasse));
        var roles = StripPrefix(SplitList(rolle));
        var flags = SplitList(flagsRaw);

        return new PersonRecord
        {
            Lkid = lkid,
            FirstName = vorname,
            LastName = nachname,
            DateOfBirth = null,
            Username = string.IsNullOrWhiteSpace(username) ? null : username.ToLowerInvariant(),
            ClassName = classes.Count > 0 ? string.Join(CsvWriter.ListSeparator, classes) : null,
            SchoolId = string.IsNullOrWhiteSpace(sid) ? null : sid,
            SoId = string.IsNullOrWhiteSpace(soid) ? null : soid,
            SgId = string.IsNullOrWhiteSpace(sgid) ? null : sgid,
            SchoolName = string.IsNullOrWhiteSpace(schulname) ? null : schulname,
            SchoolType = string.IsNullOrWhiteSpace(schultyp) ? null : schultyp,
            SourceGuid = lkid,
            SourceFile = sourceFile,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.ToLowerInvariant(),
            SystemEmail = string.IsNullOrWhiteSpace(systemEmail) ? null : systemEmail.ToLowerInvariant(),
            AppleId = string.IsNullOrWhiteSpace(appleId) ? null : appleId.ToLowerInvariant(),
            Roles = roles,
            Flags = flags,
            Grade = string.IsNullOrWhiteSpace(stufe) ? null : stufe,
            IsStudent = roles.Contains("Schueler"),
            IsTeacher = roles.Contains("Lehrer"),
        };
    }

    // ---------------- Misc ----------------

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
