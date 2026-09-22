using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    /// <summary>O'qilgan bildirishnoma shuncha vaqtdan keyin ro'yxatdan yo'qoladi (mijoz, 2026-09-22).</summary>
    public static readonly TimeSpan ReadRetention = TimeSpan.FromDays(1);

    /// <summary>Bitta so'rovda o'chiriladigan eng ko'p id.</summary>
    private const int MaxDismiss = 200;

    /// <summary>Barcha bildirishnomalar + o'qilmaganlar soni.</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationListDto>> Get()
    {
        var result = (await VisibleAsync())
            .OrderByDescending(i => i.CreatedAt)
            .Take(MaxItems)
            .ToList();

        return new NotificationListDto(result, result.Count(i => i.IsNew));
    }

    /// <summary>Faqat o'qilmaganlar soni — qo'ng'iroq nishonini tez yangilash uchun.</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount() =>
        new UnreadCountDto((await VisibleAsync()).Count(i => i.IsNew));

    /// <summary>
    /// O'qilgan deb belgilash. <c>ids</c> berilsa — faqat o'shalar (bildirishnoma ochilganda);
    /// berilmasa — ko'rinib turganlarning hammasi ("O'qildi" tugmasi). Har biriga O'Z vaqti
    /// yoziladi — "1 kundan keyin yo'qolish" shundan sanaladi.
    ///
    /// <para><c>user_settings.notifications_read_at</c> endi YANGILANMAYDI: u faqat bu jadvaldan
    /// oldingi bildirishnomalar uchun qotgan chegara. Yangilanib tursa, kechroq paydo bo'lgan,
    /// lekin sanasi eski bildirishnoma (masalan orqaga sanalangan yangilik) "o'qilgan" deb
    /// hisoblanib, ko'rinmasdan yo'qolardi.</para>
    /// </summary>
    [HttpPost("read")]
    public async Task<IActionResult> MarkRead(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MarkNotificationsReadRequest? req = null)
    {
        var uid = MyUserId;
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var now = AppClock.Now;
        var legacyReadAt = await ReadAtAsync();
        var items = await BuildAsync();
        var only = req?.Ids is { Count: > 0 } ids ? ids.ToHashSet(StringComparer.Ordinal) : null;
        if (only is { Count: > MaxDismiss })
            return BadRequest(new { message = $"Bir martada ko'pi bilan {MaxDismiss} ta" });

        var targets = items.Where(i => only is null || only.Contains(i.Id)).ToList();
        // Bu jadvaldan OLDIN o'qilganlari — eski chegara vaqti bilan, aks holda ular
        // yana bir kunga qolib ketardi.
        var legacyRead = targets.Where(i => legacyReadAt is { } l && i.CreatedAt <= l).Select(i => i.Id).ToList();
        var fresh = targets.Select(i => i.Id).Except(legacyRead).ToList();

        await UpsertAsync(uid, legacyRead, legacyReadAt, null);
        await UpsertAsync(uid, fresh, now, null);
        await PruneAsync(uid, now);
        return NoContent();
    }

    /// <summary>
    /// Tanlangan bildirishnomalarni o'chirish (bittasi yoki "hammasini belgilab").
    /// Faqat shu foydalanuvchi uchun — voqeaning o'zi (shikoyat, chat ...) tegilmaydi.
    /// Hozir ro'yxatda yo'q id jim o'tkazib yuboriladi.
    /// </summary>
    [HttpPost("dismiss")]
    public async Task<IActionResult> Dismiss([FromBody] DismissNotificationsRequest req)
    {
        var uid = MyUserId;
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var ids = (req.Ids ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest(new { message = "Hech narsa tanlanmagan" });
        if (ids.Count > MaxDismiss)
            return BadRequest(new { message = $"Bir martada ko'pi bilan {MaxDismiss} ta o'chiriladi" });

        var now = AppClock.Now;
        var existing = (await BuildAsync()).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        await UpsertAsync(uid, [.. ids.Where(existing.Contains)], now, now);
        await PruneAsync(uid, now);
        return NoContent();
    }

    /// <summary>
    /// Holat qatorlarini BITTA so'rovda yozadi (<c>INSERT … ON CONFLICT</c>). "O'qib, keyin
    /// qo'shish" ikki parallel so'rovda (ikki tab, yoki o'chirish + ochish) kalit to'qnashuvi
    /// bilan 500 berardi. O'qilgan vaqt bir marta yoziladi (keyingisi uni surmaydi);
    /// o'chirilgan vaqt berilsa — qo'yiladi.
    /// </summary>
    private async Task UpsertAsync(string uid, List<string> ids, DateTime? readAt, DateTime? dismissedAt)
    {
        if (ids.Count == 0) return;
        // Vaqt ustunlari `timestamp without time zone` (mahalliy vaqt) — tur ANIQ beriladi,
        // aks holda Npgsql DateTime'ni timestamptz deb yuboradi va rad etadi.
        static NpgsqlParameter Ts(string name, DateTime? v) =>
            new(name, NpgsqlTypes.NpgsqlDbType.Timestamp) { Value = (object?)v ?? DBNull.Value };
        await db.Database.ExecuteSqlRawAsync(@"
            INSERT INTO notification_states (user_id, notification_id, read_at, dismissed_at)
            SELECT @uid, x, @read, @dismissed
            FROM unnest(@ids) AS x
            ON CONFLICT (user_id, notification_id) DO UPDATE SET
                read_at = COALESCE(notification_states.read_at, EXCLUDED.read_at),
                dismissed_at = COALESCE(EXCLUDED.dismissed_at, notification_states.dismissed_at)",
            new NpgsqlParameter("uid", NpgsqlTypes.NpgsqlDbType.Text) { Value = uid },
            new NpgsqlParameter("ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = ids.ToArray() },
            Ts("read", readAt),
            Ts("dismissed", dismissedAt));
    }

    // ------------------------------------------------------------------ ichki

    /// <summary>
    /// Foydalanuvchiga KO'RINADIGAN bildirishnomalar: o'chirilganlari va o'qilganiga
    /// <see cref="ReadRetention"/> dan ko'p bo'lganlari chiqarilgan, <c>IsNew</c> qo'yilgan.
    /// </summary>
    private async Task<List<NotificationDto>> VisibleAsync()
    {
        var uid = MyUserId ?? "";
        var legacyReadAt = await ReadAtAsync();
        var states = await StatesAsync(uid);
        var cutoff = AppClock.Now - ReadRetention;

        var visible = new List<NotificationDto>();
        foreach (var item in await BuildAsync())
        {
            states.TryGetValue(item.Id, out var st);
            if (st?.DismissedAt is not null) continue;

            var readAt = st?.ReadAt
                ?? (legacyReadAt is { } l && item.CreatedAt <= l ? l : (DateTime?)null);
            if (readAt is { } r && r <= cutoff) continue;

            visible.Add(item with { IsNew = readAt is null });
        }
        return visible;
    }

    /// <summary>Eski umumiy "o'qildi" belgisi — holat qatori yo'q bildirishnomalar uchun.</summary>
    private async Task<DateTime?> ReadAtAsync()
    {
        var uid = MyUserId;
        if (string.IsNullOrEmpty(uid)) return null;
        return await db.UserSettings.Where(x => x.UserId == uid)
            .Select(x => x.NotificationsReadAt).FirstOrDefaultAsync();
    }

    private async Task<Dictionary<string, NotificationState>> StatesAsync(string uid) =>
        string.IsNullOrEmpty(uid)
            ? new Dictionary<string, NotificationState>(StringComparer.Ordinal)
            : await db.NotificationStates.Where(x => x.UserId == uid)
                .ToDictionaryAsync(x => x.NotificationId, StringComparer.Ordinal);

    /// <summary>
    /// Eskirgan holatlarni tozalash: manbalar bir haftadan eskisini baribir qaytarmaydi
    /// (<see cref="ChatLookbackDays"/>), 30 kunlik qator hech qachon kerak bo'lmaydi.
    /// Hal qilinmagan shikoyat kabi uzoq yashovchi bildirishnoma qayta ko'rinsa — u
    /// baribir hal qilinmagan; bu ma'lumot yo'qotish emas.
    /// </summary>
    private async Task PruneAsync(string uid, DateTime now)
    {
        var old = now.AddDays(-30);
        await db.NotificationStates
            .Where(x => x.UserId == uid && (x.DismissedAt ?? x.ReadAt) < old)
            .ExecuteDeleteAsync();
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

        // 5) Ommaviy formadan kelgan yangi arizalar (sales-marketing.md §3.3 N9).
        //    Faqat lid yaratganlari: takroriy topshiriq yangi murojaat emas.
        var sinceInstant = AppClock.NowInstant.AddDays(-ChatLookbackDays);
        var submissions = await (
            from x in db.SurveySubmissions
            join sv in db.Surveys on x.SurveyId equals sv.Id
            where x.CreatedAt >= sinceInstant && x.Status == SurveySubmissionStatus.Lead
            orderby x.CreatedAt descending
            select new { x.Id, x.ParentFirstName, x.ParentLastName, Survey = sv.Name, x.CreatedAt })
            .Take(MaxItems).ToListAsync();
        items.AddRange(submissions.Select(x => new NotificationDto(
            Id: "survey:" + x.Id,
            Kind: "survey",
            Title: "Yangi ariza",
            Text: Short($"{x.ParentFirstName} {x.ParentLastName}".Trim() + " · " + x.Survey),
            CreatedAt: AppClock.ToLocal(x.CreatedAt),
            Link: "/admin/marketing/topshirilganlar")));

        // 6) E'lon qilingan yangiliklar (sales-marketing.md §3.3 N9).
        var news = await db.News
            .Where(n => n.PublishedAt != null && n.PublishedAt >= sinceInstant && n.DeletedAt == null)
            .OrderByDescending(n => n.PublishedAt)
            .Select(n => new { n.Id, n.Title, n.PublishedAt })
            .Take(MaxItems).ToListAsync();
        items.AddRange(news.Select(n => new NotificationDto(
            Id: "news:" + n.Id,
            Kind: "news",
            Title: "Yangilik e'lon qilindi",
            Text: Short(n.Title),
            CreatedAt: AppClock.ToLocal(n.PublishedAt!.Value),
            Link: "/admin/marketing/yangiliklar")));

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
