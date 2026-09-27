using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.HrTasks;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// HR → Topshiriqlar — the panel API (docs/modules/hr-tasks.md §6), a mirror of the old
/// WunderkindHR <c>presentation/api/router.py</c>: positions, employees (+ tree, managers,
/// invite, deactivate), templates (+ toggle, batch), tasks (+ batch, return, cancel),
/// attachments, reports (+ export), settings and the dashboard.
///
/// <para>
/// Access: admin / superadmin, and staff whose role carries <c>hrTasks</c> (<c>GatedRead</c>:
/// <c>hrTasks:view</c> reads only). WHICH rows is the scope (the service): directors see the whole
/// organisation, managers self + their subtree, and anything outside answers 404. Positions
/// writes and settings are for directors only (403 <c>forbidden</c>). Every response is
/// <c>no-store</c>. Errors are <c>{ code, message }</c> with the old codes (<c>hr_*</c>).
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm(HrTaskAccess.PermissionKey, GatedRead = true)]
[NoStore]
[Route("api/admin/hr")]
public sealed class HrTasksController(HrTasksService service, AppDbContext db) : ControllerBase
{
    /// <summary>Upload ceiling: the 10 MB limit + multipart overhead, so our own 413 answers (not Kestrel's).</summary>
    private const long UploadRequestLimit = 12 * 1024 * 1024;

    private CancellationToken Ct => HttpContext.RequestAborted;

    private HrActor Actor
    {
        get
        {
            var role = User.IsInRole(Roles.SuperAdmin) ? Roles.SuperAdmin
                : User.IsInRole(Roles.Admin) ? Roles.Admin
                : Roles.Staff;
            var perms = User.Claims.Where(c => c.Type == AdminPermAttribute.ClaimType).Select(c => c.Value).ToHashSet();
            return new HrActor(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "", role, perms);
        }
    }

    internal static ObjectResult Fail(HrTasksException ex) =>
        new(new { code = ex.Code, message = ex.Message }) { StatusCode = ex.StatusCode };

    private async Task<ActionResult> Run<T>(Func<HrPanel, Task<T>> action, int status = StatusCodes.Status200OK)
    {
        try
        {
            var panel = await service.PanelAsync(Actor, Ct);
            var result = await action(panel);
            return status == StatusCodes.Status200OK ? Ok(result) : StatusCode(status, result);
        }
        catch (HrTasksException ex) { return Fail(ex); }
    }

    private async Task<ActionResult> RunNoContent(Func<HrPanel, Task> action)
    {
        try
        {
            await action(await service.PanelAsync(Actor, Ct));
            return NoContent();
        }
        catch (HrTasksException ex) { return Fail(ex); }
    }

    private static HrPaging Paging(int? limit, string? cursor, bool withTotal) =>
        new(limit ?? HrPagingDefaults.Limit, cursor, withTotal);

    // ---------- Who am I in HR ----------

    /// <summary>What the panel may show: director?, manage?, scope ("all" | "subtree"), own HR row.</summary>
    [HttpGet("access")]
    public Task<ActionResult> Access() => Run(p => Task.FromResult(service.Access(p)));

    /// <summary>"Pick an existing user" candidates for a new employee.</summary>
    [HttpGet("users")]
    public Task<ActionResult> Users([FromQuery] string? q = null) => Run(_ => service.UserCandidatesAsync(q, Ct));

    // ---------- Positions ----------

    [HttpGet("positions")]
    public Task<ActionResult> Positions(
        [FromQuery] string? q = null, [FromQuery] bool? isActive = null,
        [FromQuery] int? limit = null, [FromQuery] string? cursor = null, [FromQuery] bool withTotal = false) =>
        Run(_ => service.PositionsAsync(q, isActive, Paging(limit, cursor, withTotal), Ct));

    /// <summary>Directors only.</summary>
    [HttpPost("positions")]
    public Task<ActionResult> CreatePosition(HrPositionCreateInput input) =>
        Run(p => service.CreatePositionAsync(p, input, Ct), StatusCodes.Status201Created);

    /// <summary>Directors only. Absent fields are untouched.</summary>
    [HttpPatch("positions/{id}")]
    public Task<ActionResult> UpdatePosition(string id, [FromBody] JsonElement body) =>
        Run(p => service.UpdatePositionAsync(p, id, body, Ct));

    /// <summary>Directors only. 409 <c>hr_position_in_use</c> while employees or templates point to it.</summary>
    [HttpPost("positions/{id}/deactivate")]
    public Task<ActionResult> DeactivatePosition(string id) => Run(p => service.DeactivatePositionAsync(p, id, Ct));

    // ---------- Employees ----------

    [HttpGet("employees")]
    public Task<ActionResult> Employees(
        [FromQuery] string? q = null, [FromQuery] string? positionId = null, [FromQuery] bool? isActive = null,
        [FromQuery] bool? linked = null, [FromQuery] string? branch = null, [FromQuery] string? sort = null,
        [FromQuery] int? limit = null, [FromQuery] string? cursor = null, [FromQuery] bool withTotal = false) =>
        Run(p => service.EmployeesAsync(p, new HrEmployeeFilter(q, positionId, isActive, linked, branch, sort),
            Paging(limit, cursor, withTotal), Ct));

    /// <summary>
    /// An existing user (<c>userId</c>) or name + phone → a new <c>staff</c> user (login = phone
    /// digits); its one-time password is in <c>credentials</c>. A manager's new employee lands
    /// under the manager.
    /// </summary>
    [HttpPost("employees")]
    public Task<ActionResult> CreateEmployee(HrEmployeeCreateInput input) =>
        Run(p => service.CreateEmployeeAsync(p, input, NewStaffUser, Ct), StatusCodes.Status201Created);

    private (AppUser User, string Password) NewStaffUser(string fullName, string login, string displayPhone)
    {
        var user = AccountFactory.CreateAccountFor(db, Roles.Staff, fullName, out var password);
        user.Email = login;
        user.Phone = displayPhone;
        return (user, password);
    }

    /// <summary>Declared before <c>employees/{id}</c> for readability; ASP.NET ranks literal segments first anyway.</summary>
    [HttpGet("employees/tree")]
    public Task<ActionResult> Tree(
        [FromQuery] string? q = null, [FromQuery] string? positionId = null, [FromQuery] bool? isActive = null,
        [FromQuery] bool? linked = null, [FromQuery] string? branch = null) =>
        Run(p => service.TreeAsync(p, new HrEmployeeFilter(q, positionId, isActive, linked, branch), Ct));

    [HttpGet("employees/{id}")]
    public Task<ActionResult> Employee(string id) => Run(p => service.EmployeeAsync(p, id, Ct));

    [HttpPatch("employees/{id}")]
    public Task<ActionResult> UpdateEmployee(string id, [FromBody] JsonElement body) =>
        Run(p => service.UpdateEmployeeAsync(p, id, body, Ct));

    /// <summary>The list is replaced; a cycle is 409 <c>hr_manager_cycle</c>.</summary>
    [HttpPut("employees/{id}/managers")]
    public Task<ActionResult> SetManagers(string id, HrManagersInput input) =>
        Run(p => service.SetManagersAsync(p, id, input.ManagerIds ?? [], Ct));

    [HttpPost("employees/{id}/deactivate")]
    public Task<ActionResult> DeactivateEmployee(string id) => Run(p => service.DeactivateEmployeeAsync(p, id, Ct));

    /// <summary>A one-time Telegram link code (shown once) + the bot link.</summary>
    [HttpPost("employees/{id}/invite")]
    public Task<ActionResult> Invite(string id) => Run(p => service.InviteAsync(p, id, Ct));

    // ---------- Templates ----------

    [HttpGet("templates")]
    public Task<ActionResult> Templates(
        [FromQuery] string? q = null, [FromQuery] bool? isActive = null, [FromQuery] string? targetType = null,
        [FromQuery] string? targetEmployeeId = null, [FromQuery] string? targetPositionId = null,
        [FromQuery] string? recurrence = null, [FromQuery] string? branch = null, [FromQuery] string? sort = null,
        [FromQuery] int? limit = null, [FromQuery] string? cursor = null, [FromQuery] bool withTotal = false) =>
        Run(p => service.TemplatesAsync(p,
            new HrTemplateFilter(q, isActive, targetType, targetEmployeeId, targetPositionId, recurrence, branch, sort),
            Paging(limit, cursor, withTotal), Ct));

    [HttpPost("templates")]
    public Task<ActionResult> CreateTemplate(HrTemplateCreateInput input) =>
        Run(p => service.CreateTemplateAsync(p, input, Ct), StatusCodes.Status201Created);

    /// <summary>One employee-targeted template per employee × item. Limits 100 / 20 / 500.</summary>
    [HttpPost("templates/batch")]
    public Task<ActionResult> CreateTemplateBatch(HrTemplateBatchInput input) =>
        Run(p => service.CreateTemplateBatchAsync(p, input, Ct), StatusCodes.Status201Created);

    [HttpPatch("templates/{id}")]
    public Task<ActionResult> UpdateTemplate(string id, [FromBody] JsonElement body) =>
        Run(p => service.UpdateTemplateAsync(p, id, body, Ct));

    /// <summary><c>?isActive=</c> sets it; without — flips.</summary>
    [HttpPost("templates/{id}/toggle")]
    public Task<ActionResult> ToggleTemplate(string id, [FromQuery] bool? isActive = null) =>
        Run(p => service.ToggleTemplateAsync(p, id, isActive, Ct));

    /// <summary>The rule goes; its tasks stay with <c>templateId = null</c>.</summary>
    [HttpDelete("templates/{id}")]
    public Task<ActionResult> DeleteTemplate(string id) => RunNoContent(p => service.DeleteTemplateAsync(p, id, Ct));

    // ---------- Tasks ----------

    /// <summary><c>?status=open&amp;status=returned</c>; dates filter the LOCAL date of <c>dueAt</c>; <c>overdue</c> ignores dates.</summary>
    [HttpGet("tasks")]
    public Task<ActionResult> Tasks(
        [FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, [FromQuery] string? employeeId = null,
        [FromQuery] string? positionId = null, [FromQuery] string[]? status = null, [FromQuery] string[]? source = null,
        [FromQuery] bool? overdue = null, [FromQuery] string? branch = null, [FromQuery] bool? accepted = null,
        [FromQuery] string? q = null, [FromQuery] string? sort = null,
        [FromQuery] int? limit = null, [FromQuery] string? cursor = null, [FromQuery] bool withTotal = false) =>
        Run(p => service.TasksAsync(p,
            new HrTaskFilter(dateFrom, dateTo, employeeId, positionId, status, source, overdue == true, branch, accepted, q, sort),
            Paging(limit, cursor, withTotal), Ct));

    /// <summary>The employee is told on Telegram immediately.</summary>
    [HttpPost("tasks")]
    public Task<ActionResult> CreateTask(HrTaskCreateInput input) =>
        Run(p => service.CreateTaskAsync(p, input, Ct), StatusCodes.Status201Created);

    /// <summary>employees × items in one transaction; one message per employee. Limits 100 / 20 / 500.</summary>
    [HttpPost("tasks/batch")]
    public Task<ActionResult> CreateTaskBatch(HrTaskBatchInput input) =>
        Run(p => service.CreateTaskBatchAsync(p, input, Ct), StatusCodes.Status201Created);

    [HttpGet("tasks/{id}")]
    public Task<ActionResult> Task_(string id) => Run(p => service.TaskAsync(p, id, Ct));

    [HttpPatch("tasks/{id}")]
    public Task<ActionResult> UpdateTask(string id, [FromBody] JsonElement body) =>
        Run(p => service.UpdateTaskAsync(p, id, body, Ct));

    [HttpPost("tasks/{id}/return")]
    public Task<ActionResult> ReturnTask(string id, HrTaskReturnInput input) =>
        Run(p => service.ReturnTaskAsync(p, id, input.Comment, Ct));

    /// <summary>The body is optional (<c>{"comment": null}</c> is accepted).</summary>
    [HttpPost("tasks/{id}/cancel")]
    public async Task<ActionResult> CancelTask(string id)
    {
        string? comment = null;
        if (Request.ContentLength is > 0 || Request.Headers.TransferEncoding.Count > 0)
        {
            try
            {
                var body = await JsonSerializer.DeserializeAsync<HrTaskCancelInput>(
                    Request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), Ct);
                comment = body?.Comment;
            }
            catch (JsonException) { return BadRequest(new { code = "validation_error", message = "JSON noto'g'ri" }); }
        }
        return await Run(p => service.CancelTaskAsync(p, id, comment, Ct));
    }

    // ---------- Attachments (a manager's voice note) ----------

    /// <summary>
    /// multipart: <c>file</c> + <c>kind</c> (voice | file) [+ <c>durationSeconds</c>]. Returns a DRAFT
    /// (<c>taskId: null</c>) whose id goes to <c>items[].attachmentId</c> of <c>tasks/batch</c>.
    /// 413 over 10 MB, 415 for html/svg/js/xml, 422 for a voice over 5 minutes.
    /// </summary>
    [HttpPost("attachments")]
    [RequestSizeLimit(UploadRequestLimit)]
    public async Task<ActionResult> UploadAttachment(
        IFormFile? file, [FromForm] string? kind = null, [FromForm] int? durationSeconds = null)
    {
        if (file is null) return BadRequest(new { code = "validation_error", message = "Fayl yuborilmadi" });
        if (file.Length > HrLimits.MaxAttachmentBytes) return Fail(HrErrors.AttachmentTooLarge(HrLimits.MaxAttachmentBytes));
        var upload = await ReadAsync(file, HrLimits.MaxAttachmentBytes, Ct);
        return await Run(p => service.UploadAttachmentAsync(p, upload, kind, durationSeconds, Ct), StatusCodes.Status201Created);
    }

    [HttpDelete("attachments/{id}")]
    public Task<ActionResult> DeleteAttachment(string id) => RunNoContent(p => service.DeleteAttachmentAsync(p, id, Ct));

    /// <summary>Reads at most <paramref name="max"/> + 1 bytes — a huge body is never buffered whole.</summary>
    internal static async Task<HrUpload> ReadAsync(IFormFile file, long max, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max) break;
        }
        return new HrUpload(buffer.ToArray(), file.FileName ?? "", file.ContentType ?? "");
    }

    // ---------- Reports ----------

    /// <summary>Default period: the 1st of this month → today.</summary>
    [HttpGet("reports/summary")]
    public Task<ActionResult> Summary([FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, [FromQuery] string? branch = null) =>
        Run(p => service.SummaryAsync(p.Scope, dateFrom, dateTo, branch, Ct));

    [HttpGet("reports/employees")]
    public Task<ActionResult> EmployeeReport([FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, [FromQuery] string? branch = null) =>
        Run(p => service.EmployeeReportAsync(p.Scope, dateFrom, dateTo, branch, Ct));

    /// <summary>The old CSV (<c>;</c>, BOM — opens in Excel).</summary>
    [HttpGet("reports/export.csv")]
    public async Task<ActionResult> ExportCsv([FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, [FromQuery] string? branch = null)
    {
        try
        {
            var panel = await service.PanelAsync(Actor, Ct);
            var report = await service.EmployeeReportAsync(panel.Scope, dateFrom, dateTo, branch, Ct);
            return File(HrTasksService.ReportCsv(report), "text/csv; charset=utf-8",
                $"hr-hisobot-{report.DateFrom:yyyy-MM-dd}_{report.DateTo:yyyy-MM-dd}.csv");
        }
        catch (HrTasksException ex) { return Fail(ex); }
    }

    /// <summary>The same table as .xlsx (<see cref="ExcelExport"/>).</summary>
    [HttpGet("reports/export.xlsx")]
    public async Task<ActionResult> ExportXlsx([FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, [FromQuery] string? branch = null)
    {
        try
        {
            var panel = await service.PanelAsync(Actor, Ct);
            var report = await service.EmployeeReportAsync(panel.Scope, dateFrom, dateTo, branch, Ct);
            var xlsx = ExcelExport.Build("Hisobot", HrTasksService.ReportHeaders, HrTasksService.ReportRows(report));
            return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"hr-hisobot-{report.DateFrom:yyyy-MM-dd}_{report.DateTo:yyyy-MM-dd}.xlsx");
        }
        catch (HrTasksException ex) { return Fail(ex); }
    }

    // ---------- Settings (directors) and dashboard ----------

    [HttpGet("settings")]
    public Task<ActionResult> Settings() => Run(p => service.SettingsAsync(p, Ct));

    /// <summary>The whole object (<c>PUT</c>).</summary>
    [HttpPut("settings")]
    public Task<ActionResult> UpdateSettings(HrSettingsInput input) => Run(p => service.UpdateSettingsAsync(p, input, Ct));

    [HttpGet("dashboard")]
    public Task<ActionResult> Dashboard() => Run(p => service.DashboardAsync(p, Ct));
}
