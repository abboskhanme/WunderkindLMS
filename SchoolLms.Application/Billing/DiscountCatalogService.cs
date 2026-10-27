using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Chegirma turlari (catalog) and attaching them to students — billing-plans.md §8.
//
//  Client, 2026-09-28: named discounts with an amount, attached from the student's
//  "Abonementlar" tab, NO approval ("tasdiqlash shartmas"). A hand-written discount
//  (DiscountService) still waits for the director.
//
//  A catalog discount is a normal `discounts` row — approved at birth, `approved_by` null,
//  `discount_type_id` set — so accrual applies it like any other (one discount per invoice,
//  category-specific beats global). Months ALREADY invoiced from its start on get the
//  difference through an invoice adjustment (§10), never below what is paid.
// ===========================================================================

public record DiscountTypeDto(
    Guid Id, string Name, decimal Percent, decimal Amount, Guid? CategoryId, string? CategoryName,
    bool IsActive, int StudentCount);

public record CreateDiscountTypeRequest(string? Name, decimal Percent, decimal Amount, Guid? CategoryId);

/// <summary>The abonement type (category) is fixed once created.</summary>
public record UpdateDiscountTypeRequest(string? Name, decimal Percent, decimal Amount, bool IsActive);

/// <param name="Updated">Attached, still running discounts moved to the new value (next accrual on).</param>
public record UpdateDiscountTypeResult(DiscountTypeDto DiscountType, int Updated);

public record AttachDiscountRequest(string StudentId, Guid DiscountTypeId, DateOnly StartsOn, DateOnly? EndsOn);

public record AttachDiscountResult(DiscountDto Discount, int AdjustedInvoices, decimal AdjustedTotal);

public record EndDiscountRequest(DateOnly EndsOn);

/// <param name="RestoredInvoices">Months whose debt went back up.</param>
/// <param name="RestoredTotal">The net sum put back on them (so'm, after any other discount took over).</param>
public record DeleteDiscountResult(Guid Id, string Name, int RestoredInvoices, decimal RestoredTotal);

public sealed class DiscountCatalogService(IAppDbContext db, AuditService audit)
{
    private const int NameMax = 120;
    private const int MoneyScale = 2;

    // ------------------------------------------------------------------ catalog

    public async Task<IReadOnlyList<DiscountTypeDto>> ListAsync(bool activeOnly, CancellationToken ct = default)
    {
        var q = db.DiscountTypes.AsNoTracking();
        if (activeOnly) q = q.Where(t => t.IsActive);
        return await DtosAsync(await q.Select(t => t.Id).ToListAsync(ct), ct);
    }

    public async Task<DiscountTypeDto> CreateAsync(CreateDiscountTypeRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);
        var name = RequireName(request.Name);
        var (percent, amount) = RequireValue(request.Percent, request.Amount);
        if (request.CategoryId is { } categoryId && !await db.FeeCategories.AnyAsync(c => c.Id == categoryId && c.IsActive, ct))
            throw BillingRuleException.NotFound("category_not_found", "Abonement turi topilmadi yoki o'chirilgan.");
        await RequireFreeNameAsync(name, exclude: null, ct);

        var type = new DiscountType
        {
            Name = name, Percent = percent, Amount = amount, CategoryId = request.CategoryId,
            IsActive = true, CreatedAt = AppClock.NowInstant,
        };
        db.DiscountTypes.Add(type);
        audit.Record("DiscountType", type.Id.ToString(), "create",
            $"Chegirma turi qo'shildi: {name} — {Show(percent, amount)}", after: Snapshot(type));
        await db.SaveChangesAsync(ct);
        return (await DtosAsync([type.Id], ct))[0];
    }

    public async Task<UpdateDiscountTypeResult> UpdateAsync(
        Guid id, UpdateDiscountTypeRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);
        var type = await db.DiscountTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw BillingRuleException.NotFound("discount_type_not_found", "Chegirma turi topilmadi.");
        var name = RequireName(request.Name);
        var (percent, amount) = RequireValue(request.Percent, request.Amount);
        if (request.IsActive && (name != type.Name || !type.IsActive))
            await RequireFreeNameAsync(name, exclude: type.Id, ct);

        var before = Snapshot(type);
        var valueChanged = percent != type.Percent || amount != type.Amount;
        type.Name = name;
        type.Percent = percent;
        type.Amount = amount;
        type.IsActive = request.IsActive;

        // Like an abonement price: the running attached discounts take the new value from the next
        // accrual on; invoices already written keep theirs.
        var updated = 0;
        if (valueChanged)
        {
            var today = AppClock.Today;
            var running = await db.Discounts
                .Where(d => d.DiscountTypeId == type.Id && d.Status == DiscountStatus.Approved
                            && (d.EndsOn == null || d.EndsOn >= today))
                .ToListAsync(ct);
            foreach (var d in running)
            {
                audit.Record(AuditService.EntityStudentDiscount, d.Id.ToString(), "update",
                    $"'{name}' chegirma turi o'zgardi: {Show(d.Percent, d.Amount)} → {Show(percent, amount)}",
                    before: new { d.Percent, d.Amount }, after: new { Percent = percent, Amount = amount }, studentId: d.StudentId);
                (d.Percent, d.Amount) = (percent, amount);
                updated++;
            }
        }

        audit.Record("DiscountType", type.Id.ToString(), "update",
            $"Chegirma turi o'zgardi: {name} — {Show(percent, amount)}, faol: {request.IsActive}"
            + (updated > 0 ? $" ({updated} ta o'quvchida)" : ""),
            before: before, after: Snapshot(type));
        await db.SaveChangesAsync(ct);
        return new UpdateDiscountTypeResult((await DtosAsync([type.Id], ct))[0], updated);
    }

    // ------------------------------------------------------------------ attach / end

    public async Task<AttachDiscountResult> AttachAsync(AttachDiscountRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);
        if (request.EndsOn is { } end && end < request.StartsOn)
            throw BillingRuleException.Invalid("invalid_period", "Tugash sanasi boshlanishdan oldin bo'lmasin.");

        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StudentId, ct)
            ?? throw BillingRuleException.NotFound("student_not_found", "O'quvchi topilmadi.");
        if (student.IsArchived)
            throw BillingRuleException.Conflict("student_archived", $"{student.FullName} arxivlangan — chegirma biriktirib bo'lmaydi.");
        var type = await db.DiscountTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.DiscountTypeId, ct)
            ?? throw BillingRuleException.NotFound("discount_type_not_found", "Chegirma turi topilmadi.");
        if (!type.IsActive)
            throw BillingRuleException.Conflict("discount_type_inactive", $"'{type.Name}' chegirma turi arxivda.");

        var endsOrMax = request.EndsOn ?? DateOnly.MaxValue;
        if (await db.Discounts.AnyAsync(d => d.StudentId == student.Id && d.DiscountTypeId == type.Id
                                            && d.Status == DiscountStatus.Approved
                                            && d.StartsOn <= endsOrMax
                                            && (d.EndsOn == null || d.EndsOn >= request.StartsOn), ct))
            throw BillingRuleException.Conflict("discount_already_attached",
                $"'{type.Name}' chegirmasi bu o'quvchida shu davrda allaqachon bor.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var now = AppClock.NowInstant;
        var discount = new Discount
        {
            StudentId = student.Id,
            CategoryId = type.CategoryId,
            DiscountTypeId = type.Id,
            Percent = type.Percent,
            Amount = type.Amount,
            Reason = type.Name,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            Status = DiscountStatus.Approved,
            CreatedBy = actorId,
            ApprovedBy = null,
            DecidedAt = now,
            CreatedAt = now,
        };
        db.Discounts.Add(discount);
        audit.Record(AuditService.EntityStudentDiscount, discount.Id.ToString(), "create",
            $"Katalogdan chegirma biriktirildi (tasdiqsiz): {type.Name} — {Show(type.Percent, type.Amount)}, "
            + $"{request.StartsOn:yyyy-MM-dd} dan" + (request.EndsOn is { } e ? $" {e:yyyy-MM-dd} gacha" : ""),
            after: new { discount.Percent, discount.Amount, discount.CategoryId, discount.StartsOn, discount.EndsOn },
            studentId: student.Id);
        await db.SaveChangesAsync(ct);

        // Months already invoiced from the start month on: give them the difference now.
        var adjuster = new InvoiceAdjustmentService(db, audit);
        var (invoices, paid) = await adjuster.LockStudentAsync(student.Id, ct);
        var fromMonth = new DateOnly(request.StartsOn.Year, request.StartsOn.Month, 1);
        // A sickness discount on the month is its own reduction — it does not count as this one.
        var sick = await adjuster.SickNetAsync(invoices.Select(i => i.Id).ToList(), ct);
        var groupId = Guid.NewGuid();
        var adjusted = 0;
        var total = 0m;
        foreach (var invoice in invoices.Where(i => i.PeriodMonth >= fromMonth
                                                    && (request.EndsOn == null || i.PeriodMonth <= request.EndsOn)
                                                    && (type.CategoryId == null || i.CategoryId == type.CategoryId))
                                        .OrderBy(i => i.PeriodMonth))
        {
            var target = DiscountMath.DiscountFor(invoice.Amount, type.Percent, type.Amount);
            var extra = decimal.Round(target - (invoice.Discount - sick.GetValueOrDefault(invoice.Id)), MoneyScale);
            if (extra <= 0m) continue;
            var taken = await adjuster.ApplyAsync(invoice, paid, extra, InvoiceAdjustmentKind.Discount,
                sickDays: null, discount.Id, type.Name, groupId, actorId, ct);
            if (taken <= 0m) continue;
            adjusted++;
            total += taken;
        }
        await tx.CommitAsync(ct);

        var dto = await new DiscountService(db, audit).GetAsync(discount.Id, ct)
            ?? throw BillingRuleException.NotFound("discount_not_found", "Chegirma topilmadi.");
        return new AttachDiscountResult(dto, adjusted, total);
    }

    /// <summary>No month after <paramref name="request"/>.EndsOn gets it; written invoices stay.</summary>
    public async Task<DiscountDto> EndAsync(Guid id, EndDiscountRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId);
        var discount = await db.Discounts.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw BillingRuleException.NotFound("discount_not_found", "Chegirma topilmadi.");
        if (discount.Status != DiscountStatus.Approved)
            throw BillingRuleException.Conflict("discount_not_active", "Faqat amaldagi chegirmani tugatish mumkin.");
        if (request.EndsOn < discount.StartsOn)
            throw BillingRuleException.Invalid("invalid_period", "Tugash sanasi boshlanishdan oldin bo'lmasin.");
        if (discount.EndsOn is { } current && current <= request.EndsOn)
            throw BillingRuleException.Conflict("discount_already_ends", $"Chegirma {current:yyyy-MM-dd} da allaqachon tugaydi.");

        var before = new { discount.EndsOn };
        discount.EndsOn = request.EndsOn;
        audit.Record(AuditService.EntityStudentDiscount, discount.Id.ToString(), "end",
            $"Chegirma tugatildi: {discount.Reason} — {request.EndsOn:yyyy-MM-dd} gacha",
            before: before, after: new { discount.EndsOn }, studentId: discount.StudentId);
        await db.SaveChangesAsync(ct);
        return await new DiscountService(db, audit).GetAsync(discount.Id, ct)
            ?? throw BillingRuleException.NotFound("discount_not_found", "Chegirma topilmadi.");
    }

    /// <summary>
    /// Deletes a discount for good (client, 2026-10-27): as if it had never been attached. Every
    /// month it reduced gets the debt back — its own adjustments are reversed, the share accrual
    /// wrote into the invoice is undone — and the discount that would have won without it (accrual
    /// order) tops the month up instead. The row stays as <see cref="DiscountStatus.Deleted"/>:
    /// the adjustments point at it, and the list no longer shows it.
    /// </summary>
    public async Task<DeleteDiscountResult> DeleteAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        RequireActor(actorId);
        var discount = await db.Discounts.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw BillingRuleException.NotFound("discount_not_found", "Chegirma topilmadi.");
        if (discount.Status == DiscountStatus.Deleted)
            throw BillingRuleException.Conflict("discount_deleted", "Chegirma allaqachon o'chirilgan.");
        var name = discount.Reason;

        // Pending / rejected never reached an invoice — nothing to give back.
        if (discount.Status != DiscountStatus.Approved)
        {
            MarkDeleted(discount, 0, 0m);
            await db.SaveChangesAsync(ct);
            return new DeleteDiscountResult(discount.Id, name, 0, 0m);
        }

        // Bringing the debt back is not for an archived pupil (2026-10-08, as cancelling a sickness discount).
        if (await db.Students.AnyAsync(s => s.Id == discount.StudentId && s.IsArchived, ct))
            throw BillingRuleException.Conflict("student_archived", "O'quvchi arxivlangan — chegirmani o'chirib bo'lmaydi.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var adjuster = new InvoiceAdjustmentService(db, audit);
        var (invoices, paid) = await adjuster.LockStudentAsync(discount.StudentId, ct);
        var before = invoices.ToDictionary(i => i.Id, i => i.Amount - i.Discount);
        var groupId = Guid.NewGuid();
        var note = $"Chegirma o'chirildi: {name}";

        // 1. Its own adjustments (months invoiced before it was attached), read under the locks.
        var own = await db.InvoiceAdjustments.AsNoTracking()
            .Where(a => a.DiscountId == discount.Id && a.Kind == InvoiceAdjustmentKind.Discount && a.ReversalOf == null
                        && !db.InvoiceAdjustments.Any(r => r.ReversalOf == a.Id))
            .OrderBy(a => a.InvoiceId)
            .ToListAsync(ct);
        var touched = new HashSet<Guid>();
        foreach (var row in own)
        {
            // A voided month carries no debt — there is nothing to put back on it.
            if (invoices.SingleOrDefault(i => i.Id == row.InvoiceId) is not { } invoice) continue;
            await adjuster.ReverseAsync(row, invoice, paid, note, groupId, actorId, ct);
            touched.Add(invoice.Id);
        }

        // 2. The months it covers: undo the share accrual wrote, let the next discount take over.
        var others = await db.Discounts.AsNoTracking()
            .Where(d => d.StudentId == discount.StudentId && d.Id != discount.Id
                        && d.Status == DiscountStatus.Approved && (d.ApprovedBy != null || d.DiscountTypeId != null))
            .ToListAsync(ct);
        var covered = invoices
            .Where(i => Covers(discount, i))
            .OrderBy(i => i.PeriodMonth)
            .ToList();
        var net = await adjuster.AdjustedNetAsync(covered.Select(i => i.Id).ToList(), ct);
        var sick = await adjuster.SickNetAsync(covered.Select(i => i.Id).ToList(), ct);
        foreach (var invoice in covered)
        {
            // Replay accrual's choice with the discounts in force when the month was written.
            var inForce = others
                .Where(d => Covers(d, invoice) && (d.DecidedAt ?? d.CreatedAt) <= invoice.CreatedAt)
                .Append(discount)
                .Where(d => (d.DecidedAt ?? d.CreatedAt) <= invoice.CreatedAt);
            if (AccrualWinner(inForce)?.Id == discount.Id)
            {
                var written = invoice.Discount - net.GetValueOrDefault(invoice.Id);
                await adjuster.UndoDiscountAsync(invoice, paid, written, discount.Id, note, groupId, actorId, ct);
                touched.Add(invoice.Id);
            }

            // Only a month it actually reduced is re-decided; the others keep what they have.
            if (touched.Contains(invoice.Id) && AccrualWinner(others.Where(d => Covers(d, invoice))) is { } next)
            {
                var target = DiscountMath.DiscountFor(invoice.Amount, next.Percent, next.Amount);
                var extra = decimal.Round(target - (invoice.Discount - sick.GetValueOrDefault(invoice.Id)), MoneyScale);
                if (extra > 0m)
                    await adjuster.ApplyAsync(invoice, paid, extra, InvoiceAdjustmentKind.Discount,
                        sickDays: null, next.Id, next.Reason, groupId, actorId, ct);
            }
        }

        var changed = invoices.Where(i => i.Amount - i.Discount != before[i.Id]).ToList();
        var restored = decimal.Round(changed.Sum(i => i.Amount - i.Discount - before[i.Id]), MoneyScale);
        MarkDeleted(discount, changed.Count, restored);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DeleteDiscountResult(discount.Id, name, changed.Count, restored);
    }

    // ------------------------------------------------------------------ helpers

    private void MarkDeleted(Discount discount, int invoices, decimal restored)
    {
        var before = new { discount.Status };
        discount.Status = DiscountStatus.Deleted;
        audit.Record(AuditService.EntityStudentDiscount, discount.Id.ToString(), "delete",
            $"Chegirma butunlay o'chirildi: {discount.Reason} — {Show(discount.Percent, discount.Amount)}"
            + (invoices > 0 ? $"; {invoices} ta oy qarziga {AuditService.Money(restored)} so'm qaytdi" : ""),
            before: before, after: new { discount.Status }, studentId: discount.StudentId);
    }

    /// <summary>The discount reaches this invoice's month and abonement type (accrual's filter).</summary>
    private static bool Covers(Discount d, Invoice invoice) =>
        d.StartsOn <= invoice.PeriodMonth.AddMonths(1).AddDays(-1)
        && (d.EndsOn == null || d.EndsOn >= invoice.PeriodMonth)
        && (d.CategoryId == null || d.CategoryId == invoice.CategoryId);

    /// <summary>Accrual's pick: category-specific beats global, then the later start, then the later one.</summary>
    private static Discount? AccrualWinner(IEnumerable<Discount> candidates) =>
        candidates
            .OrderByDescending(d => d.CategoryId != null)
            .ThenByDescending(d => d.StartsOn)
            .ThenByDescending(d => d.CreatedAt)
            .FirstOrDefault();

    private async Task<List<DiscountTypeDto>> DtosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var today = AppClock.Today;
        var counts = await db.Discounts.AsNoTracking()
            .Where(d => d.DiscountTypeId != null && ids.Contains(d.DiscountTypeId.Value)
                        && d.Status == DiscountStatus.Approved && (d.EndsOn == null || d.EndsOn >= today))
            .GroupBy(d => d.DiscountTypeId!.Value)
            .Select(g => new { Id = g.Key, Students = g.Select(d => d.StudentId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Students, ct);
        var rows = await (
            from t in db.DiscountTypes.AsNoTracking()
            where ids.Contains(t.Id)
            join cRaw in db.FeeCategories.AsNoTracking() on t.CategoryId equals (Guid?)cRaw.Id into cats
            from c in cats.DefaultIfEmpty()
            orderby t.Name
            select new { t.Id, t.Name, t.Percent, t.Amount, t.CategoryId, CategoryName = c == null ? null : c.Name, t.IsActive })
            .ToListAsync(ct);
        return [.. rows.Select(r => new DiscountTypeDto(
            r.Id, r.Name, r.Percent, r.Amount, r.CategoryId, r.CategoryName, r.IsActive, counts.GetValueOrDefault(r.Id)))];
    }

    private async Task RequireFreeNameAsync(string name, Guid? exclude, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.DiscountTypes.AnyAsync(t => t.IsActive && t.Name.ToLower() == lower && (exclude == null || t.Id != exclude), ct))
            throw BillingRuleException.Conflict("discount_type_name_taken", $"'{name}' nomli faol chegirma turi allaqachon bor.");
    }

    private static string RequireName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0) throw BillingRuleException.Invalid("name_required", "Chegirma nomi majburiy.");
        if (name.Length > NameMax) throw BillingRuleException.Invalid("name_too_long", $"Chegirma nomi {NameMax} belgidan oshmasin.");
        return name;
    }

    /// <summary>
    /// A catalog discount is a fixed so'm amount — client, 2026-09-28: "chegirma foizda emas summada
    /// bo'lsin". The percent column stays (hand-written discounts use it) but is always 0 here.
    /// </summary>
    private static (decimal Percent, decimal Amount) RequireValue(decimal percent, decimal amount)
    {
        if (percent != 0m || amount <= 0m)
            throw BillingRuleException.Invalid("invalid_discount", "Chegirma summada bo'ladi: summa 0 dan katta, foiz yo'q.");
        return (0m, decimal.Round(amount, MoneyScale));
    }

    private static void RequireActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw BillingRuleException.Invalid("actor_required", "Amalni bajaruvchi aniqlanmadi.");
    }

    private static string Show(decimal percent, decimal amount) =>
        string.Join(" + ", new[]
        {
            percent > 0m ? $"{percent:0.##}%" : null,
            amount > 0m ? $"{AuditService.Money(amount)} so'm" : null,
        }.Where(x => x is not null));

    private static object Snapshot(DiscountType t) => new { t.Name, t.Percent, t.Amount, t.CategoryId, t.IsActive };
}
