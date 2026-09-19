using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Moliya hisobotlarining IKKINCHI avlodi (docs/modules/finance-parity.md
/// §2.4, §2.5, §2.7): kunlik panel, yil × oy P&amp;L matritsasi, toifalar
/// kesimidagi pul oqimi va har bir katakning ortidagi jurnal satrlari.
///
/// <para>
/// <b>FAQAT O'QISH.</b> POST ham, PUT ham, DELETE ham yo'q va bo'lmaydi:
/// hisobot jurnalni ko'rsatadi, unga yozmaydi (SPEC §4.1 — append-only,
/// tuzatish faqat storno orqali).
/// </para>
///
/// <para>
/// <b>RUXSAT (SPEC §4.3).</b> Ikki qavat, <see cref="CashDayController"/>
/// bilan bir xil: <c>[Authorize(Roles = Roles.FinanceStaff)]</c> va amal
/// darajasidagi <c>[FinanceRole(FinanceAction.ViewBillingReports)]</c> —
/// ya'ni admin va direktor. <b>Kassir 403 oladi</b>: §4.3 dagi "See variance
/// report across cashiers — ⛔" qatori. Butun maktabning foydasi va pul
/// oqimi kassaning kundalik ishi emas.
/// </para>
///
/// <para>
/// <b>Nega alohida controller.</b> <see cref="FinanceReportsController"/>
/// P1-13 ning shartnomasi (qarzdorlar, P&amp;L, pul oqimi, yig'ilish
/// darajasi) va unga frontend allaqachon bog'langan. Yangi yo'llar boshqa
/// manzillarda (<c>dashboard</c>, <c>pnl/matrix</c>, <c>ledger/lines</c>,
/// <c>cashflow/statement</c>, <c>cashflow/lines</c>), ya'ni to'qnashuv yo'q;
/// so'rovlar esa BIR XIL klassdan keladi
/// (<see cref="FinanceReportQueries"/>), shuning uchun ikkinchi ta'rif ham
/// paydo bo'lmaydi.
/// </para>
/// <para>
/// <b>DI:</b> <c>Program.cs</c> ga tegilmaydi — klass holatsiz va faqat
/// <c>AppDbContext</c> ga bog'liq, shuning uchun so'rov doirasida shu yerda
/// quriladi (<see cref="FinanceReportsController"/> bilan bir xil naqsh).
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[FinanceRole(FinanceAction.ViewBillingReports)]
[Route("api/admin/finance")]
public sealed class FinanceStatementsController(AppDbContext db) : ControllerBase
{
    private readonly FinanceReportQueries _reports = new(db);

    /// <summary>
    /// <c>GET /api/admin/finance/dashboard?from&amp;to</c> — "Moliya
    /// hisobotlari" paneli (§2.4 F4.01, F4.02): kirim / chiqim / qoldiq
    /// (oldingi davr bilan taqqoslab), kunlik grafik, toifalar va to'lov
    /// usullari kesimi.
    /// </summary>
    /// <param name="from">Davr boshi (sukut: joriy oyning 1-kuni).</param>
    /// <param name="to">Davr oxiri (sukut: bugun).</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet("dashboard")]
    public async Task<ActionResult<FinanceDashboardDto>> Dashboard(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var (start, end) = Period(from, to);
        if (end < start) return InvalidPeriod(start, end);

        try
        {
            return Ok(await _reports.DashboardAsync(start, end, ct));
        }
        catch (ArgumentOutOfRangeException tooWide)
        {
            return BadRequest(new { message = tooWide.Message });
        }
    }

    /// <summary>
    /// <c>GET /api/admin/finance/dashboard/export?from&amp;to</c> — "Moliya
    /// hisobotlari" ekranining eksporti, .xlsx (§2.4 F4.05).
    ///
    /// <para>
    /// Mijoz, 2026-09-19: "yuklab olish csv emas excel fayl uchun bo'lsin".
    /// Ekranda beshta blok bor, shuning uchun kitobda ham beshta varaq:
    /// <b>Umumiy</b> (KPI va qoldiq), <b>Kunlar</b>, <b>Toifalar</b>,
    /// <b>To'lov usullari</b> va <b>Chegirmalar</b>. Raqamlar
    /// <see cref="Dashboard"/> dan olinadi — ekrandagining aynan o'zi.
    /// </para>
    /// </summary>
    /// <param name="from">Davr boshi.</param>
    /// <param name="to">Davr oxiri.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet("dashboard/export")]
    public async Task<ActionResult> DashboardExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var result = await Dashboard(from, to, ct);
        if (result.Result is not OkObjectResult ok || ok.Value is not FinanceDashboardDto d)
            return result.Result ?? StatusCode(500);

        // 1) Umumiy — KPI, o'zgarish foizi va qoldiqlar.
        string[] kpiHeaders = ["Ko'rsatkich", "Joriy davr", "Oldingi davr", "O'zgarish, %"];
        IReadOnlyList<ExcelExport.XlsxCell> openingRow =
        [
            ExcelExport.XlsxCell.Of("Davr boshidagi qoldiq"),
            ExcelExport.XlsxCell.Num(d.OpeningBalance),
        ];

        List<IReadOnlyList<ExcelExport.XlsxCell>> kpiRows =
        [
            KpiRow("Kirim", d.Inflow),
            KpiRow("Chiqim", d.Outflow),
            KpiRow("Sof", d.Net),
            openingRow,
        ];

        IReadOnlyList<ExcelExport.XlsxCell> kpiTotals =
        [
            ExcelExport.XlsxCell.Of("Davr oxiridagi qoldiq"),
            ExcelExport.XlsxCell.Num(d.ClosingBalance),
        ];

        // 2) Kunlar — grafikning o'sha ustunlari.
        string[] dayHeaders = ["Sana", "Kirim", "Chiqim", "Sof"];
        var dayRows = d.Days.Select(x => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            ExcelExport.XlsxCell.Of(x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ExcelExport.XlsxCell.Num(x.Inflow),
            ExcelExport.XlsxCell.Num(x.Outflow),
            ExcelExport.XlsxCell.Num(x.Net),
        ]).ToList();

        // 3) Toifalar — ekrandagi bo'lim × toifa jadvali.
        string[] catHeaders = ["Bo'lim", "Toifa", "Kirim", "Chiqim", "Sof"];
        var catRows = new List<IReadOnlyList<ExcelExport.XlsxCell>>();
        foreach (var section in d.Sections)
        {
            catRows.AddRange(section.Rows.Select(r => (IReadOnlyList<ExcelExport.XlsxCell>)
            [
                ExcelExport.XlsxCell.Of(section.Label),
                ExcelExport.XlsxCell.Of(r.Label),
                ExcelExport.XlsxCell.Num(r.Total.Inflow),
                ExcelExport.XlsxCell.Num(r.Total.Outflow),
                ExcelExport.XlsxCell.Num(r.Total.Amount),
            ]));

            catRows.Add(
            [
                ExcelExport.XlsxCell.Of(section.Label),
                ExcelExport.XlsxCell.Of("Jami"),
                ExcelExport.XlsxCell.Num(section.Total.Inflow),
                ExcelExport.XlsxCell.Num(section.Total.Outflow),
                ExcelExport.XlsxCell.Num(section.Total.Amount),
            ]);
        }

        // 4) To'lov usullari — faqat to'lovlar (chiqimda usul saqlanmaydi).
        string[] methodHeaders = ["Usul", "Kirim", "Chiqim", "Sof", "Soni"];
        var methodRows = d.Methods.Select(m => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            ExcelExport.XlsxCell.Of(m.Label),
            ExcelExport.XlsxCell.Num(m.Inflow),
            ExcelExport.XlsxCell.Num(m.Outflow),
            ExcelExport.XlsxCell.Num(m.Amount),
            ExcelExport.XlsxCell.Num(m.Count),
        ]).ToList();

        IReadOnlyList<ExcelExport.XlsxCell> methodTotals =
        [
            ExcelExport.XlsxCell.Of("Jami"),
            ExcelExport.XlsxCell.Num(d.MethodsTotal.Inflow),
            ExcelExport.XlsxCell.Num(d.MethodsTotal.Outflow),
            ExcelExport.XlsxCell.Num(d.MethodsTotal.Amount),
            ExcelExport.XlsxCell.Num(d.MethodsTotal.Count),
        ];

        // 5) Chegirmalar — toifa kesimi (qoida kesimi emas, izoh DiscountAnalysisDto da).
        string[] discountHeaders = ["Toifa", "Hisob-fakturalar", "Chegirma", "Ulush, %"];
        var discountRows = d.Discounts.Rows.Select(r => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            ExcelExport.XlsxCell.Of(r.CategoryName),
            ExcelExport.XlsxCell.Num(r.InvoiceCount),
            ExcelExport.XlsxCell.Num(r.Total),
            ExcelExport.XlsxCell.Num(r.Share),
        ]).ToList();

        IReadOnlyList<ExcelExport.XlsxCell> discountTotals =
        [
            ExcelExport.XlsxCell.Of("Jami"),
            ExcelExport.XlsxCell.Num(d.Discounts.AppliedCount),
            ExcelExport.XlsxCell.Num(d.Discounts.Total),
            ExcelExport.XlsxCell.Of(null),
        ];

        var bytes = ExcelExport.BuildTables(
        [
            new ExcelExport.TableSpec("Umumiy", kpiHeaders, kpiRows, kpiTotals),
            new ExcelExport.TableSpec("Kunlar", dayHeaders, dayRows),
            new ExcelExport.TableSpec("Toifalar", catHeaders, catRows),
            new ExcelExport.TableSpec("To'lov usullari", methodHeaders, methodRows, methodTotals),
            new ExcelExport.TableSpec("Chegirmalar", discountHeaders, discountRows, discountTotals),
        ]);

        return File(bytes, XlsxMime,
            $"moliya-hisoboti_{d.From:yyyy-MM-dd}_{d.To:yyyy-MM-dd}.xlsx");
    }

    /// <summary>KPI qatori: joriy, oldingi va o'zgarish foizi (bo'sh bo'lishi mumkin).</summary>
    private static IReadOnlyList<ExcelExport.XlsxCell> KpiRow(string label, FinanceKpiDto kpi) =>
        [
            ExcelExport.XlsxCell.Of(label),
            ExcelExport.XlsxCell.Num(kpi.Current),
            ExcelExport.XlsxCell.Num(kpi.Previous),
            ExcelExport.XlsxCell.Num(kpi.ChangePercent),
        ];

    /// <summary>
    /// <c>GET /api/admin/finance/pnl/matrix?year=2026</c> — yil × oy P&amp;L
    /// (§2.5 F5.01, F5.02): daromad va chiqim qatorlari, sof natija va oy
    /// boshidagi / oxiridagi pul qoldig'i.
    /// </summary>
    /// <param name="year">Kalendar yili (sukut: joriy yil).</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet("pnl/matrix")]
    public async Task<ActionResult<ProfitLossMatrixDto>> ProfitLossMatrix(
        [FromQuery] int? year,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _reports.ProfitLossMatrixAsync(year ?? AppClock.Today.Year, ct));
        }
        catch (ArgumentOutOfRangeException badYear)
        {
            return BadRequest(new { message = badYear.Message });
        }
    }

    /// <summary>
    /// <c>GET /api/admin/finance/pnl/matrix/export?year=2026</c> — YIL × OY
    /// P&amp;L eksporti, .xlsx (§2.5 F5.06). Parametr <see cref="ProfitLossMatrix"/>
    /// bilan AYNAN bir xil (<c>FinanceReportsController.ArrearsPivotExport</c>
    /// bilan bir xil naqsh: ichkaridan o'sha action'ni chaqiradi, ikkinchi
    /// ta'rif yo'q).
    ///
    /// <para>
    /// Qatorlar <c>PnlTab.tsx</c>'ning <c>PnlYear</c>'dagi <c>handleExport</c>
    /// (CSV) bilan AYNAN bir shaklda va tartibda: toifa qatorlari (oylar +
    /// jami), "Jami · Daromad", "Jami · Xarajat", "Jami · Sof natija", so'ng
    /// F5.02'ning qoldiq qatorlari — "Oy boshida" va (qalin, oxirgi qator)
    /// "Oy oxirida". Ekranda ko'rinadigan raqamning O'ZI, qayta hisoblanmagan.
    /// </para>
    /// </summary>
    [HttpGet("pnl/matrix/export")]
    public async Task<ActionResult> ProfitLossMatrixExport(
        [FromQuery] int? year,
        CancellationToken ct = default)
    {
        var result = await ProfitLossMatrix(year, ct);

        // `ProfitLossMatrix` o'zi 400 qaytargan bo'lishi mumkin (yil chegaradan
        // tashqarida) — o'sha xatoni AYNAN o'zi bilan qaytaramiz.
        if (result.Result is not OkObjectResult ok || ok.Value is not ProfitLossMatrixDto matrix)
            return result.Result ?? StatusCode(500);

        string[] headers = ["Yo'nalish", "Toifa", .. matrix.Months, "Jami"];

        var rows = new List<IReadOnlyList<ExcelExport.XlsxCell>>();
        rows.AddRange(matrix.Revenue.Select(r =>
            MatrixRow("Daromad", MoneyFlowQueries.LabelFor(r.Account), r.Months, r.Total)));
        rows.AddRange(matrix.Expense.Select(r =>
            MatrixRow("Xarajat", MoneyFlowQueries.LabelFor(r.Account), r.Months, r.Total)));
        rows.Add(MatrixRow("Jami", "Daromad", matrix.RevenueMonths, matrix.RevenueTotal));
        rows.Add(MatrixRow("Jami", "Xarajat", matrix.ExpenseMonths, matrix.ExpenseTotal));
        rows.Add(MatrixRow("Jami", "Sof natija", matrix.NetMonths, matrix.NetTotal));
        rows.Add(MatrixRow("Qoldiq", "Oy boshida", matrix.StartBalance, matrix.OpeningBalance));

        IReadOnlyList<ExcelExport.XlsxCell> totalsRow =
            MatrixRow("Qoldiq", "Oy oxirida", matrix.EndBalance, matrix.ClosingBalance);

        var bytes = ExcelExport.BuildTable($"P&L-{matrix.Year}", headers, rows, totalsRow);
        return File(bytes, XlsxMime, $"foyda-zarar_{matrix.Year}.xlsx");
    }

    /// <summary>Matritsaning bitta qatori — yo'nalish, toifa, 12 oy va yakun (son katagi).</summary>
    private static IReadOnlyList<ExcelExport.XlsxCell> MatrixRow(
        string direction, string category, IReadOnlyList<decimal> months, decimal total) =>
        [
            ExcelExport.XlsxCell.Of(direction),
            ExcelExport.XlsxCell.Of(category),
            .. months.Select(m => ExcelExport.XlsxCell.Num(m)),
            ExcelExport.XlsxCell.Num(total),
        ];

    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// <c>GET /api/admin/finance/ledger/lines?account&amp;from&amp;to</c> —
    /// P&amp;L katagining ortidagi jurnal satrlari (§2.5 F5.03).
    ///
    /// <para>
    /// <paramref name="account"/> — bitta hisob kodi (<c>revenue:tuition</c>)
    /// yoki guruh (<c>revenue:*</c>, <c>expense:*</c>). Javobdagi
    /// <c>total</c> — katakning AYNAN o'zi.
    /// </para>
    /// </summary>
    /// <param name="account">Hisob kodi yoki guruh.</param>
    /// <param name="from">Davr boshi.</param>
    /// <param name="to">Davr oxiri.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet("ledger/lines")]
    public async Task<ActionResult<LedgerLinesDto>> LedgerLines(
        [FromQuery] string? account,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account))
            return BadRequest(new { message = "'account' majburiy: qaysi hisobning satrlari kerak?" });

        var (start, end) = Period(from, to);
        if (end < start) return InvalidPeriod(start, end);

        try
        {
            return Ok(await _reports.LedgerLinesAsync(account, start, end, ct));
        }
        catch (ArgumentOutOfRangeException tooWide)
        {
            return BadRequest(new { message = tooWide.Message });
        }
    }

    /// <summary>
    /// <c>GET /api/admin/finance/cashflow/statement?from&amp;to&amp;account</c> —
    /// pul oqimi TOIFALAR kesimida (§2.7 F7.01).
    /// </summary>
    /// <param name="from">Davr boshi (sukut: joriy oyning 1-kuni).</param>
    /// <param name="to">Davr oxiri (sukut: bugun).</param>
    /// <param name="account">Faqat <c>cash</c> yoki faqat <c>bank</c>. Berilmasa — ikkovi.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet("cashflow/statement")]
    public async Task<ActionResult<CashFlowStatementDto>> CashFlowStatement(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? account,
        CancellationToken ct = default)
    {
        var (start, end) = Period(from, to);
        if (end < start) return InvalidPeriod(start, end);

        try
        {
            return Ok(await _reports.CashFlowStatementAsync(start, end, Clean(account), ct));
        }
        catch (ArgumentOutOfRangeException invalid)
        {
            return BadRequest(new { message = invalid.Message });
        }
    }

    /// <summary>
    /// <c>GET /api/admin/finance/cashflow/export?from&amp;to&amp;account</c> —
    /// pul oqimi eksporti, .xlsx (§2.7 F7.04).
    ///
    /// <para>
    /// Mijoz, 2026-09-19: "yuklab olish csv emas excel fayl uchun bo'lsin".
    /// Ikki varaq, chunki ekranda ham ikki jadval bor: <b>Oylar</b> (qoldiq,
    /// kirim, chiqim, sof) va <b>Toifalar</b> (bo'lim × oy).
    /// </para>
    ///
    /// <para>
    /// Ikkalasi ham BITTA so'rovdan chiqadi (<see cref="CashFlowStatement"/>),
    /// ya'ni faylda ekrandagi raqamning AYNAN o'zi turadi — bu yerda hech
    /// narsa qayta hisoblanmaydi.
    /// </para>
    /// </summary>
    /// <param name="from">Davr boshi.</param>
    /// <param name="to">Davr oxiri.</param>
    /// <param name="account">Faqat <c>cash</c> yoki faqat <c>bank</c>. Berilmasa — ikkovi.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet("cashflow/export")]
    public async Task<ActionResult> CashFlowExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? account,
        CancellationToken ct = default)
    {
        var result = await CashFlowStatement(from, to, account, ct);

        // So'rov o'zi 400 qaytargan bo'lsa (davr teskari yoki juda keng) —
        // o'sha xatoni AYNAN o'zi bilan qaytaramiz (P&L eksporti bilan bir xil naqsh).
        if (result.Result is not OkObjectResult ok || ok.Value is not CashFlowStatementDto st)
            return result.Result ?? StatusCode(500);

        // 1-varaq — oylar kesimi (ekrandagi birinchi jadval).
        string[] monthHeaders = ["Oy", "Boshlang'ich qoldiq", "Kirim", "Chiqim", "Sof", "Yakuniy qoldiq"];
        var monthRows = st.Months.Select((m, i) => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            ExcelExport.XlsxCell.Of(m),
            ExcelExport.XlsxCell.Num(st.Opening[i]),
            ExcelExport.XlsxCell.Num(st.MonthTotals[i].Inflow),
            ExcelExport.XlsxCell.Num(st.MonthTotals[i].Outflow),
            ExcelExport.XlsxCell.Num(st.MonthTotals[i].Amount),
            ExcelExport.XlsxCell.Num(st.Closing[i]),
        ]).ToList();

        IReadOnlyList<ExcelExport.XlsxCell> monthTotals =
        [
            ExcelExport.XlsxCell.Of("Jami"),
            ExcelExport.XlsxCell.Num(st.OpeningBalance),
            ExcelExport.XlsxCell.Num(st.Total.Inflow),
            ExcelExport.XlsxCell.Num(st.Total.Outflow),
            ExcelExport.XlsxCell.Num(st.Total.Amount),
            ExcelExport.XlsxCell.Num(st.ClosingBalance),
        ];

        // 2-varaq — toifalar kesimi (ekrandagi ikkinchi jadval).
        string[] catHeaders = ["Bo'lim", "Toifa", .. st.Months, "Jami"];
        var catRows = new List<IReadOnlyList<ExcelExport.XlsxCell>>();
        foreach (var section in st.Sections)
        {
            foreach (var row in section.Rows)
                catRows.Add(
                [
                    ExcelExport.XlsxCell.Of(section.Label),
                    ExcelExport.XlsxCell.Of(row.Label),
                    .. row.Months.Select(c => ExcelExport.XlsxCell.Num(c.Amount)),
                    ExcelExport.XlsxCell.Num(row.Total.Amount),
                ]);

            // Bo'lim yakuni — ekranda ham har bo'limning o'z yakuni bor.
            catRows.Add(
            [
                ExcelExport.XlsxCell.Of(section.Label),
                ExcelExport.XlsxCell.Of("Jami"),
                .. section.Months.Select(c => ExcelExport.XlsxCell.Num(c.Amount)),
                ExcelExport.XlsxCell.Num(section.Total.Amount),
            ]);
        }

        IReadOnlyList<ExcelExport.XlsxCell> catTotals =
        [
            ExcelExport.XlsxCell.Of("Sof oqim"),
            ExcelExport.XlsxCell.Of(null),
            .. st.MonthTotals.Select(c => ExcelExport.XlsxCell.Num(c.Amount)),
            ExcelExport.XlsxCell.Num(st.Total.Amount),
        ];

        var bytes = ExcelExport.BuildTables(
        [
            new ExcelExport.TableSpec("Oylar", monthHeaders, monthRows, monthTotals),
            new ExcelExport.TableSpec("Toifalar", catHeaders, catRows, catTotals),
        ]);

        var suffix = st.Account is { Length: > 0 } a ? $"_{a}" : string.Empty;
        return File(bytes, XlsxMime,
            $"pul-oqimi_{st.From:yyyy-MM-dd}_{st.To:yyyy-MM-dd}{suffix}.xlsx");
    }

    /// <summary>
    /// <c>GET /api/admin/finance/cashflow/lines?key&amp;method&amp;from&amp;to&amp;account</c> —
    /// toifa katagining ortidagi pul satrlari (§2.7 F7.03, §2.4 F4.02).
    ///
    /// <para>
    /// Satrning summasi — katakka TUSHGAN qismi: bir necha toifaga
    /// taqsimlangan to'lov har toifada o'z bo'lagi bilan ko'rinadi.
    /// </para>
    /// </summary>
    /// <param name="key">Toifa kaliti (<c>revenue:tuition</c>, <c>advance</c> …).
    /// Berilmasa — hamma toifa (usul bo'yicha drill-down uchun).</param>
    /// <param name="method">To'lov usuli bo'yicha filtr (faqat to'lov satrlari).</param>
    /// <param name="from">Davr boshi.</param>
    /// <param name="to">Davr oxiri.</param>
    /// <param name="account">Faqat <c>cash</c> yoki faqat <c>bank</c>.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    [HttpGet("cashflow/lines")]
    public async Task<ActionResult<CashFlowLinesDto>> CashFlowLines(
        [FromQuery] string? key,
        [FromQuery] string? method,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? account,
        CancellationToken ct = default)
    {
        var (start, end) = Period(from, to);
        if (end < start) return InvalidPeriod(start, end);

        try
        {
            return Ok(await _reports.CashFlowLinesAsync(
                Clean(key), Clean(method), start, end, Clean(account), ct));
        }
        catch (ArgumentOutOfRangeException invalid)
        {
            return BadRequest(new { message = invalid.Message });
        }
    }

    /// <summary>
    /// Davrni to'ldiradi: <paramref name="to"/> berilmasa — bugun,
    /// <paramref name="from"/> berilmasa — joriy oyning 1-kuni. Sukut ataylab
    /// kichik: hisobot ochilishi bilan butun tarixni yig'ib o'tirmasin.
    /// </summary>
    private static (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to)
    {
        var end = to ?? AppClock.Today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        return (start, end);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private BadRequestObjectResult InvalidPeriod(DateOnly from, DateOnly to) =>
        BadRequest(new
        {
            message = $"Davr oxiri boshidan oldin: "
                      + $"{from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} … "
                      + $"{to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
        });
}
