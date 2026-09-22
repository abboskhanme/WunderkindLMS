using System.Net;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Exams;

// ===========================================================================
//  Test bazasi — Excel import of questions.
//  docs/modules/admission-and-testing.md §6.2 (template, the seven reasons,
//  dry run), §13 Q9 (.xlsx only). Unit: B1.
// ===========================================================================
//
//  1. THE RULES — straight on `QuestionImportService.Read`, no HTTP: every
//     reason fires on the row that earns it, with 1-based sheet row numbers
//     (header = row 1); a row with two problems is listed twice and counted
//     once; blank rows are ignored; the file-level failures are one sentence.
//  2. THE ENDPOINT — the dry run writes nothing, the real run writes only the
//     valid rows, after the bank's existing questions, in file order, with an
//     audit row; `dryRun` omitted is a dry run; the template reads back.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class QuestionImportTests(ApiFixture fixture)
{
    private const string Import = QuestionBankKit.Questions + "/import";

    // =====================================================================
    //  1. Rules
    // =====================================================================

    [Fact]
    public void Clean_rows_become_questions_in_file_order()
    {
        var plan = Read(
            QuestionBankKit.Row("  Poytaxt?  ", ["Samarqand", "Toshkent", "Buxoro"], "B"),
            QuestionBankKit.Row("2+2", ["3", "4"], "b", "/uploads/7a1f2b0e-2f55-4e0b-9a55-8f2b3a1c4d5e.jpg"));

        Assert.Null(plan.FatalMessage);
        Assert.Equal(2, plan.TotalRows);
        Assert.Equal(0, plan.ErrorCount);
        Assert.Equal(new[] { 2, 3 }, plan.Valid.Select(v => v.Row));

        var first = plan.Valid[0].Question;
        Assert.Equal("Poytaxt?", first.Text);
        Assert.Null(first.ImageUrl);
        Assert.Equal(new[] { "Samarqand", "Toshkent", "Buxoro" }, first.Options.Select(o => o.Text));
        Assert.Equal(new[] { false, true, false }, first.Options.Select(o => o.IsCorrect));

        var second = plan.Valid[1].Question;
        Assert.Equal("/uploads/7a1f2b0e-2f55-4e0b-9a55-8f2b3a1c4d5e.jpg", second.ImageUrl);
        Assert.Equal(new[] { false, true }, second.Options.Select(o => o.IsCorrect));
    }

    /// <summary>One row per reason; the chip order is the order of <see cref="QuestionImportReason.All"/>.</summary>
    [Fact]
    public void Every_reason_is_reported_with_its_sheet_row()
    {
        var plan = Read(
            QuestionBankKit.Row("Yaxshi", ["a", "b"], "A"),                               // row 2 — valid
            QuestionBankKit.Row("   ", ["a", "b"], "A"),                                  // row 3 — emptyText
            QuestionBankKit.Row("Bitta variant", ["a"], "A"),                             // row 4 — tooFewOptions
            QuestionBankKit.Row("Javobsiz", ["a", "b"], ""),                              // row 5 — noCorrect
            QuestionBankKit.Row("G harfi", ["a", "b"], "G"),                              // row 6 — badCorrect
            QuestionBankKit.Row("Bo'sh D", ["a", "b", "c"], "D"),                         // row 7 — badCorrect
            QuestionBankKit.Row("Takror", ["a", " a ", "b"], "C"),                        // row 8 — duplicateOption
            QuestionBankKit.Row("Tashqi rasm", ["a", "b"], "A", "https://x.uz/a.png"));   // row 9 — badImageUrl

        Assert.Null(plan.FatalMessage);
        Assert.Equal(8, plan.TotalRows);
        Assert.Equal(7, plan.ErrorCount);
        Assert.Equal(2, Assert.Single(plan.Valid).Row);

        var result = plan.ToResult("f.xlsx", imported: 0);
        Assert.Equal(1, result.ValidCount);
        Assert.Equal(
            new[]
            {
                (QuestionImportReason.EmptyText, "3"),
                (QuestionImportReason.TooFewOptions, "4"),
                (QuestionImportReason.NoCorrect, "5"),
                (QuestionImportReason.BadCorrect, "6,7"),
                (QuestionImportReason.DuplicateOption, "8"),
                (QuestionImportReason.BadImageUrl, "9"),
            },
            result.Errors.Select(e => (e.Reason, string.Join(",", e.Rows))));
    }

    [Fact]
    public void A_row_with_two_problems_is_listed_under_both_and_counted_once()
    {
        var plan = Read(QuestionBankKit.Row("", ["faqat bitta"], ""));

        Assert.Equal(1, plan.TotalRows);
        Assert.Equal(1, plan.ErrorCount);
        var result = plan.ToResult("f.xlsx", imported: 0);
        Assert.Equal(
            new[] { QuestionImportReason.EmptyText, QuestionImportReason.TooFewOptions, QuestionImportReason.NoCorrect },
            result.Errors.Select(e => e.Reason));
        Assert.All(result.Errors, e => Assert.Equal(new[] { 2 }, e.Rows));
    }

    /// <summary>
    /// A blank row between two questions is skipped and not counted, and the
    /// row after it still reports its real sheet number (the writer stores the
    /// empty row, so the index stays true).
    /// </summary>
    [Fact]
    public void Blank_rows_are_skipped_and_do_not_shift_the_row_numbers()
    {
        var plan = Read(
            QuestionBankKit.Row("Birinchi", ["a", "b"], "A"),
            QuestionBankKit.Row("", [], ""),
            QuestionBankKit.Row("  ", ["  "], " "),
            QuestionBankKit.Row("To'rtinchi qator", ["a", "b"], "Z"));

        Assert.Equal(2, plan.TotalRows);
        Assert.Equal(2, Assert.Single(plan.Valid).Row);
        var error = Assert.Single(plan.ToResult("f.xlsx", 0).Errors);
        Assert.Equal(QuestionImportReason.BadCorrect, error.Reason);
        Assert.Equal(new[] { 5 }, error.Rows);
    }

    [Fact]
    public void A_gap_between_options_closes_up_and_keeps_the_key_on_the_same_text()
    {
        var plan = Read(QuestionBankKit.Row("Bo'shliq", ["a", "b", "", "d"], "D"));

        var question = Assert.Single(plan.Valid).Question;
        Assert.Equal(new[] { "a", "b", "d" }, question.Options.Select(o => o.Text));
        Assert.Equal("d", Assert.Single(question.Options, o => o.IsCorrect).Text);
    }

    /// <summary>Lower case, and the Cyrillic look-alikes a Russian keyboard types.</summary>
    [Theory]
    [InlineData("c")]
    [InlineData(" C ")]
    [InlineData("\u0421")] // Cyrillic capital ES
    [InlineData("\u0441")] // Cyrillic small es
    public void Correct_letter_accepts_lower_case_and_cyrillic_twins(string letter)
    {
        var plan = Read(QuestionBankKit.Row("Harf", ["a", "b", "c"], letter));

        var question = Assert.Single(plan.Valid).Question;
        Assert.Equal("c", Assert.Single(question.Options, o => o.IsCorrect).Text);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("AB")]
    [InlineData("A)")]
    public void Correct_cell_that_is_not_one_letter_is_badCorrect(string letter)
    {
        var plan = Read(QuestionBankKit.Row("Harf", ["a", "b", "c"], letter));

        Assert.Empty(plan.Valid);
        Assert.Equal(QuestionImportReason.BadCorrect, Assert.Single(plan.ToResult("f.xlsx", 0).Errors).Reason);
    }

    [Fact]
    public void Wrong_headers_are_fatal()
    {
        var headers = QuestionImportService.Headers.ToArray();
        headers[7] = "Javob";
        var bytes = QuestionBankKit.Sheet([QuestionBankKit.Row("Q", ["a", "b"], "A")], headers);

        var plan = ReadBytes(bytes);

        Assert.Equal(QuestionImportService.BadHeaderMessage, plan.FatalMessage);
    }

    [Fact]
    public void The_optional_image_header_may_be_left_blank()
    {
        var headers = QuestionImportService.Headers.ToArray();
        headers[8] = "";

        var plan = ReadBytes(QuestionBankKit.Sheet([QuestionBankKit.Row("Q", ["a", "b"], "A")], headers));

        Assert.Null(plan.FatalMessage);
        Assert.Single(plan.Valid);
    }

    [Fact]
    public void A_broken_file_is_a_sentence_not_an_exception()
    {
        var plan = ReadBytes("PK\u0003\u0004 this is not a workbook"u8.ToArray());

        Assert.Equal(QuestionImportService.BadFileMessage, plan.FatalMessage);
    }

    [Fact]
    public void A_header_only_sheet_is_zero_rows_not_an_error()
    {
        var plan = Read();

        Assert.Null(plan.FatalMessage);
        Assert.Equal(0, plan.TotalRows);
        Assert.Empty(plan.ToResult("f.xlsx", 0).Errors);
    }

    [Fact]
    public void More_than_the_row_cap_is_fatal()
    {
        var rows = Enumerable.Range(0, QuestionImportService.MaxRows + 1)
            .Select(i => QuestionBankKit.Row($"Savol {i}", ["a", "b"], "A"))
            .ToArray();

        var plan = Read(rows);

        Assert.Equal(QuestionImportService.TooManyRowsMessage(QuestionImportService.MaxRows + 1), plan.FatalMessage);
    }

    [Theory]
    [InlineData("savollar.xls", 100L, QuestionImportService.NotXlsxMessage)]
    [InlineData("savollar.csv", 100L, QuestionImportService.NotXlsxMessage)]
    [InlineData("savollar.xlsx.exe", 100L, QuestionImportService.NotXlsxMessage)]
    [InlineData("SAVOLLAR.XLSX", 100L, null)]
    [InlineData("savollar.xlsx", 0L, QuestionImportService.NoFileMessage)]
    [InlineData("", 100L, QuestionImportService.NoFileMessage)]
    public void Only_xlsx_is_accepted(string fileName, long length, string? expected)
    {
        Assert.Equal(expected, QuestionImportService.RejectFile(fileName, length));
    }

    /// <summary>§13 Q9: "the existing Uzbek error message" — one sentence across both imports.</summary>
    [Fact]
    public void The_xlsx_only_sentence_is_the_pupil_import_one()
    {
        Assert.Equal(StudentImportController.NotXlsxMessage, QuestionImportService.NotXlsxMessage);
    }

    // =====================================================================
    //  2. Endpoint
    // =====================================================================

    [Fact]
    public async Task Dry_run_writes_nothing_and_the_real_run_writes_only_the_valid_rows_after_the_existing_ones()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        await QuestionBankKit.SeedQuestionAsync(fixture.Api, bank.Id, "Oldingi", ["a", "b"], correct: 0, order: 0);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var sheet = QuestionBankKit.Sheet(
        [
            QuestionBankKit.Row("Birinchi", ["1", "2", "3"], "C"),
            QuestionBankKit.Row("Ikkinchi", ["x", "y"], "A"),
            QuestionBankKit.Row("Xato", ["x", "y"], "F"),
            QuestionBankKit.Row("Uchinchi", ["p", "q", "r", "s", "t", "u"], "F"),
        ]);

        using (var dry = await ResultAsync(await admin.PostAsync(Import, QuestionBankKit.ImportForm(sheet, bank.Id, dryRun: true))))
        {
            var r = dry.RootElement;
            Assert.Equal("savollar.xlsx", r.GetProperty("fileName").GetString());
            Assert.Equal(4, r.GetProperty("totalRows").GetInt32());
            Assert.Equal(3, r.GetProperty("validCount").GetInt32());
            Assert.Equal(1, r.GetProperty("errorCount").GetInt32());
            Assert.Equal(0, r.GetProperty("imported").GetInt32());
            var error = Assert.Single(r.GetProperty("errors").EnumerateArray());
            Assert.Equal(QuestionImportReason.BadCorrect, error.GetProperty("reason").GetString());
            Assert.Equal(new[] { 4 }, error.GetProperty("rows").EnumerateArray().Select(x => x.GetInt32()));
        }
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(1, await db.Questions.CountAsync(q => q.BankId == bank.Id)));

        using (var real = await ResultAsync(await admin.PostAsync(Import, QuestionBankKit.ImportForm(sheet, bank.Id, dryRun: false))))
            Assert.Equal(3, real.RootElement.GetProperty("imported").GetInt32());

        await fixture.Api.WithDbAsync(async db =>
        {
            var imported = await db.Questions.Include(q => q.Options)
                .Where(q => q.BankId == bank.Id && q.Text != "Oldingi")
                .OrderBy(q => q.Order).ToListAsync();
            Assert.Equal(new[] { "Birinchi", "Ikkinchi", "Uchinchi" }, imported.Select(q => q.Text));
            Assert.Equal(new[] { 1, 2, 3 }, imported.Select(q => q.Order));
            Assert.Equal("3", imported[0].Options.Single(o => o.IsCorrect).Text);
            Assert.Equal("u", imported[2].Options.Single(o => o.IsCorrect).Text);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, imported[2].Options.OrderBy(o => o.Order).Select(o => o.Order));

            var audit = await db.AuditLogs.SingleAsync(a =>
                a.EntityType == QuestionBankService.AuditEntityBank && a.EntityId == bank.Id && a.Action == "import");
            Assert.Contains("3 ta savol", audit.Summary);
        });
    }

    [Fact]
    public async Task Omitted_dryRun_is_a_dry_run()
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var sheet = QuestionBankKit.Sheet([QuestionBankKit.Row("Q", ["a", "b"], "A")]);

        using (var result = await ResultAsync(await admin.PostAsync(Import, QuestionBankKit.ImportForm(sheet, bank.Id, dryRun: null))))
        {
            Assert.Equal(1, result.RootElement.GetProperty("validCount").GetInt32());
            Assert.Equal(0, result.RootElement.GetProperty("imported").GetInt32());
        }

        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.Questions.AnyAsync(q => q.BankId == bank.Id)));
    }

    [Theory]
    [InlineData("savollar.xls", QuestionImportService.NotXlsxMessage)]
    [InlineData("savollar.xlsx", QuestionImportService.BadFileMessage)]
    public async Task Unusable_file_is_400_with_one_sentence(string fileName, string message)
    {
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsync(Import,
            QuestionBankKit.ImportForm("not a workbook"u8.ToArray(), bank.Id, dryRun: false, fileName));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(QuestionBankService.Codes.Validation, doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Import_into_an_unknown_bank_is_404()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var sheet = QuestionBankKit.Sheet([QuestionBankKit.Row("Q", ["a", "b"], "A")]);

        var response = await admin.PostAsync(Import, QuestionBankKit.ImportForm(sheet, Guid.NewGuid().ToString(), dryRun: true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(QuestionBankService.Codes.BankNotFound, doc.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// The template's first sheet carries exactly the import headers (so a
    /// filled-in template imports), and its rules sheet names the bank.
    /// </summary>
    [Fact]
    public async Task Template_is_savollar_shablon_with_the_import_headers_and_the_bank_named()
    {
        var subject = await QuestionBankKit.SeedSubjectAsync(fixture.Api, $"Tarix {QuestionBankKit.Tag()}");
        var bank = await QuestionBankKit.SeedBankAsync(fixture.Api, subject, grade: 0);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.GetAsync($"{QuestionBankKit.Questions}/import/template?bankId={bank.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(QuestionImportService.TemplateFileName, response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        var rows = StudentListTests.XlsxRows(bytes, QuestionImportService.Headers.Length);
        Assert.Equal(QuestionImportService.Headers, Assert.Single(rows));

        var sheetNames = SheetNames(bytes);
        Assert.Equal(new[] { QuestionImportService.SheetName, QuestionImportService.GuideSheetName }, sheetNames);
        Assert.Contains(QuestionBankService.BankTitle(0, subject.Name), GuideText(bytes));

        // An untouched template is a valid, empty import.
        Assert.Equal(0, ReadBytes(bytes).TotalRows);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static QuestionImportPlan Read(params IReadOnlyList<string>[] rows) =>
        ReadBytes(QuestionBankKit.Sheet(rows));

    private static QuestionImportPlan ReadBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return QuestionImportService.Read(stream);
    }

    private static async Task<JsonDocument> ResultAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private static List<string> SheetNames(byte[] bytes)
    {
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        return doc.WorkbookPart!.Workbook!.Descendants<Sheet>().Select(s => s.Name!.Value!).ToList();
    }

    /// <summary>All the text of the rules sheet, joined — enough to look for the bank's name.</summary>
    private static string GuideText(byte[] bytes)
    {
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var workbook = doc.WorkbookPart!;
        var sheet = workbook.Workbook!.Descendants<Sheet>().Single(s => s.Name?.Value == QuestionImportService.GuideSheetName);
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
        return string.Join("\n", part.Worksheet!.Descendants<Cell>().Select(c => c.InnerText));
    }
}
