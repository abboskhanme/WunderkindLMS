using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// F6.05 — yillik reja/fakt jadvali (<see cref="FinanceReportQueries.RevenueExpectationYearlyAsync"/>,
/// <c>FinanceReportQueries.RevenueExpectationYearly.cs</c>).
///
/// <para>
/// <b>Qabul mezoni — reconciliation "bepul".</b> Har oy
/// <see cref="FinanceReportQueries.RevenueExpectationAsync"/> ning O'ZINI
/// chaqiradi, shuning uchun ikkinchi ta'rif paydo bo'lish IMKONI yo'q — test
/// buni "chaqiruv natijasi qatorga AYNAN shunday tushadi" darajasida
/// tekshiradi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class RevenueExpectationYearlyTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    private const string YearlyUrl = "/api/admin/finance/pnl/expectation/yearly";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Kassir_kira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);
        var response = await client.GetAsync(YearlyUrl);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();
        var response = await client.GetAsync(YearlyUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_kira_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        var response = await client.GetAsync($"{YearlyUrl}?year=2026");
        Assert.True(response.IsSuccessStatusCode,
            $"{YearlyUrl} → {(int)response.StatusCode} {response.StatusCode}");
    }

    [Fact]
    public async Task Yaroqsiz_yil_400()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await client.GetAsync($"{YearlyUrl}?year=1500");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =====================================================================
    //  2. RECONCILIATION — har oy = RevenueExpectationAsync(shu oy)
    // =====================================================================

    [Fact]
    public async Task Har_oy_RevenueExpectationAsync_bilan_aynan_mos_va_engyaxshi_yomon_ogri()
    {
        await using var db = await NewDbAsync("yearly");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var tuition = await CategoryIdAsync(db, "tuition");

        db.Students.Add(NewStudent("stu-y1", "Yillik Y1", "5-A"));
        await db.SaveChangesAsync();

        // Faol obuna — butun yil davomida.
        db.StudentSubscriptions.Add(new StudentSubscription
        {
            StudentId = "stu-y1",
            CategoryId = tuition,
            MonthlyAmount = 500_000m,
            StartsOn = new DateOnly(2019, 1, 1),
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();

        var ledger = new LedgerService(db);
        // Martda daromad tan olinadi (eng katta oy — "eng yaxshi").
        var refMarch = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, 500_000m,
                LedgerRefType.Invoice, refMarch, new DateOnly(2019, 3, 5)),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, 500_000m,
                LedgerRefType.Invoice, refMarch, new DateOnly(2019, 3, 5)),
        ], actorId);
        // Iyulda chiqim (foyda manfiy — "eng yomon").
        var refJuly = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.ExpenseRent, LedgerDirection.Debit, 200_000m,
                LedgerRefType.Expense, refJuly, new DateOnly(2019, 7, 10)),
            new LedgerPosting(Accounts.Bank, LedgerDirection.Credit, 200_000m,
                LedgerRefType.Expense, refJuly, new DateOnly(2019, 7, 10)),
        ], actorId);

        var queries = new FinanceReportQueries(db);
        var yearly = await queries.RevenueExpectationYearlyAsync(2019);

        Assert.Equal(2019, yearly.Year);
        Assert.Equal(12, yearly.Months.Count);

        // Har bir oy AYNAN RevenueExpectationAsync bilan mos — reconciliation.
        foreach (var monthRow in yearly.Months)
        {
            var expectation = await queries.RevenueExpectationAsync(monthRow.Month);
            Assert.Equal(expectation.GrossExpected, monthRow.GrossPlan);
            Assert.Equal(expectation.DiscountAmount, monthRow.DiscountAmount);
            Assert.Equal(expectation.NetExpected, monthRow.NetPlan);
            Assert.Equal(expectation.RevenueActual, monthRow.RevenueActual);
            Assert.Equal(expectation.ExpenseActual, monthRow.ExpenseActual);
            Assert.Equal(expectation.ProfitActual, monthRow.ProfitActual);
        }

        Assert.Equal(yearly.Months.Sum(m => m.NetPlan), yearly.NetPlanTotal);
        Assert.Equal(yearly.Months.Sum(m => m.RevenueActual), yearly.RevenueActualTotal);
        Assert.Equal(yearly.Months.Sum(m => m.ExpenseActual), yearly.ExpenseActualTotal);
        Assert.Equal(yearly.Months.Sum(m => m.ProfitActual), yearly.ProfitActualTotal);

        Assert.Equal(new DateOnly(2019, 3, 1), yearly.BestMonth);
        Assert.Equal(new DateOnly(2019, 7, 1), yearly.WorstMonth);
    }

    [Fact]
    public async Task Harakatsiz_yilda_engyaxshi_yomon_null()
    {
        await using var db = await NewDbAsync("noactivity");
        var queries = new FinanceReportQueries(db);

        var yearly = await queries.RevenueExpectationYearlyAsync(2031);

        Assert.Equal(12, yearly.Months.Count);
        Assert.Null(yearly.BestMonth);
        Assert.Null(yearly.WorstMonth);
        Assert.Equal(0m, yearly.NetPlanTotal);
        Assert.Equal(0m, yearly.ProfitActualTotal);
    }

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("yearly_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<string> SeedUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser
        {
            FullName = $"Test {role}",
            Role = role,
            Email = $"{role}.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> CategoryIdAsync(AppDbContext db, string code) =>
        await db.FeeCategories.Where(c => c.Code == code).Select(c => c.Id).SingleAsync();

    private static Student NewStudent(string id, string fullName, string className) => new()
    {
        Id = id,
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2015-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = className,
        EnrollmentDate = "2018-09-01",
    };
}
