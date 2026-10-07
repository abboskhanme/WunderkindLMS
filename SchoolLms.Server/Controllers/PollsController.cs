using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>So'rovnomalar va NPS (client, 2026-10-07) — the admin side; Polls.cs, PollService.cs.</summary>
[ApiController]
[Authorize]
[AdminPerm(PollService.Permission, GatedRead = true)]
[Route("api/admin/polls")]
public sealed class PollsController(AppDbContext db, TelegramService telegram, ILogger<PollNotifier> notifierLogger) : ControllerBase
{
    private string ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private ObjectResult Error(int status, string? message) => StatusCode(status, new { message });

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PollDto>>> List(CancellationToken ct) => await PollService.ListAsync(db, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PollDto>> Get(Guid id, CancellationToken ct) =>
        await PollService.GetAsync(db, id, ct) is { } dto ? dto : NotFound(new { message = "So'rovnoma topilmadi" });

    [HttpPost]
    public async Task<ActionResult<PollDto>> Create(PollInput input, CancellationToken ct)
    {
        var (error, poll) = await PollService.CreateAsync(db, input, ActorId, ct);
        if (error is not null) return BadRequest(new { message = error });
        return (await PollService.GetAsync(db, poll!.Id, ct))!;
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PollDto>> Update(Guid id, PollInput input, CancellationToken ct)
    {
        var (status, error) = await PollService.UpdateAsync(db, id, input, ct);
        if (error is not null) return Error(status, error);
        return (await PollService.GetAsync(db, id, ct))!;
    }

    [HttpPost("{id:guid}/open")]
    public async Task<ActionResult<PollDto>> Open(Guid id, CancellationToken ct)
    {
        var (status, error, _) = await PollService.SetStatusAsync(db, id, PollStatus.Open, ct);
        if (error is not null) return Error(status, error);
        return (await PollService.GetAsync(db, id, ct))!;
    }

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<PollDto>> Close(Guid id, CancellationToken ct)
    {
        var (status, error, _) = await PollService.SetStatusAsync(db, id, PollStatus.Closed, ct);
        if (error is not null) return Error(status, error);
        return (await PollService.GetAsync(db, id, ct))!;
    }

    /// <summary>How many chats the announcement would reach — shown in the confirm dialog.</summary>
    [HttpGet("{id:guid}/recipients")]
    public async Task<ActionResult<object>> Recipients(Guid id, CancellationToken ct)
    {
        var poll = await db.Polls.FindAsync([id], ct);
        if (poll is null) return NotFound(new { message = "So'rovnoma topilmadi" });
        return new { count = (await PollNotifier.RecipientsAsync(db, poll.Audience, ct)).Count, botConfigured = telegram.IsConfigured };
    }

    /// <summary>The one bot announcement — only for an open poll, only once.</summary>
    [HttpPost("{id:guid}/notify")]
    public async Task<ActionResult<object>> Notify(Guid id, CancellationToken ct)
    {
        var poll = await db.Polls.FindAsync([id], ct);
        if (poll is null) return NotFound(new { message = "So'rovnoma topilmadi" });
        if (!PollService.IsAccepting(poll, AppClock.Today)) return BadRequest(new { message = "Avval so'rovnomani oching" });
        if (poll.NotifiedAt is not null) return Conflict(new { message = "E'lon allaqachon yuborilgan" });
        if (!telegram.IsConfigured) return BadRequest(new { message = "Telegram bot sozlanmagan" });
        poll.NotifiedAt = AppClock.NowInstant;
        await db.SaveChangesAsync(ct);
        var (total, sent) = await new PollNotifier(db, telegram, notifierLogger).AnnounceAsync(poll, ct);
        return new { total, sent };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (status, error) = await PollService.DeleteAsync(db, id, ct);
        return error is null ? NoContent() : Error(status, error);
    }

    [HttpGet("{id:guid}/results")]
    public async Task<ActionResult<PollResultsDto>> Results(Guid id, [FromQuery] string? className, CancellationToken ct) =>
        await PollService.ResultsAsync(db, id, className, ct) is { } dto ? dto : NotFound(new { message = "So'rovnoma topilmadi" });
}
