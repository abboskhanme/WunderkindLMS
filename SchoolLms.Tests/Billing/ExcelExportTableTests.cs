using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SchoolLms.Application.Services;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Moliya xlsx yozuvchisi (docs/modules/finance-parity.md F0.01).
///
/// <para>
/// <b>Nima uchun test kerak.</b> Faylni ochib ko'rish bilan tekshirib
/// bo'lmaydi: buzilgan uslublar jadvali bilan Excel faylni "tiklab" ochadi va
/// odam farqni sezmasligi mumkin. Bu yerda fayl QAYTA O'QILADI va uchta narsa
/// tasdiqlanadi: (1) pul kataklari HAQIQIY son (matn emas) — aks holda
/// buxgalter ustunni qo'sha olmaydi; (2) sarlavha va yakun qatori qalin;
/// (3) yakun qatori oxirgi qator.
/// </para>
/// <para>
/// Bu klass baza yaratmaydi, shuning uchun ulanish hovuzini tozalash ham
/// kerak emas.
/// </para>
/// </summary>
public class ExcelExportTableTests
{
    [Fact]
    public void Pul_kataklari_matn_emas_haqiqiy_son_bolib_yoziladi()
    {
        var bytes = ExcelExport.BuildTable(
            "Tranzaksiyalar",
            ["Sana", "Kim", "Summa"],
            [
                [ExcelExport.XlsxCell.Of("2026-09-17"), ExcelExport.XlsxCell.Of("Ali Valiyev"),
                 ExcelExport.XlsxCell.Num(1_250_000.50m)],
                [ExcelExport.XlsxCell.Of("2026-09-18"), ExcelExport.XlsxCell.Of("Vali Aliyev"),
                 ExcelExport.XlsxCell.Num(-300_000m)],
            ],
            ["Jami", ExcelExport.XlsxCell.Of(null), ExcelExport.XlsxCell.Num(950_000.50m)]);

        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, isEditable: false);

        var wbPart = doc.WorkbookPart!;
        var sheet = Assert.Single(wbPart.Workbook.Descendants<Sheet>());
        Assert.Equal("Tranzaksiyalar", sheet.Name!.Value);

        var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!.Value!);
        var rows = wsPart.Worksheet.Descendants<Row>().ToList();

        // Sarlavha + 2 qator + yakun.
        Assert.Equal(4, rows.Count);

        // ---- Sarlavha: matn va QALIN ----
        var header = rows[0].Elements<Cell>().ToList();
        Assert.Equal(3, header.Count);
        Assert.All(header, c => Assert.Equal(CellValues.InlineString, c.DataType!.Value));
        Assert.All(header, c => Assert.Equal(1u, c.StyleIndex!.Value));

        // ---- Pul katagi: HAQIQIY son ----
        var money = rows[1].Elements<Cell>().ElementAt(2);
        Assert.Equal(CellValues.Number, money.DataType!.Value);
        // InvariantCulture bilan yozilgani muhim: vergulli o'nlik Excel'da
        // faylni butunlay buzardi.
        Assert.Equal("1250000.50", money.CellValue!.Text);

        var negative = rows[2].Elements<Cell>().ElementAt(2);
        Assert.Equal("-300000.00", negative.CellValue!.Text);

        // ---- Yakun qatori: oxirgi, qalin va sonli ----
        var totals = rows[3].Elements<Cell>().ToList();
        Assert.Equal("Jami", totals[0].InlineString!.Text!.Text);
        Assert.Equal(1u, totals[0].StyleIndex!.Value);
        Assert.Equal(CellValues.Number, totals[2].DataType!.Value);
        Assert.Equal(3u, totals[2].StyleIndex!.Value);

        // ---- Uslublar jadvali fayl ichida ----
        Assert.NotNull(wbPart.WorkbookStylesPart);
        Assert.Equal(4, wbPart.WorkbookStylesPart!.Stylesheet.CellFormats!.Count());
    }

    /// <summary>
    /// Eski <c>Build(...)</c> TEGILMAGAN: uni yettita joy chaqiradi va u
    /// hamma katakni matn qilib yozishda davom etadi (telefon va parol
    /// "ilmiy son" bo'lib ketmasin).
    /// </summary>
    [Fact]
    public void Eski_matnli_yozuvchi_ozgarmadi()
    {
        var bytes = ExcelExport.Build(
            "O'quvchilar", ["F.I.SH", "Telefon"],
            [new[] { "Ali Valiyev", "+998901112233" }]);

        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, isEditable: false);

        var wbPart = doc.WorkbookPart!;
        var sheet = Assert.Single(wbPart.Workbook.Descendants<Sheet>());
        var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!.Value!);
        var cells = wsPart.Worksheet.Descendants<Row>().ElementAt(1).Elements<Cell>().ToList();

        Assert.All(cells, c => Assert.Equal(CellValues.InlineString, c.DataType!.Value));
        Assert.Equal("+998901112233", cells[1].InlineString!.Text!.Text);
    }
}
