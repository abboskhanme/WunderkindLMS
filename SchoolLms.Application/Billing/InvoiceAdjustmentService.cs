using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Invoice adjustment — docs/modules/billing-plans.md §10.
//
//  Until now a written month could only be voided whole (and not once money is on it). A
//  sickness discount or a catalog discount reaching an invoiced month needs a PART of it off:
//
//   * `invoices.discount += amount` — every "remaining" in the system (cashier, debtors, arrears,
//     P&L) is `amount − discount − paid`, so they all follow without being touched;
//   * a ledger batch `invoice_adjustment`: DEBIT revenue:<category>, CREDIT receivable — accrual
//     posted revenue net of discount, so this takes the same sum off both;
//   * an append-only `invoice_adjustments` row: who, when, why, how many sick days.
//
//  Never below what is paid: allocations are append-only, an over-reduced invoice would lose
//  the excess from the student's balance. The caller holds the student's invoice locks (the same
//  advisory keys PaymentService and VoidAsync take), inside its transaction.
//
//  Cancelling (§9.1) is the mirror image and never edits the row: a reversal row, the discount
//  back down, DEBIT receivable / CREDIT revenue. The debt only grows, so paid money is never
//  stranded by it.
// ===========================================================================

public sealed class InvoiceAdjustmentService(IAppDbContext db, AuditService audit)
{
    private const int MoneyScale = 2;
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))";
    private readonly DbContext ef = db as DbContext
        ?? throw new ArgumentException("InvoiceAdjustmentService needs the EF context (advisory locks).", nameof(db));

    /// <summary>
    /// Locks every non-void invoice of the student in id order (deadlock-free with PaymentService)
    /// and returns them TRACKED with what is already paid on each. Call inside a transaction.
    /// </summary>
    public async Task<(List<Invoice> Invoices, Dictionary<Guid, decimal> Paid)> LockStudentAsync(
        string studentId, CancellationToken ct)
    {
        var ids = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == studentId && i.Status != InvoiceStatus.Void)
            .Select(i => i.Id).ToListAsync(ct);
        foreach (var id in ids.OrderBy(id => id))
            await ef.Database.ExecuteSqlRawAsync(LockSql, [InvoiceService.VoidLockKey(id)], ct);

        var invoices = await db.Invoices
            .Where(i => i.StudentId == studentId && i.Status != InvoiceStatus.Void)
            .ToListAsync(ct);
        return (invoices, await PaidAsync(invoices.Select(i => i.Id).ToList(), ct));
    }

    /// <summary>What is still unpaid on the invoice.</summary>
    public static decimal Remaining(Invoice invoice, IReadOnlyDictionary<Guid, decimal> paid) =>
        Math.Max(0m, invoice.Amount - invoice.Discount - paid.GetValueOrDefault(invoice.Id));

    /// <summary>
    /// Takes up to <paramref name="wanted"/> off a locked, tracked invoice; returns what was applied
    /// (0 = nothing left to reduce). Posts the ledger batch — which saves the context.
    /// </summary>
    public async Task<decimal> ApplyAsync(
        Invoice invoice, IReadOnlyDictionary<Guid, decimal> paid, decimal wanted, string kind,
        int? sickDays, Guid? discountId, string? note, Guid groupId, string actorId, CancellationToken ct)
    {
        var amount = decimal.Round(Math.Min(wanted, Remaining(invoice, paid)), MoneyScale);
        if (amount <= 0m) return 0m;

        var code = await db.FeeCategories.AsNoTracking()
            .Where(c => c.Id == invoice.CategoryId).Select(c => c.Code).FirstOrDefaultAsync(ct);
        var before = new { invoice.Amount, invoice.Discount, invoice.Status };
        invoice.Discount += amount;
        var allocated = paid.GetValueOrDefault(invoice.Id);
        invoice.Status = InvoiceService.StatusFor(invoice.Amount - invoice.Discount, allocated);

        var row = new InvoiceAdjustment
        {
            InvoiceId = invoice.Id,
            Kind = kind,
            Amount = amount,
            SickDays = sickDays,
            DiscountId = discountId,
            Note = Clip(note),
            GroupId = groupId,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        };
        db.InvoiceAdjustments.Add(row);

        var what = Label(kind, sickDays);
        audit.Record(AuditService.EntityInvoice, invoice.Id.ToString(), "adjust",
            $"{invoice.PeriodMonth:yyyy-MM} hisobidan {what}: {AuditService.Money(amount)} so'm ayrildi",
            before: before, after: new { invoice.Amount, invoice.Discount, invoice.Status },
            studentId: invoice.StudentId);

        var memo = $"{what}, {invoice.PeriodMonth:yyyy-MM}";
        await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.RevenueFor(code), LedgerDirection.Debit, amount,
                LedgerRefType.InvoiceAdjustment, row.Id, AppClock.Today, memo),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, amount,
                LedgerRefType.InvoiceAdjustment, row.Id, AppClock.Today, memo),
        ], actorId, ct);
        return amount;
    }

    /// <summary>
    /// Gives an adjustment's sum back to a locked, tracked invoice: a reversal row in
    /// <paramref name="groupId"/>, the discount down, the status recomputed, the ledger batch
    /// mirrored. The caller checks the row is not reversed yet (the unique index backs it up).
    /// </summary>
    public async Task ReverseAsync(
        InvoiceAdjustment original, Invoice invoice, IReadOnlyDictionary<Guid, decimal> paid,
        string? note, Guid groupId, string actorId, CancellationToken ct)
    {
        if (original.InvoiceId != invoice.Id)
            throw new InvalidOperationException("The adjustment belongs to another invoice.");
        if (invoice.Discount < original.Amount)
            throw BillingRuleException.Conflict("adjustment_not_reversible",
                "Hisob-fakturadagi chegirma bu summadan kam — bekor qilib bo'lmaydi.");

        var code = await db.FeeCategories.AsNoTracking()
            .Where(c => c.Id == invoice.CategoryId).Select(c => c.Code).FirstOrDefaultAsync(ct);
        var before = new { invoice.Amount, invoice.Discount, invoice.Status };
        invoice.Discount -= original.Amount;
        invoice.Status = InvoiceService.StatusFor(invoice.Amount - invoice.Discount, paid.GetValueOrDefault(invoice.Id));

        var row = new InvoiceAdjustment
        {
            InvoiceId = invoice.Id,
            Kind = original.Kind,
            Amount = original.Amount,
            SickDays = original.SickDays,
            DiscountId = original.DiscountId,
            Note = Clip(note),
            GroupId = groupId,
            ReversalOf = original.Id,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        };
        db.InvoiceAdjustments.Add(row);

        var what = Label(original.Kind, original.SickDays);
        audit.Record(AuditService.EntityInvoice, invoice.Id.ToString(), "adjust_cancel",
            $"{invoice.PeriodMonth:yyyy-MM} hisobidagi {what} bekor qilindi: {AuditService.Money(original.Amount)} so'm qaytdi",
            before: before, after: new { invoice.Amount, invoice.Discount, invoice.Status },
            studentId: invoice.StudentId);

        var memo = $"{what} bekor qilindi, {invoice.PeriodMonth:yyyy-MM}";
        await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, original.Amount,
                LedgerRefType.InvoiceAdjustment, row.Id, AppClock.Today, memo),
            new LedgerPosting(Accounts.RevenueFor(code), LedgerDirection.Credit, original.Amount,
                LedgerRefType.InvoiceAdjustment, row.Id, AppClock.Today, memo),
        ], actorId, ct);
    }

    /// <summary>
    /// Sickness discount still standing on each invoice (applied minus cancelled) — the part of
    /// <see cref="Invoice.Discount"/> that is not the catalog / approved discount.
    /// </summary>
    public async Task<Dictionary<Guid, decimal>> SickNetAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        if (invoiceIds.Count == 0) return [];
        var rows = await db.InvoiceAdjustments.AsNoTracking()
            .Where(a => a.Kind == InvoiceAdjustmentKind.Sick && invoiceIds.Contains(a.InvoiceId))
            .GroupBy(a => a.InvoiceId)
            .Select(g => new
            {
                InvoiceId = g.Key,
                Net = g.Sum(a => a.ReversalOf == null ? a.Amount : -a.Amount),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.InvoiceId, r => r.Net);
    }

    /// <summary>Audit / ledger wording of an adjustment kind.</summary>
    private static string Label(string kind, int? sickDays) => kind switch
    {
        InvoiceAdjustmentKind.Sick => $"kasallik chegirmasi ({sickDays} kun)",
        InvoiceAdjustmentKind.Withdrawal => "chiqib ketish (o'qilmagan kunlar)",
        _ => "chegirma",
    };

    private static string? Clip(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];

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
}

/// <summary>Lesson days of a month by an abonement's schedule (billing-plans.md §7).</summary>
public static class LessonDays
{
    /// <summary>
    /// Maktab haftasi — dushanbadan shanbagacha (dars jadvali shunday). Jadvali belgilanmagan
    /// ("Butun oy") abonement "chiqib ketdi" hisobida shu bilan sanaladi: mijoz, 2026-10-06 —
    /// "yakshanba va bayramlar sanalmaydi".
    /// </summary>
    public const int SchoolWeek = 6;

    /// <summary>
    /// Dars kunlari <paramref name="from"/>..<paramref name="to"/> oralig'ida (ikkalasi ham kiradi),
    /// bitta oy ichida. <paramref name="daysPerWeek"/> — <see cref="Count"/> dagi bilan bir xil ma'no.
    /// Oraliq bo'sh bo'lsa 0.
    /// </summary>
    public static int CountBetween(DateOnly from, DateOnly to, int daysPerWeek, IReadOnlySet<DateOnly> holidays)
    {
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var iso = d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;
            if (iso <= daysPerWeek && !holidays.Contains(d)) count++;
        }
        return count;
    }

    /// <summary>
    /// null ("Butun oy") → every calendar day; N → days whose ISO weekday ≤ N (5 = Mon–Fri,
    /// 6 = Mon–Sat, 7 = all), minus school holidays. Never below 1.
    /// </summary>
    public static int Count(DateOnly monthStart, int? daysPerWeek, IReadOnlySet<DateOnly> holidays)
    {
        var days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        if (daysPerWeek is not { } perWeek) return days;
        var count = 0;
        for (var d = 1; d <= days; d++)
        {
            var date = new DateOnly(monthStart.Year, monthStart.Month, d);
            var iso = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            if (iso <= perWeek && !holidays.Contains(date)) count++;
        }
        return Math.Max(1, count);
    }

    /// <summary>School holidays (<c>holidays.date</c> "yyyy-MM-dd") inside the month.</summary>
    public static async Task<HashSet<DateOnly>> HolidaysAsync(IAppDbContext db, DateOnly monthStart, CancellationToken ct)
    {
        var prefix = monthStart.ToString("yyyy-MM");
        var raw = await db.Holidays.AsNoTracking().Where(h => h.Date.StartsWith(prefix)).Select(h => h.Date).ToListAsync(ct);
        return [.. raw.Select(r => DateOnly.TryParse(r, out var d) ? d : (DateOnly?)null).OfType<DateOnly>()];
    }
}
