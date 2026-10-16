using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Typed cash-box Kirim/Chiqim reach the ledger, so P&amp;L shows them (client, 2026-10-08: P&amp;L said
/// "Yillik xarajat 0 so'm" while 832 mln of EduSchool expenses sat in the cash box —
/// "eduschool bilan moliya bo'limlari funksionalliklari bir xil ishlashi kerak").
/// Own database: P&amp;L sums the whole journal, neighbours' rows would shift it.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CashBoxLedgerSyncTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Turi_bor_chiqim_PL_da_xarajat_bolib_chiqadi()
    {
        await using var db = await NewDbAsync("payout");
        var (box, actor) = await SeedAsync(db);
        var rent = await TypeAsync(db, TransactionTypeKind.Out, "Бино ижараси", Accounts.ExpenseRent);
        var day = AppClock.Today.AddDays(-2);

        await new CashBoxService(db).PayOutAsync(box,
            new CashBoxPayOutRequest(47_200_000m, PaymentMethod.Cash, "sentyabr", rent.Id, day), actor);

        var pl = await new FinanceReportQueries(db).ProfitLossAsync(day, day);
        Assert.Equal(47_200_000m, pl.ExpenseTotal);
        Assert.Equal(47_200_000m, pl.Expense.Single(l => l.Account == Accounts.ExpenseRent).Amount);

        var lines = await db.LedgerEntries.AsNoTracking().Where(e => e.RefType == LedgerRefType.CashBoxTransaction).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(day, l.EntryDate));   // the cash box row's own day
        Assert.Contains(lines, l => l.Account == Accounts.Cash && l.Direction == LedgerDirection.Credit);
    }

    [Fact]
    public async Task Turi_bor_kirim_daromad_bank_orqali()
    {
        await using var db = await NewDbAsync("payin");
        var (box, actor) = await SeedAsync(db);
        var books = await TypeAsync(db, TransactionTypeKind.In, "Mock imtihon uchun", Accounts.RevenueOther);

        await new CashBoxService(db).PayInAsync(box,
            new CashBoxPayInRequest(300_000m, PaymentMethod.Card, null, null, books.Id), actor);

        var lines = await db.LedgerEntries.AsNoTracking().Where(e => e.RefType == LedgerRefType.CashBoxTransaction).ToListAsync();
        Assert.Contains(lines, l => l.Account == Accounts.Bank && l.Direction == LedgerDirection.Debit && l.Amount == 300_000m);
        Assert.Contains(lines, l => l.Account == Accounts.RevenueOther && l.Direction == LedgerDirection.Credit);
    }

    [Fact]
    public async Task Turi_yoq_chiqim_jurnalga_tushmaydi()
    {
        // EduSchool's untyped outflows moved terminal/Click money to the bank — not spent.
        await using var db = await NewDbAsync("untyped");
        var (box, actor) = await SeedAsync(db);

        await new CashBoxService(db).PayOutAsync(box, new CashBoxPayOutRequest(79_676_000m, PaymentMethod.Card), actor);

        Assert.False(await db.LedgerEntries.AnyAsync());
    }

    [Fact]
    public async Task Bekor_qilingan_chiqim_jurnalda_ham_teskari_bolib_nolga_tushadi()
    {
        await using var db = await NewDbAsync("cancel");
        var (box, actor) = await SeedAsync(db);
        var other = await TypeAsync(db, TransactionTypeKind.Out, "Бошка харажатлар", Accounts.ExpenseOther);
        var service = new CashBoxService(db);

        var row = await service.PayOutAsync(box, new CashBoxPayOutRequest(250_000m, PaymentMethod.Cash, null, other.Id), actor);
        // The same person cancels — the cash box allows it, so the journal must follow, not refuse.
        await service.CancelTransactionAsync(row.Id, "xato kiritildi", actor);

        var pl = await new FinanceReportQueries(db).ProfitLossAsync(AppClock.Today, AppClock.Today);
        Assert.Equal(0m, pl.ExpenseTotal);
        Assert.Equal(2, await db.LedgerEntries.CountAsync(e => e.RefType == LedgerRefType.Reversal));
    }

    [Fact]
    public async Task Eski_qatorlar_bir_marta_jurnalga_otkaziladi()
    {
        await using var db = await NewDbAsync("backfill");
        var (box, actor) = await SeedAsync(db);
        // Imported EduSchool types come without an account — the sync guesses it from the name.
        var salary = await TypeAsync(db, TransactionTypeKind.Out, "Hodimga oylik", null);
        var loan = await TypeAsync(db, TransactionTypeKind.Out, "Кредитлар  (ишчилар иш хаки ушлаб утказилади)", null);
        var mine = await TypeAsync(db, TransactionTypeKind.Out, "Bank xizmati", Accounts.ExpenseRepair);   // admin's pick

        var day = new DateOnly(2026, 9, 30);
        var at = AppClock.InstantOn(day);
        CashBoxTransaction Out(decimal amount, Guid? type) => new()
        {
            CashBoxId = box, Kind = CashBoxTransactionKind.PayOut, Amount = amount, Method = PaymentMethod.Cash,
            Status = CashBoxTransactionStatus.Posted, CreatedBy = actor, CreatedAt = at, TransactionTypeId = type,
        };
        var paid = Out(5_000_000m, salary.Id);
        var cancelled = Out(1_500_000m, salary.Id);
        db.CashBoxTransactions.AddRange(paid, cancelled, Out(5_784_000m, loan.Id), Out(1_000m, mine.Id),
            Out(27_000_000m, null),
            new CashBoxTransaction
            {
                CashBoxId = box, Kind = CashBoxTransactionKind.PayOut, Amount = 1_500_000m, Method = PaymentMethod.Cash,
                Status = CashBoxTransactionStatus.Reversal, CreatedBy = actor, CreatedAt = at,
                TransactionTypeId = salary.Id, ReversalOf = cancelled.Id,
            });
        await db.SaveChangesAsync();

        var (types, batches) = await CashBoxLedgerSync.RunAsync(db);

        Assert.True(types >= 2);     // salary and loan (plus the migration's seeded types)
        var accounts = await db.TransactionTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.LedgerAccount);
        Assert.Equal(Accounts.ExpenseSalary, accounts[salary.Id]);
        Assert.Equal(Accounts.ExpenseOther, accounts[loan.Id]);     // a loan, not salary, despite "иш хаки"
        Assert.Equal(Accounts.ExpenseRepair, accounts[mine.Id]);    // the admin's choice stays
        Assert.Equal(3, batches);    // paid, loan, bank fee — not the cancelled one, not the untyped one
        var pl = await new FinanceReportQueries(db).ProfitLossAsync(day, day);
        Assert.Equal(5_000_000m, pl.Expense.Single(l => l.Account == Accounts.ExpenseSalary).Amount);
        Assert.Equal(5_784_000m, pl.Expense.Single(l => l.Account == Accounts.ExpenseOther).Amount);
        Assert.Equal(1_000m, pl.Expense.Single(l => l.Account == Accounts.ExpenseRepair).Amount);
        Assert.Equal(10_785_000m, pl.ExpenseTotal);

        Assert.Equal((0, 0), await CashBoxLedgerSync.RunAsync(db));   // idempotent
    }

    [Fact]
    public async Task Turga_boshqa_turkumning_hisobi_berilmaydi()
    {
        await using var db = await NewDbAsync("validate");
        var (_, actor) = await SeedAsync(db);
        var service = new TransactionTypeService(db, new AuditService(db, new HttpContextAccessor()));

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => service.CreateAsync(
            new CreateTransactionTypeRequest(TransactionTypeKind.Out, "Ijara", LedgerAccount: Accounts.RevenueOther), actor));
        Assert.Equal("invalid_ledger_account", ex.Code);

        var guessed = await service.CreateAsync(new CreateTransactionTypeRequest(TransactionTypeKind.Out, "Автобус харажати"), actor);
        Assert.Equal(Accounts.ExpenseTransport, guessed.LedgerAccount);
    }

    [Theory]
    [InlineData("Hodimga avans", Accounts.ExpenseSalary)]
    [InlineData("Ish haqi", Accounts.ExpenseSalary)]
    [InlineData("oshhona uchun", Accounts.ExpenseFood)]
    [InlineData("Ётоқхона харажати", Accounts.ExpenseDormitory)]
    [InlineData("Маркент хизмати", Accounts.ExpenseMarketing)]
    [InlineData("Daromad solig’i uchun (12%)", Accounts.ExpenseTaxes)]
    [InlineData("INPS (0,1%)", Accounts.ExpenseTaxes)]
    [InlineData("Эlektir energiya", Accounts.ExpenseUtilities)]
    [InlineData("Ichimlik suv", Accounts.ExpenseUtilities)]
    [InlineData("Maxsulot etkazib beruvchilar", Accounts.ExpenseSupplies)]
    [InlineData("Qo’riqlash xizmati", Accounts.ExpenseOther)]
    [InlineData("Kanselyariya xarajati", Accounts.ExpenseSupplies)]
    public void Chiqim_turi_nomidan_hisob_taxmin_qilinadi(string name, string account) =>
        Assert.Equal(account, Accounts.GuessForTransactionType(TransactionTypeKind.Out, name));

    [Theory]
    [InlineData("O'quvchi oylik to'lovi", Accounts.RevenueTuition)]
    [InlineData("Transport 5-10sinflar", Accounts.RevenueBus)]
    // Shop goods (2026-10-15, docs/modules/shop.md) — a word starting with kitob/forma/do'ppi/kiyim.
    [InlineData("Do'ppi uchun", Accounts.RevenueSales)]
    [InlineData("Kitob uchun to'lov", Accounts.RevenueSales)]
    [InlineData("Maktab formasi", Accounts.RevenueSales)]
    [InlineData("Informatika to'garagi", Accounts.RevenueOther)]
    [InlineData("Mock uchun to'lov", Accounts.RevenueOther)]
    public void Kirim_turi_nomidan_hisob_taxmin_qilinadi(string name, string account) =>
        Assert.Equal(account, Accounts.GuessForTransactionType(TransactionTypeKind.In, name));

    // =====================================================================
    //  Helpers
    // =====================================================================

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("cash_ledger_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<(Guid Box, string Actor)> SeedAsync(AppDbContext db)
    {
        var user = new AppUser { FullName = "Kassir", Role = Roles.Admin, Email = $"kassa.{Guid.NewGuid():N}" };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        var box = new CashBox { Name = "Kushakov Turdiali " + Guid.NewGuid().ToString("N")[..6], IsActive = true, CreatedAt = AppClock.NowInstant };
        db.CashBoxes.Add(box);
        await db.SaveChangesAsync();
        return (box.Id, user.Id);
    }

    private static async Task<TransactionType> TypeAsync(AppDbContext db, string kind, string name, string? account)
    {
        var type = new TransactionType { Kind = kind, Name = name, LedgerAccount = account, CreatedAt = AppClock.NowInstant };
        db.TransactionTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }
}
