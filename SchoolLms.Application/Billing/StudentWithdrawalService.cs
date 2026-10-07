using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  "Chiqib ketdi — abonementni bekor qilish" (mijoz, 2026-10-06).
//
//  A student leaves mid-month, but the month (and maybe the next) was already invoiced. Marking it
//  paid would invent cash; archiving is refused while debt stands. So, from the last day studied:
//
//    leave month:  keep  = max(0, (payable + sick) × studied lesson days ÷ billed lesson days − sick)
//    later months: keep  = 0
//
//  Billed lesson days run from the month's start — or from the abonement's start when it opened inside
//  the month and was billed from there (billing-plans.md §10.4) — to the month's end.
//  `sick` is the month's standing sickness discount. Its days carry no dates, but they were days
//  the student missed while still enrolled, so they come off what stays in full — prorating them
//  would bill a sick child for the very days the discount was given for.
//    write-off     = payable − keep         (an `invoice_adjustments` row, kind "withdrawal")
//    released      = max(0, paid − keep)   (negative "release" allocations → the payment's
//                                           unallocated part, i.e. advance, refundable)
//
//  Lesson days follow the abonement's schedule; "Butun oy" counts the school week Mon–Sat
//  (client: "yakshanba va bayramlar sanalmaydi"). The selected subscriptions end on the last day,
//  so accrual never writes those months again. Freed money first closes the student's other debts
//  (AdvanceAllocator — the same oldest-month rule as a payment). No cash row, no cash-box change.
//
//  One action = one `student_withdrawals` row, the group id of its adjustments and releases.
//  Cancelling (30 days) reverses the adjustments, restores the subscriptions and puts the released
//  money back on the very invoices and payments it came from (as far as it is still unspent and not
//  refunded), so each receipt's split by category is what it was. Whatever cannot go back that way
//  is left to the advance rule.
// ===========================================================================

public record WithdrawalRequest(DateOnly LastDay, IReadOnlyList<Guid>? SubscriptionIds = null, string? Note = null);

public record WithdrawalSubscriptionDto(
    Guid Id, string CategoryName, string? PlanName, decimal MonthlyAmount, DateOnly StartsOn, DateOnly? EndsOn, bool Selected);

/// <summary>
/// One invoice the action touches. <see cref="StudiedDays"/>/<see cref="LessonDays"/> only matter in the leave
/// month; <see cref="SickDeduction"/> is the month's sickness discount taken off what stays.
/// </summary>
public record WithdrawalLineDto(
    Guid InvoiceId, string Month, string CategoryName, decimal Payable, decimal Paid,
    int LessonDays, int StudiedDays, decimal Keep, decimal WriteOff, decimal Released, decimal SickDeduction = 0m);

public record WithdrawalPreviewDto(
    DateOnly LastDay,
    IReadOnlyList<WithdrawalSubscriptionDto> Subscriptions,
    IReadOnlyList<WithdrawalLineDto> Lines,
    decimal TotalWriteOff,
    decimal TotalReleased,
    decimal BalanceBefore,
    decimal BalanceAfter,
    Guid? WithdrawalId = null);

public record WithdrawalEntryDto(
    Guid Id, DateOnly LastDay, decimal WrittenOff, decimal Released, string? Note,
    DateTimeOffset CreatedAt, string? CreatedByName, DateTimeOffset? CancelledAt, bool CanCancel);

public sealed class StudentWithdrawalService(IAppDbContext db, AuditService audit)
{
    private const int MoneyScale = 2;
    public const int CancelWindowDays = 30;
    private const string AuditEntity = "StudentWithdrawal";
    private const string SubscriptionAuditEntity = "StudentSubscription";

    /// <summary>The subscription's state before the action — what cancelling puts back.</summary>
    internal sealed record SubscriptionChange(Guid Id, DateOnly? PrevEndsOn, DateOnly? PrevAccrueFrom, DateOnly? EndsOn, DateOnly? AccrueFrom);

    // =====================================================================
    //  Preview / apply
    // =====================================================================

    /// <summary>What the action would do — nothing is written.</summary>
    public async Task<WithdrawalPreviewDto> PreviewAsync(string studentId, WithdrawalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireStudentAsync(studentId, ct);
        RequireLastDay(request.LastDay);

        var subscriptions = await RunningAfterAsync(studentId, request.LastDay, track: false, ct);
        var selected = Choose(subscriptions, request.SubscriptionIds);
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == studentId && i.Status != InvoiceStatus.Void)
            .ToListAsync(ct);
        var paid = await PaidAsync(invoices.Select(i => i.Id).ToList(), ct);
        var lines = await LinesAsync(studentId, invoices, paid, selected, request.LastDay, ct);
        var before = await new StudentBalanceQuery(db).ForAsync(studentId, ct);
        return await ToDtoAsync(request.LastDay, subscriptions, selected, lines, before, null, ct);
    }

    /// <summary>Applies it under the student's invoice locks, in one transaction.</summary>
    public async Task<WithdrawalPreviewDto> ApplyAsync(string studentId, WithdrawalRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actorId))
            throw BillingRuleException.Invalid("actor_required", "Amalni bajaruvchi aniqlanmadi.");
        await RequireStudentAsync(studentId, ct);
        RequireLastDay(request.LastDay);

        var balanceBefore = await new StudentBalanceQuery(db).ForAsync(studentId, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        var adjuster = new InvoiceAdjustmentService(db, audit);
        var (invoices, paid) = await adjuster.LockStudentAsync(studentId, ct);

        var subscriptions = await RunningAfterAsync(studentId, request.LastDay, track: true, ct);
        var selected = Choose(subscriptions, request.SubscriptionIds);
        if (selected.Count == 0)
            throw BillingRuleException.Conflict("nothing_to_withdraw",
                $"{request.LastDay:dd.MM.yyyy} dan keyin davom etadigan abonement yo'q.");
        var lines = await LinesAsync(studentId, invoices, paid, selected, request.LastDay, ct);

        var withdrawal = new StudentWithdrawal
        {
            StudentId = studentId,
            LastDay = request.LastDay,
            Note = Clip(request.Note),
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudentWithdrawals.Add(withdrawal);

        // 1) Paid beyond what stays → back to the payment's unallocated part (latest payments first).
        foreach (var line in lines.Where(l => l.Released > 0m))
            await ReleaseAsync(line.InvoiceId, line.Released, withdrawal.Id, ct);
        foreach (var line in lines.Where(l => l.Released > 0m))
            paid[line.InvoiceId] = paid.GetValueOrDefault(line.InvoiceId) - line.Released;

        // 2) Write the unstudied part off (one ledger batch per invoice, revenue back down).
        var written = 0m;
        foreach (var line in lines.Where(l => l.WriteOff > 0m))
        {
            var invoice = invoices.Single(i => i.Id == line.InvoiceId);
            var taken = await adjuster.ApplyAsync(
                invoice, paid, line.WriteOff, InvoiceAdjustmentKind.Withdrawal, sickDays: null, discountId: null,
                NoteFor(request), withdrawal.Id, actorId, ct);
            if (taken != line.WriteOff)
                throw BillingRuleException.Conflict("withdrawal_changed",
                    "Hisob-faktura shu orada o'zgardi — oynani qayta oching.");
            written += taken;
        }

        // 3) The subscriptions end on the last day, so accrual never writes those months again.
        var changes = new List<SubscriptionChange>();
        foreach (var sub in selected)
        {
            var before = SubscriptionService.Snapshot(sub);
            var change = End(sub, request.LastDay);
            changes.Add(change);
            audit.Record(SubscriptionAuditEntity, sub.Id.ToString(), "update",
                $"Obuna yopildi: o'quvchi chiqib ketdi, oxirgi o'qigan kuni {request.LastDay:dd.MM.yyyy}",
                before: before, after: SubscriptionService.Snapshot(sub), studentId: studentId);
        }

        withdrawal.WrittenOff = decimal.Round(written, MoneyScale);
        withdrawal.Released = decimal.Round(lines.Sum(l => l.Released), MoneyScale);
        withdrawal.Details = JsonSerializer.Serialize(changes);
        audit.Record(AuditEntity, withdrawal.Id.ToString(), "create",
            $"O'quvchi chiqib ketdi ({request.LastDay:dd.MM.yyyy}): {AuditService.Money(withdrawal.WrittenOff)} so'm hisobdan chiqarildi"
            + (withdrawal.Released > 0m ? $", {AuditService.Money(withdrawal.Released)} so'm ortiqcha to'lov avansga qaytdi" : ""),
            after: new { withdrawal.LastDay, withdrawal.WrittenOff, withdrawal.Released, Subscriptions = changes.Count },
            studentId: studentId);
        await db.SaveChangesAsync(ct);

        // 4) Freed money closes the student's remaining debts first (oldest month).
        await AdvanceAllocator.ApplyAsync(db, studentId, ct);
        await tx.CommitAsync(ct);

        var all = await RunningAfterAsync(studentId, request.LastDay, track: false, ct);
        var dto = await ToDtoAsync(request.LastDay, all, selected, lines, balanceBefore, withdrawal.Id, ct);
        return dto with { BalanceAfter = await new StudentBalanceQuery(db).ForAsync(studentId, ct) };
    }

    // =====================================================================
    //  List / cancel
    // =====================================================================

    public async Task<IReadOnlyList<WithdrawalEntryDto>> ListAsync(string studentId, CancellationToken ct = default)
    {
        var since = AppClock.NowInstant.AddDays(-CancelWindowDays);
        return await (
                from w in db.StudentWithdrawals.AsNoTracking()
                where w.StudentId == studentId
                join u in db.Users.AsNoTracking() on w.CreatedBy equals u.Id into users
                from u in users.DefaultIfEmpty()
                orderby w.CreatedAt descending
                select new WithdrawalEntryDto(
                    w.Id, w.LastDay, w.WrittenOff, w.Released, w.Note, w.CreatedAt,
                    u == null ? null : u.FullName, w.CancelledAt, w.CancelledAt == null && w.CreatedAt >= since))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Puts it back: adjustments reversed, subscriptions restored, the advance closes the debt again.
    /// Refused after <see cref="CancelWindowDays"/> days, twice, or when a subscription was changed since.
    /// </summary>
    public async Task<WithdrawalEntryDto> CancelAsync(
        string studentId, Guid withdrawalId, string actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw BillingRuleException.Invalid("actor_required", "Amalni bajaruvchi aniqlanmadi.");
        if (!await db.StudentWithdrawals.AsNoTracking().AnyAsync(w => w.Id == withdrawalId && w.StudentId == studentId, ct))
            throw BillingRuleException.NotFound("withdrawal_not_found", "Bunday amal topilmadi.");
        // Cancelling brings the debt and the abonement back — not for an archived pupil (2026-10-08).
        await ArchivedStudents.EnsureActiveAsync(db, studentId, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        var adjuster = new InvoiceAdjustmentService(db, audit);
        var (invoices, paid) = await adjuster.LockStudentAsync(studentId, ct);
        // Read under the locks — a second click waits for the first and then finds it cancelled.
        var withdrawal = await db.StudentWithdrawals.SingleAsync(w => w.Id == withdrawalId, ct);
        if (withdrawal.CancelledAt is not null)
            throw BillingRuleException.Conflict("withdrawal_already_cancelled", "Bu amal allaqachon bekor qilingan.");
        if (withdrawal.CreatedAt < AppClock.NowInstant.AddDays(-CancelWindowDays))
            throw BillingRuleException.Conflict("withdrawal_too_old",
                $"{CancelWindowDays} kundan eski amalni bekor qilib bo'lmaydi.");

        // Validate every subscription first — nothing is touched if one cannot go back.
        var changes = JsonSerializer.Deserialize<List<SubscriptionChange>>(withdrawal.Details) ?? [];
        var ids = changes.Select(c => c.Id).ToList();
        var subs = await db.StudentSubscriptions.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        foreach (var change in changes)
        {
            var sub = subs.SingleOrDefault(s => s.Id == change.Id)
                ?? throw BillingRuleException.Conflict("subscription_missing", "Abonement topilmadi — qo'lda tiklang.");
            if (sub.EndsOn != change.EndsOn || sub.AccrueFrom != change.AccrueFrom)
                throw BillingRuleException.Conflict("subscription_changed",
                    "Abonement chiqib ketgandan keyin o'zgartirilgan — bekor qilib bo'lmaydi, qo'lda tiklang.");
            var clash = await db.StudentSubscriptions.AsNoTracking()
                .AnyAsync(s => s.Id != sub.Id && s.StudentId == sub.StudentId && s.CategoryId == sub.CategoryId
                               && s.StartsOn > sub.StartsOn
                               && (change.PrevEndsOn == null || s.StartsOn <= change.PrevEndsOn), ct);
            if (clash)
                throw BillingRuleException.Conflict("subscription_overlap",
                    "Shu toifada keyin yangi abonement ochilgan — bekor qilib bo'lmaydi.");
        }

        var rows = await db.InvoiceAdjustments.AsNoTracking()
            .Where(a => a.GroupId == withdrawalId && a.Kind == InvoiceAdjustmentKind.Withdrawal && a.ReversalOf == null
                        && !db.InvoiceAdjustments.Any(r => r.ReversalOf == a.Id))
            .OrderBy(a => a.InvoiceId)
            .ToListAsync(ct);
        var reversalGroup = Guid.NewGuid();
        foreach (var row in rows)
        {
            var invoice = invoices.SingleOrDefault(i => i.Id == row.InvoiceId)
                ?? throw BillingRuleException.Conflict("invoice_void",
                    "Hisob-faktura shu orada bekor qilingan — amalni qaytarib bo'lmaydi.");
            await adjuster.ReverseAsync(row, invoice, paid, "Chiqib ketish bekor qilindi", reversalGroup, actorId, ct);
        }

        foreach (var change in changes)
        {
            var sub = subs.Single(s => s.Id == change.Id);
            var before = SubscriptionService.Snapshot(sub);
            sub.EndsOn = change.PrevEndsOn;
            sub.AccrueFrom = change.PrevAccrueFrom;
            audit.Record(SubscriptionAuditEntity, sub.Id.ToString(), "update",
                "Obuna tiklandi: chiqib ketish bekor qilindi",
                before: before, after: SubscriptionService.Snapshot(sub), studentId: studentId);
        }

        withdrawal.CancelledAt = AppClock.NowInstant;
        withdrawal.CancelledBy = actorId;
        audit.Record(AuditEntity, withdrawal.Id.ToString(), "cancel",
            $"Chiqib ketish bekor qilindi ({withdrawal.LastDay:dd.MM.yyyy}): {AuditService.Money(withdrawal.WrittenOff)} so'm qarz qaytdi",
            studentId: studentId);
        await db.SaveChangesAsync(ct);
        // The released money goes back where it came from; anything it cannot cover is left to the advance rule.
        await PutBackReleasesAsync(studentId, withdrawalId, invoices, paid, ct);
        await AdvanceAllocator.ApplyAsync(db, studentId, ct);
        await tx.CommitAsync(ct);

        // Months the ended subscription skipped meanwhile are written now (own transactions).
        var accrualActor = await BillingAccrualService.ResolveActorAsync(db, ct) ?? actorId;
        await new InvoiceService(db, new LedgerService(db)).AccrueStudentAsync(studentId, accrualActor, ct);

        return (await ListAsync(studentId, ct)).Single(e => e.Id == withdrawalId);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>Subscriptions still running after the last day (they are what the action ends).</summary>
    private async Task<List<StudentSubscription>> RunningAfterAsync(string studentId, DateOnly lastDay, bool track, CancellationToken ct)
    {
        var q = db.StudentSubscriptions.Where(s => s.StudentId == studentId && (s.EndsOn == null || s.EndsOn > lastDay));
        if (!track) q = q.AsNoTracking();
        return await q.OrderBy(s => s.StartsOn).ToListAsync(ct);
    }

    private static List<StudentSubscription> Choose(List<StudentSubscription> running, IReadOnlyList<Guid>? ids)
    {
        if (ids is null) return running;
        var unknown = ids.Where(id => running.All(s => s.Id != id)).ToList();
        if (unknown.Count > 0)
            throw BillingRuleException.Invalid("subscription_not_running",
                "Tanlangan abonement shu sanadan keyin davom etmaydi yoki boshqa o'quvchiniki.");
        return [.. running.Where(s => ids.Contains(s.Id))];
    }

    private async Task<List<WithdrawalLineDto>> LinesAsync(
        string studentId, List<Invoice> invoices, IReadOnlyDictionary<Guid, decimal> paid, List<StudentSubscription> selected,
        DateOnly lastDay, CancellationToken ct)
    {
        var leaveMonth = new DateOnly(lastDay.Year, lastDay.Month, 1);
        var categoryIds = selected.Select(s => s.CategoryId).ToHashSet();
        var touched = invoices
            .Where(i => i.PeriodMonth >= leaveMonth && categoryIds.Contains(i.CategoryId) && i.Amount - i.Discount > 0m)
            .OrderBy(i => i.PeriodMonth).ThenBy(i => i.CategoryId)
            .ToList();
        if (touched.Count == 0) return [];

        var names = await db.FeeCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var planIds = selected.Where(s => s.PlanId != null).Select(s => s.PlanId!.Value).Distinct().ToList();
        var schedules = await db.SubscriptionPlans.AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DaysPerWeek, ct);
        var holidays = await LessonDays.HolidaysAsync(db, leaveMonth, ct);
        // Where the leave month's billing starts per category: an abonement opened inside the month was
        // billed from its start (billing-plans.md §10.4), so its days are counted from there too.
        var leaveEnd = leaveMonth.AddMonths(1).AddDays(-1);
        var startsByCategory = (await db.StudentSubscriptions.AsNoTracking()
                .Where(s => s.StudentId == studentId && categoryIds.Contains(s.CategoryId)
                            && s.StartsOn <= leaveEnd && (s.EndsOn == null || s.EndsOn >= leaveMonth)
                            && (s.AccrueFrom == null || s.AccrueFrom <= leaveEnd))
                .Select(s => new { s.CategoryId, s.StartsOn })
                .ToListAsync(ct))
            .GroupBy(s => s.CategoryId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.StartsOn).ToList());
        var leaveIds = touched.Where(i => i.PeriodMonth == leaveMonth).Select(i => i.Id).ToList();
        var sickByInvoice = await db.InvoiceAdjustments.AsNoTracking()
            .Where(a => leaveIds.Contains(a.InvoiceId) && a.Kind == InvoiceAdjustmentKind.Sick && a.ReversalOf == null
                        && !db.InvoiceAdjustments.Any(r => r.ReversalOf == a.Id))
            .GroupBy(a => a.InvoiceId)
            .Select(g => new { g.Key, Total = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

        var lines = new List<WithdrawalLineDto>();
        foreach (var invoice in touched)
        {
            var payable = invoice.Amount - invoice.Discount;
            var already = paid.GetValueOrDefault(invoice.Id);
            int lessonDays = 0, studied = 0;
            decimal keep = 0m, sick = 0m;
            if (invoice.PeriodMonth == leaveMonth)
            {
                var monthEnd = leaveMonth.AddMonths(1).AddDays(-1);
                // The subscription behind the month — the latest one of the category started by its end.
                var sub = selected.Where(s => s.CategoryId == invoice.CategoryId && s.StartsOn <= monthEnd)
                    .OrderByDescending(s => s.StartsOn).FirstOrDefault();
                var perWeek = (sub?.PlanId is { } planId ? schedules.GetValueOrDefault(planId) : null) ?? LessonDays.SchoolWeek;
                var from = LessonDays.BilledFrom(startsByCategory.GetValueOrDefault(invoice.CategoryId) ?? [], leaveMonth);
                lessonDays = Math.Max(1, LessonDays.CountBetween(from, monthEnd, perWeek, holidays));
                studied = Math.Min(lessonDays, LessonDays.CountBetween(from, lastDay, perWeek, holidays));
                sick = sickByInvoice.GetValueOrDefault(invoice.Id);
                keep = studied >= lessonDays
                    ? payable
                    : Math.Max(0m, decimal.Round((payable + sick) * studied / lessonDays, MoneyScale) - sick);
            }
            var writeOff = payable - keep;
            if (writeOff <= 0m) continue;
            lines.Add(new WithdrawalLineDto(
                invoice.Id, invoice.PeriodMonth.ToString("yyyy-MM"), names.GetValueOrDefault(invoice.CategoryId, "—"),
                payable, already, lessonDays, studied, keep, writeOff, Math.Max(0m, already - keep), sick));
        }
        return lines;
    }

    /// <summary>Negative "release" rows, taken from the latest payments on the invoice first.</summary>
    private async Task ReleaseAsync(Guid invoiceId, decimal amount, Guid withdrawalId, CancellationToken ct)
    {
        var byPayment = await (
                from a in db.PaymentAllocations.AsNoTracking()
                join p in db.Payments.AsNoTracking() on a.PaymentId equals p.Id
                where a.InvoiceId == invoiceId && p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id)
                group a by new { a.PaymentId, p.ReceivedAt, p.ReceiptNo } into g
                select new { g.Key.PaymentId, g.Key.ReceivedAt, g.Key.ReceiptNo, Net = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var left = amount;
        foreach (var source in byPayment.Where(x => x.Net > 0m).OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.ReceiptNo))
        {
            if (left <= 0m) break;
            var take = Math.Min(source.Net, left);
            db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = source.PaymentId,
                InvoiceId = invoiceId,
                Amount = -take,
                Kind = PaymentAllocationKind.Release,
                WithdrawalId = withdrawalId,
            });
            left -= take;
        }
        if (left > 0m)
            throw BillingRuleException.Conflict("withdrawal_changed", "To'lovlar shu orada o'zgardi — oynani qayta oching.");
    }

    /// <summary>
    /// Cancelling: each release goes back onto its own invoice from its own payment — as far as that payment
    /// still holds it unspent, the advance was not refunded meanwhile, and the restored invoice owes it.
    /// </summary>
    private async Task PutBackReleasesAsync(
        string studentId, Guid withdrawalId, List<Invoice> invoices, Dictionary<Guid, decimal> paid, CancellationToken ct)
    {
        var releases = await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.WithdrawalId == withdrawalId && a.Kind == PaymentAllocationKind.Release)
            .ToListAsync(ct);
        if (releases.Count == 0) return;

        var paymentIds = releases.Select(a => a.PaymentId).Distinct().ToList();
        var unallocated = await db.Payments.AsNoTracking()
            .Where(p => paymentIds.Contains(p.Id) && p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id))
            .Select(p => new
            {
                p.Id,
                Left = p.Amount - (db.PaymentAllocations.Where(a => a.PaymentId == p.Id).Sum(a => (decimal?)a.Amount) ?? 0m),
            })
            .ToDictionaryAsync(x => x.Id, x => x.Left, ct);
        // Refunded advance is the parent's now — the same limit a refund is checked against.
        var budget = await new StudentBalanceQuery(db).AdvanceForAsync(studentId, ct);

        var touched = new HashSet<Guid>();
        foreach (var release in releases)
        {
            var invoice = invoices.SingleOrDefault(i => i.Id == release.InvoiceId);
            if (invoice is null || budget <= 0m) continue;
            var take = decimal.Round(Math.Min(
                Math.Min(-release.Amount, unallocated.GetValueOrDefault(release.PaymentId)),
                Math.Min(InvoiceAdjustmentService.Remaining(invoice, paid), budget)), MoneyScale);
            if (take <= 0m) continue;
            db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = release.PaymentId, InvoiceId = invoice.Id, Amount = take });
            unallocated[release.PaymentId] -= take;
            paid[invoice.Id] = paid.GetValueOrDefault(invoice.Id) + take;
            budget -= take;
            touched.Add(invoice.Id);
        }
        foreach (var invoice in invoices.Where(i => touched.Contains(i.Id)))
            invoice.Status = InvoiceService.StatusFor(invoice.Amount - invoice.Discount, paid[invoice.Id]);
        await db.SaveChangesAsync(ct);
    }

    private static SubscriptionChange End(StudentSubscription sub, DateOnly lastDay)
    {
        var prevEnds = sub.EndsOn;
        var prevAccrue = sub.AccrueFrom;
        if (sub.StartsOn <= lastDay)
        {
            sub.EndsOn = lastDay;
        }
        else
        {
            // Not started yet: it can neither end before it starts nor accrue its first month.
            sub.EndsOn = sub.StartsOn;
            sub.AccrueFrom = new DateOnly(sub.StartsOn.Year, sub.StartsOn.Month, 1).AddMonths(1);
        }
        return new SubscriptionChange(sub.Id, prevEnds, prevAccrue, sub.EndsOn, sub.AccrueFrom);
    }

    private async Task<WithdrawalPreviewDto> ToDtoAsync(
        DateOnly lastDay, List<StudentSubscription> running, List<StudentSubscription> selected,
        List<WithdrawalLineDto> lines, decimal balanceBefore, Guid? withdrawalId, CancellationToken ct)
    {
        var categoryIds = running.Select(s => s.CategoryId).Concat(selected.Select(s => s.CategoryId)).Distinct().ToList();
        var categories = await db.FeeCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var planIds = running.Concat(selected).Where(s => s.PlanId != null).Select(s => s.PlanId!.Value).Distinct().ToList();
        var plans = await db.SubscriptionPlans.AsNoTracking()
            .Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var selectedIds = selected.Select(s => s.Id).ToHashSet();
        var shown = running.Concat(selected.Where(s => running.All(r => r.Id != s.Id)));
        var subs = shown
            .Select(s => new WithdrawalSubscriptionDto(
                s.Id, categories.GetValueOrDefault(s.CategoryId, "—"),
                s.PlanId is { } p ? plans.GetValueOrDefault(p) : null,
                s.MonthlyAmount, s.StartsOn, s.EndsOn, selectedIds.Contains(s.Id)))
            .ToList();
        var writeOff = lines.Sum(l => l.WriteOff);
        return new WithdrawalPreviewDto(
            lastDay, subs, lines, writeOff, lines.Sum(l => l.Released),
            balanceBefore, decimal.Round(balanceBefore + writeOff, MoneyScale), withdrawalId);
    }

    private async Task RequireStudentAsync(string studentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentId) || !await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId, ct))
            throw BillingRuleException.NotFound("student_not_found", "O'quvchi topilmadi.");
    }

    private static void RequireLastDay(DateOnly lastDay)
    {
        var today = AppClock.Today;
        if (lastDay > today)
            throw BillingRuleException.Invalid("last_day_in_future",
                "Oxirgi o'qigan kun kelajakda bo'la olmaydi — o'quvchi ketgan kuni belgilang.");
        if (lastDay < today.AddYears(-1))
            throw BillingRuleException.Invalid("last_day_too_old", "Bir yildan eski sana bilan bo'lmaydi.");
    }

    private static string NoteFor(WithdrawalRequest request)
    {
        var note = Clip(request.Note);
        return $"Chiqib ketdi, oxirgi kuni {request.LastDay:dd.MM.yyyy}" + (note is null ? "" : $" — {note}");
    }

    private static string? Clip(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 300)];

    private async Task<Dictionary<Guid, decimal>> PaidAsync(List<Guid> invoiceIds, CancellationToken ct)
    {
        if (invoiceIds.Count == 0) return [];
        return await (
                from a in db.PaymentAllocations.AsNoTracking()
                join p in db.Payments.AsNoTracking() on a.PaymentId equals p.Id
                where p.ReversalOf == null && !db.Payments.Any(r => r.ReversalOf == p.Id) && invoiceIds.Contains(a.InvoiceId)
                group a by a.InvoiceId into g
                select new { InvoiceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Total, ct);
    }
}
