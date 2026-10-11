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

    // ---------- Ro'yxat — qo'lda (mijoz, 2026-10-11: "o'quvchilarni qo'lda qo'shadigan qilish") ----------

    /// <summary>Sessiyaning hozirgi ro'yxati.</summary>
    [HttpGet("roster")]
    public async Task<ActionResult<List<BoardingRosterRowDto>>> Roster([FromQuery] string session, CancellationToken ct = default)
    {
        if (CheckSession(session, write: false) is { } bad) return bad;
        return await service.RosterAsync(session, ct);
    }

    /// <summary>Ro'yxatga qo'shish uchun nomzodlar (ro'yxatda yo'q faol o'quvchilar, qidiruv bilan).</summary>
    [HttpGet("roster/candidates")]
    public async Task<ActionResult<List<BoardingCandidateDto>>> Candidates(
        [FromQuery] string session, [FromQuery] string? q, CancellationToken ct = default)
    {
        if (CheckSession(session, write: true) is { } bad) return bad;
        return await service.CandidatesAsync(session, q, ct);
    }

    /// <summary>Ro'yxatga qo'shish — bugundan.</summary>
    [HttpPost("roster")]
    public async Task<ActionResult<object>> AddToRoster(AddBoardingRosterRequest req, CancellationToken ct = default)
    {
        if (CheckSession(req.Session, write: true) is { } bad) return bad;
        var (added, error) = await service.AddAsync(req.Session, req.StudentIds ?? [], Uid, ct);
        if (error is not null) return BadRequest(new { message = error });
        return Ok(new { added });
    }

    /// <summary>Ro'yxatdan chiqarish — bugundan; o'tgan kunlardagi davomati saqlanadi.</summary>
    [HttpPost("roster/remove")]
    public async Task<IActionResult> RemoveFromRoster(RemoveBoardingRosterRequest req, CancellationToken ct = default)
    {
        if (CheckSession(req.Session, write: true) is { } bad) return bad;
        return await service.RemoveAsync(req.Session, req.StudentId, Uid, ct)
            ? NoContent()
            : NotFound(new { message = "O'quvchi bu ro'yxatda yo'q" });
    }

    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private ActionResult? CheckSession(string session, bool write)
    {
        if (!BoardingSession.All.Contains(session))
            return BadRequest(new { message = "Sessiya noto'g'ri (evening yoki dorm)" });
        var perm = BoardingSession.PermissionOf(session);
        return (write ? User.HasPerm(perm) : User.HasReadPerm(perm)) ? null : Forbid();
    }

    private ActionResult? Check(string date, string session, out DateOnly day)
    {
        day = default;
        if (!BoardingSession.All.Contains(session))
            return BadRequest(new { message = "Sessiya noto'g'ri (evening yoki dorm)" });
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            return BadRequest(new { message = "Sana formati YYYY-MM-DD bo'lishi kerak" });
        var perm = BoardingSession.PermissionOf(session);
        var allowed = HttpMethods.IsGet(Request.Method) ? User.HasReadPerm(perm) : User.HasPerm(perm);
        if (!allowed) return Forbid();
        return null;
    }
}
