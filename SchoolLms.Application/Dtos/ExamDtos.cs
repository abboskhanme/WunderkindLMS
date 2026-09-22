namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Exams, exam types, participants, manual result entry and the results
//  register — the wire shapes of `/api/admin/exams/**`.
//  Spec: docs/modules/admission-and-testing.md §6.3. Unit: B2.
//  Client: schoollms.client/src/api/services/exams.ts (unit C3) — every record
//  below is named after the TypeScript interface it serialises into.
// ===========================================================================
//
//  NAMING. Every type here starts with `Exam`. Two sibling units write DTOs
//  into this namespace in the same wave (question bank, seasonal marks), and a
//  bare `PagedDto<T>` or `ImportErrorDto` in two files is a compile error that
//  neither author would see alone.
//
//  NULLABLE INPUTS. Request records declare their strings and ids nullable on
//  purpose: `[ApiController]` turns a missing non-nullable member into an
//  automatic English ProblemDetails 400 before the action runs. Nullable
//  members reach the service, which answers in Uzbek with `{ code, message }`.
//
//  TIMESTAMPS are `DateTime` in the Tashkent wall clock (`AppClock.Now`,
//  `timestamp without time zone`) and serialise with no offset and no `Z`
//  (§6, "Timestamp format").
// ===========================================================================

/// <summary>
/// The paging envelope of every new paged list (§6): <c>{ items, total, page, limit }</c>.
/// <c>exams.ts</c> <c>Paged&lt;T&gt;</c>.
/// </summary>
public sealed record ExamPageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int Limit);

// ---------------------------------------------------------------------------
//  Exam types (§5.4)
// ---------------------------------------------------------------------------

/// <summary><c>ExamType</c> — one row of the "Imtihon turi" catalogue.</summary>
public sealed record ExamTypeDto(string Id, string Name, string Description, bool IsActive, DateTime CreatedAt);

/// <summary><c>POST /api/admin/exams/types</c> — <c>{ name, description }</c>. A new type is always active.</summary>
public sealed record ExamTypeCreateRequest(string? Name, string? Description);

/// <summary>
/// <c>PUT /api/admin/exams/types/{id}</c> — <c>{ name, description, isActive }</c>.
/// A missing <c>isActive</c> leaves the flag as it is: a client that forgets
/// the field must not deactivate the type by accident.
/// </summary>
public sealed record ExamTypeUpdateRequest(string? Name, string? Description, bool? IsActive);

// ---------------------------------------------------------------------------
//  Exams (§5.5, §5.6)
// ---------------------------------------------------------------------------

/// <summary>
/// <c>ExamRow</c> — one line of the exam register. The four counts are
/// computed per page in grouped queries (never per row).
/// </summary>
public record ExamRowDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary><c>admission</c> | <c>block</c>.</summary>
    public required string Kind { get; init; }

    /// <summary><c>online</c> | <c>manual</c>.</summary>
    public required string Delivery { get; init; }

    /// <summary><c>draft</c> | <c>published</c> | <c>closed</c> | <c>cancelled</c>.</summary>
    public required string Status { get; init; }

    public string? ExamTypeId { get; init; }
    public string? ExamTypeName { get; init; }

    /// <summary>0–11, admission exams only.</summary>
    public int? Grade { get; init; }

    /// <summary>"YYYY-MM-DD", manual exams.</summary>
    public string? ExamDate { get; init; }

    public DateTime? OpensAt { get; init; }
    public DateTime? ClosesAt { get; init; }
    public int? TimeLimitMin { get; init; }
    public DateTime CreatedAt { get; init; }

    public int SectionCount { get; init; }
    public int ParticipantCount { get; init; }

    /// <summary>Participants with <c>status = 'finished'</c>.</summary>
    public int FinishedCount { get; init; }

    /// <summary>Participants with <c>status = 'absent'</c>.</summary>
    public int AbsentCount { get; init; }
}

/// <summary><c>Exam</c> — the register row plus its sections, in order.</summary>
public sealed record ExamDto : ExamRowDto
{
    public required IReadOnlyList<ExamSectionDto> Sections { get; init; }
}

/// <summary>One <c>exam_sections</c> row — one subject of the sitting.</summary>
/// <param name="BankId">Online only.</param>
/// <param name="QuestionCount">Online: how many questions are drawn.</param>
/// <param name="PointsPerCorrect">Online: copied from the bank at publish; <c>null</c> before.</param>
/// <param name="MaxScore">
/// Manual: the entry-grid ceiling. Online: <c>question_count × points_per_correct</c>,
/// written at publish — <c>null</c> for an online draft, whose stored 0 is a
/// placeholder, not a ceiling.
/// </param>
public sealed record ExamSectionDto(
    string Id, string SubjectId, string SubjectName, string? BankId, int? QuestionCount,
    decimal? PointsPerCorrect, decimal? MaxScore, int Order);

/// <summary>
/// <c>ExamUpsertDto</c> (§6.3), verbatim. <c>kind</c> and <c>delivery</c> are
/// immutable after create; <c>grade</c> is admission-only; <c>examDate</c> is
/// manual-only; the window and the time limit are online-only. Values that do
/// not belong to the exam's delivery are ignored rather than stored.
/// </summary>
public sealed record ExamUpsertRequest(
    string? Title,
    string? Kind,
    string? Delivery,
    string? ExamTypeId,
    int? Grade,
    string? ExamDate,
    DateTime? OpensAt,
    DateTime? ClosesAt,
    int? TimeLimitMin,
    IReadOnlyList<ExamSectionInput>? Sections);

/// <summary>One section of <see cref="ExamUpsertRequest"/>.</summary>
/// <param name="SubjectId">Online: may be omitted — it is taken from the bank.</param>
/// <param name="BankId">Online only.</param>
/// <param name="QuestionCount">Online only; omitted = the bank's <c>questions_per_test</c> at publish.</param>
/// <param name="MaxScore">Manual only — required there. Ignored for online (written at publish).</param>
/// <param name="Order">Display order; the list is sorted by it and renumbered 0…n-1.</param>
public sealed record ExamSectionInput(
    string? SubjectId, string? BankId, int? QuestionCount, decimal? MaxScore, int Order);

/// <summary><c>POST /api/admin/exams/{id}/cancel</c> — <c>{ reason }</c>.</summary>
public sealed record ExamCancelRequest(string? Reason);

/// <summary>
/// <c>GET /api/admin/exams</c> query (§6.3). Unknown <c>kind</c>/<c>status</c>
/// values are refused with 400, not ignored — an ignored filter returns the
/// whole register and reads as "nothing matched my filter" the wrong way round.
/// </summary>
public sealed class ExamListQuery
{
    public int? Page { get; set; }
    public int? Limit { get; set; }

    /// <summary>Case-insensitive substring of the title.</summary>
    public string? Search { get; set; }

    public string? Kind { get; set; }
    public string? Status { get; set; }
    public string? ExamTypeId { get; set; }

    /// <summary>
    /// Inclusive. Compared with the exam's day: <c>exam_date</c> for a manual
    /// exam, the date of <c>opens_at</c> for an online one.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Inclusive.</summary>
    public DateOnly? To { get; set; }
}

// ---------------------------------------------------------------------------
//  Participants (§5.7)
// ---------------------------------------------------------------------------

/// <summary>
/// <c>ParticipantRow</c>. <c>fullName</c> of a lead participant whose lead was
/// deleted (enrolment, or the board's delete — §2.2) is
/// <c>ExamService.DeletedCandidateName</c>: the row survives with its score,
/// the person's name does not (<c>exam_participants</c> keeps no personal data).
/// </summary>
public sealed record ExamParticipantRowDto(
    string Id,
    string ParticipantKind,
    string? LeadId,
    string? StudentId,
    string FullName,
    string? ClassId,
    string? ClassName,
    string Status,
    decimal? TotalPoints,
    decimal? MaxPoints,
    decimal? Percent,
    DateTime? ScoredAt);

/// <summary><c>GET /api/admin/exams/{id}/participants</c> query.</summary>
public sealed class ExamParticipantListQuery
{
    public int? Page { get; set; }
    public int? Limit { get; set; }

    /// <summary>Case-insensitive substring of the pupil's or the candidate's name.</summary>
    public string? Search { get; set; }

    public string? Status { get; set; }
}

/// <summary>
/// <c>POST /api/admin/exams/{id}/participants</c> — <c>{ leadIds?, studentIds?, classIds? }</c>.
/// An admission exam takes <c>leadIds</c> only; a block exam takes
/// <c>studentIds</c> and <c>classIds</c> only.
/// </summary>
public sealed record ExamAddParticipantsRequest(
    IReadOnlyList<string>? LeadIds, IReadOnlyList<string>? StudentIds, IReadOnlyList<string>? ClassIds);

/// <summary>
/// <c>{ added, skipped }</c>. <c>skipped</c> = already on the exam, or an
/// archived pupil named explicitly in <c>studentIds</c> (class expansion never
/// picks archived pupils in the first place).
/// </summary>
public sealed record ExamAddParticipantsResultDto(int Added, int Skipped);

// ---------------------------------------------------------------------------
//  Entry table (§6.3 EntryTableDto, §8.4)
// ---------------------------------------------------------------------------

/// <summary>One subject column of the grid.</summary>
public sealed record ExamEntryColumnDto(string SectionId, string SubjectId, string Name, decimal MaxScore);

/// <summary>
/// One participant row. <c>scores</c> has a key for EVERY column —
/// <c>null</c> when nothing has been entered for it.
/// </summary>
public sealed record ExamEntryRowDto(
    string ParticipantId,
    string FullName,
    string? ClassName,
    string Status,
    IReadOnlyDictionary<string, decimal?> Scores,
    decimal? TotalPoints,
    decimal? MaxPoints,
    decimal? Percent);

/// <summary><c>EntryTableDto</c> — bare (not paged), capped at 500 rows.</summary>
public sealed record ExamEntryTableDto(
    string ExamId,
    string Title,
    string? ExamDate,
    IReadOnlyList<ExamEntryColumnDto> Columns,
    IReadOnlyList<ExamEntryRowDto> Rows);

/// <summary><c>POST /api/admin/exams/{id}/entry-table</c> — <c>{ rows }</c>.</summary>
public sealed record ExamEntrySaveRequest(IReadOnlyList<ExamEntrySaveRow>? Rows);

/// <summary>
/// One row of the save. <c>absent: true</c> sets the participant absent and
/// clears their scores (§8.4) — <c>scores</c> must then be empty. Otherwise
/// the listed sections are upserted and the others are left as they are: a
/// score is removed by marking the pupil absent, never by omitting a cell.
/// </summary>
public sealed record ExamEntrySaveRow(string? ParticipantId, IReadOnlyList<ExamEntrySaveScore>? Scores, bool Absent);

/// <summary>One cell. <c>points</c> is rounded half-up to two decimals, then range-checked.</summary>
public sealed record ExamEntrySaveScore(string? SectionId, decimal? Points);

/// <summary>
/// <c>{ saved }</c> — the number of rows that changed something. A row that
/// repeats what is already stored is accepted and not counted.
/// </summary>
public sealed record ExamEntrySaveResultDto(int Saved);

/// <summary>
/// One problem group of an imported sheet — the <c>QuestionImportResultDto</c>
/// shape of §6.2: one chip per reason with the offending rows.
/// </summary>
/// <param name="Reason">Machine key — see <c>ResultImportService.Reasons</c>.</param>
/// <param name="Message">The Uzbek chip text.</param>
/// <param name="Rows">1-based sheet rows, header included — what the user sees in Excel.</param>
public sealed record ExamResultImportErrorDto(string Reason, string Message, IReadOnlyList<int> Rows);

/// <summary><c>ResultImportResultDto</c>.</summary>
/// <param name="TotalRows">Non-empty data rows (the header and blank rows are not counted).</param>
/// <param name="ValidCount">Rows without any error.</param>
/// <param name="ErrorCount">Rows with at least one error — skipped whole.</param>
/// <param name="Imported">Rows that changed something. Always 0 on a dry run.</param>
public sealed record ExamResultImportResultDto(
    string FileName,
    int TotalRows,
    int ValidCount,
    int ErrorCount,
    int Imported,
    IReadOnlyList<ExamResultImportErrorDto> Errors);

// ---------------------------------------------------------------------------
//  Results register (screen 8)
// ---------------------------------------------------------------------------

/// <summary>One subject of one result row. <c>points</c> is <c>null</c> when nothing was entered.</summary>
public sealed record ExamResultScoreDto(string SectionId, string SubjectId, string Name, decimal? Points, decimal MaxPoints);

/// <summary>
/// <c>ResultRow</c> — one participant of one exam. <c>examDate</c> is the
/// exam's day: <c>exam_date</c> for a manual exam, the date of <c>opens_at</c>
/// for an online one (the screen has one "Sana" column for both).
/// </summary>
public sealed record ExamResultRowDto(
    string ParticipantId,
    string ExamId,
    string ExamTitle,
    string? ExamDate,
    string Kind,
    string Delivery,
    string ParticipantKind,
    string FullName,
    string? ClassId,
    string? ClassName,
    string Status,
    decimal? TotalPoints,
    decimal? MaxPoints,
    decimal? Percent,
    DateTime? ScoredAt,
    IReadOnlyList<ExamResultScoreDto> Scores);

/// <summary><c>GET /api/admin/exams/results</c> and <c>/results/export</c> query.</summary>
public sealed class ExamResultListQuery
{
    public int? Page { get; set; }
    public int? Limit { get; set; }

    /// <summary>Case-insensitive substring of the participant's name or the exam's title.</summary>
    public string? Search { get; set; }

    public string? ExamId { get; set; }

    /// <summary>The class snapshot taken when the pupil was added (§5.7).</summary>
    public string? ClassId { get; set; }

    /// <summary>Participants of exams that have a section for this subject.</summary>
    public string? SubjectId { get; set; }

    /// <summary>Participant status. No default on the server — the screen sends <c>finished</c> (§3.2).</summary>
    public string? Status { get; set; }
}
