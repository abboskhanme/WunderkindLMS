using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Hubs;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quvchilar turniketi — har o'quvchining kunlik turniket/FaceID kirgan va chiqqan vaqti.
/// Ma'lumot turniket integratsiyasidan keladi (xom hodisalar Student.DeviceUserId bo'yicha moslanadi).
/// Tarix hodisalar jurnalida (TurnstileEvent) saqlanadi — istalgan kunni ko'rish mumkin.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/students/turnstile")]
public class StudentTurnstileController(
    AppDbContext db, TurnstileService turnstile, IHubContext<LiveHub> live) : ControllerBase
{
    /// <summary>Tanlangan kun (yyyy-MM-dd) uchun o'quvchilar turniketi: FISH, sinf, kirgan/chiqqan vaqt.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<StudentTurnstileDashboardDto>> Dashboard([FromQuery] string? date)
    {
        var d = string.IsNullOrEmpty(date) || date.Length < 10 ? AppClock.Today.ToString("yyyy-MM-dd") : date[..10];
        return await turnstile.BuildStudentDashboardAsync(db, d);
    }

    /// <summary>Turniket qurilmasidan so'nggi hodisalarni tortib oladi (o'quvchi/o'qituvchi — barchasi bir sync).</summary>
    [HttpPost("sync")]
    public async Task<ActionResult<TurnstileSyncResultDto>> Sync()
    {
        var res = await turnstile.SyncAsync(db);
        if (res.Ok && res.EventsFetched > 0)
            await live.Clients.Group(LiveHub.Group("turnstile"))
                .SendAsync("turnstileChanged", new { at = AppClock.Iso() });
        return res;
    }

    /// <summary>O'quvchiga turniket qurilma ID'sini (employeeNo) biriktiradi. Bo'sh = aloqani uzadi.</summary>
    [HttpPut("device")]
    public async Task<IActionResult> SetDevice(SetStudentDeviceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.StudentId))
            return BadRequest(new { message = "O'quvchi ko'rsatilishi shart" });
        var s = await db.Students.FindAsync(req.StudentId);
        if (s is null) return NotFound();
        s.DeviceUserId = (req.DeviceUserId ?? "").Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }
}
