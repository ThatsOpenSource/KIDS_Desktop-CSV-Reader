using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

public static class XlsxWriter
{
    public static byte[] Build(IReadOnlyList<PersonRecord> persons, AppConfig? config = null)
    {
        config ??= ConfigStore.Current;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypes);
            WriteEntry(zip, "_rels/.rels", RootRels);
            WriteEntry(zip, "xl/workbook.xml", Workbook);
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            WriteEntry(zip, "xl/worksheets/sheet1.xml", BuildSheet(persons, config));
        }
        return ms.ToArray();
    }

    private static string BuildSheet(IReadOnlyList<PersonRecord> persons, AppConfig config)
    {
        var columns = config.Columns.Where(c => c.ExportEnabled).ToList();
        var sb = new StringBuilder(64 * 1024);
        sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
        sb.Append(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");

        int rowIdx = 1;

        if (config.Output.Headers)
        {
            sb.Append("<row>");
            for (int i = 0; i < columns.Count; i++)
                sb.Append(CellInline(i, rowIdx, columns[i].ExportName));
            sb.Append("</row>");
            rowIdx++;
        }

        foreach (var p in persons
            .OrderBy(p => p.SchoolId ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.IsStudent ? 0 : 1)
            .ThenBy(p => p.ClassName ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("<row>");
            for (int c = 0; c < columns.Count; c++)
            {
                var val = FormatColumn(columns[c].Field, p, config);
                if (string.IsNullOrEmpty(val)) continue;
                sb.Append(CellInline(c, rowIdx, val));
            }
            sb.Append("</row>");
            rowIdx++;
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string FormatColumn(string field, PersonRecord p, AppConfig cfg) => field switch
    {
        "sid"           => p.SchoolId ?? "",
        "sgid"          => p.SgId ?? "",
        "soid"          => p.SoId ?? "",
        "school_name"   => p.SchoolName ?? "",
        "school_type"   => p.SchoolType ?? "",
        "class"         => string.Join(cfg.Output.ListSeparator, CsvWriter.BuildClassLabels(p, cfg)),
        "grade"         => p.Grade ?? "",
        "lkid"          => p.Lkid,
        "last_name"     => p.LastName,
        "first_name"    => p.FirstName,
        "date_of_birth" => FormatDate(p.DateOfBirth, cfg.Output.DateFormat),
        "username"      => p.Username ?? "",
        "apple_id"      => p.AppleId ?? "",
        "email"         => p.Email ?? "",
        "system_email"  => p.SystemEmail ?? "",
        "vce_type"      => p.VceType?.ToString() ?? "",
        "roles"         => string.Join(cfg.Output.ListSeparator, CsvWriter.BuildRoleLabels(p, cfg)),
        "flags"         => string.Join(cfg.Output.ListSeparator, CsvWriter.BuildFlagsColumn(p)),
        _               => "",
    };

    private static string FormatDate(string? iso, string fmt)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParseExact(iso, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString(fmt);
        return iso;
    }

    private static string CellInline(int col, int row, string value) =>
        $"<c r=\"{ColumnName(col)}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlEscape(value)}</t></is></c>";

    private static string ColumnName(int index)
    {
        var name = "";
        index++;
        while (index > 0) { index--; name = (char)('A' + index % 26) + name; index /= 26; }
        return name;
    }

    private static string XmlEscape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s) switch (c)
        {
            case '&':  sb.Append("&amp;"); break;
            case '<':  sb.Append("&lt;"); break;
            case '>':  sb.Append("&gt;"); break;
            case '"':  sb.Append("&quot;"); break;
            case '\'': sb.Append("&apos;"); break;
            default:   sb.Append(c); break;
        }
        return sb.ToString();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var w = new StreamWriter(s, new UTF8Encoding(false));
        w.Write(content);
    }

    private const string ContentTypes = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>";

    private const string RootRels = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>";

    private const string Workbook = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets><sheet name=""Personen"" sheetId=""1"" r:id=""rId1""/></sheets>
</workbook>";

    private const string WorkbookRels = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
</Relationships>";
}
