using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  History and statistics of automatic messages (2026-10-15, docs/modules/auto-messages.md):
//  the runs, the per-chat delivery log (paged, filtered, exported) and counts per rule × status × day.
//  Read-only.
// ===========================================================================

public sealed partial class AutoMessageService
{
    /// <summary>Shown instead of a user name for what the system did itself.</summary>
    public const string SystemName = "Tizim";

    /// <summary>The first instant of a school day.</summary>
    public static DateTimeOffset DayStart(DateOnly day) => ToInstant(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>What went out, newest first, at most <paramref name="max"/> rows (the 2026-10-13 endpoint).</summary>
    public async Task<List<AutoMessageLogRowDto>> LogAsync(
        string? rule, DateOnly? from, DateOnly? to, string? status, int max, CancellationToken ct = default) =>
        (await LogPageAsync(new AutoMessageLogQuery(rule, status, from, to), 1, max, ct)).Rows;

    /// <summary>One page of the delivery log, newest first.</summary>
    public async Task<AutoMessageLogPageDto> LogPageAsync(
        AutoMessageLogQuery query, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        var q = Filtered(query);
        var total = await q.CountAsync(ct);
        var rows = await (from l in q
                          join s in db.Students.AsNoTracking() on l.StudentId equals s.Id
                          orderby l.At descending, l.Id
                          select new { l, s.FullName, s.ClassName })
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var chats = rows.Where(r => r.l.ChatId != null).Select(r => r.l.ChatId!.Value).Distinct().ToList();
        var parents = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => chats.Contains(r.ChatId) && r.TeacherId == null)
                .Select(r => new { r.ChatId, r.StudentId, r.ParentName }).ToListAsync(ct))
            .GroupBy(r => (r.ChatId, r.StudentId)).ToDictionary(g => g.Key, g => g.First().ParentName);
        var users = await UserNamesAsync(rows.Select(r => r.l.SentBy), ct);

        return new AutoMessageLogPageDto(total, page, pageSize, [.. rows.Select(r => new AutoMessageLogRowDto(
            r.l.Id, r.l.RuleKey, r.l.Period, r.l.StudentId, r.FullName, r.ClassName ?? "", r.l.ChatId,
            r.l.ChatId is { } c ? parents.GetValueOrDefault((c, r.l.StudentId)) : null,
            r.l.Status, r.l.Text, r.l.At, r.l.RunId, r.l.RefId, r.l.Source,
            r.l.SentBy is { } by ? users.GetValueOrDefault(by) : null,
            r.l.Attempts, r.l.Error, r.l.PlannedAt))]);
    }

    private IQueryable<AutoMessageLogEntry> Filtered(AutoMessageLogQuery f)
    {
        var q = db.AutoMessageLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.Rule)) q = q.Where(l => l.RuleKey == f.Rule);
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(l => l.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.Source)) q = q.Where(l => l.Source == f.Source);
        if (!string.IsNullOrWhiteSpace(f.StudentId)) q = q.Where(l => l.StudentId == f.StudentId);
        if (f.RunId is { } runId) q = q.Where(l => l.RunId == runId);
        if (f.From is { } from)
        {
            var lower = DayStart(from);
            q = q.Where(l => l.At >= lower);
        }
        if (f.To is { } to)
        {
            var upper = DayStart(to.AddDays(1));
            q = q.Where(l => l.At < upper);
        }
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = f.Search.Trim().ToLower();
            var ids = db.Students.AsNoTracking().Where(s => s.FullName.ToLower().Contains(term)).Select(s => s.Id);
            q = q.Where(l => ids.Contains(l.StudentId));
        }
        return q;
    }

    /// <summary>Runs planned in the period (school days), newest first.</summary>
    public async Task<List<AutoMessageRunDto>> RunsAsync(
        DateOnly from, DateOnly to, string? rule, string? status, int max, CancellationToken ct = default)
    {
        var lower = DayStart(from);
        var upper = DayStart(to.AddDays(1));
        var q = db.AutoMessageRuns.AsNoTracking().Where(r => r.PlannedAt >= lower && r.PlannedAt < upper);
        if (!string.IsNullOrWhiteSpace(rule)) q = q.Where(r => r.RuleKey == rule);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        var runs = await q.OrderByDescending(r => r.PlannedAt).ThenBy(r => r.RuleKey).Take(max).ToListAsync(ct);
        var users = await UserNamesAsync(runs.SelectMany(r => new[] { r.ApprovedBy, r.CancelledBy }), ct);
        return [.. runs.Select(r => RunDto(r, users))];
    }

    /// <summary>One run, or null.</summary>
    public async Task<AutoMessageRunDto?> GetRunAsync(Guid id, CancellationToken ct = default)
    {
        var run = await db.AutoMessageRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return null;
        return RunDto(run, await UserNamesAsync([run.ApprovedBy, run.CancelledBy], ct));
    }

    private static AutoMessageRunDto RunDto(AutoMessageRun r, IReadOnlyDictionary<string, string> users) => new(
        r.Id, r.RuleKey, r.Period, r.Status, r.PlannedAt, r.Pupils, r.Sent, r.Failed, r.NoTelegram, r.Pending,
        r.Cancelled, r.Note,
        r.ApprovedBy is { } a ? users.GetValueOrDefault(a, a) : null, r.ApprovedAt,
        r.CancelledBy is { } c ? users.GetValueOrDefault(c, c) : null, r.CancelledAt,
        r.StartedAt, r.FinishedAt, r.ImageUrls);

    /// <summary>Counts per status, per rule × status, and per day × rule × status (by the row's time).</summary>
    public async Task<AutoMessageStatsDto> StatsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var lower = DayStart(from);
        var upper = DayStart(to.AddDays(1));
        var rows = await db.AutoMessageLog.AsNoTracking()
            .Where(l => l.At >= lower && l.At < upper)
            .Select(l => new { l.RuleKey, l.Status, l.At }).ToListAsync(ct);

        var totals = rows.GroupBy(r => r.Status, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var byRule = rows.GroupBy(r => (r.RuleKey, r.Status))
            .OrderBy(g => AutoMessageKind.All.ToList().IndexOf(g.Key.RuleKey)).ThenBy(g => g.Key.Status, StringComparer.Ordinal)
            .Select(g => new AutoMessageStatsCellDto("", g.Key.RuleKey, g.Key.Status, g.Count())).ToList();
        var cells = rows
            .GroupBy(r => (Day: AppClock.LocalDateOf(r.At).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.RuleKey, r.Status))
            .OrderByDescending(g => g.Key.Day, StringComparer.Ordinal)
            .ThenBy(g => AutoMessageKind.All.ToList().IndexOf(g.Key.RuleKey)).ThenBy(g => g.Key.Status, StringComparer.Ordinal)
            .Select(g => new AutoMessageStatsCellDto(g.Key.Day, g.Key.RuleKey, g.Key.Status, g.Count())).ToList();
        return new AutoMessageStatsDto(
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            totals, byRule, cells);
    }

    /// <summary>users.id → full name; "auto" → "Tizim".</summary>
    private async Task<Dictionary<string, string>> UserNamesAsync(IEnumerable<string?> ids, CancellationToken ct)
    {
        var list = ids.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).Distinct().ToList();
        var names = list.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        names[SystemUser] = SystemName;
        return names;
    }
}
