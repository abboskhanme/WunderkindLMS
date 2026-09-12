using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Foydalanuvchining qurilmalariga push yuborish (fire-and-forget).
///
/// <para>
/// Bir xil oltita qator uchta controllerda takrorlangan edi. Yangi kod shu
/// yerdan chaqiradi; FCM sozlanmagan bo'lsa metod jimgina qaytadi — push
/// yo'qligi hech qachon asosiy amalni (to'lov, pickup, baho) to'xtatmasligi kerak.
/// </para>
/// </summary>
public static class AppPush
{
    public static async Task ToUserAsync(
        IAppDbContext db, FcmService fcm, string? userId, string title, string body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return;

        var meta = await db.SchoolMeta.FirstOrDefaultAsync(ct);
        var json = meta?.FcmServiceAccountJson ?? "";
        if (!FcmService.IsConfigured(json)) return;

        var tokens = await db.DeviceTokens.Where(d => d.UserId == userId)
            .Select(d => d.Token).Distinct().ToListAsync(ct);
        if (tokens.Count > 0) _ = fcm.SendAsync(json, tokens, title, body);
    }
}

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

    /// <summary>Sinf rahbariga "ota-ona keldi" push'i. Rahbar yo'q bo'lsa hech narsa qilmaydi.</summary>
    public static async Task NotifyHomeroomAsync(
        IAppDbContext db, FcmService fcm, Student student, CancellationToken ct = default)
    {
        var teacher = await db.Teachers.FirstOrDefaultAsync(
            t => !t.IsArchived && t.HomeroomClass == student.ClassName, ct);
        if (teacher?.UserId is null) return;

        await AppPush.ToUserAsync(db, fcm, teacher.UserId, "Farzandni olib ketish",
            $"{student.FullName} ({student.ClassName}) — ota-ona olib ketishga keldi. Qabul qiling.", ct);
    }
}
