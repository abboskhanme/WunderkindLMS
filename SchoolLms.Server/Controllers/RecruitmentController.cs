using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Ishga qabul va adaptatsiya (client, 2026-10-07) — HR → "Ishga qabul". Gated read: applicants'
/// contacts and CVs are personal data.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm(RecruitmentService.Permission, GatedRead = true)]
[Route("api/admin/recruitment")]
public sealed class RecruitmentController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private string ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private async Task<IActionResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return NoContent();
        }
        catch (RecruitmentException e)
        {
            return StatusCode(e.Status, new { message = e.Message });
        }
    }

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (RecruitmentException e)
        {
            return StatusCode(e.Status, new { message = e.Message });
        }
    }

    // ---------- Vakansiyalar ----------

    [HttpGet("vacancies")]
    public async Task<ActionResult<IEnumerable<VacancyDto>>> Vacancies(CancellationToken ct) =>
        await RecruitmentService.VacanciesAsync(db, ct);

    [HttpPost("vacancies")]
    public Task<ActionResult<VacancyDto>> CreateVacancy(VacancyInput input, CancellationToken ct) => Run(async () =>
    {
        var v = await RecruitmentService.CreateVacancyAsync(db, input, ActorId, ct);
        return (await RecruitmentService.VacanciesAsync(db, ct)).First(x => x.Id == v.Id);
    });

    [HttpPut("vacancies/{id:guid}")]
    public Task<IActionResult> UpdateVacancy(Guid id, VacancyInput input, CancellationToken ct) =>
        Run(() => RecruitmentService.UpdateVacancyAsync(db, id, input, ct));

    [HttpPost("vacancies/{id:guid}/open")]
    public Task<IActionResult> OpenVacancy(Guid id, CancellationToken ct) =>
        Run(() => RecruitmentService.SetVacancyStatusAsync(db, id, true, ct));

    [HttpPost("vacancies/{id:guid}/close")]
    public Task<IActionResult> CloseVacancy(Guid id, CancellationToken ct) =>
        Run(() => RecruitmentService.SetVacancyStatusAsync(db, id, false, ct));

    // ---------- Nomzodlar ----------

    [HttpGet("counts")]
    public async Task<ActionResult<RecruitmentCountsDto>> Counts(CancellationToken ct) => await RecruitmentService.CountsAsync(db, ct);

    /// <summary><paramref name="tab"/>: new | pipeline | onboarding | archive.</summary>
    [HttpGet("applicants")]
    public async Task<ActionResult<IEnumerable<ApplicantRowDto>>> Applicants(
        [FromQuery] string? tab, [FromQuery] Guid? vacancyId, [FromQuery] string? q, CancellationToken ct) =>
        await RecruitmentService.ListAsync(db, tab, vacancyId, q, ct);

    [HttpGet("applicants/{id:guid}")]
    public Task<ActionResult<ApplicantDetailDto>> Applicant(Guid id, CancellationToken ct) =>
        Run(() => RecruitmentService.DetailAsync(db, id, ct));

    [HttpPost("applicants")]
    [RequestSizeLimit(12_000_000)]
    public async Task<ActionResult<ApplicantDetailDto>> CreateApplicant([FromForm] ApplicantForm form, IFormFile? cv, CancellationToken ct)
    {
        if (cv is not null && RecruitmentFiles.Validate(cv) is { } error) return BadRequest(new { message = error });
        string? path = cv is null ? null : await RecruitmentFiles.SaveAsync(env, cv, ct);
        try
        {
            var a = await RecruitmentService.CreateAsync(db, form.ToInput(), path, cv?.FileName, ActorId, ct);
            return await RecruitmentService.DetailAsync(db, a.Id, ct);
        }
        catch (RecruitmentException e)
        {
            RecruitmentFiles.Delete(env, path);
            return StatusCode(e.Status, new { message = e.Message });
        }
    }

    [HttpPut("applicants/{id:guid}")]
    public Task<IActionResult> UpdateApplicant(Guid id, ApplicantInput input, CancellationToken ct) =>
        Run(() => RecruitmentService.UpdateAsync(db, id, input, ct));

    [HttpPost("applicants/{id:guid}/cv")]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> UploadCv(Guid id, IFormFile? cv, CancellationToken ct)
    {
        if (RecruitmentFiles.Validate(cv) is { } error) return BadRequest(new { message = error });
        var path = await RecruitmentFiles.SaveAsync(env, cv!, ct);
        try
        {
            RecruitmentFiles.Delete(env, await RecruitmentService.SetCvAsync(db, id, path, cv!.FileName, ct));
            return NoContent();
        }
        catch (RecruitmentException e)
        {
            RecruitmentFiles.Delete(env, path);
            return StatusCode(e.Status, new { message = e.Message });
        }
    }

    [HttpGet("applicants/{id:guid}/cv")]
    public async Task<IActionResult> Cv(Guid id, CancellationToken ct)
    {
        var a = await db.JobApplicants.FindAsync([id], ct);
        if (RecruitmentFiles.FullPath(env, a?.CvPath) is not { } full) return NotFound(new { message = "CV topilmadi" });
        var name = string.IsNullOrWhiteSpace(a!.CvName) ? "cv" + Path.GetExtension(full) : a.CvName;
        return PhysicalFile(full, RecruitmentFiles.ContentType(full), name);
    }

    [HttpPost("applicants/{id:guid}/move")]
    public Task<IActionResult> Move(Guid id, ApplicantMoveRequest req, CancellationToken ct) =>
        Run(() => RecruitmentService.MoveAsync(db, id, req, ActorId, ct));

    [HttpPut("applicants/{id:guid}/results/{stage}")]
    public Task<IActionResult> Result(Guid id, string stage, ApplicantResultInput input, CancellationToken ct) =>
        Run(() => RecruitmentService.SaveResultAsync(db, id, stage, input, ActorId, ct));

    [HttpPost("applicants/{id:guid}/reject")]
    public Task<IActionResult> Reject(Guid id, ApplicantRejectRequest req, CancellationToken ct) =>
        Run(() => RecruitmentService.RejectAsync(db, id, req.Reason, ActorId, ct));

    [HttpPost("applicants/{id:guid}/hire")]
    public Task<IActionResult> Hire(Guid id, ApplicantHireRequest req, CancellationToken ct) =>
        Run(() => RecruitmentService.HireAsync(db, id, req, ActorId, ct));

    [HttpPut("applicants/{id:guid}/onboarding/{stepId:guid}")]
    public Task<IActionResult> Step(Guid id, Guid stepId, OnboardingStepUpdate req, CancellationToken ct) =>
        Run(() => RecruitmentService.SetStepAsync(db, id, stepId, req, ActorId, ct));

    [HttpPost("applicants/{id:guid}/adapted")]
    public Task<IActionResult> Adapted(Guid id, ApplicantMoveRequest? req, CancellationToken ct) =>
        Run(() => RecruitmentService.AdaptAsync(db, id, req?.Note, ActorId, ct));

    // ---------- Adaptatsiya bosqichlari ----------

    [HttpGet("onboarding-steps")]
    public async Task<ActionResult<IEnumerable<OnboardingTemplateDto>>> Templates(CancellationToken ct) =>
        await RecruitmentService.TemplatesAsync(db, ct);

    [HttpPut("onboarding-steps")]
    public Task<ActionResult<List<OnboardingTemplateDto>>> SaveTemplates(List<OnboardingTemplateInput> input, CancellationToken ct) => Run(async () =>
    {
        await RecruitmentService.SaveTemplatesAsync(db, input, ct);
        return await RecruitmentService.TemplatesAsync(db, ct);
    });
}

/// <summary>The manual-add form (multipart, so the CV can ride along).</summary>
public sealed class ApplicantForm
{
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public Guid? VacancyId { get; set; }
    public string? Position { get; set; }
    public string? Experience { get; set; }
    public string? About { get; set; }

    public ApplicantInput ToInput() => new(FullName, Phone, Email, VacancyId, Position, Experience, About);
}
