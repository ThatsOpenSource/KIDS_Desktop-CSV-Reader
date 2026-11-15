using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public static class DuplicateDetector
{
    public static Dictionary<string, List<PersonRecord>> DetectPersonIdentity(List<PersonRecord> records)
    {
        return records
            .Where(p => !string.IsNullOrWhiteSpace(p.DateOfBirth)
                     && !string.IsNullOrWhiteSpace(p.FirstName)
                     && !string.IsNullOrWhiteSpace(p.LastName))
            .GroupBy(p => (
                First: p.FirstName.Trim().ToLowerInvariant(),
                Last:  p.LastName.Trim().ToLowerInvariant(),
                Dob:   p.DateOfBirth!))
            .Where(g => g.Select(x => x.Lkid).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToDictionary(
                g => $"{g.First().LastName}, {g.First().FirstName} — {g.Key.Dob}",
                g => g.ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, List<PersonRecord>> DetectLkid(List<PersonRecord> records) =>
        records
            .GroupBy(p => p.Lkid, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, List<PersonRecord>> DetectUsername(List<PersonRecord> records) =>
        records
            .Where(p => !string.IsNullOrWhiteSpace(p.Username))
            .GroupBy(p => p.Username!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, List<PersonRecord>> DetectAppleId(List<PersonRecord> records) =>
        records
            .Where(p => !string.IsNullOrWhiteSpace(p.AppleId))
            .GroupBy(p => p.AppleId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, List<PersonRecord>> DetectSystemEmail(List<PersonRecord> records) =>
        records
            .Where(p => !string.IsNullOrWhiteSpace(p.SystemEmail))
            .GroupBy(p => p.SystemEmail!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
}
