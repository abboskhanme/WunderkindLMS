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

    // ------------------------------------------------------------------ helpers

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
