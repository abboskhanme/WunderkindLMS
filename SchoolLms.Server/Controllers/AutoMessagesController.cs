using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Automatic Telegram messages to parents (docs/modules/auto-messages.md, 2026-10-13).
//
//    GET  /api/admin/auto-messages                — the four rules
//    PUT  /api/admin/auto-messages/{key}          — switch on/off, time, day, minimum, text (audited)
//    POST /api/admin/auto-messages/{key}/preview  — who it would reach now, sample text (sends nothing)
//    GET  /api/admin/auto-messages/log            — what went out, newest first
//
//  The same permission as broadcasts ("messages"): a rule reaches the same parents a broadcast does.
// ===========================================================================

[ApiController]
[Authorize]
[AdminPerm("messages")]
[Route("api/admin/auto-messages")]
public class AutoMessagesController(AppDbContext db, TelegramService telegram, AuditService audit) : ControllerBase
{
    private const int MaxTextLength = 3500;
    private const int MaxLogRows = 500;

    [HttpGet]
    public async Task<ActionResult<List<AutoMessageRuleDto>>> List(CancellationToken ct)
    {
        var rules = await db.AutoMessageRules.AsNoTracking().ToListAsync(ct);
        var editors = rules.Select(r => r.UpdatedBy).Where(id => id != null).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => editors.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var lastPeriods = rules.Where(r => r.LastPeriod != null).Select(r => new { r.Key, r.LastPeriod }).ToList();
        var counts = new Dictionary<(string, string), int>();
        foreach (var lp in lastPeriods)
        {
            var byStatus = await db.AutoMessageLog.AsNoTracking()
                .Where(l => l.RuleKey == lp.Key && l.Period == lp.LastPeriod)
                .GroupBy(l => l.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            foreach (var s in byStatus) counts[(lp.Key, s.Key)] = s.Count;
        }

        return AutoMessageKind.All
            .Select(k => rules.FirstOrDefault(r => r.Key == k))
            .Where(r => r is not null)
            .Select(r => Dto(r!, names.GetValueOrDefault(r!.UpdatedBy ?? ""),
                counts.GetValueOrDefault((r.Key, BroadcastDelivery.Sent)),
                counts.GetValueOrDefault((r.Key, BroadcastDelivery.Failed)),
                counts.GetValueOrDefault((r.Key, BroadcastDelivery.NoTelegram))))
            .ToList();
    }

    [HttpPut("{key}")]
    public async Task<ActionResult<AutoMessageRuleDto>> Save(string key, SaveAutoMessageRuleRequest req, CancellationToken ct)
    {
        var rule = await db.AutoMessageRules.FirstOrDefaultAsync(r => r.Key == key, ct);
        if (rule is null) return NotFound(new { message = "Qoida topilmadi" });

        var text = (req.Text ?? "").Trim();
        if (TelegramHtml.MarkupToPlain(text).Trim().Length == 0) return BadRequest(new { message = "Xabar matni kerak" });
        if (text.Length > MaxTextLength) return BadRequest(new { message = $"Matn {MaxTextLength} belgidan oshmasin" });
        if (!TimeOnly.TryParseExact(req.SendTime ?? "", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return BadRequest(new { message = "Vaqt SS:dd ko'rinishida bo'lsin (masalan 10:00)" });
        if (AutoMessageKind.IsMonthly(key) && req.DayOfMonth is < 1 or > 28)
            return BadRequest(new { message = "Kun 1 dan 28 gacha bo'lsin" });
        if (req.MinAmount < 0) return BadRequest(new { message = "Eng kam summa manfiy bo'lmasin" });

        var before = new { rule.Enabled, rule.SendTime, rule.DayOfMonth, rule.MinAmount, rule.Text };
        rule.Enabled = req.Enabled;
        rule.SendTime = req.SendTime!;
        if (AutoMessageKind.IsMonthly(key)) rule.DayOfMonth = req.DayOfMonth;
        if (key == AutoMessageKind.Debt) rule.MinAmount = req.MinAmount;
        rule.Text = text;
        rule.UpdatedBy = CurrentUserId();
        rule.UpdatedAt = AppClock.NowInstant;
        var after = new { rule.Enabled, rule.SendTime, rule.DayOfMonth, rule.MinAmount, rule.Text };
        audit.Record("AutoMessageRule", key, "update",
            $"Avtomatik xabar \"{key}\": {(rule.Enabled ? "yoqilgan" : "o'chirilgan")}, {rule.SendTime}", before, after);
        await db.SaveChangesAsync(ct);

        var name = await db.Users.AsNoTracking().Where(u => u.Id == rule.UpdatedBy).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return Dto(rule, name, 0, 0, 0);
    }

    [HttpPost("{key}/preview")]
    public async Task<ActionResult<AutoMessagePreviewDto>> Preview(string key, PreviewAutoMessageRequest req, CancellationToken ct)
    {
        var rule = await db.AutoMessageRules.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (rule is null) return NotFound(new { message = "Qoida topilmadi" });
        var text = string.IsNullOrWhiteSpace(req.Text) ? null : req.Text.Trim();
        return await new AutoMessageService(db, telegram).PreviewAsync(rule, text, AppClock.Now, ct);
    }

    [HttpGet("log")]
    public async Task<ActionResult<List<AutoMessageLogRowDto>>> Log(
        [FromQuery] string? rule, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? status,
        CancellationToken ct)
    {
        DateOnly? Day(string? v) =>
            DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        return await new AutoMessageService(db, telegram).LogAsync(rule, Day(from), Day(to), status, MaxLogRows, ct);
    }

    private static AutoMessageRuleDto Dto(AutoMessageRule r, string? editor, int sent, int failed, int noTelegram) => new(
        r.Key, r.Enabled, r.SendTime, r.DayOfMonth, r.MinAmount, r.Text, AutoMessageKind.IsMonthly(r.Key),
        AutoMessageService.TokensOf(r.Key), AutoMessageService.DefaultText.GetValueOrDefault(r.Key, ""),
        r.LastPeriod, r.LastRunAt, sent, failed, noTelegram, r.UpdatedAt, editor);

    private string CurrentUserId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";
}
