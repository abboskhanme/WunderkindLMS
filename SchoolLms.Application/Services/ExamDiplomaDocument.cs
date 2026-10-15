using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolLms.Application.Billing;

namespace SchoolLms.Application.Services;

/// <summary>What goes on a "Tashakkurnoma" (docs/modules/exam-awards.md).</summary>
public sealed record ExamDiplomaModel(
    string SchoolName, string StudentName, string ClassName, string ExamTitle, string? ExamDate,
    int Rank, string Score, string Percent, DateOnly IssuedOn);

/// <summary>
/// The thank-you diploma sent with a top result (client, 2026-10-15: "tashakkurnoma diplom"). A4 landscape, the
/// school's logo, the pupil's name large; text in Uzbek — it is a document for the family. No database, no network:
/// the same split as <see cref="ReceiptDocument"/>, whose static constructor also sets the QuestPDF licence.
/// </summary>
public sealed class ExamDiplomaDocument(ExamDiplomaModel model) : IDocument
{
    static ExamDiplomaDocument()
    {
        // QuestPDF Community — free below 1 M USD yearly revenue (docs/ASSUMPTIONS.md, ReceiptDocument).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly Color Gold = Color.FromHex("#FFD006");
    private static readonly Color Ink = Colors.Black;
    private static readonly Color Muted = Colors.Grey.Darken1;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Tashakkurnoma — {model.StudentName}",
        Author = model.SchoolName,
        Language = "uz",
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public byte[] Render() => this.GeneratePdf();

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(14, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(13).FontColor(Ink));
            page.Content().Border(3).BorderColor(Gold).Padding(14, Unit.Millimetre).Element(Body);
        });
    }

    private void Body(IContainer container) =>
        container.Column(col =>
        {
            col.Spacing(10);
            if (ReceiptQr.LogoJpeg is { } logo)
                col.Item().AlignCenter().Height(20, Unit.Millimetre).Image(logo).FitHeight();
            else
                col.Item().AlignCenter().Text(model.SchoolName).Bold().FontSize(18);

            col.Item().PaddingTop(6).AlignCenter().Text("TASHAKKURNOMA").Bold().FontSize(40).LetterSpacing(0.08f);
            col.Item().AlignCenter().Width(60, Unit.Millimetre).LineHorizontal(2).LineColor(Gold);

            col.Item().PaddingTop(10).AlignCenter().Text(model.StudentName).Bold().FontSize(28);
            if (!string.IsNullOrWhiteSpace(model.ClassName))
                col.Item().AlignCenter().Text($"{model.ClassName} sinf o'quvchisi").FontSize(14).FontColor(Muted);

            col.Item().PaddingTop(8).PaddingHorizontal(20, Unit.Millimetre).AlignCenter().Text(text =>
            {
                text.AlignCenter();
                var when = string.IsNullOrWhiteSpace(model.ExamDate) ? "" : $"{model.ExamDate} kuni o'tkazilgan ";
                text.Span($"{when}«{model.ExamTitle}» da ");
                text.Span($"{model.Score} ball ({model.Percent})").Bold();
                text.Span(" to'plab, ");
                text.Span($"{model.Rank}-o'rinni").Bold();
                text.Span(" egallagani uchun taqdirlanadi. Bundan keyin ham yuksak natijalar tilaymiz!");
            });

            col.Item().PaddingTop(24).Row(row =>
            {
                row.RelativeItem().AlignBottom().Text(model.IssuedOn.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
                    .FontSize(11).FontColor(Muted);
                row.RelativeItem().AlignRight().AlignBottom().Text("Direktor: ____________________").FontSize(12);
            });
        });
}
