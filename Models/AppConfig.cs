namespace KidsXmlToCsv.Models;

public sealed class AppConfig
{
    public OutputConfig Output { get; set; } = new();
    public List<ColumnConfig> Columns { get; set; } = new();
    public List<FlagDefinition> Flags { get; set; } = new();
    public List<RoleDefinition> Roles { get; set; } = new();
}

public sealed class OutputConfig
{
    public bool Headers { get; set; } = false;
    public string Encoding { get; set; } = "ansi";
    public string CsvDelimiter { get; set; } = ";";
    public string ListSeparator { get; set; } = ",";
    public bool SanitizeFilenames { get; set; } = true;
    public string DateFormat { get; set; } = "dd.MM.yyyy";

    /// <summary>
    /// Wenn true, werden die Rollen-IDs (Schueler, Lehrer, …) zusätzlich
    /// vor den Flags in die "Flags"-Spalte geschrieben. Temporär — kann
    /// abgeschaltet werden, sobald die Web-App die Rolle separat führt.
    /// </summary>
    public bool FlagsIncludeRoles { get; set; } = true;
}

public sealed class ColumnConfig
{
    public string Field { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ExportName { get; set; } = "";
    public bool ExportEnabled { get; set; } = false;
    public int PreferredWidth { get; set; } = 140;
    public bool IsCore { get; set; } = false;
}

public sealed class FlagDefinition
{
    public string DataId { get; set; } = "";
    public string DataLabel { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public int VceType { get; set; } = 0;
}

public sealed class RoleDefinition
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int VceType { get; set; } = 0;
}
