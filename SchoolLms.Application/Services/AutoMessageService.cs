using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Automatic Telegram messages to parents (docs/modules/auto-messages.md, 2026-10-13).
//
//  Four scheduled rules — absence, invoice, debt, birthday — each switched on separately once the
//  school approves its text, plus the event-driven payment rule (ReceiptService). The scheduler
//  (AutoMessageScheduler) calls RunDueAsync every couple of minutes.
//
//  2026-10-15 — runs and statuses. Each rule has one run per period (auto_message_runs):
//    scheduled         created on the first tick of its day, with the planned time;
//    pending_approval  the rule requires approval: the messages are prepared, nothing is sent;
//    sending → done    the messages go out pupil by pupil; the run's status is re-read between
//                      pupils, so cancelling (or switching the rule off) stops it;
//    cancelled         by staff, by switching the rule off, or when the period ends unapproved.
//  Every message is a log row (auto_message_log) that is first written as scheduled / pending and
//  then sent; a failed row is retried on later ticks inside the period, up to MaxAttempts.
// ===========================================================================

/// <summary>One rule's run (or one step of it).</summary>
/// <param name="Closed">false — the source was not ready (no invoices yet); the period stays open.</param>
/// <param name="Pending">Chats waiting for approval.</param>
/// <param name="Note">What the run left out (absence: classes whose attendance was not finished).</param>
public sealed record AutoMessageRunResult(
    string Key, string Period, int Pupils, int Sent, int AlreadySent, int Failed, int NoTelegram, bool Closed,
    int Pending = 0, string? Note = null, Guid? RunId = null);

/// <summary>What a rule would do now, without sending anything.</summary>
public sealed record AutoMessagePreviewDto(
    string Key, string Period, int Pupils, int WithTelegram, int AlreadySent, string? Sample, string? SampleFor);

/// <summary>A token the rule's text may use, with what it becomes and the client's word for it.</summary>
public sealed record AutoMessageTokenDto(string Token, string Meaning, string? Label = null);

/// <param name="env">Where <c>uploads/</c> lives — the rules' pictures are read from there; null — text only.</param>
public sealed partial class AutoMessageService(
    IAppDbContext db, TelegramService telegram, ILogger<AutoMessageService>? logger = null, IWebHostEnvironment? env = null)
{
    /// <summary><c>absence_notices.sent_by</c> / <c>cancelled_by</c> for what the system did itself.</summary>
    public const string SystemUser = "auto";

    /// <summary>A failed message of a run is tried this many times in total.</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// One sender at a time in this process: the scheduler's tick, an approval and a manual run never
    /// send the same run twice in parallel. Cancelling does not take it — it must be able to stop a
    /// run that is sending.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Pause between two messages — keeps a big run under Telegram's ~30 messages a second.</summary>
    internal static TimeSpan SendPause { get; set; } = TimeSpan.FromMilliseconds(40);

    private const string RejectedByTelegram = "Telegram xabarni qabul qilmadi";

    private readonly ILogger log = logger ?? NullLogger<AutoMessageService>.Instance;

    // =====================================================================
    //  Texts and tokens
    // =====================================================================

    /// <summary>The text each rule ships with (the migrations seed the same).</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultText = new Dictionary<string, string>
    {
        [AutoMessageKind.Absence] =
            "📋 **Davomat**\n\nHurmatli ota-ona, farzandingiz {fish} ({sinf}) {sana} kuni maktabga kelmadi.\nSabab: {sabab}",
        [AutoMessageKind.Invoice] =
            "🧾 **{oy} oyi uchun hisob**\n\nHurmatli ota-ona, farzandingiz {fish} ({sinf}) uchun {oy} oyi to'lovi: {summa} so'm.\nJami to'lanmagan: {qarzdorlik} so'm.",
        [AutoMessageKind.Debt] =
            "💳 **To'lov eslatmasi**\n\nHurmatli ota-ona, farzandingiz {fish} ({sinf}) bo'yicha {qarzdorlik} so'm qarzdorlik bor ({oylar}).\nIltimos, to'lovni o'z vaqtida amalga oshiring.",
        [AutoMessageKind.Birthday] =
            "🎉 **Tug'ilgan kuningiz muborak!**\n\nHurmatli ota-ona, bugun farzandingiz {ism}ning tug'ilgan kuni. Wunderkind maktabi jamoasi uni chin qalbdan tabriklaydi: sog'lik, baxt va yangi yutuqlar tilaymiz!",
        [AutoMessageKind.Payment] =
            "✅ **To'lov qabul qilindi**\n\nHurmatli ota-ona, farzandingiz {fish} ({sinf}) uchun {summa} so'm to'lov qabul qilindi.\nChek № {chek} · {sana} {vaqt}\nTo'lov usuli: {usul}\nQolgan qarzdorlik: {qarzdorlik} so'm",
    };

    private static readonly AutoMessageTokenDto[] Common =
    [
        new("{fish}", "O'quvchining F.I.Sh.", "F.I.Sh."),
        new("{ism}", "O'quvchining ismi", "Ism"),
        new("{sinf}", "Sinfi", "Sinf"),
        new("{ota_ona}", "Botga ulangan ota-onaning ismi", "Ota-ona"),
    ];

    private static readonly AutoMessageTokenDto SendDay = new("{sana}", "Yuborilgan kun (15.10.2026)", "Sana");
    private static readonly AutoMessageTokenDto SendClock = new("{vaqt}", "Yuborilgan vaqt (10:00)", "Vaqt");

    public static IReadOnlyList<AutoMessageTokenDto> TokensOf(string key) => key switch
    {
        AutoMessageKind.Absence =>
        [
            .. Common, new("{sana}", "Davomat kuni (13.10.2026)", "Sana"), SendClock,
            new("{sabab}", "Yo'qlik sababi (bo'lmasa — qator tushib qoladi)", "Sabab"),
        ],
        AutoMessageKind.Invoice =>
        [
            .. Common, SendDay, SendClock, new("{oy}", "Oy nomi (Oktabr)", "Oy"),
            new("{summa}", "Shu oy hisobi, so'm", "To'lov summasi"), new("{qarzdorlik}", "Jami to'lanmagan, so'm", "Qarzdorlik"),
        ],
        AutoMessageKind.Debt =>
        [
            .. Common, SendDay, SendClock, new("{qarzdorlik}", "Jami qarz, so'm", "Qarzdorlik"),
            new("{oylar}", "To'lanmagan oylar (Sentabr, Oktabr)", "Oylar"),
        ],
        AutoMessageKind.Payment =>
        [
            .. Common, new("{summa}", "Qabul qilingan summa, so'm", "To'lov summasi"),
            new("{sana}", "To'lov kuni (15.10.2026)", "Sana"), new("{vaqt}", "To'lov vaqti (14:35)", "Vaqt"),
            new("{chek}", "Chek raqami", "Chek №"), new("{usul}", "To'lov usuli (Naqd pul, Bank kartasi)", "To'lov usuli"),
            new("{qarzdorlik}", "To'lovdan keyin qolgan qarz, so'm", "Qarzdorlik"),
        ],
        _ => [.. Common, SendDay, SendClock],
    };

    private static readonly string[] MonthNames =
    [
        "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
        "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr",
    ];

    // =====================================================================
    //  Scheduling
    // =====================================================================

    /// <summary>
    /// One tick (school time <paramref name="localNow"/>): stops runs whose period is over, plans
    /// today's runs, runs the due ones, resumes interrupted ones and retries failed messages.
    /// </summary>
    public async Task<List<AutoMessageRunResult>> RunDueAsync(DateTime localNow, CancellationToken ct = default)
    {
        var results = new List<AutoMessageRunResult>();
        if (!telegram.IsConfigured) return results;
        await Gate.WaitAsync(ct);
        try
        {
            var rules = await db.AutoMessageRules.OrderBy(r => r.Key).ToListAsync(ct);
            await CloseStaleRunsAsync(rules, localNow, ct);

            foreach (var rule in rules.Where(r => r.Enabled && !r.IsArchived && !AutoMessageKind.IsEventDriven(r.Key)))
            {
                try
                {
                    await PlanRunAsync(rule, localNow, ct);
                    var period = DuePeriod(rule, localNow);
                    if (period is null) continue;
                    var run = await RunOfAsync(rule.Key, period, ct);
                    // A period closed before runs existed (2026-10-13 build) stays closed.
                    if (run is null && rule.LastPeriod == period) continue;
                    switch (run?.Status)
                    {
                        case AutoMessageStatus.Cancelled:
                        case AutoMessageStatus.PendingApproval:
                            continue;
                        case AutoMessageStatus.Done:
                            if (await RetryFailedAsync(rule, run!, ct) is { } retried) results.Add(retried);
                            continue;
                        case AutoMessageStatus.Sending:
                            // Interrupted (a restart, or an approval whose request was cut off).
                            var (sent, failed) = await FinishRunAsync(rule, run!, null, ct);
                            results.Add(new AutoMessageRunResult(rule.Key, period, run!.Pupils, sent, 0, failed,
                                run.NoTelegram, true, run.Pending, run.Note, run.Id));
                            continue;
                        default:
                            results.Add(await RunCoreAsync(rule, period, localNow, ct));
                            continue;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One rule failing must not stop the others; it is tried again on the next tick.
                    log.LogError(ex, "Avtomatik xabar ({Rule}) xato bilan to'xtadi", rule.Key);
                }
            }
        }
        finally
        {
            Gate.Release();
        }
        return results;
    }

    /// <summary>
    /// The period a rule should run for now, or null. Daily rules: today, once the send time has
    /// passed. Monthly rules: this month, from the send time on their day until two days later
    /// (a restart or a late source is caught up; a rule switched on later in the month waits).
    /// </summary>
    public static string? DuePeriod(AutoMessageRule rule, DateTime localNow)
    {
        if (AutoMessageKind.IsEventDriven(rule.Key)) return null;
        if (!TryTime(rule.SendTime, out var at)) return null;
        var today = DateOnly.FromDateTime(localNow);
        var timeReached = TimeOnly.FromDateTime(localNow) >= at;
        if (!AutoMessageKind.IsMonthly(rule))
            return timeReached && AutoMessageKind.RunsOn(rule, today) ? Iso(today) : null;

        var due = DueDay(rule, today);
        var inWindow = (today == due && timeReached) || (today > due && today <= due.AddDays(2));
        return inWindow ? today.ToString("yyyy-MM", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// The run a rule has today, planned or due: daily rules — today at the send time; monthly
    /// rules — from their day until two days later, planned for the send time on the day.
    /// </summary>
    public static (string Period, DateTime PlannedLocal)? PlannedRun(AutoMessageRule rule, DateTime localNow)
    {
        if (AutoMessageKind.IsEventDriven(rule.Key) || !TryTime(rule.SendTime, out var at)) return null;
        var today = DateOnly.FromDateTime(localNow);
        if (!AutoMessageKind.IsMonthly(rule))
            return AutoMessageKind.RunsOn(rule, today) ? (Iso(today), today.ToDateTime(at)) : null;
        var due = DueDay(rule, today);
        if (today < due || today > due.AddDays(2)) return null;
        return (today.ToString("yyyy-MM", CultureInfo.InvariantCulture), due.ToDateTime(at));
    }

    /// <summary>The period can no longer run: a past day, or a month past its two-day window.</summary>
    public static bool PeriodOver(AutoMessageRule rule, string period, DateTime localNow)
    {
        var today = DateOnly.FromDateTime(localNow);
        if (period.Length == 10)
            return DateOnly.TryParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                   && day < today;
        var month = MonthOf(period);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        if (month != thisMonth) return month < thisMonth;
        return today > DueDay(rule, today).AddDays(2);
    }

    /// <summary>Creates today's run as "Rejalashtirilgan" the first time a tick sees it.</summary>
    private async Task PlanRunAsync(AutoMessageRule rule, DateTime localNow, CancellationToken ct)
    {
        if (PlannedRun(rule, localNow) is not { } plan || rule.LastPeriod == plan.Period) return;
        if (await db.AutoMessageRuns.AnyAsync(r => r.RuleKey == rule.Key && r.Period == plan.Period, ct)) return;
        db.AutoMessageRuns.Add(new AutoMessageRun
        {
            RuleKey = rule.Key, Period = plan.Period, Status = AutoMessageStatus.Scheduled,
            PlannedAt = ToInstant(plan.PlannedLocal), CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Scheduled or pending runs whose rule is off or whose period is over are stopped.</summary>
    private async Task CloseStaleRunsAsync(List<AutoMessageRule> rules, DateTime localNow, CancellationToken ct)
    {
        var open = await db.AutoMessageRuns
            .Where(r => r.Status == AutoMessageStatus.Scheduled || r.Status == AutoMessageStatus.PendingApproval)
            .ToListAsync(ct);
        foreach (var run in open)
        {
            var rule = rules.FirstOrDefault(r => r.Key == run.RuleKey);
            var why = rule is null || !rule.Enabled || rule.IsArchived ? "Qoida o'chirilgan"
                : !PeriodOver(rule, run.Period, localNow) ? null
                : run.Status == AutoMessageStatus.PendingApproval ? "Tasdiqlanmadi — muddati tugadi"
                : "Vaqti o'tdi";
            if (why is null) continue;
            await CancelCoreAsync(run, SystemUser, why, ct);
            log.LogInformation("Avtomatik xabar ({Rule}, {Period}) to'xtatildi: {Why}", run.RuleKey, run.Period, why);
        }
    }

    // =====================================================================
    //  One run
    // =====================================================================

    /// <summary>
    /// Runs the rule for <paramref name="period"/> now: prepares its messages and sends them (or leaves
    /// them waiting for approval). Running it again sends only what has not gone yet and retries failures.
    /// </summary>
    public async Task<AutoMessageRunResult> RunAsync(AutoMessageRule rule, string period, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await RunCoreAsync(rule, period, AppClock.Now, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<AutoMessageRunResult> RunCoreAsync(AutoMessageRule rule, string period, DateTime localNow, CancellationToken ct)
    {
        var source = await ItemsAsync(rule, period, localNow, ct);
        if (source is null)
        {
            log.LogInformation("Avtomatik xabar ({Rule}, {Period}): manba hali tayyor emas", rule.Key, period);
            return new AutoMessageRunResult(rule.Key, period, 0, 0, 0, 0, 0, Closed: false);
        }

        var run = await RunOfAsync(rule.Key, period, ct);
        if (run is null)
        {
            run = new AutoMessageRun
            {
                RuleKey = rule.Key, Period = period, Status = AutoMessageStatus.Scheduled,
                PlannedAt = ToInstant(PlannedLocalOf(rule, period)), CreatedAt = AppClock.NowInstant,
            };
            db.AutoMessageRuns.Add(run);
        }
        if (run.Status == AutoMessageStatus.Cancelled)
            return new AutoMessageRunResult(rule.Key, period, 0, 0, 0, 0, 0, Closed: true, Note: run.Note, RunId: run.Id);

        run.Note = source.Note;
        // The pictures are taken with the text: an approved run sends what was approved (2026-10-25).
        run.ImageUrls = [.. rule.ImageUrls];
        var awaitApproval = rule.RequiresApproval && run.ApprovedAt is null;
        var (already, noTelegram) = await MaterializeAsync(
            rule, run, source.Items, awaitApproval ? AutoMessageStatus.PendingApproval : AutoMessageStatus.Scheduled, ct);

        if (awaitApproval)
        {
            if (run.Status is AutoMessageStatus.Scheduled or AutoMessageStatus.Done)
                await MoveRunAsync(run, [AutoMessageStatus.Scheduled, AutoMessageStatus.Done], AutoMessageStatus.PendingApproval, ct);
            await RefreshCountsAsync(run, ct);
            log.LogInformation("Avtomatik xabar ({Rule}, {Period}): tasdiq kutmoqda, chat={Pending}", rule.Key, period, run.Pending);
            return new AutoMessageRunResult(rule.Key, period, source.Items.Count, 0, already, 0, noTelegram, true,
                run.Pending, run.Note, run.Id);
        }

        var reasonOf = source.Items.ToDictionary(i => i.StudentId, i => i.AbsenceReasonId, StringComparer.Ordinal);
        var (sent, failed) = await FinishRunAsync(rule, run, reasonOf, ct);
        log.LogInformation(
            "Avtomatik xabar ({Rule}, {Period}): o'quvchi={Pupils}, yuborildi={Sent}, oldin={Already}, xato={Failed}, ulanmagan={NoTelegram}",
            rule.Key, period, source.Items.Count, sent, already, failed, noTelegram);
        return new AutoMessageRunResult(rule.Key, period, source.Items.Count, sent, already, failed, noTelegram, true,
            run.Pending, run.Note, run.Id);
    }

    /// <summary>Sends what is waiting in the run, closes it (unless it was cancelled meanwhile) and the rule's period.</summary>
    private async Task<(int Sent, int Failed)> FinishRunAsync(
        AutoMessageRule rule, AutoMessageRun run, IReadOnlyDictionary<string, string?>? reasonOf, CancellationToken ct)
    {
        if (run.Status != AutoMessageStatus.Sending
            && !await MoveRunAsync(run, [AutoMessageStatus.Scheduled, AutoMessageStatus.PendingApproval, AutoMessageStatus.Done],
                AutoMessageStatus.Sending, ct))
            return (0, 0);

        var result = await SendRowsAsync(rule, run, AutoMessageStatus.WaitingRow, reasonOf, ct);
        await MoveRunAsync(run, [AutoMessageStatus.Sending], AutoMessageStatus.Done, ct);
        if (run.Status == AutoMessageStatus.Done)
        {
            rule.LastPeriod = run.Period;
            rule.LastRunAt = AppClock.NowInstant;
            await db.SaveChangesAsync(ct);
        }
        await RefreshCountsAsync(run, ct);
        return result;
    }

    /// <summary>Failed messages of a finished run are tried again (fewer than <see cref="MaxAttempts"/> so far).</summary>
    private async Task<AutoMessageRunResult?> RetryFailedAsync(AutoMessageRule rule, AutoMessageRun run, CancellationToken ct)
    {
        var due = await db.AutoMessageLog.AnyAsync(l => l.RunId == run.Id && l.ChatId != null
            && l.Status == BroadcastDelivery.Failed && l.Attempts < MaxAttempts, ct);
        if (!due) return null;
        var (sent, failed) = await SendRowsAsync(rule, run, [BroadcastDelivery.Failed], null, ct);
        await RefreshCountsAsync(run, ct);
        log.LogInformation("Avtomatik xabar ({Rule}, {Period}): qayta urinish — yuborildi={Sent}, xato={Failed}",
            rule.Key, run.Period, sent, failed);
        return new AutoMessageRunResult(rule.Key, run.Period, run.Pupils, sent, 0, failed, run.NoTelegram, true, run.Pending, run.Note, run.Id);
    }

    // =====================================================================
    //  Approval and stopping
    // =====================================================================

    /// <summary>Approves a run waiting for approval and sends it now.</summary>
    /// <exception cref="KeyNotFoundException">No such run.</exception>
    /// <exception cref="InvalidOperationException">Not waiting for approval, past its period, or no bot.</exception>
    public async Task<AutoMessageRunResult> ApproveAsync(Guid runId, string userId, DateTime localNow, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured)
            throw new InvalidOperationException("Telegram bot sozlanmagan — Sozlamalar → Integratsiyalar.");
        await Gate.WaitAsync(ct);
        try
        {
            var run = await db.AutoMessageRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                      ?? throw new KeyNotFoundException("Yuborish topilmadi");
            if (run.Status != AutoMessageStatus.PendingApproval)
                throw new InvalidOperationException("Faqat tasdiq kutayotgan yuborishni tasdiqlash mumkin.");
            var rule = await db.AutoMessageRules.FirstAsync(r => r.Key == run.RuleKey, ct);
            if (PeriodOver(rule, run.Period, localNow))
            {
                await CancelCoreAsync(run, SystemUser, "Tasdiqlanmadi — muddati tugadi", ct);
                throw new InvalidOperationException("Muddati o'tgan — yuborish to'xtatildi.");
            }

            var now = AppClock.NowInstant;
            var claimed = await db.AutoMessageRuns
                .Where(r => r.Id == run.Id && r.Status == AutoMessageStatus.PendingApproval)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, AutoMessageStatus.Sending)
                    .SetProperty(r => r.ApprovedBy, userId)
                    .SetProperty(r => r.ApprovedAt, (DateTimeOffset?)now)
                    .SetProperty(r => r.StartedAt, (DateTimeOffset?)now), ct);
            await db.AutoMessageRuns.Entry(run).ReloadAsync(ct);
            if (claimed == 0) throw new InvalidOperationException("Yuborish holati o'zgargan — sahifani yangilang.");

            var (sent, failed) = await FinishRunAsync(rule, run, null, ct);
            log.LogInformation("Avtomatik xabar ({Rule}, {Period}) tasdiqlandi: yuborildi={Sent}, xato={Failed}",
                rule.Key, run.Period, sent, failed);
            return new AutoMessageRunResult(rule.Key, run.Period, run.Pupils, sent, 0, failed, run.NoTelegram, true,
                run.Pending, run.Note, run.Id);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Stops a run that has not finished: its waiting messages become "To'xtatilgan".</summary>
    /// <returns>false — the run had already finished or been stopped.</returns>
    public async Task<bool> CancelAsync(Guid runId, string userId, string? why = null, CancellationToken ct = default)
    {
        var run = await db.AutoMessageRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new KeyNotFoundException("Yuborish topilmadi");
        return await CancelCoreAsync(run, userId, why ?? "Xodim to'xtatdi", ct);
    }

    /// <summary>Stops every unfinished run of a rule (the rule was switched off).</summary>
    public async Task<int> CancelOpenRunsAsync(string ruleKey, string userId, CancellationToken ct = default)
    {
        var runs = await db.AutoMessageRuns
            .Where(r => r.RuleKey == ruleKey && AutoMessageStatus.OpenRun.Contains(r.Status))
            .ToListAsync(ct);
        var n = 0;
        foreach (var run in runs)
            if (await CancelCoreAsync(run, userId, "Qoida o'chirildi", ct)) n++;
        return n;
    }

    /// <summary>Takes one pupil out of a run waiting for approval.</summary>
    /// <returns>Chats taken out; 0 — the pupil is not waiting in this run.</returns>
    public async Task<int> ExcludeAsync(Guid runId, string studentId, CancellationToken ct = default)
    {
        var run = await db.AutoMessageRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new KeyNotFoundException("Yuborish topilmadi");
        if (run.Status != AutoMessageStatus.PendingApproval)
            throw new InvalidOperationException("O'quvchini faqat tasdiq kutayotgan yuborishdan chiqarish mumkin.");
        var n = await db.AutoMessageLog
            .Where(l => l.RunId == run.Id && l.StudentId == studentId && l.Status == AutoMessageStatus.PendingApproval)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Status, AutoMessageStatus.Cancelled)
                .SetProperty(l => l.Error, "Ro'yxatdan chiqarildi")
                .SetProperty(l => l.At, AppClock.NowInstant), ct);
        await RefreshCountsAsync(run, ct);
        return n;
    }

    /// <summary>
    /// Cancels the run in one statement (only if it is still open) and its waiting rows — a row the
    /// sender has just delivered keeps its "sent".
    /// </summary>
    private async Task<bool> CancelCoreAsync(AutoMessageRun run, string by, string why, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var now = AppClock.NowInstant;
        var note = string.IsNullOrWhiteSpace(run.Note) ? why : Trim($"{run.Note} · {why}", 1000);
        var n = await db.AutoMessageRuns
            .Where(r => r.Id == run.Id && AutoMessageStatus.OpenRun.Contains(r.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, AutoMessageStatus.Cancelled)
                .SetProperty(r => r.CancelledBy, by)
                .SetProperty(r => r.CancelledAt, (DateTimeOffset?)now)
                .SetProperty(r => r.Note, note), ct);
        await db.AutoMessageLog
            .Where(l => l.RunId == run.Id && AutoMessageStatus.WaitingRow.Contains(l.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Status, AutoMessageStatus.Cancelled)
                .SetProperty(l => l.Error, why)
                .SetProperty(l => l.At, now), ct);
        await db.AutoMessageRuns.Entry(run).ReloadAsync(ct);
        await RefreshCountsAsync(run, ct);
        return n > 0;
    }

    /// <summary>Moves the run's status in one conditional statement; false — someone moved it first.</summary>
    private async Task<bool> MoveRunAsync(AutoMessageRun run, string[] from, string to, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var now = AppClock.NowInstant;
        var q = db.AutoMessageRuns.Where(r => r.Id == run.Id && from.Contains(r.Status));
        var n = to switch
        {
            AutoMessageStatus.Sending => await q.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, to)
                .SetProperty(r => r.StartedAt, r => r.StartedAt ?? now), ct),
            AutoMessageStatus.Done => await q.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, to)
                .SetProperty(r => r.FinishedAt, (DateTimeOffset?)now), ct),
            _ => await q.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to), ct),
        };
        await db.AutoMessageRuns.Entry(run).ReloadAsync(ct);
        return n > 0;
    }

    private Task<AutoMessageRun?> RunOfAsync(string key, string period, CancellationToken ct) =>
        db.AutoMessageRuns.FirstOrDefaultAsync(r => r.RuleKey == key && r.Period == period, ct);

    /// <summary>The run's counts, from its log rows.</summary>
    private async Task RefreshCountsAsync(AutoMessageRun run, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var byStatus = await db.AutoMessageLog.AsNoTracking().Where(l => l.RunId == run.Id)
            .GroupBy(l => l.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Of(string s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        run.Pupils = await db.AutoMessageLog.AsNoTracking().Where(l => l.RunId == run.Id)
            .Select(l => l.StudentId).Distinct().CountAsync(ct);
        run.Sent = Of(BroadcastDelivery.Sent);
        run.Failed = Of(BroadcastDelivery.Failed);
        run.NoTelegram = Of(BroadcastDelivery.NoTelegram);
        run.Pending = Of(AutoMessageStatus.Scheduled) + Of(AutoMessageStatus.PendingApproval);
        run.Cancelled = Of(AutoMessageStatus.Cancelled);
        await db.SaveChangesAsync(ct);
    }

    // =====================================================================
    //  Sources
    // =====================================================================

    /// <summary>The pupils a rule reaches for a period and the values of their tokens; null — source not ready.</summary>
    private async Task<Source?> ItemsAsync(AutoMessageRule rule, string period, DateTime localNow, CancellationToken ct)
    {
        var source = rule.Key switch
        {
            AutoMessageKind.Absence => await AbsenceItemsAsync(rule, DayOf(period), localNow, ct),
            AutoMessageKind.Birthday => new Source(await BirthdayItemsAsync(DayOf(period), ct)),
            AutoMessageKind.Invoice => await InvoiceItemsAsync(MonthOf(period), ct) is { } invoices ? new Source(invoices) : null,
            AutoMessageKind.Debt => new Source(await DebtItemsAsync(rule.MinAmount, ct)),
            var key when AutoMessageKind.IsCustom(key) => new Source(await AudienceItemsAsync(rule.ClassIds, ct)),
            _ => new Source([]),
        };
        if (source is null) return null;
        // {sana} and {vaqt} in every rule (2026-10-15): the run's day (daily) or today, and the time now.
        var day = period.Length == 10 ? DayOf(period) : DateOnly.FromDateTime(localNow);
        foreach (var item in source.Items)
        {
            item.Values.TryAdd("{sana}", day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            item.Values.TryAdd("{vaqt}", localNow.ToString("HH:mm", CultureInfo.InvariantCulture));
        }
        return source;
    }

    private sealed record Source(List<Item> Items, string? Note = null);

    /// <summary>One pupil and the values of the rule's tokens (plain text, escaped when rendered).</summary>
    private sealed record Item(string StudentId, Dictionary<string, string> Values, string? AbsenceReasonId = null);

    /// <summary>
    /// Pupils absent in every checked lesson that starts before the cut-off (default: the send time)
    /// and not told yet today. A class whose pre-cut-off lessons are not all marked is left out and
    /// named in the run's note — half-taken attendance must not tell a parent "did not come".
    /// </summary>
    private async Task<Source> AbsenceItemsAsync(AutoMessageRule rule, DateOnly day, DateTime localNow, CancellationToken ct)
    {
        var cutoffText = string.IsNullOrWhiteSpace(rule.CutoffTime) ? rule.SendTime : rule.CutoffTime;
        HashSet<int>? periods = null;
        string? note = null;
        if (TryTime(cutoffText, out var cutoff))
        {
            var times = await db.LessonTimes.AsNoTracking().ToListAsync(ct);
            if (times.Count > 0)
                periods = [.. times.Where(t => TryTime(t.StartTime, out var start) && start < cutoff).Select(t => t.Period)];
            else if (!string.IsNullOrWhiteSpace(rule.CutoffTime))
                note = "Dars vaqtlari kiritilmagan — kesish vaqti qo'llanmadi";
        }

        var notifier = new DailyAbsenceNotifier(db, telegram, NullLogger<DailyAbsenceNotifier>.Instance);
        var (rows, reasonIdOf) = await notifier.RowsAsync(day, ct, periods);
        // Told already today (the nurse's button, or an earlier run) — not again.
        var pending = rows.Where(r => r.SentAt is null).ToList();

        if (periods is not null)
        {
            var unfinished = await UnfinishedLessonsAsync(day, periods, ct);
            if (unfinished.Lessons.Count > 0)
            {
                var affected = await AffectedPupilsAsync([.. pending.Select(r => r.StudentId)], unfinished.Lessons, ct);
                pending = [.. pending.Where(r => !affected.Contains(r.StudentId))];
                var at = cutoff.ToString("HH:mm", CultureInfo.InvariantCulture);
                note = Trim($"Davomat {at} gacha to'liq olinmagan: {string.Join(", ", unfinished.Owners)}"
                            + $" — bu sinflardan {affected.Count} o'quvchiga xabar ketmadi", 1000);
            }
        }

        var names = await NamesAsync([.. pending.Select(r => r.StudentId)], ct);
        return new Source([.. pending.Select(r => new Item(r.StudentId, Values(names, r.StudentId, new()
        {
            ["{sinf}"] = r.RowName,
            ["{sana}"] = day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["{sabab}"] = r.ReasonName ?? "",
        }), reasonIdOf.GetValueOrDefault(r.StudentId)))], note);
    }

    /// <summary>The day's lessons before the cut-off that nobody marked, and the names of their classes.</summary>
    private async Task<(List<HeldLesson> Lessons, List<string> Owners)> UnfinishedLessonsAsync(
        DateOnly day, IReadOnlySet<int> periods, CancellationToken ct)
    {
        var iso = Iso(day);
        var lessons = (await HeldLessons.ForReportAsync(db, null, iso, iso, ct))
            .Where(l => periods.Contains(l.Period) && !l.Marked).ToList();
        if (lessons.Count == 0) return ([], []);

        var classNames = await db.Classes.AsNoTracking().Where(c => !c.IsArchived)
            .Select(c => new { c.Id, c.Name, c.Grade }).ToListAsync(ct);
        var groupNames = (await db.StudyGroups.AsNoTracking().Where(g => !g.IsArchived)
                .Select(g => new { g.Id, g.Name }).ToListAsync(ct))
            .ToDictionary(g => g.Id.ToString(), g => g.Name, StringComparer.Ordinal);
        var classById = classNames.ToDictionary(c => c.Id, c => c, StringComparer.Ordinal);
        lessons = [.. lessons.Where(l => classById.ContainsKey(l.ClassId) || groupNames.ContainsKey(l.ClassId))];
        var owners = lessons.Select(l => l.ClassId).Distinct(StringComparer.Ordinal)
            .Select(id => classById.TryGetValue(id, out var c) ? (Grade: c.Grade, Name: c.Name) : (Grade: 99, Name: groupNames[id]))
            .OrderBy(o => o.Grade).ThenBy(o => o.Name, StringComparer.Ordinal)
            .Select(o => o.Name).ToList();
        return (lessons, owners);
    }

    /// <summary>Of <paramref name="studentIds"/>, those who sit in one of the unmarked lessons.</summary>
    private async Task<HashSet<string>> AffectedPupilsAsync(List<string> studentIds, List<HeldLesson> lessons, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (studentIds.Count == 0) return result;
        var pupils = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ClassName, s.SubGroup }).ToListAsync(ct);
        var classIdByName = (await db.Classes.AsNoTracking().Where(c => !c.IsArchived)
                .Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var groupsOf = (await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.LeftOn == null && studentIds.Contains(m.StudentId))
                .Select(m => new { m.StudentId, m.GroupId }).ToListAsync(ct))
            .GroupBy(m => m.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.GroupId.ToString()).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (var p in pupils)
        {
            var classId = classIdByName.GetValueOrDefault(p.ClassName);
            var groups = groupsOf.GetValueOrDefault(p.Id);
            if (lessons.Any(l => (l.ClassId == classId && (l.SubGroup == 0 || l.SubGroup == p.SubGroup))
                                 || (groups?.Contains(l.ClassId) ?? false)))
                result.Add(p.Id);
        }
        return result;
    }

    private async Task<List<Item>> BirthdayItemsAsync(DateOnly day, CancellationToken ct)
    {
        // birth_date is "YYYY-MM-DD" text; 29 February is greeted on 28 February in other years.
        var suffix = day.ToString("-MM-dd", CultureInfo.InvariantCulture);
        var leapSuffix = day is { Month: 2, Day: 28 } && !DateTime.IsLeapYear(day.Year) ? "-02-29" : suffix;
        var ids = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && (s.BirthDate.EndsWith(suffix) || s.BirthDate.EndsWith(leapSuffix)))
            .OrderBy(s => s.FullName)
            .Select(s => s.Id).ToListAsync(ct);
        var names = await NamesAsync(ids, ct);
        return [.. ids.Select(id => new Item(id, Values(names, id, new())))];
    }

    private async Task<List<Item>?> InvoiceItemsAsync(DateOnly month, CancellationToken ct)
    {
        var due = await (from i in db.Invoices.AsNoTracking()
                         join s in db.Students.AsNoTracking() on i.StudentId equals s.Id
                         where i.PeriodMonth == month && i.Status != InvoiceStatus.Void && !s.IsArchived
                         group i by i.StudentId into g
                         select new { StudentId = g.Key, Amount = g.Sum(x => x.Amount - x.Discount) })
            .ToListAsync(ct);
        // No invoice for the month yet — the accrual has not run; the period stays open.
        if (due.Count == 0) return null;
        due = [.. due.Where(d => d.Amount > 0m)];

        var ids = due.Select(d => d.StudentId).ToList();
        var debts = (await new FinanceReportQueries(db).DebtorsAsync(
                new DebtorReportQuery(IncludeArchived: false, StudentIds: ids), ct))
            .ToDictionary(d => d.StudentId, d => d.Debt, StringComparer.Ordinal);
        var names = await NamesAsync(ids, ct);
        var monthName = MonthNames[month.Month - 1];
        return [.. due.Select(d => new Item(d.StudentId, Values(names, d.StudentId, new()
        {
            ["{oy}"] = monthName,
            ["{summa}"] = Money(d.Amount),
            ["{qarzdorlik}"] = Money(debts.GetValueOrDefault(d.StudentId)),
        })))];
    }

    private async Task<List<Item>> DebtItemsAsync(decimal minAmount, CancellationToken ct)
    {
        // The "Qarzdorlar" report's own number, and only pupils with something already overdue:
        // the month that is not due yet is not a debt to remind about.
        var reports = new FinanceReportQueries(db);
        var debtors = await reports.DebtorsAsync(
            new DebtorReportQuery(MinDebt: Math.Max(minAmount, 0.01m), OnlyOverdue: true, IncludeArchived: false), ct);
        var ids = debtors.Select(d => d.StudentId).ToList();
        var months = await reports.UnpaidMonthsAsync(ids, ct);
        var names = await NamesAsync(ids, ct);
        return [.. debtors.Select(d => new Item(d.StudentId, Values(names, d.StudentId, new()
        {
            ["{qarzdorlik}"] = Money(d.Debt),
            ["{oylar}"] = string.Join(", ", months.GetValueOrDefault(d.StudentId, [])
                .OrderBy(m => m.Month).Select(m => MonthNames[m.Month.Month - 1])),
        })))];
    }

    /// <summary>
    /// A template's pupils (2026-10-26): every pupil who is not archived, or only those in the chosen
    /// classes. A chosen class that no longer exists reaches nobody — never "everyone".
    /// </summary>
    private async Task<List<Item>> AudienceItemsAsync(List<string> classIds, CancellationToken ct)
    {
        var pupils = db.Students.AsNoTracking().Where(s => !s.IsArchived);
        if (classIds.Count > 0)
        {
            var classNames = await db.Classes.AsNoTracking()
                .Where(c => classIds.Contains(c.Id) && !c.IsArchived)
                .Select(c => c.Name).ToListAsync(ct);
            pupils = pupils.Where(s => s.ClassName != null && classNames.Contains(s.ClassName));
        }
        var ids = await pupils.OrderBy(s => s.ClassName).ThenBy(s => s.FullName).Select(s => s.Id).ToListAsync(ct);
        var names = await NamesAsync(ids, ct);
        return [.. ids.Select(id => new Item(id, Values(names, id, new())))];
    }

    private sealed record Names(string FullName, string FirstName, string ClassName);

    private async Task<Dictionary<string, Names>> NamesAsync(List<string> ids, CancellationToken ct) =>
        (await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.FirstName, s.ClassName }).ToListAsync(ct))
        .ToDictionary(s => s.Id, s => new Names(s.FullName, FirstNameOf(s.FullName, s.FirstName), s.ClassName ?? ""),
            StringComparer.Ordinal);

    /// <summary>The pupil's own name: the first-name field, else the second word of "Surname Name Patronymic".</summary>
    internal static string FirstNameOf(string fullName, string? firstName)
    {
        if (!string.IsNullOrWhiteSpace(firstName)) return firstName.Trim();
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : fullName.Trim();
    }

    private static Dictionary<string, string> Values(
        Dictionary<string, Names> names, string id, Dictionary<string, string> extra)
    {
        var n = names.GetValueOrDefault(id) ?? new Names("", "", "");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{fish}"] = n.FullName, ["{ism}"] = n.FirstName, ["{sinf}"] = n.ClassName,
        };
        foreach (var (k, v) in extra) values[k] = v;
        return values;
    }

    // =====================================================================
    //  Preparing and sending
    // =====================================================================

    /// <summary>
    /// Writes the run's log rows: one per pupil and parent chat, as <paramref name="rowStatus"/>
    /// (scheduled or pending approval), with the rendered text. A chat already sent this period is
    /// counted as such and left alone; a failed one of this run with attempts left is queued again;
    /// a pupil taken out of the run stays out.
    /// </summary>
    private async Task<(int Already, int NoTelegram)> MaterializeAsync(
        AutoMessageRule rule, AutoMessageRun run, List<Item> items, string rowStatus, CancellationToken ct)
    {
        var ids = items.Select(i => i.StudentId).ToList();
        var regs = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId) && r.TeacherId == null)
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.DistinctBy(r => r.ChatId).ToList(), StringComparer.Ordinal);
        var logged = await db.AutoMessageLog
            .Where(l => l.RuleKey == rule.Key && l.Period == run.Period && ids.Contains(l.StudentId))
            .ToListAsync(ct);
        var now = AppClock.NowInstant;

        int already = 0, noTelegram = 0;
        foreach (var item in items)
        {
            var chats = regs.GetValueOrDefault(item.StudentId) ?? [];
            if (chats.Count == 0)
            {
                noTelegram++;
                if (!logged.Any(l => l.StudentId == item.StudentId && l.ChatId == null))
                    db.AutoMessageLog.Add(new AutoMessageLogEntry
                    {
                        RuleKey = rule.Key, Period = run.Period, StudentId = item.StudentId, ChatId = null,
                        RunId = run.Id, Status = BroadcastDelivery.NoTelegram, PlannedAt = run.PlannedAt, At = now,
                    });
                continue;
            }

            foreach (var reg in chats)
            {
                var row = logged.FirstOrDefault(l => l.StudentId == item.StudentId && l.ChatId == reg.ChatId);
                if (row?.Status == BroadcastDelivery.Sent) { already++; continue; }
                if (row is not null && row.RunId == run.Id
                    && (row.Status == AutoMessageStatus.Cancelled
                        || (row.Status == BroadcastDelivery.Failed && row.Attempts >= MaxAttempts)))
                    continue;

                var values = new Dictionary<string, string>(item.Values, StringComparer.OrdinalIgnoreCase)
                {
                    ["{ota_ona}"] = string.IsNullOrWhiteSpace(reg.ParentName) ? "Ota-ona" : reg.ParentName,
                };
                if (row is null)
                {
                    row = new AutoMessageLogEntry
                    {
                        RuleKey = rule.Key, Period = run.Period, StudentId = item.StudentId, ChatId = reg.ChatId,
                    };
                    db.AutoMessageLog.Add(row);
                    logged.Add(row);
                }
                // Also takes over a failure of the nurse's button or of an earlier run of the period.
                row.RunId = run.Id;
                row.Source = AutoMessageSource.Auto;
                row.SentBy = null;
                row.Status = rowStatus;
                row.Text = Render(rule.Text, values);
                row.Error = null;
                row.PlannedAt = run.PlannedAt;
                row.At = now;
            }
        }
        await db.SaveChangesAsync(ct);
        return (already, noTelegram);
    }

    /// <summary>
    /// Sends the run's rows in <paramref name="statuses"/> pupil by pupil. Before each pupil the run's
    /// status is read again: cancelled — stop. Saved pupil by pupil, so a crash half-way loses nothing.
    /// </summary>
    private async Task<(int Sent, int Failed)> SendRowsAsync(
        AutoMessageRule rule, AutoMessageRun run, string[] statuses, IReadOnlyDictionary<string, string?>? reasonOf,
        CancellationToken ct)
    {
        var retry = statuses.Contains(BroadcastDelivery.Failed);
        var rows = await db.AutoMessageLog
            .Where(l => l.RunId == run.Id && l.ChatId != null && statuses.Contains(l.Status)
                        && (!retry || l.Attempts < MaxAttempts))
            .OrderBy(l => l.StudentId).ThenBy(l => l.ChatId)
            .ToListAsync(ct);
        var isAbsence = rule.Key == AutoMessageKind.Absence;
        var day = isAbsence ? DayOf(run.Period) : default;
        // Read once per send: the first chat uploads the files, every next one gets Telegram's file_id.
        var photos = rows.Count > 0 ? await PhotosAsync(run, ct) : [];

        int sent = 0, failed = 0;
        foreach (var pupil in rows.GroupBy(r => r.StudentId, StringComparer.Ordinal))
        {
            var status = await db.AutoMessageRuns.AsNoTracking().Where(r => r.Id == run.Id)
                .Select(r => r.Status).FirstOrDefaultAsync(ct);
            if (status is null or AutoMessageStatus.Cancelled) break;

            AbsenceNotice? notice = null;
            if (isAbsence)
            {
                notice = await db.AbsenceNotices.AsNoTracking()
                    .FirstOrDefaultAsync(n => n.StudentId == pupil.Key && n.Date == day, ct);
                // The nurse's button told this pupil's parents in the meantime — not twice.
                if (notice is not null && notice.SentBy != SystemUser)
                {
                    foreach (var row in pupil)
                    {
                        row.Status = AutoMessageStatus.Cancelled;
                        row.Error = "Bu kun uchun xabar qo'lda yuborilgan";
                        row.At = AppClock.NowInstant;
                    }
                    await db.SaveChangesAsync(ct);
                    continue;
                }
            }

            var delivered = 0;
            foreach (var row in pupil)
            {
                var ok = photos.Count == 0
                    ? await telegram.SendRichAsync(row.ChatId!.Value, row.Text, ct)
                    : await telegram.SendPhotosWithTextAsync(row.ChatId!.Value, photos, row.Text, ct);
                row.Attempts++;
                row.Status = ok ? BroadcastDelivery.Sent : BroadcastDelivery.Failed;
                row.Error = ok ? null : RejectedByTelegram;
                row.At = AppClock.NowInstant;
                if (ok) { sent++; delivered++; } else failed++;
                if (SendPause > TimeSpan.Zero) await Task.Delay(SendPause, ct);
            }

            // The nurse's button reads absence_notices: a pupil the rule told is not told again by hand.
            if (isAbsence && delivered > 0 && notice is null)
                db.AbsenceNotices.Add(new AbsenceNotice
                {
                    StudentId = pupil.Key, Date = day,
                    ReasonId = reasonOf is not null && reasonOf.TryGetValue(pupil.Key, out var reasonId)
                        ? reasonId
                        : await MainReasonAsync(pupil.Key, day, ct),
                    Chats = delivered, SentBy = SystemUser, SentAt = AppClock.NowInstant,
                });
            await db.SaveChangesAsync(ct);
        }
        return (sent, failed);
    }

    /// <summary>
    /// The run's pictures. One that can no longer be read (deleted from <c>uploads/</c>) does not hold
    /// the message back: the text goes alone and the reason is logged.
    /// </summary>
    private async Task<List<TelegramPhoto>> PhotosAsync(AutoMessageRun run, CancellationToken ct)
    {
        if (run.ImageUrls.Count == 0) return [];
        if (env is null)
        {
            log.LogWarning("Avtomatik xabar ({Rule}, {Period}): rasmlar o'qilmadi — faqat matn ketadi", run.RuleKey, run.Period);
            return [];
        }
        var (photos, error) = await BroadcastImages.LoadAsync(env, run.ImageUrls, ct);
        if (error is null) return photos;
        log.LogWarning("Avtomatik xabar ({Rule}, {Period}): {Error} — faqat matn ketadi", run.RuleKey, run.Period, error);
        return [];
    }

    /// <summary>The reason most of the pupil's absent lessons that day carry (late reasons aside).</summary>
    private async Task<string?> MainReasonAsync(string studentId, DateOnly day, CancellationToken ct)
    {
        var iso = Iso(day);
        var late = await db.AbsenceReasons.AsNoTracking().Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct);
        var reasons = await db.JournalEntries.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.Date == iso && e.ReasonId != null && !late.Contains(e.ReasonId))
            .Select(e => e.ReasonId!).ToListAsync(ct);
        return reasons.GroupBy(r => r, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key).FirstOrDefault();
    }

    /// <summary>
    /// The rule's markup as Telegram HTML with the tokens filled (values escaped). A line whose
    /// token has no value is left out — "Sabab: {sabab}" disappears when no reason was given.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var lines = (template ?? "").Replace("\r\n", "\n").Split('\n')
            .Where(line => !TokenRegex().Matches(line).Any(m =>
                values.TryGetValue(m.Value, out var v) && string.IsNullOrWhiteSpace(v)));
        var html = TelegramHtml.FromMarkup(string.Join('\n', lines));
        return TokenRegex().Replace(html, m =>
            values.TryGetValue(m.Value, out var v) ? TelegramHtml.Escape(v) : m.Value);
    }

    private static Regex TokenRegex() => TokenPattern;
    private static readonly Regex TokenPattern = new(@"\{[a-z_\-]+\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // =====================================================================
    //  Preview
    // =====================================================================

    /// <summary>Who the rule would reach if it ran now, and the text the first of them would get.</summary>
    public async Task<AutoMessagePreviewDto> PreviewAsync(AutoMessageRule rule, string? text, DateTime localNow, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(localNow);
        var period = AutoMessageKind.IsMonthly(rule) ? today.ToString("yyyy-MM", CultureInfo.InvariantCulture) : Iso(today);
        var items = (await ItemsAsync(rule, period, localNow, ct))?.Items ?? [];
        var ids = items.Select(i => i.StudentId).ToList();
        var withChat = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId) && r.TeacherId == null)
                .Select(r => r.StudentId).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var alreadySent = await db.AutoMessageLog.AsNoTracking()
            .Where(l => l.RuleKey == rule.Key && l.Period == period && l.Status == BroadcastDelivery.Sent && ids.Contains(l.StudentId))
            .Select(l => l.StudentId).Distinct().CountAsync(ct);

        var first = items.FirstOrDefault();
        string? sample = null, sampleFor = null;
        if (first is not null)
        {
            var values = new Dictionary<string, string>(first.Values, StringComparer.OrdinalIgnoreCase) { ["{ota_ona}"] = "Ota-ona" };
            sample = Render(text ?? rule.Text, values);
            sampleFor = first.Values.GetValueOrDefault("{fish}");
        }
        return new AutoMessagePreviewDto(rule.Key, period, items.Count, withChat.Count, alreadySent, sample, sampleFor);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly DayOf(string period) =>
        DateOnly.ParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly MonthOf(string period) =>
        DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly DueDay(AutoMessageRule rule, DateOnly inMonth) =>
        new(inMonth.Year, inMonth.Month, Math.Clamp(rule.DayOfMonth, 1, 28));

    /// <summary>The planned local time of a run for <paramref name="period"/>.</summary>
    private static DateTime PlannedLocalOf(AutoMessageRule rule, string period)
    {
        var at = TryTime(rule.SendTime, out var t) ? t : TimeOnly.MinValue;
        return period.Length == 10 ? DayOf(period).ToDateTime(at) : DueDay(rule, MonthOf(period)).ToDateTime(at);
    }

    /// <summary>"HH:mm" (or "H:mm").</summary>
    internal static bool TryTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact((text ?? "").Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>A school-time wall clock as an instant (UTC, as Npgsql wants it).</summary>
    public static DateTimeOffset ToInstant(DateTime local)
    {
        var guess = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.Zero);
        var offset = AppClock.ToLocal(guess) - guess.UtcDateTime;
        return guess - offset;
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>1 500 000 — spaces between thousands, no decimals.</summary>
    public static string Money(decimal amount) =>
        decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
}
