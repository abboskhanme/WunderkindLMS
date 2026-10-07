using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'rindosh ustoz (client, 2026-10-07) — the same gate as teacher attendance ("teachers"): the
/// people who mark a teacher absent also name who covers the lessons.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("teachers")]
[Route("api/admin/substitutions")]
public sealed class SubstitutionsController(AppDbContext db, TelegramService telegram, ILogger<SubstitutionNotifier> notifierLogger)
    : ControllerBase
{
    private SubstitutionNotifier Notifier => new(db, telegram, notifierLogger);
    private string ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>One teacher's lessons on a date, their current substitutes, and who is free when.</summary>
    [HttpGet("day")]
    public async Task<ActionResult<SubstitutionDayDto>> Day([FromQuery] string teacherId, [FromQuery] string date, CancellationToken ct)
    {
        if (Parse(date) is not { } d) return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
        var day = await LessonSubstitutionService.DayAsync(db, teacherId, d, ct);
        return day is null ? NotFound(new { message = "O'qituvchi topilmadi" }) : day;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubstitutionDto>>> List(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? teacherId, CancellationToken ct)
    {
        var start = Parse(from) ?? AppClock.Today.AddDays(-30);
        var end = Parse(to) ?? AppClock.Today.AddDays(30);
        return await LessonSubstitutionService.ListAsync(db, start, end, teacherId, ct);
    }

    /// <summary>Names substitutes; each one hears from the bot.</summary>
    [HttpPost]
    public async Task<ActionResult<IEnumerable<SubstitutionDto>>> Create(SubstitutionCreateRequest req, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (error, created) = await LessonSubstitutionService.CreateAsync(db, req, ActorId, ct);
        if (error is not null) return BadRequest(new { message = error });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var dtos = await LessonSubstitutionService.ToDtosAsync(db, created, ct);
        await Notifier.AssignedAsync(dtos, ct);
        return dtos;
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var row = await LessonSubstitutionService.CancelAsync(db, id, ActorId, ct);
        if (row is null) return NotFound(new { message = "O'rindosh dars topilmadi" });
        await db.SaveChangesAsync(ct);
        await Notifier.CancelledAsync(await LessonSubstitutionService.ToDtosAsync(db, [row], ct), ct);
        return NoContent();
    }
}
