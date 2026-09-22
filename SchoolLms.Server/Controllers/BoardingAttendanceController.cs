using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Kechki dars va yotoqxona davomati (mijoz, 2026-09-23). Mantiq — <see cref="BoardingAttendanceService"/>.
///
/// <para><b>Ruxsat — sessiya bo'yicha, O'QISH ham</b>: kechki — <c>attendanceEvening</c>,
/// yotoqxona — <c>attendanceDorm</c>. Uch xil odam uch xil davomat oladi va biri ikkinchisining
/// ro'yxatini ko'rmasligi kerak, shuning uchun xodimning "hamma narsani o'qiydi" qoidasi bu yerda
/// ishlamaydi. Admin va superadmin — har doim.</para>
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/boarding-attendance")]
public class BoardingAttendanceController(BoardingAttendanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BoardingDayDto>> Get(
        [FromQuery] string date, [FromQuery] string session, CancellationToken ct = default)
    {
        if (Check(date, session, out var day) is { } bad) return bad;
        return await service.DayAsync(day, session, ct);
    }

    [HttpPut]
    public async Task<ActionResult<SaveBoardingResult>> Save(SaveBoardingRequest req, CancellationToken ct = default)
    {
        if (Check(req.Date, req.Session, out var day) is { } bad) return bad;
        if (day > AppClock.Today) return BadRequest(new { message = "Kelajakdagi kunga davomat qo'yib bo'lmaydi" });
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var (result, error) = await service.SaveAsync(day, req.Session, req.Marks ?? [], uid, ct);
        if (error is not null) return BadRequest(new { message = error });
        return result!;
    }

    private ActionResult? Check(string date, string session, out DateOnly day)
    {
        day = default;
        if (!BoardingSession.All.Contains(session))
            return BadRequest(new { message = "Sessiya noto'g'ri (evening yoki dorm)" });
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            return BadRequest(new { message = "Sana formati YYYY-MM-DD bo'lishi kerak" });
        if (!User.HasPerm(BoardingSession.PermissionOf(session))) return Forbid();
        return null;
    }
}
