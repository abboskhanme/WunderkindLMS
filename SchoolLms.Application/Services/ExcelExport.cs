using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SchoolLms.Application.Services;

/// <summary>
/// Oddiy .xlsx generatori (OpenXML, inline-string). Sarlavha + qatorlardan bitta varaqli kitob yasaydi.
/// Barcha kataklar matn (telefon/parol "ilmiy son" bo'lib ketmaydi).
/// </summary>
public static class ExcelExport
{
    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            wsPart.Worksheet = new Worksheet(sheetData);

            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = wbPart.GetIdOfPart(wsPart),
                SheetId = 1,
                Name = sheetName.Length > 31 ? sheetName[..31] : sheetName,
            });

            sheetData.Append(MakeRow(headers));
            foreach (var r in rows) sheetData.Append(MakeRow(r));

            wbPart.Workbook.Save();
        }
        return ms.ToArray();
    }

    private static Row MakeRow(IReadOnlyList<string> cells)
    {
        var row = new Row();
        foreach (var c in cells)
        {
            row.Append(new Cell
            {
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(c ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }),
            });
        }
        return row;
    }
}
