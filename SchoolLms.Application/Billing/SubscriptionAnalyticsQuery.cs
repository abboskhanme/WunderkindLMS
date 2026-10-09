using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  ABONEMENTLAR ANALITIKASI — FAQAT O'QISH (2026-10-09)
// ===========================================================================
//
//  Mijoz: "sinflarda o'quvchilar bo'yicha aboniment turlari bo'yicha ko'rish mumkin bo'lsin ...
//  umumiy sinflar ro'yxati abonimetlar turlari bo'yicha analitika hisoboti tursin, va sinfni bosib
//  kirganda o'quvchilar bo'yicha qaysi aboniment kimda qanaqa borligi jadvali chiqadigan bo'lsin";
//  "bunda vaqt filterlari ham muammosiz ishlasin".
//
//  DAVR QANDAY QO'LLANADI
//  ----------------------
//  · Abonement davrda BOR, agar uning muddati davr bilan kesishsa:
//    `starts_on <= to` va (`ends_on` yo'q yoki `ends_on >= from`). Davr ichida abonementini
//    almashtirgan o'quvchi ikkala abonementda ham sanaladi — ikkalasi ham shu davrda bo'lgan.
//  · Pul — davrga TUSHGAN OYLARNING hisob-fakturalari (`period_month` from oyidan to oyigacha):
//    hisoblangan = summa − chegirma (registrdagi `Payable`), to'langan = shu fakturalarga kuchdagi
//    taqsimotlar (`InvoiceService.EffectiveAllocations` bilan bir xil ta'rif), qarz = farqi.
//    Faktura abonementga bog'lanmagan (`invoices` da plan yo'q) — u o'sha o'quvchining o'sha
//    toifadagi, o'sha oyni qamragan abonementiga yoziladi; topilmasa "Abonement ko'rsatilmagan".
//
//  QATORLAR — sinflar (arxivlanmagan), keyin "Sinfsiz" va "Arxiv" (o'quvchi maktabdan ketgan, lekin
//  davrda abonementi yoki fakturasi bor) — jami summa hech qayerda yo'qolmasin.
// ===========================================================================

/// <summary>One abonement column of the report.</summary>
public sealed record SubscriptionPlanColumnDto(
    Guid PlanId, string Name, string CategoryCode, string CategoryName, decimal MonthlyAmount, bool IsActive);

/// <summary>One class × abonement cell: pupils on it in the period and their money for its months.</summary>
public sealed record SubscriptionPlanCellDto(Guid PlanId, int Students, decimal Billed, decimal Paid);

/// <summary>One row of the class list.</summary>
/// <param name="ClassId">The class; <c>none</c> — pupils without a class; <c>archived</c> — pupils who left.</param>
/// <param name="WithoutTuition">Pupils with no tuition abonement in the period.</param>
public sealed record SubscriptionClassRowDto(
    string ClassId, string ClassName, int Grade, int Students, int WithoutTuition,
    List<SubscriptionPlanCellDto> Plans, decimal Billed, decimal Paid, decimal Debt);

/// <summary>An abonement over the whole school.</summary>
public sealed record SubscriptionPlanTotalDto(Guid PlanId, int Students, decimal Billed, decimal Paid, decimal Debt);

/// <summary>The class list report.</summary>
public sealed record SubscriptionAnalyticsDto(
    DateOnly From, DateOnly To,
    List<SubscriptionPlanColumnDto> Plans,
    List<SubscriptionPlanTotalDto> PlanTotals,
    List<SubscriptionClassRowDto> Classes,
    int Students, int WithSubscription, int WithoutTuition,
    decimal Billed, decimal Paid, decimal Debt);

/// <summary>One abonement of one pupil.</summary>
public sealed record SubscriptionStudentPlanDto(
    Guid SubscriptionId, Guid PlanId, string PlanName, string CategoryCode, string CategoryName,
    decimal MonthlyAmount, DateOnly StartsOn, DateOnly? EndsOn, string? Detail,
    decimal Billed, decimal Paid);

/// <summary>One pupil of the class page.</summary>
public sealed record SubscriptionStudentRowDto(
    string StudentId, string FullName, List<SubscriptionStudentPlanDto> Subscriptions,
    decimal Billed, decimal Paid, decimal Debt);

/// <summary>The class page: who is on which abonement.</summary>
public sealed record SubscriptionClassDetailDto(
    DateOnly From, DateOnly To, string ClassId, string ClassName,
    List<SubscriptionPlanColumnDto> Plans, List<SubscriptionStudentRowDto> Students,
    decimal Billed, decimal Paid, decimal Debt);

/// <inheritdoc cref="SubscriptionAnalyticsDto"/>
public sealed class SubscriptionAnalyticsQuery(IAppDbContext db)
{
    public const string NoClass = "none";
    public const string Archived = "archived";

    /// <summary>The pseudo column for an invoice no abonement covers.</summary>
    public static readonly Guid NoPlan = Guid.Empty;

    /// <summary>Longest period one request may cover — a school year and a bit.</summary>
    public const int MaxDays = 400;

    private sealed record Pupil(string Id, string FullName, string RowId);

    private sealed record Sub(
        Guid Id, string StudentId, Guid CategoryId, Guid PlanId, decimal MonthlyAmount,
        DateOnly StartsOn, DateOnly? EndsOn, string? Detail);

    private sealed record Money(string StudentId, Guid PlanId, Guid? SubscriptionId, decimal Billed, decimal Paid);

    /// <summary>Default period: from the first of <paramref name="to"/>'s month (today's when not given).</summary>
    public static (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to)
    {
        var t = to ?? AppClock.Today;
        return (from ?? new DateOnly(t.Year, t.Month, 1), t);
    }

    /// <summary>Why the period cannot be served, or null when it can.</summary>
    public static string? PeriodError(DateOnly from, DateOnly to) =>
        to < from ? $"Davr oxiri boshidan oldin: {from:yyyy-MM-dd} … {to:yyyy-MM-dd}"
        : to.DayNumber - from.DayNumber > MaxDays ? $"Davr juda uzun: {MaxDays} kundan oshmasin."
        : null;

    /// <summary>Class list × abonements for the period.</summary>
    public async Task<SubscriptionAnalyticsDto> ClassesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        RequireRange(from, to);
        var (classes, pupils) = await PupilsAsync(ct);
        var categories = await db.FeeCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var subs = await SubsAsync(from, to, null, ct);
        var money = await MoneyAsync(from, to, null, subs, ct);
        var columns = await ColumnsAsync(categories, money.Any(m => m.PlanId == NoPlan), ct);
        var tuition = categories.Values.Where(c => c.Code == FeeCategoryCode.Tuition).Select(c => c.Id).ToHashSet();

        // Pupils who left the school only appear when the period has something of theirs.
        var known = pupils.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var missing = subs.Select(s => s.StudentId).Concat(money.Select(m => m.StudentId))
            .Where(id => !known.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            foreach (var s in await db.Students.AsNoTracking().Where(s => missing.Contains(s.Id))
                         .Select(s => new { s.Id, s.FullName }).ToListAsync(ct))
                known[s.Id] = new Pupil(s.Id, s.FullName, Archived);

        var rowDefs = classes.Select(c => (c.Id, c.Name, c.Grade)).ToList();
        rowDefs.Add((NoClass, "Sinfsiz", 99));
        rowDefs.Add((Archived, "Arxiv (maktabdan ketgan)", 100));

        var rows = new List<SubscriptionClassRowDto>();
        foreach (var (rowId, rowName, grade) in rowDefs)
        {
            var mine = known.Values.Where(p => p.RowId == rowId).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            if (mine.Count == 0) continue;
            var rowSubs = subs.Where(s => mine.Contains(s.StudentId)).ToList();
            var rowMoney = money.Where(m => mine.Contains(m.StudentId)).ToList();
            if (rowId == Archived && rowSubs.Count == 0 && rowMoney.Count == 0) continue;

            var cells = columns.Select(c => new SubscriptionPlanCellDto(
                    c.PlanId,
                    rowSubs.Where(s => s.PlanId == c.PlanId).Select(s => s.StudentId).Distinct(StringComparer.Ordinal).Count(),
                    rowMoney.Where(m => m.PlanId == c.PlanId).Sum(m => m.Billed),
                    rowMoney.Where(m => m.PlanId == c.PlanId).Sum(m => m.Paid)))
                .ToList();
            var withTuition = rowSubs.Where(s => tuition.Contains(s.CategoryId)).Select(s => s.StudentId)
                .ToHashSet(StringComparer.Ordinal);
            var billed = rowMoney.Sum(m => m.Billed);
            var paid = rowMoney.Sum(m => m.Paid);
            rows.Add(new SubscriptionClassRowDto(
                rowId, rowName, grade, rowId == Archived ? rowSubs.Select(s => s.StudentId).Concat(rowMoney.Select(m => m.StudentId))
                    .Distinct(StringComparer.Ordinal).Count() : mine.Count,
                rowId == Archived ? 0 : mine.Count(id => !withTuition.Contains(id)),
                cells, billed, paid, billed - paid));
        }

        var planTotals = columns.Select(c =>
        {
            var b = money.Where(m => m.PlanId == c.PlanId).Sum(m => m.Billed);
            var p = money.Where(m => m.PlanId == c.PlanId).Sum(m => m.Paid);
            return new SubscriptionPlanTotalDto(
                c.PlanId, subs.Where(s => s.PlanId == c.PlanId).Select(s => s.StudentId).Distinct(StringComparer.Ordinal).Count(),
                b, p, b - p);
        }).ToList();

        var inSchool = pupils.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var subscribed = subs.Where(s => inSchool.Contains(s.StudentId)).Select(s => s.StudentId).ToHashSet(StringComparer.Ordinal);
        var tuitioned = subs.Where(s => inSchool.Contains(s.StudentId) && tuition.Contains(s.CategoryId))
            .Select(s => s.StudentId).ToHashSet(StringComparer.Ordinal);
        var totalBilled = money.Sum(m => m.Billed);
        var totalPaid = money.Sum(m => m.Paid);

        return new SubscriptionAnalyticsDto(
            from, to, columns, planTotals, rows,
            inSchool.Count, subscribed.Count, inSchool.Count - tuitioned.Count,
            totalBilled, totalPaid, totalBilled - totalPaid);
    }

    /// <summary>One class (or <see cref="NoClass"/> / <see cref="Archived"/>): every pupil and their abonements.</summary>
    public async Task<SubscriptionClassDetailDto?> ClassAsync(
        string classId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        RequireRange(from, to);
        var (classes, pupils) = await PupilsAsync(ct);
        string className;
        List<Pupil> mine;
        if (classId == Archived)
        {
            className = "Arxiv (maktabdan ketgan)";
            mine = await db.Students.AsNoTracking().Where(s => s.IsArchived)
                .Select(s => new Pupil(s.Id, s.FullName, Archived)).ToListAsync(ct);
        }
        else
        {
            var cls = classes.FirstOrDefault(c => c.Id == classId);
            if (cls is null && classId != NoClass) return null;
            className = cls?.Name ?? "Sinfsiz";
            mine = [.. pupils.Where(p => p.RowId == classId)];
        }

        var ids = mine.Select(p => p.Id).ToList();
        var categories = await db.FeeCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var subs = await SubsAsync(from, to, ids, ct);
        var money = await MoneyAsync(from, to, ids, subs, ct);
        var columns = await ColumnsAsync(categories, money.Any(m => m.PlanId == NoPlan), ct);
        var planById = columns.ToDictionary(c => c.PlanId);

        // An archived pupil is listed only when the period has something of theirs.
        if (classId == Archived)
        {
            var active = subs.Select(s => s.StudentId).Concat(money.Select(m => m.StudentId)).ToHashSet(StringComparer.Ordinal);
            mine = [.. mine.Where(p => active.Contains(p.Id))];
        }

        var students = mine
            .OrderBy(p => p.FullName, StringComparer.Ordinal)
            .Select(p =>
            {
                var own = subs.Where(s => s.StudentId == p.Id)
                    .OrderBy(s => categories.TryGetValue(s.CategoryId, out var c) ? c.Code : "")
                    .ThenBy(s => s.StartsOn)
                    .Select(s =>
                    {
                        var col = planById.GetValueOrDefault(s.PlanId);
                        var cat = categories.GetValueOrDefault(s.CategoryId);
                        var sm = money.Where(m => m.SubscriptionId == s.Id).ToList();
                        return new SubscriptionStudentPlanDto(
                            s.Id, s.PlanId, col?.Name ?? "—", cat?.Code ?? "", cat?.Name ?? "",
                            s.MonthlyAmount, s.StartsOn, s.EndsOn, s.Detail,
                            sm.Sum(m => m.Billed), sm.Sum(m => m.Paid));
                    })
                    .ToList();
                var mm = money.Where(m => m.StudentId == p.Id).ToList();
                var b = mm.Sum(m => m.Billed);
                var pd = mm.Sum(m => m.Paid);
                return new SubscriptionStudentRowDto(p.Id, p.FullName, own, b, pd, b - pd);
            })
            .ToList();

        var billed = money.Sum(m => m.Billed);
        var paid = money.Sum(m => m.Paid);
        return new SubscriptionClassDetailDto(from, to, classId, className, columns, students, billed, paid, billed - paid);
    }

    // =================================================================
    //  Manbalar
    // =================================================================

    /// <summary>Active classes and the pupils in school, each with the row they belong to.</summary>
    private async Task<(List<SchoolClass> Classes, List<Pupil> Pupils)> PupilsAsync(CancellationToken ct)
    {
        var classes = await db.Classes.AsNoTracking().Where(c => !c.IsArchived)
            .OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync(ct);
        var idByName = classes.GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var pupils = (await db.Students.AsNoTracking().Where(s => !s.IsArchived)
                .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync(ct))
            .Select(s => new Pupil(s.Id, s.FullName,
                s.ClassName is not null && idByName.TryGetValue(s.ClassName, out var id) ? id : NoClass))
            .ToList();
        return (classes, pupils);
    }

    /// <summary>Abonements whose term meets the period (file header).</summary>
    private async Task<List<Sub>> SubsAsync(DateOnly from, DateOnly to, List<string>? studentIds, CancellationToken ct)
    {
        var q = db.StudentSubscriptions.AsNoTracking()
            .Where(s => s.StartsOn <= to && (s.EndsOn == null || s.EndsOn >= from));
        if (studentIds is not null) q = q.Where(s => studentIds.Contains(s.StudentId));
        return await q
            .Select(s => new Sub(s.Id, s.StudentId, s.CategoryId, s.PlanId ?? NoPlan, s.MonthlyAmount,
                s.StartsOn, s.EndsOn, s.Detail))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Invoices of the period's months with what is paid on them, each tied to the abonement that
    /// covers its month. Abonements that ended before the period still name the plan of an older
    /// invoice, so they are read here without the period cut.
    /// </summary>
    private async Task<List<Money>> MoneyAsync(
        DateOnly from, DateOnly to, List<string>? studentIds, List<Sub> periodSubs, CancellationToken ct)
    {
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);
        var invoices = db.Invoices.AsNoTracking()
            .Where(i => i.PeriodMonth >= firstMonth && i.PeriodMonth <= lastMonth
                        && i.RemovedAt == null && i.VoidedAt == null && i.Status != InvoiceStatus.Void);
        if (studentIds is not null) invoices = invoices.Where(i => studentIds.Contains(i.StudentId));
        var rows = await invoices
            .Select(i => new { i.Id, i.StudentId, i.CategoryId, i.PeriodMonth, Payable = i.Amount - i.Discount })
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        var invoiceIds = rows.Select(r => r.Id).ToList();
        var paid = await EffectiveAllocations()
            .Where(a => invoiceIds.Contains(a.InvoiceId))
            .GroupBy(a => a.InvoiceId)
            .Select(g => new { g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

        var owners = rows.Select(r => r.StudentId).Distinct().ToList();
        var allSubs = await db.StudentSubscriptions.AsNoTracking()
            .Where(s => owners.Contains(s.StudentId))
            .Select(s => new Sub(s.Id, s.StudentId, s.CategoryId, s.PlanId ?? NoPlan, s.MonthlyAmount,
                s.StartsOn, s.EndsOn, s.Detail))
            .ToListAsync(ct);
        var byOwner = allSubs.GroupBy(s => (s.StudentId, s.CategoryId)).ToDictionary(g => g.Key, g => g.ToList());

        return [.. rows.Select(r =>
        {
            var monthEnd = r.PeriodMonth.AddMonths(1).AddDays(-1);
            // The abonement that covers the month; with two (a change mid-month) — the later one.
            var sub = byOwner.GetValueOrDefault((r.StudentId, r.CategoryId))?
                .Where(s => s.StartsOn <= monthEnd && (s.EndsOn == null || s.EndsOn >= r.PeriodMonth))
                .OrderByDescending(s => s.StartsOn)
                .FirstOrDefault();
            return new Money(r.StudentId, sub?.PlanId ?? NoPlan, sub?.Id, r.Payable, paid.GetValueOrDefault(r.Id));
        })];
    }

    /// <summary>
    /// Allocations that are really in force — the definition of <c>InvoiceService.EffectiveAllocations</c>
    /// (private there): neither a storno's own allocation nor one of a payment that was reversed later.
    /// </summary>
    private IQueryable<PaymentAllocation> EffectiveAllocations() =>
        db.PaymentAllocations.AsNoTracking()
            .Where(a => !db.Payments.Any(p => p.Id == a.PaymentId && p.ReversalOf != null))
            .Where(a => !db.Payments.Any(r => r.ReversalOf == a.PaymentId));

    /// <summary>Every abonement, grouped by category (tuition first); the "no abonement" column when needed.</summary>
    private async Task<List<SubscriptionPlanColumnDto>> ColumnsAsync(
        Dictionary<Guid, FeeCategory> categories, bool withNoPlan, CancellationToken ct)
    {
        var order = new[] { FeeCategoryCode.Tuition, FeeCategoryCode.Bus, FeeCategoryCode.Dormitory, FeeCategoryCode.Meals };
        var plans = await db.SubscriptionPlans.AsNoTracking().ToListAsync(ct);
        var columns = plans
            .Select(p =>
            {
                var c = categories.GetValueOrDefault(p.CategoryId);
                return new SubscriptionPlanColumnDto(p.Id, p.Name, c?.Code ?? "", c?.Name ?? "", p.MonthlyAmount, p.IsActive);
            })
            .OrderBy(c => Array.IndexOf(order, c.CategoryCode) is var i && i >= 0 ? i : order.Length)
            .ThenBy(c => c.CategoryName, StringComparer.Ordinal)
            .ThenByDescending(c => c.MonthlyAmount)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
        if (withNoPlan)
            columns.Add(new SubscriptionPlanColumnDto(NoPlan, "Abonement ko'rsatilmagan", "", "Boshqa", 0m, true));
        return columns;
    }

    private static void RequireRange(DateOnly from, DateOnly to)
    {
        if (PeriodError(from, to) is { } error)
            throw BillingRuleException.Invalid("invalid_period", error);
    }
}
