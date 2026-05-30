using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>O'zgarishlar tarixi (audit). Moliyaga oid yozuvlar bo'yicha tarixni qaytaradi.</summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/audit")]
public class AuditController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// O'zgarishlar tarixi. Filtrlar: entityType+entityId (bitta yozuv), studentId, teacherId,
    /// entityType, action, davr (from/to). Hammasi ixtiyoriy — vaqt bo'yicha kamayish tartibida.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> Get(
        [FromQuery] string? entityType, [FromQuery] string? entityId,
        [FromQuery] string? studentId, [FromQuery] string? teacherId,
        [FromQuery] string? action, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int? limit)
    {
        var q = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId)) q = q.Where(a => a.EntityId == entityId);
        if (!string.IsNullOrEmpty(studentId)) q = q.Where(a => a.StudentId == studentId);
        if (!string.IsNullOrEmpty(teacherId)) q = q.Where(a => a.TeacherId == teacherId);
        if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(from)) q = q.Where(a => string.Compare(a.Timestamp, from) >= 0);
        if (!string.IsNullOrEmpty(to)) q = q.Where(a => string.Compare(a.Timestamp, to) <= 0);

        q = q.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id);
        if (limit is > 0) q = q.Take(limit.Value);

        var list = await q.ToListAsync();
        return list.Select(a => new AuditLogDto(
            a.Id, a.EntityType, a.EntityId, a.Action, a.Timestamp,
            a.ActorName, a.Summary, a.Before, a.After,
            a.StudentId, a.TeacherId)).ToList();
    }
}
