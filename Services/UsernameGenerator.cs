using System.Text;

namespace KidsXmlToCsv.Services;

public static class UsernameGenerator
{
    public const string AppleIdDomain = "appleid.ds-greiz.de";
    public const string SystemEmailDomain = "schulen-greiz.de";

    /// <summary>Lowercased, umlauts folded, non-[a-z0-9] stripped.</summary>
    public static string SanitizeUsername(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Sanitize(NormalizeUmlauts(input).ToLowerInvariant());
    }

    /// <summary>First 3 of vorname + first 3 of nachname, sanitized.</summary>
    public static string BaseUsername(string vorname, string nachname)
    {
        var v = SanitizeUsername(vorname ?? "");
        var n = SanitizeUsername(nachname ?? "");
        var combined = Take(v, 3) + Take(n, 3);
        if (string.IsNullOrEmpty(combined)) combined = "user";
        return combined.Length > 12 ? combined[..12] : combined;
    }

    /// <summary>
    /// Generate a unique username based on OUR base pattern (first3+last3),
    /// then numbered suffix on collision. Existing value of the record is
    /// never used — callers must supply the base via first/last name.
    /// </summary>
    public static string GenerateUnique(string vorname, string nachname, ISet<string> taken)
    {
        var baseName = BaseUsername(vorname, nachname);
        if (!taken.Contains(baseName)) return baseName;
        for (int i = 1; i < 10000; i++)
        {
            var candidate = baseName + i;
            if (!taken.Contains(candidate)) return candidate;
        }
        return baseName + DateTime.UtcNow.Ticks.ToString()[^4..];
    }

    public static string GenerateAppleId(string username) =>
        $"{username}@{AppleIdDomain}";

    public static string GenerateSystemEmail(string username, string? soid)
    {
        var prefix = string.IsNullOrWhiteSpace(soid) ? "00" : soid!.Trim();
        return $"{username}@{prefix}.{SystemEmailDomain}";
    }

    private static string NormalizeUmlauts(string s) => s
        .Replace("ä", "ae").Replace("Ä", "Ae")
        .Replace("ö", "oe").Replace("Ö", "Oe")
        .Replace("ü", "ue").Replace("Ü", "Ue")
        .Replace("ß", "ss");

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                sb.Append(c);
        return sb.ToString();
    }

    private static string Take(string s, int n) => s.Length <= n ? s : s[..n];
}
