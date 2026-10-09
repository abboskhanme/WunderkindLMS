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
//  "bunda vaqt filterlari ham muammosiz ishlasin"; "cardlar ham filter vazifasini bajarsin";
//  "excelga kerakli malumotlarni tortib olish ham mumkin bo'lsin".
//
//  DAVR QANDAY QO'LLANADI
//  ----------------------
//  · Pul — davrga TUSHGAN OYLARNING hisob-fakturalari (`period_month` from oyidan to oyigacha):
//    hisoblangan = summa − chegirma (registrdagi `Payable`), to'langan = shu fakturalarga kuchdagi
//    taqsimotlar (`InvoiceService.EffectiveAllocations` bilan bir xil ta'rif), qarz = farqi.
//    Faktura abonementga bog'lanmagan (`invoices` da plan yo'q) — u o'sha o'quvchining o'sha
//    toifadagi, o'sha oyni qamragan abonementiga yoziladi; topilmasa "Abonement ko'rsatilmagan"
//    ustuniga (abonementi yo'q oyga faktura — o'quvchisi ham shu ustunda sanaladi).
//  · O'quvchining davrdagi abonementlari — muddati davr bilan kesishganlari
//    (`starts_on <= to` va `ends_on` yo'q yoki `>= from`) VA davr fakturasini qamragan abonement.
//    Davr ichida abonementini almashtirgan o'quvchi ikkalasida ham sanaladi.
//
//  QATORLAR — sinflar (arxivlanmagan), keyin "Sinfsiz" va "Arxiv" (o'quvchi maktabdan ketgan, lekin
//  davrda abonementi yoki fakturasi bor) — jami summa hech qayerda yo'qolmasin.
//
//  KARTALAR VA FILTRLAR — bitta ta'rif: kartadagi son va o'sha karta ochadigan ro'yxat (`StudentsAsync`)
//  bir xil qoidadan chiqadi (`Matches`), ya'ni "5" deb yozilgan kartani bossangiz 5 ta o'quvchi chiqadi.
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

/// <summary>One pupil with their abonements and money for the period.</summary>
/// <param name="ClassId">Class id, <c>none</c> or <c>archived</c>.</param>
/// <param name="InSchool">false — the pupil left the school (archived) but the period has something of theirs.</param>
/// <param name="NoPlanBilled">Billed on invoices no abonement covers.</param>
public sealed record SubscriptionStudentRowDto(
    string StudentId, string FullName, List<SubscriptionStudentPlanDto> Subscriptions,
    decimal Billed, decimal Paid, decimal Debt,
    string ClassId = "", string ClassName = "", bool InSchool = true, decimal NoPlanBilled = 0m);

/// <summary>The class page: who is on which abonement.</summary>
public sealed record SubscriptionClassDetailDto(
    DateOnly From, DateOnly To, string ClassId, string ClassName,
    List<SubscriptionPlanColumnDto> Plans, List<SubscriptionStudentRowDto> Students,
    decimal Billed, decimal Paid, decimal Debt);

/// <summary>The pupils a card or an abonement row stands for (<see cref="SubscriptionAnalyticsQuery.Filters"/>).</summary>
public sealed record SubscriptionStudentsDto(
    DateOnly From, DateOnly To, string Filter,
    List<SubscriptionPlanColumnDto> Plans, List<SubscriptionStudentRowDto> Students,
    decimal Billed, decimal Paid, decimal Debt);

/// <inheritdoc cref="SubscriptionAnalyticsDto"/>
public sealed class SubscriptionAnalyticsQuery(IAppDbContext db)
{
    public const string NoClass = "none";
    public const string Archived = "archived";
    public const string NoClassName = "Sinfsiz";
    public const string ArchivedName = "Arxiv (maktabdan ketgan)";

    /// <summary>The pseudo column for an invoice no abonement covers.</summary>
    public static readonly Guid NoPlan = Guid.Empty;

    /// <summary>Longest period one request may cover — a school year and a bit.</summary>
    public const int MaxDays = 400;

    /// <summary>
    /// Card filters: <c>all</c> — pupils in school; <c>subscribed</c> — in school with an abonement;
    /// <c>no-tuition</c> — in school without a tuition abonement; <c>billed</c> — anyone billed;
    /// <c>debt</c> — anyone owing (largest first); <c>plan:{id}</c> — on that abonement
    /// (<c>plan:00000000-…</c> — billed with no abonement); <c>everyone</c> — all of the above's
    /// universe: pupils in school plus those who left but have something in the period (Excel).
    /// </summary>
    public static readonly string[] Filters = ["all", "subscribed", "no-tuition", "billed", "debt", "plan:{id}", "everyone"];

    private sealed record Pupil(string Id, string FullName, string RowId, string ClassName, bool InSchool, int Order);

    private sealed record Sub(
        Guid Id, string StudentId, Guid CategoryId, Guid PlanId, decimal MonthlyAmount,
        DateOnly StartsOn, DateOnly? EndsOn, string? Detail);

    private sealed record Money(string StudentId, Guid PlanId, Guid? SubscriptionId, decimal Billed, decimal Paid);

    /// <summary>Everything the period needs, read once.</summary>
    private sealed class Data
    {
        public required List<SchoolClass> Classes { get; init; }
        public required Dictionary<string, Pupil> Pupils { get; init; }
        public required Dictionary<Guid, FeeCategory> Categories { get; init; }
        public required List<SubscriptionPlanColumnDto> Columns { get; init; }
        public required HashSet<Guid> Tuition { get; init; }
        public required ILookup<string, Sub> SubsOf { get; init; }
        public required ILookup<string, Money> MoneyOf { get; init; }
    }

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

    /// <summary>Is <paramref name="filter"/> one of <see cref="Filters"/>?</summary>
    public static bool IsFilter(string? filter) =>
        filter is "all" or "subscribed" or "no-tuition" or "billed" or "debt" or "everyone"
        || (filter is not null && filter.StartsWith("plan:", StringComparison.Ordinal)
            && Guid.TryParse(filter["plan:".Length..], out _));

    /// <summary>Class list × abonements for the period.</summary>
    public async Task<SubscriptionAnalyticsDto> ClassesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var d = await LoadAsync(from, to, ct);

        var rowDefs = d.Classes.Select(c => (c.Id, c.Name, c.Grade)).ToList();
        rowDefs.Add((NoClass, NoClassName, 99));
        rowDefs.Add((Archived, ArchivedName, 100));

        var rows = new List<SubscriptionClassRowDto>();
        foreach (var (rowId, rowName, grade) in rowDefs)
        {
            var mine = d.Pupils.Values.Where(p => p.RowId == rowId).ToList();
            if (mine.Count == 0) continue;
            var cells = d.Columns.Select(c => Cell(d, mine, c.PlanId)).ToList();
            var money = mine.SelectMany(p => d.MoneyOf[p.Id]).ToList();
            var billed = money.Sum(m => m.Billed);
            var paid = money.Sum(m => m.Paid);
            rows.Add(new SubscriptionClassRowDto(
                rowId, rowName, grade, mine.Count,
                rowId == Archived ? 0 : mine.Count(p => Matches(d, p, "no-tuition")),
                cells, billed, paid, billed - paid));
        }

        var everyone = d.Pupils.Values.ToList();
        var planTotals = d.Columns.Select(c =>
        {
            var cell = Cell(d, everyone, c.PlanId);
            return new SubscriptionPlanTotalDto(c.PlanId, cell.Students, cell.Billed, cell.Paid, cell.Billed - cell.Paid);
        }).ToList();

        var all = everyone.SelectMany(p => d.MoneyOf[p.Id]).ToList();
        var totalBilled = all.Sum(m => m.Billed);
        var totalPaid = all.Sum(m => m.Paid);
        return new SubscriptionAnalyticsDto(
            from, to, d.Columns, planTotals, rows,
            everyone.Count(p => Matches(d, p, "all")),
            everyone.Count(p => Matches(d, p, "subscribed")),
            everyone.Count(p => Matches(d, p, "no-tuition")),
            totalBilled, totalPaid, totalBilled - totalPaid);
    }

    /// <summary>One class (or <see cref="NoClass"/> / <see cref="Archived"/>): every pupil and their abonements.</summary>
    public async Task<SubscriptionClassDetailDto?> ClassAsync(
        string classId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var d = await LoadAsync(from, to, ct);
        var cls = d.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls is null && classId != NoClass && classId != Archived) return null;
        var className = cls?.Name ?? (classId == NoClass ? NoClassName : ArchivedName);

        var rows = d.Pupils.Values.Where(p => p.RowId == classId)
            .OrderBy(p => p.FullName, StringComparer.Ordinal)
            .Select(p => Row(d, p)).ToList();
        var (billed, paid) = (rows.Sum(r => r.Billed), rows.Sum(r => r.Paid));
        return new SubscriptionClassDetailDto(from, to, classId, className, d.Columns, rows, billed, paid, billed - paid);
    }

    /// <summary>The whole school's pupils a card or an abonement row stands for (<see cref="Filters"/>).</summary>
    public async Task<SubscriptionStudentsDto> StudentsAsync(
        DateOnly from, DateOnly to, string filter, CancellationToken ct = default)
    {
        if (!IsFilter(filter))
            throw BillingRuleException.Invalid("invalid_filter", $"Noma'lum filtr: {filter}");
        var d = await LoadAsync(from, to, ct);

        var picked = d.Pupils.Values.Where(p => Matches(d, p, filter));
        var rows = (filter == "debt"
                ? picked.Select(p => Row(d, p)).OrderByDescending(r => r.Debt).ThenBy(r => r.FullName, StringComparer.Ordinal)
                : picked.OrderBy(p => p.Order).ThenBy(p => p.FullName, StringComparer.Ordinal).Select(p => Row(d, p)))
            .ToList();
        var (billed, paid) = (rows.Sum(r => r.Billed), rows.Sum(r => r.Paid));
        return new SubscriptionStudentsDto(from, to, filter, d.Columns, rows, billed, paid, billed - paid);
    }

    // =================================================================
    //  Bitta ta'rif: kartalar, ustunlar va ro'yxatlar
    // =================================================================

    private static bool Matches(Data d, Pupil p, string filter)
    {
        var subs = d.SubsOf[p.Id];
        return filter switch
        {
            "everyone" => true,
            "all" => p.InSchool,
            "subscribed" => p.InSchool && subs.Any(),
            "no-tuition" => p.InSchool && !subs.Any(s => d.Tuition.Contains(s.CategoryId)),
            "billed" => d.MoneyOf[p.Id].Sum(m => m.Billed) != 0m,
            "debt" => d.MoneyOf[p.Id].Sum(m => m.Billed - m.Paid) > 0m,
            _ when filter.StartsWith("plan:", StringComparison.Ordinal)
                   && Guid.TryParse(filter["plan:".Length..], out var planId) => OnPlan(d, p, planId),
            _ => false,
        };
    }

    /// <summary>On the abonement in the period — by a subscription, or (no abonement) by an uncovered invoice.</summary>
    private static bool OnPlan(Data d, Pupil p, Guid planId) =>
        d.SubsOf[p.Id].Any(s => s.PlanId == planId) || d.MoneyOf[p.Id].Any(m => m.PlanId == planId);

    private static SubscriptionPlanCellDto Cell(Data d, IReadOnlyCollection<Pupil> pupils, Guid planId)
    {
        var money = pupils.SelectMany(p => d.MoneyOf[p.Id]).Where(m => m.PlanId == planId).ToList();
        return new SubscriptionPlanCellDto(planId, pupils.Count(p => OnPlan(d, p, planId)),
            money.Sum(m => m.Billed), money.Sum(m => m.Paid));
    }

    private static SubscriptionStudentRowDto Row(Data d, Pupil p)
    {
        var categoryOrder = d.Columns.Select(c => c.CategoryCode).Distinct().ToList();
        var planName = d.Columns.ToDictionary(c => c.PlanId, c => c.Name);
        var money = d.MoneyOf[p.Id].ToList();
        var plans = d.SubsOf[p.Id]
            .Select(s => (s, cat: d.Categories.GetValueOrDefault(s.CategoryId)))
            .OrderBy(x => categoryOrder.IndexOf(x.cat?.Code ?? "") is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(x => x.s.StartsOn)
            .Select(x =>
            {
                var sm = money.Where(m => m.SubscriptionId == x.s.Id).ToList();
                return new SubscriptionStudentPlanDto(
                    x.s.Id, x.s.PlanId, planName.GetValueOrDefault(x.s.PlanId, "—"),
                    x.cat?.Code ?? "", x.cat?.Name ?? "",
                    x.s.MonthlyAmount, x.s.StartsOn, x.s.EndsOn, x.s.Detail,
                    sm.Sum(m => m.Billed), sm.Sum(m => m.Paid));
            })
            .ToList();
        var billed = money.Sum(m => m.Billed);
        var paid = money.Sum(m => m.Paid);
        return new SubscriptionStudentRowDto(
            p.Id, p.FullName, plans, billed, paid, billed - paid,
            p.RowId, p.ClassName, p.InSchool, money.Where(m => m.PlanId == NoPlan).Sum(m => m.Billed));
    }

    // =================================================================
    //  Manbalar
    // =================================================================

    private async Task<Data> LoadAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        RequireRange(from, to);

        var classes = await db.Classes.AsNoTracking().Where(c => !c.IsArchived)
            .OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync(ct);
        var classByName = classes.Select((c, i) => (c, i)).GroupBy(x => x.c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var pupils = (await db.Students.AsNoTracking().Where(s => !s.IsArchived)
                .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync(ct))
            .Select(s => classByName.TryGetValue(s.ClassName ?? "", out var c)
                ? new Pupil(s.Id, s.FullName, c.c.Id, c.c.Name, true, c.i)
                : new Pupil(s.Id, s.FullName, NoClass, NoClassName, true, classes.Count))
            .ToDictionary(p => p.Id, StringComparer.Ordinal);

        var categories = await db.FeeCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var periodSubs = await db.StudentSubscriptions.AsNoTracking()
            .Where(s => s.StartsOn <= to && (s.EndsOn == null || s.EndsOn >= from))
            .Select(s => new Sub(s.Id, s.StudentId, s.CategoryId, s.PlanId ?? NoPlan, s.MonthlyAmount,
                s.StartsOn, s.EndsOn, s.Detail))
            .ToListAsync(ct);
        var (money, covering) = await MoneyAsync(from, to, ct);

        // An abonement that covers an invoice month of the period belongs to the period too.
        var subs = periodSubs.Concat(covering.Where(c => periodSubs.All(s => s.Id != c.Id))).ToList();

        // Pupils who left the school only appear when the period has something of theirs.
        var missing = subs.Select(s => s.StudentId).Concat(money.Select(m => m.StudentId))
            .Where(id => !pupils.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            foreach (var s in await db.Students.AsNoTracking().Where(s => missing.Contains(s.Id))
                         .Select(s => new { s.Id, s.FullName }).ToListAsync(ct))
                pupils[s.Id] = new Pupil(s.Id, s.FullName, Archived, ArchivedName, false, classes.Count + 1);

        return new Data
        {
            Classes = classes,
            Pupils = pupils,
            Categories = categories,
            Columns = await ColumnsAsync(categories, money.Any(m => m.PlanId == NoPlan), ct),
            Tuition = [.. categories.Values.Where(c => c.Code == FeeCategoryCode.Tuition).Select(c => c.Id)],
            SubsOf = subs.ToLookup(s => s.StudentId, StringComparer.Ordinal),
            MoneyOf = money.ToLookup(m => m.StudentId, StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Invoices of the period's months with what is paid on them, each tied to the abonement that
    /// covers its month — and those covering abonements (one may have ended before a custom period).
    /// </summary>
    private async Task<(List<Money> Money, List<Sub> Covering)> MoneyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);
        var rows = await db.Invoices.AsNoTracking()
            .Where(i => i.PeriodMonth >= firstMonth && i.PeriodMonth <= lastMonth
                        && i.RemovedAt == null && i.VoidedAt == null && i.Status != InvoiceStatus.Void)
            .Select(i => new { i.Id, i.StudentId, i.CategoryId, i.PeriodMonth, Payable = i.Amount - i.Discount })
            .ToListAsync(ct);
        if (rows.Count == 0) return ([], []);

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

        var covering = new Dictionary<Guid, Sub>();
        var money = rows.Select(r =>
        {
            var monthEnd = r.PeriodMonth.AddMonths(1).AddDays(-1);
            // The abonement that covers the month; with two (a change mid-month) — the later one.
            var sub = byOwner.GetValueOrDefault((r.StudentId, r.CategoryId))?
                .Where(s => s.StartsOn <= monthEnd && (s.EndsOn == null || s.EndsOn >= r.PeriodMonth))
                .OrderByDescending(s => s.StartsOn)
                .FirstOrDefault();
            if (sub is not null) covering[sub.Id] = sub;
            return new Money(r.StudentId, sub?.PlanId ?? NoPlan, sub?.Id, r.Payable, paid.GetValueOrDefault(r.Id));
        }).ToList();
        return (money, [.. covering.Values]);
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
