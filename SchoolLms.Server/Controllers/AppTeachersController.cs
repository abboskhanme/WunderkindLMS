using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin "Ilova → O'qituvchilar" bo'limi — o'qituvchilar ilova faolligi (birinchi/oxirgi login) va
/// oxirgi faol qurilmasi (qurilma nomi, platforma, app_id). "Ota-onalar" bo'limiga o'xshash.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("app")]
[Route("api/admin/app/teachers")]
public class AppTeachersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TeacherAppRowDto>>> GetAll()
    {
        var teachers = await db.Teachers.Where(t => !t.IsArchived).ToListAsync();
        var userIds = teachers.Where(t => t.UserId != null).Select(t => t.UserId!).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u);
        // Qurilma (push) o'rniga — Telegram'ga ulanganmi (2026-10-11: push olib tashlandi).
        var teacherIds = teachers.Select(t => t.Id).ToList();
        var telegramLinked = (await db.TelegramAccounts.Where(a => userIds.Contains(a.UserId)).Select(a => a.UserId).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        var registered = (await db.TelegramRegistrations
                .Where(r => r.TeacherId != null && teacherIds.Contains(r.TeacherId)).Select(r => r.TeacherId!).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        return teachers.Select(t =>
        {
            string? first = null, last = null;
            if (t.UserId is not null && users.TryGetValue(t.UserId, out var u)) { first = u.FirstLoginAt; last = u.LastLoginAt; }
            return new TeacherAppRowDto(
                t.Id, t.FullName, t.Phone ?? "",
                first is not null, first, last,
                registered.Contains(t.Id) || (t.UserId is not null && telegramLinked.Contains(t.UserId)));
        })
        .OrderByDescending(r => r.LastSeenAt, StringComparer.Ordinal)
        .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }
}
