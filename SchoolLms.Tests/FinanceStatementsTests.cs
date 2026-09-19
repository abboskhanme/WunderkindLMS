using System.Globalization;
using System.Net;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Moliya hisobotlarining ikkinchi avlodi (docs/modules/finance-parity.md
/// §2.4, §2.5, §2.7): yil × oy P&amp;L, katakcha ortidagi jurnal satrlari,
/// toifalar kesimidagi pul oqimi va kunlik panel.
///
/// <para>
/// <b>Asosiy mezon — IKKINCHI TA'RIF YO'Q.</b> Testlar raqamni qo'lda qayta
/// hisoblash bilan cheklanmaydi; ular yangi hisobotni ESKISI bilan
/// taqqoslaydi: matritsaning katagi <c>ProfitLossAsync</c> ning o'sha oyi
/// bilan, toifalar yig'indisi esa <c>CashFlowAsync</c> ning kirim/chiqimi
/// bilan bir xil bo'lishi shart. Ikkovi ayrilsa — demak biror joyda pul
/// ikkinchi marta ta'riflangan.
/// </para>
/// <para>
/// <b>Testlar ikki guruhda</b> (<see cref="FinanceReportsTests"/> dagi sabab
/// bilan): RUXSAT — umumiy bazada, HTTP orqali; ARIFMETIKA — har biri
/// o'zining toza bazasida, chunki hisobotlar butun jadvalni yig'adi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class FinanceStatementsTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Shu klass yaratgan bazalarning ulanish satrlari — test tugagach
    /// hovuzlari yopiladi. Tozalanmasa konteynerdagi <c>max_connections</c>
    /// tugaydi va KEYINGI klasslar <c>53300</c> bilan yiqiladi
    /// (<c>FinanceReportsTests</c> dagi izoh bilan bir xil sabab).
    /// </summary>
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));

        return Task.CompletedTask;
    }

    private const string Dashboard = "/api/admin/finance/dashboard";
    private const string Matrix = "/api/admin/finance/pnl/matrix";
    private const string LedgerLines = "/api/admin/finance/ledger/lines?account=revenue:*";
    private const string Statement = "/api/admin/finance/cashflow/statement";
    private const string CashFlowLines = "/api/admin/finance/cashflow/lines?key=advance";
    // F5.06 — `Matrix` ning o'sha ruxsat darvozasidan o'tadi (ichkaridan uni
    // chaqiradi), shuning uchun u ham RUXSAT testlarida `AllEndpoints` qatorida.
    private const string MatrixExport = "/api/admin/finance/pnl/matrix/export";
    // F7.04 va F4.05 — ikkalasi ham o'z so'rovini ICHKARIDAN chaqiradi, ya'ni
    // o'sha ruxsat darvozasidan o'tadi; shunday bo'lsa ham ro'yxatda turadi,
    // aks holda kelajakdagi o'zgarish ularni jimgina ochib yuborishi mumkin.
    private const string StatementExport = "/api/admin/finance/cashflow/export";
    private const string DashboardExport = "/api/admin/finance/dashboard/export";

    private static readonly string[] AllEndpoints =
    [
        Dashboard, Matrix, LedgerLines, Statement, CashFlowLines,
        MatrixExport, StatementExport, DashboardExport,
    ];

    // =====================================================================
    //  1. RUXSAT — SPEC §4.3
    // =====================================================================

    /// <summary>
    /// Kassir bu hisobotlarning BIRORTASIGA kira olmaydi: §4.3 dagi "See
    /// variance report across cashiers — ⛔". Yangi endpoint eski qoidadan
    /// chetda qolib ketmasin.
    /// </summary>
    [Fact]
    public async Task Kassir_yangi_hisobotlarga_kira_olmaydi_403()
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

    /// <summary>Chegaralar 400 bilan qaytadi: xato so'rov 500 ham, bo'sh javob ham emas.</summary>
    [Fact]
    public async Task Notogri_sorovlar_400_qaytaradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Yil ishonchli oraliqdan tashqarida.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Matrix}?year=20226")).StatusCode);

        // Teskari davr.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"{Statement}?from=2026-05-01&to=2026-04-30")).StatusCode);

        // 12 oydan uzun davr — toifalar kesimi satr darajasida ishlaydi.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"{Statement}?from=2024-01-01&to=2026-12-31")).StatusCode);

        // Hisob kodi noto'g'ri.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"{Statement}?account=revenue:tuition")).StatusCode);

        // `account` majburiy.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/admin/finance/ledger/lines")).StatusCode);
    }

    // =====================================================================
    //  2. Yil × oy P&L — katak = o'sha oyning P&L satri
    // =====================================================================

    /// <summary>
    /// <b>Matritsaning asosiy mezoni.</b> Har katak AYNAN
    /// <c>ProfitLossAsync</c> ning o'sha oyi bo'yicha satriga teng; qator
    /// yakuni kataklarning, ustun yakuni qatorlarning yig'indisi.
    ///
    /// <para>
    /// Daromadga qo'yilgan DEBET (tuzatish) katakni kamaytiradi — belgi
    /// qoidasi ham o'zgarmaydi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Matritsa_katagi_oylik_PnL_ga_teng()
    {
        await using var db = await NewDbAsync("matrix");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var ledger = new LedgerService(db);
        var queries = new FinanceReportQueries(db);

        await PostAsync(ledger, actorId, new DateOnly(2019, 3, 5),
            Accounts.Receivable, Accounts.RevenueTuition, 1_000_000m, LedgerRefType.Invoice);
        await PostAsync(ledger, actorId, new DateOnly(2019, 3, 20),
            Accounts.Receivable, Accounts.RevenueBus, 300_000m, LedgerRefType.Invoice);

        // Mart daromadining tuzatilishi: daromad hisobiga DEBET.
        await PostAsync(ledger, actorId, new DateOnly(2019, 3, 25),
            Accounts.RevenueBus, Accounts.Receivable, 100_000m, LedgerRefType.Invoice);

        await PostAsync(ledger, actorId, new DateOnly(2019, 4, 10),
            Accounts.ExpenseSalary, Accounts.Bank, 450_000m, LedgerRefType.Salary);
        await PostAsync(ledger, actorId, new DateOnly(2019, 4, 12),
            Accounts.ExpenseUtilities, Accounts.Cash, 120_000m, LedgerRefType.Expense);

        await PostAsync(ledger, actorId, new DateOnly(2019, 6, 1),
            Accounts.Receivable, Accounts.RevenueTuition, 500_000m, LedgerRefType.Invoice);

        // Boshqa YILDAGI yozuv — matritsaga umuman tushmasligi kerak.
        await PostAsync(ledger, actorId, new DateOnly(2020, 1, 9),
            Accounts.Receivable, Accounts.RevenueTuition, 777_000m, LedgerRefType.Invoice);

        var matrix = await queries.ProfitLossMatrixAsync(2019);

        Assert.Equal(12, matrix.Months.Count);
        Assert.Equal("2019-01", matrix.Months[0]);
        Assert.Equal("2019-12", matrix.Months[11]);

        // ---- Har oy: matritsa = o'sha oyning P&L hisoboti ----
        for (var month = 1; month <= 12; month++)
        {
            var first = new DateOnly(2019, month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            var pnl = await queries.ProfitLossAsync(first, last);

            Assert.Equal(pnl.RevenueTotal, matrix.RevenueMonths[month - 1]);
            Assert.Equal(pnl.ExpenseTotal, matrix.ExpenseMonths[month - 1]);
            Assert.Equal(pnl.Net, matrix.NetMonths[month - 1]);

            foreach (var line in pnl.Revenue)
                Assert.Equal(line.Amount, matrix.Revenue.Single(r => r.Account == line.Account).Months[month - 1]);
            foreach (var line in pnl.Expense)
                Assert.Equal(line.Amount, matrix.Expense.Single(r => r.Account == line.Account).Months[month - 1]);
        }

        // ---- Yakunlar: qator = kataklar, ustun = qatorlar ----
        Assert.All(matrix.Revenue, r => Assert.Equal(r.Total, r.Months.Sum()));
        Assert.All(matrix.Expense, r => Assert.Equal(r.Total, r.Months.Sum()));
        Assert.Equal(matrix.RevenueTotal, matrix.Revenue.Sum(r => r.Total));
        Assert.Equal(matrix.RevenueTotal, matrix.RevenueMonths.Sum());
        Assert.Equal(matrix.ExpenseTotal, matrix.ExpenseMonths.Sum());
        Assert.Equal(matrix.NetTotal, matrix.RevenueTotal - matrix.ExpenseTotal);

        // Mustaqil hisob — qog'ozdagidek.
        Assert.Equal(1_700_000m, matrix.RevenueTotal);   // 1 000 000 + (300 000 − 100 000) + 500 000
        Assert.Equal(570_000m, matrix.ExpenseTotal);     // 450 000 + 120 000
        Assert.Equal(1_130_000m, matrix.NetTotal);

        Assert.Equal(200_000m, matrix.Revenue.Single(r => r.Account == Accounts.RevenueBus).Months[2]);
        Assert.Equal(500_000m, matrix.Revenue.Single(r => r.Account == Accounts.RevenueTuition).Months[5]);

        // Harakati bo'lmagan hisob ham qatorda turadi (ustunlar o'zgarmasin).
        Assert.Contains(matrix.Revenue, r => r.Account == Accounts.RevenueMeals && r.Total == 0m);

        // ---- Qoldiq qatorlari pul oqimidan ----
        var cash = await queries.CashFlowAsync(new DateOnly(2019, 1, 1), new DateOnly(2019, 12, 31));
        Assert.Equal(cash.Opening, matrix.OpeningBalance);
        Assert.Equal(cash.Closing, matrix.ClosingBalance);
        Assert.Equal(matrix.OpeningBalance, matrix.StartBalance[0]);
        Assert.Equal(matrix.ClosingBalance, matrix.EndBalance[11]);
        for (var i = 0; i < 11; i++)
            Assert.Equal(matrix.EndBalance[i], matrix.StartBalance[i + 1]);

        // Aprelda 570 000 chiqdi — qoldiq shuncha kamayadi.
        Assert.Equal(-570_000m, matrix.EndBalance[3] - matrix.StartBalance[3]);
    }

    /// <summary>
    /// F5.06 — YIL × OY .xlsx eksporti, QOLDIQ QATORLARI bilan. <b>Mezon —
    /// IKKINCHI TA'RIF YO'Q:</b> faylning har katagi <c>ProfitLossMatrixAsync</c>
    /// bilan (demak ekranda ko'ringan bilan) AYNAN bir xil, "Oy boshida" /
    /// "Oy oxirida" qatorlari ham ichida — <c>ClassAnalyticsController</c>
    /// testlaridagi bilan bir xil naqsh: controller HTTP'siz chaqiriladi.
    /// </summary>
    [Fact]
    public async Task Matrix_export_xlsx_qoldiq_qatorlari_bilan_ekrandagi_raqamga_teng()
    {
        await using var db = await NewDbAsync("matrix-export");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var ledger = new LedgerService(db);

        await PostAsync(ledger, actorId, new DateOnly(2021, 3, 5),
            Accounts.Receivable, Accounts.RevenueTuition, 1_000_000m, LedgerRefType.Invoice);
        await PostAsync(ledger, actorId, new DateOnly(2021, 4, 10),
            Accounts.ExpenseSalary, Accounts.Bank, 450_000m, LedgerRefType.Salary);

        var matrix = await new FinanceReportQueries(db).ProfitLossMatrixAsync(2021);

        var response = await new FinanceStatementsController(db).ProfitLossMatrixExport(2021);
        var file = Assert.IsType<FileContentResult>(response);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.ContentType);

        var rows = ReadXlsxRows(file.FileContents);

        // Sarlavha: "Yo'nalish", "Toifa", 12 oy, "Jami".
        var header = rows[0];
        Assert.Equal("Jami", header[^1]);
        Assert.Equal(matrix.Months, header[2..^1]);

        var revenueRow = rows.Single(r => r[1] == MoneyFlowQueries.LabelFor(Accounts.RevenueTuition));
        var revenueLine = matrix.Revenue.Single(l => l.Account == Accounts.RevenueTuition);
        Assert.Equal("Daromad", revenueRow[0]);
        Assert.Equal(revenueLine.Months, ParseAmounts(revenueRow[2..^1]));
        Assert.Equal(revenueLine.Total, ParseAmount(revenueRow[^1]));

        var revenueTotalRow = rows.Single(r => r[0] == "Jami" && r[1] == "Daromad");
        Assert.Equal(matrix.RevenueMonths, ParseAmounts(revenueTotalRow[2..^1]));
        Assert.Equal(matrix.RevenueTotal, ParseAmount(revenueTotalRow[^1]));

        var expenseTotalRow = rows.Single(r => r[0] == "Jami" && r[1] == "Xarajat");
        Assert.Equal(matrix.ExpenseMonths, ParseAmounts(expenseTotalRow[2..^1]));
        Assert.Equal(matrix.ExpenseTotal, ParseAmount(expenseTotalRow[^1]));

        var netRow = rows.Single(r => r[0] == "Jami" && r[1] == "Sof natija");
        Assert.Equal(matrix.NetMonths, ParseAmounts(netRow[2..^1]));
        Assert.Equal(matrix.NetTotal, ParseAmount(netRow[^1]));

        // F5.02 — qoldiq qatorlari, "Oy oxirida" oxirgi (qalin) qator.
        var startRow = rows.Single(r => r[0] == "Qoldiq" && r[1] == "Oy boshida");
        Assert.Equal(matrix.StartBalance, ParseAmounts(startRow[2..^1]));
        Assert.Equal(matrix.OpeningBalance, ParseAmount(startRow[^1]));

        var endRow = rows[^1];
        Assert.Equal("Qoldiq", endRow[0]);
        Assert.Equal("Oy oxirida", endRow[1]);
        Assert.Equal(matrix.EndBalance, ParseAmounts(endRow[2..^1]));
        Assert.Equal(matrix.ClosingBalance, ParseAmount(endRow[^1]));

        // Yil ishonchli oraliqdan tashqarida — export ham 400 (500 emas).
        var invalid = await new FinanceStatementsController(db).ProfitLossMatrixExport(20226);
        Assert.IsType<BadRequestObjectResult>(invalid);
    }

    /// <summary>
    /// <b>Drill-down mezoni: katak = satrlar yig'indisi.</b> Katakcha
    /// bosilganda ko'rinadigan jurnal satrlari aynan o'sha katakni hosil
    /// qiladi — na ko'p, na kam.
    /// </summary>
    [Fact]
    public async Task Katak_ortidagi_satrlar_yigindisi_katakka_teng()
    {
        await using var db = await NewDbAsync("drill");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var ledger = new LedgerService(db);
        var queries = new FinanceReportQueries(db);

        var studentId = await SeedStudentAsync(db, "Drill Bolasi", "9-A");
        var tuition = await CategoryIdAsync(db, "tuition");

        // Ikkinchi o'quvchi: bitta oyda bitta toifaga BITTA hisob-faktura
        // (unikal indeks), shuning uchun ikkinchi satr boshqa bolaniki.
        var classmateId = await SeedStudentAsync(db, "Drill Sinfdoshi", "9-A");

        var march = new DateOnly(2019, 3, 1);
        var invoice = await AccrueAsync(db, ledger, actorId, studentId, tuition,
            Accounts.RevenueTuition, march, 1_000_000m);
        await AccrueAsync(db, ledger, actorId, classmateId, tuition,
            Accounts.RevenueTuition, march, 400_000m);

        // Daromadni kamaytiruvchi tuzatish — o'sha katakda MINUS bilan.
        await PostAsync(ledger, actorId, new DateOnly(2019, 3, 28),
            Accounts.RevenueTuition, Accounts.Receivable, 150_000m, LedgerRefType.Invoice, invoice.Id);

        await PostAsync(ledger, actorId, new DateOnly(2019, 4, 3),
            Accounts.ExpenseRent, Accounts.Bank, 800_000m, LedgerRefType.Expense);

        var matrix = await queries.ProfitLossMatrixAsync(2019);
        var cell = matrix.Revenue.Single(r => r.Account == Accounts.RevenueTuition).Months[2];

        var lines = await queries.LedgerLinesAsync(
            Accounts.RevenueTuition, march, march.AddMonths(1).AddDays(-1));

        Assert.Equal(cell, lines.Total);
        Assert.Equal(cell, lines.Lines.Sum(l => l.Signed));
        Assert.Equal(3, lines.Count);
        Assert.False(lines.Truncated);

        // Tuzatish satri — qarshi tomonda, ya'ni MINUS.
        Assert.Contains(lines.Lines, l => l.Signed == -150_000m && l.Direction == LedgerDirection.Debit);
        // Hisob-faktura satrida o'quvchi ismi ko'rinadi (P&L da pul emas, daromad).
        Assert.Contains(lines.Lines, l => l.Person == "Drill Bolasi" && l.Signed == 1_000_000m);
        Assert.All(lines.Lines, l => Assert.Equal("Hisob-faktura", l.KindLabel));

        // Guruh so'rovi: butun yilning daromad yakuni.
        var group = await queries.LedgerLinesAsync(
            FinanceReportQueries.RevenuePrefix + "*", new DateOnly(2019, 1, 1), new DateOnly(2019, 12, 31));
        Assert.Equal(matrix.RevenueTotal, group.Total);
        Assert.Equal(group.Total, group.Lines.Sum(l => l.Signed));

        // Chiqim guruhi — belgi qoidasi teskari (debet musbat).
        var expense = await queries.LedgerLinesAsync(
            FinanceReportQueries.ExpensePrefix + "*", new DateOnly(2019, 4, 1), new DateOnly(2019, 4, 30));
        Assert.Equal(matrix.Expense.Single(r => r.Account == Accounts.ExpenseRent).Months[3], expense.Total);
        Assert.Equal(800_000m, expense.Total);
    }

    // =====================================================================
    //  3. Toifalar kesimidagi pul oqimi
    // =====================================================================

    /// <summary>
    /// <b>Pul oqimining asosiy mezoni.</b> Toifalar yig'indisi
    /// <c>CashFlowAsync</c> ning kirim va chiqimiga TENG: toifalar yangi
    /// raqam yaratmaydi, mavjudini bo'ladi.
    ///
    /// <para>
    /// Sahna: taqsimlangan to'lov (ikki toifa), taqsimlanmagan to'lov
    /// (avans), storno qilingan to'lov (o'z kunida chiqim) va naqd chiqim.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Toifalar_yigindisi_pul_oqimiga_teng()
    {
        await using var db = await NewDbAsync("statement");
        var (queries, ledger, cashierId, approverId, shiftId, studentId) = await SeedDeskAsync(db, "Toifa Bolasi");

        var tuition = await CategoryIdAsync(db, "tuition");
        var bus = await CategoryIdAsync(db, "bus");
        var today = AppClock.Today;
        var month = new DateOnly(today.Year, today.Month, 1);

        var tuitionInvoice = await AccrueAsync(db, ledger, approverId, studentId, tuition,
            Accounts.RevenueTuition, month, 1_000_000m);
        var busInvoice = await AccrueAsync(db, ledger, approverId, studentId, bus,
            Accounts.RevenueBus, month, 500_000m);

        // (1) Ikki toifaga taqsimlangan naqd to'lov.
        await PayAsync(db, ledger, cashierId, studentId, shiftId, 1_300_000m, PaymentMethod.Cash, today,
            [(tuitionInvoice.Id, 1_000_000m), (busInvoice.Id, 300_000m)]);

        // (2) Taqsimlanmagan to'lov — AVANS, karta orqali (bankka tushadi).
        await PayAsync(db, ledger, cashierId, studentId, shiftId, 500_000m, PaymentMethod.Card, today, []);

        // (3) Storno qilinadigan naqd to'lov — avtobusga.
        var (reversedId, anchor) = await PayAsync(db, ledger, cashierId, studentId, shiftId,
            200_000m, PaymentMethod.Cash, today, [(busInvoice.Id, 200_000m)]);
        await ledger.ReverseAsync(anchor, "Xato chek", approverId);

        // (4) Naqd chiqim.
        await SpendAsync(db, ledger, approverId, "utilities", Accounts.ExpenseUtilities,
            Accounts.Cash, 400_000m, today);

        var statement = await queries.CashFlowStatementAsync(month, today);
        var cash = await queries.CashFlowAsync(month, today);

        // ---- Yig'indi eski hisobotning O'ZI ----
        Assert.Equal(cash.Inflow, statement.Total.Inflow);
        Assert.Equal(cash.Outflow, statement.Total.Outflow);
        Assert.Equal(cash.Net, statement.Total.Amount);
        Assert.Equal(cash.Opening, statement.OpeningBalance);
        Assert.Equal(cash.Closing, statement.ClosingBalance);

        Assert.Equal(2_000_000m, statement.Total.Inflow);   // 1 300 000 + 500 000 + 200 000
        Assert.Equal(600_000m, statement.Total.Outflow);    // 200 000 storno + 400 000 chiqim

        // Bo'lim yakuni — qatorlarning yig'indisi; oy ustuni ham shunday.
        var rows = statement.Sections.SelectMany(s => s.Rows).ToList();
        Assert.Equal(statement.Total.Inflow, rows.Sum(r => r.Total.Inflow));
        Assert.Equal(statement.Total.Outflow, rows.Sum(r => r.Total.Outflow));
        Assert.All(statement.Sections, s =>
            Assert.Equal(s.Total.Amount, s.Rows.Sum(r => r.Total.Amount)));
        Assert.All(statement.Sections, s =>
            Assert.All(s.Rows, r => Assert.Equal(r.Total.Amount, r.Months.Sum(m => m.Amount))));

        // ---- Toifalar ----
        var income = statement.Sections.Single(s => s.Kind == CashFlowSectionKind.Income);
        var expenses = statement.Sections.Single(s => s.Kind == CashFlowSectionKind.Expense);

        Assert.Equal(1_000_000m, income.Rows.Single(r => r.Key == Accounts.RevenueTuition).Total.Amount);

        // Avtobus: 300 000 + 200 000 kirdi, 200 000 storno bo'lib chiqdi.
        var busRow = income.Rows.Single(r => r.Key == Accounts.RevenueBus);
        Assert.Equal(500_000m, busRow.Total.Inflow);
        Assert.Equal(200_000m, busRow.Total.Outflow);
        Assert.Equal(300_000m, busRow.Total.Amount);

        // Taqsimlanmagan to'lov — avans.
        Assert.Equal(500_000m, income.Rows.Single(r => r.Key == CashFlowKeys.Advance).Total.Amount);

        // Chiqim qatori MANFIY: belgi pul oqimi bo'yicha, ekranda ham shunday.
        Assert.Equal(-400_000m, expenses.Rows.Single(r => r.Key == Accounts.ExpenseUtilities).Total.Amount);

        // "Boshqa harakat" bo'lmasligi kerak — hamma satr toifasini topdi.
        Assert.DoesNotContain(statement.Sections, s => s.Kind == CashFlowSectionKind.Other);

        // ---- Hisob filtri: naqd + bank = jami ----
        var onlyCash = await queries.CashFlowStatementAsync(month, today, Accounts.Cash);
        var onlyBank = await queries.CashFlowStatementAsync(month, today, Accounts.Bank);
        Assert.Equal(statement.Total.Inflow, onlyCash.Total.Inflow + onlyBank.Total.Inflow);
        Assert.Equal(statement.Total.Outflow, onlyCash.Total.Outflow + onlyBank.Total.Outflow);
        // Avans karta bilan kelgan — u faqat bank tomonda.
        Assert.Contains(onlyBank.Sections.SelectMany(s => s.Rows), r => r.Key == CashFlowKeys.Advance);
        Assert.DoesNotContain(onlyCash.Sections.SelectMany(s => s.Rows), r => r.Key == CashFlowKeys.Advance);

        // ---- Drill-down: katak = satrlar ----
        var busLines = await queries.CashFlowLinesAsync(Accounts.RevenueBus, null, month, today);
        Assert.Equal(busRow.Total, busLines.Total);
        Assert.Equal(3, busLines.Count);
        Assert.Equal(busRow.Total.Amount, busLines.Lines.Sum(l => l.Signed));
        // Taqsimlangan to'lovning BO'LAGI ko'rinadi, to'liq summasi emas.
        Assert.Contains(busLines.Lines, l => l.Amount == 300_000m && !l.IsReversal);
        Assert.Contains(busLines.Lines, l => l.IsReversal && l.Signed == -200_000m);
        Assert.DoesNotContain(busLines.Lines, l => l.Amount == 1_300_000m);

        var advanceLines = await queries.CashFlowLinesAsync(CashFlowKeys.Advance, null, month, today);
        var advance = Assert.Single(advanceLines.Lines);
        Assert.Equal(500_000m, advance.Amount);
        Assert.Equal(PaymentMethod.Card, advance.Method);

        var expenseLines = await queries.CashFlowLinesAsync(Accounts.ExpenseUtilities, null, month, today);
        Assert.Equal(-400_000m, expenseLines.Total.Amount);
        Assert.Equal("Kommunal xarajat", Assert.Single(expenseLines.Lines).Title);

        // Storno to'lovi bazada qoladi — hisobot uni yashirmaydi.
        Assert.True(await db.Payments.AsNoTracking().AnyAsync(p => p.Id == reversedId));
    }

    /// <summary>
    /// F7.04 — pul oqimi .xlsx, IKKI varaq. Mezon o'sha: faylda ekrandagi
    /// raqamning aynan o'zi turadi, ya'ni <c>CashFlowStatementAsync</c>
    /// javobidan nusxa. "Oylar" varag'ining oxirgi qatori — davr yakuni,
    /// "Toifalar" niki — sof oqim.
    /// </summary>
    [Fact]
    public async Task Cashflow_export_ikki_varaqli_xlsx_ekrandagi_raqamga_teng()
    {
        await using var db = await NewDbAsync("cashflow-export");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var ledger = new LedgerService(db);

        var today = AppClock.Today;
        var from = new DateOnly(today.Year, today.Month, 1);

        await PostAsync(ledger, actorId, from,
            Accounts.Cash, Accounts.RevenueTuition, 1_000_000m, LedgerRefType.Payment);
        await PostAsync(ledger, actorId, today,
            Accounts.ExpenseUtilities, Accounts.Bank, 400_000m, LedgerRefType.Expense);

        var statement = await new FinanceReportQueries(db).CashFlowStatementAsync(from, today);

        var response = await new FinanceStatementsController(db).CashFlowExport(from, today, null);
        var file = Assert.IsType<FileContentResult>(response);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.ContentType);
        Assert.Equal(["Oylar", "Toifalar"], SheetNames(file.FileContents));

        // ---- 1-varaq: oylar ----
        var months = ReadXlsxSheet(file.FileContents, "Oylar");
        Assert.Equal("Oy", months[0][0]);
        Assert.Equal(statement.Months.Count + 2, months.Count);   // sarlavha + oylar + "Jami"

        var firstMonth = months[1];
        Assert.Equal(statement.Months[0], firstMonth[0]);
        Assert.Equal(statement.Opening[0], ParseAmount(firstMonth[1]));
        Assert.Equal(statement.MonthTotals[0].Inflow, ParseAmount(firstMonth[2]));
        Assert.Equal(statement.MonthTotals[0].Outflow, ParseAmount(firstMonth[3]));
        Assert.Equal(statement.MonthTotals[0].Amount, ParseAmount(firstMonth[4]));
        Assert.Equal(statement.Closing[0], ParseAmount(firstMonth[5]));

        var monthTotals = months[^1];
        Assert.Equal("Jami", monthTotals[0]);
        Assert.Equal(statement.OpeningBalance, ParseAmount(monthTotals[1]));
        Assert.Equal(statement.Total.Inflow, ParseAmount(monthTotals[2]));
        Assert.Equal(statement.Total.Outflow, ParseAmount(monthTotals[3]));
        Assert.Equal(statement.Total.Amount, ParseAmount(monthTotals[4]));
        Assert.Equal(statement.ClosingBalance, ParseAmount(monthTotals[5]));

        // ---- 2-varaq: toifalar ----
        var categories = ReadXlsxSheet(file.FileContents, "Toifalar");
        Assert.Equal(statement.Months, categories[0][2..^1]);

        foreach (var section in statement.Sections)
        {
            foreach (var row in section.Rows)
            {
                var line = categories.Single(r => r[0] == section.Label && r[1] == row.Label);
                Assert.Equal(row.Months.Select(c => c.Amount), ParseAmounts(line[2..^1]));
                Assert.Equal(row.Total.Amount, ParseAmount(line[^1]));
            }

            var sectionTotal = categories.Single(r => r[0] == section.Label && r[1] == "Jami");
            Assert.Equal(section.Total.Amount, ParseAmount(sectionTotal[^1]));
        }

        var net = categories[^1];
        Assert.Equal("Sof oqim", net[0]);
        Assert.Equal(statement.MonthTotals.Select(c => c.Amount), ParseAmounts(net[2..^1]));
        Assert.Equal(statement.Total.Amount, ParseAmount(net[^1]));

        // Teskari davr — eksport ham 400 (500 emas).
        var invalid = await new FinanceStatementsController(db).CashFlowExport(today, from, null);
        Assert.IsType<BadRequestObjectResult>(invalid);
    }

    // =====================================================================
    //  4. "Moliya hisobotlari" paneli
    // =====================================================================

    /// <summary>
    /// Chegirmalar tahlili: jami, qo'llanishlar soni, o'rtacha va TOIFA
    /// kesimi. Bekor qilingan (`void`) hisob-faktura sanalmaydi — uning
    /// chegirmasi amalda yo'q.
    /// </summary>
    [Fact]
    public async Task Panel_chegirmalar_tahlilini_toifa_kesimida_beradi()
    {
        await using var db = await NewDbAsync("discounts");
        var (queries, ledger, _, approverId, _, studentId) = await SeedDeskAsync(db, "Chegirma Bolasi");

        // Bitta o'quvchiga bir oyda bir toifadan BITTA hisob-faktura
        // (`ix_invoices_student_id_category_id_period_month`) — shuning uchun
        // kesim uchun toifalar har xil olinadi.
        var tuition = await CategoryIdAsync(db, "tuition");
        var meals = await CategoryIdAsync(db, "meals");
        var bus = await CategoryIdAsync(db, "bus");
        var other = await CategoryIdAsync(db, "other");
        var today = AppClock.Today;
        var from = new DateOnly(today.Year, today.Month, 1);

        var a = await AccrueAsync(db, ledger, approverId, studentId, tuition,
            Accounts.RevenueTuition, from, 2_000_000m);
        var b = await AccrueAsync(db, ledger, approverId, studentId, meals,
            Accounts.RevenueMeals, from, 1_000_000m);
        var c = await AccrueAsync(db, ledger, approverId, studentId, bus,
            Accounts.RevenueBus, from, 400_000m);
        var voided = await AccrueAsync(db, ledger, approverId, studentId, other,
            Accounts.RevenueOther, from, 400_000m);

        a.Discount = 300_000m;
        b.Discount = 100_000m;
        c.Discount = 100_000m;
        // `ck_invoices_discount`: chegirma summadan katta bo'lolmaydi — baza
        // pulni shu yerda ham qo'riqlaydi.
        voided.Discount = 200_000m;
        voided.Status = InvoiceStatus.Void;   // sanalmasligi kerak
        await db.SaveChangesAsync();

        var panel = await queries.DashboardAsync(from, today);

        Assert.Equal(500_000m, panel.Discounts.Total);
        Assert.Equal(3, panel.Discounts.AppliedCount);
        Assert.Equal(decimal.Round(500_000m / 3, 2), panel.Discounts.Average);

        var rows = panel.Discounts.Rows;
        Assert.Equal(3, rows.Count);
        Assert.Equal("tuition", rows[0].CategoryCode);   // eng kattasi birinchi
        Assert.Equal(300_000m, rows[0].Total);
        Assert.Equal(1, rows[0].InvoiceCount);
        Assert.Equal(60m, rows[0].Share);
        Assert.DoesNotContain(rows, r => r.CategoryCode == "other");   // void sanalmadi
    }

    /// <summary>
    /// Panel: KPI'lar pul oqimining o'sha raqamlari, foiz esa oldingi
    /// (shu uzunlikdagi) davrga nisbatan. Kunlik grafik harakatsiz kunni ham
    /// ustun sifatida qoldiradi, to'lov usullari kesimi esa faqat
    /// TO'LOVLARNI sanaydi — chiqimda usul saqlanmaydi.
    /// </summary>
    [Fact]
    public async Task Panel_KPI_va_kesimlari_pul_oqimiga_mos()
    {
        await using var db = await NewDbAsync("dashboard");
        var (queries, ledger, cashierId, approverId, shiftId, studentId) = await SeedDeskAsync(db, "Panel Bolasi");

        var tuition = await CategoryIdAsync(db, "tuition");
        var today = AppClock.Today;
        var from = new DateOnly(today.Year, today.Month, 1);
        var previousTo = from.AddDays(-1);

        var invoice = await AccrueAsync(db, ledger, approverId, studentId, tuition,
            Accounts.RevenueTuition, from, 2_000_000m);

        // Oldingi davr (o'tgan oyning oxirgi kuni — davr uzunligidan qat'i nazar ichida).
        await PayAsync(db, ledger, cashierId, studentId, shiftId, 800_000m, PaymentMethod.Cash, previousTo,
            [(invoice.Id, 800_000m)]);

        // Joriy davr: 1 200 000 kirim, 300 000 chiqim.
        await PayAsync(db, ledger, cashierId, studentId, shiftId, 1_200_000m, PaymentMethod.Transfer, today,
            [(invoice.Id, 1_200_000m)]);
        await SpendAsync(db, ledger, approverId, "supplies", Accounts.ExpenseSupplies,
            Accounts.Bank, 300_000m, today);

        var panel = await queries.DashboardAsync(from, today);
        var cash = await queries.CashFlowAsync(from, today);

        Assert.Equal(cash.Inflow, panel.Inflow.Current);
        Assert.Equal(cash.Outflow, panel.Outflow.Current);
        Assert.Equal(cash.Net, panel.Net.Current);
        Assert.Equal(cash.Opening, panel.OpeningBalance);
        Assert.Equal(cash.Closing, panel.ClosingBalance);

        Assert.Equal(1_200_000m, panel.Inflow.Current);
        Assert.Equal(800_000m, panel.Inflow.Previous);
        Assert.Equal(previousTo, panel.PreviousTo);
        Assert.Equal(50m, panel.Inflow.ChangePercent);   // (1 200 000 − 800 000) / 800 000

        // Oldingi davrda chiqim bo'lmagan — foiz YO'Q (nolga bo'lish ham, "+100%" ham yolg'on).
        Assert.Equal(0m, panel.Outflow.Previous);
        Assert.Null(panel.Outflow.ChangePercent);

        // Kunlik grafik: davrning HAR kuni.
        Assert.Equal(today.Day, panel.Days.Count);
        Assert.Equal(panel.Inflow.Current, panel.Days.Sum(d => d.Inflow));
        Assert.Equal(panel.Outflow.Current, panel.Days.Sum(d => d.Outflow));
        Assert.Equal(1_200_000m, panel.Days.Single(d => d.Date == today).Inflow);

        // Toifalar kesimi — pul oqimi hisobotining o'sha bo'limlari.
        var rows = panel.Sections.SelectMany(s => s.Rows).ToList();
        Assert.Equal(panel.Inflow.Current, rows.Sum(r => r.Total.Inflow));
        Assert.Equal(panel.Outflow.Current, rows.Sum(r => r.Total.Outflow));

        // To'lov usullari — FAQAT to'lovlar (chiqim usulsiz).
        var method = Assert.Single(panel.Methods);
        Assert.Equal(PaymentMethod.Transfer, method.Method);
        Assert.Equal("O'tkazma", method.Label);
        Assert.Equal(1_200_000m, method.Inflow);
        Assert.Equal(panel.MethodsTotal.Amount, panel.Methods.Sum(r => r.Amount));
        Assert.NotEqual(panel.Outflow.Current, panel.MethodsTotal.Outflow);

        // Usul bo'yicha drill-down ham shu raqamni beradi.
        var lines = await queries.CashFlowLinesAsync(null, PaymentMethod.Transfer, from, today);
        Assert.Equal(1_200_000m, lines.Total.Inflow);
        Assert.Equal(method.Inflow, lines.Lines.Sum(l => l.Signed));
    }

    /// <summary>
    /// F4.05 — "Moliya hisobotlari" .xlsx, BESH varaq. Ekrandagi beshala
    /// blok ham faylga tushadi va raqamlar <c>DashboardAsync</c> niki
    /// bo'ladi: KPI, kunlar, toifalar, to'lov usullari, chegirmalar.
    /// </summary>
    [Fact]
    public async Task Panel_export_besh_varaqli_xlsx_ekrandagi_raqamga_teng()
    {
        await using var db = await NewDbAsync("panel-export");
        var (queries, ledger, _, approverId, _, studentId) = await SeedDeskAsync(db, "Panel Bolasi");

        var today = AppClock.Today;
        var from = new DateOnly(today.Year, today.Month, 1);

        var tuition = await CategoryIdAsync(db, "tuition");
        var invoice = await AccrueAsync(db, ledger, approverId, studentId, tuition,
            Accounts.RevenueTuition, from, 2_000_000m);
        invoice.Discount = 200_000m;
        await db.SaveChangesAsync();

        await PostAsync(ledger, approverId, from,
            Accounts.Cash, Accounts.RevenueTuition, 1_000_000m, LedgerRefType.Payment);
        await PostAsync(ledger, approverId, today,
            Accounts.ExpenseUtilities, Accounts.Bank, 400_000m, LedgerRefType.Expense);

        var panel = await queries.DashboardAsync(from, today);

        var response = await new FinanceStatementsController(db).DashboardExport(from, today);
        var file = Assert.IsType<FileContentResult>(response);
        Assert.Equal(
            ["Umumiy", "Kunlar", "Toifalar", "To'lov usullari", "Chegirmalar"],
            SheetNames(file.FileContents));

        // ---- Umumiy: KPI va qoldiqlar ----
        var summary = ReadXlsxSheet(file.FileContents, "Umumiy");
        var inflow = summary.Single(r => r[0] == "Kirim");
        Assert.Equal(panel.Inflow.Current, ParseAmount(inflow[1]));
        Assert.Equal(panel.Inflow.Previous, ParseAmount(inflow[2]));

        var opening = summary.Single(r => r[0] == "Davr boshidagi qoldiq");
        Assert.Equal(panel.OpeningBalance, ParseAmount(opening[1]));

        var closing = summary[^1];
        Assert.Equal("Davr oxiridagi qoldiq", closing[0]);
        Assert.Equal(panel.ClosingBalance, ParseAmount(closing[1]));

        // ---- Kunlar: har kun bitta qator ----
        var days = ReadXlsxSheet(file.FileContents, "Kunlar");
        Assert.Equal(panel.Days.Count + 1, days.Count);   // sarlavha + kunlar
        Assert.Equal(
            panel.Days[0].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            days[1][0]);

        // ---- Toifalar: bo'lim yakuni bilan ----
        var categories = ReadXlsxSheet(file.FileContents, "Toifalar");
        foreach (var section in panel.Sections)
        {
            var total = categories.Single(r => r[0] == section.Label && r[1] == "Jami");
            Assert.Equal(section.Total.Amount, ParseAmount(total[^1]));
        }

        // ---- To'lov usullari: oxirgi qator — yakun ----
        var methods = ReadXlsxSheet(file.FileContents, "To'lov usullari");
        var methodTotals = methods[^1];
        Assert.Equal("Jami", methodTotals[0]);
        Assert.Equal(panel.MethodsTotal.Inflow, ParseAmount(methodTotals[1]));
        Assert.Equal(panel.MethodsTotal.Count, ParseAmount(methodTotals[4]));

        // ---- Chegirmalar: toifa kesimi va yakuni ----
        var discounts = ReadXlsxSheet(file.FileContents, "Chegirmalar");
        var discountTotals = discounts[^1];
        Assert.Equal("Jami", discountTotals[0]);
        Assert.Equal(panel.Discounts.AppliedCount, ParseAmount(discountTotals[1]));
        Assert.Equal(panel.Discounts.Total, ParseAmount(discountTotals[2]));

        // Teskari davr — eksport ham 400.
        var invalid = await new FinanceStatementsController(db).DashboardExport(today, from);
        Assert.IsType<BadRequestObjectResult>(invalid);
    }

    // =====================================================================
    //  Test ma'lumoti
    // =====================================================================

    /// <summary>
    /// F5.06 eksport testi uchun: .xlsx faylning birinchi varag'ini qator ×
    /// katak matniga aylantiradi (sarlavha ham ichida) — <c>CertificatesTests</c>
    /// dagi bilan bir xil o'qish naqshi.
    /// </summary>
    private static List<string[]> ReadXlsxRows(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, isEditable: false);
        var wbPart = doc.WorkbookPart!;
        var sheet = Assert.Single(wbPart.Workbook.Descendants<Sheet>());
        var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!.Value!);
        return [.. wsPart.Worksheet.Descendants<Row>()
            .Select(r => r.Elements<Cell>().Select(c => c.InnerText).ToArray())];
    }

    /// <summary>
    /// Ko'p varaqli .xlsx dan BITTA varaqni nomi bo'yicha o'qiydi (F7.04,
    /// F4.05 eksportlari bir necha varaqdan iborat).
    /// </summary>
    private static List<string[]> ReadXlsxSheet(byte[] bytes, string name)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, isEditable: false);
        var wbPart = doc.WorkbookPart!;
        var sheet = Assert.Single(
            wbPart.Workbook.Descendants<Sheet>().Where(x => x.Name?.Value == name));
        var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!.Value!);
        return [.. wsPart.Worksheet.Descendants<Row>()
            .Select(r => r.Elements<Cell>().Select(c => c.InnerText).ToArray())];
    }

    /// <summary>Kitobdagi varaq nomlari — tartibi bilan.</summary>
    private static List<string> SheetNames(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, isEditable: false);
        return [.. doc.WorkbookPart!.Workbook.Descendants<Sheet>().Select(x => x.Name!.Value!)];
    }

    /// <summary>Son katagining matnini pulga aylantiradi (`ExcelExport` — "0.00", invariant).</summary>
    private static decimal ParseAmount(string text) =>
        decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static List<decimal> ParseAmounts(IEnumerable<string> texts) => [.. texts.Select(ParseAmount)];

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("finstmt_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>Kassir, tasdiqlovchi (storno uchun ikkinchi shaxs), smena va o'quvchi.</summary>
    private static async Task<(FinanceReportQueries Queries, LedgerService Ledger, string CashierId,
        string ApproverId, Guid ShiftId, string StudentId)> SeedDeskAsync(AppDbContext db, string studentName)
    {
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var approverId = await SeedUserAsync(db, Roles.Admin);

        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.Add(shift);
        await db.SaveChangesAsync();

        var studentId = await SeedStudentAsync(db, studentName, "7-A");

        return (new FinanceReportQueries(db), new LedgerService(db), cashierId, approverId, shift.Id, studentId);
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

    private static async Task<string> SeedStudentAsync(AppDbContext db, string fullName, string className)
    {
        var id = $"fs-{Guid.NewGuid():N}"[..16];
        db.Students.Add(new Student
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
            EnrollmentDate = "2019-09-01",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> CategoryIdAsync(AppDbContext db, string code) =>
        await db.FeeCategories.AsNoTracking().Where(c => c.Code == code).Select(c => c.Id).SingleAsync();

    /// <summary>Balanslashgan partiya: debet → kredit (LedgerService qoidasi).</summary>
    private static async Task<long> PostAsync(
        LedgerService ledger, string actorId, DateOnly date,
        string debit, string credit, decimal amount, string refType, Guid? refId = null)
    {
        var id = refId ?? Guid.NewGuid();
        var created = await ledger.PostAsync(
        [
            new LedgerPosting(debit, LedgerDirection.Debit, amount, refType, id, date),
            new LedgerPosting(credit, LedgerDirection.Credit, amount, refType, id, date),
        ], actorId);

        return created[0].Id;
    }

    /// <summary>
    /// Hisob-faktura + uning jurnal partiyasi — <c>InvoiceService</c> ning
    /// aynan o'sha ikki qatori (debet <c>receivable</c>, kredit <c>revenue:*</c>).
    /// </summary>
    private static async Task<Invoice> AccrueAsync(
        AppDbContext db, LedgerService ledger, string actorId, string studentId, Guid categoryId,
        string revenueAccount, DateOnly periodMonth, decimal amount)
    {
        var invoice = new Invoice
        {
            StudentId = studentId,
            CategoryId = categoryId,
            PeriodMonth = new DateOnly(periodMonth.Year, periodMonth.Month, 1),
            Amount = amount,
            Discount = 0m,
            DueOn = new DateOnly(periodMonth.Year, periodMonth.Month, 1).AddDays(9),
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        await PostAsync(ledger, actorId, periodMonth,
            Accounts.Receivable, revenueAccount, amount, LedgerRefType.Invoice, invoice.Id);

        return invoice;
    }

    /// <summary>
    /// To'lov + taqsimotlar + jurnal partiyasi — <c>PaymentService</c> ning
    /// aynan o'sha ikki qatori (debet pul hisobi, kredit <c>receivable</c>).
    /// </summary>
    private static async Task<(Guid PaymentId, long AnchorEntryId)> PayAsync(
        AppDbContext db, LedgerService ledger, string cashierId, string studentId, Guid shiftId,
        decimal amount, string method, DateOnly date, (Guid InvoiceId, decimal Amount)[] allocations)
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

        foreach (var (invoiceId, allocated) in allocations)
            db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id,
                InvoiceId = invoiceId,
                Amount = allocated,
            });

        if (allocations.Length > 0) await db.SaveChangesAsync();

        var anchor = await PostAsync(ledger, cashierId, date,
            Accounts.SettlementFor(method), Accounts.Receivable, amount, LedgerRefType.Payment, payment.Id);

        return (payment.Id, anchor);
    }

    /// <summary>
    /// Chiqim + jurnal partiyasi — <c>ExpenseService</c> ning aynan o'sha ikki
    /// qatori (debet <c>expense:*</c>, kredit pul hisobi).
    /// </summary>
    private static async Task SpendAsync(
        AppDbContext db, LedgerService ledger, string actorId, string category,
        string expenseAccount, string moneyAccount, decimal amount, DateOnly date)
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

        await PostAsync(ledger, actorId, date,
            expenseAccount, moneyAccount, amount, LedgerRefType.Expense, expense.Id);
    }
}
