using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.HrTasks;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// The employee's own HR API — Telegram Mini App and "Mening topshiriqlarim" (docs/modules/hr-tasks.md
/// §6), a mirror of the old WunderkindHR <c>presentation/api/miniapp.py</c> (+ the panel's
/// <c>/hr/me/tasks</c> list). Any authenticated user (our JWT — web or Mini App) with an ACTIVE
/// <c>hr_employees</c> row; otherwise 403 <c>{ code: "hr_not_employee" }</c>. The employee id comes
/// from the account, never from the request — another person's task answers 403
/// <c>task_not_owned</c>. Error codes drop the <c>hr_</c> prefix like the old Mini App API
/// (<c>proof_required</c>, <c>task_not_editable</c> …). Every response is <c>no-store</c>.
/// </summary>
[ApiController]
[Authorize]
[NoStore]
[Route("api/hr/me")]
public sealed class HrMeController(HrTasksService service) : ControllerBase
{
    private const long UploadRequestLimit = 12 * 1024 * 1024;

    private CancellationToken Ct => HttpContext.RequestAborted;

    private static ObjectResult Fail(HrTasksException ex)
    {
        var code = ex.Code.StartsWith("hr_", StringComparison.Ordinal) && ex.Code != "hr_not_employee" ? ex.Code[3..] : ex.Code;
        return new ObjectResult(new { code, message = ex.Message }) { StatusCode = ex.StatusCode };
    }

    private async Task<ActionResult> Run<T>(Func<HrEmployee, Task<T>> action, int status = StatusCodes.Status200OK)
    {
        try
        {
            var me = await service.MeAsync(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "", Ct);
            var result = await action(me);
            return status == StatusCodes.Status200OK ? Ok(result) : StatusCode(status, result);
        }
        catch (HrTasksException ex) { return Fail(ex); }
    }

    /// <summary><c>{ employeeId, fullName, positionName, timezone, today }</c> — today by the SERVER's local date.</summary>
    [HttpGet]
    public Task<ActionResult> Me() => Run(me => service.ProfileAsync(me, Ct));

    /// <summary>
    /// Two shapes on one path (both old clients map here):
    /// <list type="bullet">
    ///   <item><c>?date=YYYY-MM-DD</c> (or no query) — the Mini App day list <c>{ date, items: Task[] }</c>;</item>
    ///   <item>any of <c>limit, cursor, withTotal, dateFrom, dateTo, status, overdue, accepted, q, sort</c>
    ///   without <c>date</c> — the panel page <c>{ items: HrTask[], nextCursor, hasMore, total? }</c>.</item>
    /// </list>
    /// </summary>
    [HttpGet("tasks")]
    public Task<ActionResult> Tasks(
        [FromQuery] DateOnly? date = null,
        [FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, [FromQuery] string[]? status = null,
        [FromQuery] bool? overdue = null, [FromQuery] bool? accepted = null, [FromQuery] string? q = null,
        [FromQuery] string? sort = null, [FromQuery] int? limit = null, [FromQuery] string? cursor = null,
        [FromQuery] bool? withTotal = null)
    {
        var wantsPage = !Request.Query.ContainsKey("date")
            && new[] { "limit", "cursor", "withTotal", "dateFrom", "dateTo", "status", "overdue", "accepted", "q", "sort" }
                .Any(Request.Query.ContainsKey);
        if (!wantsPage) return Run(me => service.DayAsync(me, date, Ct));
        return Run(me => service.MyTasksAsync(me,
            new HrTaskFilter(dateFrom, dateTo, null, null, status, null, overdue == true, null, accepted, q, sort),
            new HrPaging(limit ?? HrPagingDefaults.Limit, cursor, withTotal == true), Ct));
    }

    /// <summary>Always the day list (explicit alias of <c>GET tasks?date=</c>).</summary>
    [HttpGet("day")]
    public Task<ActionResult> Day([FromQuery] DateOnly? date = null) => Run(me => service.DayAsync(me, date, Ct));

    [HttpGet("tasks/{id}")]
    public Task<ActionResult> Task_(string id) => Run(me => service.MyTaskAsync(me, id, Ct));

    /// <summary>A task for oneself (<c>source = self</c>).</summary>
    [HttpPost("tasks")]
    public Task<ActionResult> Create(HrSelfTaskInput input) =>
        Run(me => service.CreateSelfTaskAsync(me, input, Ct), StatusCodes.Status201Created);

    /// <summary>"Bajardim" — a proof file OR a non-empty comment, else 409 <c>proof_required</c>. Body optional.</summary>
    [HttpPost("tasks/{id}/complete")]
    public Task<ActionResult> Complete(string id, [FromBody] HrCompleteInput? input = null) =>
        Run(me => service.CompleteAsync(me, id, input, Ct));

    /// <summary>Same local day, before today's digest — else 409 <c>task_not_editable</c>.</summary>
    [HttpPost("tasks/{id}/reopen")]
    public Task<ActionResult> Reopen(string id) => Run(me => service.ReopenAsync(me, id, Ct));

    [HttpPost("tasks/{id}/accept")]
    public Task<ActionResult> Accept(string id) => Run(me => service.AcceptAsync(me, id, Ct));

    /// <summary>A KPI step <c>{ amount: "12.50", note }</c>; the target reached → done.</summary>
    [HttpPost("tasks/{id}/progress")]
    public Task<ActionResult> Progress(string id, HrProgressInput input) => Run(me => service.AddProgressAsync(me, id, input, Ct));

    /// <summary>
    /// multipart: <c>file</c> + the task id as <c>taskId</c> or <c>task_id</c> (the Mini App sends both).
    /// ≤ 10 MB (422 <c>proof_too_large</c>), ≤ 5 per task (409), html/svg/js/xml refused (422).
    /// A voice note recorded on completion is an ordinary proof file.
    /// </summary>
    [HttpPost("proofs")]
    [RequestSizeLimit(UploadRequestLimit)]
    public async Task<ActionResult> UploadProof(IFormFile? file)
    {
        var form = await Request.ReadFormAsync(Ct);
        var taskId = form["taskId"].FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                     ?? form["task_id"].FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (file is null || string.IsNullOrWhiteSpace(taskId))
            return BadRequest(new { code = "validation_error", message = "Fayl va topshiriq (taskId) kerak" });
        if (file.Length > HrLimits.MaxProofBytes) return Fail(HrErrors.ProofTooLarge(HrLimits.MaxProofBytes));
        var upload = await HrTasksController.ReadAsync(file, HrLimits.MaxProofBytes, Ct);
        return await Run(me => service.UploadProofAsync(me, taskId, upload, Ct), StatusCodes.Status201Created);
    }

    /// <summary>"Natijam" — this month.</summary>
    [HttpGet("summary")]
    public Task<ActionResult> Summary() => Run(me => service.MySummaryAsync(me, Ct));
}
