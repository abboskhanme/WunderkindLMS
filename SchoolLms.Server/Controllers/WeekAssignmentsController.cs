using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("schedule")]
[Route("api/admin/classes/{classId}/week-assignments")]
public class WeekAssignmentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WeekAssignmentDto>>> Get(string classId, [FromQuery] int quarter)
    {
        return await db.WeekAssignments
            .Where(a => a.ClassId == classId && a.Quarter == quarter)
            .OrderBy(a => a.Week)
            .Select(a => new WeekAssignmentDto(a.Week, a.TemplateId))
            .ToListAsync();
    }

    [HttpPut]
    public async Task<IActionResult> Save(string classId, SaveWeekAssignmentsRequest req)
    {
        var existing = db.WeekAssignments.Where(a => a.ClassId == classId && a.Quarter == req.Quarter);
        db.WeekAssignments.RemoveRange(existing);
        db.WeekAssignments.AddRange(req.Assignments.Select(a => new WeekAssignment
        {
            ClassId = classId,
            Quarter = req.Quarter,
            Week = a.Week,
            TemplateId = a.TemplateId,
        }));
        await db.SaveChangesAsync();
        return NoContent();
    }
}
