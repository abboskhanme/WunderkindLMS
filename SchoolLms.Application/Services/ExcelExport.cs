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

    // =====================================================================
    //  MOLIYA JADVALI — SON KATAKLARI, QALIN SARLAVHA, YAKUN QATORI
    //  (docs/modules/finance-parity.md F0.01)
    // =====================================================================
    //
    //  Yuqoridagi `Build(...)` HAMMA katakni matn qilib yozadi. Telefon va
    //  parol uchun bu to'g'ri edi (ular "ilmiy son" bo'lib ketmasin), pul
    //  uchun esa NOTO'G'RI: Excel'da matn ustunini qo'shib bo'lmaydi, ya'ni
    //  buxgalter eksportni ochib "jami qancha" degan savolga javob ololmaydi
    //  va raqamlarni qo'lda qayta teradi.
    //
    //  Shuning uchun quyidagi QO'SHIMCHA yuza kiritildi — eskisi TEGILMAGAN
    //  (uni yettita joy chaqiradi). Farqi uchta: (1) katak matn YOKI son
    //  bo'la oladi, (2) sarlavha qatori qalin, (3) ixtiyoriy yakun qatori
    //  qalin va sonli.

    /// <summary>
    /// Bitta katak: matn YOKI son. Ikkovi birdan bo'lmaydi.
    ///
    /// <para>
    /// <c>struct</c> — qator ko'p (eksportda minglab katak), har biri uchun
    /// obyekt yaratish ortiqcha. <c>Number</c> to'ldirilgan bo'lsa katak
    /// Excel'da HAQIQIY son bo'ladi va <c>SUM()</c> ga tushadi.
    /// </para>
    /// </summary>
    public readonly record struct XlsxCell(string? Text, decimal? Number)
    {
        /// <summary>Matn katagi (<c>null</c> — bo'sh katak).</summary>
        public static XlsxCell Of(string? text) => new(text, null);

        /// <summary>Son katagi — Excel'da <c># ##0.00</c> formatida ko'rinadi.</summary>
        public static XlsxCell Num(decimal number) => new(null, number);

        /// <summary>Son katagi, qiymat bo'lmasa bo'sh qoladi.</summary>
        public static XlsxCell Num(decimal? number) => number is { } v ? Num(v) : Of(null);

        /// <summary>Matnni katakka o'girish — <c>(XlsxCell)"matn"</c> deb yozish uchun.</summary>
        public static implicit operator XlsxCell(string? text) => Of(text);
    }

    /// <summary>
    /// Bitta varaq: nom, sarlavha, qatorlar va ixtiyoriy yakun qatori.
    /// </summary>
    /// <param name="Totals">Yakun qatori — jadvalning OXIRGI qatori sifatida,
    /// qalin shrift bilan yoziladi. <c>null</c> = yakun yo'q. Uzunligi
    /// <paramref name="Headers"/> bilan bir xil bo'lishi SHART emas: qisqa
    /// bo'lsa qolgan kataklar bo'sh qoladi.</param>
    public sealed record TableSpec(
        string Name,
        IReadOnlyList<string> Headers,
        IEnumerable<IReadOnlyList<XlsxCell>> Rows,
        IReadOnlyList<XlsxCell>? Totals = null);

    /// <summary>Bitta varaqli moliya jadvali (F0.01).</summary>
    public static byte[] BuildTable(
        string sheetName, IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<XlsxCell>> rows, IReadOnlyList<XlsxCell>? totals = null)
        => BuildTables([new TableSpec(sheetName, headers, rows, totals)]);

    /// <summary>Ko'p varaqli moliya jadvali (F0.01).</summary>
    public static byte[] BuildTables(IReadOnlyList<TableSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);

        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();

            // Uslublar VARAQLARDAN OLDIN qo'shiladi: OpenXML sxemasi
            // `styles.xml` ni `sheet` qismlariga havola qilinishidan oldin
            // talab qiladi, aks holda Excel faylni "tiklab" ochadi.
            var stylesPart = wbPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = MoneyStylesheet();
            stylesPart.Stylesheet.Save();

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

                sheetData.Append(MakeRow([.. spec.Headers.Select(XlsxCell.Of)], bold: true));
                foreach (var r in spec.Rows) sheetData.Append(MakeRow(r, bold: false));
                if (spec.Totals is { } totals) sheetData.Append(MakeRow(totals, bold: true));
            }

            wbPart.Workbook.Save();
        }
        return ms.ToArray();
    }

    /// <summary>Uslublar indeksi — <see cref="MoneyStylesheet"/> dagi tartib.</summary>
    private const uint StyleNormal = 0;
    private const uint StyleBold = 1;
    private const uint StyleMoney = 2;
    private const uint StyleMoneyBold = 3;

    private static Row MakeRow(IReadOnlyList<XlsxCell> cells, bool bold)
    {
        var row = new Row();
        foreach (var c in cells)
        {
            if (c.Number is { } number)
            {
                row.Append(new Cell
                {
                    DataType = CellValues.Number,
                    // InvariantCulture MAJBURIY: o'nlik ajratgichi vergul
                    // bo'lgan mintaqada "1,50" yozilsa Excel faylni buzilgan
                    // deb hisoblaydi.
                    CellValue = new CellValue(number.ToString(
                        "0.00", System.Globalization.CultureInfo.InvariantCulture)),
                    StyleIndex = bold ? StyleMoneyBold : StyleMoney,
                });
                continue;
            }

            row.Append(new Cell
            {
                DataType = CellValues.InlineString,
                InlineString = new InlineString(
                    new Text(c.Text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }),
                StyleIndex = bold ? StyleBold : StyleNormal,
            });
        }
        return row;
    }

    /// <summary>
    /// Eng kichik yaroqli uslublar jadvali: oddiy / qalin shrift × matn / pul
    /// formati. <c>NumberFormatId = 4</c> — Excel'ning o'z <c>#,##0.00</c> si,
    /// ya'ni maxsus format e'lon qilish shart emas va fayl har mintaqada
    /// bir xil ochiladi.
    /// </summary>
    private static Stylesheet MoneyStylesheet() => new(
        new Fonts(
            new Font(new FontSize { Val = 11D }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Calibri" }))
        { Count = 2 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
        { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 })
        { Count = 1 },
        new CellFormats(
            // 0 — oddiy matn
            new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
            // 1 — qalin matn (sarlavha va yakun qatori)
            new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true },
            // 2 — pul
            new CellFormat { NumberFormatId = 4, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true },
            // 3 — pul, qalin
            new CellFormat { NumberFormatId = 4, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true, ApplyFont = true })
        { Count = 4 });
}
