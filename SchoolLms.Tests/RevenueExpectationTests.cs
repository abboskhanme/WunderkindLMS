using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// P&amp;L 2.0 (§2.6, <c>FINANCE_ALL.PNL_EXPECTATION</c>, beta) — bir oy
/// uchun reja/fakt/farq (<see cref="FinanceReportQueries.RevenueExpectationAsync"/>,
/// <c>FinanceReportQueries.RevenueExpectation.cs</c>).
///
/// <para>
/// <b>Asosiy mezon — qabul qilingan vazifaning o'zidan.</b> "Fakt" tomonidagi
/// daromad/chiqim/sof natija eski P&amp;L (<c>ProfitLossAsync</c>, §2.5) bilan
/// AYNAN kelishishi SHART, chunki ikkovi bitta funksiyani chaqiradi.
/// <see cref="Reja_fakt_farq_arifmetikasi"/> buni bitta oyni IKKI YO'L bilan
/// hisoblab (yangi so'rov va eski <c>ProfitLossAsync</c>) tekshiradi — shakl
/// testi emas, arifmetika testi.
/// </para>
/// <para>
/// Testlar ikki guruhda (<c>FinanceReportsTests</c> dagi sabab bilan): RUXSAT
/// — umumiy bazada HTTP orqali; ARIFMETIKA — o'zining toza bazasida, chunki
/// hisobot butun oyni yig'adi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class RevenueExpectationTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Shu klass yaratgan bazalarning ulanish satrlari — test tugagach
    /// hovuzlari yopiladi (<c>FinanceReportsTests</c> dagi izoh bilan bir
    /// xil sabab: tozalanmasa <c>max_connections</c> tugaydi).
    /// </summary>
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));

        return Task.CompletedTask;
    }

    private const string PnlExpectation = "/api/admin/finance/pnl/expectation";

    // =====================================================================
    //  1. RUXSAT — SPEC §4.3, ViewBillingReports bilan bir xil darvoza
    // =====================================================================

    /// <summary>Kassir — 403 (§4.3, "See variance report across cashiers — ⛔").</summary>
    [Fact]
    public async Task Kassir_kira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        var response = await client.GetAsync(PnlExpectation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Xodim va o'qituvchi ham yopiq — §4.3 da ular uchun ustun yo'q.</summary>
    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    public async Task Xodim_va_oqituvchi_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "finance");

        var response = await client.GetAsync(PnlExpectation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Token'siz so'rov — 401.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync(PnlExpectation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_kira_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        var response = await client.GetAsync($"{PnlExpectation}?month=2026-01");

        Assert.True(response.IsSuccessStatusCode,
            $"{PnlExpectation} → {(int)response.StatusCode} {response.StatusCode}");
    }

    /// <summary>Noto'g'ri oy formati — 400, 500 emas.</summary>
    [Fact]
    public async Task Notogri_oy_formati_400()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{PnlExpectation}?month=2026-13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =====================================================================
    //  2. ARIFMETIKA — reja, fakt, va eski P&L bilan solishtirish
    // =====================================================================

    /// <summary>
    /// <b>Qabul mezoni.</b> Bitta oy uchun: reja (hisob-fakturalar va
    /// obunalardan), fakt-pul (taqsimotlar — storno chiqarib tashlangan),
    /// o'quvchi harakati (obunalar boshi/oxiri) va jurnal fakti
    /// (<c>ProfitLossAsync</c> bilan AYNAN mos).
    /// </summary>
    [Fact]
    public async Task Reja_fakt_farq_arifmetikasi()
    {
        await using var db = await NewDbAsync("core");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);
        var tuition = await CategoryIdAsync(db, "tuition");

        var march = new DateOnly(2019, 3, 1);
        var april = new DateOnly(2019, 4, 1);

        var studentA = Guid.NewGuid().ToString();
        var studentB = Guid.NewGuid().ToString();
        var studentC = Guid.NewGuid().ToString();   // faqat obunada — hisob-fakturasiz
        var studentD = Guid.NewGuid().ToString();   // apreldan boshlanadigan obuna — martda YO'Q
        db.Students.AddRange(
            NewStudent(studentA, "Reja A", "5-A"),
            NewStudent(studentB, "Reja B", "5-A"),
            NewStudent(studentC, "Reja C", "5-B"),
            NewStudent(studentD, "Reja D", "5-B"));

        // ---- Reja: martning hisob-fakturalari ----
        var invA = NewInvoice(studentA, tuition, march, 1_000_000m, discount: 100_000m);
        var invB = NewInvoice(studentB, tuition, march, 500_000m);
        // Bekor qilingan (void) — hisobga UMUMAN kirmasligi kerak. Boshqa
        // o'quvchida (studentA'da ustiga qo'yilsa unikal indeksga uriladi:
        // bitta o'quvchi × toifa × oy uchun bitta hisob-faktura).
        var invVoid = NewInvoice(studentC, tuition, march, 9_999_999m);
        invVoid.Status = InvoiceStatus.Void;
        // Boshqa OY — martga tushmasligi kerak.
        var invApril = NewInvoice(studentA, tuition, april, 700_000m);
        db.Invoices.AddRange(invA, invB, invVoid, invApril);
        await db.SaveChangesAsync();

        // ---- Fakt-pul: A to'liq to'laydi, B qisman ----
        await PayAsync(db, studentA, cashierId, shiftId, 900_000m, [(invA.Id, 900_000m)]);
        await PayAsync(db, studentB, cashierId, shiftId, 200_000m, [(invB.Id, 200_000m)]);

        // Storno qilingan to'lov — na o'zi, na bekor qilingani hisobga
        // kirmasligi kerak (EffectiveAllocations qoidasi, 1.0 bilan bir xil).
        var reversed = await PayAsync(db, studentB, cashierId, shiftId, 150_000m, [(invB.Id, 150_000m)]);
        await PayAsync(db, studentB, cashierId, shiftId, 150_000m, [(invB.Id, 150_000m)], reversalOf: reversed);

        // ---- O'quvchi harakati: obunalar ----
        await AddSubscriptionAsync(db, studentA, tuition, 1_000_000m, new DateOnly(2019, 1, 1), actorId);
        await AddSubscriptionAsync(db, studentB, tuition, 500_000m, new DateOnly(2019, 3, 10), actorId);
        await AddSubscriptionAsync(
            db, studentC, tuition, 400_000m, new DateOnly(2019, 1, 1), actorId, endsOn: new DateOnly(2019, 3, 15));
        // Apreldan boshlanadi — martda faol EMAS.
        await AddSubscriptionAsync(db, studentD, tuition, 300_000m, new DateOnly(2019, 4, 1), actorId);

        // ---- Fakt: jurnal (invoyslardan MUSTAQIL raqamlar — ikki manba aralashmasin) ----
        // Bitta partiyaning IKKI oyog'i BIR XIL RefId ga ega bo'lishi shart
        // (LedgerService qoidasi) — shuning uchun refId oldindan olinadi.
        var ledger = new LedgerService(db);
        var refRevenue = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, 1_000_000m,
                LedgerRefType.Invoice, refRevenue, new DateOnly(2019, 3, 5)),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, 1_000_000m,
                LedgerRefType.Invoice, refRevenue, new DateOnly(2019, 3, 5)),
        ], actorId);
        var refExpense = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.ExpenseSalary, LedgerDirection.Debit, 300_000m,
                LedgerRefType.Salary, refExpense, new DateOnly(2019, 3, 10)),
            new LedgerPosting(Accounts.Bank, LedgerDirection.Credit, 300_000m,
                LedgerRefType.Salary, refExpense, new DateOnly(2019, 3, 10)),
        ], actorId);
        // Boshqa OY — martga tushmasligi kerak (ProfitLossAsync ham shu bilan tekshiriladi).
        var refOtherMonth = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, 5_000_000m,
                LedgerRefType.Invoice, refOtherMonth, new DateOnly(2019, 4, 2)),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, 5_000_000m,
                LedgerRefType.Invoice, refOtherMonth, new DateOnly(2019, 4, 2)),
        ], actorId);

        var queries = new FinanceReportQueries(db);

        // Kunning o'zi ahamiyatsiz — faqat oy va yil.
        var expectation = await queries.RevenueExpectationAsync(new DateOnly(2019, 3, 15));

        Assert.Equal(march, expectation.Month);

        // ---- Reja ----
        Assert.Equal(1_500_000m, expectation.GrossExpected);   // 1 000 000 + 500 000 (void va aprel YO'Q)
        Assert.Equal(100_000m, expectation.DiscountAmount);
        Assert.Equal(1_400_000m, expectation.NetExpected);
        Assert.Equal(decimal.Round(100_000m / 1_500_000m * 100m, 2), expectation.DiscountRate);
        Assert.Equal(2, expectation.StudentsExpected);
        Assert.Equal(1, expectation.StudentsPaid);             // faqat A to'liq to'lagan
        Assert.Equal(decimal.Round(1_400_000m / 2, 2), expectation.PerStudentNet);

        // ---- Fakt-pul ----
        Assert.Equal(1_100_000m, expectation.CollectedForPeriod);   // 900 000 + 200 000 (storno YO'Q)
        Assert.Equal(300_000m, expectation.OutstandingForPeriod);   // 1 400 000 − 1 100 000
        Assert.Equal(decimal.Round(1_100_000m / 1_400_000m * 100m, 2), expectation.CollectionRateForPeriod);

        // ---- O'quvchi harakati ----
        Assert.Equal(3, expectation.StudentsActive);      // A, B, C (D — apreldan)
        Assert.Equal(1, expectation.StudentsAdmitted);     // B (10-mart boshlagan)
        Assert.Equal(1, expectation.StudentsDeparted);     // C (15-mart tugagan)

        // ---- Fakt — ESKI P&L bilan AYNAN mos (qabul mezoni) ----
        var pnl = await queries.ProfitLossAsync(march, new DateOnly(2019, 3, 31));
        Assert.Equal(pnl.RevenueTotal, expectation.RevenueActual);
        Assert.Equal(pnl.ExpenseTotal, expectation.ExpenseActual);
        Assert.Equal(pnl.Net, expectation.ProfitActual);

        Assert.Equal(1_000_000m, expectation.RevenueActual);
        Assert.Equal(300_000m, expectation.ExpenseActual);
        Assert.Equal(700_000m, expectation.ProfitActual);
        Assert.Equal(decimal.Round(700_000m / 1_000_000m * 100m, 2), expectation.Margin);
        Assert.Equal(decimal.Round(700_000m / 3, 2), expectation.ProfitPerStudent);

        // ---- Farq: fakt daromad − reja sof daromad ----
        Assert.Equal(pnl.RevenueTotal - expectation.NetExpected, expectation.RevenueDiff);
        Assert.Equal(-400_000m, expectation.RevenueDiff);   // 1 000 000 − 1 400 000
    }

    /// <summary>
    /// Bo'sh oy — bitta ham hisob-faktura, obuna yoki jurnal yozuvi yo'q.
    /// Nolga bo'linish YO'Q: foiz va o'rtacha maydonlar <c>null</c> bo'ladi,
    /// summalar esa 0 (shakli buzilmaydi, xatolik ham chiqmaydi).
    /// </summary>
    [Fact]
    public async Task Bosh_oyda_nolga_bolinish_yoq()
    {
        await using var db = await NewDbAsync("empty");
        var queries = new FinanceReportQueries(db);

        var expectation = await queries.RevenueExpectationAsync(new DateOnly(2030, 1, 1));

        Assert.Equal(0, expectation.StudentsExpected);
        Assert.Equal(0m, expectation.GrossExpected);
        Assert.Equal(0m, expectation.NetExpected);
        Assert.Null(expectation.DiscountRate);
        Assert.Null(expectation.PerStudentNet);
        Assert.Null(expectation.CollectionRateForPeriod);
        Assert.Null(expectation.Margin);
        Assert.Null(expectation.ProfitPerStudent);
        Assert.Equal(0m, expectation.RevenueActual);
        Assert.Equal(0m, expectation.ExpenseActual);
        Assert.Equal(0m, expectation.RevenueDiff);
    }

    // =====================================================================
    //  Test ma'lumoti
    // =====================================================================

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("revexp_" + prefix);
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

    /// <summary>Kassir va uning ochiq smenasi — to'lov yozish uchun ikkovi ham SHART (SPEC §4.2).</summary>
    private static async Task<(string CashierId, Guid ShiftId)> SeedCashDeskAsync(AppDbContext db)
    {
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.Add(shift);
        await db.SaveChangesAsync();
        return (cashierId, shift.Id);
    }

    private static async Task<Guid> CategoryIdAsync(AppDbContext db, string code) =>
        await db.FeeCategories.AsNoTracking().Where(c => c.Code == code).Select(c => c.Id).SingleAsync();

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

    private static Invoice NewInvoice(
        string studentId, Guid categoryId, DateOnly periodMonth, decimal amount, decimal discount = 0m) => new()
        {
            StudentId = studentId,
            CategoryId = categoryId,
            PeriodMonth = periodMonth,
            Amount = amount,
            Discount = discount,
            DueOn = periodMonth.AddDays(9),
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };

    private static async Task AddSubscriptionAsync(
        AppDbContext db, string studentId, Guid categoryId, decimal monthlyAmount,
        DateOnly startsOn, string createdBy, DateOnly? endsOn = null)
    {
        db.StudentSubscriptions.Add(new StudentSubscription
        {
            StudentId = studentId,
            CategoryId = categoryId,
            MonthlyAmount = monthlyAmount,
            StartsOn = startsOn,
            EndsOn = endsOn,
            CreatedBy = createdBy,
            CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// To'lov + taqsimotlar. Chek raqami smena ichida uzluksiz bo'lishi kerak
    /// (SPEC §4.2), shuning uchun mavjud eng katta raqamdan davom etadi.
    /// </summary>
    /// <param name="reversalOf">Storno bo'lsa — bekor qilinayotgan to'lov id'si.</param>
    private static async Task<Guid> PayAsync(
        AppDbContext db, string studentId, string cashierId, Guid shiftId, decimal amount,
        (Guid InvoiceId, decimal Amount)[] allocations,
        Guid? reversalOf = null)
    {
        var lastReceipt = await db.Payments
            .Where(p => p.CashShiftId == shiftId)
            .MaxAsync(p => (long?)p.ReceiptNo) ?? 0;

        var payment = new Payment
        {
            ReceiptNo = lastReceipt + 1,
            StudentId = studentId,
            Amount = amount,
            Method = PaymentMethod.Cash,
            CashShiftId = shiftId,
            CashierId = cashierId,
            ReceivedAt = AppClock.NowInstant,
            ReversalOf = reversalOf,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        foreach (var (invoiceId, allocated) in allocations)
            db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id,
                InvoiceId = invoiceId,
                Amount = allocated,
            });

        if (allocations.Length > 0) await db.SaveChangesAsync();
        return payment.Id;
    }
}
