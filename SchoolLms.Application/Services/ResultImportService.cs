using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Manual exam results from Excel — the template and the import.
//  Spec: docs/modules/admission-and-testing.md §6.2 (the dry-run rule, the
//  error-report shape), §6.3 (`entry-table/template`, `entry-table/import`),
//  §8.4 ("import validates the same way and reports per-row errors before
//  writing"). Unit: B2.
// ===========================================================================
//
//  ONE ENDPOINT, POSTED TWICE (§6.2)
//  ---------------------------------
//  `dryRun=true` reads, checks and reports; `dryRun=false` does the same and
//  then writes the valid rows. No staging table, no token, no expiry.
//
//  THE WRITE IS THE GRID'S WRITE
//  -----------------------------
//  Valid rows are handed to `ExamService.SaveEntryAsync` — the very method
//  behind the grid's "Saqlash". So an imported score and a typed score are
//  validated, rounded, summarised, audited and stamped identically, and there
//  is no second way for a result to reach `exam_section_scores`.
//
//  THE SHEET
//  ---------
//  `ID | F.I.SH | Sinf | <fan> (0–<max>) … | Kelmadi`, one row per
//  participant, PRE-FILLED with what is stored — so the template doubles as
//  an export, and re-importing an untouched file changes nothing. Rows are
//  matched on the participant id in column A, never on the name: two pupils
//  called "Aliyev Ali" in one year group is ordinary.
//  Subject columns are matched on their HEADER, not their position, so a
//  column the user moved still lands in the right subject. An unknown header
//  is refused (400) rather than skipped — a renamed subject column that was
//  silently ignored would read as "imported" and write nothing.
// ===========================================================================

/// <summary>Template and import of manual exam results (§6.3). Stateless; see the file header.</summary>
public static class ResultImportService
{
    public const string TemplateFileName = "natijalar_shablon.xlsx";

    public const string IdHeader = "ID";
    public const string NameHeader = "F.I.SH";
    public const string ClassHeader = "Sinf";
    public const string AbsentHeader = "Kelmadi";

    /// <summary>What the template writes into <see cref="AbsentHeader"/> for an absent pupil.</summary>
    public const string AbsentYes = "ha";

    public const string UnreadableMessage = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring";
    public const string EmptyFileMessage = "Fayl bo'sh — shablonni yuklab olib, uni to'ldiring";
    public const string BadTemplateMessage =
        "Fayl natijalar shabloniga mos emas: birinchi ustun «ID» bo'lishi kerak. Avval shablonni yuklab oling.";

    /// <summary>
    /// Columns read beyond the ones the template has — enough to notice an
    /// extra column the user added, so it can be refused by name.
    /// </summary>
    private const int ExtraColumns = 20;

    /// <summary>
    /// The closed set of <c>reason</c> keys (the client renders one chip per
    /// reason; <c>ResultImportModal.tsx</c> has a label for each of the first
    /// six and shows <see cref="ExamResultImportErrorDto.Message"/> for all).
    /// </summary>
    public static class Reasons
    {
        public const string UnknownParticipant = "unknownParticipant";
        public const string DuplicateParticipant = "duplicateParticipant";
        public const string OutOfRange = "outOfRange";
        public const string NotANumber = "notANumber";
        public const string BadAbsent = "badAbsent";
        public const string ParticipantLocked = "participantLocked";
    }

    private static readonly Dictionary<string, string> ReasonMessages = new(StringComparer.Ordinal)
    {
        [Reasons.UnknownParticipant] = "Ishtirokchi topilmadi (ID ustuni bo'sh yoki boshqa imtihonniki)",
        [Reasons.DuplicateParticipant] = "Ishtirokchi takrorlangan",
        [Reasons.OutOfRange] = "Ball ruxsat etilgan oraliqdan tashqarida",
        [Reasons.NotANumber] = "Ball son emas",
        [Reasons.BadAbsent] = "\"Kelmadi\" ustuni noto'g'ri (\"ha\" yoki bo'sh; \"ha\" bo'lsa ball yozilmaydi)",
        [Reasons.ParticipantLocked] = "Ishtirok bekor qilingan yoki imtihon hali davom etmoqda — natija kiritib bo'lmaydi",
    };

    /// <summary>Order of the chips in the report — the order a user fixes things in.</summary>
    private static readonly string[] ReasonOrder =
    [
        Reasons.UnknownParticipant, Reasons.DuplicateParticipant, Reasons.ParticipantLocked,
        Reasons.NotANumber, Reasons.OutOfRange, Reasons.BadAbsent,
    ];

    /// <summary>The header of a subject column: <c>Matematika (0–50)</c>.</summary>
    public static string SectionHeader(string subjectName, decimal maxScore) =>
        $"{subjectName} (0–{ExamScoringService.Format(maxScore)})";

    // =====================================================================
    //  Template
    // =====================================================================

    /// <summary>
    /// <c>GET /api/admin/exams/{id}/entry-table/template</c> — the sheet
    /// pre-filled with every participant that can still receive a score and
    /// what is stored for them, plus a second sheet with the rules.
    /// </summary>
    public static async Task<ExamOutcome<byte[]>> TemplateAsync(
        IAppDbContext db, string examId, CancellationToken ct = default)
    {
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null) return ExamError.NotFound("exam_not_found", ExamService.ExamNotFoundMessage);
        if (ExamService.EntryStateProblem(exam) is { } stateProblem) return stateProblem;

        var count = await db.ExamParticipants.AsNoTracking().CountAsync(p => p.ExamId == examId, ct);
        if (count > ExamService.EntryTableCap) return ExamService.EntryTooLarge(count);

        var columns = await ExamService.ColumnsAsync(db, examId, ct);
        var people = await ExamService.EntryPeopleAsync(db, examId, ct);
        var scores = await ExamService.ScoresOfExamAsync(db, examId, ct);

        var headers = new List<string> { IdHeader, NameHeader, ClassHeader };
        headers.AddRange(columns.Select(c => SectionHeader(c.Name, c.MaxScore)));
        headers.Add(AbsentHeader);

        var rows = people.Where(p => !IsLocked(p.Status)).Select(p =>
        {
            var mine = scores.GetValueOrDefault(p.ParticipantId);
            var cells = new List<string> { p.ParticipantId, p.FullName, p.ClassName ?? "" };
            cells.AddRange(columns.Select(c =>
                mine is not null && mine.TryGetValue(c.SectionId, out var pts) ? ExamScoringService.Format(pts) : ""));
            cells.Add(p.Status == ExamParticipantStatus.Absent ? AbsentYes : "");
            return (IReadOnlyList<string>)cells;
        }).ToList();

        string[] rules =
        [
            "«ID» ustunini o'zgartirmang — natija shu raqam bo'yicha ishtirokchiga yoziladi (ism bo'yicha emas).",
            "Har bir fan ustuniga 0 dan shu fanning maksimal ballgacha son yozing: 42 yoki 42.5 (ko'pi bilan 2 xona kasr).",
            "Bo'sh katak — o'sha fan bo'yicha saqlangan ball o'zgarmaydi. Ballni o'chirish uchun «Kelmadi» belgilang.",
            $"«{AbsentHeader}» ustuniga «{AbsentYes}» yozing — o'quvchining shu imtihondagi ballari o'chadi. Bunday qatorga ball yozilmaydi.",
            "Ustun nomlarini o'zgartirmang va yangi ustun qo'shmang; ustunlar tartibini o'zgartirsa bo'ladi.",
            "Avval «Tekshirish» — fayl yozilmasdan tekshiriladi. Xatoli qatorlar yozilmaydi, qolganlari yoziladi.",
            "Fayl faqat .xlsx (Excel) ko'rinishida qabul qilinadi.",
        ];

        var bytes = ExcelExport.Build(
        [
            new ExcelExport.SheetSpec("Natijalar", headers, rows),
            new ExcelExport.SheetSpec("Yo'riqnoma", ["Qoida"], rules.Select(r => (IReadOnlyList<string>)new[] { r })),
        ]);
        return bytes;
    }

    // =====================================================================
    //  Import
    // =====================================================================

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/entry-table/import</c>. A row with any
    /// error is skipped whole and reported; the valid rows are written (when
    /// <paramref name="dryRun"/> is false) through
    /// <see cref="ExamService.SaveEntryAsync"/>. A file-level problem — not a
    /// workbook, no «ID» column, an unknown or doubled column — is a 400 with
    /// one sentence, because no row of such a file can be trusted.
    /// </summary>
    public static async Task<ExamOutcome<ExamResultImportResultDto>> ImportAsync(
        IAppDbContext db, ExamActor actor, string examId, string fileName, Stream file, bool dryRun,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null) return ExamError.NotFound("exam_not_found", ExamService.ExamNotFoundMessage);
        if (ExamService.EntryStateProblem(exam) is { } stateProblem) return stateProblem;

        var columns = await ExamService.ColumnsAsync(db, examId, ct);

        List<(int Number, string[] Cells)> sheet;
        try
        {
            sheet = ReadSheet(file, 3 + columns.Count + 1 + ExtraColumns);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExamError.BadRequest("unreadable_file", UnreadableMessage);
        }
        if (sheet.Count == 0) return ExamError.BadRequest("empty_file", EmptyFileMessage);

        var (layout, layoutProblem) = MapHeader(sheet[0].Cells, columns);
        if (layoutProblem is not null) return layoutProblem;

        var statuses = await db.ExamParticipants.AsNoTracking().Where(p => p.ExamId == examId)
            .Select(p => new { p.Id, p.Status })
            .ToDictionaryAsync(p => p.Id, p => p.Status, StringComparer.Ordinal, ct);

        var dataRows = sheet.Skip(1).Where(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        var idCounts = dataRows.Select(r => r.Cells[0].Trim()).Where(id => id.Length > 0)
            .GroupBy(id => id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var errorsByReason = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var valid = new List<ExamEntrySaveRow>();
        foreach (var (number, cells) in dataRows)
        {
            var reasons = new HashSet<string>(StringComparer.Ordinal);
            var id = cells[0].Trim();

            if (id.Length == 0 || !statuses.TryGetValue(id, out var status)) reasons.Add(Reasons.UnknownParticipant);
            else if (idCounts[id] > 1) reasons.Add(Reasons.DuplicateParticipant);
            else if (IsLocked(status)) reasons.Add(Reasons.ParticipantLocked);

            var absent = false;
            if (layout.AbsentColumn is { } absentColumn)
            {
                var parsed = ParseAbsent(cells[absentColumn]);
                if (parsed is null) reasons.Add(Reasons.BadAbsent);
                else absent = parsed.Value;
            }

            var scores = new List<ExamEntrySaveScore>();
            foreach (var (column, section) in layout.Sections)
            {
                var text = cells[column].Trim();
                if (text.Length == 0) continue;
                if (!TryParsePoints(text, out var raw)) { reasons.Add(Reasons.NotANumber); continue; }
                var points = ExamScoringService.RoundPoints(raw);
                if (ExamScoringService.PointsProblem(points, section.MaxScore) is not null)
                {
                    reasons.Add(Reasons.OutOfRange);
                    continue;
                }
                scores.Add(new ExamEntrySaveScore(section.SectionId, points));
            }
            if (absent && scores.Count > 0) reasons.Add(Reasons.BadAbsent);

            if (reasons.Count == 0)
            {
                valid.Add(new ExamEntrySaveRow(id, scores, absent));
                continue;
            }
            foreach (var reason in reasons)
            {
                if (!errorsByReason.TryGetValue(reason, out var rows)) errorsByReason[reason] = rows = [];
                rows.Add(number);
            }
        }

        var imported = 0;
        if (!dryRun && valid.Count > 0)
        {
            var written = await ExamService.SaveEntryAsync(db, actor, examId, valid, ct);
            if (written.Error is { } error) return error;
            imported = written.Value!.Saved;
        }

        var errors = ReasonOrder.Where(errorsByReason.ContainsKey)
            .Select(r => new ExamResultImportErrorDto(r, ReasonMessages[r], errorsByReason[r].ToList()))
            .ToList();
        return new ExamResultImportResultDto(
            fileName, dataRows.Count, valid.Count, dataRows.Count - valid.Count, imported, errors);
    }

    // =====================================================================
    //  Header
    // =====================================================================

    private sealed record Layout(List<(int Column, ExamEntryColumnDto Section)> Sections, int? AbsentColumn);

    /// <summary>
    /// Maps header cells to meaning. A subject column is recognised by its
    /// full template header (<c>Matematika (0–50)</c>) or by the bare subject
    /// name, when that name is unique within the exam. The «ID» column must be
    /// first — it is what every row is matched on.
    /// </summary>
    private static (Layout Layout, ExamError? Problem) MapHeader(string[] header, List<ExamEntryColumnDto> columns)
    {
        var none = new Layout([], null);
        if (!string.Equals(header[0].Trim(), IdHeader, StringComparison.OrdinalIgnoreCase))
            return (none, ExamError.BadRequest("bad_template", BadTemplateMessage));

        var byHeader = new Dictionary<string, ExamEntryColumnDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in columns) byHeader[SectionHeader(c.Name, c.MaxScore)] = c;
        foreach (var group in columns.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() == 1) byHeader.TryAdd(group.Key, group.First());
        }

        var sections = new List<(int, ExamEntryColumnDto)>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        int? absentColumn = null;
        for (var j = 1; j < header.Length; j++)
        {
            var text = header[j].Trim();
            if (text.Length == 0) continue;
            if (string.Equals(text, NameHeader, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, ClassHeader, StringComparison.OrdinalIgnoreCase)) continue;

            if (string.Equals(text, AbsentHeader, StringComparison.OrdinalIgnoreCase))
            {
                if (absentColumn is not null)
                    return (none, ExamError.BadRequest("bad_template", $"«{AbsentHeader}» ustuni ikki marta uchradi"));
                absentColumn = j;
                continue;
            }

            if (!byHeader.TryGetValue(text, out var section))
            {
                return (none, ExamError.BadRequest("unknown_column",
                    $"Noma'lum ustun: «{text}». Ustun nomlarini shablondagidek qoldiring — yangi shablonni yuklab oling."));
            }
            if (!mapped.Add(section.SectionId))
                return (none, ExamError.BadRequest("bad_template", $"«{text}» ustuni ikki marta uchradi"));
            sections.Add((j, section));
        }

        return (new Layout(sections, absentColumn), null);
    }

    // =====================================================================
    //  Cells
    // =====================================================================

    /// <summary>
    /// «Kelmadi»: yes / no / not understood (<c>null</c>). Apostrophe variants
    /// (’ ‘ ʻ ʼ `) are one letter in Uzbek Latin and are read as one.
    /// </summary>
    internal static bool? ParseAbsent(string raw)
    {
        var text = raw.Trim().ToLowerInvariant()
            .Replace('’', '\'').Replace('‘', '\'').Replace('ʻ', '\'').Replace('ʼ', '\'').Replace('`', '\'');
        return text switch
        {
            "" or "yo'q" or "yoq" or "-" or "0" or "false" or "no" => false,
            "ha" or "h" or "+" or "1" or "x" or "true" or "yes" or "kelmadi" => true,
            _ => null,
        };
    }

    /// <summary>
    /// A points cell: <c>42</c>, <c>42.5</c>, <c>42,5</c> (a decimal comma is
    /// how an Uzbek-locale Excel types it), or Excel's own numeric text.
    /// </summary>
    internal static bool TryParsePoints(string text, out decimal value) =>
        decimal.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool IsLocked(string status) =>
        status is ExamParticipantStatus.Cancelled or ExamParticipantStatus.InProgress;

    // =====================================================================
    //  Reading the sheet
    // =====================================================================

    /// <summary>
    /// The first worksheet as <c>(sheet row number, cells)</c>.
    ///
    /// <para>
    /// <b>Why not <see cref="ExcelImport.ReadRows"/>:</b> it numbers rows by
    /// their position in the XML, and Excel does not write empty rows at all —
    /// one blank line in the middle of the sheet would shift every row number
    /// after it. §6.2 promises "1-based sheet rows including the header, so
    /// they match what the user sees in Excel", so the number here comes from
    /// the row's own <c>r</c> attribute. Everything else (shared strings,
    /// inline strings, cells placed by their letter) is read the same way.
    /// </para>
    /// </summary>
    private static List<(int Number, string[] Cells)> ReadSheet(Stream stream, int columnCount)
    {
        var result = new List<(int, string[])>();
        using var doc = SpreadsheetDocument.Open(stream, false);
        var workbook = doc.WorkbookPart;
        var sheet = workbook?.Workbook?.Descendants<Sheet>().FirstOrDefault();
        if (workbook is null || sheet?.Id?.Value is not { } partId) return result;

        var worksheet = (WorksheetPart)workbook.GetPartById(partId);
        var shared = workbook.SharedStringTablePart?.SharedStringTable;
        var data = worksheet.Worksheet?.GetFirstChild<SheetData>();
        if (data is null) return result;

        var nextRow = 1;
        foreach (var row in data.Elements<Row>())
        {
            var number = row.RowIndex?.Value is { } index ? (int)index : nextRow;
            nextRow = number + 1;

            var cells = new string[columnCount];
            Array.Fill(cells, "");
            var nextColumn = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                var column = ColumnIndex(cell.CellReference?.Value);
                if (column < 0) column = nextColumn;
                nextColumn = column + 1;
                if (column < columnCount) cells[column] = CellText(cell, shared);
            }
            result.Add((number, cells));
        }
        return result;
    }

    private static string CellText(Cell cell, SharedStringTable? shared)
    {
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            return shared is not null
                && int.TryParse(cell.CellValue?.Text, out var index)
                && index >= 0 && index < shared.ChildElements.Count
                ? shared.ElementAt(index).InnerText
                : "";
        }
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.Text?.Text ?? cell.InnerText;
        return cell.CellValue?.Text ?? "";
    }

    /// <summary>"A" → 0, "B" → 1, "AA" → 26; -1 when the cell carries no reference.</summary>
    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;
        var column = 0;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z') column = column * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') column = column * 26 + (ch - 'a' + 1);
            else break;
        }
        return column - 1;
    }
}
