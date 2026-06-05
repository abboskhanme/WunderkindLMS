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
    /// <summary>Bitta varaq spetsifikatsiyasi (nom + sarlavha + qatorlar) — ko'p varaqli kitob uchun.</summary>
    public sealed record SheetSpec(string Name, IReadOnlyList<string> Headers, IEnumerable<IReadOnlyList<string>> Rows);

    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        => Build(new[] { new SheetSpec(sheetName, headers, rows) });

    /// <summary>Ko'p varaqli .xlsx — har varaq sarlavha + qatorlardan iborat.</summary>
    public static byte[] Build(IReadOnlyList<SheetSpec> specs)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var sheets = wbPart.Workbook.AppendChild(new Sheets());

            uint sheetId = 1;
            foreach (var spec in specs)
            {
                var wsPart = wbPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                wsPart.Worksheet = new Worksheet(sheetData);

                sheets.Append(new Sheet
                {
                    Id = wbPart.GetIdOfPart(wsPart),
                    SheetId = sheetId++,
                    Name = spec.Name.Length > 31 ? spec.Name[..31] : spec.Name,
                });

                sheetData.Append(MakeRow(spec.Headers));
                foreach (var r in spec.Rows) sheetData.Append(MakeRow(r));
            }

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
