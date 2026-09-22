using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using static SchoolLms.Application.Services.QuestionBankService;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Test bazasi — Excel import of questions into one bank.
//  docs/modules/admission-and-testing.md §6.2 (template, reasons), §13 Q9. Unit: B1.
// ===========================================================================
//
//  ONE ENDPOINT, A DRY RUN, NO STAGING
//  -----------------------------------
//  EduSchool does preview → importToken → confirm, and its token can expire.
//  Ours is single-step like every other import here: `dryRun=true` reads and
//  reports, `dryRun=false` reads the SAME file again and writes. Nothing is
//  kept on the server between the two posts.
//
//  PARTIAL IMPORT IS THE CONTRACT HERE
//  -----------------------------------
//  Unlike the pupil import (all-or-nothing), §6.2 says a row with any error is
//  skipped whole and the valid rows still import. A question is independent of
//  its neighbours — importing 48 good ones while two are fixed loses nothing.
//
//  THE REASONS ARE A CLOSED SET
//  ----------------------------
//  The screen renders each reason as one chip with its row numbers, so the
//  seven strings in `QuestionImportReason` are the whole vocabulary. A row with
//  two problems is listed under both, so one round of fixes clears the file.
//
//  THIS CLASS DOES NOT TOUCH THE DATABASE
//  --------------------------------------
//  `Read` is pure (stream in, plan out) so the rules are testable without HTTP;
//  the controller checks the bank, writes the plan and records the audit row.
// ===========================================================================

/// <summary>
/// The seven import error reasons of §6.2 — closed list, in the order the chips
/// are shown. The client maps each to its Uzbek text (<c>BankHelpers.ts</c>).
/// </summary>
public static class QuestionImportReason
{
    /// <summary>"Savol matni bo'sh" — column A blank after trim.</summary>
    public const string EmptyText = "emptyText";

    /// <summary>"Variant 2 tadan kam" — fewer than 2 non-blank option cells.</summary>
    public const string TooFewOptions = "tooFewOptions";

    /// <summary>
    /// "Variant 6 tadan ko'p". Cannot fire while the sheet has six option
    /// columns (B–G); kept because §6.2 fixes the set and the check costs nothing.
    /// </summary>
    public const string TooManyOptions = "tooManyOptions";

    /// <summary>"To'g'ri javob ko'rsatilmagan" — column H blank.</summary>
    public const string NoCorrect = "noCorrect";

    /// <summary>"To'g'ri javob noto'g'ri" — column H is not A–F, or names a blank option.</summary>
    public const string BadCorrect = "badCorrect";

    /// <summary>"Bir xil variant" — two option cells identical after trim.</summary>
    public const string DuplicateOption = "duplicateOption";

    /// <summary>"Rasm havolasi noto'g'ri" — column I set and not an <c>/uploads/</c> path.</summary>
    public const string BadImageUrl = "badImageUrl";

    public static readonly IReadOnlyList<string> All =
        [EmptyText, TooFewOptions, TooManyOptions, NoCorrect, BadCorrect, DuplicateOption, BadImageUrl];
}

/// <summary>A row that passed every check, and where it came from.</summary>
/// <param name="Row">1-based sheet row including the header.</param>
public sealed record ImportedQuestion(int Row, CheckedQuestion Question);

/// <summary>The whole file, read and checked. Built by <see cref="QuestionImportService.Read"/>.</summary>
public sealed class QuestionImportPlan
{
    private readonly Dictionary<string, List<int>> _rowsByReason = new(StringComparer.Ordinal);

    /// <summary>The file cannot be read at all (broken, wrong columns, too big) — a 400, no report.</summary>
    public string? FatalMessage { get; private init; }

    /// <summary>Non-blank data rows.</summary>
    public int TotalRows { get; private set; }

    /// <summary>Rows with at least one error.</summary>
    public int ErrorCount { get; private set; }

    public List<ImportedQuestion> Valid { get; } = [];

    public static QuestionImportPlan Fatal(string message) => new() { FatalMessage = message };

    internal void AddValid(ImportedQuestion question)
    {
        TotalRows++;
        Valid.Add(question);
    }

    internal void AddInvalid(int row, IEnumerable<string> reasons)
    {
        TotalRows++;
        ErrorCount++;
        foreach (var reason in reasons)
        {
            if (!_rowsByReason.TryGetValue(reason, out var rows)) _rowsByReason[reason] = rows = [];
            rows.Add(row);
        }
    }

    /// <summary>The wire shape. <paramref name="imported"/> is 0 on a dry run.</summary>
    public QuestionImportResultDto ToResult(string fileName, int imported) => new(
        fileName,
        TotalRows,
        Valid.Count,
        ErrorCount,
        imported,
        QuestionImportReason.All
            .Where(_rowsByReason.ContainsKey)
            .Select(reason => new QuestionImportErrorDto(reason, _rowsByReason[reason].Order().ToList()))
            .ToList());
}

/// <summary>Template, file checks and row rules of the question import.</summary>
public static class QuestionImportService
{
    public const string SheetName = "Savollar";
    public const string GuideSheetName = "Yo'riqnoma";
    public const string TemplateFileName = "savollar_shablon.xlsx";
    public const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Upload ceiling — the same 10 MB as the pupil import.</summary>
    public const int MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Data rows per file. A bank is "a few hundred rows" (§5.1); 2 000 leaves
    /// room and still stops a stray 100 000-row sheet before it becomes one INSERT.
    /// </summary>
    public const int MaxRows = 2000;

    /// <summary>§6.2 column order. The sheet MUST be the first one in the book — <see cref="ExcelImport"/> reads that.</summary>
    public static readonly string[] Headers =
    [
        "Savol matni",          // 0  A
        "A",                    // 1  B
        "B",                    // 2  C
        "C",                    // 3  D
        "D",                    // 4  E
        "E",                    // 5  F
        "F",                    // 6  G
        "To'g'ri javob (A-F)",  // 7  H
        "Rasm havolasi",        // 8  I
    ];

    private const int TextColumn = 0;
    private const int FirstOptionColumn = 1;
    private const int OptionColumns = 6;
    private const int CorrectColumn = 7;
    private const int ImageColumn = 8;

    public const string NoFileMessage = "Fayl tanlanmagan";

    /// <summary>§13 Q9 — word for word <c>StudentImportController.NotXlsxMessage</c> (a test pins them together).</summary>
    public const string NotXlsxMessage = "Faqat .xlsx (Excel) fayl qabul qilinadi";

    public const string BadFileMessage = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring";

    public const string BadHeaderMessage =
        "Ustunlar shablonga mos emas. «Shablon» tugmasi orqali shablonni yuklab oling "
        + "va ustun nomlarini o'zgartirmasdan to'ldiring.";

    public static string TooManyRowsMessage(int rows) =>
        $"Faylda {rows} ta savol — bir martada eng ko'pi {MaxRows} ta";

    // =====================================================================
    //  File and template
    // =====================================================================

    /// <summary>Is the upload acceptable at all. <c>null</c> = yes. Extension only — the parser judges the content.</summary>
    public static string? RejectFile(string? fileName, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName) || length <= 0) return NoFileMessage;
        return fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? null : NotXlsxMessage;
    }

    /// <summary>
    /// <c>savollar_shablon.xlsx</c>: sheet <c>Savollar</c> (headers only — a sample
    /// row would be imported by whoever forgets to delete it) and the rules sheet.
    /// </summary>
    public static byte[] Template(string bankTitle)
    {
        var guide = new List<IReadOnlyList<string>>
        {
            new[] { "Baza", bankTitle },
            new[] { "", "" },
            new[] { Headers[TextColumn], "Majburiy. Bir qator — bitta savol." },
            new[] { "A … F", "Javob variantlari: kamida 2 ta, ko'pi bilan 6 ta. Bo'sh katak — variant yo'q." },
            new[] { Headers[CorrectColumn], "Majburiy. To'g'ri variantning harfi, masalan: B. To'ldirilgan katakni ko'rsatsin." },
            new[] { Headers[ImageColumn], "Ixtiyoriy. Tizimga yuklangan rasm havolasi — /uploads/ bilan boshlanadi." },
            new[] { "", "" },
            new[] { "Bir xil variant", "Bitta savolda ikki variant bir xil bo'lmasin." },
            new[] { "Oradagi bo'sh katak", "Variantlar ketma-ket qayta harflanadi, to'g'ri javob o'zgarmaydi." },
            new[] { "Xato qator", "Butunlay o'tkazib yuboriladi; qolgan to'g'ri qatorlar yoziladi." },
            new[] { "Tekshirish", "Fayl avval tekshiriladi — bazaga hech narsa yozilmaydi. Savollar «Yozish» dan keyin qo'shiladi." },
            new[] { "Qayta yuklash", "Bir faylni ikki marta yozish savollarni ikki marta qo'shadi." },
            new[] { "Fayl turi", "Faqat .xlsx" },
        };

        return ExcelExport.Build(
        [
            new ExcelExport.SheetSpec(SheetName, Headers, []),
            new ExcelExport.SheetSpec(GuideSheetName, ["Maydon", "Izoh"], guide),
        ]);
    }

    // =====================================================================
    //  Reading
    // =====================================================================

    /// <summary>
    /// Reads the first sheet and checks every row. Never throws on a bad file —
    /// a file it cannot use comes back as <see cref="QuestionImportPlan.FatalMessage"/>.
    ///
    /// <para>
    /// <b>Row numbers.</b> <see cref="ExcelImport.ReadRows"/> returns the rows the
    /// file stores, in order, without their <c>r=</c> index; the sheet row is
    /// therefore <c>list index + 1</c>. That holds for every file our template
    /// and Excel produce with contiguous rows. Excel does not store a fully
    /// empty row, so a blank line in the MIDDLE of the data shifts the numbers
    /// after it by one — the same limitation the pupil import has. The fix
    /// belongs in <c>ExcelImport</c> (return <c>Row.RowIndex</c>), a shared file.
    /// </para>
    /// </summary>
    public static QuestionImportPlan Read(Stream xlsx)
    {
        List<string[]> rows;
        try
        {
            rows = ExcelImport.ReadRows(xlsx, Headers.Length);
        }
        catch (Exception)
        {
            // OpenXML throws a handful of unrelated types for a broken or
            // non-OOXML file; to the user every one of them means the same thing.
            return QuestionImportPlan.Fatal(BadFileMessage);
        }

        var plan = new QuestionImportPlan();
        if (rows.Count == 0) return plan; // an empty sheet — reported as 0 rows, the screen explains it
        if (!HeadersMatch(rows[0])) return QuestionImportPlan.Fatal(BadHeaderMessage);

        var dataRows = rows.Skip(1).Count(r => !IsBlank(r));
        if (dataRows > MaxRows) return QuestionImportPlan.Fatal(TooManyRowsMessage(dataRows));

        for (var i = 1; i < rows.Count; i++)
        {
            if (IsBlank(rows[i])) continue;
            var sheetRow = i + 1; // 1-based, header is row 1

            var (question, reasons) = CheckRow(rows[i]);
            if (question is null) plan.AddInvalid(sheetRow, reasons);
            else plan.AddValid(new ImportedQuestion(sheetRow, question));
        }

        return plan;
    }

    /// <summary>
    /// The plan's valid rows as entities, appended after the bank's last question
    /// in file order. The caller adds them and saves once — one statement batch,
    /// one transaction.
    /// </summary>
    public static List<Question> ToQuestions(string bankId, int firstOrder, QuestionImportPlan plan) =>
        plan.Valid.Select((v, i) => NewQuestion(bankId, firstOrder + i, v.Question)).ToList();

    /// <summary>
    /// Every §6.2 rule for one row. Returns the question when the row is clean,
    /// otherwise <c>null</c> and every reason that applies.
    ///
    /// <para>
    /// Options are the non-blank cells B–G in column order; a gap is closed up,
    /// so "A, B, D filled, correct D" stores three options with the third one
    /// correct. The answer's TEXT is what matters to the candidate, and a gap is
    /// not one of the seven reasons.
    /// </para>
    /// </summary>
    private static (CheckedQuestion? Question, List<string> Reasons) CheckRow(string[] cells)
    {
        var reasons = new List<string>();

        var text = cells[TextColumn].Trim();
        if (text.Length == 0) reasons.Add(QuestionImportReason.EmptyText);

        var options = new List<(int Letter, string Text)>();
        for (var letter = 0; letter < OptionColumns; letter++)
        {
            var value = cells[FirstOptionColumn + letter].Trim();
            if (value.Length > 0) options.Add((letter, value));
        }
        if (options.Count < MinOptions) reasons.Add(QuestionImportReason.TooFewOptions);
        if (options.Count > MaxOptions) reasons.Add(QuestionImportReason.TooManyOptions);
        if (options.Select(o => o.Text).Distinct(StringComparer.Ordinal).Count() != options.Count)
            reasons.Add(QuestionImportReason.DuplicateOption);

        var correctCell = cells[CorrectColumn].Trim();
        var correctLetter = CorrectLetter(correctCell);
        if (correctCell.Length == 0)
            reasons.Add(QuestionImportReason.NoCorrect);
        else if (correctLetter is null || options.All(o => o.Letter != correctLetter))
            reasons.Add(QuestionImportReason.BadCorrect);

        var image = cells[ImageColumn].Trim();
        if (image.Length > 0 && !IsUploadUrl(image)) reasons.Add(QuestionImportReason.BadImageUrl);

        if (reasons.Count > 0) return (null, reasons);

        var question = new CheckedQuestion(
            text,
            image.Length == 0 ? null : image,
            options.Select(o => new CheckedOption(null, o.Text, o.Letter == correctLetter)).ToList());
        return (question, reasons);
    }

    /// <summary>
    /// "A"…"F" → 0…5, case-insensitive. The Cyrillic А, В, С, Е are accepted as
    /// their Latin twins: they look identical in Excel, a Russian keyboard
    /// layout types them, and refusing them would show an error the user cannot
    /// see the cause of. Anything else → <c>null</c>.
    /// </summary>
    private static int? CorrectLetter(string cell)
    {
        if (cell.Length != 1) return null;
        var c = char.ToUpperInvariant(cell[0]) switch
        {
            '\u0410' => 'A', // Cyrillic А
            '\u0412' => 'B', // Cyrillic В
            '\u0421' => 'C', // Cyrillic С
            '\u0415' => 'E', // Cyrillic Е
            var other => other,
        };
        var index = OptionLetters.IndexOf(c);
        return index < 0 ? null : index;
    }

    /// <summary>
    /// The first eight headers must match (by <see cref="StudentImportSheet.HeaderKey"/>:
    /// letters and digits only, case-insensitive). The optional image column may
    /// have lost its header.
    /// </summary>
    private static bool HeadersMatch(string[] header)
    {
        for (var i = 0; i < ImageColumn; i++)
        {
            if (StudentImportSheet.HeaderKey(header[i]) != StudentImportSheet.HeaderKey(Headers[i]))
                return false;
        }
        var imageKey = StudentImportSheet.HeaderKey(header[ImageColumn]);
        return imageKey.Length == 0 || imageKey == StudentImportSheet.HeaderKey(Headers[ImageColumn]);
    }

    private static bool IsBlank(string[] row) => row.All(string.IsNullOrWhiteSpace);
}
