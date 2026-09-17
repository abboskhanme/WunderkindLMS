using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Moliya hisobotlari (P1-13): qarzdorlar, P&amp;L, pul oqimi, yig'ilish darajasi.
///
/// <para>
/// <b>RUXSAT (SPEC §4.3).</b> Klass darajasidagi
/// <c>[FinanceRole(FinanceAction.ViewBillingReports)]</c> — ya'ni <c>admin</c>
/// va <c>superadmin</c>. <b>Kassir bu yerga UMUMAN kira olmaydi</b> (403):
/// §4.3 dagi "See variance report across cashiers — ⛔" qatori. Sababi
/// oddiy: kassir boshqa kassirlarning va butun maktabning moliyaviy holatini
/// ko'rishi uchun hech qanday ish sababi yo'q, ko'rgan odam esa nimani
/// yashirish kerakligini biladi.
/// </para>
/// <para>
/// Eski <see cref="FinanceController"/> ga TEGILMAGAN — u o'quvchi
/// qatoridagi saqlangan qoldiqqa va <c>finance_transactions</c> ga tayanadi,
/// P1-21 da olib tashlanadi. Ikkisi bir vaqtda yashaydi: bu yerdagi
/// endpoint'lar boshqa yo'llarda (<c>debtors</c>, <c>pnl</c>,
/// <c>cashflow</c>, <c>collection-rate</c>), ya'ni to'qnashuv yo'q.
/// </para>
/// <para>
/// <b>Nega <c>FinanceReportQueries</c> DI'dan olinmaydi.</b> P1-13 ning
/// qabul mezoni <c>Program.cs</c> ga tegmaslikni talab qiladi (u P1-15 ishi,
/// parallel vazifalar bilan konflikt maydoni). Klass holatsiz va faqat
/// <c>IAppDbContext</c> ga bog'liq, shuning uchun so'rov doirasidagi
/// <see cref="AppDbContext"/> ustidan shu yerda yaratiladi.
/// Ro'yxatdan o'tkazish kerak bo'lsa — <c>docs/PENDING_WIRING.md</c>,
/// P1-15 bandi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[FinanceRole(FinanceAction.ViewBillingReports)]
[Route("api/admin/finance")]
public class FinanceReportsController(AppDbContext db) : ControllerBase
{
    private readonly FinanceReportQueries _reports = new(db);

    /// <summary>
    /// Qarzdorlar: har o'quvchi bitta qator, toifalar kesimidagi yoyilma bilan
    /// (o'qish / avtobus / yotoqxona / ovqat / boshqa).
    ///
    /// <para>
    /// Qarz HAR SAFAR <c>invoices</c> va <c>payment_allocations</c> dan
    /// hisoblanadi — saqlangan ustundan EMAS (docs/TASKS.md §1.4).
    /// </para>
    /// </summary>
    /// <param name="className">Sinf bo'yicha filtr (aniq moslik).</param>
    /// <param name="minDebt">Shu summadan kam qarz ko'rsatilmaydi (sukut 0.01).</param>
    /// <param name="month">Bitta hisob-faktura oyi, "YYYY-MM". Berilsa — o'sha
    /// oyning qoldig'i (EduSchool'dagi <c>month</c> / <c>monthlyDebt</c> filtri,
    /// docs/modules/finance-parity.md §2.2 F2.02). Berilmasa — jami qarz.</param>
    /// <param name="onlyOverdue">true = faqat muddati o'tganlar.</param>
    /// <param name="includeArchived">false = arxivlangan o'quvchilarni yashirish.</param>
    [HttpGet("debtors")]
    public async Task<ActionResult<IEnumerable<DebtorRowDto>>> Debtors(
        [FromQuery] string? className,
        [FromQuery] decimal? minDebt,
        [FromQuery] string? month,
        [FromQuery] bool onlyOverdue = false,
        [FromQuery] bool includeArchived = true,
        CancellationToken ct = default)
    {
        // Oy IXTIYORIY, lekin berilgani NOTO'G'RI bo'lsa — 400. Jimgina
        // e'tiborsiz qoldirish butun maktabning qarzini "sentyabr qarzi"
        // deb ko'rsatardi.
        DateOnly? selectedMonth = null;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!TryMonth(month, DefaultToMonth(), out var parsed))
                return InvalidMonth(nameof(month), month);
            selectedMonth = parsed;
        }

        var query = new DebtorReportQuery(
            ClassName: className,
            MinDebt: minDebt ?? 0.01m,
            OnlyOverdue: onlyOverdue,
            IncludeArchived: includeArchived,
            Month: selectedMonth);

        var rows = await _reports.DebtorsAsync(query, ct);
        return Ok(rows);
    }

    /// <summary>
    /// Foyda va zarar (P&amp;L): <c>ledger_entries</c> ni akkaunt prefiksi
    /// bo'yicha yig'adi — <c>revenue:*</c> va <c>expense:*</c>.
    /// </summary>
    /// <param name="from">Davr boshi (sukut: joriy oyning 1-kuni).</param>
    /// <param name="to">Davr oxiri (sukut: bugun).</param>
    [HttpGet("pnl")]
    public async Task<ActionResult<ProfitLossDto>> ProfitLoss(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var (start, end) = Period(from, to, defaultMonths: 1);
        if (end < start) return InvalidPeriod(start, end);

        return Ok(await _reports.ProfitLossAsync(start, end, ct));
    }

    /// <summary>
    /// Pul oqimi (Cash Flow): <c>cash</c> va <c>bank</c> hisoblarining
    /// harakati, oylar kesimida. Davr boshidagi qoldiq ham beriladi.
    /// </summary>
    /// <param name="from">Davr boshi (sukut: 12 oy oldingi oyning 1-kuni).</param>
    /// <param name="to">Davr oxiri (sukut: bugun).</param>
    [HttpGet("cashflow")]
    public async Task<ActionResult<CashFlowDto>> CashFlow(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var (start, end) = Period(from, to, defaultMonths: 12);
        if (end < start) return InvalidPeriod(start, end);

        var months = FinanceReportQueries.MonthsBetween(start, end);
        if (months > FinanceReportQueries.MaxCashFlowMonths)
            return BadRequest(new
            {
                message = $"Davr juda uzun: {months} oy. Ruxsat etilgani — "
                          + $"{FinanceReportQueries.MaxCashFlowMonths} oy.",
            });

        return Ok(await _reports.CashFlowAsync(start, end, ct));
    }

    /// <summary>
    /// Yig'ilish darajasi: oylar kesimida hisoblangan va yig'ilgan summa.
    /// Oy — hisob-faktura oyi (<c>period_month</c>); batafsil izoh
    /// <see cref="FinanceReportQueries.CollectionRateAsync"/> da.
    /// </summary>
    /// <param name="from">Boshlang'ich oy. Berilmasa — eng erta oydan.</param>
    /// <param name="to">Oxirgi oy. Berilmasa — eng oxirgi oygacha.</param>
    [HttpGet("collection-rate")]
    public async Task<ActionResult<IEnumerable<BillingMonthlyDto>>> CollectionRate(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        if (from is { } f && to is { } t && t < f) return InvalidPeriod(f, t);

        return Ok(await _reports.CollectionRateAsync(from, to, ct));
    }

    /// <summary>
    /// Oyma-oy qarzdorlik jadvali: o'quvchi × oy. Har katakda hisoblangan,
    /// to'langan va qolgan summa; pastda ustun yakunlari.
    ///
    /// <para>
    /// Direktorning kundalik savoli — "kim, qaysi oydan beri to'lamayapti".
    /// Qarzdorlar hisoboti unga BITTA raqam bilan javob beradi, bu jadval esa
    /// qarzning qaysi oyda boshlangani va uzilib-uzilib to'langanini
    /// ko'rsatadi. Arifmetika ikkovida bir xil manbadan
    /// (<see cref="FinanceReportQueries.ArrearsPivotAsync"/>).
    /// </para>
    /// </summary>
    /// <param name="fromMonth">Birinchi oy, "YYYY-MM". Sukut: joriy o'quv
    /// yilining sentyabri.</param>
    /// <param name="toMonth">Oxirgi oy, "YYYY-MM". Sukut: joriy oy.</param>
    /// <param name="className">Sinf bo'yicha filtr (aniq moslik).</param>
    /// <param name="categoryId">Bitta to'lov toifasi. Berilmasa — hammasi
    /// bitta katakka yig'iladi.</param>
    /// <param name="debtorsOnly">true = qoldig'i bor o'quvchilargina.</param>
    /// <param name="includeArchived">false = arxivlangan o'quvchilarni yashirish.</param>
    [HttpGet("arrears-pivot")]
    public async Task<ActionResult<ArrearsPivotDto>> ArrearsPivot(
        [FromQuery] string? fromMonth,
        [FromQuery] string? toMonth,
        [FromQuery] string? className,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool debtorsOnly = false,
        [FromQuery] bool includeArchived = true,
        CancellationToken ct = default)
    {
        if (!TryMonth(toMonth, DefaultToMonth(), out var to))
            return InvalidMonth(nameof(toMonth), toMonth);
        if (!TryMonth(fromMonth, DefaultFromMonth(to), out var from))
            return InvalidMonth(nameof(fromMonth), fromMonth);

        if (to < from) return InvalidPeriod(from, to);

        var months = FinanceReportQueries.MonthsBetween(from, to);
        if (months > FinanceReportQueries.MaxArrearsMonths)
            return BadRequest(new
            {
                message = $"Davr juda uzun: {months} oy. Ruxsat etilgani — "
                          + $"{FinanceReportQueries.MaxArrearsMonths} oy.",
            });

        var query = new ArrearsPivotQuery(
            FromMonth: from,
            ToMonth: to,
            ClassName: className,
            CategoryId: categoryId,
            DebtorsOnly: debtorsOnly,
            IncludeArchived: includeArchived);

        try
        {
            return Ok(await _reports.ArrearsPivotAsync(query, ct));
        }
        catch (ArgumentOutOfRangeException tooWide)
        {
            // Yagona sabab — o'quvchilar chegarasi (oy chegarasi yuqorida
            // tekshirilgan). Bu 500 emas: so'rov noto'g'ri, tuzatish esa
            // foydalanuvchi qo'lida — sinfni tanlasin.
            return BadRequest(new { message = tooWide.Message });
        }
    }


    /// <summary>
    /// Davrni to'ldiradi: <paramref name="to"/> berilmasa — bugun,
    /// <paramref name="from"/> berilmasa — shuncha oy oldingi oyning 1-kuni.
    /// Sukut qiymatlar ataylab kichik: hisobot ochilishi bilan butun tarixni
    /// yig'ib o'tirmasin.
    /// </summary>
    private static (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to, int defaultMonths)
    {
        var end = to ?? AppClock.Today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-(defaultMonths - 1));
        return (start, end);
    }

    private BadRequestObjectResult InvalidPeriod(DateOnly from, DateOnly to) =>
        BadRequest(new { message = $"Davr oxiri boshidan oldin: {from:yyyy-MM-dd} … {to:yyyy-MM-dd}" });

    /// <summary>
    /// "YYYY-MM" (yoki to'liq sana) ni oyning birinchi kuniga o'giradi.
    /// Bo'sh qiymat — xato emas: <paramref name="fallback"/> ishlatiladi.
    /// </summary>
    private static bool TryMonth(string? text, DateOnly fallback, out DateOnly month)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            month = fallback;
            return true;
        }

        var value = text.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)
            || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed))
        {
            month = new DateOnly(parsed.Year, parsed.Month, 1);
            return true;
        }

        month = fallback;
        return false;
    }

    /// <summary>Sukut: joriy oy.</summary>
    private static DateOnly DefaultToMonth()
    {
        var today = AppClock.Today;
        return new DateOnly(today.Year, today.Month, 1);
    }

    /// <summary>
    /// Sukut: joriy O'QUV YILINING sentyabri (sentyabrgacha — o'tgan yilniki).
    /// Kalendar yili emas: yanvarda ochilgan jadval sentyabr–dekabr qarzini
    /// tashlab ketmasligi kerak, aynan o'sha oylar qarzdor bo'ladi.
    /// </summary>
    private static DateOnly DefaultFromMonth(DateOnly to)
    {
        var year = to.Month >= AcademicYearStartMonth ? to.Year : to.Year - 1;
        var start = new DateOnly(year, AcademicYearStartMonth, 1);
        return start <= to ? start : to;
    }

    /// <summary>O'quv yili sentyabrda boshlanadi (SPEC §3.2).</summary>
    private const int AcademicYearStartMonth = 9;

    private BadRequestObjectResult InvalidMonth(string field, string? value) =>
        BadRequest(new
        {
            message = $"Oy formati noto'g'ri ({field}: \"{value}\"). Kutilgani — \"YYYY-MM\", masalan 2026-09.",
        });
}

