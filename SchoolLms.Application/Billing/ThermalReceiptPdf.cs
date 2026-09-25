using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SchoolLms.Application.Billing;

/// <summary>
/// To'lov cheki PDF'i — 58 mm TERMAL chek bilan AYNAN bir xil ko'rinishda (mijoz, 2026-09-25: "o'quvchi uchun
/// saqlanadigan chek ham pdf variantida katta varoqdagi chek bo'lmasin 58 mm mo'ljallab chiqarayotgan chekimiz
/// bilan bir xil bo'lsin"). Telegram'ga yuboriladigan, o'quvchi/ota-ona profilidan va admin paneldan qayta
/// ochiladigan chek — hammasi shu.
///
/// <para>
/// Manba — <see cref="ReceiptPrintDto"/> (termal chek JSON'i), ya'ni qatorlar, "to'liq yopildi / qoldi" holati,
/// sinf, QR va matnlar kassada chop etilgan chekdan farq qilmaydi. Tartib <c>schoollms.client/src/lib/
/// thermalReceipt.ts</c> dagi <c>copyHtml</c> bilan bir xil; biri o'zgarsa, ikkinchisi ham o'zgaradi.
/// </para>
/// <para>Sahifa: kengligi 58 mm, balandligi mazmunga qarab (<c>ContinuousSize</c>) — rulon chek kabi.</para>
/// </summary>
public sealed class ThermalReceiptPdf(ReceiptPrintDto r) : IDocument
{
    private const float WidthMm = 58f;
    // Termal chekdagi kabi: 58 mm qog'ozda chop etiladigan kenglik 48 mm.
    private const float SideMarginMm = 5f;

    static ThermalReceiptPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = ReceiptText.MetadataTitle(r.ReceiptNo),
        Subject = ReceiptText.MetadataSubject(r.CancelledAt),
        Author = r.SchoolName,
        Language = "uz",
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public byte[] Render() => this.GeneratePdf();

    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            page.ContinuousSize(WidthMm, Unit.Millimetre);
            page.MarginHorizontal(SideMarginMm, Unit.Millimetre);
            page.MarginVertical(3, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(8.5f).FontColor(Colors.Black).LineHeight(1.25f));
            page.Content().Column(Body);
        });

    private void Body(ColumnDescriptor col)
    {
        if (ReceiptQr.LogoJpeg is { } logo)
            col.Item().AlignCenter().Width(40, Unit.Millimetre).Image(logo);
        else
            col.Item().AlignCenter().Text(r.SchoolName).Bold().FontSize(11);

        if (!string.IsNullOrWhiteSpace(r.SchoolAddress))
            col.Item().PaddingTop(1).AlignCenter().Text(r.SchoolAddress).FontSize(7.5f).AlignCenter();
        if (!string.IsNullOrWhiteSpace(r.SchoolPhone))
            col.Item().AlignCenter().Text($"Tel: {r.SchoolPhone}").FontSize(7.5f);

        Rule(col);
        col.Item().AlignCenter().Text($"TO'LOV CHEKI № {r.ReceiptNo}").Bold().FontSize(9.5f);
        col.Item().AlignCenter().Text(r.ReceivedAtText).FontSize(7.5f);

        if (r.CancelledStamp is { } stamp)
            col.Item().PaddingVertical(2).Border(1.2f).Padding(2).Column(s =>
            {
                s.Item().AlignCenter().Text(stamp).Bold().FontSize(10);
                if (r.CancelledAtText is { } at) s.Item().AlignCenter().Text(at).FontSize(7.5f);
            });

        col.Item().PaddingTop(2).Text("O'quvchi (F.I.SH)").FontSize(7.5f);
        col.Item().Text(r.StudentName).Bold();
        if (r.ClassName is { } cls) col.Item().Text($"Sinf: {cls}").FontSize(7.5f);

        Rule(col);
        foreach (var line in r.Lines)
        {
            col.Item().PaddingTop(1.5f).Row(row =>
            {
                row.RelativeItem().Text(line.CategoryName).Bold();
                row.AutoItem().PaddingLeft(2).Text(line.AmountText).Bold();
            });
            if (!string.IsNullOrEmpty(line.PeriodText)) col.Item().Text(line.PeriodText).FontSize(7.5f);
            if (!string.IsNullOrEmpty(line.StatusText)) col.Item().Text(line.StatusText).FontSize(7.5f);
        }
        Rule(col);

        col.Item().Row(row =>
        {
            row.RelativeItem().Text("JAMI").Bold().FontSize(11);
            row.AutoItem().Text(r.TotalText).Bold().FontSize(11);
        });
        Field(col, "To'lov turi", r.MethodText);
        Field(col, "Kassir", r.CashierName);

        if (r.VerifyUrl is { } url)
        {
            col.Item().PaddingTop(3).AlignCenter().Width(30, Unit.Millimetre).Image(ReceiptQr.Png(url));
            col.Item().AlignCenter().Text(ReceiptText.QrNote).FontSize(7.5f);
        }

        col.Item().PaddingTop(3).AlignCenter().Text(ReceiptText.SloganLine1).Bold().FontSize(8.5f);
        col.Item().AlignCenter().Text(ReceiptText.SloganLine2).Bold().FontSize(8.5f);

        col.Item().PaddingTop(2).AlignCenter().Text($"Chop etildi: {r.PrintedAtText}").FontSize(7);
    }

    private static void Rule(ColumnDescriptor col) =>
        col.Item().PaddingVertical(2).LineHorizontal(0.6f).LineColor(Colors.Black);

    private static void Field(ColumnDescriptor col, string label, string value) =>
        col.Item().Row(row =>
        {
            row.AutoItem().Text(label);
            row.RelativeItem().AlignRight().Text(value);
        });
}
