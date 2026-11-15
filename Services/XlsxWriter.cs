using System.IO;
using System.IO.Compression;
using System.Text;
using KidsXmlToCsv.Models;

namespace KidsXmlToCsv.Services;

/// <summary>
/// Minimal, dependency-free XLSX writer. Produces a spec-compliant
/// Office Open XML workbook (inline strings only — no sharedStrings needed).
/// Encoding-agnostic, delimiter-agnostic, opens identically on Windows,
/// macOS and Linux, on every Excel locale.
/// </summary>
public static class XlsxWriter
{
    private static readonly string[] Headers =
    {
        "SID", "SGID", "SOID", "Schulname", "Schultyp",
        "Klasse", "Stufe", "LKID", "Name", "Vorname",
        "Benutzername", "Apple ID", "E-Mail", "System-E-Mail",
        "Rolle", "Flags",
    };

    public static byte[] Build(IReadOnlyList<PersonRecord> persons)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypes);
            WriteEntry(zip, "_rels/.rels", RootRels);
            WriteEntry(zip, "xl/workbook.xml", Workbook);
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            WriteEntry(zip, "xl/worksheets/sheet1.xml", BuildSheet(persons));
        }
        return ms.ToArray();
    }

    private static string BuildSheet(IReadOnlyList<PersonRecord> persons)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
        sb.Append(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">");
        sb.Append("<sheetData>");

        // Header row (row 1)
        sb.Append("<row>");
        for (int i = 0; i < Headers.Length; i++)
            sb.Append(CellInline(i, 1, Headers[i]));
        sb.Append("</row>");

        var sorted = persons
            .OrderBy(p => p.SchoolId ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.IsStudent ? 0 : 1)
            .ThenBy(p => p.ClassName ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int r = 0; r < sorted.Count; r++)
        {
            var p = sorted[r];
            var row = 2 + r;

            var cells = new[]
            {
                p.SchoolId ?? "",
                p.SgId ?? "",
                p.SoId ?? "",
                p.SchoolName ?? "",
                p.SchoolType ?? "",
                string.Join(CsvWriter.ListSeparator, CsvWriter.BuildClassLabels(p)),
                p.Grade ?? "",
                p.Lkid,
                p.LastName,
                p.FirstName,
                p.Username ?? "",
                p.AppleId ?? "",
                p.Email ?? "",
                p.SystemEmail ?? "",
                string.Join(CsvWriter.ListSeparator, CsvWriter.BuildRoleLabels(p)),
                string.Join(CsvWriter.ListSeparator, p.Flags.Distinct().OrderBy(x => x)),
            };

            sb.Append("<row>");
            for (int c = 0; c < cells.Length; c++)
                sb.Append(CellInline(c, row, cells[c]));
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string CellInline(int col, int row, string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var cellRef = ColumnName(col) + row.ToString();
        return $"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlEscape(value)}</t></is></c>";
    }

    /// <summary>0 → A, 25 → Z, 26 → AA, 27 → AB, …</summary>
    private static string ColumnName(int index)
    {
        var name = "";
        index++;
        while (index > 0)
        {
            index--;
            name = (char)('A' + (index % 26)) + name;
            index /= 26;
        }
        return name;
    }

    private static string XmlEscape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;");  break;
                case '>': sb.Append("&gt;");  break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
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
  <sheets>
    <sheet name=""Personen"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
</workbook>";

    private const string WorkbookRels = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
</Relationships>";
}
