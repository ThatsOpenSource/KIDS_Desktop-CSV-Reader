using System.IO;
using System.Text;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public static class ConfigStore
{
    private static AppConfig? _current;
    public static AppConfig Current => _current ??= ConfigLoader.LoadOrCreateDefault();
    public static void Reload() => _current = null;
}

public static class ConfigLoader
{
    public const string FileName = "config.toml";

    public static string ConfigDirectory()
    {
        var proc = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(proc))
        {
            var name = Path.GetFileNameWithoutExtension(proc);
            if (name.Equals("KIDS", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(proc)!;
        }
        return AppContext.BaseDirectory;
    }

    public static string ConfigPath() => Path.Combine(ConfigDirectory(), FileName);

    public static AppConfig LoadOrCreateDefault()
    {
        try
        {
            var path = ConfigPath();
            if (!File.Exists(path))
            {
                var def = DefaultConfig();
                File.WriteAllText(path, Serialize(def), new UTF8Encoding(false));
                return def;
            }
            return ParseFromFile(path);
        }
        catch
        {
            return DefaultConfig();
        }
    }

    public static AppConfig ParseFromFile(string path)
    {
        var toml = TomlParser.Parse(File.ReadAllText(path, Encoding.UTF8));
        var cfg = DefaultConfig();

        var outT = toml.GetTable("output");
        if (outT is not null)
        {
            cfg.Output.Headers = outT.GetBool("headers", cfg.Output.Headers);
            cfg.Output.Encoding = outT.GetString("encoding", cfg.Output.Encoding);
            cfg.Output.CsvDelimiter = outT.GetString("csv_delimiter", cfg.Output.CsvDelimiter);
            cfg.Output.ListSeparator = outT.GetString("list_separator", cfg.Output.ListSeparator);
            cfg.Output.SanitizeFilenames = outT.GetBool("sanitize_filenames", cfg.Output.SanitizeFilenames);
            cfg.Output.DateFormat = outT.GetString("date_format", cfg.Output.DateFormat);
            cfg.Output.FlagsIncludeRoles = outT.GetBool("flags_include_roles", cfg.Output.FlagsIncludeRoles);
        }

        var cols = toml.GetTableArray("columns");
        if (cols.Count > 0)
            cfg.Columns = cols.Select(t => new ColumnConfig
            {
                Field = t.GetString("field"),
                DisplayName = t.GetString("display_name"),
                ExportName = t.GetString("export_name"),
                ExportEnabled = t.GetBool("export_enabled"),
                PreferredWidth = t.GetInt("preferred_width", 140),
                IsCore = t.GetBool("is_core", false),
            }).ToList();

        var flags = toml.GetTableArray("flags");
        if (flags.Count > 0)
            cfg.Flags = flags.Select(t => new FlagDefinition
            {
                DataId = t.GetString("data_id"),
                DataLabel = t.GetString("data_label"),
                Aliases = t.GetStringArray("aliases"),
                VceType = t.GetInt("vce_type", 0),
            }).ToList();

        var roles = toml.GetTableArray("roles");
        if (roles.Count > 0)
            cfg.Roles = roles.Select(t => new RoleDefinition
            {
                Id = t.GetString("id"),
                Label = t.GetString("label"),
                VceType = t.GetInt("vce_type", 0),
            }).ToList();

        return cfg;
    }

    public static AppConfig DefaultConfig() => new()
    {
        Output = new OutputConfig
        {
            Headers = false,
            Encoding = "ansi",
            CsvDelimiter = ";",
            ListSeparator = ",",
            SanitizeFilenames = true,
            DateFormat = "dd.MM.yyyy",
            FlagsIncludeRoles = true,
        },
        Columns = new()
        {
            new() { Field = "sid",           DisplayName = "SID",           ExportName = "SID",           ExportEnabled = true, PreferredWidth = 60,  IsCore = true },
            new() { Field = "soid",          DisplayName = "SOID",          ExportName = "SOID",          ExportEnabled = true, PreferredWidth = 50,  IsCore = true },
            new() { Field = "school_name",   DisplayName = "Schulname",     ExportName = "Schulname",     ExportEnabled = true, PreferredWidth = 200, IsCore = true },
            new() { Field = "school_type",   DisplayName = "Schultyp",      ExportName = "Schultyp",      ExportEnabled = true, PreferredWidth = 110, IsCore = true },
            new() { Field = "class",         DisplayName = "Klasse",        ExportName = "Klasse",        ExportEnabled = true, PreferredWidth = 220, IsCore = true },
            new() { Field = "grade",         DisplayName = "Stufe",         ExportName = "Stufe",         ExportEnabled = true, PreferredWidth = 55,  IsCore = true },
            new() { Field = "lkid",          DisplayName = "LKID",          ExportName = "LKID",          ExportEnabled = true, PreferredWidth = 150, IsCore = true },
            new() { Field = "last_name",     DisplayName = "Name",          ExportName = "Name",          ExportEnabled = true, PreferredWidth = 140, IsCore = true },
            new() { Field = "first_name",    DisplayName = "Vorname",       ExportName = "Vorname",       ExportEnabled = true, PreferredWidth = 120, IsCore = true },
            new() { Field = "date_of_birth", DisplayName = "Geburtsdatum",  ExportName = "Geburtsdatum",  ExportEnabled = true, PreferredWidth = 110, IsCore = true },
            new() { Field = "username",      DisplayName = "Benutzername",  ExportName = "Benutzername",  ExportEnabled = true, PreferredWidth = 130, IsCore = true },
            new() { Field = "apple_id",      DisplayName = "Apple ID",      ExportName = "Apple ID",      ExportEnabled = true, PreferredWidth = 220, IsCore = true },
            new() { Field = "email",         DisplayName = "E-Mail",        ExportName = "E-Mail",        ExportEnabled = true, PreferredWidth = 200, IsCore = true },
            new() { Field = "system_email",  DisplayName = "System-E-Mail", ExportName = "System-E-Mail", ExportEnabled = true, PreferredWidth = 220, IsCore = true },
            new() { Field = "vce_type",      DisplayName = "vceType",       ExportName = "vceType",       ExportEnabled = true, PreferredWidth = 80,  IsCore = true },
            new() { Field = "roles",         DisplayName = "Rolle",         ExportName = "Rolle",         ExportEnabled = true, PreferredWidth = 140, IsCore = true },
            new() { Field = "flags",         DisplayName = "Flags",         ExportName = "Flags",         ExportEnabled = true, PreferredWidth = 160, IsCore = true },
        },
        Flags = new()
        {
            new() { DataId = "sg-prt",   DataLabel = "PaperCut",                   Aliases = new() { "print", "sp-prt" } },
            new() { DataId = "sg-iot",   DataLabel = "Datenverarbeitungsvertrag",  Aliases = new() { "dvv", "sp-iot" } },
            new() { DataId = "sg-mdm",   DataLabel = "MDM",                        Aliases = new() { "mdm" } },
            new() { DataId = "sg-cloud", DataLabel = "Nextcloud",                  Aliases = new() { "cloud" } },
        },
        Roles = new()
        {
            new() { Id = "Schueler", Label = "Schüler",  VceType = 431 },
            new() { Id = "Lehrer",   Label = "Lehrer",   VceType = 411 },
            new() { Id = "Sonstige", Label = "Sonstige", VceType = 421 },
        },
    };

    public static string Serialize(AppConfig cfg)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# KIDS — Desktop CSV Reader — Konfiguration");
        sb.AppendLine("# Änderungen werden beim nächsten Start der Anwendung wirksam.");
        sb.AppendLine();

        sb.AppendLine("[output]");
        sb.AppendLine("# headers: Spaltenüberschriften im Export (false = keine Kopfzeile)");
        sb.AppendLine("# encoding: ansi | utf-8 | utf-8-bom | latin1 (ansi = Windows-1252)");
        sb.AppendLine("# csv_delimiter: Feldtrenner in der CSV");
        sb.AppendLine("# list_separator: Trenner innerhalb von Liste-Zellen (z.B. Klasse)");
        sb.AppendLine("# sanitize_filenames: unzulässige Windows-Zeichen in Klassennamen entfernen");
        sb.AppendLine("# date_format: Datumsformat im Export");
        sb.AppendLine("# flags_include_roles: Rollen-IDs (Schueler, Lehrer) zusätzlich in der Flags-Spalte ausgeben (temporär)");
        sb.AppendLine($"headers = {Bool(cfg.Output.Headers)}");
        sb.AppendLine($"encoding = {Str(cfg.Output.Encoding)}");
        sb.AppendLine($"csv_delimiter = {Str(cfg.Output.CsvDelimiter)}");
        sb.AppendLine($"list_separator = {Str(cfg.Output.ListSeparator)}");
        sb.AppendLine($"sanitize_filenames = {Bool(cfg.Output.SanitizeFilenames)}");
        sb.AppendLine($"date_format = {Str(cfg.Output.DateFormat)}");
        sb.AppendLine($"flags_include_roles = {Bool(cfg.Output.FlagsIncludeRoles)}");
        sb.AppendLine();

        sb.AppendLine("# ─── Spaltendefinitionen ───");
        sb.AppendLine("# display_name: Spaltenkopf in der Vorschautabelle");
        sb.AppendLine("# export_name: Spaltenkopf in der Exportdatei (nur wenn output.headers = true)");
        sb.AppendLine("# export_enabled: Spalte in den Export aufnehmen");
        sb.AppendLine("# Neue Spalten sind standardmäßig export_enabled = false.");
        sb.AppendLine();
        foreach (var c in cfg.Columns)
        {
            sb.AppendLine("[[columns]]");
            sb.AppendLine($"field = {Str(c.Field)}");
            sb.AppendLine($"display_name = {Str(c.DisplayName)}");
            sb.AppendLine($"export_name = {Str(c.ExportName)}");
            sb.AppendLine($"export_enabled = {Bool(c.ExportEnabled)}");
            sb.AppendLine($"preferred_width = {c.PreferredWidth}");
            sb.AppendLine($"is_core = {Bool(c.IsCore)}");
            sb.AppendLine();
        }

        sb.AppendLine("# ─── Flagdefinitionen ───");
        sb.AppendLine("# data_id: Wert, wie er im Export erscheint");
        sb.AppendLine("# aliases: Schreibweisen, die beim Import erkannt werden");
        sb.AppendLine();
        foreach (var f in cfg.Flags)
        {
            sb.AppendLine("[[flags]]");
            sb.AppendLine($"data_id = {Str(f.DataId)}");
            sb.AppendLine($"data_label = {Str(f.DataLabel)}");
            sb.AppendLine($"aliases = [{string.Join(", ", f.Aliases.Select(Str))}]");
            sb.AppendLine($"vce_type = {f.VceType}");
            sb.AppendLine();
        }

        sb.AppendLine("# ─── Rollendefinitionen ───");
        sb.AppendLine();
        foreach (var r in cfg.Roles)
        {
            sb.AppendLine("[[roles]]");
            sb.AppendLine($"id = {Str(r.Id)}");
            sb.AppendLine($"label = {Str(r.Label)}");
            sb.AppendLine($"vce_type = {r.VceType}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static string Bool(bool b) => b ? "true" : "false";
}
