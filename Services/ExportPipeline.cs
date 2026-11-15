using System.IO;
using System.IO.Compression;
using System.Text;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public sealed record ProgressUpdate(string Message, int Done, int Total);

public sealed class ExportPackage
{
    public IReadOnlyList<PersonRecord> Persons { get; set; } = Array.Empty<PersonRecord>();
    public Dictionary<string, List<PersonRecord>> LkidDuplicates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<PersonRecord>> UsernameDuplicates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<PersonRecord>> AppleIdDuplicates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<PersonRecord>> SystemEmailDuplicates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<PersonRecord>> PersonIdentityDuplicates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; } = new();

    public int EmptyUsernameCount => Persons.Count(p => string.IsNullOrWhiteSpace(p.Username));
    public int EmptyAppleIdCount => Persons.Count(p => string.IsNullOrWhiteSpace(p.AppleId));
    public int EmptySystemEmailCount => Persons.Count(p => string.IsNullOrWhiteSpace(p.SystemEmail));
}

public sealed class ExportPipeline
{
    public ExportPackage Run(IReadOnlyList<string> filePaths, IProgress<ProgressUpdate>? progress = null)
    {
        var csvFiles = new List<(string Name, string Content)>();
        var totalSteps = Math.Max(1, filePaths.Count + 3);
        var done = 0;

        foreach (var path in filePaths)
        {
            progress?.Report(new ProgressUpdate($"Lese {Path.GetFileName(path)} …", done, totalSteps));
            foreach (var entry in ReadCsvFromFile(path)) csvFiles.Add(entry);
            done++;
            progress?.Report(new ProgressUpdate(
                $"{Path.GetFileName(path)} gelesen ({csvFiles.Count} CSV(s) gesamt)", done, totalSteps));
        }

        if (csvFiles.Count == 0) throw new InvalidOperationException("Keine CSV-Datei gefunden.");

        var allRecords = new List<PersonRecord>();
        var warnings = new List<string>();

        for (int i = 0; i < csvFiles.Count; i++)
        {
            var (name, content) = csvFiles[i];
            progress?.Report(new ProgressUpdate($"Analysiere {name} ({i + 1}/{csvFiles.Count}) …", done, totalSteps));
            allRecords.AddRange(CsvInputParser.Parse(content, name, warnings));
        }

        progress?.Report(new ProgressUpdate("Klassen für Lehrer zuordnen …", done + 1, totalSteps));
        EnrichTeacherClasses(allRecords);

        var lkidDups = allRecords
            .GroupBy(p => p.Lkid, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // PersonIdentity: same (FirstName, LastName, DateOfBirth) but distinct LKIDs,
        // and neither record belongs to an LKID-duplicate group.
        var lkidDupSet = lkidDups.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var identityGroups = allRecords
            .Where(p => !string.IsNullOrWhiteSpace(p.DateOfBirth)
                     && !string.IsNullOrWhiteSpace(p.FirstName)
                     && !string.IsNullOrWhiteSpace(p.LastName))
            .Where(p => !lkidDupSet.Contains(p.Lkid))
            .GroupBy(p => (
                First: p.FirstName.Trim().ToLowerInvariant(),
                Last: p.LastName.Trim().ToLowerInvariant(),
                Dob: p.DateOfBirth!))
            .Where(g => g.Select(x => x.Lkid).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToDictionary(
                g => $"{g.First().LastName}, {g.First().FirstName} — {g.Key.Dob}",
                g => g.ToList(),
                StringComparer.OrdinalIgnoreCase);

        var usernameDups = allRecords
            .Where(p => !string.IsNullOrWhiteSpace(p.Username))
            .GroupBy(p => p.Username!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var appleDups = allRecords
            .Where(p => !string.IsNullOrWhiteSpace(p.AppleId))
            .GroupBy(p => p.AppleId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var sysDups = allRecords
            .Where(p => !string.IsNullOrWhiteSpace(p.SystemEmail))
            .GroupBy(p => p.SystemEmail!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var pkg = new ExportPackage
        {
            Persons = allRecords,
            LkidDuplicates = lkidDups,
            UsernameDuplicates = usernameDups,
            AppleIdDuplicates = appleDups,
            SystemEmailDuplicates = sysDups,
            PersonIdentityDuplicates = identityGroups,
        };
        pkg.Warnings.AddRange(warnings);
        progress?.Report(new ProgressUpdate("Fertig.", totalSteps, totalSteps));
        return pkg;
    }

    private static void EnrichTeacherClasses(List<PersonRecord> allRecords)
    {
        var classesBySchool = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in allRecords)
        {
            if (!r.IsStudent || r.IsTeacher) continue;
            if (string.IsNullOrWhiteSpace(r.SchoolId) || string.IsNullOrWhiteSpace(r.ClassName)) continue;
            if (!classesBySchool.TryGetValue(r.SchoolId!, out var set))
                classesBySchool[r.SchoolId!] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(r.ClassName!);
        }

        foreach (var t in allRecords)
        {
            if (!t.IsTeacher || string.IsNullOrWhiteSpace(t.SchoolId)) continue;
            if (classesBySchool.TryGetValue(t.SchoolId!, out var classes) && classes.Count > 0)
                t.ClassName = string.Join(",", classes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static IEnumerable<(string Name, string Content)> ReadCsvFromFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".csv")
        {
            yield return (Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8));
        }
        else if (ext == ".zip")
        {
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.Contains("/.")) continue;
                if (!entry.Name.ToLowerInvariant().EndsWith(".csv")) continue;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                yield return (entry.FullName, reader.ReadToEnd());
            }
        }
        else
        {
            throw new InvalidOperationException($"Dateityp nicht unterstützt: {path}");
        }
    }
}
