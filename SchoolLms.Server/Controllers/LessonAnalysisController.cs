using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Boshqaruv → Dars tahlili (docs/modules/lesson-analysis.md §5): lesson observations,
/// checklist criteria, the teacher rating board, dashboard and analytics.
///
/// <para>
/// Access: admin / superadmin, and staff whose role carries <c>lessonAnalysis</c> (the old
/// MANAGER). <c>GatedRead</c>: a staff account needs the key to READ too — the rating board
/// is personnel data. Admin-only (the old ADMIN): delete an observation, create / patch
/// criteria. A COMPLETED observation is corrected only by its observer or an admin (the
/// service answers 403). Every response is <c>no-store</c>.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm(LessonAnalysisAccess.PermissionKey, GatedRead = true)]
[NoStore]
[Route("api/admin/lesson-analysis")]
public sealed class LessonAnalysisController(LessonAnalysisService service) : ControllerBase
{
    private bool IsAdmin => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin);

    private LessonAnalysisActor Actor => new(
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "", IsAdmin);

    private static ActionResult Fail(LessonAnalysisException ex) =>
        new ObjectResult(new { message = ex.Message }) { StatusCode = ex.StatusCode };

    private async Task<ActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (LessonAnalysisException ex) { return Fail(ex); }
    }

    // ---------- Criteria ----------

    [HttpGet("criteria")]
    public Task<ActionResult> Criteria([FromQuery] bool includeInactive = false) =>
        Run(() => service.CriteriaAsync(includeInactive, HttpContext.RequestAborted));

    /// <summary>Admin only.</summary>
    [HttpPost("criteria")]
    public async Task<ActionResult> CreateCriterion(LaCriterionCreateDto input)
    {
        if (!IsAdmin) return Forbid();
        try
        {
            var created = await service.CreateCriterionAsync(input, HttpContext.RequestAborted);
            return StatusCode(StatusCodes.Status201Created, created);
        }
        catch (LessonAnalysisException ex) { return Fail(ex); }
    }

    /// <summary>Admin only. text / orderIndex / isActive — no delete, deactivate instead.</summary>
    [HttpPatch("criteria/{id}")]
    public async Task<ActionResult> PatchCriterion(string id, LaCriterionPatchDto input)
    {
        if (!IsAdmin) return Forbid();
        return await Run(() => service.PatchCriterionAsync(id, input, HttpContext.RequestAborted));
    }

    // ---------- Teachers (ours) ----------

    [HttpGet("teachers")]
    public Task<ActionResult> Teachers([FromQuery] string? q = null) =>
        Run(() => service.TeachersAsync(q, HttpContext.RequestAborted));

    [HttpGet("teachers/random")]
    public Task<ActionResult> RandomTeacher() =>
        Run(() => service.RandomTeacherAsync(HttpContext.RequestAborted));

    [HttpGet("teachers/{id}")]
    public Task<ActionResult> Teacher(string id) =>
        Run(() => service.TeacherAsync(id, HttpContext.RequestAborted));

    /// <summary>That teacher's timetable lessons on <paramref name="date"/> (yyyy-MM-dd).</summary>
    [HttpGet("teachers/{id}/lessons")]
    public async Task<ActionResult> TeacherLessons(string id, [FromQuery] DateOnly? date)
    {
        if (date is null) return BadRequest(new { message = "date kerak (yyyy-MM-dd)" });
        return await Run(() => service.TeacherLessonsAsync(id, date.Value, HttpContext.RequestAborted));
    }

    // ---------- Observations ----------

    [HttpGet("observations")]
    public Task<ActionResult> Observations(
        [FromQuery] string? teacherId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0) =>
        Run(() => service.ListAsync(
            new LessonObservationFilter(teacherId, string.IsNullOrEmpty(status) ? null : status, dateFrom, dateTo, limit, offset),
            HttpContext.RequestAborted));

    [HttpPost("observations")]
    public async Task<ActionResult> Create(LaObservationInputDto input)
    {
        try
        {
            var created = await service.CreateAsync(input, Actor, HttpContext.RequestAborted);
            return Created($"/api/admin/lesson-analysis/observations/{created.Id}", created);
        }
        catch (LessonAnalysisException ex) { return Fail(ex); }
    }

    [HttpGet("observations/{id}")]
    public Task<ActionResult> Get(string id) =>
        Run(() => service.GetAsync(id, Actor, HttpContext.RequestAborted));

    /// <summary>Only the fields present in the body change (old PATCH semantics).</summary>
    [HttpPatch("observations/{id}")]
    public Task<ActionResult> Update(string id, [FromBody] JsonElement body) =>
        Run(() => service.UpdateAsync(id, body, Actor, HttpContext.RequestAborted));

    /// <summary>Finalise + AI summary (best-effort). Idempotent on a completed observation.</summary>
    [HttpPost("observations/{id}/complete")]
    public Task<ActionResult> Complete(string id) =>
        Run(() => service.CompleteAsync(id, Actor, HttpContext.RequestAborted));

    [HttpPost("observations/{id}/ai-summary")]
    public Task<ActionResult> RegenerateSummary(string id) =>
        Run(() => service.RegenerateSummaryAsync(id, Actor, HttpContext.RequestAborted));

    /// <summary>Admin only.</summary>
    [HttpDelete("observations/{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        if (!IsAdmin) return Forbid();
        try
        {
            await service.DeleteAsync(id, HttpContext.RequestAborted);
            return NoContent();
        }
        catch (LessonAnalysisException ex) { return Fail(ex); }
    }

    [HttpGet("observations/{id}/report.pdf")]
    public async Task<ActionResult> ReportPdf(string id)
    {
        try
        {
            var (pdf, name) = await service.ReportPdfAsync(id, HttpContext.RequestAborted);
            return File(pdf, "application/pdf", name);
        }
        catch (LessonAnalysisException ex) { return Fail(ex); }
    }

    // ---------- Rating, dashboard, analytics ----------

    [HttpGet("rating")]
    public Task<ActionResult> Rating([FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null) =>
        Run(() => service.RatingAsync(dateFrom, dateTo, HttpContext.RequestAborted));

    [HttpGet("rating.xlsx")]
    public async Task<ActionResult> RatingXlsx([FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null)
    {
        var (xlsx, name) = await service.RatingXlsxAsync(dateFrom, dateTo, HttpContext.RequestAborted);
        return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    [HttpGet("dashboard")]
    public Task<ActionResult> Dashboard() =>
        Run(() => service.DashboardAsync(HttpContext.RequestAborted));

    /// <summary>Chart data; <paramref name="teacherId"/> narrows every aggregate to one teacher.</summary>
    [HttpGet("analytics")]
    public Task<ActionResult> Analytics(
        [FromQuery] string? teacherId = null,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null) =>
        Run(() => service.AnalyticsAsync(dateFrom, dateTo,
            string.IsNullOrEmpty(teacherId) ? null : teacherId, HttpContext.RequestAborted));
}
