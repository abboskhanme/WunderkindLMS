using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin panel qo'ng'irog'i (topbar) uchun bildirishnomalar.
///
/// <para>Alohida "Notifications" jadvali YO'Q — ro'yxat mavjud ma'lumotlardan hisoblanadi
/// (hal qilinmagan taklif/shikoyat, kutilayotgan pickup, yangi chat xabari, bugungi tug'ilgan
/// kunlar). Shuning uchun hech qanday migratsiya yoki trigger kerak emas: qayerda voqea sodir
/// bo'lsa, u avtomatik shu ro'yxatga tushadi.</para>
///
/// <para>"O'qildi" holati foydalanuvchi bo'yicha bitta vaqt belgisi
/// (<see cref="UserSettings.NotificationsReadAt"/>) bilan saqlanadi: undan keyin yaratilgan
/// har qanday element "yangi" hisoblanadi.</para>
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/notifications")]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    /// <summary>Ro'yxatda ko'rsatiladigan eng ko'p element soni.</summary>
    private const int MaxItems = 40;

    /// <summary>Chat xabarlari shu kundan orqasi olinmaydi (ro'yxat cheksiz o'smasligi uchun).</summary>
    private const int ChatLookbackDays = 7;

    private string? MyUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>Barcha bildirishnomalar + o'qilmaganlar soni.</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationListDto>> Get()
    {
        var readAt = await ReadAtAsync();
        var items = await BuildAsync();

        var result = items
            .OrderByDescending(i => i.CreatedAt)
            .Take(MaxItems)
            .Select(i => i with { IsNew = readAt is null || i.CreatedAt > readAt })
            .ToList();

        return new NotificationListDto(result, result.Count(i => i.IsNew));
    }

    /// <summary>Faqat o'qilmaganlar soni — qo'ng'iroq nishonini tez yangilash uchun.</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount()
    {
        var readAt = await ReadAtAsync();
        var items = await BuildAsync();
        return new UnreadCountDto(items.Count(i => readAt is null || i.CreatedAt > readAt));
    }

    /// <summary>Hammasini o'qilgan deb belgilash (joriy vaqt yoziladi).</summary>
    [HttpPost("read")]
    public async Task<IActionResult> MarkRead()
    {
        var uid = MyUserId;
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var s = await db.UserSettings.FirstOrDefaultAsync(x => x.UserId == uid);
        if (s is null)
        {
            s = new UserSettings { UserId = uid };
            db.UserSettings.Add(s);
        }
        s.NotificationsReadAt = AppClock.Now;
        s.UpdatedAt = AppClock.Now;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ------------------------------------------------------------------ ichki

    private async Task<DateTime?> ReadAtAsync()
    {
        var uid = MyUserId;
        if (string.IsNullOrEmpty(uid)) return null;
        return await db.UserSettings.Where(x => x.UserId == uid)
            .Select(x => x.NotificationsReadAt).FirstOrDefaultAsync();
    }

    /// <summary>Barcha manbalardan bildirishnomalarni yig'adi (tartiblanmagan).</summary>
    private async Task<List<NotificationDto>> BuildAsync()
    {
        var now = AppClock.Now;
        var items = new List<NotificationDto>();

        // 1) Hal qilinmagan taklif va shikoyatlar.
        var feedbacks = await db.Feedbacks.Where(f => f.Status == "new")
            .OrderByDescending(f => f.CreatedAt).Take(MaxItems).ToListAsync();
        items.AddRange(feedbacks.Select(f => new NotificationDto(
            Id: "feedback:" + f.Id,
            Kind: f.Type == "complaint" ? "complaint" : "suggestion",
            Title: f.Type == "complaint" ? "Yangi shikoyat" : "Yangi taklif",
            Text: Short(string.IsNullOrWhiteSpace(f.SenderName) ? f.Text : $"{f.SenderName}: {f.Text}"),
            CreatedAt: f.CreatedAt,
            Link: "/admin/boshqaruv/feedback")));

        // 2) Kutilayotgan pickup so'rovlari ("farzandimni olishga keldim").
        var pickups = await db.PickupRequests.Where(p => p.Status == "pending")
            .OrderByDescending(p => p.CreatedAt).Take(MaxItems).ToListAsync();
        items.AddRange(pickups
            .Select(p => new { p, at = ParseDate(p.CreatedAt, now) })
            .Select(x => new NotificationDto(
                Id: "pickup:" + x.p.Id,
                Kind: "pickup",
                Title: "Farzandini olishga kelishdi",
                Text: $"{x.p.StudentName} · {x.p.ClassName} — sinf rahbari javobini kutmoqda",
                CreatedAt: x.at,
                Link: "/admin/students")));

        // 3) Yangi chat xabarlari (o'zim yozganlari hisobga olinmaydi).
        var uid = MyUserId ?? "";
        var since = now.AddDays(-ChatLookbackDays);
        var chats = await db.ChatMessages
            .Where(m => m.CreatedAt >= since && m.SenderUserId != uid)
            .OrderByDescending(m => m.CreatedAt).Take(MaxItems).ToListAsync();
        items.AddRange(chats.Select(m => new NotificationDto(
            Id: "chat:" + m.Id,
            Kind: "chat",
            Title: $"{m.ClassName} — yangi xabar",
            Text: Short($"{m.SenderName}: {m.Text}"),
            CreatedAt: m.CreatedAt,
            Link: "/admin/messages")));

        // 4) Bugungi tug'ilgan kunlar (arxivlanmagan o'quvchilar).
        var mmdd = now.ToString("MM-dd");
        var birthdays = await db.Students
            .Where(s => !s.IsArchived && s.BirthDate.Length >= 10 && s.BirthDate.Substring(5, 5) == mmdd)
            .Select(s => new { s.Id, s.FullName, s.ClassName }).Take(MaxItems).ToListAsync();
        items.AddRange(birthdays.Select(s => new NotificationDto(
            // Kun bo'yicha barqaror id — bir kunda bir marta "yangi" bo'ladi.
            Id: $"birthday:{s.Id}:{now:yyyy-MM-dd}",
            Kind: "birthday",
            Title: "Bugun tug'ilgan kun",
            Text: $"{s.FullName} · {s.ClassName}",
            CreatedAt: now.Date,
            Link: "/admin/students/" + s.Id)));

        return items;
    }

    /// <summary>Uzun matnni ro'yxat uchun qisqartiradi.</summary>
    private static string Short(string text, int max = 90)
    {
        var t = (text ?? "").Replace('\n', ' ').Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }

    /// <summary>Matn sanani (ISO) o'qiydi; bo'lmasa joriy vaqtni qaytaradi.</summary>
    private static DateTime ParseDate(string value, DateTime fallback) =>
        DateTime.TryParse(value, out var d) ? d : fallback;
}
