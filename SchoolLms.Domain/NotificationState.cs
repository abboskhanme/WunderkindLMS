namespace SchoolLms.Domain;

/// <summary>
/// Bitta foydalanuvchining bitta bildirishnomaga munosabati — qachon o'qigani va
/// o'chirgani (mijoz, 2026-09-22: "o'qilganlari 1 kundan keyin o'chib ketsin, qo'lda
/// ham, belgilab o'chirish mumkin bo'lsin").
///
/// <para>
/// Bildirishnomaning o'zi SAQLANMAYDI — u mavjud ma'lumotlardan yig'iladi
/// (<c>NotificationsController</c>) va barqaror matnli id'ga ega
/// (<c>"feedback:&lt;id&gt;"</c>, <c>"chat:&lt;id&gt;"</c> ...). Bu jadval faqat shu id
/// bo'yicha foydalanuvchining holatini yozadi; qatorni o'chirish hech qanday voqeani
/// yo'qotmaydi — bildirishnoma shunchaki yana ko'rinadi.
/// </para>
/// </summary>
public class NotificationState
{
    /// <summary>users.id.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Bildirishnomaning barqaror id'si (masalan <c>"chat:42"</c>).</summary>
    public string NotificationId { get; set; } = string.Empty;

    /// <summary>Qachon o'qildi (mahalliy vaqt). Shundan 1 kun o'tgach ro'yxatdan yo'qoladi.</summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>Qachon qo'lda o'chirildi. null emas — ro'yxatda ko'rinmaydi.</summary>
    public DateTime? DismissedAt { get; set; }
}
