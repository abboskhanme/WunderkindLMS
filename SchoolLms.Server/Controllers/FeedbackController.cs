using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Taklif va shikoyatlar — ota-onalar va o'qituvchilar ilova orqali yuboradi (StudentPortalController,
/// TeacherPortalController), maktab shu yerda ishlaydi: qabul qilindi → jarayonda → hal qilindi,
/// bo'lim va mas'ulga yo'naltirish, yuboruvchiga javob (client, 2026-10-07; FeedbackWorkflow.cs).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("feedback")]
[Route("api/admin/feedback")]
public class FeedbackController(AppDbContext db, TelegramService telegram, ILogger<FeedbackNotifier> notifierLogger) : ControllerBase
{
    private FeedbackNotifier Notifier => new(db, telegram, notifierLogger);

    private string ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private async Task<string> ActorNameAsync() =>
        await db.Users.AsNoTracking().Where(u => u.Id == ActorId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "";

    /// <param name="mine">Faqat menga biriktirilgan yoki men a'zo bo'lgan bo'limdagilar.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<FeedbackDto>>> GetAll(
        [FromQuery] string? type, [FromQuery] string? status,
        [FromQuery] Guid? departmentId = null, [FromQuery] bool mine = false)
    {
        var q = db.Feedbacks.AsQueryable();
        if (!string.IsNullOrEmpty(type)) q = q.Where(f => f.Type == type);
        if (!string.IsNullOrEmpty(status)) q = q.Where(f => f.Status == status);
        if (departmentId is { } dep) q = q.Where(f => f.DepartmentId == dep);
        if (mine)
        {
            var me = ActorId;
            var myDeps = db.FeedbackDepartmentMembers.Where(m => m.UserId == me).Select(m => m.DepartmentId);
            q = q.Where(f => f.AssigneeId == me || (f.DepartmentId != null && myDeps.Contains(f.DepartmentId.Value)));
        }
        var list = await q.OrderByDescending(f => f.CreatedAt).Take(300).ToListAsync();
        return await ToDtosAsync(list);
    }

    /// <summary>One item with its whole history.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<object>> Get(string id, CancellationToken ct)
    {
        var f = await db.Feedbacks.FindAsync([id], ct);
        if (f is null) return NotFound();
        return new { item = (await ToDtosAsync([f]))[0], history = await FeedbackWorkflowService.HistoryAsync(db, id, ct) };
    }

    /// <summary>
    /// One step: status, department, responsible person, answer to the sender, internal note.
    /// A new person or department hears from the bot; the sender hears about status and answer.
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<ActionResult<FeedbackDto>> Update(string id, FeedbackUpdateRequest request, CancellationToken ct)
    {
        var f = await db.Feedbacks.FindAsync([id], ct);
        if (f is null) return NotFound();
        var (error, changes) = await FeedbackWorkflowService.UpdateAsync(db, f, request, ActorId, await ActorNameAsync(), ct);
        if (error is not null) return BadRequest(new { message = error });
        await db.SaveChangesAsync(ct);
        if (changes.Routed) await Notifier.RoutedAsync(f, ct);
        if (changes.StatusChanged || changes.Answered) await Notifier.SenderAsync(f, ct);
        return (await ToDtosAsync([f]))[0];
    }

    /// <summary>"Hal qilindi" — kept for older screens; the same step as PATCH status=resolved.</summary>
    [HttpPost("{id}/resolve")]
    public async Task<IActionResult> Resolve(string id)
    {
        var f = await db.Feedbacks.FindAsync(id);
        if (f is null) return NotFound();
        if (f.Status == FeedbackStatus.Resolved) return NoContent();
        var (error, _) = await FeedbackWorkflowService.UpdateAsync(
            db, f, new FeedbackUpdateRequest(Status: FeedbackStatus.Resolved), ActorId, await ActorNameAsync());
        if (error is not null) return BadRequest(new { message = error });
        await db.SaveChangesAsync();
        await Notifier.SenderAsync(f);
        return NoContent();
    }

    // ---------- Bo'limlar ----------

    [HttpGet("departments")]
    public async Task<ActionResult<IEnumerable<FeedbackDepartmentDto>>> Departments(CancellationToken ct) =>
        await FeedbackWorkflowService.DepartmentsAsync(db, ct);

    [HttpPost("departments")]
    public async Task<ActionResult<FeedbackDepartmentDto>> CreateDepartment(FeedbackDepartmentPayload p, CancellationToken ct)
    {
        var row = new FeedbackDepartment();
        if (await FeedbackWorkflowService.SaveDepartmentAsync(db, row, p, isNew: true, ct) is { } error)
            return BadRequest(new { message = error });
        await db.SaveChangesAsync(ct);
        return (await FeedbackWorkflowService.DepartmentsAsync(db, ct)).Single(d => d.Id == row.Id);
    }

    [HttpPut("departments/{depId:guid}")]
    public async Task<ActionResult<FeedbackDepartmentDto>> UpdateDepartment(Guid depId, FeedbackDepartmentPayload p, CancellationToken ct)
    {
        var row = await db.FeedbackDepartments.FirstOrDefaultAsync(d => d.Id == depId, ct);
        if (row is null) return NotFound();
        if (await FeedbackWorkflowService.SaveDepartmentAsync(db, row, p, isNew: false, ct) is { } error)
            return BadRequest(new { message = error });
        await db.SaveChangesAsync(ct);
        return (await FeedbackWorkflowService.DepartmentsAsync(db, ct)).Single(d => d.Id == row.Id);
    }

    /// <summary>People who can be responsible: admins and staff (with or without inbox access).</summary>
    [HttpGet("assignees")]
    public async Task<ActionResult<IEnumerable<FeedbackDepartmentMemberDto>>> Assignees(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || u.Role == Roles.Staff)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Role, u.Permissions })
            .ToListAsync(ct);
        return users.Select(u => new FeedbackDepartmentMemberDto(u.Id, u.FullName, FeedbackWorkflowService.HasAccess(u.Role, u.Permissions))).ToList();
    }

    private async Task<List<FeedbackDto>> ToDtosAsync(List<Feedback> list)
    {
        var ids = list.Select(f => f.StudentId).Distinct().ToList();
        var students = await db.Students.Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync();
        var byId = students.ToDictionary(s => s.Id);
        var depIds = list.Where(f => f.DepartmentId != null).Select(f => f.DepartmentId!.Value).Distinct().ToList();
        var deps = await db.FeedbackDepartments.AsNoTracking().Where(d => depIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name);
        var userIds = list.Where(f => f.AssigneeId != null).Select(f => f.AssigneeId!).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        return list.Select(f => new FeedbackDto(
            f.Id,
            byId.TryGetValue(f.StudentId, out var s) ? s.FullName : "",
            f.ParentName,
            byId.TryGetValue(f.StudentId, out var s2) ? s2.ClassName : "",
            f.Type, f.Text, f.CreatedAt.ToString("o"), f.Status,
            string.IsNullOrEmpty(f.SenderRole) ? "parent" : f.SenderRole,
            f.SenderName, f.ImageUrl,
            f.DepartmentId,
            f.DepartmentId is { } d ? deps.GetValueOrDefault(d) : null,
            f.AssigneeId,
            f.AssigneeId is { } a ? users.GetValueOrDefault(a) : null,
            f.Answer, f.UpdatedAt?.ToString("o"), f.ResolvedAt?.ToString("o"))).ToList();
    }
}
