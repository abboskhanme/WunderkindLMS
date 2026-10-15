using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// The public poll page <c>/sorovnoma/{token}</c> — its two anonymous endpoints (client, 2026-10-15:
/// "so'rovnoma Google form holatda bo'lishi"; docs/modules/surveys-forms.md).
///
/// <para>
/// Shaped like <see cref="PublicSurveyController"/>: <c>[AllowAnonymous]</c> on the class, rate limits
/// by IP, a request size cap. What it may touch is narrow: it reads one poll by its unguessable token
/// and writes one <c>poll_responses</c> row with its answers — nothing else, and it never returns
/// anything about other respondents.
/// </para>
///
/// <para>
/// An unknown token and a poll whose link is switched off are the same 404. A known poll that is
/// closed (or not opened yet) answers 200 with <c>accepting = false</c> — the page then says
/// "So'rovnoma yopilgan" instead of "not found"; the token is unguessable, so this confirms nothing
/// to a stranger.
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/polls")]
public sealed class PublicPollsController(AppDbContext db) : ControllerBase
{
    [HttpGet("{token}")]
    [EnableRateLimiting("survey-read")]
    public async Task<ActionResult<PublicPollDto>> Get(string token, CancellationToken ct)
    {
        var poll = await PollService.FindByTokenAsync(db, token, ct);
        if (poll is null) return PollNotFound();
        return PollService.ToPublicDto(poll);
    }

    /// <summary>One answer sheet. 204 when saved; 400/409 carry a <c>code</c> the page branches on.</summary>
    [HttpPost("{token}")]
    [EnableRateLimiting("poll")]
    // 50 questions × 2000 characters of Cyrillic text is ~200 KB; anything larger is not a person.
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Submit(string token, PublicPollSubmitRequest req, CancellationToken ct)
    {
        var poll = await PollService.FindByTokenAsync(db, token, ct);
        if (poll is null) return PollNotFound();
        var (status, code, error) = await PollService.PublicSubmitAsync(db, poll, req, ct);
        return error is null ? NoContent() : StatusCode(status, new { code, message = error });
    }

    private NotFoundObjectResult PollNotFound() =>
        NotFound(new { code = PollService.CodeNotFound, message = PollService.NotFoundMessage });
}
