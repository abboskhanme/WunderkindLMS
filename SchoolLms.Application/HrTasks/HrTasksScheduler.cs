using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  HR scheduler — ONE BackgroundService, a tick every 5 minutes (old
//  application/tasks.py `hr_scheduler_tick`). No fixed cron times: each tick
//  compares the LOCAL time with hr_settings, so a time changed in the panel
//  applies without a restart. Daily jobs claim (job_name, run_date) in
//  hr_job_runs first — a restart, a second instance or a late tick never runs
//  a job twice; a job missed while the server was down runs late the same day.
//
//  Each job runs in its OWN DI scope (own DbContext): a failed job cannot
//  poison the next one's change tracker, and its hr_job_runs row is marked
//  failed from a clean context.
// ===========================================================================

public sealed class HrTasksScheduler(IServiceScopeFactory scopes, ILogger<HrTasksScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);
    /// <summary>Let start-up (migrations, seeding) finish before the first tick.</summary>
    public static readonly TimeSpan FirstTickDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstTickDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncPeopleAsync(stoppingToken);
                await TickAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // a failed tick never kills the service — the next one retries
                logger.LogError(ex, "HR rejalashtiruvchi: tick bajarilmadi");
            }
            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One tick at <paramref name="now"/>. Returns what ran (job → count); tests call it directly.</summary>
    public async Task<Dictionary<string, int>> TickAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        HrSettings settings;
        using (var scope = scopes.CreateScope())
            settings = await Service(scope).GetOrCreateSettingsAsync(ct);
        var tz = settings.Zone;
        var local = HrValues.Local(now, tz);
        var today = DateOnly.FromDateTime(local);
        var at = TimeOnly.FromDateTime(local);
        var result = new Dictionary<string, int>();

        if (at >= settings.GenerationTime
            && await GuardedAsync(HrJobs.Generate, today, now, async s => (await s.GenerateAsync(today, null, now, ct)).Created, ct) is { } generated)
            result["generated"] = generated;

        if (settings.MorningListEnabled && at >= settings.MorningTime
            && await GuardedAsync(HrJobs.Morning, today, now, s => s.SendMorningListsAsync(today, now, ct), ct) is { } morning)
            result["morning_sent"] = morning;

        if (at >= settings.DigestTime && settings.DigestWeekdays.Contains((short)HrRecurrenceRules.IsoWeekday(today))
            && await GuardedAsync(HrJobs.Digest, today, now, s => s.SendDigestAsync(today, now, ct), ct) is { } digest)
            result["digest_sent"] = digest;

        if (at >= settings.CloseOverdueTime
            && await GuardedAsync(HrJobs.CloseOverdue, today, now, s => s.CloseOverdueAsync(now, ct), ct) is { } closed)
            result["closed"] = closed;

        if (settings.ReminderEnabled)
        {
            try
            {
                using var scope = scopes.CreateScope();
                result["reminders"] = await Service(scope).SendRemindersAsync(now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "HR: eslatmalar yuborilmadi");
            }
        }

        if (at >= settings.CloseOverdueTime
            && await GuardedAsync(HrJobs.CleanDrafts, today, now, s => s.CleanDraftsAsync(now, ct), ct) is { } cleaned)
            result["drafts_cleaned"] = cleaned;

        if (result.Count > 0) logger.LogInformation("HR tick {Day}: {@Result}", today, result);
        return result;
    }

    /// <summary>
    /// New teachers/staff become HR employees (<see cref="HrTasksService.SyncLmsPeopleAsync"/>).
    /// Not a Telegram job — runs whatever the message switches say. Kept OUT of
    /// <see cref="TickAsync"/> on purpose: tests drive ticks against a shared database and must
    /// not turn every test user into an HR employee.
    /// </summary>
    private async Task SyncPeopleAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var sync = await Service(scope).SyncLmsPeopleAsync(ct);
            if (sync.Added + sync.Updated > 0)
                logger.LogInformation("HR: LMS xodimlari — {Added} ta qo'shildi, {Updated} ta yangilandi", sync.Added, sync.Updated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "HR: LMS xodimlari ro'yxatga qo'shilmadi");
        }
    }

    private static HrTasksService Service(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<HrTasksService>();

    /// <summary>Claim → work → mark ok/failed, each step in its own scope. null = not run (already done today or failed).</summary>
    private async Task<int?> GuardedAsync(
        string job, DateOnly day, DateTimeOffset now, Func<HrTasksService, Task<int>> work, CancellationToken ct)
    {
        string? runId;
        using (var scope = scopes.CreateScope())
            runId = await Service(scope).ClaimJobAsync(job, day, now, ct);
        if (runId is null) return null;

        try
        {
            int count;
            using (var scope = scopes.CreateScope())
                count = await work(Service(scope));
            using (var scope = scopes.CreateScope())
                await Service(scope).FinishJobAsync(runId, true, new { count }, DateTimeOffset.UtcNow, ct);
            return count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "HR ishi bajarilmadi: {Job} {Day}", job, day);
            try
            {
                using var scope = scopes.CreateScope();
                var error = $"{ex.GetType().Name}: {ex.Message}";
                await Service(scope).FinishJobAsync(runId, false, new { error = error.Length > 500 ? error[..500] : error },
                    DateTimeOffset.UtcNow, ct);
            }
            catch (Exception inner) { logger.LogError(inner, "HR: ish holati yozilmadi {Job}", job); }
            return null;
        }
    }
}
