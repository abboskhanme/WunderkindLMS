using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;
using Xunit.Abstractions;

namespace SchoolLms.Tests;

/// <summary>
/// Moliya hisobotlari (P1-13): qarzdorlar, P&amp;L, pul oqimi, yig'ilish darajasi.
///
/// <para>
/// <b>Testlar ikki guruhga bo'lingan va bu ataylab.</b>
/// </para>
/// <list type="number">
///   <item><b>RUXSAT va uchidan-uchiga (HTTP)</b> — umumiy bazada, har test
///     o'z ma'lumotini takrorlanmas sinf nomi bilan ajratadi.</item>
///   <item><b>ARIFMETIKA va UNUMDORLIK</b> — har biri O'ZINING toza bazasida
///     (<c>CreateDatabaseAsync</c>). Sababi: <c>collection-rate</c> butun
///     tarixni yig'adi, ya'ni boshqa testning bitta hisob-fakturasi ham
///     yig'indini o'zgartiradi. Umumiy bazada bunday testni yozib bo'lmaydi —
///     u bugun yashil, ertaga (qo'shni vazifa test qo'shganda) qizil bo'lardi.
///     Toza baza shablondan ~100 ms da nusxalanadi.</item>
/// </list>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class FinanceReportsTests(ApiFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    /// <summary>
    /// Shu klass yaratgan bazalarning ulanish satrlari — test tugagach
    /// hovuzlari yopiladi. Tozalanmasa ulanishlar ochiq qolib, konteynerdagi
    /// <c>max_connections</c> tugaydi va KEYINGI klasslar <c>53300</c> bilan
    /// yiqiladi (<c>AllocationTests</c> dagi izoh bilan bir xil sabab).
    /// </summary>
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));

        return Task.CompletedTask;
    }

    private const string Debtors = "/api/admin/finance/debtors";
    private const string Pnl = "/api/admin/finance/pnl";
    private const string CashFlow = "/api/admin/finance/cashflow";
    private const string CollectionRate = "/api/admin/finance/collection-rate";
    private const string ArrearsPivot = "/api/admin/finance/arrears-pivot";
    // F13.05 — o'sha ruxsat darvozasidan o'tadi (`ArrearsPivot` ni ichkaridan
    // chaqiradi), shuning uchun RUXSAT testlarida ham `AllReports` qatorida.
    private const string ArrearsPivotExport = "/api/admin/finance/arrears-pivot/export";

    private static readonly string[] AllReports =
        [Debtors, Pnl, CashFlow, CollectionRate, ArrearsPivot, ArrearsPivotExport];

    // =====================================================================
    //  1. RUXSAT — SPEC §4.3
    // =====================================================================

    /// <summary>
    /// <b>P1-13 ning asosiy ruxsat mezoni.</b> Kassir bu hisobotlarning
    /// BIRORTASIGA kira olmaydi (SPEC §4.3, "See variance report across
    /// cashiers — ⛔"). Kassir to'lov qabul qiladi; butun maktabning qarzi,
    /// foydasi va pul oqimi uning ishi emas.
    /// </summary>
    [Theory]
    [InlineData(Debtors)]
    [InlineData(Pnl)]
    [InlineData(CashFlow)]
    [InlineData(CollectionRate)]
    [InlineData(ArrearsPivot)]
    public async Task Kassir_moliya_hisobotlariga_kira_olmaydi_403(string url)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// O'qituvchi va xodim ham yopiq: §4.3 jadvalida ular uchun ustun yo'q,
    /// <see cref="FinanceMatrix"/> esa qoidasi yo'q rolni RAD ETADI.
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Oqituvchi_va_xodim_ham_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "finance");

        foreach (var url in AllReports)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>Token'siz so'rov — 401 (403 emas): kim so'rayotgani umuman noma'lum.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        foreach (var url in AllReports)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>Admin va direktor — ha (SPEC §4.3, <c>ViewBillingReports</c>).</summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_kira_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        foreach (var url in AllReports)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode,
                $"{url} → {(int)response.StatusCode} {response.StatusCode}");
        }

        // Holat kodi yetarli emas: javob SHAKLI ham kutilganidek bo'lsin
        // (JSON nomlari — frontend shularga bog'lanadi).
        using var pnl = JsonDocument.Parse(
            await (await client.GetAsync(Pnl)).Content.ReadAsStringAsync());
        Assert.True(pnl.RootElement.TryGetProperty("revenueTotal", out _));
        Assert.True(pnl.RootElement.TryGetProperty("expenseTotal", out _));
        Assert.True(pnl.RootElement.TryGetProperty("net", out _));

        using var cashflow = JsonDocument.Parse(
            await (await client.GetAsync(CashFlow)).Content.ReadAsStringAsync());
        // 12 oylik sukut davri: `cash` va `bank` uchun 12 tadan qator.
        var accounts = cashflow.RootElement.GetProperty("accounts").EnumerateArray().ToList();
        Assert.Equal(2, accounts.Count);
        Assert.All(accounts, a => Assert.Equal(12, a.GetProperty("months").GetArrayLength()));
    }

    /// <summary>Davr teskari berilsa — 400, bo'sh ro'yxat yoki 500 emas.</summary>
    [Fact]
    public async Task Teskari_davr_400_qaytaradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{Pnl}?from=2026-05-01&to=2026-04-30");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Davr oxiri", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Pul oqimida davr 10 yildan oshsa — 400. Chegara bo'lmasa, bitta
    /// so'rov minglab oy qatorini quraverardi.
    /// </summary>
    [Fact]
    public async Task Juda_uzun_davr_400_qaytaradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{CashFlow}?from=2000-01-01&to=2026-12-31");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Davr juda uzun", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // =====================================================================
    //  2. Uchidan-uchiga: HTTP orqali qarzdorlar
    // =====================================================================

    /// <summary>
    /// To'liq yo'l: baza → EF → controller → JSON. Ma'lumot umumiy bazada,
    /// shuning uchun TAKRORLANMAS sinf nomi bilan ajratilgan va so'rov
    /// <c>?className=</c> bilan filtrlanadi — qo'shni testlar aralashmaydi.
    /// </summary>
    [Fact]
    public async Task Qarzdorlar_HTTP_orqali_hisoblangan_qarzni_qaytaradi()
    {
        var className = "P1-13-" + Guid.NewGuid().ToString("N")[..8];
        var studentId = Guid.NewGuid().ToString();

        await fixture.Api.WithDbAsync(async db =>
        {
            var (cashierId, shiftId) = await SeedCashDeskAsync(db);
            var tuition = await CategoryIdAsync(db, "tuition");

            db.Students.Add(NewStudent(studentId, "Qarzdor O'quvchi", className));
            var invoice = NewInvoice(studentId, tuition, new DateOnly(2025, 9, 1), 1_000_000m);
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();

            await PayAsync(db, studentId, cashierId, shiftId, 400_000m, [(invoice.Id, 400_000m)]);
        });

        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await client.GetAsync($"{Debtors}?className={className}");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(json.RootElement.EnumerateArray().ToList());

        Assert.Equal(studentId, row.GetProperty("studentId").GetString());
        // 1 000 000 hisoblandi, 400 000 to'landi → 600 000 qarz.
        Assert.Equal(600_000m, row.GetProperty("debt").GetDecimal());
        Assert.Equal("2025-09-01", row.GetProperty("oldestUnpaidMonth").GetString());

        var category = Assert.Single(row.GetProperty("byCategory").EnumerateArray().ToList());
        Assert.Equal("tuition", category.GetProperty("categoryCode").GetString());
        Assert.Equal(600_000m, category.GetProperty("debt").GetDecimal());
    }

    // =====================================================================
    //  3. Qarzdorlar — arifmetika (toza bazada)
    // =====================================================================

    /// <summary>
    /// Qarz uchta narsadan yig'iladi va HAR BIRI alohida qopqon:
    /// chegirma ayriladi, <c>void</c> hisob-faktura umuman kirmaydi,
    /// STORNO qilingan to'lov "to'langan" bo'lib qolmaydi.
    /// Toifalar kesimidagi yig'indi esa umumiy qarzga TENG bo'lishi shart.
    /// </summary>
    [Fact]
    public async Task Qarz_chegirma_void_va_stornoni_hisobga_oladi()
    {
        await using var db = await NewBillingDbAsync("debtors");
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);

        var tuition = await CategoryIdAsync(db, "tuition");
        var bus = await CategoryIdAsync(db, "bus");
        var dormitory = await CategoryIdAsync(db, "dormitory");
        var meals = await CategoryIdAsync(db, "meals");

        var debtor = Guid.NewGuid().ToString();
        var solvent = Guid.NewGuid().ToString();

        // `students.balance` ustuni P1-21 da BUTUNLAY o'chirildi: qarzni
        // "bema'ni saqlangan qiymat" bilan buzib bo'lmaydi, chunki saqlanadigan
        // qiymatning o'zi yo'q. Hisobot faqat `invoices` va
        // `payment_allocations` dan hisoblaydi (docs/TASKS.md §1.4).
        var debtorStudent = NewStudent(debtor, "Qarzdor Alisher", "5-A");
        db.Students.Add(debtorStudent);
        db.Students.Add(NewStudent(solvent, "Qarzsiz Nodira", "5-A"));

        // Chegirmali: 1 000 000 − 200 000 = 800 000 to'lanadi.
        var tuitionInvoice = NewInvoice(debtor, tuition, new DateOnly(2025, 9, 1), 1_000_000m, 200_000m);
        var busInvoice = NewInvoice(debtor, bus, new DateOnly(2025, 9, 1), 300_000m);
        var dormInvoice = NewInvoice(debtor, dormitory, new DateOnly(2025, 10, 1), 500_000m);
        // Xato hisoblangan oy — bekor qilingan, qarzga KIRMAYDI.
        var voidInvoice = NewInvoice(debtor, meals, new DateOnly(2025, 9, 1), 400_000m);
        voidInvoice.Status = InvoiceStatus.Void;
        var solventInvoice = NewInvoice(solvent, tuition, new DateOnly(2025, 9, 1), 700_000m);

        db.Invoices.AddRange(tuitionInvoice, busInvoice, dormInvoice, voidInvoice, solventInvoice);
        await db.SaveChangesAsync();

        await PayAsync(db, debtor, cashierId, shiftId, 300_000m, [(tuitionInvoice.Id, 300_000m)]);
        await PayAsync(db, debtor, cashierId, shiftId, 500_000m, [(dormInvoice.Id, 500_000m)]);
        await PayAsync(db, solvent, cashierId, shiftId, 700_000m, [(solventInvoice.Id, 700_000m)]);

        // Avtobus uchun to'langan, keyin STORNO qilingan to'lov. Taqsimot
        // o'chmaydi (jadval o'zgarmas), shuning uchun hisobot uni O'ZI
        // chiqarib tashlashi kerak — ikkala qatorni ham.
        var reversed = await PayAsync(db, debtor, cashierId, shiftId, 250_000m, [(busInvoice.Id, 250_000m)]);
        await PayAsync(db, debtor, cashierId, shiftId, 250_000m, [(busInvoice.Id, 250_000m)],
            reversalOf: reversed);

        var rows = await new FinanceReportQueries(db).DebtorsAsync(new DebtorReportQuery());

        // Qarzsiz o'quvchi ro'yxatga TUSHMAYDI.
        var row = Assert.Single(rows);
        Assert.Equal(debtor, row.StudentId);

        // (1 000 000 − 200 000 − 300 000) + (300 000 − 0) + (500 000 − 500 000)
        Assert.Equal(800_000m, row.Debt);
        Assert.Equal(row.Debt, row.ByCategory.Sum(c => c.Debt));

        Assert.Equal(500_000m, row.ByCategory.Single(c => c.CategoryCode == "tuition").Debt);
        Assert.Equal(300_000m, row.ByCategory.Single(c => c.CategoryCode == "bus").Debt);
        // To'liq to'langan toifa yoyilmada ko'rinmaydi (0 — shovqin).
        Assert.DoesNotContain(row.ByCategory, c => c.CategoryCode == "dormitory");
        // Bekor qilingan hisob-faktura toifasi ham yo'q.
        Assert.DoesNotContain(row.ByCategory, c => c.CategoryCode == "meals");

        Assert.Equal(new DateOnly(2025, 9, 1), row.OldestUnpaidMonth);
        // due_on = 10-kun, `overdue_after_day − payment_due_day` = 5 kun muhlat.
        Assert.Equal(
            AppClock.Today.DayNumber - new DateOnly(2025, 9, 15).DayNumber,
            row.DaysOverdue);
    }

    /// <summary>
    /// Sinf filtri va <c>minDebt</c>: ikkovi ham UI'dagi eng ko'p ishlatiladigan
    /// filtr va ikkovi ham bazada ishlashi kerak, xotirada emas.
    /// </summary>
    [Fact]
    public async Task Sinf_va_minDebt_filtrlari_ishlaydi()
    {
        await using var db = await NewBillingDbAsync("debtorfilter");
        var tuition = await CategoryIdAsync(db, "tuition");

        var big = Guid.NewGuid().ToString();
        var small = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(big, "Katta Qarz", "7-A"));
        db.Students.Add(NewStudent(small, "Kichik Qarz", "8-B"));
        db.Invoices.Add(NewInvoice(big, tuition, new DateOnly(2025, 9, 1), 900_000m));
        db.Invoices.Add(NewInvoice(small, tuition, new DateOnly(2025, 9, 1), 100_000m));
        await db.SaveChangesAsync();

        var queries = new FinanceReportQueries(db);

        var byClass = await queries.DebtorsAsync(new DebtorReportQuery(ClassName: "8-B"));
        Assert.Equal(small, Assert.Single(byClass).StudentId);

        var byMin = await queries.DebtorsAsync(new DebtorReportQuery(MinDebt: 500_000m));
        Assert.Equal(big, Assert.Single(byMin).StudentId);

        // Sukut bo'yicha ikkalasi ham qarzdor, kattasi yuqorida.
        var all = await queries.DebtorsAsync(new DebtorReportQuery());
        Assert.Equal(new[] { big, small }, all.Select(r => r.StudentId).ToArray());
    }

    /// <summary>
    /// <b>Oy filtri (§2.2 F2.02).</b> "Sentyabr qarzi" — sentyabr
    /// HISOB-FAKTURALARINING qoldig'i: boshqa oyning qarzi ham, boshqa oyning
    /// to'lovi ham unga aralashmaydi. Storno esa oy ichida ham ishlaydi —
    /// bekor qilingan to'lov qarzni yopmaydi.
    ///
    /// <para>
    /// Oylar yig'indisi jami qarzga TENG bo'lishi shart: aks holda ikki ekran
    /// (oy tanlangan va tanlanmagan) bir savolga har xil javob berardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Oy_filtri_faqat_shu_oyning_qoldigini_beradi()
    {
        await using var db = await NewBillingDbAsync("debtormonth");
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);
        var tuition = await CategoryIdAsync(db, "tuition");
        var bus = await CategoryIdAsync(db, "bus");

        var september = new DateOnly(2025, 9, 1);
        var october = new DateOnly(2025, 10, 1);

        var alisher = Guid.NewGuid().ToString();
        var nodira = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(alisher, "Sentyabr Qarzdori", "6-A"));
        db.Students.Add(NewStudent(nodira, "Oktyabr Qarzdori", "6-A"));

        // Alisher: sentyabrda 1 000 000 (300 000 to'ladi) + avtobus 200 000,
        // oktyabrda esa to'liq to'laydi.
        var sepTuition = NewInvoice(alisher, tuition, september, 1_000_000m);
        var sepBus = NewInvoice(alisher, bus, september, 200_000m);
        var octTuition = NewInvoice(alisher, tuition, october, 1_000_000m);
        // Nodira: sentyabrda qarzi yo'q, oktyabrda 500 000.
        var octNodira = NewInvoice(nodira, tuition, october, 500_000m);
        db.Invoices.AddRange(sepTuition, sepBus, octTuition, octNodira);
        await db.SaveChangesAsync();

        await PayAsync(db, alisher, cashierId, shiftId, 300_000m, [(sepTuition.Id, 300_000m)]);
        await PayAsync(db, alisher, cashierId, shiftId, 1_000_000m, [(octTuition.Id, 1_000_000m)]);

        // Avtobus uchun to'lov keyin STORNO qilindi — sentyabr qarzi qoladi.
        var reversed = await PayAsync(db, alisher, cashierId, shiftId, 200_000m, [(sepBus.Id, 200_000m)]);
        await PayAsync(db, alisher, cashierId, shiftId, 200_000m, [(sepBus.Id, 200_000m)],
            reversalOf: reversed);

        var queries = new FinanceReportQueries(db);

        var sepRows = await queries.DebtorsAsync(new DebtorReportQuery(Month: september));
        var sep = Assert.Single(sepRows);
        Assert.Equal(alisher, sep.StudentId);
        // (1 000 000 − 300 000) + 200 000 — oktyabr umuman qatnashmaydi.
        Assert.Equal(900_000m, sep.Debt);
        Assert.Equal(sep.Debt, sep.ByCategory.Sum(c => c.Debt));
        Assert.Equal(700_000m, sep.ByCategory.Single(c => c.CategoryCode == "tuition").Debt);
        Assert.Equal(200_000m, sep.ByCategory.Single(c => c.CategoryCode == "bus").Debt);
        Assert.Equal(september, sep.OldestUnpaidMonth);

        // Oktyabrda Alisher to'lagan — ro'yxatda faqat Nodira qoladi.
        var octRows = await queries.DebtorsAsync(new DebtorReportQuery(Month: october));
        var oct = Assert.Single(octRows);
        Assert.Equal(nodira, oct.StudentId);
        Assert.Equal(500_000m, oct.Debt);

        // Oyning KUNI ahamiyatsiz — oyning o'rtasi ham o'sha oy.
        var midMonth = await queries.DebtorsAsync(new DebtorReportQuery(Month: new DateOnly(2025, 9, 17)));
        Assert.Equal(900_000m, Assert.Single(midMonth).Debt);

        // Oysiz so'rov — o'sha ikki oyning yig'indisi.
        var all = await queries.DebtorsAsync(new DebtorReportQuery());
        Assert.Equal(
            sepRows.Sum(r => r.Debt) + octRows.Sum(r => r.Debt),
            all.Sum(r => r.Debt));
    }

    /// <summary>
    /// Noto'g'ri oy formati — 400. Jimgina e'tiborsiz qoldirilsa, ekran butun
    /// tarixning qarzini "sentyabr qarzi" deb ko'rsatib turardi.
    /// </summary>
    [Fact]
    public async Task Notogri_oy_formati_400_qaytaradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{Debtors}?month=sentabr");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Oy formati", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // =====================================================================
    //  4. P&L — daromad − chiqim
    // =====================================================================

    /// <summary>
    /// <b>P1-13 ning asosiy arifmetika mezoni.</b> Kutilgan raqam TESTDA
    /// MUSTAQIL hisoblanadi: quyidagi beshta o'zgarmas — jurnalga yozilgan
    /// summalarning O'ZI, va kutilgan foyda ularning oddiy ayirmasi.
    /// Test so'rovni TAKRORLAMAYDI (guruhlash, prefiks, debet/kredit
    /// mantig'i bu yerda qayta yozilmagan) — u faqat natijani tekshiradi.
    /// </summary>
    [Fact]
    public async Task Pnl_daromad_minus_xarajat_mustaqil_hisoblangan_raqamga_teng()
    {
        const decimal tuitionAccrual = 1_000_000m;
        const decimal busAccrual = 300_000m;
        const decimal busCorrection = 100_000m;   // daromad hisobiga DEBET — kamaytiradi
        const decimal salary = 450_000m;
        const decimal utilities = 120_000m;

        // Mustaqil hisob — qo'lda, qog'ozdagidek.
        const decimal expectedRevenue = tuitionAccrual + busAccrual - busCorrection;
        const decimal expectedExpense = salary + utilities;
        const decimal expectedNet = expectedRevenue - expectedExpense;

        await using var db = await NewBillingDbAsync("pnl");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var ledger = new LedgerService(db);

        var march = new DateOnly(2025, 3, 1);
        var april = new DateOnly(2025, 4, 10);

        // Partiyaning hamma satri BIR XIL RefId ga ega bo'lishi shart
        // (LedgerService: bitta chaqiruv = bitta biznes hodisasi).
        var tuitionRef = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, tuitionAccrual, LedgerRefType.Invoice, tuitionRef, march),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, tuitionAccrual, LedgerRefType.Invoice, tuitionRef, march),
        ], actorId);

        var busRef = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, busAccrual, LedgerRefType.Invoice, busRef, march),
            new LedgerPosting(Accounts.RevenueBus, LedgerDirection.Credit, busAccrual, LedgerRefType.Invoice, busRef, march),
        ], actorId);

        // Qisman tuzatish: daromadga DEBET — ya'ni daromadni kamaytiradi.
        var fixRef = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.RevenueBus, LedgerDirection.Debit, busCorrection, LedgerRefType.Invoice, fixRef, march),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, busCorrection, LedgerRefType.Invoice, fixRef, march),
        ], actorId);

        var salaryRef = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.ExpenseSalary, LedgerDirection.Debit, salary, LedgerRefType.Salary, salaryRef, april),
            new LedgerPosting(Accounts.Bank, LedgerDirection.Credit, salary, LedgerRefType.Salary, salaryRef, april),
        ], actorId);

        var utilRef = Guid.NewGuid();
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.ExpenseOther, LedgerDirection.Debit, utilities, LedgerRefType.Expense, utilRef, april),
            new LedgerPosting(Accounts.Cash, LedgerDirection.Credit, utilities, LedgerRefType.Expense, utilRef, april),
        ], actorId);

        // Davrdan TASHQARIDAGI yozuv — natijaga kirmasligi kerak.
        var outsideRef = Guid.NewGuid();
        var outsideDate = new DateOnly(2026, 1, 5);
        await ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, 777_000m, LedgerRefType.Invoice, outsideRef, outsideDate),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, 777_000m, LedgerRefType.Invoice, outsideRef, outsideDate),
        ], actorId);

        var pnl = await new FinanceReportQueries(db)
            .ProfitLossAsync(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        Assert.Equal(expectedRevenue, pnl.RevenueTotal);
        Assert.Equal(expectedExpense, pnl.ExpenseTotal);
        Assert.Equal(expectedNet, pnl.Net);

        Assert.Equal(tuitionAccrual, pnl.Revenue.Single(l => l.Account == Accounts.RevenueTuition).Amount);
        Assert.Equal(busAccrual - busCorrection, pnl.Revenue.Single(l => l.Account == Accounts.RevenueBus).Amount);
        Assert.Equal(salary, pnl.Expense.Single(l => l.Account == Accounts.ExpenseSalary).Amount);

        // Prefiks bo'yicha ajratish: pul hisoblari P&L ga UMUMAN tushmaydi.
        Assert.DoesNotContain(pnl.Revenue.Concat(pnl.Expense), l => l.Account == Accounts.Cash);
        Assert.DoesNotContain(pnl.Revenue.Concat(pnl.Expense), l => l.Account == Accounts.Receivable);
        Assert.All(pnl.Revenue, l => Assert.StartsWith(FinanceReportQueries.RevenuePrefix, l.Account));
        Assert.All(pnl.Expense, l => Assert.StartsWith(FinanceReportQueries.ExpensePrefix, l.Account));
    }

    // =====================================================================
    //  5. Cash Flow — `cash` va `bank` harakati
    // =====================================================================

    /// <summary>
    /// Pul oqimi: davrdan oldingi qoldiq boshlang'ich qoldiqqa aylanadi,
    /// harakatsiz oy ham qatorda qoladi (qoldiq ko'chadi), naqd va bank
    /// alohida sanaladi.
    /// </summary>
    [Fact]
    public async Task Cashflow_oylar_kesimida_naqd_va_bankni_ajratadi()
    {
        await using var db = await NewBillingDbAsync("cashflow");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var ledger = new LedgerService(db);

        // Davrdan OLDIN — boshlang'ich qoldiq.
        await PostCashAsync(ledger, actorId, new DateOnly(2025, 1, 15), Accounts.Cash, LedgerDirection.Debit, 500_000m);
        // Fevral: 300 000 kirdi, 120 000 chiqdi.
        await PostCashAsync(ledger, actorId, new DateOnly(2025, 2, 10), Accounts.Cash, LedgerDirection.Debit, 300_000m);
        await PostCashAsync(ledger, actorId, new DateOnly(2025, 2, 20), Accounts.Cash, LedgerDirection.Credit, 120_000m);
        // Mart: bankka 700 000.
        await PostCashAsync(ledger, actorId, new DateOnly(2025, 3, 5), Accounts.Bank, LedgerDirection.Debit, 700_000m);
        // Aprelda harakat yo'q — lekin qator BO'LISHI kerak.

        var report = await new FinanceReportQueries(db)
            .CashFlowAsync(new DateOnly(2025, 2, 1), new DateOnly(2025, 4, 30));

        var cash = report.Accounts.Single(a => a.Account == Accounts.Cash);
        Assert.Equal(500_000m, cash.Opening);
        Assert.Equal(300_000m, cash.Inflow);
        Assert.Equal(120_000m, cash.Outflow);
        Assert.Equal(680_000m, cash.Closing);

        Assert.Equal(3, cash.Months.Count);
        Assert.Equal(
            new[] { new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 1), new DateOnly(2025, 4, 1) },
            cash.Months.Select(m => m.Month).ToArray());

        var february = cash.Months[0];
        Assert.Equal(500_000m, february.Opening);
        Assert.Equal(180_000m, february.Net);
        Assert.Equal(680_000m, february.Closing);

        // Harakatsiz oy: qoldiq o'zgarmaydi, qator yo'qolmaydi.
        var april = cash.Months[2];
        Assert.Equal(0m, april.Inflow);
        Assert.Equal(0m, april.Outflow);
        Assert.Equal(680_000m, april.Closing);

        var bank = report.Accounts.Single(a => a.Account == Accounts.Bank);
        Assert.Equal(0m, bank.Opening);
        Assert.Equal(700_000m, bank.Closing);
        Assert.Equal(700_000m, bank.Months[1].Inflow);

        // Umumiy satr — ikkalasining yig'indisi.
        Assert.Equal(500_000m, report.Opening);
        Assert.Equal(1_000_000m, report.Inflow);
        Assert.Equal(120_000m, report.Outflow);
        Assert.Equal(1_380_000m, report.Closing);
    }

    // =====================================================================
    //  6. Yig'ilish darajasi
    // =====================================================================

    /// <summary>
    /// Oylar kesimida hisoblangan va yig'ilgan summa. Bekor qilingan
    /// hisob-faktura oyi UMUMAN paydo bo'lmaydi — aks holda "0% yig'ildi"
    /// degan yolg'on qator chiqardi.
    /// </summary>
    [Fact]
    public async Task Yigilish_darajasi_oylar_kesimida_hisoblanadi()
    {
        await using var db = await NewBillingDbAsync("collection");
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);
        var tuition = await CategoryIdAsync(db, "tuition");
        var bus = await CategoryIdAsync(db, "bus");
        var meals = await CategoryIdAsync(db, "meals");

        var studentId = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(studentId, "Sinov O'quvchi", "6-A"));

        var september = new DateOnly(2025, 9, 1);
        var october = new DateOnly(2025, 10, 1);

        var sepTuition = NewInvoice(studentId, tuition, september, 1_000_000m, 100_000m); // 900 000
        var sepBus = NewInvoice(studentId, bus, september, 300_000m);                     // 300 000
        var octTuition = NewInvoice(studentId, tuition, october, 1_000_000m);             // 1 000 000
        var voided = NewInvoice(studentId, meals, new DateOnly(2025, 11, 1), 400_000m);
        voided.Status = InvoiceStatus.Void;

        db.Invoices.AddRange(sepTuition, sepBus, octTuition, voided);
        await db.SaveChangesAsync();

        await PayAsync(db, studentId, cashierId, shiftId, 750_000m,
            [(sepTuition.Id, 450_000m), (sepBus.Id, 300_000m)]);

        var rows = await new FinanceReportQueries(db).CollectionRateAsync();

        Assert.Equal(2, rows.Count);

        Assert.Equal(september, rows[0].PeriodMonth);
        Assert.Equal(1_200_000m, rows[0].Accrued);
        Assert.Equal(750_000m, rows[0].Collected);
        // 750 000 / 1 200 000 = 62.5 %
        Assert.Equal(62.50m, rows[0].CollectionRate);

        Assert.Equal(october, rows[1].PeriodMonth);
        Assert.Equal(1_000_000m, rows[1].Accrued);
        Assert.Equal(0m, rows[1].Collected);
        Assert.Equal(0m, rows[1].CollectionRate);

        // Bekor qilingan oy umuman yo'q.
        Assert.DoesNotContain(rows, r => r.PeriodMonth == new DateOnly(2025, 11, 1));
    }

    // =====================================================================
    //  7. Unumdorlik — SPEC §7 (500 o'quvchi × 10 oy, har biri < 500 ms)
    // =====================================================================

    /// <summary>
    /// SPEC §7 talabi: to'rttala hisobot 500 o'quvchi × 10 oy ma'lumotda
    /// 500 ms dan tez javob bersin.
    ///
    /// <para>
    /// Hajm: 500 o'quvchi × 10 oy × 3 toifa = 15 000 hisob-faktura,
    /// 4 000 to'lov, 8 000 taqsimot, 38 000 jurnal yozuvi.
    /// </para>
    /// <para>
    /// O'lchov <b>ikki marta</b> olinadi: birinchisi "sovuq" (EF so'rovni
    /// birinchi marta kompilyatsiya qiladi — bu jarayon umri davomida BIR
    /// MARTA bo'ladi), ikkinchisi barqaror holat. Mezon barqaror holatga
    /// qo'yilgan, lekin ikkala raqam ham chiqishga yoziladi — sekinlashuvni
    /// keyin o'lchovsiz muhokama qilmaslik uchun.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tortala_hisobot_500_oquvchi_10_oy_malumotda_togri_va_500ms_dan_tez()
    {
        await using var db = await NewBillingDbAsync("perf");

        var seeded = await MeasureAsync(() => SeedLargeDatasetAsync(db));
        output.WriteLine($"seed (65 000 qator) {seeded,8:F0} ms");

        var queries = new FinanceReportQueries(db);
        var from = new DateOnly(2025, 9, 1);
        var to = new DateOnly(2026, 6, 30);

        // ---- Avval TO'G'RILIK: tezlik o'zi hech narsani isbotlamaydi ----
        // Har o'quvchi: 10 oy × (1 000 000 + 300 000 + 500 000) hisoblandi,
        // 8 oy × (1 000 000 + 300 000) to'landi.
        const decimal accruedPerStudent = 10 * (1_000_000m + 300_000m + 500_000m);
        const decimal paidPerStudent = 8 * (1_000_000m + 300_000m);
        const decimal expectedDebt = accruedPerStudent - paidPerStudent;

        var debtors = await queries.DebtorsAsync(new DebtorReportQuery());
        Assert.Equal(500, debtors.Count);
        Assert.All(debtors, r => Assert.Equal(expectedDebt, r.Debt));
        Assert.All(debtors, r => Assert.Equal(r.Debt, r.ByCategory.Sum(c => c.Debt)));

        // ---- Keyin TEZLIK ----
        var probes = new (string Name, Func<Task> Run)[]
        {
            ("debtors", async () => await queries.DebtorsAsync(new DebtorReportQuery())),
            ("pnl", async () => await queries.ProfitLossAsync(from, to)),
            ("cashflow", async () => await queries.CashFlowAsync(from, to)),
            ("collection-rate", async () => await queries.CollectionRateAsync()),
        };

        foreach (var (name, run) in probes)
        {
            var cold = await MeasureAsync(run);
            var warm = await MeasureAsync(run);

            output.WriteLine($"{name,-16} sovuq {cold,7:F1} ms · barqaror {warm,7:F1} ms");
            Assert.True(warm < 500,
                $"{name}: {warm:F1} ms — SPEC §7 chegarasi 500 ms (sovuq o'lchov {cold:F1} ms).");
        }
    }

    // =====================================================================
    //  8. Oyma-oy qarzdorlik (arrears pivot)
    // =====================================================================

    /// <summary>
    /// Jadvalning butun ma'nosi shu testda: <b>bo'sh katak</b> (o'qimagan oy)
    /// va <b>nol katak</b> (to'lab bo'lingan oy) — boshqa-boshqa narsa, va
    /// storno to'lov katakni "to'langan" qilib qo'ymaydi.
    ///
    /// <para>
    /// Yana bitta invariant tekshiriladi: qator yakuni — kataklar yig'indisi,
    /// ustun yakuni esa faqat KO'RINADIGAN qatorlardan. Ekranda qo'shilmaydigan
    /// ikki raqam turishi — hisobotdagi eng tez seziladigan xato.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Arrears_bosh_katak_nol_katak_va_stornoni_ajratadi()
    {
        await using var db = await NewBillingDbAsync("arrears");
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);

        var tuition = await CategoryIdAsync(db, "tuition");
        var meals = await CategoryIdAsync(db, "meals");

        var debtor = Guid.NewGuid().ToString();
        var solvent = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(debtor, "Qarzdor Alisher", "5-A"));
        db.Students.Add(NewStudent(solvent, "Qarzsiz Nodira", "5-B"));

        // Sentabr: chegirmali 800 000, undan 300 000 to'langan → qoldiq 500 000.
        var sep = NewInvoice(debtor, tuition, new DateOnly(2025, 9, 1), 1_000_000m, 200_000m);
        // Oktabr: to'liq to'langan → katak BOR, qoldig'i 0.
        var oct = NewInvoice(debtor, tuition, new DateOnly(2025, 10, 1), 500_000m);
        // Noyabr: to'langan, keyin STORNO → katak to'lanmagan bo'lib qoladi.
        var nov = NewInvoice(debtor, tuition, new DateOnly(2025, 11, 1), 300_000m);
        // Dekabr: xato hisoblangan oy (void) → katak UMUMAN yo'q.
        var dec = NewInvoice(debtor, meals, new DateOnly(2025, 12, 1), 400_000m);
        dec.Status = InvoiceStatus.Void;
        // Qarzsiz o'quvchining bitta oyi — to'liq to'langan.
        var solventSep = NewInvoice(solvent, tuition, new DateOnly(2025, 9, 1), 700_000m);

        db.Invoices.AddRange(sep, oct, nov, dec, solventSep);
        await db.SaveChangesAsync();

        await PayAsync(db, debtor, cashierId, shiftId, 300_000m, [(sep.Id, 300_000m)]);
        await PayAsync(db, debtor, cashierId, shiftId, 500_000m, [(oct.Id, 500_000m)]);
        await PayAsync(db, solvent, cashierId, shiftId, 700_000m, [(solventSep.Id, 700_000m)]);

        var reversed = await PayAsync(db, debtor, cashierId, shiftId, 300_000m, [(nov.Id, 300_000m)]);
        await PayAsync(db, debtor, cashierId, shiftId, 300_000m, [(nov.Id, 300_000m)],
            reversalOf: reversed);

        var pivot = await new FinanceReportQueries(db).ArrearsPivotAsync(new ArrearsPivotQuery(
            new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 1)));

        // Ustunlar — davrning HAMMA oyi, ma'lumot bor-yo'qligidan qat'i nazar.
        Assert.Equal(new[] { "2025-09", "2025-10", "2025-11", "2025-12" }, pivot.Months.ToArray());

        var row = pivot.Rows.Single(r => r.StudentId == debtor);

        // Sentabr: 1 000 000 − 200 000 hisoblangan, 300 000 to'langan.
        Assert.Equal(new ArrearsCellDto(800_000m, 300_000m, 500_000m), row.Cells["2025-09"]);
        // Oktabr: katak BOR va qoldig'i nol — "o'qidi, to'ladi".
        Assert.Equal(new ArrearsCellDto(500_000m, 500_000m, 0m), row.Cells["2025-10"]);
        // Noyabr: storno qilingan to'lov hisobga OLINMAYDI.
        Assert.Equal(new ArrearsCellDto(300_000m, 0m, 300_000m), row.Cells["2025-11"]);
        // Dekabr: bekor qilingan hisob-faktura — katak YO'Q (nol emas).
        Assert.False(row.Cells.ContainsKey("2025-12"));

        // Qator yakuni — kataklar yig'indisi.
        Assert.Equal(1_600_000m, row.Total.Amount);
        Assert.Equal(800_000m, row.Total.Paid);
        Assert.Equal(800_000m, row.Total.ToBePaid);

        // Ustun yakuni ikkala o'quvchini ham qamraydi.
        Assert.Equal(new ArrearsCellDto(1_500_000m, 1_000_000m, 500_000m), pivot.Footer["2025-09"]);
        Assert.Equal(new ArrearsCellDto(2_300_000m, 1_500_000m, 800_000m), pivot.Total);

        // Jadval yakuni — ustun yakunlarining yig'indisi.
        Assert.Equal(pivot.Total.Amount, pivot.Footer.Values.Sum(c => c.Amount));
        Assert.Equal(pivot.Total.ToBePaid, pivot.Footer.Values.Sum(c => c.ToBePaid));
    }

    /// <summary>
    /// Filtrlar: <c>debtorsOnly</c> qarzi yo'q qatorni butunlay olib tashlaydi
    /// (va u ustun yakuniga ham qo'shilmaydi), sinf filtri esa bazada ishlaydi.
    /// </summary>
    [Fact]
    public async Task Arrears_debtorsOnly_va_sinf_filtri_yakunni_ham_toraytiradi()
    {
        await using var db = await NewBillingDbAsync("arrearsfilter");
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);
        var tuition = await CategoryIdAsync(db, "tuition");

        var debtor = Guid.NewGuid().ToString();
        var solvent = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(debtor, "Qarzdor Alisher", "5-A"));
        db.Students.Add(NewStudent(solvent, "Qarzsiz Nodira", "5-B"));

        var debtorSep = NewInvoice(debtor, tuition, new DateOnly(2025, 9, 1), 900_000m);
        var solventSep = NewInvoice(solvent, tuition, new DateOnly(2025, 9, 1), 700_000m);
        db.Invoices.AddRange(debtorSep, solventSep);
        await db.SaveChangesAsync();

        await PayAsync(db, solvent, cashierId, shiftId, 700_000m, [(solventSep.Id, 700_000m)]);

        var queries = new FinanceReportQueries(db);
        var month = new DateOnly(2025, 9, 1);

        var all = await queries.ArrearsPivotAsync(new ArrearsPivotQuery(month, month));
        Assert.Equal(2, all.Rows.Count);
        Assert.Equal(1_600_000m, all.Total.Amount);

        var onlyDebtors = await queries.ArrearsPivotAsync(
            new ArrearsPivotQuery(month, month, DebtorsOnly: true));
        Assert.Equal(debtor, Assert.Single(onlyDebtors.Rows).StudentId);
        // Yakun ham faqat ko'rinadigan qatordan: 700 000 unga QO'SHILMAYDI.
        Assert.Equal(900_000m, onlyDebtors.Total.Amount);
        Assert.Equal(900_000m, onlyDebtors.Footer["2025-09"].Amount);

        var byClass = await queries.ArrearsPivotAsync(
            new ArrearsPivotQuery(month, month, ClassName: "5-B"));
        Assert.Equal(solvent, Assert.Single(byClass.Rows).StudentId);
    }

    /// <summary>
    /// HTTP yuzasi: JSON kalitlari (frontend shularga bog'lanadi), davr
    /// chegarasi va oy formatining xatosi. Uchovi ham 400 bo'lishi kerak —
    /// bo'sh jadval yoki 500 emas.
    /// </summary>
    [Fact]
    public async Task Arrears_HTTP_javob_shakli_va_xato_holatlari()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var ok = await client.GetAsync($"{ArrearsPivot}?fromMonth=2025-09&toMonth=2025-10");
        Assert.True(ok.IsSuccessStatusCode, $"{(int)ok.StatusCode} {ok.StatusCode}");

        using var body = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("months").GetArrayLength());
        Assert.True(body.RootElement.TryGetProperty("rows", out _));
        Assert.True(body.RootElement.TryGetProperty("footer", out _));
        Assert.True(body.RootElement.GetProperty("total").TryGetProperty("toBePaid", out _));

        var tooLong = await client.GetAsync($"{ArrearsPivot}?fromMonth=2024-01&toMonth=2025-12");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("Davr juda uzun", await tooLong.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var badMonth = await client.GetAsync($"{ArrearsPivot}?fromMonth=sentabr");
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
        Assert.Contains("Oy formati", await badMonth.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var reversed = await client.GetAsync($"{ArrearsPivot}?fromMonth=2025-10&toMonth=2025-09");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
    }

    // =====================================================================
    //  9. Oyma-oy qarzdorlik — finance-parity.md §2.13 gaplari
    //     (F13.01, F13.02, F13.03, F13.05, F13.06)
    // =====================================================================

    /// <summary>F13.03 — ota-ona telefoni qator bilan birga qaytadi.</summary>
    [Fact]
    public async Task Arrears_ota_ona_telefonini_qaytaradi()
    {
        await using var db = await NewBillingDbAsync("arrearsphone");
        var tuition = await CategoryIdAsync(db, "tuition");

        var studentId = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(studentId, "Telefonli O'quvchi", "5-A"));
        db.Invoices.Add(NewInvoice(studentId, tuition, new DateOnly(2025, 9, 1), 500_000m));
        await db.SaveChangesAsync();

        var pivot = await new FinanceReportQueries(db).ArrearsPivotAsync(new ArrearsPivotQuery(
            new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 1)));

        // `NewStudent` sukut telefoni — pastdagi Yordamchi qism.
        Assert.Equal("+998901112233", Assert.Single(pivot.Rows).ParentPhone);
    }

    /// <summary>
    /// F13.01 — bir nechta sinf birdaniga. <c>ClassNames</c> berilsa yagona
    /// <c>ClassName</c> dan USTUN turadi (eski chaqiruvlar buzilmasin).
    /// </summary>
    [Fact]
    public async Task Arrears_kop_tanlovli_sinf_filtri_yagona_sinfdan_ustun_turadi()
    {
        await using var db = await NewBillingDbAsync("arrearsclasses");
        var tuition = await CategoryIdAsync(db, "tuition");

        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        var c = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(a, "Besh A", "5-A"));
        db.Students.Add(NewStudent(b, "Besh B", "5-B"));
        db.Students.Add(NewStudent(c, "Olti A", "6-A"));

        var month = new DateOnly(2025, 9, 1);
        db.Invoices.Add(NewInvoice(a, tuition, month, 100_000m));
        db.Invoices.Add(NewInvoice(b, tuition, month, 200_000m));
        db.Invoices.Add(NewInvoice(c, tuition, month, 300_000m));
        await db.SaveChangesAsync();

        var queries = new FinanceReportQueries(db);

        // `ClassNames` ikkita sinfni oladi — 6-A tashqarida qoladi.
        var multi = await queries.ArrearsPivotAsync(new ArrearsPivotQuery(
            month, month, ClassNames: ["5-A", "5-B"]));
        Assert.Equal(
            new[] { a, b }.OrderBy(x => x, StringComparer.Ordinal),
            multi.Rows.Select(r => r.StudentId).OrderBy(x => x, StringComparer.Ordinal));

        // Ikkovi BIRGA berilsa — `ClassNames` g'olib: yagona `ClassName` (6-A)
        // e'tiborga olinmaydi.
        var both = await queries.ArrearsPivotAsync(new ArrearsPivotQuery(
            month, month, ClassName: "6-A", ClassNames: ["5-A", "5-B"]));
        Assert.DoesNotContain(both.Rows, r => r.StudentId == c);
    }

    /// <summary>
    /// F13.02 — "toifalar bo'yicha ajratish". <b>Asosiy invariant:</b>
    /// bo'lingan qatorlar yig'indisi bo'linmagan qatorga TENG — ajratish
    /// yangi pul yaratmaydi, faqat bitta qatorni ikkiga bo'lib ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task Arrears_toifalar_boyicha_ajratish_yigindisi_bolinmagan_qatorga_teng()
    {
        await using var db = await NewBillingDbAsync("arrearssplit");
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);
        var tuition = await CategoryIdAsync(db, "tuition");
        var bus = await CategoryIdAsync(db, "bus");

        var studentId = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(studentId, "Ikki Toifali", "5-A"));
        var month = new DateOnly(2025, 9, 1);
        var tuitionInv = NewInvoice(studentId, tuition, month, 1_000_000m);
        var busInv = NewInvoice(studentId, bus, month, 300_000m);
        db.Invoices.AddRange(tuitionInv, busInv);
        await db.SaveChangesAsync();

        await PayAsync(db, studentId, cashierId, shiftId, 400_000m, [(tuitionInv.Id, 400_000m)]);

        var queries = new FinanceReportQueries(db);

        var merged = await queries.ArrearsPivotAsync(new ArrearsPivotQuery(month, month));
        var mergedRow = Assert.Single(merged.Rows);
        // Sukut bo'yicha "ajratish" o'chiq — toifa maydonlari bo'sh.
        Assert.Null(mergedRow.CategoryCode);
        Assert.Equal(1_300_000m, mergedRow.Total.Amount);
        Assert.Equal(400_000m, mergedRow.Total.Paid);

        var split = await queries.ArrearsPivotAsync(new ArrearsPivotQuery(
            month, month, SplitByCategory: true));

        Assert.Equal(2, split.Rows.Count);
        Assert.All(split.Rows, r => Assert.Equal(studentId, r.StudentId));
        Assert.Equal(
            new[] { "bus", "tuition" },
            split.Rows.Select(r => r.CategoryCode).OrderBy(x => x, StringComparer.Ordinal));

        // Invariant: bo'lingan qatorlar yig'indisi — bo'linmagan qatorga TENG.
        Assert.Equal(mergedRow.Total.Amount, split.Rows.Sum(r => r.Total.Amount));
        Assert.Equal(mergedRow.Total.Paid, split.Rows.Sum(r => r.Total.Paid));
        Assert.Equal(mergedRow.Total.ToBePaid, split.Rows.Sum(r => r.Total.ToBePaid));

        // Yakun (footer/jami) — "ajratish" QATOR shaklini o'zgartiradi, pulni
        // emas: ikkala so'rov ham BIR XIL jami bilan javob berishi shart.
        Assert.Equal(merged.Total, split.Total);
    }

    /// <summary>
    /// F13.06 — bitta o'quv guruhi. Faqat HOZIRGI a'zolar (<c>left_on is
    /// null</c>) qoladi; guruhdan chiqqan va guruhga umuman kirmagan
    /// o'quvchi ikkovi ham tashqarida.
    /// </summary>
    [Fact]
    public async Task Arrears_guruh_filtri_faqat_hozirgi_azolarni_qaytaradi()
    {
        await using var db = await NewBillingDbAsync("arrearsgroup");
        var tuition = await CategoryIdAsync(db, "tuition");
        // `study_groups.created_by` — real `users.id` ga FK; kim ekani bu
        // testga ahamiyatsiz, shuning uchun tayyor yordamchidan olinadi.
        var (actorId, _) = await SeedCashDeskAsync(db);

        var active = Guid.NewGuid().ToString();
        var left = Guid.NewGuid().ToString();
        var outsider = Guid.NewGuid().ToString();
        db.Students.Add(NewStudent(active, "Faol Azo", "5-A"));
        db.Students.Add(NewStudent(left, "Chiqqan Azo", "5-A"));
        db.Students.Add(NewStudent(outsider, "Guruhsiz", "5-A"));

        var subject = new Subject { Name = "Ingliz tili" };
        db.Subjects.Add(subject);
        var group = new StudyGroup
        {
            Name = "Kuchli guruh",
            SubjectId = subject.Id,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.Add(group);
        await db.SaveChangesAsync();

        db.StudyGroupMembers.AddRange(
            new StudyGroupMember
            {
                GroupId = group.Id, SubjectId = subject.Id, StudentId = active,
                JoinedOn = new DateOnly(2025, 9, 1), CreatedBy = actorId, CreatedAt = AppClock.NowInstant,
            },
            new StudyGroupMember
            {
                GroupId = group.Id, SubjectId = subject.Id, StudentId = left,
                JoinedOn = new DateOnly(2025, 9, 1), LeftOn = new DateOnly(2025, 9, 10),
                CreatedBy = actorId, CreatedAt = AppClock.NowInstant,
            });
        await db.SaveChangesAsync();

        var month = new DateOnly(2025, 9, 1);
        db.Invoices.Add(NewInvoice(active, tuition, month, 100_000m));
        db.Invoices.Add(NewInvoice(left, tuition, month, 200_000m));
        db.Invoices.Add(NewInvoice(outsider, tuition, month, 300_000m));
        await db.SaveChangesAsync();

        var pivot = await new FinanceReportQueries(db).ArrearsPivotAsync(new ArrearsPivotQuery(
            month, month, StudyGroupId: group.Id));

        Assert.Equal(active, Assert.Single(pivot.Rows).StudentId);
    }

    /// <summary>
    /// F13.05 — xlsx eksport. To'g'ri content-type va bo'sh bo'lmagan tana;
    /// xato holatlar <c>arrears-pivot</c> bilan bir xil (400, 500 emas).
    /// </summary>
    [Fact]
    public async Task Arrears_export_xlsx_qaytaradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync(
            $"{ArrearsPivotExport}?fromMonth=2025-09&toMonth=2025-10");
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);

        var badMonth = await client.GetAsync($"{ArrearsPivotExport}?fromMonth=sentabr");
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
    }

    // =====================================================================
    //  Yordamchi qism
    // =====================================================================

    private static async Task<double> MeasureAsync(Func<Task> run)
    {
        var stopwatch = Stopwatch.StartNew();
        await run();
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Toza, migratsiya qo'llangan baza — shablondan nusxa (~100 ms).
    /// Hisobotlar butun jadvalni yig'adi, ya'ni umumiy bazada boshqa
    /// testning qatori natijani o'zgartirib yuborardi.
    /// </summary>
    private async Task<AppDbContext> NewBillingDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("finrep_" + prefix);
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
        EnrollmentDate = "2025-09-01",
    };

    private static Invoice NewInvoice(
        string studentId, Guid categoryId, DateOnly periodMonth, decimal amount, decimal discount = 0m) => new()
        {
            StudentId = studentId,
            CategoryId = categoryId,
            PeriodMonth = periodMonth,
            Amount = amount,
            Discount = discount,
            DueOn = periodMonth.AddDays(9),   // payment_due_day = 10 (seed)
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };

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

    private static Task PostCashAsync(
        LedgerService ledger, string actorId, DateOnly date, string account, string direction, decimal amount)
    {
        // Qarshi tomon — `receivable`: bu yerda muhimi pul hisobining harakati,
        // ikkinchi oyoq esa jurnal balanslashishi uchun kerak.
        var counterDirection = direction == LedgerDirection.Debit
            ? LedgerDirection.Credit
            : LedgerDirection.Debit;
        var refId = Guid.NewGuid();

        return ledger.PostAsync(
        [
            new LedgerPosting(account, direction, amount, LedgerRefType.Payment, refId, date),
            new LedgerPosting(Accounts.Receivable, counterDirection, amount, LedgerRefType.Payment, refId, date),
        ], actorId);
    }

    /// <summary>
    /// SPEC §7 o'lchovi uchun ma'lumot: 500 o'quvchi × 10 oy × 3 toifa.
    ///
    /// <para>
    /// Xom SQL bilan — EF orqali 65 000 qator qo'yish testni o'nlab
    /// soniyaga cho'zardi. O'lchanadigan KOD baribir EF so'rovlari; bu yerda
    /// faqat ma'lumot tayyorlanadi. Oxirida <c>ANALYZE</c>: statistikasiz
    /// planner yangi jadvalda noto'g'ri reja tanlaydi va o'lchov prod'dagi
    /// holatni aks ettirmay qoladi.
    /// </para>
    /// </summary>
    private static async Task SeedLargeDatasetAsync(AppDbContext db)
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

        var sql = $"""
            -- 500 o'quvchi
            -- `balance` va `discount_*` ustunlari P1-21 da o'chirildi: hisobot
            -- ularga qaray olmaydi, chunki ular endi mavjud emas.
            insert into students (
                id, full_name, last_name, first_name, middle_name, birth_date, address, gender,
                parent_full_name, parent_last_name, parent_first_name, parent_middle_name,
                parent_phone, class_name, enrollment_date,
                sub_group, is_archived, archived_with_class, device_user_id)
            select 'perf-' || i, 'O''quvchi ' || i, 'Familiya', 'Ism', 'Otasi', '2015-01-01',
                   'Toshkent', 'male', 'Ota-ona ' || i, 'Familiya', 'Ism', 'Otasi',
                   '+99890' || lpad(i::text, 7, '0'), 'Sinf-' || (1 + i % 10), '2025-09-01',
                   0, false, false, ''
            from generate_series(1, 500) i;

            -- 10 oy × 3 toifa = 15 000 hisob-faktura
            insert into invoices (id, student_id, category_id, period_month, amount, discount, due_on, status, created_at)
            select gen_random_uuid(), s.id, c.category_id,
                   (date '2025-09-01' + (m || ' month')::interval)::date,
                   c.amount, 0,
                   (date '2025-09-01' + (m || ' month')::interval)::date + 9,
                   'open', now()
            from students s
            cross join generate_series(0, 9) m
            cross join (values
                ((select id from fee_categories where code = 'tuition'), 1000000::numeric(14,2)),
                ((select id from fee_categories where code = 'bus'), 300000::numeric(14,2)),
                ((select id from fee_categories where code = 'dormitory'), 500000::numeric(14,2))
            ) as c(category_id, amount);

            -- 10 oydan 8 tasi to'langan: o'qish + avtobus (yotoqxona qarz bo'lib qoladi)
            insert into payments (id, receipt_no, student_id, amount, method, cash_shift_id, cashier_id, received_at)
            select gen_random_uuid(), row_number() over (), s.id, 1300000, 'cash',
                   '{shift.Id}'::uuid, '{cashierId}',
                   (date '2025-09-15' + (m || ' month')::interval)
            from students s
            cross join generate_series(0, 7) m;

            -- Har to'lov ikkita hisob-fakturaga taqsimlanadi (o'qish 1 000 000 + avtobus 300 000)
            insert into payment_allocations (id, payment_id, invoice_id, amount)
            select gen_random_uuid(), p.id, i.id, i.amount
            from payments p
            join invoices i
              on i.student_id = p.student_id
             and i.period_month = (date_trunc('month', p.received_at at time zone 'UTC'))::date
             and i.amount in (1000000, 300000);

            -- Hisob-faktura yozuvlari: debet receivable / kredit revenue:* (30 000 qator)
            insert into ledger_entries (entry_date, account, direction, amount, ref_type, ref_id, created_by, created_at)
            select i.period_month, 'receivable', 'debit', i.amount - i.discount, 'invoice', i.id, '{cashierId}', now()
            from invoices i
            union all
            select i.period_month,
                   case when i.amount = 1000000 then 'revenue:tuition'
                        when i.amount = 300000 then 'revenue:bus'
                        else 'revenue:dormitory' end,
                   'credit', i.amount - i.discount, 'invoice', i.id, '{cashierId}', now()
            from invoices i;

            -- To'lov yozuvlari: debet cash / kredit receivable (8 000 qator)
            insert into ledger_entries (entry_date, account, direction, amount, ref_type, ref_id, created_by, created_at)
            select (p.received_at at time zone 'UTC')::date, 'cash', 'debit', p.amount, 'payment', p.id, '{cashierId}', now()
            from payments p
            union all
            select (p.received_at at time zone 'UTC')::date, 'receivable', 'credit', p.amount, 'payment', p.id, '{cashierId}', now()
            from payments p;

            analyze students;
            analyze invoices;
            analyze payments;
            analyze payment_allocations;
            analyze ledger_entries;
            """;

        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
