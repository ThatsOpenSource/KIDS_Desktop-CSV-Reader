using System.Globalization;
using KidsXmlToCsv.Services;

namespace KidsXmlToCsv.Models;

public sealed class PersonRow
{
    public string Sid { get; init; } = "";
    public string Soid { get; init; } = "";
    public string Sdid { get; init; } = "";
    public string SchoolName { get; init; } = "";
    public string SchoolType { get; init; } = "";
    public string ClassList { get; init; } = "";
    public string Grade { get; init; } = "";
    public string Lkid { get; init; } = "";
    public string LastName { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string Dob { get; init; } = "";
    public string Username { get; init; } = "";
    public string AppleId { get; init; } = "";
    public string Email { get; init; } = "";
    public string SystemEmail { get; init; } = "";
    public string RoleList { get; init; } = "";
    public string FlagList { get; init; } = "";

    public bool IsDuplicateLkid { get; init; }
    public bool IsDuplicateUsername { get; init; }
    public bool IsDuplicateAppleId { get; init; }
    public bool IsDuplicateSystemEmail { get; init; }

    public string LkidDupMarker        => IsDuplicateLkid         ? "⚠" : "";
    public string UsernameDupMarker    => IsDuplicateUsername     ? "⚠" : "";
    public string AppleIdDupMarker     => IsDuplicateAppleId      ? "⚠" : "";
    public string SystemEmailDupMarker => IsDuplicateSystemEmail  ? "⚠" : "";

    public static PersonRow From(
        PersonRecord p,
        bool isDupLkid = false,
        bool isDupUsername = false,
        bool isDupAppleId = false,
        bool isDupSystemEmail = false)
    {
        var cfg = ConfigStore.Current;
        var classLabels = CsvWriter.BuildClassLabels(p, cfg).ToList();

        // Teachers enrolled in multiple classes display as "alle" in the table;
        // the export keeps the full comma-separated list. Students never collapse.
        var classDisplay = p.IsTeacher && classLabels.Count > 1
            ? "alle"
            : string.Join(cfg.Output.ListSeparator, classLabels);

        return new()
        {
            Sid = p.SchoolId ?? "",
            Soid = p.SoId ?? "",
            Sdid = p.SdId ?? "",
            SchoolName = p.SchoolName ?? "",
            SchoolType = p.SchoolType ?? "",
            ClassList = classDisplay,
            Grade = p.Grade ?? "",
            Lkid = p.Lkid,
            LastName = p.LastName,
            FirstName = p.FirstName,
            Dob = FormatDate(p.DateOfBirth, cfg.Output.DateFormat),
            Username = p.Username ?? "",
            AppleId = p.AppleId ?? "",
            Email = p.Email ?? "",
            SystemEmail = p.SystemEmail ?? "",
            RoleList = string.Join(cfg.Output.ListSeparator, CsvWriter.BuildRoleLabels(p, cfg)),
            FlagList = string.Join(cfg.Output.ListSeparator, CsvWriter.BuildFlagsColumn(p, cfg)),
            IsDuplicateLkid = isDupLkid,
            IsDuplicateUsername = isDupUsername,
            IsDuplicateAppleId = isDupAppleId,
            IsDuplicateSystemEmail = isDupSystemEmail,
        };
    }

    private static string FormatDate(string? iso, string fmt)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParseExact(iso, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString(fmt);
        return iso;
    }
}
