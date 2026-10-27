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
//  "Sinfim · Kelmaganlar" — the homeroom teacher explains a missed day (2026-10-26).
//  Mini App and teacher portal.
//
//    GET /api/teacher/homeroom/absences?date=yyyy-MM-dd            — my pupils who missed that day
//    PUT /api/teacher/homeroom/absences/{studentId}/{date}          — reason and/or comment
//
//  "My pupils" — the red list's circle (HomeroomDirectory.PupilsAsync); anyone else answers 404.
// ===========================================================================

[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/homeroom/absences")]
public class TeacherHomeroomAbsencesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HomeroomAbsencesDto>> List([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var teacher = await MeAsync(ct);
        if (teacher is null) return NotFound();
        try
        {
            return await new HomeroomAbsenceService(db).ListAsync(teacher.Id, date ?? AppClock.Today, ct);
        }
        catch (AttendanceCaseException e)
        {
            return StatusCode(e.Status, new { message = e.Message });
        }
    }

    [HttpPut("{studentId}/{date}")]
    public async Task<ActionResult<HomeroomAbsenceDto>> Save(
        string studentId, DateOnly date, SaveHomeroomAbsenceRequest req, CancellationToken ct)
    {
        var teacher = await MeAsync(ct);
        if (teacher is null) return NotFound();
        try
        {
            return await new HomeroomAbsenceService(db).SaveAsync(teacher, studentId, date, req, ct);
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
