namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Seasonal assessment — "Mavsumiy baholash" (unit B4).
//  Spec: docs/modules/admission-and-testing.md §6.6 (contract), §5.12 (table),
//  §8.6 (rules). Client: schoollms.client/src/api/services/seasonalMarks.ts.
// ===========================================================================
//
//  WHY EVERY NAME CARRIES THE `Seasonal` PREFIX
//  --------------------------------------------
//  The question-bank and exam units are being built beside this one and will
//  have their own pages, scopes and rows. A bare `PagedDto<T>` or `ScopeDto`
//  here would collide with theirs the day both land. The JSON the client reads
//  is unaffected: property names are what `seasonalMarks.ts` expects.
//
//  REQUEST TYPES ARE CLASSES WITH NULLABLE PROPERTIES
//  --------------------------------------------------
//  `[ApiController]` turns a missing non-nullable member into an English
//  ProblemDetails 400 before the action runs. Every field is therefore
//  nullable and checked by `SeasonalMarkService`, which answers in Uzbek with
//  `{ message }` (§6 "Errors").
//
//  SCALE
//  -----
//  `score` is 0–100 with two decimals (§5.12, §13 Q5) — never the 1–5 journal
//  scale, and never converted to or from it.

// ---------------------------------------------------------------------------
//  Requests
// ---------------------------------------------------------------------------

/// <summary>
/// A period as §5.12 keys it: <c>(periodKind, year, month | quarter)</c>.
/// <c>month</c> belongs to <c>monthly</c> only, <c>quarter</c> to
/// <c>quarterly</c> only; <see cref="Services.SeasonalPeriod.From"/> refuses
/// any other combination.
/// </summary>
public class SeasonalPeriodQuery
{
    /// <summary><c>monthly</c> | <c>quarterly</c> | <c>yearly</c>.</summary>
    public string? PeriodKind { get; set; }

    /// <summary>
    /// Stored exactly as sent, 2000–2100. For a quarter this is the calendar
    /// year the quarter runs in (see docs/ASSUMPTIONS.md, 2026-09-22).
    /// </summary>
    public int? Year { get; set; }

    /// <summary>1–12, <c>monthly</c> only.</summary>
    public int? Month { get; set; }

    /// <summary>1–4, <c>quarterly</c> only. A number, never a <c>quarters.id</c> (§5.12).</summary>
    public int? Quarter { get; set; }
}

/// <summary><c>GET /students</c> — the entry grid of one (class, subject, period).</summary>
public class SeasonalStudentsQuery : SeasonalPeriodQuery
{
    public string? ClassId { get; set; }

    public string? SubjectId { get; set; }
}

/// <summary>
/// <c>POST /bulk</c> (§6.6). Each row is an upsert on
/// <c>(studentId, subjectId, period_key)</c>; a row whose score AND comment are
/// both empty deletes the stored mark.
/// </summary>
public sealed class SeasonalBulkRequest : SeasonalStudentsQuery
{
    public List<SeasonalBulkRow>? Rows { get; set; }
}

/// <summary>One pupil of a <see cref="SeasonalBulkRequest"/>.</summary>
public sealed class SeasonalBulkRow
{
    public string? StudentId { get; set; }

    /// <summary>0–100; rounded half-up to two decimals. <c>null</c> = no score.</summary>
    public decimal? Score { get; set; }

    /// <summary>Trimmed; empty = no comment; 1–2 characters are refused (§8.6).</summary>
    public string? Comment { get; set; }
}

/// <summary>
/// <c>PUT /{id}</c>. Read as the FULL new state of the mark: a <c>null</c>
/// field clears that field. The page always sends both (seasonalMarks.ts
/// <c>SeasonalMarkUpdate</c>). Both empty is refused — deleting is its own
/// endpoint.
/// </summary>
public sealed class SeasonalMarkUpdateRequest
{
    public decimal? Score { get; set; }

    public string? Comment { get; set; }
}

/// <summary>The list and its export (§6.6). Everything optional.</summary>
public sealed class SeasonalMarkFilter
{
    /// <summary>Free text over the pupil's full name.</summary>
    public string? Search { get; set; }

    public string? PeriodKind { get; set; }

    public int? Year { get; set; }

    /// <summary>Only together with <c>periodKind=monthly</c>.</summary>
    public int? Month { get; set; }

    /// <summary>Only together with <c>periodKind=quarterly</c>.</summary>
    public int? Quarter { get; set; }

    /// <summary>The class snapshot stored on the mark (§8.6), not the pupil's class today.</summary>
    public string? ClassId { get; set; }

    public string? SubjectId { get; set; }

    public string? StudentId { get; set; }

    /// <summary>1-based.</summary>
    public int? Page { get; set; }

    /// <summary>Default 50, capped at 200 (§6 pagination envelope).</summary>
    public int? Limit { get; set; }
}

/// <summary>
/// <c>GET /by-subjects</c> (§6.6). <c>classIds</c> and <c>subjectIds</c> arrive
/// as repeated keys (<c>classIds=a&amp;classIds=b</c>).
/// </summary>
public sealed class SeasonalPivotFilter : SeasonalPeriodQuery
{
    public string[] ClassIds { get; set; } = [];

    public string[] SubjectIds { get; set; } = [];

    public int? Page { get; set; }

    public int? Limit { get; set; }
}

/// <summary><c>GET /coverage</c> (§6.6). Empty <c>teacherIds</c> = every teacher.</summary>
public sealed class SeasonalCoverageFilter : SeasonalPeriodQuery
{
    public string[] TeacherIds { get; set; } = [];

    public int? Page { get; set; }

    public int? Limit { get; set; }
}

/// <summary><c>GET /coverage/detail</c> — the pupils behind one coverage row.</summary>
public sealed class SeasonalCoverageDetailFilter : SeasonalPeriodQuery
{
    public string? TeacherId { get; set; }

    /// <summary><c>true</c> = marked only, <c>false</c> = unmarked only, absent = all.</summary>
    public bool? HasMark { get; set; }

    public int? Page { get; set; }

    public int? Limit { get; set; }
}

// ---------------------------------------------------------------------------
//  Responses
// ---------------------------------------------------------------------------

/// <summary>
/// The §6 pagination envelope: <c>{ items, total, page, limit }</c>.
/// <paramref name="Total"/> counts the whole filter, not the page.
/// </summary>
public sealed record SeasonalPageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int Limit);

/// <summary><c>{ id, name }</c> — a class or a subject on a mark row.</summary>
public sealed record SeasonalRefDto(string Id, string Name);

/// <summary><c>{ id, fullName }</c> — the pupil on a mark row.</summary>
public sealed record SeasonalStudentRefDto(string Id, string FullName);

/// <summary>
/// One stored mark (§6.6 <c>SeasonalMarkRowDto</c>).
/// </summary>
/// <param name="Class">The class snapshot taken at entry (§8.6), not the pupil's class today.</param>
/// <param name="PeriodLabel">Server-formatted Uzbek: "Mart 2026" | "2-chorak 2026" | "2026".</param>
/// <param name="Score">0–100; <c>null</c> = comment-only mark.</param>
/// <param name="UpdatedAt">"yyyy-MM-ddTHH:mm:ss", Tashkent wall clock, no offset (§6).</param>
/// <param name="CreatedByName"><c>null</c> when the author's account no longer exists.</param>
public sealed record SeasonalMarkRowDto(
    string Id,
    SeasonalStudentRefDto Student,
    SeasonalRefDto Class,
    SeasonalRefDto Subject,
    string PeriodKind,
    int Year,
    int? Month,
    int? Quarter,
    string PeriodLabel,
    decimal? Score,
    string? Comment,
    string UpdatedAt,
    string? CreatedByName);

/// <summary>One class a mark may be entered for.</summary>
public sealed record SeasonalScopeClassDto(string Id, string Name, int Grade);

/// <summary>One (class, subject, teacher) triple of the current schedule.</summary>
public sealed record SeasonalScopePairDto(
    string ClassId, string SubjectId, string SubjectName, string TeacherId, string TeacherFullName);

/// <summary>
/// §6.6 <c>ScopeDto</c> — which subjects are taught in which class, and by
/// whom. Built from <c>schedule_templates</c> → <c>schedule_lessons</c>
/// because nothing else answers "which subjects are taught in this class".
/// </summary>
public sealed record SeasonalScopeDto(
    IReadOnlyList<SeasonalScopeClassDto> Classes,
    IReadOnlyList<SeasonalScopePairDto> Pairs);

/// <summary>One pupil in the bulk-entry grid (§6.6 <c>SeasonalEntryRowDto</c>).</summary>
/// <param name="MarkId"><c>null</c> = nothing stored yet for this pupil and period.</param>
public sealed record SeasonalEntryRowDto(
    string StudentId, string FullName, decimal? Score, string? Comment, string? MarkId);

/// <summary><c>POST /bulk</c> outcome. A row that changed nothing is in none of the three.</summary>
public sealed record SeasonalBulkResultDto(int Created, int Updated, int Deleted);

/// <summary>A pivot column — one of the selected subjects.</summary>
public sealed record SeasonalPivotColumnDto(string SubjectId, string Name);

/// <summary>
/// One pupil of the pivot. <paramref name="Scores"/> maps subjectId → score and
/// holds only subjects that have a score; a missing key means "not marked",
/// never 0.
/// </summary>
public sealed record SeasonalPivotRowDto(
    string StudentId, string FullName, string ClassName, IReadOnlyDictionary<string, decimal> Scores);

/// <summary>§6.6 pivot response: the page envelope plus the column descriptions.</summary>
public sealed record SeasonalPivotPageDto(
    IReadOnlyList<SeasonalPivotColumnDto> Columns,
    IReadOnlyList<SeasonalPivotRowDto> Items,
    int Total,
    int Page,
    int Limit);

/// <summary>
/// §6.6 <c>CoverageRowDto</c>. Counts are pupil × subject slots (see
/// <c>SeasonalCoverageReport</c>), so <c>TotalStudents = Marked + Unmarked</c>
/// always and the drill-down lists exactly that many rows.
/// </summary>
/// <param name="Percent"><c>min(marked / totalStudents × 100, 100)</c>, whole number; 0 when there are no pupils.</param>
public sealed record SeasonalCoverageRowDto(
    string TeacherId, string FullName, int TotalStudents, int Marked, int Unmarked, int Percent);

/// <summary>One pupil in one (class, subject) the teacher teaches — the coverage drill-down.</summary>
public sealed record SeasonalCoverageDetailRowDto(
    string StudentId,
    string FullName,
    string ClassName,
    string SubjectId,
    string SubjectName,
    bool HasMark,
    decimal? Score);
