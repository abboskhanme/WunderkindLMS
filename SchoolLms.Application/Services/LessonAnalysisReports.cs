using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Dars tahlili — PDF report and Excel rating export
//  (docs/modules/lesson-analysis.md §5). Ports of the old app's
//  backend/app/services/pdf.py and excel.py: same content, same order, same
//  Uzbek texts. No database here — the service hands over plain models.
// ===========================================================================

/// <summary>One checklist row printed in the report.</summary>
public sealed record LessonReportRow(
    string SectionId, string SectionTitle, string SectionColor, string CriterionText, bool? IsDone, string? Comment);

/// <summary>Everything the single-observation PDF prints.</summary>
public sealed record LessonReportModel(
    string TeacherName,
    string ObserverName,
    DateOnly LessonDate,
    string? LessonTime,
    string? Subject,
    string? ClassName,
    int ScoreDone,
    int ScoreTotal,
    decimal ScorePercent,
    IReadOnlyList<LessonReportRow> Rows,
    string? Strengths,
    string? GrowthAreas,
    string? NextLessonAdvice,
    string? AiSummary);

/// <summary>Single-observation PDF, laid out like the paper checklist (pdf.py).</summary>
public sealed class LessonObservationPdf(LessonReportModel model) : IDocument
{
    static LessonObservationPdf()
    {
        // QuestPDF Community licence — see ReceiptDocument / docs/ASSUMPTIONS.md.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly Color Ink = Color.FromHex("#1f2937");
    private static readonly Color Muted = Color.FromHex("#6b7280");
    private static readonly Color MetaFill = Color.FromHex("#f3f4f6");
    private static readonly Color MetaBorder = Color.FromHex("#d1d5db");
    private static readonly Color Grid = Color.FromHex("#e5e7eb");
    private static readonly Color Done = Color.FromHex("#15803d");
    private static readonly Color NotDone = Color.FromHex("#b91c1c");
    private static readonly Color Unmarked = Color.FromHex("#9ca3af");

    /// <summary><c>dars-tahlili-{teacher}-{date}.pdf</c> — as the old download name.</summary>
    public static string FileName(string teacherName, DateOnly lessonDate) =>
        $"dars-tahlili-{teacherName}-{lessonDate:yyyy-MM-dd}.pdf";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Dars tahlili — {model.TeacherName} ({model.LessonDate:yyyy-MM-dd})",
        Language = "uz",
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public byte[] Render() => this.GeneratePdf();

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginLeft(14, Unit.Millimetre);
            page.MarginRight(14, Unit.Millimetre);
            page.MarginTop(12, Unit.Millimetre);
            page.MarginBottom(12, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Black));

            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text("WUNDERKIND MAKTABI").FontSize(9).FontColor(Muted);
                col.Item().AlignCenter().Text("DARS KUZATUVI - TAHLIL HISOBOTI").Bold().FontSize(15).FontColor(Ink);
                col.Item().Height(6);
                col.Item().Element(ComposeMeta);
                col.Item().Height(8);
                ComposeCriteria(col);
                col.Item().Height(8);
                TextBlock(col, "KUCHLI TOMONLAR", model.Strengths);
                TextBlock(col, "RIVOJLANISH SOHALARI", model.GrowthAreas);
                TextBlock(col, "KELGUSI DARS UCHUN TAVSIYA", model.NextLessonAdvice);
                TextBlock(col, "AI TAHLIL XULOSASI",
                    string.IsNullOrEmpty(model.AiSummary) ? null : LessonAiPrompt.FormatSummary(model.AiSummary));
            });
        });
    }

    private void ComposeMeta(IContainer container)
    {
        string[][] meta =
        [
            ["O'qituvchi:", model.TeacherName, "Sana:", model.LessonDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)],
            ["Fan:", Dash(model.Subject), "Sinf:", Dash(model.ClassName)],
            ["Kuzatuvchi:", model.ObserverName, "Dars vaqti:", Dash(model.LessonTime)],
            ["Natija:", $"{model.ScoreDone} / {model.ScoreTotal}", "Foiz:",
                $"{LessonAnalysisScoring.FormatWhole((double)model.ScorePercent)}%"],
        ];

        container.Background(MetaFill).Border(0.5f).BorderColor(MetaBorder).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(24, Unit.Millimetre);
                c.ConstantColumn(66, Unit.Millimetre);
                c.ConstantColumn(22, Unit.Millimetre);
                c.RelativeColumn();
            });
            foreach (var row in meta)
                for (var i = 0; i < row.Length; i++)
                {
                    var text = table.Cell().BorderBottom(0.25f).BorderColor(Grid)
                        .PaddingVertical(3).PaddingHorizontal(4).Text(row[i]).FontSize(8.5f);
                    if (i % 2 == 0) text.Bold();
                }
        });
    }

    private void ComposeCriteria(ColumnDescriptor col)
    {
        string? currentSection = null;
        var number = 0;
        foreach (var r in model.Rows)
        {
            if (currentSection != r.SectionId)
            {
                currentSection = r.SectionId;
                col.Item().Height(5);
                col.Item().Background(SafeColor(r.SectionColor)).PaddingVertical(4).PaddingLeft(6)
                    .Text(r.SectionTitle).Bold().FontSize(9).FontColor(Colors.White);
            }

            number++;
            var (mark, markColor) = r.IsDone switch
            {
                true => ("BAJARILDI", Done),
                false => ("BAJARILMADI", NotDone),
                null => ("-", Unmarked),
            };
            col.Item().BorderBottom(0.25f).BorderColor(Grid).PaddingVertical(3).Row(row =>
            {
                row.ConstantItem(8, Unit.Millimetre).AlignMiddle().Text(number.ToString(CultureInfo.InvariantCulture)).FontSize(7.5f);
                row.RelativeItem().AlignMiddle().Column(cell =>
                {
                    cell.Item().Text(r.CriterionText).FontSize(8);
                    if (!string.IsNullOrEmpty(r.Comment))
                        cell.Item().PaddingLeft(4).Text($"Izoh: {r.Comment}").FontSize(7.5f).FontColor(Muted);
                });
                row.ConstantItem(26, Unit.Millimetre).AlignMiddle().AlignCenter()
                    .Text(mark).Bold().FontSize(7.5f).FontColor(markColor);
            });
        }
    }

    private static void TextBlock(ColumnDescriptor col, string title, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        col.Item().PaddingTop(8).PaddingBottom(4).Text(title).Bold().FontSize(10.5f).FontColor(Colors.Black);
        foreach (var line in value.Split('\n'))
        {
            var text = line.Trim();
            col.Item().Text(text.Length == 0 ? " " : text).FontSize(9).LineHeight(1.4f);
        }
    }

    private static string Dash(string? value) => string.IsNullOrEmpty(value) ? "-" : value;

    /// <summary>Section colour from the database; a malformed value must not break the download.</summary>
    private static Color SafeColor(string hex)
    {
        try { return Color.FromHex(hex); }
        catch (Exception) { return Color.FromHex("#4b5563"); }
    }
}

/// <summary>One row of the "Tahlillar" sheet.</summary>
public sealed record LessonExcelObservation(
    DateOnly LessonDate, string TeacherName, string? Subject, string? ClassName, string ObserverName,
    int ScoreDone, int ScoreTotal, decimal ScorePercent, string Status);

/// <summary>School-wide Excel export: rating board + observation log (excel.py).</summary>
public static class LessonAnalysisExcel
{
    public static string FileName(DateOnly today) => $"wunderkind-reyting-{today:yyyy-MM-dd}.xlsx";

    public static byte[] Build(
        IReadOnlyList<LaRatingRowDto> ratings,
        IReadOnlyList<LessonExcelObservation> observations,
        DateOnly? dateFrom,
        DateOnly? dateTo)
    {
        var period = $"{Iso(dateFrom) ?? "..."} - {Iso(dateTo) ?? "..."}";

        var ratingRows = ratings.Select((r, i) => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            (i + 1).ToString(CultureInfo.InvariantCulture),
            r.TeacherName,
            r.Subject ?? "",
            r.ObservationsCount.ToString(CultureInfo.InvariantCulture),
            ExcelExport.XlsxCell.Num(LessonAnalysisScoring.PyRoundDecimal(r.Rating, 1)),
            r.LastObservationDate ?? "",
        ]).ToList();

        var logRows = observations.Select((o, i) => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            (i + 1).ToString(CultureInfo.InvariantCulture),
            o.LessonDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            o.TeacherName,
            o.Subject ?? "",
            o.ClassName ?? "",
            o.ObserverName,
            o.ScoreDone.ToString(CultureInfo.InvariantCulture),
            o.ScoreTotal.ToString(CultureInfo.InvariantCulture),
            ExcelExport.XlsxCell.Num(LessonAnalysisScoring.PyRoundDecimal((double)o.ScorePercent, 1)),
            o.Status,
        ]).ToList();

        return ExcelExport.BuildTables(
        [
            new ExcelExport.TableSpec("Reyting",
                ["#", "O'qituvchi", "Fan", "Tahlillar soni", "Reyting, %", "Oxirgi tahlil"],
                ratingRows,
                Title: $"Wunderkind maktabi - o'qituvchilar reytingi ({period})"),
            new ExcelExport.TableSpec("Tahlillar",
                ["#", "Sana", "O'qituvchi", "Fan", "Sinf", "Kuzatuvchi", "Bajarildi", "Jami", "Foiz", "Status"],
                logRows),
        ]);
    }

    private static string? Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
