using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
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
