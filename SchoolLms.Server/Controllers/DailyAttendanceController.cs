using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Kunduzgi davomatni BELGILASH — hamshira (mijoz, 2026-10-11: "Kunduzgi davomat faqat hamshirada").
/// Manzillar o'zgarmagan (<c>api/admin/attendance/daily/…</c>); faqat ruxsat alohida —
/// <c>attendanceMark</c>. O'qish (ro'yxat, kun) xodimga ochiq (AdminPerm qoidasi), saqlash va
/// ota-onaga xabar — shu ruxsat bilan.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("attendanceMark")]
[Route("api/admin/attendance/daily")]
public class DailyAttendanceController(AppDbContext db) : ControllerBase
{
    // =====================================================================
    //  KUNLIK DAVOMAT BELGILASH — bitta mas'ul xodim, barcha sinflar
    //  (mijoz, 2026-09-18). Mantiq `DailyAttendanceService` da; u yerdagi
    //  fayl boshidagi izoh nima uchun jurnalga yozilishini tushuntiradi.
    //
    //  RUXSAT (2026-10-11): alohida `attendanceMark` — "Kunduzgi davomat faqat hamshirada"
    //  (mijoz). Ilgari `attendance` ruxsati bilan edi; endi davomatni KO'RISH (`attendance`)
    //  va BELGILASH (`attendanceMark`) ajratildi: rahbarlar ko'radi, hamshira belgilaydi.
    // =====================================================================

    /// <summary>Kunning sinflar ro'yxati: qaysi biri belgilangan, qaysi biri qolgan.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<DailyAttendanceOverviewDto>> DailyOverview(
        [FromQuery] string date, CancellationToken ct)
        => await new DailyAttendanceService(db).OverviewAsync(Today(date), ct);

    /// <summary>Bitta sinfning kuni — o'quvchilar va joriy belgilar.</summary>
    [HttpGet("class")]
    public async Task<ActionResult<DailyAttendanceClassDayDto>> DailyClass(
        [FromQuery] string classId, [FromQuery] string date, CancellationToken ct)
    {
        var day = await new DailyAttendanceService(db).ClassDayAsync(classId, Today(date), ct);
        return day is null ? NotFound(new { message = "Sinf topilmadi." }) : day;
    }

    /// <summary>
    /// Kunni saqlash: belgilanganlar yo'q, qolganlari keldi. Xodim JWT'dan
    /// olinadi — so'rov tanasida "kim belgiladi" maydoni YO'Q.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SaveDaily(SaveDailyAttendanceRequest req, CancellationToken ct)
    {
        var error = await new DailyAttendanceService(db).SaveAsync(req, CurrentUserId(), ct);
        if (error is not null) return BadRequest(new { message = error });

        var day = await new DailyAttendanceService(db).ClassDayAsync(req.ClassId, req.Date, ct);
        return Ok(day);
    }

    /// <summary>
    /// Kun bo'yi kelmaganlar va ularning ota-onasiga xabar holati — tugma bosilishidan oldingi ro'yxat
    /// (mijoz, 2026-10-11: hamshira davomatdan keyin bitta tugma bilan xabar yuboradi).
    /// </summary>
    [HttpGet("absence-notices")]
    public async Task<ActionResult<AbsenceNoticePreviewDto>> AbsenceNotices(
        [FromQuery] string? date, [FromServices] DailyAbsenceNotifier notifier, CancellationToken ct)
        => await notifier.PreviewAsync(Today(date), ct);

    /// <summary>
    /// Kelmaganlarning ota-onalariga Telegram — har o'quvchiga bir kunda bir marta. Yozish amali:
    /// `attendanceMark` ruxsati kerak (AdminPerm).
    /// </summary>
    [HttpPost("absence-notices")]
    public async Task<ActionResult<AbsenceNoticeResultDto>> SendAbsenceNotices(
        SendAbsenceNoticesRequest req, [FromServices] DailyAbsenceNotifier notifier, CancellationToken ct)
    {
        try
        {
            return await notifier.SendAsync(Today(req.Date), CurrentUserId(), ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Sana berilmasa — bugun (maktab mintaqasi bo'yicha).</summary>
    private static string Today(string? date) =>
        string.IsNullOrWhiteSpace(date) ? AppClock.Today.ToString("yyyy-MM-dd") : date;

    private string CurrentUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? "";
}

/// <summary>Kelmaganlarga xabar yuborish so'rovi; sana berilmasa — bugun.</summary>
public sealed record SendAbsenceNoticesRequest(string? Date);
