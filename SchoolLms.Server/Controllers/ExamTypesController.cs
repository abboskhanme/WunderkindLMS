using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// <b>Imtihon turi</b> — the exam-type catalogue
/// (<c>docs/modules/admission-and-testing.md</c> §3.2 screen 7, §5.4, §6.3). Unit B2.
///
/// <para>
/// <b>Permission — <c>exams</c></b>, reads not gated (§4.4) — the same gate as
/// <see cref="ExamsController"/>. A flat catalogue like <c>EvaluationType</c>:
/// the list is small and returned whole (bare array, inactive included).
/// </para>
///
/// <para>
/// <b>Delete vs deactivate.</b> A type an exam uses is refused with 409 and
/// the sentence tells the user to deactivate it instead; the name is unique
/// case- and whitespace-insensitively (<c>ux_exam_types_name</c>) → 409.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("exams")]
[Route("api/admin/exams/types")]
public sealed class ExamTypesController(AppDbContext db) : ControllerBase
{
    /// <summary><c>GET /api/admin/exams/types</c> — the whole catalogue, by name.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ExamTypeDto>>> List(CancellationToken ct = default) =>
        await ExamService.ListTypesAsync(db, ct);

    /// <summary><c>POST /api/admin/exams/types</c> <c>{ name, description }</c> — 200, always active.</summary>
    [HttpPost]
    public async Task<ActionResult<ExamTypeDto>> Create(ExamTypeCreateRequest request, CancellationToken ct = default)
    {
        var result = await ExamService.CreateTypeAsync(db, Actor, request, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary><c>PUT /api/admin/exams/types/{id}</c> <c>{ name, description, isActive }</c>.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExamTypeDto>> Update(
        string id, ExamTypeUpdateRequest request, CancellationToken ct = default)
    {
        var result = await ExamService.UpdateTypeAsync(db, Actor, id, request, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary><c>DELETE /api/admin/exams/types/{id}</c> — 204, or 409 when an exam uses it.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        var error = await ExamService.DeleteTypeAsync(db, Actor, id, ct);
        return error is null ? NoContent() : Fail(error);
    }

    private ExamActor Actor => new(
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
        User.FindFirst(ClaimTypes.Name)?.Value ?? "Tizim");

    private ObjectResult Fail(ExamError error) => StatusCode(error.Status, error.ToBody());
}
