using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// "Abonement tranzaksiyalari" by abonement (client, 2026-10-08: "qaysi abonement bo'yicha nechta
/// student bor, qancha to'lovlar bo'lgan"). An invoice's abonement is the one of the subscription
/// covering its month; a month nobody covers is "Abonementsiz".
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class InvoicesByPlanTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("byplan");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    [Fact]
    public async Task Abonement_boyicha_oquvchi_hisob_tolov_va_qarzdor()
    {
        await using var db = NewDb();
        var actor = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = "admin." + Guid.NewGuid().ToString("N")[..8] };
        var cashier = new AppUser { FullName = "Kassir", Role = Roles.Cashier, Email = "cash." + Guid.NewGuid().ToString("N")[..8] };
        db.Users.AddRange(actor, cashier);
        var big = new SubscriptionPlan { Name = "5-11 sinflar", CategoryId = TuitionCategory, MonthlyAmount = 1_000_000m, CreatedAt = AppClock.NowInstant };
        var small = new SubscriptionPlan { Name = "1-4 sinflar", CategoryId = TuitionCategory, MonthlyAmount = 800_000m, CreatedAt = AppClock.NowInstant };
        db.SubscriptionPlans.AddRange(big, small);
        var a = Student("A");
        var b = Student("B");
        var c = Student("C");
        var orphan = Student("Orphan");
        db.Students.AddRange(a, b, c, orphan);
        await db.SaveChangesAsync();
        foreach (var (student, plan) in new[] { (a, big), (b, big), (c, small) })
            db.StudentSubscriptions.Add(new StudentSubscription
            {
                StudentId = student.Id, CategoryId = TuitionCategory, PlanId = plan.Id, MonthlyAmount = plan.MonthlyAmount,
                StartsOn = ThisMonth, CreatedBy = actor.Id, CreatedAt = AppClock.NowInstant.AddDays(-30),
            });
        await db.SaveChangesAsync();

        var invoices = new InvoiceService(db, new LedgerService(db));
        await invoices.AccrueMonthAsync(ThisMonth, actor.Id);
        // A month no abonement covers — e.g. an imported balance.
        db.Invoices.Add(new Invoice
        {
            StudentId = orphan.Id, CategoryId = TuitionCategory, PeriodMonth = ThisMonth, Amount = 300_000m,
            DueOn = ThisMonth.AddDays(9), Status = InvoiceStatus.Open, CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();
        await new CashShiftService(db).OpenAsync(cashier.Id, 0m);
        await new PaymentService(db, new LedgerService(db)).AcceptAsync(
            new AcceptPaymentRequest(a.Id, 1_000_000m, PaymentMethod.Cash, null, []), cashier.Id);

        var report = await invoices.ByPlanAsync(new InvoicePageQuery(FromMonth: ThisMonth, ToMonth: ThisMonth));

        var bigRow = report.Rows.Single(r => r.PlanId == big.Id);
        Assert.Equal(2, bigRow.Students);
        Assert.Equal(2_000_000m, bigRow.Amount);
        Assert.Equal(1_000_000m, bigRow.Paid);
        Assert.Equal(1_000_000m, bigRow.Remaining);
        Assert.Equal(1, bigRow.Debtors);

        var smallRow = report.Rows.Single(r => r.PlanId == small.Id);
        Assert.Equal(1, smallRow.Students);
        Assert.Equal(800_000m, smallRow.Remaining);

        var none = report.Rows.Single(r => r.PlanId == null);
        Assert.Equal("Abonementsiz", none.PlanName);
        Assert.Equal(300_000m, none.Amount);

        Assert.Equal(4, report.Total.Students);
        Assert.Equal(3_100_000m, report.Total.Amount);
        Assert.Equal(1_000_000m, report.Total.Paid);

        // The list narrowed to one abonement agrees with its line.
        var page = await invoices.ListPageAsync(new InvoicePageQuery(FromMonth: ThisMonth, ToMonth: ThisMonth, PlanId: big.Id));
        Assert.Equal(2, page.Total);
        Assert.Equal(bigRow.Remaining, page.Totals.Remaining);
        var orphanPage = await invoices.ListPageAsync(new InvoicePageQuery(FromMonth: ThisMonth, ToMonth: ThisMonth, PlanId: InvoiceService.NoPlan));
        Assert.Equal(orphan.Id, Assert.Single(orphanPage.Rows).StudentId);
    }

    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    public async Task Moliya_rolisiz_kirib_bolmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/billing/invoices/by-plan")).StatusCode);
    }

    [Fact]
    public async Task Admin_kiradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await client.GetAsync("/api/admin/billing/invoices/by-plan");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static Student Student(string name) => new()
    {
        FullName = name + " " + Guid.NewGuid().ToString("N")[..6],
        ClassName = "5-A",
        EnrollmentDate = "2026-01-01",
    };
}
