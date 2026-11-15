using KidsXmlToCsv.Services;

namespace KidsXmlToCsv.Models;

public sealed class PersonRow
{
    public string Sid { get; init; } = "";
    public string Sgid { get; init; } = "";
    public string Soid { get; init; } = "";
    public string SchoolName { get; init; } = "";
    public string SchoolType { get; init; } = "";
    public string ClassList { get; init; } = "";
    public string Grade { get; init; } = "";
    public string Lkid { get; init; } = "";
    public string LastName { get; init; } = "";
    public string FirstName { get; init; } = "";
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

    public string LkidDupMarker => IsDuplicateLkid ? "⚠" : "";
    public string UsernameDupMarker => IsDuplicateUsername ? "⚠" : "";
    public string AppleIdDupMarker => IsDuplicateAppleId ? "⚠" : "";
    public string SystemEmailDupMarker => IsDuplicateSystemEmail ? "⚠" : "";

    public static PersonRow From(
        PersonRecord p,
        bool isDupLkid = false,
        bool isDupUsername = false,
        bool isDupAppleId = false,
        bool isDupSystemEmail = false) => new()
    {
        Sid = p.SchoolId ?? "",
        Sgid = p.SgId ?? "",
        Soid = p.SoId ?? "",
        SchoolName = p.SchoolName ?? "",
        SchoolType = p.SchoolType ?? "",
        ClassList = string.Join(", ", CsvWriter.BuildClassLabels(p)),
        Grade = p.Grade ?? "",
        Lkid = p.Lkid,
        LastName = p.LastName,
        FirstName = p.FirstName,
        Username = p.Username ?? "",
        AppleId = p.AppleId ?? "",
        Email = p.Email ?? "",
        SystemEmail = p.SystemEmail ?? "",
        RoleList = string.Join(", ", CsvWriter.BuildRoleLabels(p)),
        FlagList = string.Join(", ", p.Flags.Distinct().OrderBy(x => x)),
        IsDuplicateLkid = isDupLkid,
        IsDuplicateUsername = isDupUsername,
        IsDuplicateAppleId = isDupAppleId,
        IsDuplicateSystemEmail = isDupSystemEmail,
    };
}
