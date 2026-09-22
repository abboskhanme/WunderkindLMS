namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Submitted enrolment forms — the admin register (task SM-4).
//  Spec: docs/modules/sales-marketing.md §5.3 (contract), §4.2 (columns),
//  §2.7 (what the screen shows).
// ===========================================================================
//
//  WHY A FILE OF ITS OWN
//  ---------------------
//  The same reason `SalesMarketing.cs` is not part of `Entities.cs`: this
//  module is built by several agents at once. `Dtos/SurveyDtos.cs` (the public
//  form, SM-2) and `Dtos/SurveyAdminDtos.cs` (the survey editor, SM-3) belong
//  to other slices and are not touched here.
//
//  WHY EVERY NAME CARRIES THE `SurveySubmission` PREFIX
//  ----------------------------------------------------
//  §5.3 calls these `SubmissionDto` / `SubmissionDetailDto`. In this assembly
//  a bare `Submission` already means something else — `AssignmentSubmission`
//  and its `SubmissionRowDto` (`Dtos.cs`), i.e. homework handed in by a pupil.
//  The JSON the client reads is unchanged (the property names are the ones in
//  `api/services/surveySubmissions.ts`); only the C# type names are longer.
//
//  IP AND USER AGENT LIVE ON THE DETAIL DTO ONLY
//  ---------------------------------------------
//  §4.2: `survey_submissions.ip` / `.user_agent` are the only place in this
//  system where a member of the public's IP is kept. They exist for abuse
//  triage, they are shown in the admin detail drawer, and they are in NEITHER
//  the list DTO NOR the Excel export. Adding them to <see cref="SurveySubmissionDto"/>
//  would put them on a screen and in a downloadable file in one edit.
//
//  TIME TYPE
//  ---------
//  `created_at` is `timestamptz` (§4), so it is a `DateTimeOffset` here —
//  never the `DateTime` "wall clock" the older entities use.

/// <summary>
/// Register filters, bound straight from the query string
/// (<c>[FromQuery]</c>). Everything is optional; with no parameter at all the
/// answer is the whole register, newest first (§5.3).
/// </summary>
public sealed class SurveySubmissionFilter
{
    /// <summary>One survey (<c>surveys.id</c>). Closed surveys included — their submissions stay in the register.</summary>
    public Guid? SurveyId { get; set; }

    /// <summary>Period start, inclusive, in the school's own calendar day ("YYYY-MM-DD").</summary>
    public DateOnly? From { get; set; }

    /// <summary>Period end, inclusive.</summary>
    public DateOnly? To { get; set; }

    /// <summary><c>lead</c> | <c>duplicate</c> — see <see cref="Domain.SurveySubmissionStatus"/>.</summary>
    public string? Status { get; set; }

    /// <summary>Free text over the parent's name and phone (the screen's search box).</summary>
    public string? Q { get; set; }

    /// <summary>1-based (§5.3).</summary>
    public int? Page { get; set; }

    /// <summary>Default 50, capped at 200 (§5.3).</summary>
    public int? PageSize { get; set; }
}

/// <summary>
/// One register row (§5.3 <c>SubmissionDto</c>). The names the parent typed
/// are folded into two display strings here; the drawer shows them raw
/// (<see cref="SurveySubmissionDetailDto"/>).
/// </summary>
/// <param name="Status"><c>lead</c> = a lead was created · <c>duplicate</c> = the same family inside 24 h (§2.6 step 4).</param>
/// <param name="StudentFullName"><c>null</c> when the survey does not ask for the pupil's name (§2.4).</param>
/// <param name="StudentGrade">0..11, where <c>0</c> is a real grade — nol sinf, never "unknown" (§2.1).</param>
/// <param name="LeadId"><c>null</c> when the lead was deleted from the board afterwards (the submission survives — §4.2).</param>
/// <param name="LeadStageTitle">The kanban column the lead sits in now, or <c>null</c> with the lead.</param>
public sealed record SurveySubmissionDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid SurveyId,
    string SurveyName,
    string Status,
    string ParentFullName,
    string ParentPhone,
    string? StudentFullName,
    short? StudentGrade,
    string? StudentGender,
    string? StudentPhone,
    string? LeadId,
    string? LeadStageTitle);

/// <summary>
/// One submission with every value as it was stored (§5.3
/// <c>SubmissionDetailDto</c>) — the drawer behind a register row.
///
/// <para>
/// <b>Flat, not derived from <see cref="SurveySubmissionDto"/>.</b> The wire
/// shape is what the client reads (`SubmissionDetail extends Submission` in
/// `api/services/surveySubmissions.ts`); a record hierarchy would buy nothing
/// on the wire and would make it easy to widen the list row by editing a base
/// type — which is exactly how <paramref name="Ip"/> would end up in a list
/// and in the export.
/// </para>
/// </summary>
/// <param name="Ip">Drawer only. Never in the list, never in the export (§4.2).</param>
/// <param name="UserAgent">Same purpose, same limit.</param>
public sealed record SurveySubmissionDetailDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid SurveyId,
    string SurveyName,
    string Status,
    string ParentFullName,
    string ParentPhone,
    string? StudentFullName,
    short? StudentGrade,
    string? StudentGender,
    string? StudentPhone,
    string? LeadId,
    string? LeadStageTitle,
    string ParentFirstName,
    string? ParentLastName,
    string? StudentFirstName,
    string? StudentLastName,
    string? Ip,
    string? UserAgent);

/// <summary>
/// One page of the register (§5.3: <c>{ "total": 128, "rows": [...] }</c>).
/// <paramref name="Total"/> counts the WHOLE filter, not the page — the
/// pager and the "N ta ariza" line both read it.
/// </summary>
public sealed record SurveySubmissionPageDto(
    int Total,
    IReadOnlyList<SurveySubmissionDto> Rows);

/// <summary>
/// A refused filter (§5.6 code <c>validation</c>), in the house error shape:
/// a machine <c>code</c>, one Uzbek sentence, and the offending query
/// parameters.
///
/// <para>
/// <b><paramref name="Errors"/> is never null and never empty</b> — this DTO
/// is only ever built for a failure that names at least one parameter. A
/// client that has to test for null before reading a field eventually stops
/// reading it.
/// </para>
/// </summary>
public sealed record SurveySubmissionErrorDto(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string> Errors);
