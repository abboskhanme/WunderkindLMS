using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin "Ota-onalar" bo'limi — har bir o'quvchi akkaunti oila uchun (parent role tashlanggan).
/// Telefon raqami bo'yicha guruhlangan: bir ota-ona bir nechta farzandga ega bo'lishi mumkin.
/// Ilova aktivlashtirilganligi (birinchi login) va oxirgi kirish vaqti ko'rsatiladi.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/parents")]
public class ParentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ParentRowDto>>> GetAll()
    {
        // Faqat faol (arxivlanmagan) o'quvchilar. UserId bo'lsa login kuzatuvi bor.
        var students = await db.Students
            .Where(s => !s.IsArchived)
            .ToListAsync();

        var userIds = students.Where(s => s.UserId != null).Select(s => s.UserId!).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        // Har foydalanuvchining oxirgi faol qurilmasi (push token).
        var latestDevice = (await db.DeviceTokens.Where(d => userIds.Contains(d.UserId)).ToListAsync())
            .GroupBy(d => d.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastSeenAt).First());

        ParentChildDto ChildOf(Student s)
        {
            string? firstLogin = null;
            string? lastLogin = null;
            var devName = ""; var platform = ""; var appId = "";
            if (s.UserId is not null && users.TryGetValue(s.UserId, out var u))
            {
                firstLogin = u.FirstLoginAt;
                lastLogin = u.LastLoginAt;
            }
            if (s.UserId is not null && latestDevice.TryGetValue(s.UserId, out var d))
            {
                devName = d.DeviceName; platform = d.Platform; appId = d.AppId;
            }
            return new ParentChildDto(s.Id, s.FullName, s.ClassName, firstLogin, lastLogin, devName, platform, appId);
        }

        // Guruhlash kaliti: telefon (raqamlar normallashtirilgan) yoki nom (telefon bo'sh bo'lsa).
        string Key(Student s)
        {
            var phone = new string((s.ParentPhone ?? "").Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(phone)) return "tel:" + phone;
            return "name:" + (s.ParentFullName ?? "").Trim().ToLowerInvariant();
        }

        var groups = students
            .GroupBy(Key)
            .Select(g =>
            {
                var first = g.First();
                var children = g.Select(ChildOf).ToList();
                var firstLogins = children.Where(c => c.FirstLoginAt != null).Select(c => c.FirstLoginAt!).ToList();
                var lastLogins = children.Where(c => c.LastLoginAt != null).Select(c => c.LastLoginAt!).ToList();
                var activatedAt = firstLogins.Count > 0 ? firstLogins.Min(StringComparer.Ordinal) : null;
                var lastSeenAt = lastLogins.Count > 0 ? lastLogins.Max(StringComparer.Ordinal) : null;
                // Oxirgi faol qurilma (farzandlar bo'yicha).
                var dev = g.Where(s => s.UserId != null && latestDevice.ContainsKey(s.UserId!))
                    .Select(s => latestDevice[s.UserId!])
                    .OrderByDescending(d => d.LastSeenAt).FirstOrDefault();
                return new ParentRowDto(
                    first.ParentFullName ?? "",
                    first.ParentPhone ?? "",
                    children.Count,
                    activatedAt != null,
                    activatedAt,
                    lastSeenAt,
                    children,
                    dev?.DeviceName ?? "",
                    dev?.Platform ?? "");
            })
            // Oxirgi kirgani eng yangi bo'lganlari yuqorida.
            .OrderByDescending(p => p.LastSeenAt, StringComparer.Ordinal)
            .ThenBy(p => p.FullName)
            .ToList();

        return groups;
    }
}
