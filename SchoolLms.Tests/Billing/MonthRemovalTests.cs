using System.Net;
using System.Net.Http.Json;
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
/// "Abonement oyini butunlay o'chirish" (client, 2026-10-07): even a paid month can be removed; the
/// payments stay as archive (storno) and leave the accounts — only the removed month's share, the
/// rest of a receipt is re-issued onto the months it paid. Each test in its own database.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class MonthRemovalTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);
    private static readonly DateOnly LastMonth = ThisMonth.AddMonths(-1);

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("monthrm");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private static MonthRemovalService Removal(AppDbContext db) => new(db, new AuditService(db, new HttpContextAccessor()));

    private static InvoiceService Invoices(AppDbContext db) => new(db, new LedgerService(db));

    [Fact]
    public async Task Tolangan_oy_ochiriladi_tolov_arxivda_qoladi_qolgani_qayta_rasmiylashtiriladi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var sub = await AddSubscriptionAsync(db, world, 1_600_000m, LastMonth);
        await Invoices(db).AccrueMonthAsync(LastMonth, world.ActorId);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        var payment = await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 3_200_000m, PaymentMethod.Cash, "Ikki oy", []), world.CashierId);

        var preview = await Removal(db).PreviewAsync(sub, world.RemoverId);
        Assert.Equal(2, preview.Months.Count);
        var last = preview.Months.Single(m => m.Month == LastMonth.ToString("yyyy-MM"));
        Assert.Equal(1_600_000m, last.Paid);
        Assert.Null(last.BlockedReason);
        Assert.Equal(payment.ReceiptNo, Assert.Single(last.Payments).ReceiptNo);

        var result = await Removal(db).RemoveAsync(sub, new MonthRemovalRequest([last.InvoiceId], "Noto'g'ri kiritilgan"), world.RemoverId);
        Assert.Equal(1, result.Months);
        Assert.Equal(1_600_000m, result.ReversedPaid);
        Assert.Equal(1, result.PaymentsReversed);
        Assert.Equal(1, result.PaymentsReissued);

        await using var check = NewDb();
        var removed = await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == last.InvoiceId);
        Assert.Equal(InvoiceStatus.Void, removed.Status);
        Assert.NotNull(removed.RemovedAt);
        Assert.Equal("Noto'g'ri kiritilgan", removed.RemoveReason);
        var kept = await check.Invoices.AsNoTracking().SingleAsync(i => i.StudentId == world.StudentId && i.PeriodMonth == ThisMonth);
        Assert.Equal(InvoiceStatus.Paid, kept.Status);

        // The receipt stays (archive) and is reversed; a new receipt carries the other month.
        var rows = await check.Payments.AsNoTracking().Where(p => p.StudentId == world.StudentId).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, p => p.ReversalOf == payment.Id && p.Amount == 3_200_000m);
        var copy = Assert.Single(rows, p => p.ReversalOf == null && p.Id != payment.Id);
        Assert.Equal(1_600_000m, copy.Amount);
        Assert.Equal(1_600_000m, Assert.Single(await check.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == copy.Id).ToListAsync(), a => a.InvoiceId == kept.Id).Amount);

        // Out of the accounts: no debt, the cash holds only the other month, the ledger balances.
        Assert.Equal(0m, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        Assert.Equal(1_600_000m, await new LedgerService(check).BalanceAsync(Accounts.Cash));
        Assert.Equal(0m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        await AssertLedgerBalancedAsync(check);

        // Not billed again — not even after the subscription is edited.
        var row = await check.StudentSubscriptions.SingleAsync(s => s.Id == sub);
        row.UpdatedAt = AppClock.NowInstant;
        await check.SaveChangesAsync();
        Assert.Equal(0, (await Invoices(check).AccrueStudentAsync(world.StudentId, world.ActorId)).Sum(r => r.Created));
    }

    [Fact]
    public async Task Ozi_qabul_qilgan_tolov_oyini_ochira_olmaydi_va_hech_narsa_ozgarmaydi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var sub = await AddSubscriptionAsync(db, world, 1_000_000m, ThisMonth);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 1_000_000m, PaymentMethod.Cash, null, []), world.RemoverId);

        var preview = await Removal(db).PreviewAsync(sub, world.RemoverId);
        Assert.NotNull(Assert.Single(preview.Months).BlockedReason);

        var error = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Removal(db).RemoveAsync(sub, new MonthRemovalRequest([preview.Months[0].InvoiceId], "Xato"), world.RemoverId));
        Assert.Equal("own_payment_reversal", error.Code);

        await using var check = NewDb();
        Assert.Equal(InvoiceStatus.Paid, (await check.Invoices.AsNoTracking().SingleAsync(i => i.StudentId == world.StudentId)).Status);
        Assert.Equal(1, await check.Payments.CountAsync(p => p.StudentId == world.StudentId));
        await AssertLedgerBalancedAsync(check);
    }

    /// <summary>The month's accrual is bookkeeping — the admin it was posted under may remove it (client, 2026-10-07).</summary>
    [Fact]
    public async Task Oy_hisobi_ozi_nomidan_yozilgan_admin_ham_ochira_oladi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var sub = await AddSubscriptionAsync(db, world, 700_000m, ThisMonth);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 700_000m, PaymentMethod.Cash, null, []), world.CashierId);

        var month = Assert.Single((await Removal(db).PreviewAsync(sub, world.ActorId)).Months);
        Assert.Null(month.BlockedReason);
        var result = await Removal(db).RemoveAsync(sub, new MonthRemovalRequest([month.InvoiceId], "Xato"), world.ActorId);
        Assert.Equal(700_000m, result.ReversedPaid);

        await using var check = NewDb();
        Assert.NotNull((await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == month.InvoiceId)).RemovedAt);
        Assert.Equal(0m, await new LedgerService(check).BalanceAsync(Accounts.Receivable));
        await AssertLedgerBalancedAsync(check);
    }

    [Fact]
    public async Task Hamma_oylari_ochirilsa_abonementning_ozi_ham_ochiriladi()
    {
        await using var db = NewDb();
        var world = await NewWorldAsync(db);
        var sub = await AddSubscriptionAsync(db, world, 900_000m, LastMonth, ThisMonth.AddMonths(1).AddDays(-1));
        await Invoices(db).AccrueMonthAsync(LastMonth, world.ActorId);
        await Invoices(db).AccrueMonthAsync(ThisMonth, world.ActorId);
        await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 500_000m, PaymentMethod.Card, null, []), world.CashierId);
        var months = (await Removal(db).PreviewAsync(sub, world.RemoverId)).Months;

        var partial = await Assert.ThrowsAsync<BillingRuleException>(() =>
            Removal(db).RemoveAsync(sub, new MonthRemovalRequest([months[0].InvoiceId], "Xato", DeleteSubscription: true), world.RemoverId));
        Assert.Equal("subscription_has_months", partial.Code);

        await using (var fresh = NewDb())
        {
            var result = await Removal(fresh).RemoveAsync(sub,
                new MonthRemovalRequest([.. months.Select(m => m.InvoiceId)], "Abonement xato ochilgan", DeleteSubscription: true), world.RemoverId);
            Assert.True(result.SubscriptionDeleted);
            Assert.Equal(500_000m, result.ReversedPaid);
            Assert.Equal(0, result.PaymentsReissued);
        }

        await using var check = NewDb();
        Assert.False(await check.StudentSubscriptions.AnyAsync(s => s.Id == sub));
        Assert.All(await check.Invoices.AsNoTracking().Where(i => i.StudentId == world.StudentId).ToListAsync(),
            i => Assert.NotNull(i.RemovedAt));
        Assert.Equal(0m, await new StudentBalanceQuery(check).ForAsync(world.StudentId));
        Assert.Equal(0m, await new LedgerService(check).BalanceAsync(Accounts.Bank));
        await AssertLedgerBalancedAsync(check);
        Assert.Equal(0, (await Invoices(check).AccrueStudentAsync(world.StudentId, world.ActorId)).Sum(r => r.Created));
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    public async Task Admin_va_direktordan_boshqasi_oyni_ochira_olmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        var id = Guid.NewGuid();
        var get = await client.GetAsync($"/api/admin/billing/subscriptions/{id}/remove-months");
        var post = await client.PostAsJsonAsync($"/api/admin/billing/subscriptions/{id}/remove-months",
            new { invoiceIds = new[] { Guid.NewGuid() }, reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    // =================================================================

    /// <param name="ActorId">Posts the accruals.</param>
    /// <param name="RemoverId">Another admin — removes (two-person rule).</param>
    private sealed record World(string ActorId, string RemoverId, string CashierId, string StudentId);

    private static async Task<World> NewWorldAsync(AppDbContext db)
    {
        var actorId = await AddUserAsync(db, Roles.Admin);
        var removerId = await AddUserAsync(db, Roles.Admin);
        var cashierId = await AddUserAsync(db, Roles.Cashier);
        var student = new Student
        {
            FullName = "O'quvchi " + Guid.NewGuid().ToString("N")[..8],
            ClassName = "1-A",
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
        AppDbContext db, World world, decimal amount, DateOnly startsOn, DateOnly? endsOn = null)
    {
        var subscription = new StudentSubscription
        {
            StudentId = world.StudentId,
            CategoryId = TuitionCategory,
            MonthlyAmount = amount,
            StartsOn = startsOn,
            EndsOn = endsOn,
            CreatedBy = world.ActorId,
            CreatedAt = AppClock.NowInstant.AddDays(-60),
        };
        db.StudentSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    private static async Task AssertLedgerBalancedAsync(AppDbContext db)
    {
        var rows = await db.LedgerEntries.AsNoTracking().ToListAsync();
        var debit = rows.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
        var credit = rows.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);
        Assert.True(debit == credit, $"Jurnal nomutanosib: debet {debit}, kredit {credit}.");
    }
}
