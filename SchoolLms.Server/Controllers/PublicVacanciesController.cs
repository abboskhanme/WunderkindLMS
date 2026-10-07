using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// The public vacancy page <c>/vakansiya/{slug}</c> (client, 2026-10-07: "vakansiya qoldirgan
/// xodimlar"). Anonymous like the enrolment form: it touches only <c>vacancies</c> (read) and
/// <c>job_applicants</c> / <c>applicant_events</c> (one new row), never returns anything about an
/// existing applicant, and shares the enrolment form's rate limits.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/vacancies")]
public sealed class PublicVacanciesController(AppDbContext db, IWebHostEnvironment env, ILogger<PublicVacanciesController> logger)
    : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("survey-read")]
    public async Task<ActionResult<IEnumerable<PublicVacancyDto>>> List(CancellationToken ct) =>
        await RecruitmentService.PublicVacanciesAsync(db, ct);

    [HttpGet("{slug}")]
    [EnableRateLimiting("survey-read")]
    public async Task<ActionResult<PublicVacancyDto>> Get(string slug, CancellationToken ct)
    {
        var v = await RecruitmentService.OpenVacancyAsync(db, slug, ct);
        return v is null
            ? NotFound(new { message = "Vakansiya topilmadi yoki yopilgan" })
            : new PublicVacancyDto(v.Slug, v.Title, v.Department, v.Description, v.Requirements);
    }

    /// <summary>
    /// The application. A filled honeypot (<c>website</c>) and a repeat by the same phone answer the
    /// same 200 as a real one — a bot learns nothing.
    /// </summary>
    [HttpPost("{slug}/apply")]
    [EnableRateLimiting("survey")]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> Apply(string slug, [FromForm] PublicApplyForm form, IFormFile? cv, CancellationToken ct)
    {
        var vacancy = await RecruitmentService.OpenVacancyAsync(db, slug, ct);
        if (vacancy is null) return NotFound(new { message = "Vakansiya topilmadi yoki yopilgan" });
        if (!string.IsNullOrEmpty(form.Website))
        {
            logger.LogInformation("Vakansiya honeypot: {Slug}", vacancy.Slug);
            return Ok(new { ok = true });
        }
        if (cv is not null && RecruitmentFiles.Validate(cv) is { } error) return BadRequest(new { message = error });

        string? path = cv is null ? null : await RecruitmentFiles.SaveAsync(env, cv, ct);
        try
        {
            var (_, created) = await RecruitmentService.ApplyAsync(db, vacancy,
                new PublicApplyInput(form.FullName, form.Phone, form.Email, form.Experience, form.About), path, cv?.FileName, ct);
            if (!created) RecruitmentFiles.Delete(env, path);
            return Ok(new { ok = true });
        }
        catch (RecruitmentException e)
        {
            RecruitmentFiles.Delete(env, path);
            return StatusCode(e.Status, new { message = e.Message });
        }
    }
}

public sealed class PublicApplyForm
{
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string? Experience { get; set; }
    public string? About { get; set; }
    /// <summary>Honeypot — hidden from people, filled by bots.</summary>
    public string? Website { get; set; }
}
