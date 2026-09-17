using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Tranzaksiyalar jurnali (docs/modules/finance-parity.md §2.9, F9.01–F9.05).
///
/// <para>
/// <b>Testlar ikki guruhda</b> (<c>FinanceStatementsTests</c> dagi sabab
/// bilan): RUXSAT — umumiy bazada, HTTP orqali; ARIFMETIKA — har biri o'zining
/// toza bazasida, chunki jurnal BUTUN jadvalni yig'adi va umumiy bazada
/// boshqa testlarning to'lovlari yakunni surib yuborardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TransactionJournalTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Shu klass yaratgan bazalarning ulanish satrlari — test tugagach
    /// hovuzlari yopiladi. Tozalanmasa konteynerdagi <c>max_connections</c>
    /// tugaydi va KEYINGI klasslar <c>53300</c> bilan yiqiladi.
    /// </summary>
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));

        return Task.CompletedTask;
    }

    private const string Journal = "/api/admin/finance/transactions";
    private const string Export = "/api/admin/finance/transactions/export";

    private static readonly string[] AllEndpoints = [Journal, Export];

    // =====================================================================
    //  1. RUXSAT — SPEC §4.3
    // =====================================================================

    /// <summary>
    /// Kassir jurnalga KIRA OLMAYDI. §4.3: "See variance report across
    /// cashiers — ⛔", "Record an expense — ⛔". O'z smenasi unga boshqa
    /// joyda ochiq (<c>GET /api/billing/payments</c> o'zi bilan cheklanadi).
    /// </summary>
    [Fact]
    public async Task Kassir_jurnalga_kira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        foreach (var url in AllEndpoints)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>"finance" ruxsatli xodim va o'qituvchi ham yopiq — §4.3 da ular uchun ustun yo'q.</summary>
    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    public async Task Xodim_va_oqituvchi_ham_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "finance");

        foreach (var url in AllEndpoints)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>Token'siz so'rov — 401 (403 emas): kim so'rayotgani noma'lum.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        foreach (var url in AllEndpoints)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_200_oladi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        foreach (var url in AllEndpoints)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode,
                $"{url} → {(int)response.StatusCode} {response.StatusCode}");
        }
    }

    /// <summary>
    /// Noma'lum filtr qiymati JIMGINA e'tiborsiz qoldirilmaydi: "status=faol"
    /// deb yozgan odam bo'sh emas, TO'LIQ ro'yxatni olardi va buni sezmasdi.
    /// </summary>
    [Fact]
    public async Task Notogri_filtr_400_qaytaradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Journal}?status=faol")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Journal}?kind=salary")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Journal}?method=barter")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Journal}?direction=up")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Journal}?sort=student")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"{Journal}?from=2026-05-01&to=2026-04-30")).StatusCode);
    }

    // =====================================================================
    //  2. Jurnalning asosiy mezoni — bitta ro'yxat, uchta manba
    // =====================================================================

    /// <summary>
    /// To'lov, uning stornosi va chiqim BITTA ro'yxatga tushadi; yakun esa
    /// qatorlarning ishorali yig'indisiga teng.
    ///
    /// <para>
    /// <b>Storno ikkala qatorni ham qoldiradi</b> (SPEC §4.1: original
    /// tegilmaydi) va sof natija nolga tushadi — F9.02 ning qabul mezoni.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tolov_storno_va_chiqim_bitta_royxatda_sof_natija_togri()
    {
        await using var db = await NewDbAsync("journal");
        var world = await SeedAsync(db);

        // 1 000 000 to'lov → keyin storno → sof 0.
        var payment = await PayAsync(db, world, 1_000_000m, PaymentMethod.Cash);
        await PaymentsAs(db, world)
            .ReverseAsync(payment.Id, "Summa xato kiritilgan", world.AdminId);

        // 300 000 chiqim — jurnalga tushgan.
        await SpendAsync(db, world, 300_000m, Accounts.ExpenseUtilities, Accounts.Cash);

        var page = await new TransactionJournalQuery(db).PageAsync(Filter());

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Rows.Count);

        var original = Assert.Single(page.Rows, r => r.Kind == TransactionKind.Payment);
        var storno = Assert.Single(page.Rows, r => r.Kind == TransactionKind.Reversal);
        var expense = Assert.Single(page.Rows, r => r.Kind == TransactionKind.Expense);

        // Original QOLADI va "storno qilingan" deb belgilanadi; storno qatori
        // esa o'zi kuchda — u haqiqiy pul harakati.
        Assert.Equal(TransactionStatus.Reversed, original.Status);
        Assert.Equal(TransactionStatus.Active, storno.Status);
        Assert.Equal(storno.Id, original.ReversedBy);
        Assert.Equal(original.Id, storno.ReversalOf);

        Assert.Equal(1_000_000m, original.Amount);
        Assert.Equal(-1_000_000m, storno.Amount);
        Assert.Equal(-300_000m, expense.Amount);

        // Yakun: kirim 1 000 000, chiqim 1 000 000 (storno) + 300 000 (chiqim).
        Assert.Equal(1_000_000m, page.Totals.TotalIn);
        Assert.Equal(1_300_000m, page.Totals.TotalOut);
        Assert.Equal(-300_000m, page.Totals.Net);

        // Va yakun AYNAN qatorlarning ishorali yig'indisi.
        Assert.Equal(page.Rows.Sum(r => r.SettledAmount), page.Totals.Net);
    }

    /// <summary>
    /// <b>Yakun SAHIFADAN mustaqil</b> (§2.9: "for the whole filter, not the
    /// page"). Uchinchi sahifada turgan foydalanuvchi ham butun filtr
    /// yakunini ko'radi.
    /// </summary>
    [Fact]
    public async Task Yakun_sahifadan_mustaqil_va_hamma_qatorni_qamraydi()
    {
        await using var db = await NewDbAsync("paging");
        var world = await SeedAsync(db);

        for (var i = 1; i <= 7; i++) await PayAsync(db, world, 100_000m * i, PaymentMethod.Cash);
        await SpendAsync(db, world, 50_000m, Accounts.ExpenseRent, Accounts.Bank);

        var journal = new TransactionJournalQuery(db);

        var first = await journal.PageAsync(Filter() with { Page = 1, PageSize = 3 });
        var second = await journal.PageAsync(Filter() with { Page = 2, PageSize = 3 });
        var third = await journal.PageAsync(Filter() with { Page = 3, PageSize = 3 });

        Assert.Equal(8, first.Total);
        Assert.Equal(3, first.Rows.Count);
        Assert.Equal(3, second.Rows.Count);
        Assert.Equal(2, third.Rows.Count);

        // Sahifalar KESISHMAYDI va birgalikda hamma qatorni beradi.
        var ids = first.Rows.Concat(second.Rows).Concat(third.Rows).Select(r => r.Id).ToList();
        Assert.Equal(8, ids.Distinct().Count());

        // Har sahifada yakun BIR XIL va butun to'plamga teng.
        var expected = first.Rows.Concat(second.Rows).Concat(third.Rows).Sum(r => r.SettledAmount);
        foreach (var page in new[] { first, second, third })
        {
            Assert.Equal(2_800_000m, page.Totals.TotalIn);   // 100k+200k+…+700k
            Assert.Equal(50_000m, page.Totals.TotalOut);
            Assert.Equal(expected, page.Totals.Net);
        }
    }

    /// <summary>
    /// Tasdiq kutayotgan va storno qilingan chiqim ko'rinadi, lekin PUL
    /// YAKUNIGA kirmaydi — ularda pul harakat qilmagan.
    /// </summary>
    [Fact]
    public async Task Tasdiq_kutayotgan_va_storno_qilingan_chiqim_yakunga_kirmaydi()
    {
        await using var db = await NewDbAsync("expstate");
        var world = await SeedAsync(db);

        await SpendAsync(db, world, 100_000m, Accounts.ExpenseSupplies, Accounts.Cash);

        // Storno qilingan chiqim: jurnalga tushgan, keyin ko'zgu satrlar qo'yilgan.
        var reversed = await SpendAsync(db, world, 200_000m, Accounts.ExpenseSupplies, Accounts.Cash);
        var anchor = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Expense && e.RefId == reversed && e.ReversalOf == null)
            .OrderBy(e => e.Id).Select(e => e.Id).FirstAsync();
        // Partiyani admin qo'ygan (SpendAsync), shuning uchun storno'ni
        // UCHINCHI shaxs qiladi — SPEC §4.5 ikki qavatli nazorati.
        await world.Ledger.ReverseAsync(anchor, "Xato yozilgan", world.DirectorId);

        // Tasdiq kutayotgan chiqim: `expenses` qatori bor, jurnal partiyasi YO'Q.
        db.Expenses.Add(new Expense
        {
            OnDate = AppClock.Today,
            Category = "rent",
            Amount = 900_000m,
            CreatedBy = world.CashierId,
            CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();

        var page = await new TransactionJournalQuery(db).PageAsync(Filter());

        Assert.Equal(3, page.Total);
        Assert.Equal(0m, page.Totals.TotalIn);
        Assert.Equal(100_000m, page.Totals.TotalOut);
        Assert.Equal(900_000m, page.Totals.PendingOut);
        Assert.Equal(-100_000m, page.Totals.Net);
        Assert.Equal(page.Rows.Sum(r => r.SettledAmount), page.Totals.Net);

        // Ko'rinadi, lekin 0 bilan.
        var pending = Assert.Single(page.Rows, r => r.Status == TransactionStatus.Pending);
        Assert.Equal(-900_000m, pending.Amount);
        Assert.Equal(0m, pending.SettledAmount);

        var stornoed = Assert.Single(page.Rows, r => r.Status == TransactionStatus.Reversed);
        Assert.Equal(-200_000m, stornoed.Amount);
        Assert.Equal(0m, stornoed.SettledAmount);
        Assert.Contains("Xato yozilgan", stornoed.Note ?? string.Empty, StringComparison.Ordinal);
    }

    // =====================================================================
    //  3. Filtrlar
    // =====================================================================

    /// <summary>Chek raqami bo'yicha qidiruv faqat to'lovni topadi — chiqimda chek yo'q.</summary>
    [Fact]
    public async Task Chek_raqami_boyicha_qidiruv_faqat_tolovni_topadi()
    {
        await using var db = await NewDbAsync("receipt");
        var world = await SeedAsync(db);

        var first = await PayAsync(db, world, 111_000m, PaymentMethod.Cash);
        await PayAsync(db, world, 222_000m, PaymentMethod.Card);
        await SpendAsync(db, world, 333_000m, Accounts.ExpenseRent, Accounts.Cash);

        var page = await new TransactionJournalQuery(db)
            .PageAsync(Filter() with { ReceiptNo = first.ReceiptNo });

        var row = Assert.Single(page.Rows);
        Assert.Equal(first.Id, row.Id);
        Assert.Equal(111_000m, row.Amount);
        Assert.Equal(111_000m, page.Totals.TotalIn);
        Assert.Equal(0m, page.Totals.TotalOut);
    }

    /// <summary>
    /// Yo'nalish va tur filtrlari. "Chiqim" — storno qatorlari VA chiqimlar:
    /// ikkalasida ham pul kassadan chiqadi.
    /// </summary>
    [Fact]
    public async Task Yonalish_va_tur_filtrlari()
    {
        await using var db = await NewDbAsync("filters");
        var world = await SeedAsync(db);

        var payment = await PayAsync(db, world, 400_000m, PaymentMethod.Cash);
        await PaymentsAs(db, world)
            .ReverseAsync(payment.Id, "Test storno", world.AdminId);
        await SpendAsync(db, world, 150_000m, Accounts.ExpenseRent, Accounts.Cash);

        var journal = new TransactionJournalQuery(db);

        var incoming = await journal.PageAsync(Filter() with { Direction = TransactionDirection.In });
        Assert.Equal(1, incoming.Total);
        Assert.Equal(TransactionKind.Payment, Assert.Single(incoming.Rows).Kind);

        var outgoing = await journal.PageAsync(Filter() with { Direction = TransactionDirection.Out });
        Assert.Equal(2, outgoing.Total);
        Assert.All(outgoing.Rows, r => Assert.Equal(TransactionDirection.Out, r.Direction));

        var expensesOnly = await journal.PageAsync(Filter() with { Kind = TransactionKind.Expense });
        Assert.Equal(TransactionKind.Expense, Assert.Single(expensesOnly.Rows).Kind);

        // Chiqimda o'quvchi yo'q — o'quvchi filtri qo'yilganda u tushmaydi.
        var byStudent = await journal.PageAsync(Filter() with { StudentId = world.StudentId });
        Assert.Equal(2, byStudent.Total);
        Assert.DoesNotContain(byStudent.Rows, r => r.Kind == TransactionKind.Expense);
    }

    /// <summary>
    /// F9.05 — "faqat birinchi to'lov". Birinchi deb o'quvchining eng erta
    /// KUCHDAGI to'lovi olinadi: xato kiritilib darhol storno qilingan to'lov
    /// "birinchi" bo'lib qolmasligi kerak, aks holda o'quvchi ro'yxatdan
    /// butunlay yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Faqat_birinchi_tolov_storno_qilinganini_hisobga_olmaydi()
    {
        await using var db = await NewDbAsync("firstpay");
        var world = await SeedAsync(db);

        var wrong = await PayAsync(db, world, 10_000m, PaymentMethod.Cash);
        await PaymentsAs(db, world)
            .ReverseAsync(wrong.Id, "Xato summa", world.AdminId);

        var real = await PayAsync(db, world, 500_000m, PaymentMethod.Cash);
        await PayAsync(db, world, 600_000m, PaymentMethod.Cash);

        var page = await new TransactionJournalQuery(db)
            .PageAsync(Filter() with { OnlyFirstPayment = true });

        var row = Assert.Single(page.Rows);
        Assert.Equal(real.Id, row.Id);
        Assert.Equal(500_000m, row.Amount);
    }

    /// <summary>
    /// <b>Kun chegarasi <c>PaymentService</c> nikidan farq qilmaydi.</b>
    /// Toshkent UTC+5 da kun 19:00 UTC da almashadi; ikki ekran ikki xil
    /// chegara ishlatsa, kechki to'lov birida 10-, ikkinchisida 11-kunga
    /// tushib qolardi. Shuning uchun bu yerda ikkala yo'l ham AYNAN bir xil
    /// so'rovga bir xil javob berishi tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Kun_chegarasi_PaymentService_bilan_bir_xil()
    {
        await using var db = await NewDbAsync("dayline");
        var world = await SeedAsync(db);

        // 19:30 UTC = Toshkentda ertasi kun 00:30.
        var evening = new DateTimeOffset(2026, 6, 10, 19, 30, 0, TimeSpan.Zero);
        var payment = await PayAsync(db, world, 250_000m, PaymentMethod.Cash, evening);

        var tashkentDay = new DateOnly(2026, 6, 11);

        var payments = PaymentsAs(db, world);
        var viaPayments = await payments.ListAsync(new PaymentQuery(From: tashkentDay, To: tashkentDay));

        var viaJournal = await new TransactionJournalQuery(db).PageAsync(
            Filter() with { From = tashkentDay, To = tashkentDay });

        Assert.Equal(payment.Id, Assert.Single(viaPayments).Id);
        Assert.Equal(payment.Id, Assert.Single(viaJournal.Rows).Id);
        Assert.Equal(tashkentDay, viaJournal.Rows[0].OccurredOn);

        // Oldingi kun ikkala yo'lda ham BO'SH.
        var previous = new DateOnly(2026, 6, 10);
        Assert.Empty(await payments.ListAsync(new PaymentQuery(From: previous, To: previous)));
        Assert.Empty((await new TransactionJournalQuery(db).PageAsync(
            Filter() with { From = previous, To = previous })).Rows);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>Filtr: butun tarix (testda sanaga bog'lanmaslik uchun).</summary>
    private static TransactionJournalFilter Filter() =>
        new(From: new DateOnly(2000, 1, 1), To: new DateOnly(2100, 1, 1));

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>
    /// Kassir, tasdiqlovchi admin va IKKALASINING ochiq smenasi. Ikkinchi
    /// smena kerak: storno qatori TASDIQLOVCHINING o'z smenasiga tushadi
    /// (<c>PaymentService.ReverseAsync</c> izohi — pul bugun, uning
    /// kassasidan chiqadi).
    /// </summary>
    private sealed record World(
        string CashierId, string AdminId, string DirectorId,
        Guid ShiftId, Guid AdminShiftId, string StudentId,
        Guid CategoryId, LedgerService Ledger);

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var adminId = await SeedUserAsync(db, Roles.Admin);
        var directorId = await SeedUserAsync(db, Roles.SuperAdmin);

        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        var adminShift = new CashShift
        {
            CashierId = adminId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.AddRange(shift, adminShift);

        var studentId = "stu-" + Guid.NewGuid().ToString("N")[..12];
        db.Students.Add(new Student
        {
            Id = studentId,
            FullName = "Jurnal O'quvchisi",
            LastName = "Jurnal",
            FirstName = "O'quvchi",
            ClassName = "5-A",
            EnrollmentDate = "2024-09-01",
        });
        await db.SaveChangesAsync();

        var categoryId = await db.FeeCategories.AsNoTracking()
            .Where(c => c.Code == "tuition").Select(c => c.Id).SingleAsync();

        return new World(
            cashierId, adminId, directorId, shift.Id, adminShift.Id, studentId, categoryId,
            new LedgerService(db));
    }

    /// <summary>Storno qiluvchi admin nomidan ishlaydigan to'lov xizmati.</summary>
    private static PaymentService PaymentsAs(AppDbContext db, World world) =>
        new(db, new ShiftStub(world.AdminShiftId, world.AdminId), world.Ledger);

    private static async Task<string> SeedUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser
        {
            FullName = $"Test {role} {Guid.NewGuid().ToString("N")[..6]}",
            Role = role,
            Email = $"{role}.{Guid.NewGuid().ToString("N")[..8]}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// To'lov + jurnal partiyasi — <c>PaymentService.AcceptAsync</c> ning aynan
    /// o'sha ikki qatori (debet pul hisobi, kredit <c>receivable</c>).
    /// Taqsimotsiz: jurnal qatori taqsimotga bog'liq emas.
    /// </summary>
    private static async Task<Payment> PayAsync(
        AppDbContext db, World world, decimal amount, string method,
        DateTimeOffset? receivedAt = null)
    {
        var lastReceipt = await db.Payments
            .Where(p => p.CashShiftId == world.ShiftId)
            .MaxAsync(p => (long?)p.ReceiptNo) ?? 0;

        var payment = new Payment
        {
            ReceiptNo = lastReceipt + 1,
            StudentId = world.StudentId,
            Amount = amount,
            Method = method,
            CashShiftId = world.ShiftId,
            CashierId = world.CashierId,
            ReceivedAt = receivedAt ?? AppClock.NowInstant,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        await world.Ledger.PostAsync(
        [
            new LedgerPosting(Accounts.SettlementFor(method), LedgerDirection.Debit, amount,
                LedgerRefType.Payment, payment.Id, AppClock.LocalDateOf(payment.ReceivedAt)),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, amount,
                LedgerRefType.Payment, payment.Id, AppClock.LocalDateOf(payment.ReceivedAt)),
        ], world.CashierId);

        return payment;
    }

    /// <summary>Chiqim + jurnal partiyasi (jurnalga tushgan, tasdiqlangan holat).</summary>
    private static async Task<Guid> SpendAsync(
        AppDbContext db, World world, decimal amount, string expenseAccount, string moneyAccount)
    {
        var expense = new Expense
        {
            OnDate = AppClock.Today,
            Category = expenseAccount[(expenseAccount.IndexOf(':', StringComparison.Ordinal) + 1)..],
            Amount = amount,
            Note = "Test chiqimi",
            CreatedBy = world.CashierId,
            ApprovedBy = world.AdminId,
            CreatedAt = AppClock.NowInstant,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();

        await world.Ledger.PostAsync(
        [
            new LedgerPosting(expenseAccount, LedgerDirection.Debit, amount,
                LedgerRefType.Expense, expense.Id, expense.OnDate),
            new LedgerPosting(moneyAccount, LedgerDirection.Credit, amount,
                LedgerRefType.Expense, expense.Id, expense.OnDate),
        ], world.AdminId);

        return expense.Id;
    }

    /// <summary>
    /// <see cref="ICashShiftService"/> ning storno uchun yetadigan qismi:
    /// tasdiqlovchining ochiq smenasi va keyingi chek raqami. Haqiqiy
    /// <c>CashShiftService</c> bu testlarda kerak emas — storno faqat
    /// INTERFEYSGA tayanadi.
    /// </summary>
    private sealed class ShiftStub(Guid shiftId, string cashierId) : ICashShiftService
    {
        private long _receipt = 1000;

        public Task<CashShiftDto?> CurrentAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult<CashShiftDto?>(new CashShiftDto(
                shiftId, cashierId, "Kassir", AppClock.NowInstant, null, 0m,
                null, null, null, CashShiftStatus.Open, null, 0, 0m, 0m));

        public Task<long> NextReceiptNoAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Interlocked.Increment(ref _receipt));

        public Task<CashShiftDto> OpenAsync(string id, decimal openingFloat, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CashShiftDto> CloseAsync(
            Guid id, string closedBy, decimal counted, string? note, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ZReportDto> ZReportAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CashShiftDto>> ListAsync(
            CashShiftQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
