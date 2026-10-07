using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  "Kasallik chegirmasi" — docs/modules/billing-plans.md §9 (client choices, 2026-09-28):
//  put by the cashier in the payment form, no approval, off EVERY abonement of the month:
//
//      daily     = invoice gross ÷ lesson days of the month (by the abonement's schedule, §7)
//      deduction = min(gross × days ÷ lesson days, what is still unpaid)
//
//  Sick days above a line's lesson days count as the whole month for it. A fully paid month
//  gets nothing — refunds are their own flow.
//
//  §9.1 (client, 2026-09-28: "adashib ketsa bekor qilish button ham bo'lsin"): one application
//  is one adjustment group; the cashier can cancel it whole. Cancelling writes reversal rows and
//  puts the debt back — also on a month paid since, whose invoice then owes the sum again.
// ===========================================================================

public record SickDiscountRequest(string StudentId, string Month, int Days, string? Note = null);

public record SickDiscountLineDto(
    Guid InvoiceId, string CategoryName, string? PlanName,
    decimal Gross, decimal Remaining, int LessonDays, decimal Daily, decimal Deduction);

/// <summary><see cref="GroupId"/> is set on an applied one — the handle to cancel it with.</summary>
public record SickDiscountDto(string Month, int Days, decimal Total, IReadOnlyList<SickDiscountLineDto> Lines, Guid? GroupId = null);

/// <summary>A sickness discount standing on the student's invoices (not cancelled).</summary>
public record SickDiscountEntryDto(
    Guid GroupId, string Month, int Days, decimal Total, string? Note, DateTimeOffset CreatedAt, string? CreatedByName);

public record SickDiscountCancelDto(Guid GroupId, string Month, decimal Total);

public sealed class SickDiscountService(IAppDbContext db, AuditService audit)
{
    private const int MoneyScale = 2;
    public const int MaxDays = 31;

    /// <summary>What the cashier would take off — nothing is written.</summary>
    public async Task<SickDiscountDto> PreviewAsync(string studentId, string month, int days, CancellationToken ct = default)
    {
        var monthStart = RequireMonth(month);
        RequireDays(days);
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == studentId && i.PeriodMonth == monthStart && i.Status != InvoiceStatus.Void)
            .ToListAsync(ct);
        var paid = await PaidAsync(invoices.Select(i => i.Id).ToList(), ct);
        var lines = await LinesAsync(invoices, paid, monthStart, days, ct);
        return new SickDiscountDto(month, days, lines.Sum(l => l.Deduction), lines);
    }

    /// <summary>Applies it under the student's invoice locks; one ledger batch per invoice.</summary>
    public async Task<SickDiscountDto> ApplyAsync(SickDiscountRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actorId))
            throw BillingRuleException.Invalid("actor_required", "Amalni bajaruvchi aniqlanmadi.");
        var monthStart = RequireMonth(request.Month);
        RequireDays(request.Days);
        await ArchivedStudents.EnsureActiveAsync(db, request.StudentId, ct); // 2026-10-08

        await using var tx = await db.BeginTransactionAsync(ct);
        var adjuster = new InvoiceAdjustmentService(db, audit);
        var (all, paid) = await adjuster.LockStudentAsync(request.StudentId, ct);
        var invoices = all.Where(i => i.PeriodMonth == monthStart).ToList();
        var lines = await LinesAsync(invoices, paid, monthStart, request.Days, ct);
        if (lines.Sum(l => l.Deduction) <= 0m)
            throw BillingRuleException.Conflict("nothing_to_deduct",
                $"{request.Month} oyi uchun ayiradigan to'lanmagan summa yo'q.");

        var groupId = Guid.NewGuid();
        var applied = new List<SickDiscountLineDto>();
        foreach (var line in lines)
        {
            var invoice = invoices.Single(i => i.Id == line.InvoiceId);
            var taken = line.Deduction <= 0m ? 0m : await adjuster.ApplyAsync(
                invoice, paid, line.Deduction, InvoiceAdjustmentKind.Sick,
                Math.Min(request.Days, line.LessonDays), discountId: null, request.Note, groupId, actorId, ct);
            applied.Add(line with { Deduction = taken, Remaining = InvoiceAdjustmentService.Remaining(invoice, paid) });
        }
        await tx.CommitAsync(ct);
        return new SickDiscountDto(request.Month, request.Days, applied.Sum(l => l.Deduction), applied, groupId);
    }

    /// <summary>The student's sickness discounts that still stand, newest first.</summary>
    public async Task<IReadOnlyList<SickDiscountEntryDto>> ListAsync(string studentId, CancellationToken ct = default)
    {
        var rows = await (
                from a in db.InvoiceAdjustments.AsNoTracking()
                join i in db.Invoices.AsNoTracking() on a.InvoiceId equals i.Id
                where i.StudentId == studentId && a.Kind == InvoiceAdjustmentKind.Sick && a.ReversalOf == null
                      && !db.InvoiceAdjustments.Any(r => r.ReversalOf == a.Id)
                join u in db.Users.AsNoTracking() on a.CreatedBy equals u.Id into users
                from u in users.DefaultIfEmpty()
                select new { a.GroupId, i.PeriodMonth, a.SickDays, a.Amount, a.Note, a.CreatedAt, Name = u == null ? null : u.FullName })
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.GroupId)
            .Select(g => new SickDiscountEntryDto(
                g.Key, g.Min(r => r.PeriodMonth).ToString("yyyy-MM"), g.Max(r => r.SickDays ?? 0), g.Sum(r => r.Amount),
                g.First().Note, g.Min(r => r.CreatedAt), g.First().Name))
            .OrderByDescending(e => e.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Cancels one application whole (§9.1) under the student's invoice locks. Refused when it is
    /// already cancelled; a month voided since cannot take the debt back.
    /// </summary>
    public async Task<SickDiscountCancelDto> CancelAsync(Guid groupId, string actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw BillingRuleException.Invalid("actor_required", "Amalni bajaruvchi aniqlanmadi.");
        var studentId = await (
                from a in db.InvoiceAdjustments.AsNoTracking()
                join i in db.Invoices.AsNoTracking() on a.InvoiceId equals i.Id
                where a.GroupId == groupId && a.Kind == InvoiceAdjustmentKind.Sick && a.ReversalOf == null
                select i.StudentId)
            .FirstOrDefaultAsync(ct)
            ?? throw BillingRuleException.NotFound("sick_discount_not_found", "Kasallik chegirmasi topilmadi.");
        // Cancelling brings the debt back — not for an archived pupil (2026-10-08).
        await ArchivedStudents.EnsureActiveAsync(db, studentId, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        var adjuster = new InvoiceAdjustmentService(db, audit);
        var (invoices, paid) = await adjuster.LockStudentAsync(studentId, ct);
        // Read under the locks — a second click waits for the first and then finds nothing left.
        var rows = await db.InvoiceAdjustments.AsNoTracking()
            .Where(a => a.GroupId == groupId && a.Kind == InvoiceAdjustmentKind.Sick && a.ReversalOf == null
                        && !db.InvoiceAdjustments.Any(r => r.ReversalOf == a.Id))
            .OrderBy(a => a.InvoiceId)
            .ToListAsync(ct);
        if (rows.Count == 0)
            throw BillingRuleException.Conflict("sick_discount_already_cancelled", "Bu kasallik chegirmasi allaqachon bekor qilingan.");

        var reversalGroup = Guid.NewGuid();
        var month = default(DateOnly);
        foreach (var row in rows)
        {
            var invoice = invoices.SingleOrDefault(i => i.Id == row.InvoiceId)
                ?? throw BillingRuleException.Conflict("invoice_void",
                    "Chegirma qo'llangan hisob-faktura bekor qilingan — kasallik chegirmasini qaytarib bo'lmaydi.");
            month = invoice.PeriodMonth;
            await adjuster.ReverseAsync(row, invoice, paid, "Kasallik chegirmasi bekor qilindi", reversalGroup, actorId, ct);
        }
        await tx.CommitAsync(ct);
        return new SickDiscountCancelDto(groupId, month.ToString("yyyy-MM"), rows.Sum(r => r.Amount));
    }

    private async Task<List<SickDiscountLineDto>> LinesAsync(
        List<Invoice> invoices, IReadOnlyDictionary<Guid, decimal> paid, DateOnly monthStart, int days, CancellationToken ct)
    {
        if (invoices.Count == 0)
            throw BillingRuleException.Conflict("nothing_to_deduct", "Bu oy uchun hisob-faktura yo'q.");

        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var studentId = invoices[0].StudentId;
        var categoryIds = invoices.Select(i => i.CategoryId).ToList();
        // The subscription behind each invoice — the latest one overlapping the month, as accrual picks it.
        var subs = await (
            from s in db.StudentSubscriptions.AsNoTracking()
            where s.StudentId == studentId && categoryIds.Contains(s.CategoryId)
                  && s.StartsOn <= monthEnd && (s.EndsOn == null || s.EndsOn >= monthStart)
            join pl in db.SubscriptionPlans.AsNoTracking() on s.PlanId equals (Guid?)pl.Id into plans
            from pl in plans.DefaultIfEmpty()
            select new { s.CategoryId, s.StartsOn, PlanName = pl == null ? null : pl.Name, DaysPerWeek = pl == null ? null : pl.DaysPerWeek })
            .ToListAsync(ct);
        var names = await db.FeeCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var holidays = await LessonDays.HolidaysAsync(db, monthStart, ct);

        var lines = new List<SickDiscountLineDto>();
        foreach (var invoice in invoices.OrderBy(i => names.GetValueOrDefault(i.CategoryId)))
        {
            var sub = subs.Where(s => s.CategoryId == invoice.CategoryId).OrderByDescending(s => s.StartsOn).FirstOrDefault();
            var lessonDays = LessonDays.Count(monthStart, sub?.DaysPerWeek, holidays);
            var counted = Math.Min(days, lessonDays);
            var gross = invoice.Amount;
            var daily = decimal.Round(gross / lessonDays, MoneyScale);
            var wanted = decimal.Round(gross * counted / lessonDays, MoneyScale);
            var remaining = InvoiceAdjustmentService.Remaining(invoice, paid);
            lines.Add(new SickDiscountLineDto(
                invoice.Id, names.GetValueOrDefault(invoice.CategoryId, "—"), sub?.PlanName,
                gross, remaining, lessonDays, daily, Math.Min(wanted, remaining)));
        }
        return lines;
    }

    private async Task<Dictionary<Guid, decimal>> PaidAsync(List<Guid> invoiceIds, CancellationToken ct)
    {
        if (invoiceIds.Count == 0) return [];
        var rows = await (
                from a in db.PaymentAllocations.AsNoTracking()
                join p in db.Payments.AsNoTracking() on a.PaymentId equals p.Id
                where p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id)
                      && invoiceIds.Contains(a.InvoiceId)
                select a)
            .GroupBy(a => a.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.InvoiceId, r => r.Total);
    }

    private static DateOnly RequireMonth(string? month)
    {
        if (month is { Length: 7 } && DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var start)) return start;
        throw BillingRuleException.Invalid("invalid_month", "Oy noto'g'ri (yyyy-MM).");
    }

    private static void RequireDays(int days)
    {
        if (days < 1 || days > MaxDays)
            throw BillingRuleException.Invalid("invalid_days", $"Kasal kunlar soni 1 dan {MaxDays} gacha bo'lishi kerak.");
    }
}
