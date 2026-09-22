namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Nomzodlar — admission candidates. The wire shapes of the three additive
//  endpoints under `/api/admin/leads` (LeadCandidatesController).
//  Spec: docs/modules/admission-and-testing.md §2.2, §6.1, §8.5. Unit: B5.
//  Client: schoollms.client/src/api/services/candidates.ts — every record
//  below is named after the TypeScript interface it serialises into.
// ===========================================================================
//
//  A CANDIDATE IS A LEAD (§2.2). There is no candidates table: a row here is a
//  `leads` row whose `admission_status <> 'none'`, plus a summary of one of its
//  `exam_participants` rows. Enrolment deletes the lead (2026-09-22), so no
//  row ever carries `enrolled` and `studentId` is always null.
//
//  ONLINE TEST DEFERRED (scope cut 2026-09-22). Invitations and attempts (B3)
//  are not built; admission exams run on paper. `invitationState` is always
//  `none` and `invitation` always null — the fields stay so the client types
//  hold, and so B3 only has to fill them in.
//
//  PAGING reuses `ExamPageDto<T>` — the one `{ items, total, page, limit }`
//  envelope of this module (§6), not a second copy of it.
// ===========================================================================

/// <summary>
/// <c>CandidateRow</c> — §6.1 <c>CandidateRowDto</c>, verbatim. The exam fields
/// summarise ONE participation: the newest, or — when the list is filtered by
/// <c>examId</c> — the one on that exam.
/// </summary>
public record CandidateRowDto
{
    public string LeadId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string ParentPhone { get; init; } = string.Empty;

    /// <summary>0–11 — <c>leads.target_grade</c>.</summary>
    public int TargetGrade { get; init; }

    /// <summary><c>LeadAdmissionStatus</c> — never <c>enrolled</c>.</summary>
    public string AdmissionStatus { get; init; } = string.Empty;

    /// <summary><c>null</c> when the lead has no participation.</summary>
    public string? ExamId { get; init; }
    public string? ExamTitle { get; init; }

    /// <summary><c>ExamParticipantStatus</c>; <c>null</c> when the lead has no participation.</summary>
    public string? ParticipantStatus { get; init; }

    /// <summary><c>none | issued | opened | revoked | expired</c>. Always <c>none</c> until B3.</summary>
    public string InvitationState { get; init; } = string.Empty;

    /// <summary><c>null</c> until the participation is scored.</summary>
    public decimal? TotalPoints { get; init; }
    public decimal? MaxPoints { get; init; }
    public decimal? Percent { get; init; }

    /// <summary>
    /// §6.1 "null until enrolled". Enrolment deletes the lead, so no row this
    /// endpoint returns can carry one — always <c>null</c>, kept for the contract.
    /// </summary>
    public string? StudentId { get; init; }
}

/// <summary>
/// <c>CandidateCard</c> — the row, opened: the read-only lead fields of the
/// card's left pane plus every participation of the lead, newest first.
/// <c>participations[0]</c> is the participation the row part summarises.
/// </summary>
public sealed record CandidateCardDto : CandidateRowDto
{
    /// <summary>
    /// The row part is copied (the record's copy constructor), so the card and
    /// the list row are summarised by one method and cannot drift apart.
    /// </summary>
    public CandidateCardDto(CandidateRowDto row, string gender, string birthDate, string parentFullName,
        string? note, string stage, string? stageName, IReadOnlyList<CandidateParticipationDto> participations)
        : base(row)
    {
        Gender = gender;
        BirthDate = birthDate;
        ParentFullName = parentFullName;
        Note = note;
        Stage = stage;
        StageName = stageName;
        Participations = participations;
    }

    /// <summary><c>male | female</c>.</summary>
    public string Gender { get; init; }

    /// <summary><c>YYYY-MM-DD</c>; empty for a lead created without one.</summary>
    public string BirthDate { get; init; }

    public string ParentFullName { get; init; }
    public string? Note { get; init; }

    /// <summary><c>lead_stages.id</c> — the board column the lead sits in.</summary>
    public string Stage { get; init; }

    /// <summary>The column's title; <c>null</c> if the column was deleted.</summary>
    public string? StageName { get; init; }

    public IReadOnlyList<CandidateParticipationDto> Participations { get; init; }
}

/// <summary><c>CandidateParticipation</c> — one <c>exam_participants</c> row of the lead, with its exam.</summary>
/// <param name="Grade">The exam's grade (admission exams carry one, §5.5).</param>
/// <param name="ExamDate">"YYYY-MM-DD" — paper exams.</param>
/// <param name="Invitation">The latest link of this participation. Always <c>null</c> until B3.</param>
public sealed record CandidateParticipationDto(
    string ParticipantId,
    string ExamId,
    string ExamTitle,
    string ExamStatus,
    string Delivery,
    int? Grade,
    string? ExamDate,
    DateTime? OpensAt,
    DateTime? ClosesAt,
    int? TimeLimitMin,
    string Status,
    decimal? TotalPoints,
    decimal? MaxPoints,
    decimal? Percent,
    DateTime? ScoredAt,
    DateTime CreatedAt,
    CandidateInvitationDto? Invitation);

/// <summary>
/// <c>CandidateInvitation</c> — the latest <c>exam_invitations</c> row of a
/// participation, never the token itself (§5.9). Declared so the contract is
/// typed; nothing produces it until the online test (B3) is built.
/// </summary>
public sealed record CandidateInvitationDto(
    string State,
    string TokenHint,
    DateTime ValidFrom,
    DateTime ValidUntil,
    DateTime IssuedAt,
    DateTime? FirstOpenedAt,
    DateTime? RevokedAt);

/// <summary>
/// <c>GET /api/admin/leads/candidates</c> query (§6.1). An unknown
/// <c>admissionStatus</c> is refused with 400, not ignored — an ignored filter
/// returns everyone and reads as "the filter matched" the wrong way round.
/// </summary>
public sealed class CandidateListQuery
{
    public int? Page { get; set; }
    public int? Limit { get; set; }

    /// <summary>Case-insensitive substring of the name, or a fragment of the parent's phone.</summary>
    public string? Search { get; set; }

    /// <summary><c>invited | testing | tested | accepted | rejected</c>.</summary>
    public string? AdmissionStatus { get; set; }

    /// <summary><c>leads.target_grade</c>.</summary>
    public int? Grade { get; set; }

    /// <summary>Candidates who have a participation on this exam, whatever its status.</summary>
    public string? ExamId { get; set; }
}

/// <summary>
/// <c>PATCH /api/admin/leads/{id}/admission-status</c> — <c>{ status }</c>.
/// Only <c>accepted</c> / <c>rejected</c>: the human decisions of §8.5.
/// Nullable so a missing value reaches the service and is answered in Uzbek.
/// </summary>
public sealed record CandidateStatusRequest(string? Status);
