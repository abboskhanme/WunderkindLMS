using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Turniket hisobotlari (docs/modules/existing-module-gaps.md §4 — #11, #12, #13):
/// turniket analitikasi, kirib-chiqish statistikasi va kunlik davomat hisoboti.
///
/// <para>
/// <b>Nega alohida controller.</b> <see cref="StudentTurnstileController"/> — JONLI ekran
/// (bugungi o'tishlar, SignalR, qurilma ID biriktirish). Bu yerdagilar esa TARIXIY,
/// faqat o'qiydigan hisobotlar. Ikkisini bir joyga qo'shish jonli ekranni hisobot
/// filtrlari bilan og'irlashtirardi; mavjud endpoint'lar esa umuman tegilmagan.
/// </para>
/// <para>
/// <b>RUXSAT.</b> <c>[AdminPerm("students")]</c> — "O'quvchilar" bo'limi kaliti, chunki
/// hisobotlar shu bo'lim ostida yashaydi (§4.2). Admin/direktor — to'liq; xodim (staff) —
/// o'qishi mumkin (hammasi GET); o'qituvchi, o'quvchi, ota-ona va kassir — 403.
/// </para>
/// <para>
/// <b>DI'ga tegilmagan.</b> <see cref="TurnstileAnalyticsQueries"/> holatsiz va faqat
/// <c>IAppDbContext</c> ga bog'liq, shuning uchun <c>FinanceReportsController</c> dagi
/// naqsh takrorlanadi: so'rov doirasidagi <see cref="AppDbContext"/> ustidan shu yerda
/// yaratiladi. <c>Program.cs</c> — umumiy fayl, unga tegmaymiz.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/turnstile-analytics")]
public class TurnstileAnalyticsController(AppDbContext db) : ControllerBase
{
    private readonly TurnstileAnalyticsQueries _reports = new(db);

    // =====================================================================
    //  #11 — Turniket analitikasi
    // =====================================================================

    /// <summary>
    /// Turniket davomati: har o'quvchi necha kun kirgan, necha kun kirmagan, necha marta
    /// kechikkan va erta ketgan + jamlama.
    /// </summary>
    /// <param name="from">Boshlanish sanasi "yyyy-MM-dd" (sukut — bugun).</param>
    /// <param name="to">Tugash sanasi (sukut — <paramref name="from"/>).</param>
    /// <param name="className">Sinf bo'yicha filtr (aniq moslik).</param>
    /// <param name="status">all | entered | missing | late | early | unlinked.</param>
    [HttpGet("attendance")]
    public async Task<ActionResult<TurnstileAttendanceReportDto>> Attendance(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? className, [FromQuery] string? status,
        CancellationToken ct = default)
    {
        if (!TryRange(from, to, out var a, out var b, out var error)) return BadRequest(new { message = error });
        return await _reports.AttendanceAsync(a, b, className, status, ct);
    }

    /// <summary>
    /// Buzilishlar ro'yxati (sahifalangan): kechikib kelganlar va darslar tugamasdan
    /// chiqib ketganlar.
    /// </summary>
    /// <param name="type">all | late | early.</param>
    /// <param name="page">Sahifa raqami (1 dan).</param>
    /// <param name="pageSize">Sahifadagi qatorlar (1-200, sukut 50).</param>
    [HttpGet("violations")]
    public async Task<ActionResult<TurnstileViolationsPageDto>> Violations(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? className, [FromQuery] string? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!TryRange(from, to, out var a, out var b, out var error)) return BadRequest(new { message = error });
        return await _reports.ViolationsAsync(a, b, className, type, page, pageSize, ct);
    }

    /// <summary>Kechikish va erta ketish jamlamasi — kunlar qatori (grafik uchun) va sinflar kesimi.</summary>
    [HttpGet("late-early/summary")]
    public async Task<ActionResult<TurnstileLateEarlySummaryDto>> LateEarly(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? className,
        CancellationToken ct = default)
    {
        if (!TryRange(from, to, out var a, out var b, out var error)) return BadRequest(new { message = error });
        return await _reports.LateEarlyAsync(a, b, className, ct);
    }

    /// <summary>Bugun nechta o'quvchi kechikdi — sarlavhadagi bitta raqam.</summary>
    /// <param name="date">Boshqa kunni ko'rish uchun (sukut — bugun).</param>
    [HttpGet("today-late-count")]
    public async Task<ActionResult<TurnstileTodayLateDto>> TodayLate(
        [FromQuery] string? date, CancellationToken ct = default)
        => await _reports.TodayLateAsync(ParseDate(date), ct);

    // =====================================================================
    //  #12 — Kirib-chiqish statistikasi
    // =====================================================================

    /// <summary>
    /// Kirish va chiqishlarning kun davomidagi taqsimoti — ertalabki to'lqin qachon
    /// bo'lgani va undan keyin kim kirganini ko'rsatadi.
    /// </summary>
    /// <param name="groupBy">"hour" (sukut) yoki "period" (dars vaqtlari kesimida).</param>
    [HttpGet("flow")]
    public async Task<ActionResult<TurnstileFlowReportDto>> Flow(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? className, [FromQuery] string? groupBy,
        CancellationToken ct = default)
    {
        if (!TryRange(from, to, out var a, out var b, out var error)) return BadRequest(new { message = error });
        return await _reports.FlowAsync(a, b, className, groupBy, ct);
    }

    // =====================================================================
    //  #13 — Kunlik davomat hisoboti
    // =====================================================================

    /// <summary>
    /// Bir kun, bir sahifa: har sinf bo'yicha kutilgan, turniketdan o'tgan, jurnalda "bor"
    /// belgilangan va ular orasidagi FARQ (+ kelishmagan o'quvchilar ro'yxati).
    /// </summary>
    [HttpGet("daily-report")]
    public async Task<ActionResult<TurnstileDailyReportDto>> DailyReport(
        [FromQuery] string? date, CancellationToken ct = default)
        => await _reports.DailyReportAsync(ParseDate(date) ?? AppClock.Today, ct);

    // =====================================================================
    //  Parametrlarni tekshirish
    // =====================================================================

    /// <summary>"yyyy-MM-dd" satrini sanaga o'giradi; noto'g'ri/bo'sh bo'lsa null.</summary>
    private static DateOnly? ParseDate(string? value) =>
        value is { Length: >= 10 } && DateOnly.TryParse(value[..10], out var d) ? d : null;

    /// <summary>
    /// Oraliqni tekshiradi: sukut — bugun; teskari berilgan sana almashtiriladi;
    /// juda uzun oraliq RAD ETILADI (hodisalar jadvalida <c>event_at</c> indeksi yo'q,
    /// ya'ni bir yillik so'rov butun jadvalni skanerlardi).
    /// </summary>
    private static bool TryRange(string? from, string? to,
        out DateOnly a, out DateOnly b, out string error)
    {
        error = "";
        a = ParseDate(from) ?? AppClock.Today;
        b = ParseDate(to) ?? a;
        if (b < a) (a, b) = (b, a);

        var days = b.DayNumber - a.DayNumber + 1;
        if (days > TurnstileAnalyticsQueries.MaxRangeDays)
        {
            error = $"Oraliq juda uzun — eng ko'pi {TurnstileAnalyticsQueries.MaxRangeDays} kun.";
            return false;
        }
        return true;
    }
}
