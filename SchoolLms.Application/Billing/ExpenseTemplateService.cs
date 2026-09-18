using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Rejalashtirilgan chiqim shabloni — F6.01 (finance-parity.md §2.6.3, §3.3 C7)
// ===========================================================================
//
//  "SHABLON — CHIQIM EMAS" — batafsil izoh: SchoolLms.Domain/ExpenseTemplates.cs
//  boshida. Bu xizmat `expenses`/`ledger_entries`ga hech qachon YOZMAYDI — u
//  faqat kataloqni boshqaradi. Haqiqiy chiqimni yozish har doim
//  <see cref="IExpenseService.CreateAsync"/> orqali, alohida, ikkinchi qadam.
//
//  MOLIYAVIY EMAS — TO'LIQ CRUD (PayrollAdjustment.AdjustmentReason naqshi)
//  --------------------------------------------------------------------------
//  Boshqa moliya xizmatlaridan farqli, bu yerda `Update`/`Delete` bor va
//  bo'ladi ham: shablonni o'chirish yoki o'zgartirish hech qanday pul
//  yozuvini yo'qotmaydi (`expense_templates_guards.sql` boshidagi izoh).
// ===========================================================================

/// <summary>Shablon — o'qish uchun. API shakli sozlamalar ekrani va P&amp;L 2.0 ning `planned` tabi bilan kelishilgan (finance-parity.md §2.6.3, F6.01).</summary>
public record ExpenseTemplateDto(
    Guid Id, string Name, string CategoryCode, string CategoryName,
    decimal Amount, int DayOfMonth, bool IsActive);

/// <summary>Yangi shablon. Hammasi majburiy — qisman yaratish yo'q.</summary>
public record CreateExpenseTemplateRequest(
    string Name, string CategoryCode, decimal Amount, int DayOfMonth, bool IsActive);

/// <summary>
/// Shablonni tahrirlash — QISMAN (har bir maydon ixtiyoriy). Berilmagan
/// (<c>null</c>) maydon joriy qiymatida qoladi. Hech narsa berilmasa — hech
/// narsa o'zgarmaydi (xato emas, no-op).
/// </summary>
public record UpdateExpenseTemplateRequest(
    string? Name = null, string? CategoryCode = null, decimal? Amount = null,
    int? DayOfMonth = null, bool? IsActive = null);

public interface IExpenseTemplateService
{
    /// <summary>Hammasi, nom bo'yicha (faol/faolsiz — ekran o'zi filtrlaydi).</summary>
    Task<IReadOnlyList<ExpenseTemplateDto>> ListAsync(CancellationToken ct = default);

    Task<ExpenseTemplateDto> CreateAsync(
        CreateExpenseTemplateRequest request, string actorId, CancellationToken ct = default);

    Task<ExpenseTemplateDto> UpdateAsync(
        Guid id, UpdateExpenseTemplateRequest request, string actorId, CancellationToken ct = default);

    /// <summary>
    /// Haqiqiy o'chirish (soft-delete emas) — jadval moliyaviy emas, ya'ni
    /// SPEC §4.1 ning "faqat qo'shiladi" qoidasi bu yerga tegishli emas
    /// (`expense_templates_guards.sql` boshidagi izoh). O'chirilgan shablon
    /// endi hech qanday eslatma yubormaydi va `planned` tabida ko'rinmaydi.
    /// </summary>
    Task DeleteAsync(Guid id, string actorId, CancellationToken ct = default);
}

/// <inheritdoc cref="IExpenseTemplateService"/>
public sealed class ExpenseTemplateService(IAppDbContext db, AuditService audit) : IExpenseTemplateService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — arifmetika ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>
    /// Audit yozuvidagi <c>entity_type</c>. <c>AuditService.cs</c> ga
    /// QO'SHILMAGAN — u bir nechta agent parallel qo'shadigan umumiy fayl
    /// (task topshirig'i: "Do not edit AuditService.cs"). Naqsh
    /// <c>BillingSettingsService.AuditEntity</c> bilan bir xil: mahalliy
    /// konstanta, markazlashtirilmagan.
    /// </summary>
    private const string AuditEntity = "ExpenseTemplate";

    /// <summary>
    /// Toifa kodi → ko'rsatiladigan nom. <c>schoollms.client/src/config/constants.ts</c>
    /// dagi <c>expenseCategories</c> bilan QO'LDA sinxronlangan (frontend fayli
    /// tahrirlash taqiqlangan — task topshirig'i: "Do not edit constants.ts") —
    /// ikkalasi ham <see cref="Accounts.ExpenseCategories"/> ning YOPIQ
    /// ro'yxatidan kelib chiqadi, shuning uchun ular kamdan-kam uzoqlashadi.
    /// </summary>
    private static readonly Dictionary<string, string> CategoryLabels = new(StringComparer.Ordinal)
    {
        ["salary"] = "Oylik maosh",
        ["utilities"] = "Kommunal",
        ["supplies"] = "Jihoz/materiallar",
        ["rent"] = "Ijara",
        ["repair"] = "Ta'mirlash",
        ["other"] = "Boshqa chiqim",
    };

    public async Task<IReadOnlyList<ExpenseTemplateDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.ExpenseTemplates.AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return [.. rows.Select(ToDto)];
    }

    public async Task<ExpenseTemplateDto> CreateAsync(
        CreateExpenseTemplateRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var name = RequireName(request.Name);
        var category = RequireCategory(request.CategoryCode);
        var amount = RequireAmount(request.Amount);
        var dayOfMonth = RequireDayOfMonth(request.DayOfMonth);

        var template = new ExpenseTemplate
        {
            Name = name,
            Category = category,
            Amount = amount,
            DayOfMonth = (short)dayOfMonth,
            IsActive = request.IsActive,
            CreatedAt = AppClock.NowInstant,
        };

        db.ExpenseTemplates.Add(template);

        audit.Record(AuditEntity, template.Id.ToString(), "create",
            $"Rejalashtirilgan chiqim shabloni qo'shildi: {name} — {CategoryLabels.GetValueOrDefault(category, category)}, "
            + $"{AuditService.Money(amount)} so'm, har oyning {dayOfMonth}-kuni.",
            after: Snapshot(template));

        await db.SaveChangesAsync(ct);

        return ToDto(template);
    }

    public async Task<ExpenseTemplateDto> UpdateAsync(
        Guid id, UpdateExpenseTemplateRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);

        var template = await db.ExpenseTemplates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw BillingRuleException.NotFound("template_not_found", "Shablon topilmadi.");

        var before = Snapshot(template);

        if (request.Name is not null) template.Name = RequireName(request.Name);
        if (request.CategoryCode is not null) template.Category = RequireCategory(request.CategoryCode);
        if (request.Amount is not null) template.Amount = RequireAmount(request.Amount.Value);
        if (request.DayOfMonth is not null) template.DayOfMonth = (short)RequireDayOfMonth(request.DayOfMonth.Value);
        if (request.IsActive is not null) template.IsActive = request.IsActive.Value;

        audit.Record(AuditEntity, template.Id.ToString(), "update",
            $"Rejalashtirilgan chiqim shabloni o'zgardi: {template.Name}",
            before: before, after: Snapshot(template));

        await db.SaveChangesAsync(ct);

        return ToDto(template);
    }

    public async Task DeleteAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);

        var template = await db.ExpenseTemplates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw BillingRuleException.NotFound("template_not_found", "Shablon topilmadi.");

        audit.Record(AuditEntity, template.Id.ToString(), "delete",
            $"Rejalashtirilgan chiqim shabloni o'chirildi: {template.Name}",
            before: Snapshot(template));

        db.ExpenseTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    private static ExpenseTemplateDto ToDto(ExpenseTemplate t) => new(
        t.Id, t.Name, t.Category, CategoryLabels.GetValueOrDefault(t.Category, t.Category),
        t.Amount, t.DayOfMonth, t.IsActive);

    private static object Snapshot(ExpenseTemplate t) => new
    {
        t.Id, t.Name, t.Category, t.Amount, t.DayOfMonth, t.IsActive,
    };

    private static string RequireName(string? name)
    {
        var clean = (name ?? string.Empty).Trim();
        return clean.Length == 0
            ? throw BillingRuleException.Invalid("name_required", "Shablon nomi majburiy.")
            : clean;
    }

    /// <summary>
    /// <see cref="Accounts.ExpenseCategories"/> ning YOPIQ ro'yxatidan —
    /// <see cref="ExpenseService.CreateAsync"/> qabul qiladigan AYNAN o'sha
    /// qiymatlar, shuning uchun shablondan haqiqiy chiqim yaratilganda
    /// qo'shimcha moslashtirish shart emas.
    /// </summary>
    private static string RequireCategory(string? category)
    {
        var clean = category?.Trim().ToLowerInvariant();
        return Accounts.IsExpenseCategory(clean)
            ? clean!
            : throw BillingRuleException.Invalid("invalid_category",
                $"Noma'lum chiqim toifasi: '{category}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", Accounts.ExpenseCategories)}.");
    }

    private static decimal RequireAmount(decimal amount)
    {
        if (amount <= 0m)
            throw BillingRuleException.Invalid("invalid_amount", "Summa musbat bo'lishi shart.");
        return decimal.Round(amount, MoneyScale) == amount
            ? amount
            : throw BillingRuleException.Invalid("invalid_amount",
                $"Summa tiyin aniqligida (2 kasr) bo'lishi shart: {amount}.");
    }

    private static int RequireDayOfMonth(int dayOfMonth) =>
        dayOfMonth is >= 1 and <= 28
            ? dayOfMonth
            : throw BillingRuleException.Invalid("invalid_day_of_month",
                "Oyning kuni 1 dan 28 gacha bo'lishi kerak (29/30/31 hamma oyda bo'lavermaydi).");

    private static void RequireActor(string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException(
                "Shaxs noma'lum. U JWT claim'idan olinadi — SPEC §4.4.", nameof(actorId));
    }
}
