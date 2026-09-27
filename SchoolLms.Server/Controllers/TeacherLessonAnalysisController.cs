using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Teacher portal → "Dars tahlilim" (docs/modules/lesson-analysis.md §5): the teacher's OWN
/// COMPLETED observations, their PDF and personal analytics. A draft, or another teacher's
/// observation, is 404 even by id; a teacher never sees a ranking of colleagues. The
/// <c>teachers</c> row is resolved from the account (<c>teachers.user_id</c>); an account with
/// no linked row sees nothing — a missing link never widens the scope. Every response is
/// <c>no-store</c>.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Teacher)]
[NoStore]
[Route("api/teacher/lesson-analysis")]
public sealed class TeacherLessonAnalysisController(LessonAnalysisService service) : ControllerBase
{
    /// <summary>Matches no teacher — the analytics scope of an account without a teacher row.</summary>
    private const string NoTeacher = "";

    private Task<string?> MyTeacherIdAsync() =>
        service.TeacherIdForUserAsync(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "", HttpContext.RequestAborted);

    private static ActionResult Fail(LessonAnalysisException ex) =>
        new ObjectResult(new { message = ex.Message }) { StatusCode = ex.StatusCode };

    [HttpGet("observations")]
    public async Task<ActionResult> Observations(
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        try
        {
            return Ok(await service.ListForTeacherAsync(
                await MyTeacherIdAsync(), dateFrom, dateTo, limit, offset, HttpContext.RequestAborted));
        }
        catch (LessonAnalysisException ex) { return Fail(ex); }
    }

    [HttpGet("observations/{id}")]
    public async Task<ActionResult> Get(string id)
    {
        try
        {
            return Ok(await service.GetForTeacherAsync(id, await MyTeacherIdAsync(), HttpContext.RequestAborted));
        }
        catch (LessonAnalysisException ex) { return Fail(ex); }
    }

    [HttpGet("observations/{id}/report.pdf")]
    public async Task<ActionResult> ReportPdf(string id)
    {
        try
        {
            var (pdf, name) = await service.ReportPdfForTeacherAsync(id, await MyTeacherIdAsync(), HttpContext.RequestAborted);
            return File(pdf, "application/pdf", name);
        }
        catch (LessonAnalysisException ex) { return Fail(ex); }
    }

    /// <summary>The same charts as staff see, every aggregate narrowed to this teacher.</summary>
    [HttpGet("analytics")]
    public async Task<ActionResult> Analytics([FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null) =>
        Ok(await service.AnalyticsAsync(dateFrom, dateTo, await MyTeacherIdAsync() ?? NoTeacher, HttpContext.RequestAborted));
}
