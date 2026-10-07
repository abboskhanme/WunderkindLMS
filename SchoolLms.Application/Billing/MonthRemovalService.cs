using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  "Abonement oyini butunlay o'chirish" (client, 2026-10-07): "student abonimentlarini to'langan
//  bo'lsa ham ixtiyoriy oy uchun tanlab butunlay o'chirish mumkin bo'lsin, bunda tranzaksiyalari
//  arxiv sifatida qolsin, lekin hisob-kitobdan o'chirilsin". The client chose: the money paid for
//  the month leaves the accounts too (not the advance).
//
//  Payments, allocations and the ledger are append-only, so "removed" is built from reversals,
//  all in ONE transaction (a half-done removal would lose money without losing the month):
//
//    for every payment that paid a selected month:
//      storno the whole payment (mirror ledger batch, the original row stays — the archive);
//      if it also paid other months or left an advance, re-issue the rest as a new receipt with the
//      very same allocations — so only the removed months' share leaves the cash and the balance;
//    for every selected month:
//      undo its standing adjustments (discounts, sick, withdrawal), reverse its accrual batch,
//      status void + removed_at — accrual does not bill it again when the subscription is edited.
//
//  Optionally the subscription itself goes when none of its months is left.
//  Two-person rule — for money only (client, 2026-10-07): whoever took a receipt cannot remove the
//  month it paid; the month's accrual (background job / saved subscription) does not count as own.
// ===========================================================================

public record MonthRemovalPaymentDto(Guid PaymentId, long ReceiptNo, DateTimeOffset ReceivedAt, decimal Amount, decimal Share, bool Own);

/// <param name="BlockedReason">Why THIS user cannot remove the month (two-person rule) — another admin can.</param>
public record MonthRemovalLineDto(
    Guid InvoiceId, string Month, decimal Amount, decimal Discount, decimal Payable, decimal Paid, string Status,
    IReadOnlyList<MonthRemovalPaymentDto> Payments, string? BlockedReason = null);

public record MonthRemovalPreviewDto(
    Guid SubscriptionId, string CategoryName, decimal MonthlyAmount, DateOnly StartsOn, DateOnly? EndsOn,
    IReadOnlyList<MonthRemovalLineDto> Months, IReadOnlyList<string> RemovedMonths);

public record MonthRemovalRequest(IReadOnlyList<Guid> InvoiceIds, string Reason, bool DeleteSubscription = false);

public record MonthRemovalResultDto(
    int Months, decimal RemovedPayable, decimal ReversedPaid, int PaymentsReversed, int PaymentsReissued, bool SubscriptionDeleted);

public sealed class MonthRemovalService(IAppDbContext db, AuditService audit)
{
    private const int MoneyScale = 2;
    private const string SubscriptionAuditEntity = "StudentSubscription";

    private static string Month(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The subscription's months that can be removed, with what was paid on each and by which receipts.</summary>
    public async Task<MonthRemovalPreviewDto> PreviewAsync(Guid subscriptionId, string actorId, CancellationToken ct = default)
    {
        var sub = await db.StudentSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
                  ?? throw BillingRuleException.NotFound("subscription_not_found", "Abonement topilmadi.");
        var category = await db.FeeCategories.AsNoTracking().Where(c => c.Id == sub.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "";
        var all = await InRange(sub).AsNoTracking().OrderBy(i => i.PeriodMonth).ToListAsync(ct);
        var live = all.Where(i => i.Status != InvoiceStatus.Void).ToList();
        var net = await NetAsync(sub.StudentId, ct);
        var paymentIds = net.Keys.Where(k => live.Any(i => i.Id == k.InvoiceId)).Select(k => k.PaymentId).Distinct().ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => paymentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var lines = live.Select(i =>
        {
            var shares = net.Where(kv => kv.Key.InvoiceId == i.Id && kv.Value > 0m && payments.ContainsKey(kv.Key.PaymentId))
                .Select(kv =>
                {
                    var p = payments[kv.Key.PaymentId];
                    return new MonthRemovalPaymentDto(p.Id, p.ReceiptNo, p.ReceivedAt, p.Amount, kv.Value,
                        string.Equals(p.CashierId, actorId, StringComparison.Ordinal));
                })
                .OrderBy(x => x.ReceivedAt).ToList();
            var own = shares.FirstOrDefault(x => x.Own);
            var blocked = own is null ? null : $"Chek #{own.ReceiptNo} ni o'zingiz qabul qilgansiz — boshqa admin yoki direktor o'chiradi";
            return new MonthRemovalLineDto(i.Id, Month(i.PeriodMonth), i.Amount, i.Discount, i.Amount - i.Discount,
                shares.Sum(x => x.Share), i.Status, shares, blocked);
        }).ToList();

        return new MonthRemovalPreviewDto(sub.Id, category, sub.MonthlyAmount, sub.StartsOn, sub.EndsOn, lines,
            [.. all.Where(i => i.RemovedAt != null).Select(i => Month(i.PeriodMonth))]);
    }

    public async Task<MonthRemovalResultDto> RemoveAsync(
        Guid subscriptionId, MonthRemovalRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actorId))
            throw BillingRuleException.Invalid("actor_required", "Amalni bajaruvchi aniqlanmadi.");
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length == 0)
            throw BillingRuleException.Invalid("reason_required", "O'chirish sababi majburiy — u arxivda qoladi.");
        if (reason.Length > 500) reason = reason[..500];
        var ids = (request.InvoiceIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            throw BillingRuleException.Invalid("nothing_selected", "O'chiriladigan oyni tanlang.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var adjuster = new InvoiceAdjustmentService(db, audit);
        var ledger = new LedgerService(db);
        var boxes = new CashBoxService(db);

        var sub = await db.StudentSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
                  ?? throw BillingRuleException.NotFound("subscription_not_found", "Abonement topilmadi.");
        var (invoices, _) = await adjuster.LockStudentAsync(sub.StudentId, ct);
        var inRange = await InRange(sub).Select(i => i.Id).ToListAsync(ct);
        var selected = invoices.Where(i => ids.Contains(i.Id)).OrderBy(i => i.PeriodMonth).ToList();
        if (selected.Count != ids.Count || selected.Any(i => !inRange.Contains(i.Id)))
            throw BillingRuleException.Conflict("month_not_removable",
                "Tanlangan oylardan biri bu abonementga tegishli emas yoki allaqachon bekor qilingan — oynani qayta oching.");
        var selectedIds = selected.Select(i => i.Id).ToHashSet();
        var months = string.Join(", ", selected.Select(i => Month(i.PeriodMonth)));
        var now = AppClock.NowInstant;

        // ---- 1. Payments: storno each that paid a selected month; re-issue what it paid elsewhere ----
        var net = await NetAsync(sub.StudentId, ct);
        var touchedPaymentIds = net.Where(kv => selectedIds.Contains(kv.Key.InvoiceId) && kv.Value > 0m)
            .Select(kv => kv.Key.PaymentId).Distinct().ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => touchedPaymentIds.Contains(p.Id))
            .OrderBy(p => p.ReceivedAt).ToListAsync(ct);
        var reversedPaid = 0m;
        var reissued = 0;
        var touchedInvoiceIds = new HashSet<Guid>();

        foreach (var original in payments)
        {
            if (string.Equals(original.CashierId, actorId, StringComparison.Ordinal))
                throw BillingRuleException.Forbidden("own_payment_reversal",
                    $"Chek #{original.ReceiptNo} ni o'zingiz qabul qilgansiz — bu oyni boshqa admin yoki direktor o'chirishi kerak (ikki shaxs nazorati).");
            var anchor = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefType == LedgerRefType.Payment && e.RefId == original.Id && e.ReversalOf == null)
                .OrderBy(e => e.Id).Select(e => (long?)e.Id).FirstOrDefaultAsync(ct)
                ?? throw BillingRuleException.Conflict("no_ledger_batch",
                    $"Chek #{original.ReceiptNo} ning jurnal yozuvi topilmadi — moliya mas'uliga murojaat qiling.");

            var box = await ActiveBoxAsync(original.CashBoxId, ct);
            db.Payments.Add(new Payment
            {
                ReceiptNo = await boxes.NextReceiptNoAsync(box, ct),
                StudentId = original.StudentId,
                Amount = original.Amount,
                Method = original.Method,
                CashBoxId = box,
                CashierId = actorId,
                Note = $"Oy o'chirildi ({months}): {reason}",
                ReceivedAt = now,
                ReversalOf = original.Id,
            });
            await db.SaveChangesAsync(ct);
            await ReverseLedgerAsync(() => ledger.ReverseAsync(anchor, $"Oy o'chirildi ({months}): {reason}", actorId, ct));

            var mine = net.Where(kv => kv.Key.PaymentId == original.Id).ToList();
            foreach (var kv in mine) touchedInvoiceIds.Add(kv.Key.InvoiceId);
            var removedShare = mine.Where(kv => selectedIds.Contains(kv.Key.InvoiceId) && kv.Value > 0m).Sum(kv => kv.Value);
            reversedPaid += removedShare;
            var rest = decimal.Round(original.Amount - removedShare, MoneyScale);
            if (rest > 0m)
            {
                var copy = new Payment
                {
                    ReceiptNo = await boxes.NextReceiptNoAsync(box, ct),
                    StudentId = original.StudentId,
                    Amount = rest,
                    Method = original.Method,
                    CashBoxId = box,
                    CashierId = actorId,
                    Note = $"Chek #{original.ReceiptNo} qayta rasmiylashtirildi — {months} o'chirildi",
                    ReceivedAt = now,
                };
                db.Payments.Add(copy);
                foreach (var kv in mine.Where(kv => !selectedIds.Contains(kv.Key.InvoiceId) && kv.Value > 0m))
                    db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = copy.Id, InvoiceId = kv.Key.InvoiceId, Amount = kv.Value });
                await db.SaveChangesAsync(ct);
                await ledger.PostAsync(
                [
                    new LedgerPosting(Accounts.SettlementFor(copy.Method), LedgerDirection.Debit, rest,
                        LedgerRefType.Payment, copy.Id, AppClock.Today, $"Chek #{copy.ReceiptNo} — #{original.ReceiptNo} qayta rasmiylashtirildi"),
                    new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, rest,
                        LedgerRefType.Payment, copy.Id, AppClock.Today, $"Chek #{copy.ReceiptNo} — #{original.ReceiptNo} qayta rasmiylashtirildi"),
                ], actorId, ct);
                reissued++;
            }

            audit.Record(AuditService.EntityPayment, original.Id.ToString("D"), "reverse",
                $"Oy o'chirildi ({months}): chek #{original.ReceiptNo} storno, {AuditService.Money(removedShare)} so'm hisobdan chiqdi"
                + (rest > 0m ? $", {AuditService.Money(rest)} so'm qolgan oylarga qayta rasmiylashtirildi" : ""),
                before: new { original.Id, original.ReceiptNo, original.Amount }, after: new { Removed = removedShare, Reissued = rest },
                studentId: original.StudentId);
        }

        // ---- 2. The months: adjustments back, accrual reversed, void + removed ----
        var paid = await PaidAsync(selectedIds, ct);
        var groupId = Guid.NewGuid();
        var removedPayable = 0m;
        foreach (var invoice in selected)
        {
            var standing = await db.InvoiceAdjustments
                .Where(a => a.InvoiceId == invoice.Id && a.ReversalOf == null
                            && !db.InvoiceAdjustments.Any(r => r.ReversalOf == a.Id))
                .OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
            foreach (var adjustment in standing)
                await adjuster.ReverseAsync(adjustment, invoice, paid, $"Oy o'chirildi: {reason}", groupId, actorId, ct);

            var accrual = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefType == LedgerRefType.Invoice && e.RefId == invoice.Id && e.ReversalOf == null)
                .OrderBy(e => e.Id).Select(e => new { e.Id, e.CreatedBy }).FirstOrDefaultAsync(ct);
            // The month's accrual is bookkeeping (background job / saved subscription), not anyone's own
            // cash — the two-person rule stays for the receipts above only (client, 2026-10-07).
            if (accrual is not null)
                await ReverseLedgerAsync(() => ledger.ReverseAccrualAsync(accrual.Id, $"Oy o'chirildi: {reason}", actorId, ct));

            var before = new { invoice.Id, invoice.PeriodMonth, invoice.Amount, invoice.Discount, invoice.Status };
            removedPayable += invoice.Amount - invoice.Discount;
            invoice.Status = InvoiceStatus.Void;
            invoice.VoidedAt = now;
            invoice.RemovedAt = now;
            invoice.RemovedBy = actorId;
            invoice.RemoveReason = reason;
            audit.Record(AuditService.EntityInvoice, invoice.Id.ToString("D"), "remove",
                $"{Month(invoice.PeriodMonth)} oyi butunlay o'chirildi: {AuditService.Money(invoice.Amount)} so'm — sabab: {reason}",
                before: before, after: new { invoice.Id, invoice.Status, invoice.RemovedAt, invoice.RemoveReason },
                studentId: invoice.StudentId);
        }

        // ---- 3. The other months those payments touched: status from the (re-issued) allocations ----
        var others = invoices.Where(i => touchedInvoiceIds.Contains(i.Id) && !selectedIds.Contains(i.Id)).ToList();
        var othersPaid = await PaidAsync(others.Select(i => i.Id).ToHashSet(), ct);
        foreach (var invoice in others)
            invoice.Status = InvoiceService.StatusFor(invoice.Amount - invoice.Discount, othersPaid.GetValueOrDefault(invoice.Id));

        // ---- 4. The subscription itself, when asked and nothing of it is left ----
        var deleted = false;
        if (request.DeleteSubscription)
        {
            var left = invoices.Count(i => inRange.Contains(i.Id) && !selectedIds.Contains(i.Id) && i.Status != InvoiceStatus.Void);
            if (left > 0)
                throw BillingRuleException.Conflict("subscription_has_months",
                    $"Abonementning yana {left} ta oyi bor — abonementni o'chirish uchun hamma oylarini tanlang.");
            audit.Record(SubscriptionAuditEntity, sub.Id.ToString(), "delete",
                $"Abonement o'chirildi (oylari: {months}) — sabab: {reason}",
                before: SubscriptionService.Snapshot(sub), studentId: sub.StudentId);
            db.StudentSubscriptions.Remove(sub);
            deleted = true;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new MonthRemovalResultDto(selected.Count, decimal.Round(removedPayable, MoneyScale),
            decimal.Round(reversedPaid, MoneyScale), payments.Count, reissued, deleted);
    }

    // =====================================================================

    /// <summary>The subscription's invoices: same student and category, months within its period.</summary>
    private IQueryable<Invoice> InRange(StudentSubscription sub)
    {
        var from = new DateOnly(sub.StartsOn.Year, sub.StartsOn.Month, 1);
        var to = sub.EndsOn is { } e ? new DateOnly(e.Year, e.Month, 1) : DateOnly.MaxValue;
        return db.Invoices.Where(i => i.StudentId == sub.StudentId && i.CategoryId == sub.CategoryId
                                      && i.PeriodMonth >= from && i.PeriodMonth <= to);
    }

    /// <summary>
    /// What each standing payment of the student holds on each invoice: normal allocations minus
    /// releases. Storno rows and reversed payments are out — the same rule as the balance.
    /// </summary>
    private async Task<Dictionary<(Guid PaymentId, Guid InvoiceId), decimal>> NetAsync(string studentId, CancellationToken ct)
    {
        var rows = await (from a in db.PaymentAllocations.AsNoTracking()
                          join p in db.Payments.AsNoTracking() on a.PaymentId equals p.Id
                          where p.StudentId == studentId && p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id)
                          select new { a.PaymentId, a.InvoiceId, a.Amount }).ToListAsync(ct);
        return rows.GroupBy(x => (x.PaymentId, x.InvoiceId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
    }

    private async Task<Dictionary<Guid, decimal>> PaidAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        if (invoiceIds.Count == 0) return [];
        return await (from a in db.PaymentAllocations.AsNoTracking()
                      join p in db.Payments.AsNoTracking() on a.PaymentId equals p.Id
                      where invoiceIds.Contains(a.InvoiceId) && p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id)
                      group a by a.InvoiceId into g
                      select new { g.Key, Total = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
    }

    /// <summary>The payment's own cash box when it is still active — else the default one.</summary>
    private async Task<Guid> ActiveBoxAsync(Guid? boxId, CancellationToken ct)
    {
        if (boxId is { } id && await db.CashBoxes.AnyAsync(b => b.Id == id && b.IsActive, ct)) return id;
        return await CashBoxService.DefaultBoxIdAsync(db, ct);
    }

    private static async Task ReverseLedgerAsync(Func<Task> reverse)
    {
        try
        {
            await reverse();
        }
        catch (BillingRuleException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            throw BillingRuleException.Conflict("ledger_reversal_refused", ex.Message);
        }
    }
}
