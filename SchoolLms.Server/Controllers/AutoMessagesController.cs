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
//    GET  /api/admin/auto-messages                    — the rules
//    PUT  /api/admin/auto-messages/{key}              — switch on/off, time, day, minimum, text,
//                                                       approval, cut-off, pictures (audited)
//    POST /api/admin/auto-messages/{key}/preview      — who it would reach now, sample text (sends nothing)
//    GET  /api/admin/auto-messages/log                — the delivery log, newest first, ≤ 500 rows
//    GET  /api/admin/auto-messages/log/paged          — the same, paged, with pupil / run / source filters
//    GET  /api/admin/auto-messages/log/export         — the same as .xlsx
//    GET  /api/admin/auto-messages/runs               — runs (planned, waiting, sent, stopped)
//    GET  /api/admin/auto-messages/runs/{id}          — one run
//    POST /api/admin/auto-messages/runs/{id}/approve  — approve a waiting run and send it (audited)
//    POST /api/admin/auto-messages/runs/{id}/cancel   — stop a run (audited)
//    POST /api/admin/auto-messages/runs/{id}/exclude  — take one pupil out of a waiting run (audited)
//    GET  /api/admin/auto-messages/stats              — counts per rule × status × day
//
//  The same permission as broadcasts ("messages"): a rule reaches the same parents a broadcast does.
// ===========================================================================

[ApiController]
[Authorize]
[AdminPerm("messages")]
[Route("api/admin/auto-messages")]
public class AutoMessagesController(
    AppDbContext db, TelegramService telegram, AuditService audit, IWebHostEnvironment env) : ControllerBase
{
    private const int MaxTextLength = 3500;
    private const int MaxLogRows = 500;
    private const int MaxPageSize = 200;
    private const int MaxExportRows = 10_000;
    private const int MaxRuns = 300;
    private const int MaxRangeDays = 366;

    private static readonly IReadOnlyDictionary<string, string> RuleTitles = new Dictionary<string, string>
    {
        [AutoMessageKind.Absence] = "Kelmaganlik xabari",
        [AutoMessageKind.Invoice] = "Yangi oylik hisob",
        [AutoMessageKind.Debt] = "Qarz eslatmasi",
        [AutoMessageKind.Birthday] = "Tug'ilgan kun tabrigi",
        [AutoMessageKind.Payment] = "To'lov qabul qilindi",
    };

    private static readonly IReadOnlyDictionary<string, string> StatusTitles = new Dictionary<string, string>
    {
        [AutoMessageStatus.Scheduled] = "Rejalashtirilgan",
        [AutoMessageStatus.PendingApproval] = "Tasdiq kutmoqda",
        [BroadcastDelivery.Sent] = "Yuborildi",
        [BroadcastDelivery.Failed] = "Xatolik",
        [BroadcastDelivery.NoTelegram] = "Xatolik (Telegram yo'q)",
        [AutoMessageStatus.Cancelled] = "To'xtatilgan",
    };

    private AutoMessageService Service => new(db, telegram, env: env);

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
        var cutoff = string.IsNullOrWhiteSpace(req.CutoffTime) ? null : req.CutoffTime.Trim();
        if (cutoff is not null
            && !TimeOnly.TryParseExact(cutoff, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return BadRequest(new { message = "Kesish vaqti SS:dd ko'rinishida bo'lsin (masalan 11:30)" });
        // Pictures (2026-10-25), checked as an announcement's are; the payment message is the receipt's caption.
        var images = req.ImageUrls is null || AutoMessageKind.IsEventDriven(key) ? null : BroadcastImages.Clean(req.ImageUrls);
        if (images is not null && BroadcastImages.Check(env, images) is { } imageError)
            return BadRequest(new { message = imageError });

        var before = new { rule.Enabled, rule.SendTime, rule.DayOfMonth, rule.MinAmount, rule.Text, rule.RequiresApproval, rule.CutoffTime, Images = rule.ImageUrls.ToList() };
        var switchedOff = rule.Enabled && !req.Enabled;
        rule.Enabled = req.Enabled;
        rule.SendTime = req.SendTime!;
        if (AutoMessageKind.IsMonthly(key)) rule.DayOfMonth = req.DayOfMonth;
        if (key == AutoMessageKind.Debt) rule.MinAmount = req.MinAmount;
        rule.Text = text;
        // Approval never holds a receipt back: the payment rule cannot require it.
        if (req.RequiresApproval is { } approval && !AutoMessageKind.IsEventDriven(key)) rule.RequiresApproval = approval;
        if (key == AutoMessageKind.Absence) rule.CutoffTime = cutoff;
        if (images is not null) rule.ImageUrls = images;
        rule.UpdatedBy = CurrentUserId();
        rule.UpdatedAt = AppClock.NowInstant;
        var after = new { rule.Enabled, rule.SendTime, rule.DayOfMonth, rule.MinAmount, rule.Text, rule.RequiresApproval, rule.CutoffTime, Images = rule.ImageUrls.ToList() };
        audit.Record("AutoMessageRule", key, "update",
            $"Avtomatik xabar \"{key}\": {(rule.Enabled ? "yoqilgan" : "o'chirilgan")}, {rule.SendTime}", before, after);
        await db.SaveChangesAsync(ct);

        // Switching a rule off stops what it planned or is sending (the send loop re-reads the status).
        if (switchedOff) await Service.CancelOpenRunsAsync(key, rule.UpdatedBy ?? "", ct);

        var name = await db.Users.AsNoTracking().Where(u => u.Id == rule.UpdatedBy).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return Dto(rule, name, 0, 0, 0);
    }

    [HttpPost("{key}/preview")]
    public async Task<ActionResult<AutoMessagePreviewDto>> Preview(string key, PreviewAutoMessageRequest req, CancellationToken ct)
    {
        var rule = await db.AutoMessageRules.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (rule is null) return NotFound(new { message = "Qoida topilmadi" });
        var text = string.IsNullOrWhiteSpace(req.Text) ? null : req.Text.Trim();
        if (AutoMessageKind.IsEventDriven(key))
        {
            // The payment message has nobody to count in advance: a sample with made-up values.
            var sample = AutoMessageService.Render(text ?? rule.Text, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["{fish}"] = "Aliyev Ali Valiyevich", ["{ism}"] = "Ali", ["{sinf}"] = "5-A", ["{ota_ona}"] = "Ota-ona",
                ["{summa}"] = "1 500 000", ["{sana}"] = AppClock.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["{vaqt}"] = AppClock.Now.ToString("HH:mm", CultureInfo.InvariantCulture), ["{chek}"] = "1024",
                ["{usul}"] = "Naqd pul", ["{qarzdorlik}"] = "0",
            });
            return new AutoMessagePreviewDto(key, AppClock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 0, 0, 0, sample, "namuna");
        }
        return await Service.PreviewAsync(rule, text, AppClock.Now, ct);
    }

    // =====================================================================
    //  Delivery log
    // =====================================================================

    [HttpGet("log")]
    public async Task<ActionResult<List<AutoMessageLogRowDto>>> Log(
        [FromQuery] string? rule, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? status,
        CancellationToken ct) =>
        await Service.LogAsync(rule, Day(from), Day(to), status, MaxLogRows, ct);

    [HttpGet("log/paged")]
    public async Task<ActionResult<AutoMessageLogPageDto>> LogPaged(
        [FromQuery] string? rule, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? status,
        [FromQuery] string? studentId, [FromQuery] string? q, [FromQuery] Guid? runId, [FromQuery] string? source,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        await Service.LogPageAsync(new AutoMessageLogQuery(rule, status, Day(from), Day(to), studentId, q, runId, source),
            page, Math.Clamp(pageSize, 1, MaxPageSize), ct);

    [HttpGet("log/export")]
    public async Task<IActionResult> LogExport(
        [FromQuery] string? rule, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? status,
        [FromQuery] string? studentId, [FromQuery] string? q, [FromQuery] Guid? runId, [FromQuery] string? source,
        CancellationToken ct)
    {
        var page = await Service.LogPageAsync(
            new AutoMessageLogQuery(rule, status, Day(from), Day(to), studentId, q, runId, source), 1, MaxExportRows, ct);
        var headers = new[]
        {
            "Vaqt", "Qoida", "Davr", "O'quvchi", "Sinf", "Ota-ona", "Chat", "Holat", "Urinishlar", "Xato",
            "Manba", "Kim yubordi", "Chek №", "Matn",
        };
        var rows = page.Rows.Select(r => (IReadOnlyList<string>)new[]
        {
            AppClock.ToLocal(r.At).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            RuleTitles.GetValueOrDefault(r.RuleKey, r.RuleKey), r.Period, r.FullName, r.ClassName,
            r.ParentName ?? "", r.ChatId?.ToString(CultureInfo.InvariantCulture) ?? "",
            StatusTitles.GetValueOrDefault(r.Status, r.Status), r.Attempts.ToString(CultureInfo.InvariantCulture),
            r.Error ?? "", r.Source == AutoMessageSource.Manual ? "Qo'lda" : "Avtomatik", r.SentByName ?? "",
            r.RefId ?? "", TelegramHtml.ToPlain(r.Text),
        });
        var bytes = ExcelExport.Build("Xabarlar", headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"avto-xabarlar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // =====================================================================
    //  Runs
    // =====================================================================

    [HttpGet("runs")]
    public async Task<ActionResult<List<AutoMessageRunDto>>> Runs(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? rule, [FromQuery] string? status,
        CancellationToken ct)
    {
        if (Range(from, to, out var f, out var t) is { } bad) return bad;
        return await Service.RunsAsync(f, t, rule, status, MaxRuns, ct);
    }

    [HttpGet("runs/{id:guid}")]
    public async Task<ActionResult<AutoMessageRunDto>> Run(Guid id, CancellationToken ct)
    {
        var run = await Service.GetRunAsync(id, ct);
        if (run is null) return NotFound(new { message = "Yuborish topilmadi" });
        return run;
    }

    [HttpPost("runs/{id:guid}/approve")]
    public async Task<ActionResult<AutoMessageRunDto>> Approve(Guid id, CancellationToken ct)
    {
        var service = Service;
        try
        {
            var result = await service.ApproveAsync(id, CurrentUserId(), AppClock.Now, ct);
            audit.Record("AutoMessageRun", id.ToString(), "approve",
                $"Avtomatik xabar \"{result.Key}\" ({result.Period}) tasdiqlandi: yuborildi {result.Sent}, xato {result.Failed}");
            await db.SaveChangesAsync(ct);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        return (await service.GetRunAsync(id, ct))!;
    }

    [HttpPost("runs/{id:guid}/cancel")]
    public async Task<ActionResult<AutoMessageRunDto>> Cancel(Guid id, CancellationToken ct)
    {
        var service = Service;
        try
        {
            if (!await service.CancelAsync(id, CurrentUserId(), null, ct))
                return Conflict(new { message = "Yuborish allaqachon tugagan yoki to'xtatilgan" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        var run = (await service.GetRunAsync(id, ct))!;
        audit.Record("AutoMessageRun", id.ToString(), "cancel",
            $"Avtomatik xabar \"{run.RuleKey}\" ({run.Period}) to'xtatildi: to'xtatilgan {run.Cancelled}, yuborilgan {run.Sent}");
        await db.SaveChangesAsync(ct);
        return run;
    }

    [HttpPost("runs/{id:guid}/exclude")]
    public async Task<ActionResult<AutoMessageRunDto>> Exclude(Guid id, ExcludeFromRunRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.StudentId)) return BadRequest(new { message = "O'quvchi tanlanmagan" });
        var service = Service;
        try
        {
            var n = await service.ExcludeAsync(id, req.StudentId, ct);
            if (n == 0) return NotFound(new { message = "Bu o'quvchi yuborishda tasdiq kutmayapti" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        var run = (await service.GetRunAsync(id, ct))!;
        audit.Record("AutoMessageRun", id.ToString(), "exclude",
            $"Avtomatik xabar \"{run.RuleKey}\" ({run.Period}): o'quvchi ro'yxatdan chiqarildi", studentId: req.StudentId);
        await db.SaveChangesAsync(ct);
        return run;
    }

    // =====================================================================
    //  Statistics
    // =====================================================================

    [HttpGet("stats")]
    public async Task<ActionResult<AutoMessageStatsDto>> Stats([FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        if (Range(from, to, out var f, out var t) is { } bad) return bad;
        return await Service.StatsAsync(f, t, ct);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static DateOnly? Day(string? v) =>
        DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>from/to as school days; missing — the last 7 days.</summary>
    private ActionResult? Range(string? from, string? to, out DateOnly f, out DateOnly t)
    {
        t = Day(to) ?? AppClock.Today;
        f = Day(from) ?? t.AddDays(-6);
        if (f > t) return BadRequest(new { message = "Davr boshi oxiridan keyin bo'lmasin" });
        if (t.DayNumber - f.DayNumber >= MaxRangeDays) return BadRequest(new { message = $"Davr {MaxRangeDays} kundan oshmasin" });
        return null;
    }

    private static AutoMessageRuleDto Dto(AutoMessageRule r, string? editor, int sent, int failed, int noTelegram) => new(
        r.Key, r.Enabled, r.SendTime, r.DayOfMonth, r.MinAmount, r.Text, AutoMessageKind.IsMonthly(r.Key),
        AutoMessageService.TokensOf(r.Key), AutoMessageService.DefaultText.GetValueOrDefault(r.Key, ""),
        r.LastPeriod, r.LastRunAt, sent, failed, noTelegram, r.UpdatedAt, editor,
        r.RequiresApproval, r.CutoffTime, AutoMessageKind.IsEventDriven(r.Key), r.ImageUrls);

    private string CurrentUserId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";
}
