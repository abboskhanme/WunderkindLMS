using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Moliya sozlamalari — finance-parity.md §2.14 (F14.01). Vazifa: F14.01.
// ===========================================================================
//
//  NEGA ALOHIDA XIZMAT, `InvoiceService` GA QO'SHILMADI
//  -----------------------------------------------------
//  `InvoiceService.SettingsAsync` va `ExpenseService.ThresholdAsync` sozlamani
//  FAQAT o'qiydi — ular hisoblash uchun sukut qiymatlarga muhtoj, tahrirlash
//  ular vazifasi emas. Yozish tomoni (bu fayl) ular bilan bog'liq emas:
//  o'zining validatsiyasi, ikki qavatli nazorati va audit yozuvi bor. Ikkalasi
//  bir joyda bo'lganda "InvoiceService nega sozlamani TAHRIRLAYDI" degan savol
//  tug'ilardi — SPEC §2.2 xizmatlar aniq bitta vazifaga ega bo'lishini talab
//  qiladi.
//
//  IKKI QAVATLI NAZORAT — ENDPOINT DARAJASIDAN NOZIKROQ (SPEC §4.5)
//  -------------------------------------------------------------------
//  `FinanceMatrix` (`FinanceRoleAttribute.cs`) bitta amalga BITTA rol
//  to'plamini bog'laydi — `ManageBillingSettings` esa admin VA direktorga
//  ochiq. Ammo finance-parity.md §2.14.3 (F14.01) chiqim tasdiq chegarasini
//  ALOHIDA qoidaga oladi: "superadmin only for the threshold — it is a
//  dual-control parameter". Bu maydon darajasidagi cheklov — matritsa buni
//  ifodalay olmaydi (bitta qator = bitta amal), shuning uchun
//  `DiscountService.ApproveAsync` dagi "o'z-o'zini tasdiqlash" tekshiruvi
//  kabi, bu yerda ham QOIDA XIZMAT QATLAMIDA yashaydi: <see cref="UpdateAsync"/>
//  `actorIsDirector` ni ALOHIDA parametr sifatida oladi (controller
//  `User.IsInRole(Roles.SuperAdmin)` dan beradi) va faqat CHEGARA haqiqatan
//  o'zgarganda tekshiradi — admin sozlamani ochib, chegarani TEGMAY saqlasa,
//  so'rov muvaffaqiyatli o'tadi.
//
//  OLDINGA QARAB — ORQAGA EMAS (topshiriq: "moves money forward only")
//  ---------------------------------------------------------------------
//  Bu xizmat FAQAT `billing_settings` qatorini yangilaydi. `invoices` yoki
//  `expenses` ga HECH QACHON tegmaydi, va bu ataylab qasddan qilingan tanlov
//  emas — tizim bunga MUHTOJ EMAS, chunki ikkala iste'molchi allaqachon
//  "oldinga qarab" ishlaydi:
//    * `InvoiceService.AccrueMonthAsync` `due_on` ni HISOBLAYDI va uni
//      `Invoice.DueOn` ustuniga YOZADI (bir marta, hisoblash paytida). Keyin
//      `payment_due_day` o'zgarsa, eski qatorlarning `due_on`'i BUTUNLAY
//      tegilmaydi — ular o'sha kuni saqlab qoladi.
//    * `InvoiceService.IsOverdue` `overdue_after_day` dan chegarani hisoblasa
//      ham, uni HECH QACHON `invoice.DueOn` dan OLDINGA surmaydi
//      (`if (boundary < invoice.DueOn) boundary = invoice.DueOn;`) — ya'ni
//      sozlama qisqartirilsa ham, eski hisob-faktura birdan "muddati o'tgan"
//      bo'lib qolmaydi.
//    * `ExpenseService.ThresholdAsync` chegarani FAQAT yangi chiqim
//      yaratilayotganda o'qiydi; tasdiq kutayotgan eski chiqim o'z holatini
//      saqlaydi — chegarani oshirib qo'yish uni "avtomatik tasdiqlangan"
//      qilib qo'ymaydi, chunki `RequiresApproval` allaqachon `expenses`
//      qatorida yozilgan (o'zgarmas yozuv, SPEC §4.1).
//  Ya'ni "orqaga ta'sir qilmaslik" bu xizmatning YANGI kodi emas — bu ikkala
//  eski xizmatning ALLAQACHON bor invarianti, va bu yerda faqat ISHONCH
//  bilan qayd etilgan.

/// <summary>
/// Moliya sozlamalarini o'qish va tahrirlash (F14.01, mijoz javobi SPEC §8.1 Q6).
/// </summary>
public interface IBillingSettingsService
{
    /// <summary>Joriy sozlama. Qator yo'q bo'lsa (seed o'chirilgan) — domendagi sukut qiymatlar.</summary>
    Task<BillingSettingsDto> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Sozlamani yangilaydi. Uchala qiymat ham server tomonda TEKSHIRILADI —
    /// frontend validatsiyasi shunchaki qulaylik, himoya emas.
    /// </summary>
    /// <param name="request">Yangi qiymatlar (uchtasi ham — forma to'liq yuboradi).</param>
    /// <param name="actorId">JWT'dagi foydalanuvchi id'si (SPEC §4.4).</param>
    /// <param name="actorIsDirector">
    /// Chaqiruvchi <c>superadmin</c>mi. <c>ExpenseApprovalThreshold</c> HAQIQATAN
    /// o'zgarayotganda majburiy — aks holda <see cref="BillingFault.Forbidden"/>.
    /// </param>
    Task<BillingSettingsDto> UpdateAsync(
        UpdateBillingSettingsRequest request, string actorId, bool actorIsDirector,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IBillingSettingsService"/>
public sealed class BillingSettingsService(IAppDbContext db, AuditService audit) : IBillingSettingsService
{
    private const string AuditEntity = "BillingSettings";

    public async Task<BillingSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var settings = await db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new BillingSettings();
        return await ToDtoAsync(settings, ct);
    }

    public async Task<BillingSettingsDto> UpdateAsync(
        UpdateBillingSettingsRequest request, string actorId, bool actorIsDirector,
        CancellationToken ct = default)
    {
        // Baza check constraint'lari bilan AYNAN bir xil (BillingModel.cs
        // ConfigureSettings) — farq shundaki, bu yerdagi xato o'zbekcha va
        // 400 bo'lib qaytadi, bazaniki esa 23514 (SQLSTATE) bo'lardi.
        if (request.PaymentDueDay is < 1 or > 28)
            throw BillingRuleException.Invalid(
                "due_day_range", "To'lov muddati kuni 1 dan 28 gacha bo'lishi kerak.");

        if (request.OverdueAfterDay is < 1 or > 28)
            throw BillingRuleException.Invalid(
                "overdue_day_range", "Muddati o'tgan deb hisoblash kuni 1 dan 28 gacha bo'lishi kerak.");

        if (request.OverdueAfterDay < request.PaymentDueDay)
            throw BillingRuleException.Invalid(
                "overdue_before_due",
                "Muddati o'tgan deb hisoblash kuni to'lov muddati kunidan kichik bo'lishi mumkin emas.");

        if (request.ExpenseApprovalThreshold < 0m)
            throw BillingRuleException.Invalid(
                "negative_threshold", "Chiqim tasdiq chegarasi manfiy bo'lishi mumkin emas.");

        var settings = await db.BillingSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            // Seed qatori yo'q (masalan test bazasi) — birinchi PUT uni yaratadi.
            settings = new BillingSettings { Id = BillingSettings.SingletonId };
            db.BillingSettings.Add(settings);
        }

        // F14.01 — ikki qavatli nazorat: chegara HAQIQATAN o'zgarayotgan
        // bo'lsagina direktor talab qilinadi. Admin qolgan ikki maydonni
        // bemalol o'zgartiradi, chegarani esa joriy qiymati bilan qaytarib
        // yuborishi kerak (forma buni default sifatida yuboradi).
        if (request.ExpenseApprovalThreshold != settings.ExpenseApprovalThreshold && !actorIsDirector)
            throw BillingRuleException.Forbidden(
                "threshold_requires_director",
                "Chiqim tasdiq chegarasini faqat direktor o'zgartira oladi — bu ikki qavatli "
                + "nazorat parametri (SPEC §4.5).");

        var before = new
        {
            settings.PaymentDueDay,
            settings.OverdueAfterDay,
            settings.ExpenseApprovalThreshold,
        };

        settings.PaymentDueDay = request.PaymentDueDay;
        settings.OverdueAfterDay = request.OverdueAfterDay;
        settings.ExpenseApprovalThreshold = request.ExpenseApprovalThreshold;
        settings.UpdatedAt = AppClock.NowInstant;
        settings.UpdatedBy = actorId;

        // Eslatma: bu yozuv FAQAT `billing_settings` qatoriga tegadi.
        // `invoices.due_on` va tasdiq kutayotgan `expenses` qatorlari
        // TEGILMAYDI — fayl boshidagi izohga qarang ("OLDINGA QARAB").
        audit.Record(
            AuditEntity, settings.Id.ToString(), "update",
            $"Moliya sozlamalari o'zgardi: to'lov muddati {before.PaymentDueDay} → {request.PaymentDueDay}-kun, "
            + $"muddati o'tgan kun {before.OverdueAfterDay} → {request.OverdueAfterDay}, "
            + $"chiqim tasdiq chegarasi {AuditService.Money(before.ExpenseApprovalThreshold)} → "
            + $"{AuditService.Money(request.ExpenseApprovalThreshold)} so'm",
            before: before,
            after: new
            {
                request.PaymentDueDay,
                request.OverdueAfterDay,
                request.ExpenseApprovalThreshold,
            });

        await db.SaveChangesAsync(ct);

        return await ToDtoAsync(settings, ct);
    }

    private async Task<BillingSettingsDto> ToDtoAsync(BillingSettings settings, CancellationToken ct)
    {
        string? updatedByName = null;
        if (!string.IsNullOrEmpty(settings.UpdatedBy))
        {
            updatedByName = await db.Users.AsNoTracking()
                .Where(u => u.Id == settings.UpdatedBy)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);
        }

        return new BillingSettingsDto(
            settings.PaymentDueDay,
            settings.OverdueAfterDay,
            settings.ExpenseApprovalThreshold,
            settings.UpdatedAt,
            updatedByName);
    }
}
