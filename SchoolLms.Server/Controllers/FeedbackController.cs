using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Taklif va shikoyatlar — ota-onalar ilova orqali yuboradi (StudentPortalController), admin
/// shu yerda ko'radi va "hal qilindi" deb belgilaydi.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/feedback")]
public class FeedbackController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<FeedbackDto>>> GetAll(
        [FromQuery] string? type, [FromQuery] string? status)
    {
        var q = db.Feedbacks.AsQueryable();
        if (!string.IsNullOrEmpty(type)) q = q.Where(f => f.Type == type);
        if (!string.IsNullOrEmpty(status)) q = q.Where(f => f.Status == status);
        var list = await q.OrderByDescending(f => f.CreatedAt).Take(300).ToListAsync();

        var ids = list.Select(f => f.StudentId).Distinct().ToList();
        var students = await db.Students.Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync();
        var byId = students.ToDictionary(s => s.Id);

        return list.Select(f => new FeedbackDto(
            f.Id,
            byId.TryGetValue(f.StudentId, out var s) ? s.FullName : "",
            f.ParentName,
            byId.TryGetValue(f.StudentId, out var s2) ? s2.ClassName : "",
            f.Type, f.Text, f.CreatedAt.ToString("o"), f.Status,
            string.IsNullOrEmpty(f.SenderRole) ? "parent" : f.SenderRole,
            f.SenderName, f.ImageUrl)).ToList();
    }

    [HttpPost("{id}/resolve")]
    public async Task<IActionResult> Resolve(string id)
    {
        var f = await db.Feedbacks.FindAsync(id);
        if (f is null) return NotFound();
        f.Status = "resolved";
        await db.SaveChangesAsync();
        return NoContent();
    }
}
