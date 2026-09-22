using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// <b>Mavsumiy baholash</b> — the teacher panel (docs/modules/admission-and-testing.md
/// §3.4 screen 14, §6.6): the entry grid of the admin screen, limited to the
/// (class, subject) pairs the teacher actually teaches.
///
/// <para>
/// <b>Two gates, both on every call.</b>
/// (1) The section key <see cref="TeacherPermissions.SeasonalMarks"/> on the
/// teacher row — the same kind of switch as <c>journal</c> or <c>messages</c>.
/// (2) For the grid and the save, "teaches that (class, subject)", answered by
/// <see cref="TeacherOwnerAccess.TeachesAsync"/> — the single rule the journal
/// uses on all three surfaces (G-12), so a teacher who may write a lesson's
/// journal cell may assess the same pupils, and nobody else may.
/// The service then also refuses any pupil who is not an active pupil of that
/// class, so the pair check cannot be used to reach another class's pupil.
/// </para>
///
/// <para>
/// Only scope, grid and save exist here (§6.6). The list, inline edit, delete,
/// reports and exports are admin screens; a teacher corrects a mark by saving
/// the grid again, and un-marks a pupil by clearing both fields.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/seasonal-marks")]
public sealed class TeacherSeasonalMarksController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string TeacherNotFoundMessage = "O'qituvchi topilmadi";

    private readonly SeasonalMarkService _marks = new(db, audit);

    private string? Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary><c>GET /api/teacher/seasonal-marks/scope</c> — only the classes and pairs this teacher teaches.</summary>
    [HttpGet("scope")]
    public async Task<ActionResult<SeasonalScopeDto>> Scope(CancellationToken ct = default)
    {
        var teacher = await MeAsync(ct);
        if (teacher is null) return NotFound(new { message = TeacherNotFoundMessage });
        if (!teacher.Permissions.Contains(TeacherPermissions.SeasonalMarks)) return Forbid();

        return await SeasonalMarkHttp.RunAsync(() => _marks.ScopeAsync(classId: null, teacher.Id, ct));
    }

    /// <summary><c>GET /api/teacher/seasonal-marks/students</c> — the grid of one of the teacher's own pairs.</summary>
    [HttpGet("students")]
    public async Task<ActionResult<IReadOnlyList<SeasonalEntryRowDto>>> Students(
        [FromQuery] SeasonalStudentsQuery query, CancellationToken ct = default)
    {
        if (await RefuseAsync(query, ct) is { } refused) return refused;
        return await SeasonalMarkHttp.RunAsync(() => _marks.StudentsAsync(query, ct));
    }

    /// <summary><c>POST /api/teacher/seasonal-marks/bulk</c> — save the grid of one of the teacher's own pairs.</summary>
    [HttpPost("bulk")]
    public async Task<ActionResult<SeasonalBulkResultDto>> Bulk(SeasonalBulkRequest req, CancellationToken ct = default)
    {
        if (await RefuseAsync(req, ct) is { } refused) return refused;
        return await SeasonalMarkHttp.RunAsync(() => _marks.BulkAsync(req, Uid, ct));
    }

    // ------------------------------------------------------------------

    /// <summary>The teacher row behind the token (the permission list lives there).</summary>
    private async Task<Teacher?> MeAsync(CancellationToken ct) =>
        Uid is null ? null : await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == Uid, ct);

    /// <summary>
    /// Both gates for a (class, subject): the section key, then "teaches it".
    /// <c>null</c> = go ahead. A request without a class or subject is a 400 —
    /// it cannot be a pair the teacher teaches, and a 403 would misname the fault.
    /// </summary>
    private async Task<ActionResult?> RefuseAsync(SeasonalStudentsQuery query, CancellationToken ct)
    {
        var teacher = await MeAsync(ct);
        if (teacher is null) return NotFound(new { message = TeacherNotFoundMessage });
        if (!teacher.Permissions.Contains(TeacherPermissions.SeasonalMarks)) return Forbid();

        var classId = SeasonalMarkService.Clean(query.ClassId);
        var subjectId = SeasonalMarkService.Clean(query.SubjectId);
        if (classId is null || subjectId is null)
            return BadRequest(new { message = SeasonalMarkService.ClassAndSubjectRequiredMessage });

        return await TeacherOwnerAccess.TeachesAsync(db, teacher.Id, classId, subjectId, ct) ? null : Forbid();
    }
}
