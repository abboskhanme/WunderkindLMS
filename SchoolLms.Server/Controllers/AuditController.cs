using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'zgarishlar tarixi — one record's or one person's history, for the screens that show it in place
/// (payment history, salary detail, seasonal mark, Moliya → Tarix). The whole log with paging,
/// people and Excel is the audit screen: <see cref="AuditLogController"/>.
/// </summary>
[ApiController]
// Faqat o'qish (GET). Xodim ham ko'radi: ruxsat bor bo'limdagi tarix chiqmay qolmasin (2026-09-26).
// Money rule (2026-10-15): without the finance right finance and salary rows are not returned and
// amounts are stripped from before/after — the same rule as the pupil card (AuditAccess).
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/audit")]
public class AuditController(AppDbContext db) : ControllerBase
{
    /// <summary>Rows returned when the caller gives no <c>limit</c> — the call used to load the whole table.</summary>
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    /// <summary>
    /// O'zgarishlar tarixi. Filtrlar: entityType+entityId (bitta yozuv), studentId, teacherId,
    /// action, davr (from/to), actorId, section, kind, q. Hammasi ixtiyoriy — vaqt bo'yicha kamayish
    /// tartibida, ko'pi bilan <c>limit</c> (sukut bo'yicha 100, eng ko'pi 500) qator.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> Get(
        [FromQuery] string? entityType, [FromQuery] string? entityId,
        [FromQuery] string? studentId, [FromQuery] string? teacherId,
        [FromQuery] string? action, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int? limit,
        [FromQuery] string? actorId = null, [FromQuery] string? section = null,
        [FromQuery] string? kind = null, [FromQuery] string? q = null)
    {
        var money = User.CanSeeMoney();
        var filter = new AuditFilter(entityType, entityId, studentId, teacherId, action, from, to, actorId, section, kind, q);
        var take = Math.Clamp(limit is > 0 ? limit.Value : DefaultLimit, 1, MaxLimit);

        var rows = await AuditAccess.Newest(AuditAccess.Apply(db.AuditLogs.AsNoTracking(), filter, money))
            .Take(take)
            .ToListAsync();
        return rows.Select(a => AuditAccess.ToLogDto(a, money)).ToList();
    }
}
