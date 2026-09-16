using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// "Kassa kuni" — kunlik kassa paneli (docs/modules/existing-module-gaps.md
/// §3.2, EduSchool'dagi <c>fin-map/*</c> ning o'rnini bosadi).
///
/// <para>
/// <b>FAQAT O'QISH.</b> Bu yerda POST ham, PUT ham, DELETE ham YO'Q va
/// bo'lmaydi: panel jurnalni ko'rsatadi, unga yozmaydi (SPEC §4.1 —
/// append-only, tuzatish faqat storno orqali va u
/// <see cref="PaymentsController"/> ning ishi).
/// </para>
///
/// <para>
/// <b>RUXSAT (SPEC §4.3).</b> Ikki qavat, <see cref="MoneyFlowController"/>
/// bilan bir xil: tashqi qo'pol darvoza
/// <c>[Authorize(Roles = Roles.FinanceStaff)]</c> va amal darajasidagi
/// <c>[FinanceRole(FinanceAction.ViewBillingReports)]</c>. Kassir — 403.
/// </para>
/// <para>
/// <b>Nega kassir ko'rmaydi, garchi ekran "kassa kuni" deb atalsa ham.</b>
/// Panel BUTUN maktabning kunini ko'rsatadi: hamma kassirning smenasi,
/// bank hisobi, chiqimlar, maosh to'lovlari. §4.3 dagi "See variance report
/// across cashiers — ⛔" qatori aynan shuni taqiqlaydi. Kassirning o'z kuni
/// unga allaqachon ochiq — <c>GET /api/cash/shifts/current</c> va o'z
/// smenasining Z-hisoboti. Torroq, faqat o'z smenasi ko'rinadigan variant
/// kerak bo'lsa — bu mijoz qarori, uni bu yerda o'zboshimchalik bilan
/// kengaytirmaymiz.
/// </para>
///
/// <para>
/// <b>DI:</b> <c>AppDbContext</c> va <c>ICashShiftService</c> ikkisi ham
/// allaqachon ro'yxatdan o'tgan (<c>Program.cs</c>), <see cref="CashDayQueries"/>
/// esa holatsiz — shuning uchun u shu yerda quriladi va <c>Program.cs</c> ga
/// TEGILMAYDI. Aynan shu naqsh <c>FinanceReportsController</c> da ham
/// ishlatilgan (docs/PENDING_WIRING.md, P1-15 §3a).
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[FinanceRole(FinanceAction.ViewBillingReports)]
[Route("api/admin/finance/cash-day")]
public sealed class CashDayController(AppDbContext db, ICashShiftService shifts) : ControllerBase
{
    private readonly CashDayQueries _queries = new(db, shifts);

    /// <summary>
    /// <c>GET /api/admin/finance/cash-day?date=YYYY-MM-DD</c>
    ///
    /// <para>
    /// Bir kunning to'liq manzarasi: ochilish → kirim → chiqim → yopilish
    /// (naqd va bank alohida), kunning harakatlari, eng yirik beshtasi,
    /// turlar va toifalar kesimi, hozir ochiq smenalar.
    /// </para>
    /// <para>
    /// Sana berilmasa — BUGUN (maktab mintaqasida, <see cref="AppClock.Today"/>).
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<CashDayDto>> Day(
        [FromQuery] string? date,
        CancellationToken ct = default)
    {
        if (!TryParseDate(date, AppClock.Today, out var day))
            return BadRequest(new { message = "'date' sanasi noto'g'ri. Format: YYYY-MM-DD." });

        return Ok(await _queries.DayAsync(day, ct));
    }

    /// <summary>
    /// <c>GET /api/admin/finance/cash-day/calendar?month=YYYY-MM</c>
    ///
    /// <para>
    /// Oylik kalendar: har kun uchun sof harakat va kun oxiridagi qoldiq.
    /// Katakcha bosilganda UI <see cref="Day"/> ni o'sha sana bilan qayta
    /// chaqiradi.
    /// </para>
    /// <para>
    /// Nega alohida endpoint: kalendar kun almashganda O'ZGARMAYDI, ya'ni
    /// uni har kun bosilganda qayta yuklash ortiqcha ish bo'lardi.
    /// </para>
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<CashMonthDto>> Calendar(
        [FromQuery] string? month,
        CancellationToken ct = default)
    {
        var today = AppClock.Today;
        if (!TryParseMonth(month, new DateOnly(today.Year, today.Month, 1), out var first))
            return BadRequest(new { message = "'month' noto'g'ri. Format: YYYY-MM." });

        return Ok(await _queries.MonthAsync(first, ct));
    }

    /// <summary>
    /// Bo'sh qiymat uchun standart sana, aks holda QAT'IY <c>YYYY-MM-DD</c>.
    /// Erkin format qabul qilinmaydi: "01.02.2026" ni mintaqaga qarab ikki xil
    /// o'qish mumkin, moliyada esa bu jimgina bir oylik siljish degani
    /// (<see cref="MoneyFlowController"/> dagi qoidaning aynan o'zi).
    /// </summary>
    private static bool TryParseDate(string? raw, DateOnly fallback, out DateOnly value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = fallback; return true; }

        return DateOnly.TryParseExact(
            raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>
    /// <c>YYYY-MM</c> → oyning birinchi kuni. <c>YYYY-MM-DD</c> ham qabul
    /// qilinadi (kuni tashlab yuboriladi) — UI sana tanlagichdan to'liq sana
    /// yuborib qo'ysa, so'rov 400 bilan yiqilmasin.
    /// </summary>
    private static bool TryParseMonth(string? raw, DateOnly fallback, out DateOnly value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = fallback; return true; }

        if (DateOnly.TryParseExact(
                raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
        {
            value = new DateOnly(full.Year, full.Month, 1);
            return true;
        }

        if (DateTime.TryParseExact(
                raw, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            value = new DateOnly(month.Year, month.Month, 1);
            return true;
        }

        value = fallback;
        return false;
    }
}
