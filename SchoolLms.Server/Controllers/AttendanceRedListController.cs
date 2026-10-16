using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Attendance red list — admin side (docs/modules/attendance-red-list.md, 2026-10-15).
//
//    GET    /api/admin/attendance/red-list                 — status counts, rows, filter options
//                                                            (?status=open|aloqa_kerak|…|hal_boldi&className=&teacherId=&overdue=)
//    GET    /api/admin/attendance/red-list/{id}            — one case with its history
//    POST   /api/admin/attendance/red-list/{id}/actions    — status + comment (+ call-back day)
//    DELETE /api/admin/attendance/red-list/actions/{id}    — soft delete, the case takes the previous status
//    GET    /api/admin/attendance/red-list/open            — open case per pupil (links from the report)
//    GET    /api/admin/attendance/red-list/settings        — criteria and switches
//    PUT    /api/admin/attendance/red-list/settings        — save them (audited)
//    POST   /api/admin/attendance/red-list/scan            — "Hozir tekshirish"
//
//  Gate: the attendance section's rule — any staff reads (as /api/admin/attendance), writing needs the
//  `attendanceCases` key; admin and superadmin always; teachers and parents never (they have
//  /api/teacher/homeroom/red-list for their own pupils).
// ===========================================================================

[ApiController]
[Authorize]
[AdminPerm(AttendanceRedListService.PermissionKey)]
[Route("api/admin/attendance/red-list")]
public class AttendanceRedListController(AppDbContext db, TelegramService telegram) : ControllerBase
{
    private AttendanceRedListService Service => new(db, telegram);

    [HttpGet]
    public Task<ActionResult<AttendanceCaseListDto>> List(
        [FromQuery] string? status, [FromQuery] string? className, [FromQuery] string? teacherId,
        [FromQuery] bool overdue = false, CancellationToken ct = default) =>
        Run(() => Service.ListAsync(status, className, teacherId, overdue, ct));

    [HttpGet("{id:guid}")]
    public Task<ActionResult<AttendanceCaseDetailDto>> Detail(Guid id, CancellationToken ct) =>
        Run(() => Service.DetailAsync(id, null, ct));

    [HttpPost("{id:guid}/actions")]
    public async Task<ActionResult<AttendanceCaseDetailDto>> AddAction(
        Guid id, AddAttendanceCaseActionRequest req, CancellationToken ct)
    {
        var userId = CurrentUserId();
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName)
            .FirstOrDefaultAsync(ct) ?? "Noma'lum";
        return await Run(() => Service.AddActionAsync(
            id, req, new RedListActor(userId, name, AttendanceCaseActorType.Staff), null, ct));
    }

    [HttpDelete("actions/{actionId:guid}")]
    public Task<ActionResult<AttendanceCaseDetailDto>> DeleteAction(Guid actionId, CancellationToken ct) =>
        Run(() => Service.DeleteActionAsync(actionId, CurrentUserId(), ct));

    [HttpGet("open")]
    public Task<ActionResult<List<OpenAttendanceCaseDto>>> Open(CancellationToken ct) =>
        Run(() => Service.OpenCasesAsync(ct));

    [HttpGet("settings")]
    public Task<ActionResult<AttendanceRedListSettingsDto>> Settings(CancellationToken ct) =>
        Run(() => Service.SettingsDtoAsync(ct));

    [HttpPut("settings")]
    public Task<ActionResult<AttendanceRedListSettingsDto>> SaveSettings(
        SaveAttendanceRedListSettingsRequest req, CancellationToken ct) =>
        Run(() => Service.SaveSettingsAsync(req, CurrentUserId(), ct));

    [HttpPost("scan")]
    public Task<ActionResult<AttendanceScanResultDto>> Scan(CancellationToken ct) =>
        Run(() => Service.ScanAsync(AppClock.Today, ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (AttendanceCaseException e)
        {
            return StatusCode(e.Status, new { message = e.Message });
        }
    }

    private string CurrentUserId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";
}
