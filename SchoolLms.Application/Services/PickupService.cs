using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// "Farzandimni olishga keldim" so'rovining umumiy mantig'i.
///
/// <para>
/// So'rov KUNLIK: faqat bugungi (o'qish kuni) yozuvi hisobga olinadi, kechagisi
/// qolib ketmaydi. Takror bosilsa yangi qator YARATILMAYDI — bugungi
/// kutilayotgan so'rov qaytariladi, aks holda sinf rahbari ekrani bir xil
/// bolani o'nta qator bo'lib ko'rsatardi.
/// </para>
/// </summary>
public static class PickupService
{
    public static PickupRequestDto ToDto(PickupRequest p) => new(
        p.Id, p.StudentId, p.StudentName, p.ClassName, p.Status,
        p.CreatedAt, p.AcceptedAt, p.AcceptedByName);

    /// <summary>Bugungi oxirgi so'rov (holatidan qat'i nazar). Bo'lmasa null.</summary>
    public static async Task<PickupRequest?> TodayAsync(
        IAppDbContext db, string studentId, CancellationToken ct = default)
    {
        var today = AppClock.Now.ToString("yyyy-MM-dd");
        return await db.PickupRequests
            .Where(p => p.StudentId == studentId && p.CreatedAt.StartsWith(today))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Bugungi KUTILAYOTGAN so'rovni qaytaradi, yo'q bo'lsa yaratadi.
    /// Push yubormaydi — buni chaqiruvchi <see cref="NotifyHomeroomAsync"/> bilan qiladi.
    /// </summary>
    public static async Task<PickupRequest> EnsureTodayAsync(
        IAppDbContext db, Student student, string requestedByUserId, CancellationToken ct = default)
    {
        var today = AppClock.Now.ToString("yyyy-MM-dd");
        var pending = await db.PickupRequests.FirstOrDefaultAsync(
            p => p.StudentId == student.Id && p.Status == "pending" && p.CreatedAt.StartsWith(today), ct);
        if (pending is not null) return pending;

        var created = new PickupRequest
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            ClassName = student.ClassName,
            RequestedByUserId = requestedByUserId,
            Status = "pending",
            CreatedAt = AppClock.Now.ToString("o"),
        };
        db.PickupRequests.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// Sinf rahbar(lar)iga Telegram: "ota-ona keldi" — sinfda bir nechta rahbar bo'lsa hammasiga.
    /// Push olib tashlandi (mijoz, 2026-10-11: "bizda hamma narsa telegram mini app bilan").
    /// Bot sozlanmagan yoki rahbar Telegramga ulanmagan bo'lsa — jim o'tadi; so'rov baribir
    /// yaratiladi va rahbar uni ilovadagi ro'yxatda ko'radi.
    /// </summary>
    public static async Task NotifyHomeroomAsync(
        IAppDbContext db, TelegramService telegram, Student student, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured) return;
        var teacherIds = (await HomeroomDirectory.TeachersOfClassNameAsync(db, student.ClassName, ct))
            .Select(t => t.Id).ToList();
        if (teacherIds.Count == 0) return;
        var chats = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.TeacherId != null && teacherIds.Contains(r.TeacherId))
            .Select(r => r.ChatId).Distinct().ToListAsync(ct);
        var text = $"🚸 Farzandni olib ketish\n\n{student.FullName} ({student.ClassName}) — ota-onasi olib ketishga keldi. "
                   + "Ilovadagi \"Sinf rahbarligi\" bo'limida qabul qiling.";
        foreach (var chat in chats)
            await telegram.SendMessageAsync(chat, text, ct: ct);
    }

    /// <summary>Ota-onaga Telegram (topshirildi / ruxsat berildi). Ulanmagan bo'lsa — jim o'tadi.</summary>
    public static async Task NotifyParentAsync(
        IAppDbContext db, TelegramService telegram, string studentId, string text, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured) return;
        var chats = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.StudentId == studentId && r.TeacherId == null)
            .Select(r => r.ChatId).Distinct().ToListAsync(ct);
        foreach (var chat in chats)
            await telegram.SendMessageAsync(chat, text, ct: ct);
    }
}
