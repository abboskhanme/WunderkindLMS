namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Test bazasi — the admission question bank, admin side.
//  docs/modules/admission-and-testing.md §5.1–§5.3, §6.2. Unit: B1.
// ===========================================================================
//
//  THE CLIENT CONTRACT
//  -------------------
//  `schoollms.client/src/api/services/admissionBanks.ts` mirrors these records
//  field for field (name and nullability). Renaming a field here means changing
//  that file too.
//
//  WHY EVERY REQUEST FIELD IS NULLABLE
//  -----------------------------------
//  `[ApiController]` puts an implicit `[Required]` on a non-nullable property
//  and rejects the request itself with an English `ProblemDetails` 400 — the
//  Uzbek `{ code, message }` from `QuestionBankService` would never reach the
//  screen. So the wire shapes accept null and the service decides.
//  (Same reasoning as `SurveyAdminDtos.cs`.)
//
//  THE ANSWER KEY
//  --------------
//  `QuestionOptionDto.IsCorrect` is the answer key of a live entrance exam. It
//  exists on THESE admin shapes only, behind `[AdminPerm("admission",
//  GatedRead = true)]` (§4.3). The public exam page has its own DTOs without
//  it (§7.7) — never reuse these there.
// ===========================================================================

/// <summary>
/// The §6 pagination envelope: <c>{ items, total, page, limit }</c>.
///
/// <para>
/// Named for this module rather than a generic <c>PagedDto</c> because the
/// exam and seasonal-mark units are being written in parallel and would
/// collide on one shared name in this namespace. The wire shape is the one §6
/// defines for every new paged endpoint; merging the types later changes no JSON.
/// </para>
/// </summary>
public record AdmissionPageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int Limit);

/// <summary>
/// A question bank — the list row AND the single-bank response (§6.2 names
/// them <c>BankRowDto</c> / <c>BankDto</c>; they carry the same fields, because
/// the detail screen needs the count and the state for its warning banner).
/// </summary>
/// <param name="Grade">0–11; 0 is the preparatory "nol sinf", not "unknown".</param>
/// <param name="QuestionsCount"><c>count(*)</c> over <c>questions</c> — computed, never stored (§5.1).</param>
/// <param name="QuestionsPerTest">How many questions one sitting draws. <c>null</c> = not configured.</param>
/// <param name="TimeLimitMin">Minutes for this bank's block, 1–600. <c>null</c> = not configured.</param>
/// <param name="PointsPerCorrect"><c>numeric(6,2)</c>, &gt; 0. <c>null</c> = not configured.</param>
/// <param name="State"><c>unconfigured | notEnough | ready</c> — derived on the server
/// (<see cref="Services.QuestionBankService.StateOf"/>); the client never recomputes it.</param>
public record BankDto(
    string Id,
    int Grade,
    string SubjectId,
    string SubjectName,
    int QuestionsCount,
    int? QuestionsPerTest,
    int? TimeLimitMin,
    decimal? PointsPerCorrect,
    string State);

/// <summary><c>POST /api/admin/admission/banks</c>. Grade and subject are fixed after this.</summary>
/// <param name="Grade">Required, 0–11. Nullable only so a missing value is a 400, not a silent 0.</param>
public record BankCreateRequest(
    int? Grade,
    string? SubjectId,
    int? QuestionsPerTest,
    int? TimeLimitMin,
    decimal? PointsPerCorrect);

/// <summary>
/// <c>PUT /api/admin/admission/banks/{id}</c> — the settings strip. All three
/// are always sent; <c>null</c> clears a value (the bank becomes <c>unconfigured</c>).
/// </summary>
public record BankSettingsRequest(int? QuestionsPerTest, int? TimeLimitMin, decimal? PointsPerCorrect);

/// <summary>One answer option, admin shape — <b>carries the answer key</b>.</summary>
/// <param name="Order">0 → A … 5 → F.</param>
public record QuestionOptionDto(string Id, string Text, bool IsCorrect, int Order);

/// <summary>A question with its options, ordered A–F.</summary>
/// <param name="ImageUrl"><c>/uploads/&lt;guid&gt;.&lt;ext&gt;</c> or <c>null</c>.</param>
/// <param name="Order">Display order inside the bank.</param>
public record QuestionDto(
    string Id,
    string BankId,
    string Text,
    string? ImageUrl,
    int Order,
    IReadOnlyList<QuestionOptionDto> Options);

/// <summary>
/// One option in a save request. <b>Its position in the array is its order</b>
/// (index 0 is A). On update: with <paramref name="Id"/> — that option is edited
/// in place; without — a new option; an existing option left out — deleted.
/// </summary>
public record QuestionOptionInput(string? Id, string? Text, bool IsCorrect);

/// <summary><c>POST /api/admin/admission/questions</c>.</summary>
public record QuestionCreateRequest(
    string? BankId,
    string? Text,
    string? ImageUrl,
    IReadOnlyList<QuestionOptionInput>? Options);

/// <summary><c>PUT /api/admin/admission/questions/{id}</c>. The bank does not change.</summary>
public record QuestionUpdateRequest(
    string? Text,
    string? ImageUrl,
    IReadOnlyList<QuestionOptionInput>? Options);

/// <summary>
/// One chip of the import report: a reason and every sheet row that has it.
/// </summary>
/// <param name="Reason">One of the closed set in
/// <see cref="Services.QuestionImportReason.All"/>.</param>
/// <param name="Rows">1-based sheet rows INCLUDING the header, ascending — what
/// the user sees in Excel.</param>
public record QuestionImportErrorDto(string Reason, IReadOnlyList<int> Rows);

/// <summary>
/// <c>POST /api/admin/admission/questions/import</c> — the same shape for the dry
/// run and the real run (§6.2).
/// </summary>
/// <param name="TotalRows">Non-blank data rows (the header and fully blank rows are not counted).</param>
/// <param name="ValidCount">Rows without any error. <c>ValidCount + ErrorCount = TotalRows</c>.</param>
/// <param name="ErrorCount">Rows with at least one error — each is skipped whole.</param>
/// <param name="Imported">Questions written. Always 0 on a dry run.</param>
/// <param name="Errors">Grouped by reason, in the fixed order of the reason list.
/// A row with two problems appears under both reasons.</param>
public record QuestionImportResultDto(
    string FileName,
    int TotalRows,
    int ValidCount,
    int ErrorCount,
    int Imported,
    IReadOnlyList<QuestionImportErrorDto> Errors);
