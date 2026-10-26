using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// "Oylarni o'chirish" with the payment going back to the balance, re-bill and also-bill (client,
/// 2026-10-26; billing-plans.md §10.5). The case it was built for: the imported September "O'qish
/// to'lovi" holds EduSchool's whole September charge (tuition + dorm). Each test in its own database.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class MonthRemovalBalanceTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid DormitoryCategory = new("00000000-0000-0000-0000-0000000000c3");
    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);
    private static readonly DateOnly LastMonth = ThisMonth.AddMonths(-1);

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("monthbal");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private static MonthRemovalService Removal(AppDbContext db) => new(db, new AuditService(db, new HttpContextAccessor()));

    private static InvoiceService Invoices(AppDbContext db) => new(db, new LedgerService(db));

    /// <summary>
    /// Group A: tuition 1 000 000 + dorm 480 000 in one imported tuition invoice, fully paid. After the fix
    /// the month is tuition 1 000 000 + dorm 480 000, both paid by the same receipt; the debt is unchanged.
    /// </summary>
    [Fact]
    public async Task Lump_ajratiladi_tolov_ozgarmaydi_qarz_ham_ozgarmaydi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var tuition = await AddSubscriptionAsync(db, world, TuitionCategory, 1_000_000m, LastMonth);
        var dorm = await AddSubscriptionAsync(db, world, DormitoryCategory, 480_000m, LastMonth, accrueFrom: ThisMonth);
        var lump = await AddImportedInvoiceAsync(db, world, LastMonth, 1_480_000m);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var payment = await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 1_480_000m, PaymentMethod.Cash, "Sentabr", []), world.CashierId);
        Assert.Equal(-1_480_000m, await new StudentBalanceQuery(db).ForAsync(world.StudentId));

        var preview = await Removal(db).PreviewAsync(tuition, world.RemoverId);
        var line = Assert.Single(preview.Months, m => m.InvoiceId == lump);
        Assert.Equal(1_000_000m, line.Rebill);
        var other = Assert.Single(preview.Others!);
        Assert.Equal(dorm, other.SubscriptionId);
        Assert.Equal(480_000m, Assert.Single(other.Months, m => m.Month == LastMonth.ToString("yyyy-MM")).Amount);

        var result = await Removal(db).RemoveAsync(tuition,
            new MonthRemovalRequest([lump], "EduSchool summasi abonement bo'yicha ajratildi",
                PaidTo: MonthRemovalPaidTo.Balance, Rebill: true, AlsoBill: [dorm]), world.RemoverId);
        Assert.Equal(1_480_000m, result.Released);
        Assert.Equal(1_000_000m, result.Rebilled);
        Assert.Equal(480_000m, result.AlsoBilled);
        Assert.Equal(0, result.PaymentsReversed);
        Assert.Equal(0m, result.ReversedPaid);

        await using var check = NewDb();
        var old = await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == lump);
        Assert.Equal(InvoiceStatus.Void, old.Status);
        Assert.NotNull(old.RemovedAt);
        var september = await check.Invoices.AsNoTracking()
            .Where(i => i.StudentId == world.StudentId && i.PeriodMonth == LastMonth && i.Status != InvoiceStatus.Void)
            .ToListAsync();
        Assert.Equal(2, september.Count);
        Assert.Equal(1_000_000m, september.Single(i => i.CategoryId == TuitionCategory).Amount);
        Assert.Equal(480_000m, september.Single(i => i.CategoryId == DormitoryCategory).Amount);
        Assert.All(september, i => Assert.Equal(InvoiceStatus.Paid, i.Status));
        Assert.All(await check.Invoices.AsNoTracking().Where(i => i.StudentId == world.StudentId && i.PeriodMonth == ThisMonth).ToListAsync(),
            i => Assert.Equal(InvoiceStatus.Open, i.Status));

        // The same receipt pays the new rows — no new receipt, no storno, the cash and the debt are unchanged.
        Assert.Equal(payment.Id, Assert.Single(await check.Payments.AsNoTracking().Where(p => p.StudentId == world.StudentId).ToListAsync()).Id);
        Assert.Equal(-1_480_000m, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        Assert.Equal(1_480_000m, await new LedgerService(check).BalanceAsync(Accounts.Cash));
        Assert.Equal(1_480_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        Assert.All(await check.PaymentAllocations.AsNoTracking().Where(a => a.Kind == PaymentAllocationKind.Release).ToListAsync(),
            a => Assert.NotNull(a.RemovalGroupId));
        await AssertLedgerBalancedAsync(check);

        // The dorm abonement now bills from its start; accrual writes nothing twice.
        Assert.Null((await check.StudentSubscriptions.AsNoTracking().SingleAsync(s => s.Id == dorm)).AccrueFrom);
        Assert.Equal(0, (await Invoices(check).AccrueStudentAsync(world.StudentId, world.ActorId)).Sum(r => r.Created));
    }

    /// <summary>
    /// Group B (prod: Qambarov): EduSchool's lump holds a dorm the pupil never had in LMS (the dorm abonement
    /// was cancelled on its start day). Only the abonement is billed; the paid surplus closes the next month.
    /// </summary>
    [Fact]
    public async Task Abonementsiz_qoshimcha_olib_tashlanadi_ortgan_tolov_keyingi_oyni_yopadi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var tuition = await AddSubscriptionAsync(db, world, TuitionCategory, 1_000_000m, LastMonth);
        await AddSubscriptionAsync(db, world, DormitoryCategory, 480_000m, LastMonth, endsOn: LastMonth, accrueFrom: ThisMonth);
        var lump = await AddImportedInvoiceAsync(db, world, LastMonth, 1_480_000m);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 1_400_000m, PaymentMethod.Card, null, []), world.CashierId);
        Assert.Equal(-1_080_000m, await new StudentBalanceQuery(db).ForAsync(world.StudentId));

        var preview = await Removal(db).PreviewAsync(tuition, world.RemoverId);
        Assert.Empty(preview.Others!); // the cancelled dorm is not offered

        var result = await Removal(db).RemoveAsync(tuition,
            new MonthRemovalRequest([lump], "Faqat abonement bo'yicha", PaidTo: MonthRemovalPaidTo.Balance, Rebill: true),
            world.RemoverId);
        Assert.Equal(1_400_000m, result.Released);
        Assert.Equal(1_000_000m, result.Rebilled);

        await using var check = NewDb();
        var september = await check.Invoices.AsNoTracking()
            .SingleAsync(i => i.StudentId == world.StudentId && i.PeriodMonth == LastMonth && i.Status != InvoiceStatus.Void);
        Assert.Equal(1_000_000m, september.Amount);
        Assert.Equal(InvoiceStatus.Paid, september.Status);
        var october = await check.Invoices.AsNoTracking()
            .SingleAsync(i => i.StudentId == world.StudentId && i.PeriodMonth == ThisMonth && i.CategoryId == TuitionCategory);
        Assert.Equal(InvoiceStatus.Partial, october.Status);
        Assert.Equal(-600_000m, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        Assert.Equal(600_000m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        await AssertLedgerBalancedAsync(check);
    }

    /// <summary>No receipt is reversed in balance mode, so the two-person rule (own receipt) does not stop it.</summary>
    [Fact]
    public async Task Balansga_qaytarishda_ozi_olgan_chek_tosiq_emas_pul_avans_bolib_qoladi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var sub = await AddSubscriptionAsync(db, world, TuitionCategory, 1_000_000m, ThisMonth);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 1_000_000m, PaymentMethod.Cash, null, []), world.RemoverId);
        var month = Assert.Single((await Removal(db).PreviewAsync(sub, world.RemoverId)).Months);
        Assert.NotNull(month.BlockedReason); // storno would be blocked

        var result = await Removal(db).RemoveAsync(sub,
            new MonthRemovalRequest([month.InvoiceId], "Xato oy", PaidTo: MonthRemovalPaidTo.Balance), world.RemoverId);
        Assert.Equal(1_000_000m, result.Released);
        Assert.Equal(0m, result.Rebilled);

        await using var check = NewDb();
        Assert.NotNull((await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == month.InvoiceId)).RemovedAt);
        Assert.Equal(1, await check.Payments.CountAsync(p => p.StudentId == world.StudentId));
        Assert.Equal(1_000_000m, await new StudentBalanceQuery(check).ForAsync(world.StudentId)); // advance
        Assert.Equal(1_000_000m, await new LedgerService(check).BalanceAsync(Accounts.Cash));
        await AssertLedgerBalancedAsync(check);
    }

    [Fact]
    public async Task Notogri_sorovlar_rad_etiladi_hech_narsa_ozgarmaydi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var sub = await AddSubscriptionAsync(db, world, TuitionCategory, 1_000_000m, ThisMonth);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var month = Assert.Single((await Removal(db).PreviewAsync(sub, world.RemoverId)).Months);

        var badMode = await Assert.ThrowsAsync<BillingRuleException>(() => Removal(db).RemoveAsync(sub,
            new MonthRemovalRequest([month.InvoiceId], "x", PaidTo: "cash"), world.RemoverId));
        Assert.Equal("paid_to_invalid", badMode.Code);
        var rebillDelete = await Assert.ThrowsAsync<BillingRuleException>(() => Removal(db).RemoveAsync(sub,
            new MonthRemovalRequest([month.InvoiceId], "x", DeleteSubscription: true, PaidTo: MonthRemovalPaidTo.Balance, Rebill: true),
            world.RemoverId));
        Assert.Equal("rebill_with_delete", rebillDelete.Code);
        var foreign = await Assert.ThrowsAsync<BillingRuleException>(() => Removal(db).RemoveAsync(sub,
            new MonthRemovalRequest([month.InvoiceId], "x", PaidTo: MonthRemovalPaidTo.Balance, AlsoBill: [sub]), world.RemoverId));
        Assert.Equal("also_bill_invalid", foreign.Code);

        await using var check = NewDb();
        var row = await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == month.InvoiceId);
        Assert.Equal(InvoiceStatus.Open, row.Status);
        Assert.Null(row.RemovedAt);
        await AssertLedgerBalancedAsync(check);
    }

    // =================================================================

    /// <param name="ActorId">Posts the accruals.</param>
    /// <param name="RemoverId">Another admin — removes.</param>
    private sealed record World(string ActorId, string RemoverId, string CashierId, string StudentId);

    private static async Task<World> NewWorldAsync(AppDbContext db)
    {
        var actorId = await AddUserAsync(db, Roles.Admin);
        var removerId = await AddUserAsync(db, Roles.Admin);
        var cashierId = await AddUserAsync(db, Roles.Cashier);
        var student = new Student
        {
            FullName = "O'quvchi " + Guid.NewGuid().ToString("N")[..8],
            ClassName = "9-A",
            EnrollmentDate = "2026-01-01",
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        await new CashShiftService(db).OpenAsync(cashierId, 0m);
        return new World(actorId, removerId, cashierId, student.Id);
    }

    private static async Task<string> AddUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser { FullName = $"Test {role}", Role = role, Email = $"{role}.{Guid.NewGuid():N}" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> AddSubscriptionAsync(
        AppDbContext db, World world, Guid category, decimal amount, DateOnly startsOn,
        DateOnly? endsOn = null, DateOnly? accrueFrom = null)
    {
        var subscription = new StudentSubscription
        {
            StudentId = world.StudentId,
            CategoryId = category,
            MonthlyAmount = amount,
            StartsOn = startsOn,
            EndsOn = endsOn,
            AccrueFrom = accrueFrom,
            CreatedBy = world.ActorId,
            CreatedAt = AppClock.NowInstant.AddDays(-60),
        };
        db.StudentSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    /// <summary>The import's shape (2026-09-24): one tuition invoice with EduSchool's whole month and its accrual batch.</summary>
    private static async Task<Guid> AddImportedInvoiceAsync(AppDbContext db, World world, DateOnly month, decimal amount)
    {
        var invoice = new Invoice
        {
            StudentId = world.StudentId,
            CategoryId = TuitionCategory,
            PeriodMonth = month,
            Amount = amount,
            DueOn = month.AddDays(9),
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant.AddDays(-30),
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, amount, LedgerRefType.Invoice, invoice.Id, month, "import"),
            new LedgerPosting(Accounts.RevenueFor(FeeCategoryCode.Tuition), LedgerDirection.Credit, amount, LedgerRefType.Invoice, invoice.Id, month, "import"),
        ], world.ActorId);
        return invoice.Id;
    }

    private static async Task AssertLedgerBalancedAsync(AppDbContext db)
    {
        var rows = await db.LedgerEntries.AsNoTracking().ToListAsync();
        var debit = rows.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
        var credit = rows.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);
        Assert.True(debit == credit, $"Jurnal nomutanosib: debet {debit}, kredit {credit}.");
    }
}
