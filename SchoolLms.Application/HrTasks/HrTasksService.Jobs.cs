using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  Scheduled work (old application/tasks.py + use_cases/generate_tasks.py,
//  send_morning_lists.py, send_reminders.py, send_daily_digest.py,
//  close_overdue.py, cleanup_drafts.py). Every method takes `now` explicitly —
//  the scheduler passes the tick time, tests pass a fixed instant.
// ===========================================================================

/// <summary>What one generation run did (old GenerationResult).</summary>
public sealed record HrGenerationResult(DateOnly Day, int Created, int Skipped, int Templates, IReadOnlyList<string> Errors);

public sealed partial class HrTasksService
{
    // =====================================================================
    //  Generation (old §5.2)
    // =====================================================================

    /// <summary>
    /// Materialises today's instances of active templates. Daily/weekly: one instance when the
    /// ISO weekday matches. Monthly: EVERY date of the month at once (period_start = the 1st,
    /// short months clamp to the last day), but never a date whose deadline already passed.
    /// Position targets expand to ACTIVE employees; inactive employees get nothing.
    /// Idempotent: an existing (template, employee, period_start[, due_at]) is skipped.
    /// </summary>
    public async Task<HrGenerationResult> GenerateAsync(DateOnly? day, string? templateId, DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var target = day ?? HrValues.LocalDate(now, tz);

        List<HrTaskTemplate> templates;
        if (templateId is not null)
        {
            var one = await db.HrTaskTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, ct);
            templates = one is null ? [] : [one];
        }
        else
            templates = await db.HrTaskTemplates.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync(ct);

        var created = 0;
        var skipped = 0;
        var matched = 0;
        var errors = new List<string>();
        var batch = new List<HrTask>();
        foreach (var template in templates)
        {
            var occurrences = Occurrences(template, target, now, tz);
            if (occurrences.Length == 0) continue;
            matched++;
            try
            {
                var (made, missed) = await MaterializeAsync(template, occurrences, tz, now, batch, ct);
                created += made;
                skipped += missed;
            }
            catch (HrTasksException ex)
            {
                errors.Add($"{template.Id}: {ex.Message}");
            }
        }
        if (batch.Count > 0)
        {
            db.HrTasks.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }
        return new HrGenerationResult(target, created, skipped, matched, errors);
    }

    /// <summary>The dates a template produces for <paramref name="day"/> (pure; public for tests).</summary>
    public static DateOnly[] Occurrences(HrTaskTemplate template, DateOnly day, DateTimeOffset now, TimeZoneInfo tz)
    {
        if (template.Recurrence != HrRecurrences.Monthly)
            return HrRecurrenceRules.OccursOn(template.Recurrence, template.WeekdayList, template.DayOfMonthList, day) ? [day] : [];
        return [.. HrRecurrenceRules.MonthlyOccurrences(template.DayOfMonthList, day)
            .Where(o => HrRecurrenceRules.DueAtFor(o, template.DueTime, tz) >= now)];
    }

    private async Task<(int Created, int Skipped)> MaterializeAsync(
        HrTaskTemplate template, DateOnly[] occurrences, TimeZoneInfo tz, DateTimeOffset now, List<HrTask> batch, CancellationToken ct)
    {
        List<HrEmployee> recipients;
        if (template.TargetType == HrTargetTypes.Employee)
        {
            var employee = await db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == template.TargetEmployeeId, ct);
            recipients = employee is { IsActive: true } ? [employee] : [];
        }
        else
            recipients = await db.HrEmployees.AsNoTracking()
                .Where(e => e.PositionId == template.TargetPositionId && e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);
        if (recipients.Count == 0) return (0, 0);

        var periodStart = HrRecurrenceRules.PeriodStartFor(template.Recurrence, occurrences[0]);
        var existing = (await db.HrTasks.AsNoTracking()
                .Where(t => t.TemplateId == template.Id && t.PeriodStart == periodStart)
                .Select(t => new { t.EmployeeId, t.DueAt }).ToListAsync(ct))
            .Concat(batch.Where(t => t.TemplateId == template.Id && t.PeriodStart == periodStart).Select(t => new { t.EmployeeId, t.DueAt }))
            .ToList();

        var created = 0;
        var skipped = 0;
        foreach (var employee in recipients)
        {
            var taken = existing.Where(e => e.EmployeeId == employee.Id).Select(e => e.DueAt).ToHashSet();
            // single occurrence (daily/weekly or one monthly date): any instance of this period counts
            // as done — even if the template's due time was changed since (old _pending)
            DateOnly[] wanted = taken.Count == 0 ? occurrences
                : occurrences.Length == 1 ? []
                : [.. occurrences.Where(o => !taken.Contains(HrRecurrenceRules.DueAtFor(o, template.DueTime, tz)))];
            skipped += occurrences.Length - wanted.Length;
            foreach (var occurrence in wanted)
            {
                batch.Add(new HrTask
                {
                    EmployeeId = employee.Id,
                    TemplateId = template.Id,
                    Source = HrTaskSources.Template,
                    Title = template.Title,
                    Description = template.Description,
                    Kind = template.Kind,
                    TargetAmount = template.TargetAmount,
                    Unit = template.Unit,
                    ProofRequired = template.ProofRequired,
                    OverduePolicy = template.OverduePolicy,
                    PeriodStart = HrRecurrenceRules.PeriodStartFor(template.Recurrence, occurrence),
                    DueAt = HrRecurrenceRules.DueAtFor(occurrence, template.DueTime, tz),
                    Status = HrTaskStatuses.Open,
                    CreatedBy = null, // created by the scheduler, not a person (old §3.4)
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                created++;
            }
        }
        return (created, skipped);
    }

    // =====================================================================
    //  Legacy close (overdue_policy = close rows only — new rows stay open)
    // =====================================================================

    public async Task<int> CloseOverdueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        var rows = await db.HrTasks
            .Where(t => (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                && t.OverduePolicy == HrOverduePolicies.Close && t.DueAt < now)
            .ToListAsync(ct);
        var closed = rows.Count(t => t.MarkMissed(now));
        if (closed > 0) await db.SaveChangesAsync(ct);
        return closed;
    }

    /// <summary>Drafts nobody bound within 24 h — the row and its file go.</summary>
    public async Task<int> CleanDraftsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var cutoff = now.ToUniversalTime().AddHours(-HrLimits.DraftTtlHours);
        var drafts = await db.HrTaskAttachments.Where(a => a.TaskId == null && a.CreatedAt < cutoff).ToListAsync(ct);
        if (drafts.Count == 0) return 0;
        db.HrTaskAttachments.RemoveRange(drafts);
        await db.SaveChangesAsync(ct);
        foreach (var draft in drafts)
        {
            try { files.Delete(draft.FilePath); }
            catch (Exception ex) { logger.LogWarning(ex, "HR: draft fayli o'chirilmadi {Path}", draft.FilePath); }
        }
        return drafts.Count;
    }

    // =====================================================================
    //  Telegram jobs — morning list, reminders, evening digest
    // =====================================================================

    private static HrDigestTask DigestTask(HrTask t, int proofCount) => new(
        t.Id, t.Title, t.Status, t.Kind, t.IsLate, t.DueAt, t.EmployeeComment, proofCount, t.TargetAmount, t.CurrentAmount, t.Unit);

    private async Task<Dictionary<string, int>> ProofCountsAsync(List<string> taskIds, CancellationToken ct) =>
        await db.HrTaskProofs.AsNoTracking().Where(p => taskIds.Contains(p.TaskId))
            .GroupBy(p => p.TaskId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    /// <summary>
    /// Each active, linked employee gets their open list (period_start ≤ day), overdue first in red.
    /// No tasks → no message. Returns messages delivered.
    /// </summary>
    public async Task<int> SendMorningListsAsync(DateOnly? day, DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        var settings = await GetOrCreateSettingsAsync(ct);
        if (!settings.MorningListEnabled || !telegram.IsConfigured) return 0;
        var tz = settings.Zone;
        var target = day ?? HrValues.LocalDate(now, tz);
        var rows = await (from t in db.HrTasks.AsNoTracking()
                          join e in db.HrEmployees.AsNoTracking() on t.EmployeeId equals e.Id
                          where e.IsActive && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                                && t.PeriodStart <= target
                          orderby e.FullName, t.DueAt, t.Id
                          select new { Task = t, e.UserId, EmployeeId = e.Id }).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        var proofs = await ProofCountsAsync([.. rows.Select(r => r.Task.Id)], ct);
        var chats = await ChatsAsync(rows.Select(r => r.UserId), ct);
        var sent = 0;
        foreach (var group in rows.GroupBy(r => (r.EmployeeId, r.UserId)))
        {
            if (!chats.TryGetValue(group.Key.UserId, out var chatIds)) continue;
            var text = HrTasksTexts.Morning([.. group.Select(r => DigestTask(r.Task, proofs.GetValueOrDefault(r.Task.Id)))], tz, now);
            if (text.Length == 0) continue;
            foreach (var chat in chatIds)
                if (await SafeSendAsync(chat, text, ct)) sent++;
        }
        return sent;
    }

    private async Task<bool> SafeSendAsync(long chat, string text, CancellationToken ct)
    {
        try { return await telegram.SendHtmlAsync(chat, Truncate(text), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "HR: Telegram xabari yuborilmadi (chat {Chat})", chat);
            return false;
        }
    }

    /// <summary>
    /// Deadline reminders (every tick). Due within the largest offset, still open; "end of day"
    /// (23:59) tasks skipped unless enabled. Each (task, offset) is CLAIMED once in
    /// <c>hr_task_reminders</c>; a late tick claims all pending offsets but sends ONE line.
    /// One message per employee. Returns messages delivered.
    /// </summary>
    public async Task<int> SendRemindersAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        var settings = await GetOrCreateSettingsAsync(ct);
        if (!settings.ReminderEnabled || settings.ReminderOffsets.Length == 0 || !telegram.IsConfigured) return 0;
        var tz = settings.Zone;
        var offsets = settings.ReminderOffsets.Select(o => (int)o).ToArray();
        var horizon = now.AddMinutes(offsets.Max());
        var tasks = await (from t in db.HrTasks.AsNoTracking()
                           join e in db.HrEmployees.AsNoTracking() on t.EmployeeId equals e.Id
                           where e.IsActive && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                                 && t.DueAt > now && t.DueAt <= horizon
                           orderby t.DueAt, t.Id
                           select new { Task = t, e.UserId }).ToListAsync(ct);
        tasks = [.. tasks.Where(x => settings.RemindEndOfDay || !HrReminderRules.IsEndOfDay(x.Task.DueAt, tz))];
        if (tasks.Count == 0) return 0;

        var chats = await ChatsAsync(tasks.Select(x => x.UserId), ct);
        tasks = [.. tasks.Where(x => chats.ContainsKey(x.UserId))];
        var ids = tasks.Select(x => x.Task.Id).ToList();
        var claimed = (await db.HrTaskReminders.AsNoTracking().Where(r => ids.Contains(r.TaskId))
                .Select(r => new { r.TaskId, r.OffsetMinutes }).ToListAsync(ct))
            .Select(r => (r.TaskId, (int)r.OffsetMinutes)).ToHashSet();

        var plan = new Dictionary<string, List<(string Title, int Remaining)>>();
        foreach (var x in tasks)
        {
            int? winner = null;
            foreach (var offset in HrReminderRules.DueOffsets(x.Task.DueAt, now, offsets))
            {
                if (!claimed.Add((x.Task.Id, offset))) continue;
                db.HrTaskReminders.Add(new HrTaskReminder { TaskId = x.Task.Id, OffsetMinutes = (short)offset, SentAt = now });
                winner ??= offset;
            }
            if (winner is null) continue;
            if (!plan.TryGetValue(x.UserId, out var items)) plan[x.UserId] = items = [];
            items.Add((x.Task.Title, Math.Max(1, (int)Math.Floor((x.Task.DueAt - now).TotalSeconds / 60))));
        }
        if (plan.Count == 0) return 0;
        await db.SaveChangesAsync(ct); // claim first — a crash after this never repeats a reminder

        var sent = 0;
        foreach (var (userId, items) in plan)
        {
            var text = HrTasksTexts.Reminder(items);
            foreach (var chat in chats[userId])
                if (await SafeSendAsync(chat, text, ct)) sent++;
        }
        return sent;
    }

    /// <summary>
    /// The digest data for one day and scope (old read_models.digest_data): done that day;
    /// due that day and not done; keep_open, overdue from earlier, still open. Active employees only.
    /// </summary>
    public async Task<HrDigest> DigestDataAsync(DateOnly day, IReadOnlySet<string>? scopeIds, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        var tz = settings.Zone;
        var start = HrValues.DayStart(day, tz);
        var end = HrValues.DayStart(day.AddDays(1), tz);
        var q = from t in db.HrTasks.AsNoTracking()
                join e in db.HrEmployees.AsNoTracking() on t.EmployeeId equals e.Id
                where e.IsActive
                      && ((t.Status == HrTaskStatuses.Done && t.CompletedAt >= start && t.CompletedAt < end)
                          || ((t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned || t.Status == HrTaskStatuses.Missed)
                              && t.DueAt >= start && t.DueAt < end)
                          || (t.OverduePolicy == HrOverduePolicies.KeepOpen
                              && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned) && t.DueAt < start))
                select new { Task = t, e.Id, e.FullName, e.PositionId, e.UserId };
        if (scopeIds is not null)
        {
            var ids = scopeIds.ToList();
            q = q.Where(x => ids.Contains(x.Id));
        }
        var rows = await q.OrderBy(x => x.FullName).ThenBy(x => x.Task.DueAt).ThenBy(x => x.Task.Id).ToListAsync(ct);

        var selfQ = from t in db.HrTasks.AsNoTracking()
                    join e in db.HrEmployees.AsNoTracking() on t.EmployeeId equals e.Id
                    where e.IsActive && t.Source == HrTaskSources.Self && t.CreatedAt >= start && t.CreatedAt < end
                    select t.EmployeeId;
        if (scopeIds is not null)
        {
            var ids = scopeIds.ToList();
            selfQ = selfQ.Where(id => ids.Contains(id));
        }
        var selfCreated = await selfQ.CountAsync(ct);

        var proofs = await ProofCountsAsync([.. rows.Select(r => r.Task.Id)], ct);
        var positionIds = rows.Where(r => r.PositionId != null).Select(r => r.PositionId!).Distinct().ToList();
        var positions = await db.HrPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var employees = new List<HrDigestEmployee>();
        foreach (var group in rows.GroupBy(r => r.Id))
        {
            var first = group.First();
            var done = new List<HrDigestTask>();
            var notDone = new List<HrDigestTask>();
            var overdue = new List<HrDigestTask>();
            foreach (var r in group)
            {
                var task = DigestTask(r.Task, proofs.GetValueOrDefault(r.Task.Id));
                if (r.Task.Status == HrTaskStatuses.Done && r.Task.CompletedAt >= start && r.Task.CompletedAt < end) done.Add(task);
                else if (r.Task.DueAt >= start && r.Task.DueAt < end) notDone.Add(task);
                else overdue.Add(task);
            }
            employees.Add(new HrDigestEmployee(first.Id, first.FullName,
                first.PositionId is null ? null : positions.GetValueOrDefault(first.PositionId), first.UserId, done, notDone, overdue));
        }
        return new HrDigest(day, employees, selfCreated);
    }

    /// <summary>
    /// One digest per recipient: admin/superadmin and staff with <c>hrTasks</c> (or its view key)
    /// who have Telegram. Directors (admin/superadmin, hr_role director) get the whole
    /// organisation, managers their subtree, a panel user without an HR row nothing. Empty → not
    /// sent (checked per scope). Returns messages delivered.
    /// </summary>
    public async Task<int> SendDigestAsync(DateOnly? day, DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        var settings = await GetOrCreateSettingsAsync(ct);
        if (!telegram.IsConfigured) return 0;
        var tz = settings.Zone;
        var target = day ?? HrValues.LocalDate(now, tz);

        var key = HrTaskAccess.PermissionKey;
        var viewKey = key + Roles.ViewSuffix;
        var recipients = await (from u in db.Users.AsNoTracking()
                                join a in db.TelegramAccounts.AsNoTracking() on u.Id equals a.UserId
                                where u.Role == Roles.Admin || u.Role == Roles.SuperAdmin
                                      || (u.Role == Roles.Staff && (u.Permissions.Contains(key) || u.Permissions.Contains(viewKey)))
                                select new { u.Id, u.Role, a.TelegramUserId }).ToListAsync(ct);
        if (recipients.Count == 0) return 0;

        var userIds = recipients.Select(r => r.Id).Distinct().ToList();
        var hrRows = await db.HrEmployees.AsNoTracking().Where(e => userIds.Contains(e.UserId)).ToDictionaryAsync(e => e.UserId, ct);
        var edges = await EdgesAsync(ct);
        var cache = new Dictionary<string, IReadOnlyList<string>>();
        var sent = 0;
        foreach (var r in recipients)
        {
            try
            {
                IReadOnlySet<string>? scope;
                if (r.Role is Roles.Admin or Roles.SuperAdmin) scope = null;
                else if (!hrRows.TryGetValue(r.Id, out var row)) continue; // no HR row → nobody in scope
                else scope = row.HrRole == HrRoles.Director ? null : Subtree(edges, row.Id);

                var cacheKey = scope is null ? "*" : string.Join(",", scope.OrderBy(x => x, StringComparer.Ordinal));
                if (!cache.TryGetValue(cacheKey, out var chunks))
                {
                    var digest = await DigestDataAsync(target, scope, ct);
                    if (digest.IsEmpty) chunks = [];
                    else
                    {
                        var summary = await SummaryCoreAsync(scope, target, target, null, tz, now, ct);
                        chunks = HrTasksTexts.Digest(digest with { UnacceptedCount = summary.UnacceptedCount }, tz);
                    }
                    cache[cacheKey] = chunks;
                }
                foreach (var chunk in chunks)
                    if (await SafeSendAsync(r.TelegramUserId, chunk, ct)) sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // one recipient's failure never stops the others
                logger.LogWarning(ex, "HR: kunlik yakun yuborilmadi (user {UserId})", r.Id);
            }
        }
        return sent;
    }

    // =====================================================================
    //  hr_job_runs — once per job per local day (old SchedulerTick._guarded)
    // =====================================================================

    /// <summary>Claims (job, day). null = already run (or running) today.</summary>
    public async Task<string?> ClaimJobAsync(string jobName, DateOnly runDate, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await db.HrJobRuns.AnyAsync(r => r.JobName == jobName && r.RunDate == runDate, ct)) return null;
        var run = new HrJobRun { JobName = jobName, RunDate = runDate, StartedAt = now.ToUniversalTime(), Status = HrJobs.StatusOk };
        db.HrJobRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
            return run.Id;
        }
        catch (DbUpdateException)
        {
            db.HrJobRuns.Entry(run).State = EntityState.Detached;
            return null; // the unique key (job_name, run_date) — another worker won
        }
    }

    public async Task FinishJobAsync(string runId, bool ok, object details, DateTimeOffset now, CancellationToken ct = default)
    {
        var run = await db.HrJobRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;
        run.FinishedAt = now.ToUniversalTime();
        run.Status = ok ? HrJobs.StatusOk : HrJobs.StatusFailed;
        run.Details = JsonSerializer.Serialize(details);
        await db.SaveChangesAsync(ct);
    }
}
