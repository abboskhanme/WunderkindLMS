using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Nomzodlar — the admission candidate register, the candidate card and the
//  human decision (accepted / rejected).
//  Spec: docs/modules/admission-and-testing.md §2.2, §6.1, §8.5. Unit: B5.
// ===========================================================================
//
//  A CANDIDATE IS A LEAD (§2.2)
//  ----------------------------
//  The register is `leads WHERE admission_status <> 'none'`. A lead becomes a
//  candidate when `ExamService.AddParticipantsAsync` puts it on an admission
//  exam (`none → invited`) and advances when a score is saved
//  (`ExamService.AdvanceLeadToTested`). Neither rule is repeated here — this
//  file only READS those transitions and adds the one human step.
//
//  Enrolment (`POST /api/admin/leads/{id}/enrol`, StudentsController) DELETES
//  the lead, so an enrolled candidate simply stops being listed, and its card
//  is a 404. `enrolled` is never stored (`ck_leads_admission_status`).
//
//  THE ONLINE TEST IS DEFERRED (scope cut 2026-09-22)
//  --------------------------------------------------
//  Invitations and attempts (unit B3) are not built — admission exams run on
//  paper with manual result entry. So every `invitationState` is `none` and
//  every `invitation` is null. When B3 lands, `ParticipationsAsync` is the one
//  place that must join the latest `exam_invitations` row per participant.
//
//  NO N+1
//  ------
//  A page is three statements whatever its size: the count, the page of leads,
//  and ONE query for every participation of those leads (joined to its exam).
//  The "latest participation" is picked in memory from that last result — a
//  lead sits a handful of admission exams at most, so the set is small.
//
//  STATIC, NOT IN DI — the `ExamService` pattern: `Program.cs` is a shared
//  file, so the controller passes its scoped context. Refusals reuse
//  `ExamError` (`{ code, message }`, Uzbek) so this module answers in one shape.
// ===========================================================================

/// <summary>Reads of the candidate register (§6.1): the paged list and the card.</summary>
public static class CandidateQuery
{
    /// <summary><c>CandidateRowDto.invitationState</c> of every row until B3 issues links.</summary>
    public const string NoInvitation = "none";

    public const string LeadNotFoundCode = "lead_not_found";

    /// <summary>Same sentence as the client's fallback: the usual reason a candidate is gone is enrolment.</summary>
    public const string CandidateNotFoundMessage =
        "Nomzod topilmadi — u o'quvchiga aylantirilgan yoki lidlar doskasidan o'chirilgan bo'lishi mumkin";

    /// <summary>What <c>admissionStatus</c> may filter by: every stored status except <c>none</c>.</summary>
    public static readonly IReadOnlyList<string> CandidateStatuses =
        LeadAdmissionStatus.All.Where(s => s != LeadAdmissionStatus.None).ToList();

    /// <summary>
    /// A search term needs at least this many digits before it is also matched
    /// against the phone with its separators removed — "90" alone would match
    /// every Uzbek number.
    /// </summary>
    private const int MinPhoneDigits = 3;

    /// <summary>
    /// <c>GET /api/admin/leads/candidates</c> — one page, by name. Filters:
    /// search (name or phone), admission status, target grade, and exam
    /// (candidates with a participation on it, whose row then describes THAT
    /// participation rather than the newest one).
    /// </summary>
    public static async Task<ExamOutcome<ExamPageDto<CandidateRowDto>>> ListAsync(
        IAppDbContext db, CandidateListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, limit) = ExamService.Paging(query.Page, query.Limit);

        var status = ExamService.Clean(query.AdmissionStatus);
        if (status is not null && !CandidateStatuses.Contains(status))
            return ExamError.BadRequest("validation",
                $"Noma'lum qiymat: admissionStatus='{status}'. Ruxsat etilganlar: {string.Join(", ", CandidateStatuses)}");
        var examId = ExamService.Clean(query.ExamId);

        var q = db.Leads.AsNoTracking().Where(l => l.AdmissionStatus != LeadAdmissionStatus.None);
        if (status is not null) q = q.Where(l => l.AdmissionStatus == status);
        if (query.Grade is { } grade) q = q.Where(l => l.TargetGrade == grade);
        if (examId is not null)
            q = q.Where(l => db.ExamParticipants.Any(p => p.ExamId == examId && p.LeadId == l.Id));
        if (ExamService.Clean(query.Search) is { } term) q = WhereSearch(q, term);

        var total = await q.CountAsync(ct);
        var leads = await q.OrderBy(l => l.FullName).ThenBy(l => l.Id)
            .Skip((page - 1) * limit).Take(limit)
            .ToListAsync(ct);

        var participations = await ParticipationsAsync(db, leads.Select(l => l.Id).ToList(), examId, ct);
        var items = leads
            .Select(l => Row(l, participations.TryGetValue(l.Id, out var list) ? list[0] : null))
            .ToList();
        return new ExamPageDto<CandidateRowDto>(items, total, page, limit);
    }

    /// <summary>
    /// <c>GET /api/admin/leads/{id}/admission</c> — any lead, <c>none</c>
    /// included (the card offers no decision then). <c>null</c> = no such lead.
    /// </summary>
    public static async Task<CandidateCardDto?> GetCardAsync(IAppDbContext db, string leadId, CancellationToken ct = default)
    {
        var found = await (
                from l in db.Leads.AsNoTracking()
                where l.Id == leadId
                join s in db.LeadStages.AsNoTracking() on l.Stage equals s.Id into sj
                from s in sj.DefaultIfEmpty()
                select new { Lead = l, StageName = s == null ? null : s.Title })
            .FirstOrDefaultAsync(ct);
        if (found is null) return null;

        var lead = found.Lead;
        var participations = (await ParticipationsAsync(db, [lead.Id], examId: null, ct))
            .GetValueOrDefault(lead.Id) ?? [];
        return new CandidateCardDto(
            Row(lead, participations.FirstOrDefault()),
            lead.Gender, lead.BirthDate, lead.ParentFullName, lead.Note, lead.Stage, found.StageName,
            participations);
    }

    /// <summary>
    /// The row: the lead, summarised by one participation (or none). The one
    /// place that decides what the row's exam columns say.
    /// </summary>
    private static CandidateRowDto Row(Lead lead, CandidateParticipationDto? shown) => new()
    {
        LeadId = lead.Id,
        FullName = lead.FullName,
        ParentPhone = lead.ParentPhone,
        TargetGrade = lead.TargetGrade,
        AdmissionStatus = lead.AdmissionStatus,
        ExamId = shown?.ExamId,
        ExamTitle = shown?.ExamTitle,
        ParticipantStatus = shown?.Status,
        InvitationState = shown?.Invitation?.State ?? NoInvitation,
        TotalPoints = shown?.TotalPoints,
        MaxPoints = shown?.MaxPoints,
        Percent = shown?.Percent,
        StudentId = null,
    };

    /// <summary>
    /// Every participation of the given leads with its exam, grouped by lead,
    /// newest first inside each group — one query. <paramref name="examId"/>
    /// narrows it to one exam (the list's exam filter).
    /// </summary>
    private static async Task<Dictionary<string, List<CandidateParticipationDto>>> ParticipationsAsync(
        IAppDbContext db, IReadOnlyCollection<string> leadIds, string? examId, CancellationToken ct)
    {
        if (leadIds.Count == 0) return [];

        var q = from p in db.ExamParticipants.AsNoTracking()
                join e in db.Exams.AsNoTracking() on p.ExamId equals e.Id
                where p.LeadId != null && leadIds.Contains(p.LeadId)
                select new { P = p, E = e };
        if (examId is not null) q = q.Where(x => x.E.Id == examId);

        var rows = await q
            .OrderByDescending(x => x.P.CreatedAt).ThenByDescending(x => x.P.Id)
            .Select(x => new
            {
                LeadId = x.P.LeadId!,
                Dto = new CandidateParticipationDto(
                    x.P.Id, x.E.Id, x.E.Title, x.E.Status, x.E.Delivery, x.E.Grade, x.E.ExamDate,
                    x.E.OpensAt, x.E.ClosesAt, x.E.TimeLimitMin,
                    x.P.Status, x.P.TotalPoints, x.P.MaxPoints, x.P.Percent, x.P.ScoredAt, x.P.CreatedAt,
                    null),
            })
            .ToListAsync(ct);

        // GroupBy keeps the source order inside each group, so [0] is the newest.
        return rows.GroupBy(r => r.LeadId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Dto).ToList(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Name (case-insensitive substring), or phone. The phone is typed on the
    /// board with or without spaces, dashes and brackets, so a term with enough
    /// digits is also matched against the stored phone with those removed.
    /// </summary>
    private static IQueryable<Lead> WhereSearch(IQueryable<Lead> q, string term)
    {
        var lower = term.ToLowerInvariant();
        var digits = PhoneUtil.DigitsOnly(term);
        if (digits.Length < MinPhoneDigits)
            return q.Where(l => l.FullName.ToLower().Contains(lower) || l.ParentPhone.Contains(term));

        return q.Where(l => l.FullName.ToLower().Contains(lower)
            || l.ParentPhone.Contains(term)
            || l.ParentPhone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Contains(digits));
    }
}

/// <summary>
/// The one write of the candidate module: the human admission decision
/// (§8.5 — "never automatic: the system does not decide admissions on a score").
/// </summary>
public static class CandidateDecision
{
    /// <summary>
    /// §8.5's last state. Never stored (enrolment deletes the lead), so it has no
    /// <see cref="LeadAdmissionStatus"/> constant — named here only to refuse it clearly.
    /// </summary>
    private const string Enrolled = "enrolled";

    /// <summary>The only values <c>PATCH /admission-status</c> accepts.</summary>
    public static readonly IReadOnlyList<string> Decisions = [LeadAdmissionStatus.Accepted, LeadAdmissionStatus.Rejected];

    public const string StatusRequiredMessage = "Qarorni tanlang: qabul qilish yoki rad etish";
    public const string EnrolViaEndpointMessage =
        "Nomzod o'quvchiga «O'quvchi qilish» tugmasi orqali aylantiriladi — holatni qo'lda «enrolled» qilib bo'lmaydi";
    public const string NotADecisionMessage =
        "Qo'lda faqat «qabul qilingan» yoki «rad etilgan» holatini qo'yish mumkin — qolganlarini tizim o'zi qo'yadi";
    public const string NotACandidateMessage =
        "Bu lid hali nomzod emas — avval uni qabul imtihoniga biriktiring";

    /// <summary>
    /// <c>PATCH /api/admin/leads/{id}/admission-status</c> <c>{ status }</c>.
    /// <list type="bullet">
    ///   <item>400 — missing, <c>enrolled</c> (enrolment is its own endpoint and deletes the lead),
    ///     or any other value (<c>invited</c>/<c>testing</c>/<c>tested</c> are the engine's, the rest unknown).</item>
    ///   <item>404 — no such lead (typically: already enrolled).</item>
    ///   <item>409 — the lead is not a candidate (<c>none</c>): a decision needs a candidate.</item>
    ///   <item>Otherwise set and audited. <c>accepted ↔ rejected</c> both ways is allowed — a human
    ///     may change their mind (§8.5 "backward moves … only by hand"). Repeating the current
    ///     value is a no-op, not an error, and writes no audit row.</item>
    /// </list>
    /// </summary>
    public static async Task<ExamError?> SetAsync(
        IAppDbContext db, ExamActor actor, string leadId, CandidateStatusRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = ExamService.Clean(request.Status);
        if (status is null) return ExamError.BadRequest("validation", StatusRequiredMessage);
        if (status == Enrolled) return ExamError.BadRequest("enrol_via_endpoint", EnrolViaEndpointMessage);
        if (!Decisions.Contains(status)) return ExamError.BadRequest("validation", NotADecisionMessage);

        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return ExamError.NotFound(CandidateQuery.LeadNotFoundCode, CandidateQuery.CandidateNotFoundMessage);
        if (lead.AdmissionStatus == LeadAdmissionStatus.None)
            return ExamError.Conflict("not_candidate", NotACandidateMessage);
        if (lead.AdmissionStatus == status) return null;

        var before = lead.AdmissionStatus;
        lead.AdmissionStatus = status;
        var verb = status == LeadAdmissionStatus.Accepted ? "qabul qilindi" : "rad etildi";
        db.AuditLogs.Add(AuditService.Entry(AuditService.EntityLead, lead.Id, "update",
            $"Nomzod {verb}: «{lead.FullName}»", actor.UserId, actor.Name,
            before: new { AdmissionStatus = before }, after: new { AdmissionStatus = status }));
        await db.SaveChangesAsync(ct);
        return null;
    }
}
