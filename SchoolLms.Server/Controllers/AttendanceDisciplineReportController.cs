using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Davomat intizomi bo'yicha hisobot (§4, #6) — faqat O'QISH.
///
/// <para>
/// <b>Nega <c>discipline</c> ruxsati, <c>attendance</c> emas.</b> Hisobotning asosiy raqami —
/// intizomiy ball: <c>AbsenceReason.Points</c>, <c>DisciplinePoint</c> (kim qo'ygani va izohi
/// bilan) va 100 balldan qolgan qoldiq. Bularning hammasi bugun faqat <c>DisciplineController</c>
/// (<c>AdminPerm("discipline")</c>) orqali ko'rinadi. Davomat belgisi bu yerda VOSITA — ball
/// qayerdan kelganini ko'rsatadi. Hisobotni <c>attendance</c> ostiga qo'yish davomat ruxsatli
/// xodimga intizomiy ball tarixini ochib berardi, ya'ni darvozani kengaytirardi. Alohida
/// controller — <c>DisciplineController</c> ga bu to'lqinda boshqa agent tegayotgani uchun.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("discipline")]
[Route("api/admin/attendance-discipline-report")]
public class AttendanceDisciplineReportController(AppDbContext db) : ControllerBase
{
    /// <summary>Sukut bo'yicha davr — oxirgi 30 kun (bugun ham kiradi).</summary>
    private const int DefaultDays = 29;

    /// <param name="from">Boshlanish sanasi "yyyy-MM-dd". Berilmasa — <paramref name="to"/> dan 30 kun oldin.</param>
    /// <param name="to">Tugash sanasi "yyyy-MM-dd". Berilmasa — bugun.</param>
    /// <param name="classId">Bitta sinf bo'yicha filtr (ixtiyoriy).</param>
    [HttpGet]
    public async Task<ActionResult<AttendanceDisciplineReportDto>> Get(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? classId)
    {
        var toDate = Parse(to) ?? AppClock.Today;
        var fromDate = Parse(from) ?? toDate.AddDays(-DefaultDays);
        // Sanalar teskari kelsa — almashtiramiz, xato qaytarish o'rniga: hisobot o'qish uchun,
        // bo'sh ekran sababini tushuntirmaydi.
        if (fromDate > toDate) (fromDate, toDate) = (toDate, fromDate);

        return await AttendanceDisciplineReport.BuildAsync(
            db,
            fromDate.ToString("yyyy-MM-dd"),
            toDate.ToString("yyyy-MM-dd"),
            string.IsNullOrWhiteSpace(classId) ? null : classId);
    }

    private static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? d
            : null;
}
