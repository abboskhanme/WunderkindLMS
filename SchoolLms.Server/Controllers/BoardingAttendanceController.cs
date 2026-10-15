using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Kechki dars va yotoqxona davomati (mijoz, 2026-09-23). Mantiq — <see cref="BoardingAttendanceService"/>.
///
/// <para><b>Ruxsat — sessiya bo'yicha, O'QISH ham</b>: kechki — <c>attendanceEvening</c>,
/// yotoqxona — <c>attendanceDorm</c>. Uch xil odam uch xil davomat oladi va biri ikkinchisining
/// ro'yxatini ko'rmasligi kerak, shuning uchun xodimning "hamma narsani o'qiydi" qoidasi bu yerda
/// ishlamaydi. Admin va superadmin — har doim.</para>
/// <para>Teachers too (2026-10-12): a teacher who is also a commandant gets the same keys through an
/// access role on the teacher account and marks from the Mini App — one login. Every action here
/// checks the session's key, so a teacher without the role gets 403.</para>
/// <para>Dorm by gender (2026-10-15): a role may also carry one of the <see cref="DormGender"/> flags — its holder
/// works with only girls or only boys, everywhere on this controller (day, save, roster, statistics).</para>
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff,teacher")]
[Route("api/admin/boarding-attendance")]
public class BoardingAttendanceController(BoardingAttendanceService service, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BoardingDayDto>> Get(
        [FromQuery] string date, [FromQuery] string session, CancellationToken ct = default)
    {
        if (Check(date, session, out var day) is { } bad) return bad;
        return await service.DayAsync(day, session, GenderOf(session), ct);
    }

    [HttpPut]
    public async Task<ActionResult<SaveBoardingResult>> Save(SaveBoardingRequest req, CancellationToken ct = default)
    {
        if (Check(req.Date, req.Session, out var day) is { } bad) return bad;
        if (day > AppClock.Today) return BadRequest(new { message = "Kelajakdagi kunga davomat qo'yib bo'lmaydi" });
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var (result, error) = await service.SaveAsync(day, req.Session, req.Marks ?? [], uid, GenderOf(req.Session), ct);
        if (error is not null) return BadRequest(new { message = error });
        return result!;
    }

    // ---------- Statistika — "Davomat analitikasi 2.0" (2026-10-11) ----------

    /// <summary>Kechki dars yoki yotoqxona statistikasi davr bo'yicha (o'qish ruxsati — sessiyaniki).</summary>
    [HttpGet("analytics")]
    public async Task<ActionResult<BoardingAnalyticsDto>> Analytics(
        [FromQuery] string session, [FromQuery] string from, [FromQuery] string to, CancellationToken ct = default)
    {
        if (CheckRange(session, from, to, out var f, out var t) is { } bad) return bad;
        return await BoardingAnalytics.BuildAsync(db, session, f, t, gender: GenderOf(session), ct: ct);
    }

    /// <summary>Statistika Excel'da: kunlar, guruhlar, o'quvchilar, kunlar va sabablar.</summary>
    [HttpGet("analytics/export")]
    public async Task<IActionResult> AnalyticsExport(
        [FromQuery] string session, [FromQuery] string from, [FromQuery] string to, CancellationToken ct = default)
    {
        if (CheckRange(session, from, to, out var f, out var t) is { } bad) return bad;
        var d = await BoardingAnalytics.BuildAsync(db, session, f, t, maxEvents: 50_000, gender: GenderOf(session), ct: ct);
        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        return File(BoardingAnalyticsExport.Build(d, reasons),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{(session == BoardingSession.Dorm ? "yotoqxona" : "kechki_dars")}_{d.From}_{d.To}.xlsx");
    }

    private ActionResult? CheckRange(string session, string from, string to, out DateOnly f, out DateOnly t)
    {
        f = t = default;
        if (CheckSession(session, write: false) is { } bad) return bad;
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out f)
            || !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
            return BadRequest(new { message = "Sana formati YYYY-MM-DD bo'lishi kerak" });
        return BoardingAnalytics.RangeError(f, t) is { } error ? BadRequest(new { message = error }) : null;
    }

    // ---------- Ro'yxat — qo'lda (mijoz, 2026-10-11: "o'quvchilarni qo'lda qo'shadigan qilish") ----------

    /// <summary>Sessiyaning hozirgi ro'yxati.</summary>
    [HttpGet("roster")]
    public async Task<ActionResult<List<BoardingRosterRowDto>>> Roster([FromQuery] string session, CancellationToken ct = default)
    {
        if (CheckSession(session, write: false) is { } bad) return bad;
        return await service.RosterAsync(session, GenderOf(session), ct);
    }

    /// <summary>Ro'yxatga qo'shish uchun nomzodlar (ro'yxatda yo'q faol o'quvchilar, qidiruv bilan).</summary>
    [HttpGet("roster/candidates")]
    public async Task<ActionResult<List<BoardingCandidateDto>>> Candidates(
        [FromQuery] string session, [FromQuery] string? q, CancellationToken ct = default)
    {
        if (CheckSession(session, write: true) is { } bad) return bad;
        return await service.CandidatesAsync(session, q, GenderOf(session), ct);
    }

    /// <summary>Ro'yxatga qo'shish — bugundan.</summary>
    [HttpPost("roster")]
    public async Task<ActionResult<object>> AddToRoster(AddBoardingRosterRequest req, CancellationToken ct = default)
    {
        if (CheckSession(req.Session, write: true) is { } bad) return bad;
        var (added, error) = await service.AddAsync(req.Session, req.StudentIds ?? [], Uid, GenderOf(req.Session), ct);
        if (error is not null) return BadRequest(new { message = error });
        return Ok(new { added });
    }

    /// <summary>Ro'yxatdan chiqarish — bugundan; o'tgan kunlardagi davomati saqlanadi.</summary>
    [HttpPost("roster/remove")]
    public async Task<IActionResult> RemoveFromRoster(RemoveBoardingRosterRequest req, CancellationToken ct = default)
    {
        if (CheckSession(req.Session, write: true) is { } bad) return bad;
        return await service.RemoveAsync(req.Session, req.StudentId, Uid, GenderOf(req.Session), ct)
            ? NoContent()
            : NotFound(new { message = "O'quvchi bu ro'yxatda yo'q" });
    }

    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>The half of the dormitory this user works with (<see cref="DormGender"/>); null — all of it.</summary>
    private string? GenderOf(string session)
    {
        if (session != BoardingSession.Dorm || User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin)) return null;
        bool Holds(string key) => User.HasClaim(AdminPermAttribute.ClaimType, key);
        return DormGender.Of(Holds(DormGender.GirlsOnlyKey), Holds(DormGender.BoysOnlyKey));
    }

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
