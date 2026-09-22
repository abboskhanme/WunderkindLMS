using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Seasonal assessment — entry, list and the shared rules (unit B4).
//  Spec: docs/modules/admission-and-testing.md §2.4, §5.12, §6.6, §8.6.
// ===========================================================================
//
//  WHAT LIVES WHERE
//  ----------------
//  This file   — the rules every surface shares (period, score, comment), the
//                scope ("which subjects are taught in which class"), the entry
//                grid, the bulk upsert, inline edit, delete and the list.
//  SeasonalPivotQuery.cs     — "Fanlar kesimida", pupils × subjects.
//  SeasonalCoverageReport.cs — "Hisobot", per-teacher coverage.
//  Both reports read `seasonal_marks`; neither has a table of its own (§10
//  "Reports, not storage").
//
//  THE SCALE IS 0–100 AND NOTHING ELSE
//  -----------------------------------
//  Never the 1–5 journal grade, never converted (§13 Q5). If you find yourself
//  reading `journal_entries.grade` here you have crossed into o'zlashtirish,
//  which is a different feature (§2.4 "Boundary").
//
//  OUT-OF-RANGE SCORES ARE REFUSED, NOT CLAMPED
//  --------------------------------------------
//  §5.12 / §8.6 say "server-clamped" (EduSchool clamps in its input). Our page
//  deliberately refuses instead — "150 is more likely a typo for 50 or 15 than
//  a request for 100" (periods.ts `parseScore`). A server that clamped would
//  turn the same typo from any other client into a real 100. So: rounded
//  half-up to two decimals, then anything outside 0..100 is a 400. Logged in
//  docs/ASSUMPTIONS.md (2026-09-22).
//
//  EDIT HISTORY IS THE AUDIT LOG
//  -----------------------------
//  Every create, update and delete adds one `audit_logs` row
//  (`EntityType = "SeasonalMark"`, `EntityId` = mark id, `StudentId` = pupil)
//  to the SAME `SaveChanges` as the change itself, so a rolled-back save
//  leaves no history behind. The before/after snapshots carry `Score` and
//  `Comment` — the history dialog (MarkHistoryModal.tsx) diffs exactly those.
//
//  WHO MAY WRITE WHAT
//  ------------------
//  Nothing in this class checks the caller. The admin controller is behind
//  `[AdminPerm("seasonalMarks")]` and has no teaching restriction (§8.6); the
//  teacher controller checks `TeacherOwnerAccess.TeachesAsync` BEFORE calling
//  in. What this class does check, for both, is that every pupil in a bulk
//  save really is an active pupil of the class named in the request — without
//  it a teacher could write a mark for any pupil in the school by pairing a
//  class they teach with somebody else's student id.
// ===========================================================================

/// <summary>What went wrong, so the controller can pick the status.</summary>
public enum SeasonalMarkError
{
    /// <summary>The request breaks a rule → 400.</summary>
    Invalid,

    /// <summary>The class, subject or mark does not exist → 404.</summary>
    NotFound,

    /// <summary>Someone else changed the same mark in the meantime → 409.</summary>
    Conflict,
}

/// <summary>
/// A refused seasonal-mark operation. <see cref="Exception.Message"/> is the
/// Uzbek sentence the client shows as-is (§6 "Errors"). Same shape as
/// <c>PaymentException</c>: the service throws, the controller maps.
/// </summary>
public sealed class SeasonalMarkException(SeasonalMarkError error, string message) : Exception(message)
{
    public SeasonalMarkError Error { get; } = error;

    public static SeasonalMarkException Invalid(string message) => new(SeasonalMarkError.Invalid, message);

    public static SeasonalMarkException NotFound(string message) => new(SeasonalMarkError.NotFound, message);

    public static SeasonalMarkException Conflict(string message) => new(SeasonalMarkError.Conflict, message);
}

/// <summary>
/// One validated period — <c>(kind, year, month | quarter)</c> — and the two
/// strings derived from it: the unique key and the label.
///
/// <para>
/// <b>A quarter's year</b> is stored exactly as the client sends it; the
/// page defaults it to the calendar year. So in the 2026/2027 school year
/// Q1–Q2 are <c>2026</c> and Q3–Q4 are <c>2027</c> — the calendar year the
/// quarter runs in. The server never converts between that and an academic
/// year (docs/ASSUMPTIONS.md, 2026-09-22).
/// </para>
/// </summary>
public sealed record SeasonalPeriod(string Kind, int Year, int? Month, int? Quarter)
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    public const string KindMessage =
        "Baholash turi noto'g'ri — oylik (monthly), choraklik (quarterly) yoki yillik (yearly) bo'lishi kerak";
    public const string YearMessage = "Yil 2000 dan 2100 gacha bo'lishi kerak";
    public const string MonthMessage = "Oylik baho uchun oyni (1–12) tanlang";
    public const string QuarterMessage = "Choraklik baho uchun chorakni (1–4) tanlang";
    public const string MonthOnlyMonthlyMessage = "Oy faqat oylik baho uchun beriladi";
    public const string QuarterOnlyQuarterlyMessage = "Chorak faqat choraklik baho uchun beriladi";

    /// <summary>The generated <c>period_key</c> this period is stored under (§5.12).</summary>
    public string Key => SeasonalMark.BuildPeriodKey(Kind, Year, Month, Quarter);

    /// <summary>"Mart 2026" | "2-chorak 2026" | "2026".</summary>
    public string Label => SeasonalLabels.Period(Kind, Year, Month, Quarter);

    /// <summary>
    /// Validates a complete period. A <c>month</c> with a non-monthly kind (or a
    /// <c>quarter</c> with a non-quarterly one) is refused rather than dropped:
    /// the client never sends one (seasonalMarks.ts <c>periodParams</c>), so
    /// receiving one means the caller is confused about which period it asked for.
    /// </summary>
    /// <exception cref="SeasonalMarkException">400 with the reason.</exception>
    public static SeasonalPeriod From(SeasonalPeriodQuery q)
    {
        ArgumentNullException.ThrowIfNull(q);

        var kind = SeasonalMarkService.Clean(q.PeriodKind);
        if (kind is null || !SeasonalPeriodKind.All.Contains(kind))
            throw SeasonalMarkException.Invalid(KindMessage);
        var year = ValidYear(q.Year) ?? throw SeasonalMarkException.Invalid(YearMessage);

        if (kind != SeasonalPeriodKind.Monthly && q.Month is not null)
            throw SeasonalMarkException.Invalid(MonthOnlyMonthlyMessage);
        if (kind != SeasonalPeriodKind.Quarterly && q.Quarter is not null)
            throw SeasonalMarkException.Invalid(QuarterOnlyQuarterlyMessage);

        return kind switch
        {
            SeasonalPeriodKind.Monthly when q.Month is >= 1 and <= 12 => new(kind, year, q.Month, null),
            SeasonalPeriodKind.Monthly => throw SeasonalMarkException.Invalid(MonthMessage),
            SeasonalPeriodKind.Quarterly when q.Quarter is >= 1 and <= 4 => new(kind, year, null, q.Quarter),
            SeasonalPeriodKind.Quarterly => throw SeasonalMarkException.Invalid(QuarterMessage),
            _ => new(kind, year, null, null),
        };
    }

    /// <summary>The year when it is inside the table's CHECK range, otherwise <c>null</c>.</summary>
    public static int? ValidYear(int? year) => year is >= MinYear and <= MaxYear ? year : null;
}

/// <summary>
/// Uzbek labels shared by the list, the exports and the pivot, so they cannot
/// spell a period two ways (§6.6 "periodLabel is formatted on the server").
/// </summary>
public static class SeasonalLabels
{
    /// <summary>Same spelling as the page's <c>MONTH_NAMES</c> (periods.ts).</summary>
    private static readonly string[] MonthNames =
    [
        "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
        "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr",
    ];

    /// <summary>"Oylik" | "Choraklik" | "Yillik" — the page's <c>periodKindLabels</c>.</summary>
    public static string Kind(string kind) => kind switch
    {
        SeasonalPeriodKind.Monthly => "Oylik",
        SeasonalPeriodKind.Quarterly => "Choraklik",
        SeasonalPeriodKind.Yearly => "Yillik",
        _ => kind,
    };

    /// <summary>
    /// "Mart 2026" | "2-chorak 2026" | "2026". §6.6 writes the quarter as
    /// "2 - chorak 2026" (EduSchool's spelling); ours is "2-chorak", the form the
    /// rest of this product and the page's own period caption already use.
    /// </summary>
    public static string Period(string kind, int year, int? month, int? quarter) => kind switch
    {
        SeasonalPeriodKind.Monthly when month is >= 1 and <= 12 =>
            string.Create(CultureInfo.InvariantCulture, $"{MonthNames[month.Value - 1]} {year}"),
        SeasonalPeriodKind.Quarterly when quarter is not null =>
            string.Create(CultureInfo.InvariantCulture, $"{quarter}-chorak {year}"),
        _ => year.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>The list export: the capped rows plus the real total, so the caller can tell the cap bit.</summary>
public sealed record SeasonalMarkExport(int Total, IReadOnlyList<SeasonalMarkRowDto> Rows);

/// <summary>
/// One lesson line of the current schedule, with its names: who teaches which
/// subject in which (live) class, and to which sub-group.
/// </summary>
internal sealed record TaughtLesson(
    string TeacherId, string TeacherFullName,
    string ClassId, string ClassName, int ClassGrade,
    string SubjectId, string SubjectName, int SubGroup);

/// <summary>
/// Seasonal marks: scope, entry grid, bulk upsert, inline edit, delete and the
/// list. Built per request by the controllers (no DI entry — <c>Program.cs</c>
/// is not this unit's file); the audit writer comes from DI through them.
/// </summary>
public sealed class SeasonalMarkService(IAppDbContext db, AuditService audit)
{
    /// <summary>
    /// <c>audit_logs.entity_type</c> of every row this module writes. The history
    /// dialog queries exactly this string, so it must never change. It belongs
    /// next to the other <c>Entity*</c> constants in <see cref="AuditService"/>;
    /// that file is outside this unit, so it lives here until it moves.
    /// </summary>
    public const string AuditEntity = "SeasonalMark";

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>
    /// Ceiling on one list export — the same bound and the same reasoning as
    /// <see cref="SurveySubmissionQuery.MaxExportRows"/>. A year of marks for a
    /// whole school is ~65 000 rows (§6); a period or a class is far below this.
    /// The file says so on its last line when the cap bites.
    /// </summary>
    public const int MaxExportRows = 10_000;

    /// <summary>More pupils than any class has; a bound on one request's work.</summary>
    public const int MaxBulkRows = 500;

    public const decimal ScoreMin = 0m;
    public const decimal ScoreMax = 100m;
    public const int CommentMinLength = 3;

    /// <summary>
    /// Not in the spec: `comment` is an unbounded `text` column and the editor is a
    /// three-row textarea. 2000 characters is a page of text — a bound against a
    /// pasted document, not a limit anyone writing a comment will meet.
    /// </summary>
    public const int CommentMaxLength = 2000;

    public const string ClassAndSubjectRequiredMessage = "Sinf va fanni tanlang";
    public const string ClassNotFoundMessage = "Sinf topilmadi";
    public const string SubjectNotFoundMessage = "Fan topilmadi";
    public const string MarkNotFoundMessage = "Baho topilmadi — u o'chirilgan bo'lishi mumkin. Ro'yxatni yangilang.";
    public const string ScoreRangeMessage = "Ball 0 dan 100 gacha bo'lishi kerak";
    public const string CommentTooShortMessage = "Izoh kamida 3 ta belgidan iborat bo'lsin";
    public const string EmptyUpdateMessage =
        "Ball ham, izoh ham bo'sh — baho qolmaydi. Bahoni olib tashlash uchun o'chirish amalidan foydalaning.";
    public const string RowsRequiredMessage = "Saqlanadigan qatorlar yuborilmadi";
    public const string StudentRequiredMessage = "Qatorlardan birida o'quvchi ko'rsatilmagan";
    public const string ConcurrentMessage =
        "Bu baho shu orada boshqa foydalanuvchi tomonidan o'zgartirildi. Ro'yxatni yangilab, qayta urinib ko'ring.";

    public static string CommentTooLongMessage =>
        $"Izoh {CommentMaxLength} belgidan oshmasin";

    public static string TooManyRowsMessage =>
        $"Bir so'rovda ko'pi bilan {MaxBulkRows} ta o'quvchi saqlanadi";

    /// <summary>
    /// §8.6: "rejects with 'Bunday chorak sozlanmagan'". The page recognises the
    /// sentence by "chorak sozlanmagan" and adds its own hint, so keep those words.
    /// </summary>
    public static string QuarterNotConfiguredMessage(int quarter) =>
        $"Bunday chorak sozlanmagan: {quarter}-chorak sanalari Sozlamalar → Choraklar bo'limida kiritilmagan";

    private static string NotInClassMessage(string className) =>
        $"Tanlangan o'quvchilardan biri hozir {className} sinfida emas (yoki arxivlangan). Ro'yxatni yangilang.";

    private static string DuplicateRowMessage(string fullName) =>
        $"{fullName} ro'yxatda ikki marta keldi";

    // =====================================================================
    //  Scope — "which subjects are taught in which class" (§6.6)
    // =====================================================================

    /// <summary>
    /// Classes and the (class, subject, teacher) triples of the current schedule.
    ///
    /// <para>
    /// <b>Admin</b> (<paramref name="teacherId"/> null): every live class — the
    /// list screen filters by class too, and a class whose timetable is not built
    /// yet still has marks to find — and every triple.
    /// <b>Teacher</b>: only the classes and triples the teacher teaches; the
    /// same schedule rows <see cref="TeacherOwnerAccess.TeachesAsync"/> reads, so
    /// the dropdown never offers a pair the write would refuse.
    /// </para>
    /// </summary>
    public async Task<SeasonalScopeDto> ScopeAsync(
        string? classId, string? teacherId, CancellationToken ct = default)
    {
        classId = Clean(classId);
        var lessons = await TaughtLessonsAsync(db, teacherId is null ? null : [teacherId], ct);
        if (classId is not null) lessons = [.. lessons.Where(l => l.ClassId == classId)];

        var pairs = lessons
            .OrderBy(l => l.ClassGrade)
            .ThenBy(l => l.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.TeacherFullName, StringComparer.OrdinalIgnoreCase)
            .Select(l => new SeasonalScopePairDto(l.ClassId, l.SubjectId, l.SubjectName, l.TeacherId, l.TeacherFullName))
            .Distinct()
            .ToList();

        List<SeasonalScopeClassDto> classes;
        if (teacherId is null)
        {
            var q = db.Classes.AsNoTracking().Where(c => !c.IsArchived);
            if (classId is not null) q = q.Where(c => c.Id == classId);
            classes = await q
                .OrderBy(c => c.Grade).ThenBy(c => c.Name)
                .Select(c => new SeasonalScopeClassDto(c.Id, c.Name, c.Grade))
                .ToListAsync(ct);
        }
        else
        {
            classes = [.. lessons
                .Select(l => new SeasonalScopeClassDto(l.ClassId, l.ClassName, l.ClassGrade))
                .Distinct()
                .OrderBy(c => c.Grade).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
        }

        return new SeasonalScopeDto(classes, pairs);
    }

    // =====================================================================
    //  Entry — the grid and the bulk save
    // =====================================================================

    /// <summary>
    /// Every active pupil of the class with the stored mark, if any, prefilled.
    /// The mark is found by (pupil, subject, period) — not by class — so a pupil
    /// who changed class still sees the mark entered in the old one (§8.6: the
    /// class on the mark is a snapshot, the mark belongs to the pupil).
    /// </summary>
    public async Task<IReadOnlyList<SeasonalEntryRowDto>> StudentsAsync(
        SeasonalStudentsQuery q, CancellationToken ct = default)
    {
        var (cls, subject, period) = await EntryContextAsync(q, ct);
        var pupils = await RosterAsync(cls, ct);

        var ids = pupils.Select(p => p.Id).ToList();
        var key = period.Key;
        var marks = await db.SeasonalMarks.AsNoTracking()
            .Where(m => m.SubjectId == subject.Id && m.PeriodKey == key && ids.Contains(m.StudentId))
            .ToDictionaryAsync(m => m.StudentId, StringComparer.Ordinal, ct);

        return [.. pupils.Select(p => marks.TryGetValue(p.Id, out var m)
            ? new SeasonalEntryRowDto(p.Id, p.FullName, m.Score, m.Comment, m.Id)
            : new SeasonalEntryRowDto(p.Id, p.FullName, null, null, null))];
    }

    /// <summary>
    /// §6.6 <c>POST /bulk</c>, exactly: per row, upsert on (pupil, subject,
    /// period); a row with neither score nor comment deletes the stored mark.
    /// A row identical to what is stored is left alone and counted nowhere.
    ///
    /// <para>
    /// <b>All or nothing.</b> Every row is validated before anything is written,
    /// and the marks and their audit rows go in ONE <c>SaveChanges</c> — one
    /// transaction. A bad comment on row 27 leaves rows 1–26 unsaved, and the
    /// message names the pupil.
    /// </para>
    /// </summary>
    /// <param name="actorUserId">Stored as <c>created_by_user_id</c> on new marks.</param>
    public async Task<SeasonalBulkResultDto> BulkAsync(
        SeasonalBulkRequest req, string? actorUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var (cls, subject, period) = await EntryContextAsync(req, ct);

        var rows = req.Rows ?? throw SeasonalMarkException.Invalid(RowsRequiredMessage);
        if (rows.Count > MaxBulkRows) throw SeasonalMarkException.Invalid(TooManyRowsMessage);
        if (rows.Count == 0) return new SeasonalBulkResultDto(0, 0, 0);

        var roster = (await RosterAsync(cls, ct)).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var wanted = new List<(Student Pupil, decimal? Score, string? Comment)>(rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var studentId = Clean(row?.StudentId) ?? throw SeasonalMarkException.Invalid(StudentRequiredMessage);
            if (!roster.TryGetValue(studentId, out var pupil))
                throw SeasonalMarkException.Invalid(NotInClassMessage(cls.Name));
            if (!seen.Add(studentId)) throw SeasonalMarkException.Invalid(DuplicateRowMessage(pupil.FullName));

            try
            {
                wanted.Add((pupil, NormalizeScore(row!.Score), NormalizeComment(row.Comment)));
            }
            catch (SeasonalMarkException ex)
            {
                throw SeasonalMarkException.Invalid($"{pupil.FullName}: {ex.Message}");
            }
        }

        var key = period.Key;
        var ids = wanted.Select(w => w.Pupil.Id).ToList();
        var stored = await db.SeasonalMarks
            .Where(m => m.SubjectId == subject.Id && m.PeriodKey == key && ids.Contains(m.StudentId))
            .ToDictionaryAsync(m => m.StudentId, StringComparer.Ordinal, ct);

        int created = 0, updated = 0, deleted = 0;
        var now = AppClock.Now;
        foreach (var (pupil, score, comment) in wanted)
        {
            stored.TryGetValue(pupil.Id, out var mark);

            if (score is null && comment is null)
            {
                if (mark is null) continue;
                Audit("delete", mark, pupil.FullName, subject.Name, before: Snapshot(mark), after: null);
                db.SeasonalMarks.Remove(mark);
                deleted++;
            }
            else if (mark is null)
            {
                mark = new SeasonalMark
                {
                    StudentId = pupil.Id,
                    // The snapshot (§8.6): the class the pupil is in now, which the
                    // roster check above has just confirmed.
                    ClassId = cls.Id,
                    SubjectId = subject.Id,
                    PeriodKind = period.Kind,
                    Year = period.Year,
                    Month = period.Month,
                    Quarter = period.Quarter,
                    Score = score,
                    Comment = comment,
                    CreatedByUserId = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.SeasonalMarks.Add(mark);
                Audit("create", mark, pupil.FullName, subject.Name, before: null, after: Snapshot(mark));
                created++;
            }
            else if (mark.Score != score || mark.Comment != comment)
            {
                // `class_id` is NOT rewritten: it records where the mark was first
                // entered, and a later edit from another class does not move it.
                var before = Snapshot(mark);
                mark.Score = score;
                mark.Comment = comment;
                mark.UpdatedAt = now;
                Audit("update", mark, pupil.FullName, subject.Name, before, Snapshot(mark));
                updated++;
            }
        }

        if (created + updated + deleted > 0)
            await SaveAsync(onVanished: SeasonalMarkError.Conflict, ct);
        return new SeasonalBulkResultDto(created, updated, deleted);
    }

    // =====================================================================
    //  One mark — inline edit and delete (list screen)
    // =====================================================================

    /// <summary>
    /// Replaces score and comment with the request's values (both are the full
    /// new state) and returns the row as now stored. Both empty is refused:
    /// an empty row is not a mark, and deleting is <see cref="DeleteAsync"/>.
    /// </summary>
    public async Task<SeasonalMarkRowDto> UpdateAsync(
        string id, SeasonalMarkUpdateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var mark = await db.SeasonalMarks.FirstOrDefaultAsync(m => m.Id == id, ct)
                   ?? throw SeasonalMarkException.NotFound(MarkNotFoundMessage);

        var score = NormalizeScore(req.Score);
        var comment = NormalizeComment(req.Comment);
        if (score is null && comment is null) throw SeasonalMarkException.Invalid(EmptyUpdateMessage);

        if (mark.Score != score || mark.Comment != comment)
        {
            var (pupil, subject) = await NamesAsync(mark, ct);
            var before = Snapshot(mark);
            mark.Score = score;
            mark.Comment = comment;
            mark.UpdatedAt = AppClock.Now;
            Audit("update", mark, pupil, subject, before, Snapshot(mark));
            await SaveAsync(onVanished: SeasonalMarkError.NotFound, ct);
        }

        var rows = await RowsAsync(db.SeasonalMarks.AsNoTracking().Where(m => m.Id == id), ct);
        return rows.Count == 1 ? rows[0] : throw SeasonalMarkException.NotFound(MarkNotFoundMessage);
    }

    /// <summary>Deletes one mark and records it (the snapshot keeps what was lost).</summary>
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var mark = await db.SeasonalMarks.FirstOrDefaultAsync(m => m.Id == id, ct)
                   ?? throw SeasonalMarkException.NotFound(MarkNotFoundMessage);

        var (pupil, subject) = await NamesAsync(mark, ct);
        Audit("delete", mark, pupil, subject, before: Snapshot(mark), after: null);
        db.SeasonalMarks.Remove(mark);
        await SaveAsync(onVanished: SeasonalMarkError.NotFound, ct);
    }

    // =====================================================================
    //  The list (§3.3 screen 10) and its export
    // =====================================================================

    /// <summary>One page of stored marks, newest change first.</summary>
    public async Task<SeasonalPageDto<SeasonalMarkRowDto>> PageAsync(
        SeasonalMarkFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (page, limit) = Paging(filter.Page, filter.Limit);

        var matching = Matching(filter);
        var total = await matching.CountAsync(ct);
        var rows = await RowsAsync(Newest(matching).Skip((page - 1) * limit).Take(limit), ct);
        return new SeasonalPageDto<SeasonalMarkRowDto>(rows, total, page, limit);
    }

    /// <summary>The whole filter, capped at <see cref="MaxExportRows"/>; <c>Total</c> stays the real count.</summary>
    public async Task<SeasonalMarkExport> ExportAsync(SeasonalMarkFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var matching = Matching(filter);
        var total = await matching.CountAsync(ct);
        var rows = await RowsAsync(Newest(matching).Take(MaxExportRows), ct);
        return new SeasonalMarkExport(total, rows);
    }

    /// <summary>
    /// Every list filter, on `seasonal_marks` alone (the search is an EXISTS on
    /// the pupil), so the count and the page describe the same set. An invalid
    /// value is refused, not ignored: <c>month=13</c> silently dropped would
    /// return every month and read as "these are March's marks".
    /// </summary>
    private IQueryable<SeasonalMark> Matching(SeasonalMarkFilter f)
    {
        var q = db.SeasonalMarks.AsNoTracking();

        var kind = Clean(f.PeriodKind);
        if (kind is not null)
        {
            if (!SeasonalPeriodKind.All.Contains(kind)) throw SeasonalMarkException.Invalid(SeasonalPeriod.KindMessage);
            q = q.Where(m => m.PeriodKind == kind);
        }

        if (f.Year is not null)
        {
            var year = SeasonalPeriod.ValidYear(f.Year) ?? throw SeasonalMarkException.Invalid(SeasonalPeriod.YearMessage);
            q = q.Where(m => m.Year == year);
        }

        if (f.Month is { } month)
        {
            if (kind != SeasonalPeriodKind.Monthly)
                throw SeasonalMarkException.Invalid(SeasonalPeriod.MonthOnlyMonthlyMessage);
            if (month is < 1 or > 12) throw SeasonalMarkException.Invalid(SeasonalPeriod.MonthMessage);
            q = q.Where(m => m.Month == month);
        }

        if (f.Quarter is { } quarter)
        {
            if (kind != SeasonalPeriodKind.Quarterly)
                throw SeasonalMarkException.Invalid(SeasonalPeriod.QuarterOnlyQuarterlyMessage);
            if (quarter is < 1 or > 4) throw SeasonalMarkException.Invalid(SeasonalPeriod.QuarterMessage);
            q = q.Where(m => m.Quarter == quarter);
        }

        if (Clean(f.ClassId) is { } classId) q = q.Where(m => m.ClassId == classId);
        if (Clean(f.SubjectId) is { } subjectId) q = q.Where(m => m.SubjectId == subjectId);
        if (Clean(f.StudentId) is { } studentId) q = q.Where(m => m.StudentId == studentId);

        if (Clean(f.Search)?.ToLowerInvariant() is { } term)
            q = q.Where(m => db.Students.Any(s => s.Id == m.StudentId && s.FullName.ToLower().Contains(term)));

        return q;
    }

    /// <summary>Newest change first; the id breaks ties so paging is stable (a bulk save stamps 30 rows at once).</summary>
    private static IQueryable<SeasonalMark> Newest(IQueryable<SeasonalMark> q) =>
        q.OrderByDescending(m => m.UpdatedAt).ThenByDescending(m => m.Id);

    /// <summary>
    /// A filtered, ordered, paged window of marks → list rows, with pupil, class,
    /// subject and author names in the SAME query — one round trip for the page,
    /// never one per row. The inner joins are safe: all three foreign keys are
    /// NOT NULL (cascade / restrict, §5.12). The author is a left join
    /// (<c>on delete set null</c>). The order is repeated because the paged
    /// window is read as a subquery and a join does not keep its order.
    /// </summary>
    private async Task<List<SeasonalMarkRowDto>> RowsAsync(IQueryable<SeasonalMark> window, CancellationToken ct)
    {
        var rows = await (
            from m in window
            join s in db.Students.AsNoTracking() on m.StudentId equals s.Id
            join c in db.Classes.AsNoTracking() on m.ClassId equals c.Id
            join sub in db.Subjects.AsNoTracking() on m.SubjectId equals sub.Id
            join u in db.Users.AsNoTracking() on m.CreatedByUserId equals u.Id into authors
            from u in authors.DefaultIfEmpty()
            orderby m.UpdatedAt descending, m.Id descending
            select new
            {
                Mark = m,
                StudentName = s.FullName,
                ClassName = c.Name,
                SubjectName = sub.Name,
                AuthorName = u == null ? null : u.FullName,
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => new SeasonalMarkRowDto(
            r.Mark.Id,
            new SeasonalStudentRefDto(r.Mark.StudentId, r.StudentName),
            new SeasonalRefDto(r.Mark.ClassId, r.ClassName),
            new SeasonalRefDto(r.Mark.SubjectId, r.SubjectName),
            r.Mark.PeriodKind,
            r.Mark.Year,
            r.Mark.Month,
            r.Mark.Quarter,
            SeasonalLabels.Period(r.Mark.PeriodKind, r.Mark.Year, r.Mark.Month, r.Mark.Quarter),
            r.Mark.Score,
            r.Mark.Comment,
            Stamp(r.Mark.UpdatedAt),
            r.AuthorName))];
    }

    // =====================================================================
    //  Shared rules — used by the two report classes too
    // =====================================================================

    /// <summary>
    /// 0–100, two decimals, half-up (§8.6). <c>null</c> stays <c>null</c>.
    /// Out of range after rounding is refused — see the file header for why not
    /// clamped.
    /// </summary>
    public static decimal? NormalizeScore(decimal? raw)
    {
        if (raw is not { } value) return null;
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded is < ScoreMin or > ScoreMax
            ? throw SeasonalMarkException.Invalid(ScoreRangeMessage)
            : rounded;
    }

    /// <summary>
    /// Trimmed comment, or <c>null</c> when blank. 1–2 characters are refused
    /// (§8.6). Length is counted in Unicode code points — what PostgreSQL's
    /// <c>char_length</c> in <c>ck_seasonal_marks_comment</c> counts — so "😀a"
    /// is refused here instead of passing a UTF-16 count of 3 and failing the
    /// CHECK as a 500.
    /// </summary>
    public static string? NormalizeComment(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        var length = text.EnumerateRunes().Count();
        if (length < CommentMinLength) throw SeasonalMarkException.Invalid(CommentTooShortMessage);
        if (length > CommentMaxLength) throw SeasonalMarkException.Invalid(CommentTooLongMessage);
        return text;
    }

    /// <summary>"A real filter, or nothing".</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>§6 envelope: page ≥ 1, limit 1..200, default 50.</summary>
    internal static (int Page, int Limit) Paging(int? page, int? limit) =>
        (Math.Max(1, page ?? 1), Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize));

    /// <summary>"yyyy-MM-ddTHH:mm:ss", no offset — the §6 timestamp format.</summary>
    internal static string Stamp(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// The current schedule's lesson lines for LIVE classes, with names.
    ///
    /// <para>
    /// Class-owned templates only: a seasonal mark's <c>class_id</c> is a foreign
    /// key to <c>classes</c>, and a study-group template stores a GROUP id in the
    /// same column. A subject taught only through a study group therefore has no
    /// scope here — marks attach to classes (§5.12).
    /// </para>
    /// <para>
    /// A lesson without a teacher, or naming a teacher or subject that no longer
    /// exists, is dropped by the inner joins: a pair needs a teacher to show and
    /// to be accountable in the coverage report. The schedule has no academic
    /// year column; the templates in the table ARE the current year's.
    /// </para>
    /// </summary>
    /// <param name="teacherIds">Restrict to these teachers; <c>null</c> = everyone.</param>
    internal static async Task<List<TaughtLesson>> TaughtLessonsAsync(
        IAppDbContext db, IReadOnlyCollection<string>? teacherIds, CancellationToken ct)
    {
        var lessons = db.ScheduleTemplates.AsNoTracking()
            .Where(t => t.OwnerKind == LessonOwnerKind.Class)
            .SelectMany(t => t.Lessons, (t, l) => new { t.ClassId, l.TeacherId, l.SubjectId, l.SubGroup });
        if (teacherIds is not null)
        {
            var ids = teacherIds.ToList();
            lessons = lessons.Where(l => ids.Contains(l.TeacherId));
        }

        var rows = await (
            from l in lessons
            join c in db.Classes.AsNoTracking() on l.ClassId equals c.Id
            where !c.IsArchived
            join te in db.Teachers.AsNoTracking() on l.TeacherId equals te.Id
            join s in db.Subjects.AsNoTracking() on l.SubjectId equals s.Id
            select new
            {
                l.TeacherId,
                TeacherName = te.FullName,
                l.ClassId,
                ClassName = c.Name,
                c.Grade,
                l.SubjectId,
                SubjectName = s.Name,
                l.SubGroup,
            })
            .Distinct()
            .ToListAsync(ct);

        return [.. rows.Select(r => new TaughtLesson(
            r.TeacherId, r.TeacherName, r.ClassId, r.ClassName, r.Grade, r.SubjectId, r.SubjectName, r.SubGroup))];
    }

    // =====================================================================
    //  Private helpers
    // =====================================================================

    /// <summary>
    /// Validates the (class, subject, period) of the entry grid and of a bulk
    /// save. Cheap checks first; the quarter check last because it is the only
    /// one that depends on settings (§8.6 — a soft check, not a foreign key;
    /// <c>GradesOpen</c> is deliberately NOT consulted).
    /// </summary>
    private async Task<(SchoolClass Class, Subject Subject, SeasonalPeriod Period)> EntryContextAsync(
        SeasonalStudentsQuery q, CancellationToken ct)
    {
        var classId = Clean(q.ClassId);
        var subjectId = Clean(q.SubjectId);
        if (classId is null || subjectId is null)
            throw SeasonalMarkException.Invalid(ClassAndSubjectRequiredMessage);
        var period = SeasonalPeriod.From(q);

        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct)
                  ?? throw SeasonalMarkException.NotFound(ClassNotFoundMessage);
        var subject = await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subjectId, ct)
                      ?? throw SeasonalMarkException.NotFound(SubjectNotFoundMessage);

        if (period.Quarter is { } quarter && !await db.Quarters.AsNoTracking().AnyAsync(x => x.Quarter == quarter, ct))
            throw SeasonalMarkException.Invalid(QuarterNotConfiguredMessage(quarter));

        return (cls, subject, period);
    }

    /// <summary>
    /// Active pupils of the class, by name — through <see cref="LessonRoster"/>,
    /// the one place that answers "who is in this class".
    /// </summary>
    private Task<List<Student>> RosterAsync(SchoolClass cls, CancellationToken ct) =>
        LessonRoster.ForLessonAsync(db, new LessonOwner(LessonOwnerKind.Class, cls.Id, cls.Name), ct: ct);

    /// <summary>Pupil and subject names for an audit summary (two scalar lookups).</summary>
    private async Task<(string Pupil, string Subject)> NamesAsync(SeasonalMark mark, CancellationToken ct)
    {
        var pupil = await db.Students.AsNoTracking()
            .Where(s => s.Id == mark.StudentId).Select(s => s.FullName).FirstOrDefaultAsync(ct);
        var subject = await db.Subjects.AsNoTracking()
            .Where(s => s.Id == mark.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        return (pupil ?? mark.StudentId, subject ?? mark.SubjectId);
    }

    /// <summary>
    /// Adds the audit row to the current unit of work (saved with the change).
    /// The summary is the line the audit screen shows; the action badge already
    /// says created / changed / deleted.
    /// </summary>
    private void Audit(string action, SeasonalMark mark, string pupil, string subject, object? before, object? after) =>
        audit.Record(
            AuditEntity, mark.Id, action,
            $"Mavsumiy baho: {pupil} · {subject} · "
                + SeasonalLabels.Period(mark.PeriodKind, mark.Year, mark.Month, mark.Quarter),
            before, after,
            studentId: mark.StudentId);

    /// <summary>
    /// The before/after JSON. <c>Score</c> and <c>Comment</c> are what the history
    /// dialog diffs; the rest identifies the mark if the row is ever read without it.
    /// </summary>
    private static object Snapshot(SeasonalMark m) => new
    {
        m.Id,
        m.StudentId,
        m.ClassId,
        m.SubjectId,
        m.PeriodKind,
        m.Year,
        m.Month,
        m.Quarter,
        m.Score,
        m.Comment,
    };

    /// <summary>
    /// Saves, turning the two races into readable answers:
    /// a unique violation (23505) — someone created the same mark between our
    /// read and our insert — is a 409; a row that vanished under an update or
    /// delete is <paramref name="onVanished"/> (404 for one mark, 409 for a grid).
    /// </summary>
    private async Task SaveAsync(SeasonalMarkError onVanished, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw onVanished == SeasonalMarkError.NotFound
                ? SeasonalMarkException.NotFound(MarkNotFoundMessage)
                : SeasonalMarkException.Conflict(ConcurrentMessage);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            throw SeasonalMarkException.Conflict(ConcurrentMessage);
        }
    }
}
