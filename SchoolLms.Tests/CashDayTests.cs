using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using Npgsql;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// "Kassa kuni" — kunlik kassa paneli
/// (<c>GET /api/admin/finance/cash-day</c>, docs/modules/existing-module-gaps.md §3.2).
///
/// <para>
/// <b>Testlar ikki guruhga bo'lingan, <see cref="FinanceReportsTests"/> dagi
/// sabab bilan.</b> RUXSAT va JSON shakli — umumiy bazada, HTTP orqali.
/// ARIFMETIKA esa har biri O'ZINING toza bazasida: kunlik panel butun
/// jurnalni (kun boshigacha bo'lgan qoldiqni) yig'adi, ya'ni qo'shni
/// testning bitta yozuvi ham natijani o'zgartirardi.
/// </para>
/// <para>
/// <b>Sanalar 2003-yil.</b> Boshqa testlar bugungi va 2001–2002 sanalar
/// bilan yozadi (MoneyFlowTests, LedgerServiceTests) — kesishmasin.
/// Yagona istisno: storno testi, chunki <c>LedgerService.ReverseAsync</c>
/// ko'zgu satrni ATAYLAB bugungi sana bilan yozadi (SPEC §4.1) va aynan shu
/// xossa sinaladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CashDayTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Shu klass yaratgan bazalarning ulanish satrlari.
    ///
    /// <para>
    /// <b>Nega kerak.</b> Har test O'Z bazasini oladi, ya'ni O'Z ulanish
    /// hovuzini ham. Hovuz tozalanmasa tugagan testning ulanishlari ochiq
    /// qolib, konteynerdagi <c>max_connections</c> ni yeb qo'yadi va KEYINGI
    /// test klasslari <c>53300</c> bilan yiqiladi — o'z aybi bilan emas.
    /// <c>AllocationTests</c> da xuddi shu izoh bor; bu yerda bazalar test
    /// ichida yaratilgani uchun ro'yxat yuritiladi.
    /// </para>
    /// </summary>
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));

        return Task.CompletedTask;
    }

    private const string Day = "/api/admin/finance/cash-day";
    private const string Calendar = "/api/admin/finance/cash-day/calendar";

    private static readonly string[] AllEndpoints = [Day, Calendar];

    // =====================================================================
    //  1. RUXSAT — SPEC §4.3 (ViewBillingReports)
    // =====================================================================

    /// <summary>
    /// <b>Asosiy ruxsat mezoni.</b> Ekran "kassa kuni" deb atalsa ham, KASSIR
    /// uni ko'rmaydi: panel butun maktabning kunini — hamma kassirning
    /// smenasini, bank hisobini, chiqimlarni va maoshni — ochib beradi.
    /// SPEC §4.3: "See variance report across cashiers — ⛔".
    /// </summary>
    [Theory]
    [InlineData(Day)]
    [InlineData(Calendar)]
    public async Task Kassir_kassa_kuniga_kira_olmaydi_403(string url)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// "finance" ruxsat kaliti bor xodim ham yopiq: §4.3 jadvalida "staff"
    /// ustuni umuman yo'q, <c>FinanceMatrix</c> esa qoidasi yo'q rolni RAD
    /// ETADI. Menyuni yashirish yetarli emas — endpoint ham 403 berishi kerak.
    /// </summary>
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
    /// Javob SHAKLI — frontend AYNAN shu nomlarga bog'lanadi (camelCase).
    /// Holat kodi yetarli emas: nom o'zgarsa ekran jimgina bo'sh qolardi.
    /// </summary>
    [Fact]
    public async Task Javob_shakli_frontend_kutgan_maydonlarni_beradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        using var day = JsonDocument.Parse(
            await (await client.GetAsync($"{Day}?date=2003-01-15")).Content.ReadAsStringAsync());
        var root = day.RootElement;

        foreach (var field in new[]
                 {
                     "date", "total", "accounts", "movements", "movementsTruncated",
                     "movementsTotal", "topFive", "byType", "byMethod", "byCategory",
                     "allocatedTotal", "unallocatedTotal", "openShifts",
                 })
            Assert.True(root.TryGetProperty(field, out _), $"'{field}' maydoni yo'q.");

        // `cash` va `bank` — HAR DOIM ikkalasi ham, harakat bo'lmasa ham:
        // ustunlar kundan kunga o'zgarib ketmasin.
        var accounts = root.GetProperty("accounts").EnumerateArray().ToList();
        Assert.Equal(2, accounts.Count);
        Assert.Equal(Accounts.Cash, accounts[0].GetProperty("account").GetString());
        Assert.Equal(Accounts.Bank, accounts[1].GetProperty("account").GetString());
        foreach (var field in new[] { "opening", "inflow", "outflow", "net", "closing" })
            Assert.True(accounts[0].TryGetProperty(field, out _), $"accounts[0].'{field}' yo'q.");

        using var calendar = JsonDocument.Parse(
            await (await client.GetAsync($"{Calendar}?month=2003-01")).Content.ReadAsStringAsync());

        Assert.Equal("2003-01-01", calendar.RootElement.GetProperty("month").GetString());
        // Yanvar — 31 kun. Harakatsiz kun ham qatorga TUSHADI.
        Assert.Equal(31, calendar.RootElement.GetProperty("days").GetArrayLength());
    }

    /// <summary>
    /// Noto'g'ri sana jimgina "bugun" ga aylanmasin: erkin format moliyada
    /// bir oylik siljish degani, shuning uchun 400.
    /// </summary>
    [Theory]
    [InlineData(Day, "date=15.01.2003")]
    [InlineData(Day, "date=2003-13-40")]
    [InlineData(Calendar, "month=yanvar")]
    [InlineData(Calendar, "month=2003-13")]
    public async Task Notogri_sana_400_beradi(string url, string query)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{url}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Kalendar sana tanlagichidan to'liq sana kelsa ham ishlaydi (kuni
    /// tashlab yuboriladi) — UI 400 bilan yiqilmasin.
    /// </summary>
    [Fact]
    public async Task Kalendar_toliq_sanani_ham_qabul_qiladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{Calendar}?month=2003-04-17");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2003-04-01", json.RootElement.GetProperty("month").GetString());
    }

    // =====================================================================
    //  2. Kun arifmetikasi — toza bazada
    // =====================================================================

    /// <summary>
    /// <b>Asosiy arifmetika mezoni.</b> Ochilish → kirim → chiqim → yopilish,
    /// naqd va bank ALOHIDA. Kutilgan raqamlar testda mustaqil, qo'lda
    /// hisoblangan — so'rov mantig'i bu yerda takrorlanmaydi.
    /// </summary>
    [Fact]
    public async Task Kun_ochilish_kirim_chiqim_yopilishni_naqd_va_bank_kesimida_beradi()
    {
        await using var db = await NewCashDayDbAsync("figures");
        var ledger = new LedgerService(db);
        var actorId = await SeedUserAsync(db, Roles.Admin);

        var yesterday = new DateOnly(2003, 2, 9);
        var today = new DateOnly(2003, 2, 10);

        // Kechagi naqd to'lov — bugungi ochilish qoldig'i shundan.
        await CashPaymentAsync(ledger, actorId, yesterday, 1_000_000m, PaymentMethod.Cash);

        // Bugun: naqd 500 000.50, bankka 300 000, kassadan 200 000 chiqim.
        await CashPaymentAsync(ledger, actorId, today, 500_000.50m, PaymentMethod.Cash);
        await CashPaymentAsync(ledger, actorId, today, 300_000m, PaymentMethod.Card);
        await ExpenseAsync(db, ledger, actorId, today, "salary", 200_000m, PaymentMethod.Cash);

        var result = await QueriesFor(db).DayAsync(today);

        var cash = result.Accounts.Single(a => a.Account == Accounts.Cash);
        Assert.Equal(1_000_000m, cash.Opening);
        Assert.Equal(500_000.50m, cash.Inflow);
        Assert.Equal(200_000m, cash.Outflow);
        Assert.Equal(300_000.50m, cash.Net);
        Assert.Equal(1_300_000.50m, cash.Closing);

        var bank = result.Accounts.Single(a => a.Account == Accounts.Bank);
        Assert.Equal(0m, bank.Opening);
        Assert.Equal(300_000m, bank.Inflow);
        Assert.Equal(0m, bank.Outflow);
        Assert.Equal(300_000m, bank.Closing);

        // Jami — ikki hisobning yig'indisi, mustaqil hisoblanmaydi.
        Assert.Equal(1_000_000m, result.Total.Opening);
        Assert.Equal(800_000.50m, result.Total.Inflow);
        Assert.Equal(200_000m, result.Total.Outflow);
        Assert.Equal(1_600_000.50m, result.Total.Closing);

        // Uchta harakat: ikki kirim + bitta chiqim. Yangisidan eskisiga.
        Assert.Equal(3, result.MovementsTotal);
        Assert.False(result.MovementsTruncated);
        Assert.Equal(
            result.Movements.Select(m => m.EntryId).OrderByDescending(id => id).ToArray(),
            result.Movements.Select(m => m.EntryId).ToArray());
    }

    /// <summary>
    /// Kunning ochilishi — shu kungacha bo'lgan BUTUN tarix, "kecha"
    /// emas. Harakatsiz kun qoldiqni o'zgarishsiz olib o'tadi.
    /// </summary>
    [Fact]
    public async Task Harakatsiz_kunda_ochilish_yopilishga_teng()
    {
        await using var db = await NewCashDayDbAsync("idle");
        var ledger = new LedgerService(db);
        var actorId = await SeedUserAsync(db, Roles.Admin);

        await CashPaymentAsync(ledger, actorId, new DateOnly(2003, 3, 1), 750_000m, PaymentMethod.Cash);

        var result = await QueriesFor(db).DayAsync(new DateOnly(2003, 3, 20));

        Assert.Equal(750_000m, result.Total.Opening);
        Assert.Equal(0m, result.Total.Inflow);
        Assert.Equal(0m, result.Total.Outflow);
        Assert.Equal(750_000m, result.Total.Closing);
        Assert.Empty(result.Movements);
        Assert.Empty(result.ByType);
    }

    /// <summary>
    /// <b>Kun ekranidagi eng nozik qoida.</b> Storno o'tgan kunni O'ZGARTIRMAYDI:
    /// <c>LedgerService.ReverseAsync</c> ko'zgu satrni bugungi sana bilan
    /// yozadi (SPEC §4.1 — yopilgan davr orqaga qarab tuzatilmaydi). Ya'ni
    /// kecha chop etilgan kunlik hisobot bugun o'zgarib qolmaydi, storno esa
    /// BUGUNGI kunning chiqimi bo'lib ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Storno_asl_kunni_ozgartirmaydi_ozining_kunida_chiqim_boladi()
    {
        await using var db = await NewCashDayDbAsync("storno");
        var ledger = new LedgerService(db);
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var directorId = await SeedUserAsync(db, Roles.SuperAdmin);

        var paidOn = new DateOnly(2003, 4, 7);
        var (_, anchorId) = await CashPaymentAsync(ledger, cashierId, paidOn, 640_000m, PaymentMethod.Cash);

        // Storno IKKINCHI shaxs nomidan (SPEC §4.5) va DOIM bugungi sana bilan.
        await ledger.ReverseAsync(anchorId, "Ota-ona ikki marta to'lagan", directorId);

        var queries = QueriesFor(db);

        var original = await queries.DayAsync(paidOn);
        Assert.Equal(640_000m, original.Total.Inflow);
        Assert.Equal(0m, original.Total.Outflow);
        Assert.Equal(640_000m, original.Total.Closing);
        Assert.DoesNotContain(original.Movements, m => m.IsReversal);

        var reversalDay = await queries.DayAsync(AppClock.Today);
        Assert.Equal(0m, reversalDay.Total.Inflow);
        Assert.Equal(640_000m, reversalDay.Total.Outflow);

        var movement = Assert.Single(reversalDay.Movements);
        Assert.True(movement.IsReversal);
        Assert.Equal(LedgerRefType.Reversal, movement.Kind);
        Assert.Equal(LedgerDirection.Credit, movement.Direction);
        Assert.Equal(-640_000m, movement.Signed);
        // Sabab majburiy (SPEC §4.3) va u ekranda KO'RINADI.
        Assert.Equal("Ota-ona ikki marta to'lagan", movement.Memo);
        Assert.StartsWith("STORNO", movement.Title, StringComparison.Ordinal);

        // Turlar kesimida storno ALOHIDA qator — asl turga netlanmaydi.
        var row = Assert.Single(reversalDay.ByType);
        Assert.True(row.IsReversal);
        Assert.Equal($"{Accounts.Receivable}:reversal", row.Key);
        Assert.Equal(-640_000m, row.Amount);
    }

    /// <summary>
    /// Toifalar kesimi: to'lov kunida PLUS, storno kunida MINUS. Storno o'z
    /// taqsimotini yozmaydi (<c>payment_allocations.amount &gt; 0</c> — baza
    /// check'i), shuning uchun minus ASL to'lovning taqsimotidan olinadi.
    /// </summary>
    [Fact]
    public async Task Toifalar_kesimi_tolovda_plus_storno_kunida_minus()
    {
        await using var db = await NewCashDayDbAsync("categories");
        var ledger = new LedgerService(db);
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var directorId = await SeedUserAsync(db, Roles.SuperAdmin);
        var shiftId = await SeedShiftAsync(db, cashierId, openingFloat: 0m);

        var tuition = await CategoryIdAsync(db, "tuition");
        var bus = await CategoryIdAsync(db, "bus");

        var studentId = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(studentId, "Dilnoza Karimova", "6-B"));
        var tuitionInvoice = NewInvoice(studentId, tuition, new DateOnly(2003, 5, 1), 900_000m);
        var busInvoice = NewInvoice(studentId, bus, new DateOnly(2003, 5, 1), 100_000m);
        db.Invoices.AddRange(tuitionInvoice, busInvoice);
        await db.SaveChangesAsync();

        var paidOn = new DateOnly(2003, 5, 12);
        var (_, anchorId) = await CashPaymentAsync(
            ledger, cashierId, paidOn, 1_000_000m, PaymentMethod.Cash,
            db, shiftId, studentId, [(tuitionInvoice.Id, 900_000m), (busInvoice.Id, 100_000m)]);

        var queries = QueriesFor(db);

        var onPayment = await queries.DayAsync(paidOn);
        Assert.Equal(900_000m, onPayment.ByCategory.Single(c => c.CategoryCode == "tuition").Amount);
        Assert.Equal(100_000m, onPayment.ByCategory.Single(c => c.CategoryCode == "bus").Amount);
        Assert.Equal(1_000_000m, onPayment.AllocatedTotal);
        Assert.Equal(0m, onPayment.UnallocatedTotal);

        // O'quvchi ismi SERVERDAN keladi — UI uni qidirib yurmaydi.
        Assert.Equal("Dilnoza Karimova", Assert.Single(onPayment.Movements).Title);

        await ledger.ReverseAsync(anchorId, "Xato toifaga yozilgan", directorId);

        var onReversal = await queries.DayAsync(AppClock.Today);
        Assert.Equal(-900_000m, onReversal.ByCategory.Single(c => c.CategoryCode == "tuition").Amount);
        Assert.Equal(-100_000m, onReversal.ByCategory.Single(c => c.CategoryCode == "bus").Amount);
        Assert.Equal(-1_000_000m, onReversal.AllocatedTotal);

        // Asl kun O'ZGARMAGAN — tekshiruv takrorlanadi, chunki qoidaning
        // buzilishi aynan shu yerda ko'rinmay qolardi.
        var again = await queries.DayAsync(paidOn);
        Assert.Equal(1_000_000m, again.AllocatedTotal);
    }

    /// <summary>
    /// Taqsimlanmagan qism = AVANS: pul keldi, lekin hali hech qaysi
    /// hisob-fakturaga biriktirilmadi. Uni yashirish "qo'shilmaydigan ikki
    /// raqam" berardi.
    /// </summary>
    [Fact]
    public async Task Taqsimlanmagan_tolov_avans_sifatida_ajratib_korsatiladi()
    {
        await using var db = await NewCashDayDbAsync("advance");
        var ledger = new LedgerService(db);
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var shiftId = await SeedShiftAsync(db, cashierId, openingFloat: 0m);

        var tuition = await CategoryIdAsync(db, "tuition");
        var studentId = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(studentId, "Avansli O'quvchi", "1-A"));
        var invoice = NewInvoice(studentId, tuition, new DateOnly(2003, 6, 1), 400_000m);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var date = new DateOnly(2003, 6, 5);
        await CashPaymentAsync(
            ledger, cashierId, date, 1_000_000m, PaymentMethod.Cash,
            db, shiftId, studentId, [(invoice.Id, 400_000m)]);

        var result = await QueriesFor(db).DayAsync(date);

        Assert.Equal(400_000m, result.AllocatedTotal);
        Assert.Equal(600_000m, result.UnallocatedTotal);
        Assert.Equal(1_000_000m, result.Total.Inflow);
    }

    /// <summary>
    /// Kunning eng yirik beshtasi — MODUL bo'yicha, ya'ni katta chiqim ham
    /// ro'yxatga tushadi. Kassirning "bugun eng katta nima bo'ldi" degan
    /// savoliga javob shu.
    /// </summary>
    [Fact]
    public async Task Eng_yirik_beshta_harakat_modul_boyicha_tanlanadi()
    {
        await using var db = await NewCashDayDbAsync("topfive");
        var ledger = new LedgerService(db);
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var date = new DateOnly(2003, 7, 14);

        foreach (var amount in new[] { 100_000m, 200_000m, 300_000m, 400_000m, 500_000m, 600_000m })
            await CashPaymentAsync(ledger, actorId, date, amount, PaymentMethod.Cash);

        // Eng yirik harakat — CHIQIM. Modul bo'yicha tanlanmasa tushib qolardi.
        await ExpenseAsync(db, ledger, actorId, date, "rent", 9_000_000m, PaymentMethod.Transfer);

        var result = await QueriesFor(db).DayAsync(date);

        Assert.Equal(7, result.MovementsTotal);
        Assert.Equal(CashDayQueries.TopCount, result.TopFive.Count);
        Assert.Equal(
            new[] { 9_000_000m, 600_000m, 500_000m, 400_000m, 300_000m },
            result.TopFive.Select(m => m.Amount).ToArray());
        Assert.Equal(-9_000_000m, result.TopFive[0].Signed);
        Assert.Equal("rent", result.TopFive[0].Category);
    }

    /// <summary>
    /// Turlar kesimi hisoblar rejasidan kelib chiqadi — yangi taksonomiya
    /// o'ylab topilmaydi. To'lov → <c>receivable</c>, chiqim → <c>expense:*</c>,
    /// va nomlar <see cref="MoneyFlowQueries.LabelFor"/> bilan bir xil.
    /// </summary>
    [Fact]
    public async Task Turlar_kesimi_qarshi_hisob_boyicha_guruhlanadi()
    {
        await using var db = await NewCashDayDbAsync("types");
        var ledger = new LedgerService(db);
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var date = new DateOnly(2003, 8, 20);

        await CashPaymentAsync(ledger, actorId, date, 700_000m, PaymentMethod.Cash);
        await CashPaymentAsync(ledger, actorId, date, 300_000m, PaymentMethod.Transfer);
        await ExpenseAsync(db, ledger, actorId, date, "salary", 450_000m, PaymentMethod.Cash);
        await ExpenseAsync(db, ledger, actorId, date, "utilities", 120_000m, PaymentMethod.Cash);

        var result = await QueriesFor(db).DayAsync(date);

        var payments = result.ByType.Single(r => r.Account == Accounts.Receivable);
        Assert.Equal(2, payments.Count);
        Assert.Equal(1_000_000m, payments.Amount);
        Assert.Equal("O'quvchi to'lovlari", payments.Label);
        Assert.False(payments.IsReversal);

        var salary = result.ByType.Single(r => r.Account == Accounts.ExpenseSalary);
        Assert.Equal(-450_000m, salary.Amount);
        Assert.Equal(MoneyFlowQueries.LabelFor(Accounts.ExpenseSalary), salary.Label);

        Assert.Equal(-120_000m, result.ByType.Single(r => r.Account == Accounts.ExpenseUtilities).Amount);

        // Yig'indi kunning sof natijasiga TENG — kesim bo'lakni yo'qotmasin.
        Assert.Equal(result.Total.Net, result.ByType.Sum(r => r.Amount));
    }

    /// <summary>
    /// To'lov usullari kesimi (docs/modules/finance-parity.md §2.8 F8.02):
    /// "pul qanday keldi" — naqd, karta, o'tkazma, onlayn.
    ///
    /// <para>
    /// <b>FAQAT to'lovlar.</b> Chiqimda usul saqlanmaydi (<c>expenses</c> da
    /// bunday ustun yo'q), shuning uchun chiqim bu kesimga UMUMAN tushmaydi
    /// va kesim yig'indisi kunning sof natijasiga teng emas — bu ataylab.
    /// Storno esa o'z usulida MINUS bilan ko'rinadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Usullar_kesimi_faqat_tolovlarni_sanaydi()
    {
        await using var db = await NewCashDayDbAsync("methods");
        var ledger = new LedgerService(db);
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var approverId = await SeedUserAsync(db, Roles.Admin);
        var shiftId = await SeedShiftAsync(db, cashierId, openingFloat: 0m);

        // Usul `payments` qatorida saqlanadi, shuning uchun bu test HAQIQIY
        // to'lov qatorlarini yozadi (jurnal satrining o'zi yetarli emas).
        var studentId = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(studentId, "Usul O'quvchisi", "3-B"));
        await db.SaveChangesAsync();

        var date = AppClock.Today;

        await CashPaymentAsync(ledger, cashierId, date, 700_000m, PaymentMethod.Cash,
            db, shiftId, studentId);
        await CashPaymentAsync(ledger, cashierId, date, 300_000m, PaymentMethod.Card,
            db, shiftId, studentId);
        var (_, anchorId) = await CashPaymentAsync(ledger, cashierId, date, 200_000m, PaymentMethod.Cash,
            db, shiftId, studentId);
        await ledger.ReverseAsync(anchorId, "Xato chek", approverId);

        // Chiqim — usulsiz, kesimga tushmaydi.
        await ExpenseAsync(db, ledger, approverId, date, "rent", 900_000m, PaymentMethod.Cash);

        var result = await QueriesFor(db).DayAsync(date);

        var cash = result.ByMethod.Single(r => r.Method == PaymentMethod.Cash);
        Assert.Equal("Naqd", cash.Label);
        Assert.Equal(900_000m, cash.Inflow);      // 700 000 + 200 000
        Assert.Equal(200_000m, cash.Outflow);     // storno
        Assert.Equal(700_000m, cash.Amount);
        Assert.Equal(3, cash.Count);

        var card = result.ByMethod.Single(r => r.Method == PaymentMethod.Card);
        Assert.Equal(300_000m, card.Amount);
        Assert.Equal("Karta", card.Label);

        // Chiqim bu kesimda YO'Q, lekin turlar kesimida bor.
        Assert.Equal(2, result.ByMethod.Count);
        Assert.Contains(result.ByType, r => r.Account == Accounts.ExpenseRent);
        Assert.NotEqual(result.Total.Net, result.ByMethod.Sum(r => r.Amount));
    }

    /// <summary>
    /// HOZIR ochiq smena: kim kassada, qachondan beri, javonda qancha naqd
    /// bo'lishi kerak. Raqam <c>CashShiftService</c> dan olinadi — bu yerda
    /// ikkinchi ta'rif yaratilmaydi.
    /// </summary>
    [Fact]
    public async Task Ochiq_smena_kutilgan_naqdni_korsatadi()
    {
        await using var db = await NewCashDayDbAsync("shift");
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var shiftId = await SeedShiftAsync(db, cashierId, openingFloat: 50_000m);

        var studentId = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(studentId, "Smenali O'quvchi", "2-A"));
        await db.SaveChangesAsync();

        await AddPaymentRowAsync(db, shiftId, cashierId, studentId, 120_000m, PaymentMethod.Cash);
        await AddPaymentRowAsync(db, shiftId, cashierId, studentId, 80_000m, PaymentMethod.Card);

        var result = await QueriesFor(db).DayAsync(AppClock.Today);

        var shift = Assert.Single(result.OpenShifts);
        Assert.Equal(shiftId, shift.ShiftId);
        Assert.Equal(50_000m, shift.OpeningFloat);
        Assert.Equal(120_000m, shift.CashSoFar);
        // 50 000 (ochilish) + 120 000 (naqd) — kartaning 80 000 i SANALMAYDI
        // (SPEC §8.1 Q13: u bankka tushadi).
        Assert.Equal(170_000m, shift.ExpectedCashSoFar);
        Assert.Equal(80_000m, shift.NonCashSoFar);
        Assert.Equal(2, shift.PaymentsCount);
    }

    // =====================================================================
    //  3. Kalendar
    // =====================================================================

    /// <summary>
    /// Kalendar oyning HAR kunini beradi (harakatsizini ham) va qoldiqni
    /// kundan kunga ko'chiradi — grid'da teshik qolmasin.
    /// </summary>
    [Fact]
    public async Task Kalendar_har_kunni_beradi_va_qoldiq_kundan_kunga_kochadi()
    {
        await using var db = await NewCashDayDbAsync("calendar");
        var ledger = new LedgerService(db);
        var actorId = await SeedUserAsync(db, Roles.Admin);

        // Oydan OLDINGI qoldiq.
        await CashPaymentAsync(ledger, actorId, new DateOnly(2003, 8, 31), 200_000m, PaymentMethod.Cash);

        await CashPaymentAsync(ledger, actorId, new DateOnly(2003, 9, 3), 500_000m, PaymentMethod.Cash);
        await ExpenseAsync(db, ledger, actorId, new DateOnly(2003, 9, 10), "rent", 150_000m, PaymentMethod.Cash);

        var month = await QueriesFor(db).MonthAsync(new DateOnly(2003, 9, 17));

        Assert.Equal(new DateOnly(2003, 9, 1), month.Month);
        Assert.Equal(30, month.Days.Count);
        Assert.Equal(200_000m, month.Opening);
        Assert.Equal(500_000m, month.Inflow);
        Assert.Equal(150_000m, month.Outflow);
        Assert.Equal(350_000m, month.Net);
        Assert.Equal(550_000m, month.Closing);

        var first = month.Days[0];
        Assert.False(first.HasMovement);
        Assert.Equal(0m, first.Net);
        Assert.Equal(200_000m, first.Closing);

        var third = month.Days.Single(d => d.Date == new DateOnly(2003, 9, 3));
        Assert.True(third.HasMovement);
        Assert.Equal(500_000m, third.Inflow);
        Assert.Equal(500_000m, third.Net);
        Assert.Equal(700_000m, third.Closing);

        var tenth = month.Days.Single(d => d.Date == new DateOnly(2003, 9, 10));
        Assert.Equal(150_000m, tenth.Outflow);
        Assert.Equal(-150_000m, tenth.Net);
        Assert.Equal(550_000m, tenth.Closing);

        // Oxirgi kunning qoldig'i = oyning yopilish qoldig'i.
        Assert.Equal(month.Closing, month.Days[^1].Closing);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static CashDayQueries QueriesFor(AppDbContext db) => new(db, new CashShiftService(db));

    /// <summary>Toza, migratsiya qo'llangan baza — shablondan nusxa (~100 ms).</summary>
    private async Task<AppDbContext> NewCashDayDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("cashday_" + prefix);
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

    private static async Task<Guid> SeedShiftAsync(AppDbContext db, string cashierId, decimal openingFloat)
    {
        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = openingFloat,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.Add(shift);
        await db.SaveChangesAsync();
        return shift.Id;
    }

    private static async Task<Guid> CategoryIdAsync(AppDbContext db, string code) =>
        await db.FeeCategories.AsNoTracking().Where(c => c.Code == code).Select(c => c.Id).SingleAsync();

    /// <summary>
    /// To'lov: jurnal partiyasi (debet <c>cash</c>/<c>bank</c>, kredit
    /// <c>receivable</c>) va — agar <paramref name="db"/> berilsa — <c>payments</c>
    /// qatori bilan taqsimotlari. <c>PaymentService</c> ning jurnal yozuvi
    /// AYNAN shu shaklda (PaymentService.cs, "6. Jurnal: AYNAN ikki qator").
    /// </summary>
    /// <returns>(to'lov id'si, partiyaning langar satri id'si).</returns>
    private static async Task<(Guid PaymentId, long AnchorEntryId)> CashPaymentAsync(
        LedgerService ledger, string actorId, DateOnly date, decimal amount, string method,
        AppDbContext? db = null, Guid? shiftId = null, string? studentId = null,
        (Guid InvoiceId, decimal Amount)[]? allocations = null)
    {
        // `db` berilmasa — jurnal satrining o'zi yetarli: `ledger_entries.ref_id`
        // da FK yo'q (u to'lov/chiqim/hisob-fakturaga bir xil ishora qiladi),
        // ya'ni faqat pul harakati sinaladigan testlar `payments` qatorini
        // yozib o'tirmasligi mumkin.
        var paymentId = db is not null && shiftId is { } shift
            ? await AddPaymentRowAsync(
                db, shift, actorId,
                studentId ?? throw new ArgumentNullException(nameof(studentId)),
                amount, method, allocations)
            : Guid.NewGuid();

        var entries = await ledger.PostAsync(
        [
            new LedgerPosting(
                Accounts.SettlementFor(method), LedgerDirection.Debit, amount,
                LedgerRefType.Payment, paymentId, date, "Test to'lovi"),
            new LedgerPosting(
                Accounts.Receivable, LedgerDirection.Credit, amount,
                LedgerRefType.Payment, paymentId, date, "Test to'lovi"),
        ], actorId);

        return (paymentId, entries[0].Id);
    }

    /// <summary>
    /// <c>payments</c> qatori (+ taqsimotlari). Chek raqami smena ichida
    /// uzluksiz bo'lishi kerak (SPEC §4.2), shuning uchun mavjud eng
    /// kattasidan davom etadi.
    /// </summary>
    private static async Task<Guid> AddPaymentRowAsync(
        AppDbContext db, Guid shiftId, string cashierId, string studentId, decimal amount, string method,
        (Guid InvoiceId, decimal Amount)[]? allocations = null)
    {
        var lastReceipt = await db.Payments
            .Where(p => p.CashShiftId == shiftId)
            .MaxAsync(p => (long?)p.ReceiptNo) ?? 0;

        var payment = new Payment
        {
            ReceiptNo = lastReceipt + 1,
            StudentId = studentId,
            Amount = amount,
            Method = method,
            CashShiftId = shiftId,
            CashierId = cashierId,
            ReceivedAt = AppClock.NowInstant,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        foreach (var (invoiceId, allocated) in allocations ?? [])
            db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id,
                InvoiceId = invoiceId,
                Amount = allocated,
            });

        if (allocations is { Length: > 0 }) await db.SaveChangesAsync();
        return payment.Id;
    }

    /// <summary>
    /// Chiqim: <c>expenses</c> qatori va jurnal partiyasi (debet
    /// <c>expense:*</c>, kredit <c>cash</c>/<c>bank</c>) — <c>ExpenseService</c>
    /// ning yozuvi bilan bir xil shaklda.
    /// </summary>
    private static async Task<Guid> ExpenseAsync(
        AppDbContext db, LedgerService ledger, string actorId, DateOnly date,
        string category, decimal amount, string method)
    {
        var expense = new Expense
        {
            OnDate = date,
            Category = category,
            Amount = amount,
            Note = "Test chiqimi",
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();

        await ledger.PostAsync(
        [
            new LedgerPosting(
                Accounts.ExpenseFor(category), LedgerDirection.Debit, amount,
                LedgerRefType.Expense, expense.Id, date, $"Chiqim: {category}"),
            new LedgerPosting(
                Accounts.SettlementFor(method), LedgerDirection.Credit, amount,
                LedgerRefType.Expense, expense.Id, date, $"Chiqim: {category}"),
        ], actorId);

        return expense.Id;
    }

    private static Student NewStudent(string id, string fullName, string className) => new()
    {
        Id = id,
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2015-01-01",
        Address = "Toshkent",
        Gender = "female",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = className,
        EnrollmentDate = "2003-09-01",
    };

    private static Invoice NewInvoice(
        string studentId, Guid categoryId, DateOnly periodMonth, decimal amount) => new()
        {
            StudentId = studentId,
            CategoryId = categoryId,
            PeriodMonth = periodMonth,
            Amount = amount,
            Discount = 0m,
            DueOn = periodMonth.AddDays(9),
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };
}
