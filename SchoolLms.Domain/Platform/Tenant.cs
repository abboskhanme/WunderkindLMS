namespace SchoolLms.Domain.Platform;

/// <summary>Maktab (tenant) holatlari.</summary>
public static class TenantStatus
{
    /// <summary>Yangi yaratilmoqda (qisqa muddat).</summary>
    public const string Provisioning = "provisioning";
    /// <summary>Ishlamoqda — subdomen orqali kirish mumkin.</summary>
    public const string Active = "active";
    /// <summary>Vaqtincha to'xtatilgan — subdomen 403 qaytaradi.</summary>
    public const string Suspended = "suspended";
}

/// <summary>
/// Maktab reestr yozuvi. Shared-DB modelida barcha maktab ma'lumotlari BITTA bazada turadi va
/// har bir qator <c>TenantId</c> (= shu <see cref="Id"/>) bilan belgilanadi. Subdomen
/// (<see cref="Slug"/>) orqali murojaat qilinganda joriy tenant aniqlanib, global query filter
/// faqat o'sha maktab qatorlarini ko'rsatadi.
/// </summary>
public class Tenant
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Ko'rinadigan nom (masalan "1-son maktab").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Subdomen yorlig'i, DNS-safe (masalan "school1"). Unikal.</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary><see cref="TenantStatus"/> qiymatlaridan biri.</summary>
    public string Status { get; set; } = TenantStatus.Active;
    /// <summary>Maktab superadmini logini — ma'lumot uchun.</summary>
    public string SuperAdminEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;

    // ----- Obuna (Control Plane / loyiha boshlig'i boshqaradi) -----

    /// <summary>
    /// Maktab foydalana oladigan bo'limlar (modullar) — <see cref="AdminModules"/> kalitlari.
    /// BO'SH ro'yxat = CHEKLOVSIZ (hamma bo'lim ochiq) — eski maktablar bilan moslik uchun.
    /// Aks holda faqat shu ro'yxatdagi bo'limlar maktab admin/superadminiga ko'rinadi.
    /// EF Core 8 primitive collection (JSON).
    /// </summary>
    public List<string> EnabledModules { get; set; } = new();

    /// <summary>
    /// Obuna boshlanish sanasi (kun boshi, mahalliy). <c>null</c> = pastki chegara yo'q.
    /// Hozir bu sanadan oldin bo'lsa maktab hali faollashmagan deb bloklanadi.
    /// </summary>
    public DateTime? SubscriptionStartsAt { get; set; }

    /// <summary>
    /// Obuna tugash sanasi (kun oxiri, mahalliy). <c>null</c> = muddatsiz. O'tib ketgan bo'lsa
    /// maktab to'xtatilgan kabi bloklanadi (subdomen 403), loyiha boshlig'i uzaytirmaguncha.
    /// Maktab obunasi <c>[SubscriptionStartsAt .. SubscriptionEndsAt]</c> oralig'ida ishlaydi.
    /// </summary>
    public DateTime? SubscriptionEndsAt { get; set; }

    /// <summary>Davr uchun yagona obuna narxi (so'm) — ma'lumot/hisob uchun.</summary>
    public decimal SubscriptionPrice { get; set; }
}
