using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// <b>Imtihonlar</b> — exams, their lifecycle and their participants
/// (<c>docs/modules/admission-and-testing.md</c> §3.2 screen 6, §5.5–§5.7,
/// §6.3, §8.1). Unit B2.
///
/// <para>
/// <b>Permission — <c>exams</c></b> (§4.4). <see cref="AdminPermAttribute"/>:
/// admin/superadmin always; staff read freely (reads are NOT gated for this
/// module) and need the <c>exams</c> claim to write; teacher, pupil, parent
/// and cashier are refused; anonymous is 401. Adding candidates to an
/// ADMISSION exam additionally needs <c>admission</c> (§6.3) — checked in
/// <see cref="AddParticipants"/>.
/// </para>
///
/// <para>
/// <b>Thin on purpose.</b> Every rule lives in <see cref="ExamService"/>;
/// this class translates HTTP. Errors are <c>{ code, message }</c> (plus
/// <c>errors</c> when several problems were found), with the Uzbek sentence
/// the client shows as is. Sibling controllers: <see cref="ExamTypesController"/>
/// (the type catalogue) and <see cref="ExamResultsController"/> (entry grid,
/// import, results register). The online-attempt endpoints under
/// <c>/api/admin/exams/participants/{pid}/…</c> belong to unit B3.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("exams")]
[Route("api/admin/exams")]
public sealed class ExamsController(AppDbContext db) : ControllerBase
{
    /// <summary>The permission that also gates adding candidates to an admission exam (§6.3).</summary>
    public const string AdmissionPerm = "admission";

    /// <summary><c>GET /api/admin/exams</c> — paged register (§6 envelope), newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<ExamPageDto<ExamRowDto>>> List(
        [FromQuery] ExamListQuery query, CancellationToken ct = default)
    {
        var result = await ExamService.ListAsync(db, query, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary><c>GET /api/admin/exams/{id}</c> — the exam with its sections and counts.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExamDto>> Get(string id, CancellationToken ct = default)
    {
        var exam = await ExamService.GetAsync(db, id, ct);
        return exam is null
            ? NotFound(new { code = "exam_not_found", message = ExamService.ExamNotFoundMessage })
            : exam;
    }

    /// <summary><c>POST /api/admin/exams</c> — a new draft. 200 with the full <see cref="ExamDto"/>.</summary>
    [HttpPost]
    public async Task<ActionResult<ExamDto>> Create(ExamUpsertRequest request, CancellationToken ct = default)
    {
        var result = await ExamService.CreateAsync(db, Actor, request, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary>
    /// <c>PUT /api/admin/exams/{id}</c>. 409 when the sections are frozen
    /// (published, or a draft with typed results) and the request changes them.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExamDto>> Update(
        string id, ExamUpsertRequest request, CancellationToken ct = default)
    {
        var result = await ExamService.UpdateAsync(db, Actor, id, request, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary><c>POST /api/admin/exams/{id}/publish</c> — 409 with every §8.1 reason that fails.</summary>
    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<ExamDto>> Publish(string id, CancellationToken ct = default)
    {
        var result = await ExamService.PublishAsync(db, Actor, id, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/cancel</c> <c>{ reason }</c> — unfinished
    /// participants become <c>cancelled</c>, live invitations are revoked (§5.5).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ExamDto>> Cancel(
        string id, ExamCancelRequest request, CancellationToken ct = default)
    {
        var result = await ExamService.CancelAsync(db, Actor, id, request.Reason, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary><c>GET /api/admin/exams/{id}/participants</c> — paged, by class then name.</summary>
    [HttpGet("{id:guid}/participants")]
    public async Task<ActionResult<ExamPageDto<ExamParticipantRowDto>>> Participants(
        string id, [FromQuery] ExamParticipantListQuery query, CancellationToken ct = default)
    {
        var result = await ExamService.ListParticipantsAsync(db, id, query, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/participants</c> <c>{ leadIds?, studentIds?, classIds? }</c>
    /// → <c>{ added, skipped }</c>. Idempotent.
    /// </summary>
    [HttpPost("{id:guid}/participants")]
    public async Task<ActionResult<ExamAddParticipantsResultDto>> AddParticipants(
        string id, ExamAddParticipantsRequest request, CancellationToken ct = default)
    {
        var result = await ExamService.AddParticipantsAsync(
            db, Actor, id, request, callerHasAdmission: User.HasPerm(AdmissionPerm), ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary>
    /// <c>DELETE /api/admin/exams/{id}/participants/{pid}</c> — 204; 409 once an
    /// online attempt exists, or when the exam is closed or cancelled.
    /// </summary>
    [HttpDelete("{id:guid}/participants/{pid:guid}")]
    public async Task<IActionResult> RemoveParticipant(string id, string pid, CancellationToken ct = default)
    {
        var error = await ExamService.RemoveParticipantAsync(db, Actor, id, pid, ct);
        return error is null ? NoContent() : Fail(error);
    }

    // ------------------------------------------------------------------

    private ExamActor Actor => new(
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
        User.FindFirst(ClaimTypes.Name)?.Value ?? "Tizim");

    private ObjectResult Fail(ExamError error) => StatusCode(error.Status, error.ToBody());
}
