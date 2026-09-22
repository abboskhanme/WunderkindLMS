using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Submitted enrolment forms — filter, page and read (task SM-4).
//  Spec: docs/modules/sales-marketing.md §5.3.
// ===========================================================================
//
//  READ ONLY. Nothing here writes: submissions are created by the public
//  endpoint (SM-2, `SurveySubmissionService`) and are never edited afterwards
//  — the row is the evidence of what a parent typed, so the register must not
//  be able to change it. Every query is `AsNoTracking`.
//
//  WHY THE FILTER RUNS IN SQL AND NOT IN MEMORY
//  --------------------------------------------
//  `CertificateService.ListAsync` pulls its registry into memory and filters
//  there, and says why: a few hundred rows. This register has no ceiling —
//  one public link in an Instagram bio, one admissions season, and it is tens
//  of thousands of rows. §5.3 pins it: "Filtering is server-side (this list
//  grows without limit, unlike the board)."
//
//  WHY THE JOINS ARE EXPLICIT AND WHY THEY COME LAST
//  -------------------------------------------------
//  `Survey`, `Lead` and `LeadStage` carry no navigation properties in this
//  module (§4.6): `SalesMarketingModel` maps the foreign keys with
//  `HasOne<T>().WithMany()` and no collection. So the register joins by hand,
//  ONCE, for the whole page — a per-row lookup of the survey name or of the
//  lead's column would be the N+1 that turns a 50-row screen into 151 round
//  trips.
//
//  Every filter in §5.3 lives on `survey_submissions` itself, so filtering,
//  ordering and paging happen on that table alone and the joins are added
//  afterwards, to the page. The total is counted the same way — on the base
//  table, with no join at all.
//
//  THE BOARD IS NOT TOUCHED
//  ------------------------
//  `leads` and `lead_stages` are read for one label (which column the lead
//  landed in). Nothing here writes to them, and nothing here changes how the
//  design-frozen board (`pages/admin/leads/*`, CLAUDE.md) renders.
// ===========================================================================

/// <summary>
/// The submissions register (§5.3): one page, one detail row, or the whole
/// filter for the Excel export.
///
/// <para>
/// <b>No DI registration.</b> Stateless, depends only on the context, so the
/// controller builds it per request — exactly as <c>StudentSearchController</c>
/// builds <see cref="StudentListQuery"/> and <c>TransactionJournalController</c>
/// builds its journal. <c>Program.cs</c> is a shared file in this wave (§7.1)
/// and needs no entry for this.
/// </para>
/// </summary>
public sealed class SurveySubmissionQuery(IAppDbContext db)
{
    /// <summary>Page size when the caller does not ask for one (§5.3).</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Ceiling on one page (§5.3) — <c>pageSize=100000</c> is not a way to export.</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// Ceiling on one .xlsx. The export covers the whole filter, so a
    /// filterless export of a register that has been collecting for years
    /// would build the entire workbook in memory before the first byte
    /// reaches the browser. Ten thousand rows is several admissions seasons
    /// for a 500-pupil school and still opens quickly in Excel. When the limit
    /// actually bites, the file says so on its last line
    /// (<c>SurveySubmissionsController.Export</c>) — a short list that looks
    /// complete is the worse failure.
    /// </summary>
    public const int MaxExportRows = 10_000;

    // =====================================================================
    //  Public surface
    // =====================================================================

    /// <summary>One page of the register, plus the total over the whole filter.</summary>
    public async Task<SurveySubmissionPageDto> PageAsync(
        SurveySubmissionFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = Math.Max(1, filter.Page ?? 1);
        var size = Math.Clamp(filter.PageSize ?? DefaultPageSize, 1, MaxPageSize);

        var matching = Matching(filter);
        var total = await matching.CountAsync(ct);
        var window = Newest(matching).Skip((page - 1) * size).Take(size);

        return new SurveySubmissionPageDto(total, await ListAsync(window, ct));
    }

    /// <summary>
    /// The whole filter for the .xlsx export, capped at
    /// <see cref="MaxExportRows"/> rows. <c>Total</c> stays the REAL number of
    /// matching rows, so the caller can tell that the cap bit.
    /// </summary>
    public async Task<SurveySubmissionPageDto> ExportAsync(
        SurveySubmissionFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var matching = Matching(filter);
        var total = await matching.CountAsync(ct);
        var window = Newest(matching).Take(MaxExportRows);

        return new SurveySubmissionPageDto(total, await ListAsync(window, ct));
    }

    /// <summary>
    /// One submission with every stored value, including ip and user agent —
    /// the detail drawer. <c>null</c> when there is no such row.
    /// </summary>
    public async Task<SurveySubmissionDetailDto?> FindAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await JoinAsync(db.SurveySubmissions.AsNoTracking().Where(s => s.Id == id), ct);
        if (rows.Count == 0) return null;

        var (s, surveyName, stageTitle) = rows[0];
        return new SurveySubmissionDetailDto(
            s.Id, s.CreatedAt, s.SurveyId, surveyName, s.Status,
            ParentName(s), s.ParentPhone,
            FullName(s.StudentFirstName, s.StudentLastName),
            s.StudentGrade, s.StudentGender, s.StudentPhone,
            s.LeadId, stageTitle,
            s.ParentFirstName, s.ParentLastName,
            s.StudentFirstName, s.StudentLastName,
            s.Ip, s.UserAgent);
    }

    /// <summary>
    /// Trims a query-string value down to "a real filter, or nothing".
    ///
    /// <para>
    /// <b>Public on purpose:</b> the controller validates <c>status</c> before
    /// this class filters on it, and the two must look at the same string. A
    /// private copy on either side is how <c>status=" lead "</c> comes back
    /// validated and then silently unfiltered.
    /// </para>
    /// </summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // =====================================================================
    //  Filtering — `survey_submissions` only
    // =====================================================================

    /// <summary>
    /// Every filter of §5.3, and nothing else. Used by both the count and the
    /// rows, so the total can never describe a different set than the page.
    /// </summary>
    private IQueryable<SurveySubmission> Matching(SurveySubmissionFilter f)
    {
        var q = db.SurveySubmissions.AsNoTracking();

        if (f.SurveyId is { } surveyId) q = q.Where(s => s.SurveyId == surveyId);

        // The period is a calendar day in the SCHOOL's timezone, cut on
        // `timestamptz` boundaries: `from` inclusive, `to` inclusive (hence
        // the exclusive start of the following day). A form submitted at 23:30
        // in Tashkent belongs to that day, not to the next one in UTC.
        //
        // The boundary itself is borrowed rather than re-derived: the same
        // arithmetic already exists twice (`PaymentService`, and the public
        // copy used here), it had a real defect once — the offset was read at
        // the Unix epoch, when Tashkent was UTC+6, which closed the day an
        // hour early — and a test pins the two copies together. A third
        // private copy in this file would be a third chance to get it wrong.
        if (f.From is { } from)
        {
            var lower = TransactionJournalQuery.StartOfSchoolDay(from);
            q = q.Where(s => s.CreatedAt >= lower);
        }
        if (f.To is { } to)
        {
            var upperExclusive = TransactionJournalQuery.StartOfSchoolDay(to.AddDays(1));
            q = q.Where(s => s.CreatedAt < upperExclusive);
        }

        if (Clean(f.Status) is { } status) q = q.Where(s => s.Status == status);

        // Free text over the parent's name and phone — the two things the
        // screen's search box offers ("Ota-ona ismi yoki telefon...").
        if (Clean(f.Q)?.ToLowerInvariant() is { Length: > 0 } term)
        {
            // The phone is matched on DIGITS, through the same last-9 key the
            // duplicate check uses (§2.6): a parent writes "+998 90 123 45 67"
            // once and "901234567" the next time, and an officer searching for
            // either has to find both. A shorter run of digits ("1234") stays
            // a substring search inside that key. A term with no digits at all
            // ("Aziz") searches the name only.
            var phoneKey = PhoneUtil.Key(term);

            q = phoneKey.Length > 0
                ? q.Where(s => (s.ParentFirstName + " " + (s.ParentLastName ?? "")).ToLower().Contains(term)
                            || s.ParentPhoneKey.Contains(phoneKey))
                : q.Where(s => (s.ParentFirstName + " " + (s.ParentLastName ?? "")).ToLower().Contains(term));
        }

        return q;
    }

    /// <summary>
    /// Default sort, §5.3: <c>created_at desc</c>. The id breaks ties so that
    /// paging is stable — two submissions can share a timestamp (one family,
    /// two children, the form filled twice in the same second), and without a
    /// tiebreaker one of them could appear on both page 1 and page 2.
    /// </summary>
    private static IQueryable<SurveySubmission> Newest(IQueryable<SurveySubmission> q) =>
        q.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);

    // =====================================================================
    //  Joining and mapping
    // =====================================================================

    /// <summary>One materialised register row: the submission plus the two labels that live in other tables.</summary>
    private sealed record Joined(SurveySubmission Row, string SurveyName, string? LeadStageTitle);

    /// <summary>
    /// Adds the survey name and the lead's kanban column to an already
    /// filtered, ordered and paged set of submissions.
    ///
    /// <para>
    /// <b>Two queries, both bounded by the page — never one per row.</b> The
    /// survey join is INNER and rides along with the rows: <c>survey_id</c> is
    /// <c>not null</c> and <c>on delete restrict</c> (§4.2), so a submission
    /// without its survey cannot exist. The column title is fetched for the
    /// whole page in a second batch, the way <see cref="CertificateService"/>
    /// collects its subject names — and for the same reason: a lookup inside
    /// the mapping loop would be the N+1 that turns 50 rows into 51 round
    /// trips.
    /// </para>
    /// <para>
    /// <b>A missing lead, or a missing column, must not hide the submission.</b>
    /// The lead may have been deleted from the board afterwards
    /// (<c>on delete set null</c>, §4.2), and <c>leads.stage</c> is a plain
    /// <c>text</c> column with no foreign key (§2.2), so it can still name a
    /// column the school has since removed. Both cases end as a <c>null</c>
    /// title beside a row that is still listed — the submission is the evidence
    /// of what a parent typed and it outlives everything downstream of it.
    /// </para>
    /// <para>
    /// The ordering is repeated here because the paged input is read as a
    /// subquery, and a join does not preserve the order of its input.
    /// </para>
    /// </summary>
    private async Task<List<Joined>> JoinAsync(IQueryable<SurveySubmission> window, CancellationToken ct)
    {
        var rows = await (
            from s in window
            join sv in db.Surveys.AsNoTracking() on s.SurveyId equals sv.Id
            orderby s.CreatedAt descending, s.Id descending
            select new { Row = s, SurveyName = sv.Name })
            .ToListAsync(ct);

        var leadIds = rows
            .Select(r => r.Row.LeadId)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var stageByLead = new Dictionary<string, string>(StringComparer.Ordinal);
        if (leadIds.Count > 0)
        {
            // Inner join on purpose: a lead whose column no longer exists
            // simply drops out here and shows no title, which is the truth.
            stageByLead = await (
                from l in db.Leads.AsNoTracking()
                join st in db.LeadStages.AsNoTracking() on l.Stage equals st.Id
                where leadIds.Contains(l.Id)
                select new { l.Id, st.Title })
                .ToDictionaryAsync(x => x.Id, x => x.Title, StringComparer.Ordinal, ct);
        }

        return [.. rows.Select(r => new Joined(
            r.Row,
            r.SurveyName,
            r.Row.LeadId is { } leadId ? stageByLead.GetValueOrDefault(leadId) : null))];
    }

    /// <summary>The register rows for one window of submissions.</summary>
    private async Task<IReadOnlyList<SurveySubmissionDto>> ListAsync(
        IQueryable<SurveySubmission> window, CancellationToken ct)
    {
        var rows = await JoinAsync(window, ct);

        return [.. rows.Select(x => new SurveySubmissionDto(
            x.Row.Id,
            x.Row.CreatedAt,
            x.Row.SurveyId,
            x.SurveyName,
            x.Row.Status,
            ParentName(x.Row),
            x.Row.ParentPhone,
            FullName(x.Row.StudentFirstName, x.Row.StudentLastName),
            x.Row.StudentGrade,
            x.Row.StudentGender,
            x.Row.StudentPhone,
            x.Row.LeadId,
            x.LeadStageTitle))];
    }

    /// <summary>
    /// The parent always has a name to show: <c>parent_first_name</c> is
    /// <c>not null</c> and non-blank at the database level
    /// (<c>ck_survey_submissions_parent</c>, §4.2).
    /// </summary>
    private static string ParentName(SurveySubmission s) =>
        FullName(s.ParentFirstName, s.ParentLastName) ?? s.ParentFirstName;

    /// <summary>
    /// "Ism Familiya" out of the two columns, or <c>null</c> when neither was
    /// collected. Built in memory and not in SQL: the surname is optional and
    /// the pupil's name is optional as a whole (§2.4), so a SQL concatenation
    /// would leave a stray space that then renders as a name in the table.
    /// </summary>
    private static string? FullName(string? first, string? last)
    {
        var parts = new[] { first, last }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());

        var joined = string.Join(' ', parts);
        return joined.Length == 0 ? null : joined;
    }
}
