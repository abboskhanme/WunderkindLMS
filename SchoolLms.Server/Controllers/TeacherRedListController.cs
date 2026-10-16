using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  "Sinfim · Qizil ro'yxat" — the homeroom teacher's side of the red list
//  (docs/modules/attendance-red-list.md, 2026-10-15). Mini App and teacher portal.
//
//    GET  /api/teacher/homeroom/red-list                    — open cases of my own pupils
//    POST /api/teacher/homeroom/red-list/{caseId}/actions   — status + comment (+ call-back day)
//
//  "My pupils" = the classes I lead and the track groups I teach (AttendanceRedListService.TeacherScopeAsync),
//  the same circle as the homeroom debts card. A case of any other pupil answers 404 — as if it did not exist.
// ===========================================================================

[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/homeroom/red-list")]
public class TeacherRedListController(AppDbContext db, TelegramService telegram) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TeacherRedListDto>> List(CancellationToken ct)
    {
        var teacher = await MeAsync(ct);
        if (teacher is null) return NotFound();
        return await new AttendanceRedListService(db, telegram).TeacherListAsync(teacher.Id, ct);
    }

    [HttpPost("{caseId:guid}/actions")]
    public async Task<ActionResult<AttendanceCaseDetailDto>> AddAction(
        Guid caseId, AddAttendanceCaseActionRequest req, CancellationToken ct)
    {
        var teacher = await MeAsync(ct);
        if (teacher is null) return NotFound();
        try
        {
            return await new AttendanceRedListService(db, telegram).TeacherAddActionAsync(teacher, caseId, req, ct);
        }
        catch (AttendanceCaseException e)
        {
            return StatusCode(e.Status, new { message = e.Message });
        }
    }

    private async Task<Teacher?> MeAsync(CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return uid is null ? null : await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == uid, ct);
    }
}
