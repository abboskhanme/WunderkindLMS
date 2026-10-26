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
//
//  2026-10-26 (billing-plans.md §10.5) — "to'langani balansga qaytarilsin": `paidTo = balance` releases
//  each payment's share instead of the storno (receipts and cash untouched, the money is the pupil's
//  advance), `rebill` bills the months again from the abonement, `alsoBill` bills them for other
//  abonements whose accrue_from skipped them — the imported September lump is fixed in one action.
// ===========================================================================

public record MonthRemovalPaymentDto(Guid PaymentId, long ReceiptNo, DateTimeOffset ReceivedAt, decimal Amount, decimal Share, bool Own);

/// <param name="BlockedReason">
/// Why THIS user cannot remove the month with <c>paidTo = storno</c> (two-person rule) — another admin can.
/// With the payment going back to the balance no receipt is reversed and the rule does not apply.
/// </param>
/// <param name="Rebill">What the abonement bills for the month if it is billed again (payable); null = nothing covers it.</param>
public record MonthRemovalLineDto(
    Guid InvoiceId, string Month, decimal Amount, decimal Discount, decimal Payable, decimal Paid, string Status,
    IReadOnlyList<MonthRemovalPaymentDto> Payments, string? BlockedReason = null, decimal? Rebill = null);

/// <summary>A month another abonement skipped through <c>accrue_from</c> and would bill now (§10.5).</summary>
public record MonthRemovalOtherMonthDto(string Month, decimal Amount);

/// <summary>Another abonement of the pupil whose <c>accrue_from</c> skipped months — "shu oyni ham hisoblash".</summary>
public record MonthRemovalOtherDto(
    Guid SubscriptionId, string CategoryName, string? PlanName, decimal MonthlyAmount, DateOnly StartsOn, DateOnly? EndsOn,
    DateOnly AccrueFrom, IReadOnlyList<MonthRemovalOtherMonthDto> Months);

public record MonthRemovalPreviewDto(
    Guid SubscriptionId, string CategoryName, decimal MonthlyAmount, DateOnly StartsOn, DateOnly? EndsOn,
    IReadOnlyList<MonthRemovalLineDto> Months, IReadOnlyList<string> RemovedMonths,
    IReadOnlyList<MonthRemovalOtherDto>? Others = null);

/// <param name="PaidTo"><c>storno</c> (default, 2026-10-07) or <c>balance</c> — billing-plans.md §10.5.</param>
/// <param name="Rebill">Bill the removed months again from the abonement's own terms.</param>
/// <param name="AlsoBill">Other abonements of the pupil to bill the removed months for (their <c>accrue_from</c> skipped them).</param>
public record MonthRemovalRequest(
    IReadOnlyList<Guid> InvoiceIds, string Reason, bool DeleteSubscription = false,
    string? PaidTo = null, bool Rebill = false, IReadOnlyList<Guid>? AlsoBill = null);

public record MonthRemovalResultDto(
    int Months, decimal RemovedPayable, decimal ReversedPaid, int PaymentsReversed, int PaymentsReissued, bool SubscriptionDeleted,
    decimal Released = 0m, decimal Rebilled = 0m, decimal AlsoBilled = 0m);

/// <summary>Where the paid share of a removed month goes (<see cref="MonthRemovalRequest.PaidTo"/>).</summary>
public static class MonthRemovalPaidTo
{
    /// <summary>The receipts are reversed and the money leaves the accounts (client, 2026-10-07).</summary>
    public const string Storno = "storno";
    /// <summary>The money stays as the pupil's advance and closes the oldest open months (client, 2026-10-26).</summary>
    public const string Balance = "balance";
}

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

        // What a re-bill would write (§10.5) — per month, from the category's abonements.
        for (var n = 0; n < lines.Count; n++)
        {
            var charge = await ChargeAsync(sub.StudentId, sub.CategoryId, live[n].PeriodMonth, ct);
            lines[n] = lines[n] with { Rebill = charge is { } c ? c.Gross - c.Discount : null };
        }

        return new MonthRemovalPreviewDto(sub.Id, category, sub.MonthlyAmount, sub.StartsOn, sub.EndsOn, lines,
            [.. all.Where(i => i.RemovedAt != null).Select(i => Month(i.PeriodMonth))],
            await OthersAsync(sub, ct));
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
        var paidTo = string.IsNullOrWhiteSpace(request.PaidTo) ? MonthRemovalPaidTo.Storno : request.PaidTo.Trim().ToLowerInvariant();
        if (paidTo is not (MonthRemovalPaidTo.Storno or MonthRemovalPaidTo.Balance))
            throw BillingRuleException.Invalid("paid_to_invalid", "To'langan pul qayerga borishi noma'lum: «balance» yoki «storno».");
        if (request.Rebill && request.DeleteSubscription)
            throw BillingRuleException.Invalid("rebill_with_delete",
                "Oy abonement bo'yicha qayta hisoblanadigan bo'lsa, abonementning o'zini o'chirib bo'lmaydi.");
        var alsoBill = (request.AlsoBill ?? []).Distinct().ToList();

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
        //      (paidTo = balance: no receipt is touched — the share is released to the advance, 1b)
        var net = await NetAsync(sub.StudentId, ct);
        var touchedPaymentIds = net.Where(kv => selectedIds.Contains(kv.Key.InvoiceId) && kv.Value > 0m)
            .Select(kv => kv.Key.PaymentId).Distinct().ToList();
        List<Payment> payments = paidTo == MonthRemovalPaidTo.Storno
            ? await db.Payments.AsNoTracking().Where(p => touchedPaymentIds.Contains(p.Id)).OrderBy(p => p.ReceivedAt).ToListAsync(ct)
            : [];
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

        // ---- 1b. paidTo = balance: each payment's share on the month goes back to it (release rows) ----
        //      The cash, the receipts and the ledger stay; the money is the pupil's advance (step 7).
        var groupId = Guid.NewGuid();
        var released = 0m;
        if (paidTo == MonthRemovalPaidTo.Balance)
        {
            foreach (var kv in net.Where(kv => selectedIds.Contains(kv.Key.InvoiceId) && kv.Value > 0m))
            {
                db.PaymentAllocations.Add(new PaymentAllocation
                {
                    PaymentId = kv.Key.PaymentId,
                    InvoiceId = kv.Key.InvoiceId,
                    Amount = -kv.Value,
                    Kind = PaymentAllocationKind.Release,
                    RemovalGroupId = groupId,
                });
                released += kv.Value;
            }
            await db.SaveChangesAsync(ct);
            if (released > 0m)
                audit.Record(AuditService.EntityPayment, groupId.ToString("D"), "release",
                    $"Oy o'chirildi ({months}): {AuditService.Money(released)} so'm to'lov o'quvchi balansiga qaytdi — sabab: {reason}",
                    after: new { Released = released }, studentId: sub.StudentId);
        }

        // ---- 2. The months: adjustments back, accrual reversed, void + removed ----
        var paid = await PaidAsync(selectedIds, ct);
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

        // The void rows go first — the new ones below would hit the unique index (student, category, month).
        await db.SaveChangesAsync(ct);
        var billMonths = selected.Select(i => i.PeriodMonth).Distinct().OrderBy(m => m).ToList();
        var settings = await db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new BillingSettings();
        var studentName = await db.Students.AsNoTracking().Where(s => s.Id == sub.StudentId)
            .Select(s => s.FullName).FirstOrDefaultAsync(ct) ?? "";

        // ---- 5. Re-bill (§10.5): the removed months again, from the abonement's own terms ----
        var rebilled = 0m;
        if (request.Rebill)
            foreach (var month in billMonths)
                rebilled += await BillAsync(sub.StudentId, studentName, sub.CategoryId, month, settings, reason, actorId, ct);

        // ---- 6. Also bill: other abonements whose accrue_from skipped these months (the lump held them) ----
        var alsoBilled = 0m;
        foreach (var otherId in alsoBill)
        {
            var other = await db.StudentSubscriptions.FirstOrDefaultAsync(s => s.Id == otherId, ct);
            if (other is null || other.StudentId != sub.StudentId || other.CategoryId == sub.CategoryId)
                throw BillingRuleException.Conflict("also_bill_invalid",
                    "Qo'shimcha hisoblanadigan abonement shu o'quvchining boshqa toifadagi abonementi emas — oynani qayta oching.");
            var skipped = await SkippedMonthsAsync(other, ct);
            var any = false;
            foreach (var month in billMonths.Where(skipped.Contains))
            {
                alsoBilled += await BillAsync(other.StudentId, studentName, other.CategoryId, month, settings, reason, actorId, ct);
                any = true;
            }
            if (any) await MoveAccrueFromBackAsync(other, reason, ct);
        }

        // ---- 7. The released money (and any advance) closes the oldest open months — a payment's priority ----
        await db.SaveChangesAsync(ct);
        if (paidTo == MonthRemovalPaidTo.Balance || request.Rebill || alsoBill.Count > 0)
            await AdvanceAllocator.ApplyAsync(db, sub.StudentId, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new MonthRemovalResultDto(selected.Count, decimal.Round(removedPayable, MoneyScale),
            decimal.Round(reversedPaid, MoneyScale), payments.Count, reissued, deleted,
            decimal.Round(released, MoneyScale), decimal.Round(rebilled, MoneyScale), decimal.Round(alsoBilled, MoneyScale));
    }

    // =====================================================================
    //  Re-bill (billing-plans.md §10.5)
    // =====================================================================

    /// <summary>An abonement ended on its own start day — cancelled; it bills nothing here.</summary>
    private static bool Cancelled(StudentSubscription s) => s.EndsOn is { } end && end == s.StartsOn;

    /// <summary>
    /// The month's charge for one category from the pupil's abonements — accrual's rule (§10.4 lesson days,
    /// approved discounts; <see cref="InvoiceService"/>) without its skip rules: <c>accrue_from</c>, void and
    /// removed rows do not hold the month here. Null = no abonement covers the month.
    /// </summary>
    private async Task<(decimal Gross, decimal Discount)?> ChargeAsync(
        string studentId, Guid categoryId, DateOnly month, CancellationToken ct)
    {
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var subs = (await db.StudentSubscriptions.AsNoTracking()
                .Where(s => s.StudentId == studentId && s.CategoryId == categoryId
                            && s.StartsOn <= monthEnd && (s.EndsOn == null || s.EndsOn >= month))
                .ToListAsync(ct))
            .Where(s => !Cancelled(s))
            .ToList();
        if (subs.Count == 0) return null;

        var planIds = subs.Where(s => s.PlanId != null).Select(s => s.PlanId!.Value).Distinct().ToList();
        var schedules = await db.SubscriptionPlans.AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DaysPerWeek, ct);
        var holidays = await LessonDays.HolidaysAsync(db, month, ct);
        var gross = LessonDays.MonthCharge(
            [.. subs.Select(s => new LessonDays.Segment(s.StartsOn, s.EndsOn, s.CreatedAt, s.MonthlyAmount,
                (s.PlanId is { } plan ? schedules.GetValueOrDefault(plan) : null) ?? LessonDays.SchoolWeek))],
            month, holidays);

        // As accrual: the latest-starting abonement is the month's price; the most specific discount wins.
        var current = subs.OrderByDescending(s => s.StartsOn).ThenByDescending(s => s.CreatedAt).First();
        var share = current.MonthlyAmount > 0m ? Math.Min(1m, gross / current.MonthlyAmount) : 1m;
        var applied = (await db.Discounts.AsNoTracking()
                .Where(d => d.StudentId == studentId
                            && d.Status == DiscountStatus.Approved
                            && (d.ApprovedBy != null || d.DiscountTypeId != null)
                            && (d.CategoryId == null || d.CategoryId == categoryId)
                            && d.StartsOn <= monthEnd
                            && (d.EndsOn == null || d.EndsOn >= month))
                .ToListAsync(ct))
            .OrderByDescending(d => d.CategoryId != null)
            .ThenByDescending(d => d.StartsOn)
            .ThenByDescending(d => d.CreatedAt)
            .FirstOrDefault();
        var discount = applied is null
            ? 0m
            : DiscountMath.DiscountFor(gross, applied.Percent, decimal.Round(applied.Amount * share, MoneyScale));
        return (gross, discount);
    }

    /// <summary>
    /// Writes the month's invoice for the category with its accrual batch (dated on the month, like accrual).
    /// Inside the caller's transaction; the advance is applied once at the end, not here.
    /// </summary>
    private async Task<decimal> BillAsync(
        string studentId, string studentName, Guid categoryId, DateOnly month, BillingSettings settings,
        string reason, string actorId, CancellationToken ct)
    {
        if (await ChargeAsync(studentId, categoryId, month, ct) is not { } charge) return 0m;
        if (await db.Invoices.AnyAsync(i => i.StudentId == studentId && i.CategoryId == categoryId
                                            && i.PeriodMonth == month && i.Status != InvoiceStatus.Void, ct))
            throw BillingRuleException.Conflict("month_already_billed",
                $"{Month(month)} oyi bu toifada allaqachon hisoblangan — oynani qayta oching.");

        var category = await db.FeeCategories.AsNoTracking().FirstAsync(c => c.Id == categoryId, ct);
        var payable = charge.Gross - charge.Discount;
        var invoice = new Invoice
        {
            StudentId = studentId,
            CategoryId = categoryId,
            PeriodMonth = month,
            Amount = charge.Gross,
            Discount = charge.Discount,
            DueOn = new DateOnly(month.Year, month.Month,
                Math.Clamp(settings.PaymentDueDay, 1, DateTime.DaysInMonth(month.Year, month.Month))),
            Status = InvoiceService.StatusFor(payable, 0m),
            CreatedAt = AppClock.NowInstant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        if (payable > 0m)
        {
            var memo = string.Create(CultureInfo.InvariantCulture,
                $"{month:yyyy-MM} · {category.Name} · {studentName} (qayta hisoblandi)");
            await new LedgerService(db).PostAsync(
            [
                new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, payable,
                    LedgerRefType.Invoice, invoice.Id, month, memo),
                new LedgerPosting(Accounts.RevenueFor(category.Code), LedgerDirection.Credit, payable,
                    LedgerRefType.Invoice, invoice.Id, month, memo),
            ], actorId, ct);
        }

        audit.Record(AuditService.EntityInvoice, invoice.Id.ToString("D"), "rebill",
            $"{Month(month)} · {category.Name} abonement bo'yicha qayta hisoblandi: {AuditService.Money(payable)} so'm — sabab: {reason}",
            after: new { invoice.Id, invoice.PeriodMonth, invoice.Amount, invoice.Discount }, studentId: studentId);
        return payable;
    }

    /// <summary>
    /// Months from the abonement's start to its <c>accrue_from</c> that it covers and that hold no live
    /// invoice of its category — the months the imported lump carried (§10.1).
    /// </summary>
    private async Task<List<DateOnly>> SkippedMonthsAsync(StudentSubscription s, CancellationToken ct)
    {
        if (s.AccrueFrom is not { } accrueFrom || Cancelled(s)) return [];
        var first = new DateOnly(s.StartsOn.Year, s.StartsOn.Month, 1);
        var stop = new DateOnly(accrueFrom.Year, accrueFrom.Month, 1);
        var billed = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == s.StudentId && i.CategoryId == s.CategoryId && i.Status != InvoiceStatus.Void
                        && i.PeriodMonth >= first && i.PeriodMonth < stop)
            .Select(i => i.PeriodMonth)
            .ToListAsync(ct);
        var months = new List<DateOnly>();
        for (var m = first; m < stop; m = m.AddMonths(1))
            if ((s.EndsOn == null || s.EndsOn >= m) && !billed.Contains(m))
                months.Add(m);
        return months;
    }

    /// <summary>
    /// After its skipped months are billed, <c>accrue_from</c> moves back past them — to the month after the
    /// last one still unbilled, or null when every month from the start is billed. Never forward.
    /// </summary>
    private async Task MoveAccrueFromBackAsync(StudentSubscription s, string reason, CancellationToken ct)
    {
        var before = s.AccrueFrom;
        var left = await SkippedMonthsAsync(s, ct);
        DateOnly? after = left.Count == 0 ? null : left.Max().AddMonths(1);
        if (after is { } a && before is { } b && a >= b) return;
        s.AccrueFrom = after;
        audit.Record(SubscriptionAuditEntity, s.Id.ToString(), "update",
            $"Hisob boshlanishi {before:dd.MM.yyyy} → {(after is { } x ? x.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "abonement boshidan")}"
            + $" (o'tkazib yuborilgan oy hisoblandi) — sabab: {reason}",
            before: new { AccrueFrom = before }, after: new { s.AccrueFrom }, studentId: s.StudentId);
    }

    /// <summary>The pupil's other abonements whose <c>accrue_from</c> skipped months nobody billed (§10.5).</summary>
    private async Task<List<MonthRemovalOtherDto>> OthersAsync(StudentSubscription sub, CancellationToken ct)
    {
        var others = await db.StudentSubscriptions.AsNoTracking()
            .Where(s => s.StudentId == sub.StudentId && s.CategoryId != sub.CategoryId
                        && s.AccrueFrom != null && s.AccrueFrom > s.StartsOn)
            .OrderBy(s => s.StartsOn)
            .ToListAsync(ct);
        var result = new List<MonthRemovalOtherDto>();
        foreach (var other in others.Where(o => !Cancelled(o)))
        {
            var months = new List<MonthRemovalOtherMonthDto>();
            foreach (var month in await SkippedMonthsAsync(other, ct))
                if (await ChargeAsync(other.StudentId, other.CategoryId, month, ct) is { } c)
                    months.Add(new MonthRemovalOtherMonthDto(Month(month), c.Gross - c.Discount));
            if (months.Count == 0) continue;
            var category = await db.FeeCategories.AsNoTracking().Where(c => c.Id == other.CategoryId)
                .Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "";
            var plan = other.PlanId is { } planId
                ? await db.SubscriptionPlans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.Name).FirstOrDefaultAsync(ct)
                : null;
            result.Add(new MonthRemovalOtherDto(other.Id, category, plan, other.MonthlyAmount, other.StartsOn, other.EndsOn,
                other.AccrueFrom!.Value, months));
        }
        return result;
    }

    // =====================================================================

    /// <summary>
    /// The subscription's invoices: same student and category, from its start month until the next
    /// subscription of the category starts. Its end date does NOT cut the list: a month accrued before
    /// the subscription was closed stays billed (prod, 2026-10-07: closed on the 2nd to end in
    /// September, October already accrued on the 1st) and must be removable here — otherwise removing
    /// the subscription left that bill with no way to reach it.
    /// </summary>
    private IQueryable<Invoice> InRange(StudentSubscription sub)
    {
        var from = new DateOnly(sub.StartsOn.Year, sub.StartsOn.Month, 1);
        return db.Invoices.Where(i => i.StudentId == sub.StudentId && i.CategoryId == sub.CategoryId
                                      && i.PeriodMonth >= from
                                      && !db.StudentSubscriptions.Any(s =>
                                          s.StudentId == sub.StudentId && s.CategoryId == sub.CategoryId && s.Id != sub.Id
                                          && s.StartsOn > sub.StartsOn
                                          && s.StartsOn.Year * 12 + s.StartsOn.Month <= i.PeriodMonth.Year * 12 + i.PeriodMonth.Month));
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
