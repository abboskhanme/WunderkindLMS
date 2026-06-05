using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SchoolLms.Application.Services;

/// <summary>
/// Oddiy .xlsx o'qigich (OpenXML). BIRINCHI varaqdagi qatorlarni o'qiydi. Har qator —
/// <c>columnCount</c> uzunlikdagi string massiv: bo'sh kataklar "" bo'ladi va kataklar
/// ustun HARFIGA qarab to'g'ri joyga qo'yiladi (oradagi bo'sh katak ustunlarni surib yubormaydi).
/// Shared-string, inline-string va oddiy (raqam/sana) kataklar qo'llab-quvvatlanadi.
/// </summary>
public static class ExcelImport
{
    public static List<string[]> ReadRows(Stream stream, int columnCount)
    {
        var result = new List<string[]>();
        using var doc = SpreadsheetDocument.Open(stream, false);
        var wbPart = doc.WorkbookPart;
        if (wbPart is null) return result;

        var sheet = wbPart.Workbook.Descendants<Sheet>().FirstOrDefault();
        if (sheet?.Id?.Value is null) return result;

        var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!.Value!);
        var shared = wbPart.SharedStringTablePart?.SharedStringTable;
        var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
        if (sheetData is null) return result;

        foreach (var row in sheetData.Elements<Row>())
        {
            var cells = new string[columnCount];
            for (var i = 0; i < columnCount; i++) cells[i] = "";

            foreach (var cell in row.Elements<Cell>())
            {
                var col = ColumnIndex(cell.CellReference?.Value);
                if (col < 0 || col >= columnCount) continue;
                cells[col] = GetValue(cell, shared);
            }
            result.Add(cells);
        }
        return result;
    }

    private static string GetValue(Cell cell, SharedStringTable? shared)
    {
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (shared is not null
                && int.TryParse(cell.CellValue?.Text, out var idx)
                && idx >= 0 && idx < shared.ChildElements.Count)
                return shared.ElementAt(idx).InnerText;
            return "";
        }
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.Text?.Text ?? cell.InnerText;
        return cell.CellValue?.Text ?? "";
    }

    /// <summary>Katak manzilidagi ustun harfini 0-asosli indeksga aylantiradi ("A"→0, "B"→1, "AA"→26).</summary>
    private static int ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return -1;
        var col = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') col = col * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') col = col * 26 + (ch - 'a' + 1);
            else break;
        }
        return col - 1;
    }
}
