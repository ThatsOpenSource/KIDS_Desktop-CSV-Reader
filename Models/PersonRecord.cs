namespace KidsXmlToCsv.Models;

public sealed record PersonRecord
{
    public string Lkid { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Email { get; set; }
    public string? SystemEmail { get; set; }
    public string? AppleId { get; set; }
    public string? Username { get; set; }
    public string? Grade { get; set; }
    public string? ClassName { get; set; }
    public string? SchoolId { get; set; }
    public string? SoId { get; set; }
    public string? SgId { get; set; }
    public string? SchoolName { get; set; }
    public string? SchoolType { get; set; }
    public string? SourceGuid { get; set; }
    public string? SourceFile { get; set; }
    public bool IsStudent { get; set; }
    public bool IsTeacher { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsTerminated { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public List<string> Applications { get; set; } = new();
}
