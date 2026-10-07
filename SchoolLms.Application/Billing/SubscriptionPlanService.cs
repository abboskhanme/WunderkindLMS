using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Abonementlar — docs/modules/billing-plans.md.
//
//  An abonement is a named price inside a category. Its subscriptions copy the price when they
//  are written; a price change reaches them "hammaga, keyingi oydan" (client, 2026-09-28):
//
//   * a subscription whose CURRENT month is already invoiced gets the new amount in place — the
//     written invoice keeps the old price, the next accrual (next month) takes the new one, and
//     the audit before/after pair is exactly what the P&L change journal reads as "tariffChanged";
//   * one active this month but NOT yet invoiced for it (rare: the accrual runs twice a day) is
//     ended on the last day of this month and continued from the 1st of next month at the new
//     price — otherwise this month would be accrued at the new price;
//   * one that only starts next month or later just gets the new amount.
//
//  Written invoices are never touched (SubscriptionService: "obuna — kelajak, hisob-faktura — tarix").
// ===========================================================================

/// <param name="DaysPerWeek">null = "Butun oy", 1..7 = "Haftasiga N kun" (<see cref="SubscriptionPlan.DaysPerWeek"/>).</param>
public record SubscriptionPlanDto(
    Guid Id, string Name, Guid CategoryId, string CategoryName, string CategoryCode,
    decimal MonthlyAmount, bool IsActive, int StudentCount, int? DaysPerWeek = null);

public record CreateSubscriptionPlanRequest(string? Name, Guid CategoryId, decimal MonthlyAmount, int? DaysPerWeek = null);

/// <summary>
/// The type (category) is fixed once created — it is the accounting bucket of its invoices.
/// <c>DaysPerWeek</c> is always sent: null means "Butun oy".
/// </summary>
public record UpdateSubscriptionPlanRequest(string? Name, decimal MonthlyAmount, bool IsActive, int? DaysPerWeek = null);

/// <param name="Repriced">Subscriptions moved to the new price.</param>
/// <param name="EffectiveFrom">First month of the new price; null when the price did not change.</param>
public record UpdateSubscriptionPlanResult(SubscriptionPlanDto Plan, int Repriced, DateOnly? EffectiveFrom);

public sealed class SubscriptionPlanService(IAppDbContext db, AuditService audit)
{
    private const string AuditEntity = "SubscriptionPlan";
    private const string SubscriptionAuditEntity = "StudentSubscription";
    private const int NameMax = 160;
    private const int MoneyScale = 2;

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListAsync(bool activeOnly, CancellationToken ct = default)
    {
        var plans = db.SubscriptionPlans.AsNoTracking();
        if (activeOnly) plans = plans.Where(p => p.IsActive);
        var ids = await plans.Select(p => p.Id).ToListAsync(ct);
        return await DtosAsync(ids, ct);
    }

    public async Task<SubscriptionPlanDto> CreateAsync(
        CreateSubscriptionPlanRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);
        var name = RequireName(request.Name);
        var amount = RequireAmount(request.MonthlyAmount);
        var days = RequireDays(request.DaysPerWeek);

        var category = await db.FeeCategories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw BillingRuleException.NotFound("category_not_found", "Abonement turi (toifa) topilmadi.");
        if (!category.IsActive)
            throw BillingRuleException.Conflict(
                "category_inactive", $"'{category.Name}' toifasi o'chirilgan — unga abonement qo'shib bo'lmaydi.");
        await RequireFreeNameAsync(name, exclude: null, ct);

        var plan = new SubscriptionPlan
        {
            Name = name,
            CategoryId = category.Id,
            MonthlyAmount = amount,
            DaysPerWeek = days,
            IsActive = true,
            CreatedAt = AppClock.NowInstant,
        };
        db.SubscriptionPlans.Add(plan);
        audit.Record(AuditEntity, plan.Id.ToString(), "create",
            $"Abonement qo'shildi: {name} ({category.Name}) — {AuditService.Money(amount)} so'm/oy",
            after: Snapshot(plan));
        await db.SaveChangesAsync(ct);
        return (await DtosAsync([plan.Id], ct))[0];
    }

    public async Task<UpdateSubscriptionPlanResult> UpdateAsync(
        Guid id, UpdateSubscriptionPlanRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw BillingRuleException.NotFound("plan_not_found", "Abonement topilmadi.");

        var name = RequireName(request.Name);
        var amount = RequireAmount(request.MonthlyAmount);
        var days = RequireDays(request.DaysPerWeek);
        if (request.IsActive && (name != plan.Name || !plan.IsActive))
            await RequireFreeNameAsync(name, exclude: plan.Id, ct);

        var before = Snapshot(plan);
        var (oldName, oldActive, oldAmount) = (plan.Name, plan.IsActive, plan.MonthlyAmount);
        plan.Name = name;
        plan.IsActive = request.IsActive;
        plan.MonthlyAmount = amount;
        plan.DaysPerWeek = days;

        var repriced = 0;
        DateOnly? effectiveFrom = null;
        if (amount != oldAmount)
        {
            (repriced, effectiveFrom) = await RepriceAsync(plan, actorId, ct);
        }

        audit.Record(AuditEntity, plan.Id.ToString(), "update",
            $"Abonement o'zgardi: '{oldName}' → '{name}', faol: {oldActive} → {request.IsActive}, "
            + $"narxi: {AuditService.Money(oldAmount)} → {AuditService.Money(amount)} so'm"
            + (effectiveFrom is { } from ? $" ({repriced} ta obuna, {from:yyyy-MM-dd} dan)" : ""),
            before: before, after: Snapshot(plan));
        await db.SaveChangesAsync(ct);
        return new UpdateSubscriptionPlanResult((await DtosAsync([plan.Id], ct))[0], repriced, effectiveFrom);
    }

    /// <summary>"Hammaga, keyingi oydan" — the three cases in the file header. Caller saves.</summary>
    private async Task<(int Repriced, DateOnly EffectiveFrom)> RepriceAsync(
        SubscriptionPlan plan, string actorId, CancellationToken ct)
    {
        var today = AppClock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var next = monthStart.AddMonths(1);
        var price = decimal.Round(plan.MonthlyAmount, MoneyScale);

        // An archived pupil's abonement is left as it is (client, 2026-10-08: nothing is done with them).
        var live = await db.StudentSubscriptions
            .Where(s => s.PlanId == plan.Id && (s.EndsOn == null || s.EndsOn >= next)
                        && !db.Students.Any(st => st.Id == s.StudentId && st.IsArchived))
            .ToListAsync(ct);
        var studentIds = live.Select(s => s.StudentId).Distinct().ToList();
        var invoicedThisMonth = (await db.Invoices.AsNoTracking()
                .Where(i => i.CategoryId == plan.CategoryId && i.PeriodMonth == monthStart
                            && i.Status != InvoiceStatus.Void && studentIds.Contains(i.StudentId))
                .Select(i => i.StudentId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var repriced = 0;
        foreach (var s in live)
        {
            if (s.MonthlyAmount == price) continue;
            var before = SubscriptionService.Snapshot(s);
            var runsThisMonth = s.StartsOn < next;

            if (runsThisMonth && !invoicedThisMonth.Contains(s.StudentId))
            {
                var tail = new StudentSubscription
                {
                    StudentId = s.StudentId,
                    CategoryId = s.CategoryId,
                    PlanId = s.PlanId,
                    MonthlyAmount = price,
                    Detail = s.Detail,
                    StartsOn = next,
                    // Hisob keyinroq boshlanadigan bo'lsa (AccrueFrom) — dumi ham o'sha oydan hisoblanadi.
                    AccrueFrom = s.AccrueFrom > next ? s.AccrueFrom : null,
                    EndsOn = s.EndsOn,
                    CreatedBy = actorId,
                    CreatedAt = AppClock.NowInstant,
                };
                s.EndsOn = next.AddDays(-1);
                db.StudentSubscriptions.Add(tail);
                audit.Record(SubscriptionAuditEntity, s.Id.ToString(), "update",
                    $"'{plan.Name}' abonementi narxi o'zgardi — eski narx {s.EndsOn:yyyy-MM-dd} gacha",
                    before: before, after: SubscriptionService.Snapshot(s), studentId: s.StudentId);
                audit.Record(SubscriptionAuditEntity, tail.Id.ToString(), "create",
                    $"'{plan.Name}' abonementi: yangi narx {AuditService.Money(price)} so'm/oy, {next:yyyy-MM-dd} dan",
                    after: SubscriptionService.Snapshot(tail), studentId: s.StudentId);
            }
            else
            {
                var old = s.MonthlyAmount;
                s.MonthlyAmount = price;
                audit.Record(SubscriptionAuditEntity, s.Id.ToString(), "update",
                    $"'{plan.Name}' abonementi narxi o'zgardi: {AuditService.Money(old)} → "
                    + $"{AuditService.Money(price)} so'm/oy, {next:yyyy-MM-dd} dan",
                    before: before, after: SubscriptionService.Snapshot(s), studentId: s.StudentId);
            }
            repriced++;
        }
        return (repriced, next);
    }

    /// <summary>
    /// Plans with their type and how many students are on them — "bog'langan": a subscription that
    /// has not ended (a future start counts, it is already sold).
    /// </summary>
    private async Task<List<SubscriptionPlanDto>> DtosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var today = AppClock.Today;
        var counts = await db.StudentSubscriptions.AsNoTracking()
            .Where(s => s.PlanId != null && ids.Contains(s.PlanId.Value) && (s.EndsOn == null || s.EndsOn >= today))
            .GroupBy(s => s.PlanId!.Value)
            .Select(g => new { PlanId = g.Key, Students = g.Select(s => s.StudentId).Distinct().Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Students, ct);

        var rows = await (
            from p in db.SubscriptionPlans.AsNoTracking()
            join c in db.FeeCategories.AsNoTracking() on p.CategoryId equals c.Id
            where ids.Contains(p.Id)
            orderby c.Name, p.Name
            select new { p.Id, p.Name, p.CategoryId, CategoryName = c.Name, CategoryCode = c.Code, p.MonthlyAmount, p.IsActive, p.DaysPerWeek })
            .ToListAsync(ct);

        return [.. rows.Select(r => new SubscriptionPlanDto(
            r.Id, r.Name, r.CategoryId, r.CategoryName, r.CategoryCode, r.MonthlyAmount, r.IsActive,
            counts.GetValueOrDefault(r.Id), r.DaysPerWeek))];
    }

    private async Task RequireFreeNameAsync(string name, Guid? exclude, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.SubscriptionPlans.AnyAsync(
                p => p.IsActive && p.Name.ToLower() == lower && (exclude == null || p.Id != exclude), ct))
            throw BillingRuleException.Conflict("plan_name_taken", $"'{name}' nomli faol abonement allaqachon bor.");
    }

    private static string RequireName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0) throw BillingRuleException.Invalid("name_required", "Abonement nomi majburiy.");
        if (name.Length > NameMax)
            throw BillingRuleException.Invalid("name_too_long", $"Abonement nomi {NameMax} belgidan oshmasin.");
        return name;
    }

    private static int? RequireDays(int? days)
    {
        if (days is null or (>= 1 and <= 7)) return days;
        throw BillingRuleException.Invalid("invalid_days_per_week", "Haftasiga kunlar soni 1 dan 7 gacha bo'lishi kerak.");
    }

    private static decimal RequireAmount(decimal amount)
    {
        if (amount < 0m) throw BillingRuleException.Invalid("invalid_amount", "Abonement narxi manfiy bo'la olmaydi.");
        return decimal.Round(amount, MoneyScale);
    }

    private static void RequireActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw BillingRuleException.Invalid("actor_required", "Amalni bajaruvchi aniqlanmadi.");
    }

    private static object Snapshot(SubscriptionPlan p) => new { p.Name, p.IsActive, p.CategoryId, p.MonthlyAmount, p.DaysPerWeek };
}
