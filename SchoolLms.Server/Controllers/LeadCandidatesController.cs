using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// <b>Nomzodlar</b> — the admission endpoints that live under the leads path
/// (<c>docs/modules/admission-and-testing.md</c> §6.1, §8.5). Unit B5.
///
/// <para>
/// <b>Why a separate controller on the same route.</b> §6.1 puts these three
/// under <c>/api/admin/leads</c> but gates them on <c>admission</c>, not
/// <c>leads</c>. <see cref="AdminPermAttribute"/> is class-only
/// (<c>AttributeTargets.Class</c>), and <see cref="LeadsController"/>'s class
/// gate is <c>leads</c> — an action added there would need both. So they sit
/// here, and <see cref="LeadsController"/> — whose five board calls the
/// design-frozen Lidlar board depends on — is not touched at all.
/// </para>
///
/// <para>
/// <b>Routes do not collide</b> with <see cref="LeadsController"/>: it has no
/// <c>GET {id}</c>, and <c>{id}/admission</c> / <c>{id}/admission-status</c>
/// are two segments where its <c>PUT/PATCH/DELETE {id}</c> are one.
/// </para>
///
/// <para>
/// <b>Permission — <c>admission</c></b> (§4.4): admin/superadmin always; staff
/// read freely (reads are not gated for candidates) and need <c>admission</c>
/// for the decision; other roles are refused; anonymous is 401.
/// </para>
///
/// <para>
/// Thin on purpose: the rules are in <see cref="CandidateQuery"/> and
/// <see cref="CandidateDecision"/>. Errors are <c>{ code, message }</c>, Uzbek.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("admission")]
[Route("api/admin/leads")]
public sealed class LeadCandidatesController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// <c>GET /api/admin/leads/candidates</c> — paged (§6 envelope), by name.
    /// <c>?page, limit, search, admissionStatus, grade, examId</c>.
    /// </summary>
    [HttpGet("candidates")]
    public async Task<ActionResult<ExamPageDto<CandidateRowDto>>> List(
        [FromQuery] CandidateListQuery query, CancellationToken ct = default)
    {
        var result = await CandidateQuery.ListAsync(db, query, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary><c>GET /api/admin/leads/{id}/admission</c> — the candidate card; 404 once the lead is gone.</summary>
    [HttpGet("{id}/admission")]
    public async Task<ActionResult<CandidateCardDto>> Card(string id, CancellationToken ct = default)
    {
        var card = await CandidateQuery.GetCardAsync(db, id, ct);
        return card is null
            ? NotFound(new { code = CandidateQuery.LeadNotFoundCode, message = CandidateQuery.CandidateNotFoundMessage })
            : card;
    }

    /// <summary>
    /// <c>PATCH /api/admin/leads/{id}/admission-status</c> <c>{ status }</c> → 204.
    /// <c>accepted</c> / <c>rejected</c> only; anything else is 400.
    /// </summary>
    [HttpPatch("{id}/admission-status")]
    public async Task<IActionResult> SetStatus(string id, CandidateStatusRequest request, CancellationToken ct = default)
    {
        var error = await CandidateDecision.SetAsync(db, Actor, id, request, ct);
        return error is null ? NoContent() : Fail(error);
    }

    // ------------------------------------------------------------------

    private ExamActor Actor => new(
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
        User.FindFirst(ClaimTypes.Name)?.Value ?? "Tizim");

    private ObjectResult Fail(ExamError error) => StatusCode(error.Status, error.ToBody());
}
