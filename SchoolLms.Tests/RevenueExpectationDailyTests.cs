using System.Net;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// F6.04 — kunlik dinamika (<see cref="FinanceReportQueries.RevenueExpectationDailyAsync"/>,
/// <c>FinanceReportQueries.RevenueExpectationDaily.cs</c>).
///
/// <para>
/// <b>Qabul mezoni — reconciliation.</b> Oyning barcha kunlari bo'yicha
/// <c>Revenue</c>/<c>Expense</c> yig'indisi <see cref="FinanceReportQueries.ProfitLossAsync"/>
/// ning SHU OY uchun qaytargan <c>RevenueTotal</c>/<c>ExpenseTotal</c> bilan
/// AYNAN teng bo'lishi SHART — ikkalasi ham bitta manbadan (<c>ledger_entries</c>)
/// bitta yordamchi (<c>Net</c>) orqali hisoblanadi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class RevenueExpectationDailyTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    private const string DailyUrl = "/api/admin/finance/pnl/expectation/daily";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Kassir_kira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);
        var response = await client.GetAsync(DailyUrl);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();
        var response = await client.GetAsync(DailyUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_kira_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        var response = await client.GetAsync($"{DailyUrl}?month=2026-01");
        Assert.True(response.IsSuccessStatusCode,
            $"{DailyUrl} → {(int)response.StatusCode} {response.StatusCode}");
    }

    // =====================================================================
    //  2. RECONCILIATION — kunlar yig'indisi = ProfitLossAsync(shu oy)
    // =====================================================================

    [Fact]
    public async Task Kunlar_yigindisi_ProfitLossAsync_bilan_kelishadi()
    {
        await using var db = await NewDbAsync("daily");
        var actorId = await SeedUserAsync(db, Roles.Admin);

        var month = new DateOnly(2020, 9, 1);
        var monthEnd = new DateOnly(2020, 9, 30);

        var ledger = new LedgerService(db);

        // 5-sentyabr — daromad.
        var ref1 = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, 1_000_000m,
                LedgerRefType.Invoice, ref1, new DateOnly(2020, 9, 5)),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, 1_000_000m,
                LedgerRefType.Invoice, ref1, new DateOnly(2020, 9, 5)),
        ], actorId);

        // 20-sentyabr — chiqim.
        var ref2 = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.ExpenseSalary, LedgerDirection.Debit, 300_000m,
                LedgerRefType.Salary, ref2, new DateOnly(2020, 9, 20)),
            new LedgerPosting(Accounts.Bank, LedgerDirection.Credit, 300_000m,
                LedgerRefType.Salary, ref2, new DateOnly(2020, 9, 20)),
        ], actorId);

        // Boshqa oy — sentyabrga tushmasligi kerak.
        var ref3 = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, 5_000_000m,
                LedgerRefType.Invoice, ref3, new DateOnly(2020, 10, 2)),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, 5_000_000m,
                LedgerRefType.Invoice, ref3, new DateOnly(2020, 10, 2)),
        ], actorId);

        var queries = new FinanceReportQueries(db);
        var daily = await queries.RevenueExpectationDailyAsync(month);
        var pnl = await queries.ProfitLossAsync(month, monthEnd);

        Assert.Equal(30, daily.Days.Count);
        Assert.Equal(pnl.RevenueTotal, daily.Days.Sum(d => d.Revenue));
        Assert.Equal(pnl.ExpenseTotal, daily.Days.Sum(d => d.Expense));
        Assert.Equal(pnl.Net, daily.Days.Sum(d => d.Net));

        var day5 = daily.Days.Single(d => d.Date == new DateOnly(2020, 9, 5));
        Assert.Equal(1_000_000m, day5.Revenue);
        Assert.Equal(1_000_000m, day5.CumulativeNet);

        var day20 = daily.Days.Single(d => d.Date == new DateOnly(2020, 9, 20));
        Assert.Equal(300_000m, day20.Expense);
        Assert.Equal(700_000m, day20.CumulativeNet); // 1 000 000 − 300 000

        var lastDay = daily.Days[^1];
        Assert.Equal(pnl.Net, lastDay.CumulativeNet);

        Assert.Null(daily.Today); // 2020-09 — bugungidan uzoq o'tmish
    }

    [Fact]
    public async Task Bosh_oyda_hamma_kun_nol()
    {
        await using var db = await NewDbAsync("empty");
        var queries = new FinanceReportQueries(db);

        var daily = await queries.RevenueExpectationDailyAsync(new DateOnly(2030, 2, 1));

        Assert.Equal(28, daily.Days.Count); // 2030 — kabisa yil emas (2030 / 4 butun emas)
        Assert.All(daily.Days, d => Assert.Equal(0m, d.Revenue));
        Assert.All(daily.Days, d => Assert.Equal(0m, d.Expense));
        Assert.All(daily.Days, d => Assert.Equal(0m, d.CumulativeNet));
    }

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("daily_" + prefix);
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
}
