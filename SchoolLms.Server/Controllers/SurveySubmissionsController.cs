using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// <b>Topshirilgan arizalar</b> — the register of what parents typed into the
/// public enrolment form (docs/modules/sales-marketing.md §5.3, task SM-4).
///
/// <para>
/// <b>Why this screen exists at all.</b> EduSchool answers "which leads came
/// from a form?" with a filter on its kanban board. Ours is design-frozen
/// (<c>CLAUDE.md</c>, <c>pages/admin/leads/*</c>), so §2.7 answers the
/// question with a page of its own — and a better answer: it also shows the
/// submissions that were duplicates and the values as they arrived, before
/// anyone edited the lead. Nothing in this file writes to <c>leads</c> or
/// changes what the board renders.
/// </para>
///
/// <para>
/// <b>Permission — <c>marketing</c></b> on every endpoint (§5.3, and §8.1 pins
/// it). <see cref="AdminPermAttribute"/> opens GET to any staff member so that
/// one section can read another's data, and gates writes on the claim; there
/// are no writes here, so the gate reduces to: admin and superadmin always,
/// staff always, teacher / student / parent / cashier never, anonymous 401.
/// </para>
///
/// <para>
/// <b>Read only.</b> A submission is evidence, not a record to maintain: it is
/// written once by the public endpoint (SM-2) and never edited. The screen's
/// only outgoing link is to the board (<c>/admin/leads</c>), which is a
/// different controller and a different permission.
/// </para>
///
/// <para>
/// <b>DI:</b> <c>Program.cs</c> is untouched — the query object is stateless
/// and depends only on the context, so it is built per request, exactly as in
/// <see cref="StudentSearchController"/> and <see cref="TransactionJournalController"/>.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("marketing")]
[Route("api/admin/survey-submissions")]
public sealed class SurveySubmissionsController(AppDbContext db) : ControllerBase
{
    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Shown instead of a value the parent never gave — one empty cell, not "—".</summary>
    private const string Blank = "";

    private readonly SurveySubmissionQuery _query = new(db);

    /// <summary>
    /// <c>GET /api/admin/survey-submissions</c> — one page of the register
    /// (§5.3). All filters are optional; with none at all the answer is the
    /// whole register, newest first.
    /// </summary>
    /// <param name="filter">
    /// <c>surveyId</c>, <c>from</c>, <c>to</c> ("YYYY-MM-DD", both inclusive),
    /// <c>status</c>, <c>q</c>, <c>page</c> (1-based), <c>pageSize</c>
    /// (default 50, capped at 200).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<SurveySubmissionPageDto>> List(
        [FromQuery] SurveySubmissionFilter filter, CancellationToken ct = default)
    {
        if (Invalid(filter) is { } error) return error;
        return await _query.PageAsync(filter, ct);
    }

    /// <summary>
    /// <c>GET /api/admin/survey-submissions/{id}</c> — one submission with
    /// every value it carries, ip and user agent included (§5.3). This is the
    /// ONLY place either of those two is exposed: they stay out of the list
    /// and out of the export (§4.2).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SurveySubmissionDetailDto>> Detail(
        Guid id, CancellationToken ct = default)
    {
        var row = await _query.FindAsync(id, ct);

        // No machine `code` here on purpose: §5.6 lists every code this module
        // may answer with, and none of them means "no such submission". The
        // client branches on the 404 itself.
        return row is null
            ? NotFound(new { message = "Topshirilgan ariza topilmadi" })
            : row;
    }

    /// <summary>
    /// <c>GET /api/admin/survey-submissions/export</c> — the WHOLE filter as
    /// .xlsx (§5.3), not the page on screen: the browser holds 50 rows, the
    /// officer wants the season.
    ///
    /// <para>
    /// <b>Built on the server</b>, with <see cref="ExcelExport"/>, like every
    /// other export in this repository (<c>REMAINING-PARITY.md</c> §1b) — the
    /// plain text builder, the same one the certificates and pupils registers
    /// use, because there is no money on this sheet and nothing to total.
    /// </para>
    /// <para>
    /// <b>No ip, no user agent</b> (§4.2). They exist for abuse triage and are
    /// shown in the detail drawer; a downloadable file of visitors' IP
    /// addresses is a different thing entirely, and nobody asked for it.
    /// </para>
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] SurveySubmissionFilter filter, CancellationToken ct = default)
    {
        if (Invalid(filter) is { } error) return error;

        var result = await _query.ExportAsync(filter, ct);

        string[] headers =
        [
            "Sana", "Ariza", "Ota-ona F.I.SH", "Telefon", "O'quvchi",
            "Sinf", "Jinsi", "O'quvchi telefoni", "Holati", "Bosqich",
        ];

        var data = result.Rows.Select(r => (IReadOnlyList<string>)new[]
        {
            // The school reads this file, so the timestamp is in the school's
            // own timezone and in the format the screen prints — not the UTC
            // instant the column stores.
            AppClock.ToLocal(r.CreatedAt).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            r.SurveyName,
            r.ParentFullName,
            r.ParentPhone,
            r.StudentFullName ?? Blank,
            GradeLabel(r.StudentGrade),
            GenderLabel(r.StudentGender),
            r.StudentPhone ?? Blank,
            StatusLabel(r.Status),
            r.LeadStageTitle ?? Blank,
        });

        // The cap bit. Say so IN the file: a truncated export that looks whole
        // is how a family stops being called back.
        if (result.Rows.Count < result.Total)
        {
            data = data.Append(new[]
            {
                $"Ro'yxat {SurveySubmissionQuery.MaxExportRows} qator bilan cheklandi "
                    + $"(filtrga mos jami {result.Total} ta). Sana oralig'ini toraytiring.",
                Blank, Blank, Blank, Blank, Blank, Blank, Blank, Blank, Blank,
            });
        }

        var bytes = ExcelExport.Build("Topshirilgan arizalar", headers, data);
        return File(bytes, XlsxMime, $"topshirilgan-arizalar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Checks the filter and returns a ready 400, or <c>null</c> when it is
    /// sound.
    ///
    /// <para>
    /// An unknown value is refused rather than ignored, for the reason
    /// <see cref="TransactionJournalController"/> gives: somebody who writes
    /// <c>status=takror</c> would otherwise get the FULL register back and
    /// read it as "there are no duplicates".
    /// </para>
    /// <para>
    /// The code is <c>validation</c> — §5.6 is the complete list of codes this
    /// module answers with, and that is the one for a refused input.
    /// </para>
    /// </summary>
    private ActionResult? Invalid(SurveySubmissionFilter filter)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (SurveySubmissionQuery.Clean(filter.Status) is { } status
            && !SurveySubmissionStatus.All.Contains(status, StringComparer.Ordinal))
        {
            errors["status"] = $"Noma'lum holat: '{status}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", SurveySubmissionStatus.All)}.";
        }

        if (filter.From is { } from && filter.To is { } to && to < from)
        {
            errors["to"] = $"Davr teskari: {from:yyyy-MM-dd} dan {to:yyyy-MM-dd} gacha.";
        }

        return errors.Count == 0
            ? null
            : BadRequest(new SurveySubmissionErrorDto("validation", "Ma'lumotlarni tekshiring", errors));
    }

    /// <summary>
    /// The same words the screen prints (<c>submissionLabels.ts</c>): one
    /// status must not be "Takror" in the table and "duplicate" in the file
    /// the officer sends on.
    /// </summary>
    private static string StatusLabel(string status) => status switch
    {
        SurveySubmissionStatus.Lead => "Lid yaratildi",
        SurveySubmissionStatus.Duplicate => "Takror",
        _ => status,
    };

    private static string GenderLabel(string? gender) => gender switch
    {
        SurveyGender.Male => "O'g'il bola",
        SurveyGender.Female => "Qiz bola",
        null => Blank,
        _ => gender,
    };

    /// <summary>
    /// <c>0</c> is nol sinf, a real grade, never "unknown" (§2.1) — so it is
    /// spelled out, and an empty cell is what "not asked" looks like.
    /// </summary>
    private static string GradeLabel(short? grade) => grade switch
    {
        null => Blank,
        0 => "Nol sinf",
        _ => $"{grade}-sinf",
    };
}
