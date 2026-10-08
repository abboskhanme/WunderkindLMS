using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Cash-box money in the two places that did not see it (client, 2026-10-09: "eduschool bilan moliya
/// bo'limlari funksionalliklari bir xil ishlashi kerak"): the "Tranzaksiyalar" journal, and the salary
/// reports — EduSchool pays "Hodimga oylik / avans" from the cash box.
/// Own database: the journal's totals cover every row of the filter.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CashBoxJournalAndSalaryTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  Salary paid from the cash box
    // =====================================================================

    [Fact]
    public async Task Kassadan_berilgan_oylik_maosh_hisobotida_shu_oqituvchiga_yoziladi()
    {
        await using var db = await NewDbAsync("salary");
        var s = await SeedAsync(db);
        var service = new CashBoxService(db);
        var day = AppClock.Today.AddDays(-1);

        var paid = await service.PayOutAsync(s.Box,
            new CashBoxPayOutRequest(5_000_000m, PaymentMethod.Cash, "sentyabr oyligi", s.Salary, day, TeacherId: s.Teacher), s.Actor);
        await service.PayOutAsync(s.Box,
            new CashBoxPayOutRequest(1_500_000m, PaymentMethod.Cash, "avans", s.Salary, EmployeeUserId: s.Employee), s.Actor);

        var teacher = Assert.Single(await new SalaryPaymentQuery(db).ForTeacherAsync(s.Teacher));
        Assert.Equal((5_000_000m, day, day.ToString("yyyy-MM")), (teacher.Amount, teacher.OnDate, teacher.Month));
        Assert.Equal("Xasanov Erkinjon", paid.RecipientName);

        var employee = Assert.Single(await new SalaryPaymentQuery(db).ForAllEmployeesAsync());
        Assert.Equal((s.Employee, 1_500_000m), (employee.TeacherId, employee.Amount));

        // A cancelled payout is money taken back — the report must not keep it as paid.
        await service.CancelTransactionAsync(paid.Id, "xato", s.Actor);
        Assert.Empty(await new SalaryPaymentQuery(db).ForTeacherAsync(s.Teacher));
    }

    [Fact]
    public async Task Xodim_faqat_maosh_turida_va_bittasi_korsatiladi()
    {
        await using var db = await NewDbAsync("recipient");
        var s = await SeedAsync(db);
        var service = new CashBoxService(db);

        var notSalary = await Assert.ThrowsAsync<BillingRuleException>(() => service.PayOutAsync(s.Box,
            new CashBoxPayOutRequest(100_000m, PaymentMethod.Cash, null, s.Rent, TeacherId: s.Teacher), s.Actor));
        Assert.Equal("recipient_needs_salary_type", notSalary.Code);

        var both = await Assert.ThrowsAsync<BillingRuleException>(() => service.PayOutAsync(s.Box,
            new CashBoxPayOutRequest(100_000m, PaymentMethod.Cash, null, s.Salary,
                TeacherId: s.Teacher, EmployeeUserId: s.Employee), s.Actor));
        Assert.Equal("one_salary_recipient", both.Code);

        var unknown = await Assert.ThrowsAsync<BillingRuleException>(() => service.PayOutAsync(s.Box,
            new CashBoxPayOutRequest(100_000m, PaymentMethod.Cash, null, s.Salary, TeacherId: "yo'q"), s.Actor));
        Assert.Equal("teacher_not_found", unknown.Code);

        Assert.False(await db.CashBoxTransactions.AnyAsync());   // nothing was written
    }

    // =====================================================================
    //  "Tranzaksiyalar" journal
    // =====================================================================

    [Fact]
    public async Task Jurnalda_kassa_kirimi_chiqimi_va_otkazmasi_bor()
    {
        await using var db = await NewDbAsync("journal");
        var s = await SeedAsync(db);
        var service = new CashBoxService(db);

        await service.PayInAsync(s.Box, new CashBoxPayInRequest(390_000m, PaymentMethod.Cash, "kitoblar", null, s.Books), s.Actor);
        await service.PayOutAsync(s.Box, new CashBoxPayOutRequest(47_200_000m, PaymentMethod.Cash, "sentyabr", s.Rent), s.Actor);
        await service.PayOutAsync(s.Box,
            new CashBoxPayOutRequest(5_000_000m, PaymentMethod.Cash, null, s.Salary, TeacherId: s.Teacher), s.Actor);
        await service.TransferAsync(s.Box, new CashBoxTransferRequest(s.OtherBox, 1_000_000m, PaymentMethod.Cash, "buxgalterga"), s.Actor);

        var page = await new TransactionJournalQuery(db).PageAsync(new TransactionJournalFilter());

        Assert.Equal(4, page.Total);
        // The transfer stays inside the school: listed, never counted.
        Assert.Equal((390_000m, 52_200_000m, -51_810_000m), (page.Totals.TotalIn, page.Totals.TotalOut, page.Totals.Net));

        var rent = page.Rows.Single(r => r.Amount == -47_200_000m);
        Assert.Equal((TransactionKind.CashOut, TransactionDirection.Out, "rent", "Бино ижараси", Accounts.ExpenseRent),
            (rent.Kind, rent.Direction, rent.Category, rent.CategoryLabel, rent.Account));
        Assert.Equal("Kushakov", rent.CashBoxName);

        var salary = page.Rows.Single(r => r.Amount == -5_000_000m);
        Assert.Equal(("Xasanov Erkinjon", "teacher"), (salary.PersonName, salary.PersonKind));

        var transfer = page.Rows.Single(r => r.Kind == TransactionKind.CashTransfer);
        Assert.Equal((TransactionDirection.Internal, 1_000_000m, 0m, "Kushakov → Mirzoulug'bek"),
            (transfer.Direction, transfer.Amount, transfer.SettledAmount, transfer.CashBoxName));
    }

    [Fact]
    public async Task Jurnal_filtrlari_kassa_qatorlariga_ham_amal_qiladi()
    {
        await using var db = await NewDbAsync("filters");
        var s = await SeedAsync(db);
        var service = new CashBoxService(db);
        var query = new TransactionJournalQuery(db);

        await service.PayInAsync(s.Box, new CashBoxPayInRequest(390_000m, PaymentMethod.Card, null, null, s.Books), s.Actor);
        var rent = await service.PayOutAsync(s.Box, new CashBoxPayOutRequest(2_000_000m, PaymentMethod.Cash, null, s.Rent), s.Actor);
        await service.TransferAsync(s.Box, new CashBoxTransferRequest(s.OtherBox, 1_000_000m, PaymentMethod.Cash), s.Actor);
        await service.CancelTransactionAsync(rent.Id, "ikki marta kiritilgan", s.Actor);

        async Task<IReadOnlyList<TransactionRowDto>> Rows(TransactionJournalFilter f) => (await query.PageAsync(f)).Rows;

        Assert.Single(await Rows(new TransactionJournalFilter(Kind: TransactionKind.CashIn)));
        Assert.Single(await Rows(new TransactionJournalFilter(Direction: TransactionDirection.Internal)));
        Assert.Single(await Rows(new TransactionJournalFilter(Method: PaymentMethod.Card)));
        Assert.Equal(2, (await Rows(new TransactionJournalFilter(Category: "rent"))).Count);   // the row and its cancel
        Assert.Empty(await Rows(new TransactionJournalFilter(Kind: TransactionKind.Payment)));
        Assert.Empty(await Rows(new TransactionJournalFilter(ReceiptNo: 1)));

        // The cancel is its own row, like a payment storno: money came back, so it is a Kirim.
        var cancelled = Assert.Single(await Rows(new TransactionJournalFilter(Status: TransactionStatus.Reversed)));
        Assert.Equal((-2_000_000m, "ikki marta kiritilgan"), (cancelled.Amount, cancelled.CancelReason));
        var back = (await Rows(new TransactionJournalFilter(Direction: TransactionDirection.In)))
            .Single(r => r.ReversalOf == rent.Id);
        Assert.Equal((2_000_000m, TransactionKind.CashOut), (back.Amount, back.Kind));

        // Cancelled pair nets to zero; the Kirim stays.
        var totals = (await query.PageAsync(new TransactionJournalFilter())).Totals;
        Assert.Equal(390_000m, totals.Net);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private sealed record Seed(
        Guid Box, Guid OtherBox, string Actor, string Teacher, string Employee, Guid Salary, Guid Rent, Guid Books);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("cash_journal_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<Seed> SeedAsync(AppDbContext db)
    {
        AppUser User(string name, string role)
        {
            var u = new AppUser { FullName = name, Role = role, Email = $"u.{Guid.NewGuid():N}" };
            u.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
            return u;
        }
        var actor = User("Kushakov Turdiali", Roles.Admin);
        var employee = User("Karimova Ulhanoy", Roles.Staff);
        var teacher = new Teacher { FullName = "Xasanov Erkinjon" };
        var box = new CashBox { Name = "Kushakov", IsActive = true, CreatedAt = AppClock.NowInstant };
        var other = new CashBox { Name = "Mirzoulug'bek", IsActive = true, CreatedAt = AppClock.NowInstant };
        TransactionType Type(string kind, string name, string account) =>
            new() { Kind = kind, Name = name, LedgerAccount = account, CreatedAt = AppClock.NowInstant };
        var salary = Type(TransactionTypeKind.Out, "Hodimga oylik", Accounts.ExpenseSalary);
        var rent = Type(TransactionTypeKind.Out, "Бино ижараси", Accounts.ExpenseRent);
        var books = Type(TransactionTypeKind.In, "Elementary kitoblar", Accounts.RevenueOther);

        db.Users.AddRange(actor, employee);
        db.Teachers.Add(teacher);
        db.CashBoxes.AddRange(box, other);
        db.TransactionTypes.AddRange(salary, rent, books);
        await db.SaveChangesAsync();
        return new Seed(box.Id, other.Id, actor.Id, teacher.Id, employee.Id, salary.Id, rent.Id, books.Id);
    }
}
