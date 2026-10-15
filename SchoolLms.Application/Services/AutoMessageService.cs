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
//  Four rules — absence, invoice, debt, birthday — each switched on separately once the school
//  approves its text. The scheduler (AutoMessageScheduler) calls RunDueAsync every couple of
//  minutes; a rule runs once per period (a day, or a month for invoice and debt), and the log
//  (auto_message_log) makes sure a parent chat gets one message per rule and period even if the
//  rule is run again.
// ===========================================================================

/// <summary>One rule's run.</summary>
/// <param name="Closed">false — the source was not ready (no invoices yet); the period stays open.</param>
public sealed record AutoMessageRunResult(
    string Key, string Period, int Pupils, int Sent, int AlreadySent, int Failed, int NoTelegram, bool Closed);

/// <summary>What a rule would do now, without sending anything.</summary>
public sealed record AutoMessagePreviewDto(
    string Key, string Period, int Pupils, int WithTelegram, int AlreadySent, string? Sample, string? SampleFor);

/// <summary>A token the rule's text may use, with what it becomes.</summary>
public sealed record AutoMessageTokenDto(string Token, string Meaning);

public sealed class AutoMessageService(IAppDbContext db, TelegramService telegram, ILogger<AutoMessageService>? logger = null)
{
    /// <summary><c>absence_notices.sent_by</c> for a notice the absence rule sent.</summary>
    public const string SystemUser = "auto";

    private readonly ILogger log = logger ?? NullLogger<AutoMessageService>.Instance;

    // =====================================================================
    //  Texts and tokens
    // =====================================================================

    /// <summary>The text each rule ships with (the migration seeds the same).</summary>
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
    };

    private static readonly AutoMessageTokenDto[] Common =
    [
        new("{fish}", "O'quvchining F.I.Sh."),
        new("{ism}", "O'quvchining ismi"),
        new("{sinf}", "Sinfi"),
        new("{ota_ona}", "Botga ulangan ota-onaning ismi"),
    ];

    public static IReadOnlyList<AutoMessageTokenDto> TokensOf(string key) => key switch
    {
        AutoMessageKind.Absence => [.. Common, new("{sana}", "Kun (13.10.2026)"), new("{sabab}", "Yo'qlik sababi (bo'lmasa — qator tushib qoladi)")],
        AutoMessageKind.Invoice => [.. Common, new("{oy}", "Oy nomi (Oktabr)"), new("{summa}", "Shu oy hisobi, so'm"), new("{qarzdorlik}", "Jami to'lanmagan, so'm")],
        AutoMessageKind.Debt => [.. Common, new("{qarzdorlik}", "Jami qarz, so'm"), new("{oylar}", "To'lanmagan oylar (Sentabr, Oktabr)")],
        _ => Common,
    };

    private static readonly string[] MonthNames =
    [
        "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
        "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr",
    ];

    // =====================================================================
    //  Scheduling
    // =====================================================================

    /// <summary>Runs every switched-on rule that is due at <paramref name="localNow"/> (school time).</summary>
    public async Task<List<AutoMessageRunResult>> RunDueAsync(DateTime localNow, CancellationToken ct = default)
    {
        var results = new List<AutoMessageRunResult>();
        if (!telegram.IsConfigured) return results;
        var rules = await db.AutoMessageRules.Where(r => r.Enabled).OrderBy(r => r.Key).ToListAsync(ct);
        foreach (var rule in rules)
        {
            var period = DuePeriod(rule, localNow);
            if (period is null || rule.LastPeriod == period) continue;
            try
            {
                results.Add(await RunAsync(rule, period, ct));
            }
            catch (Exception ex)
            {
                // One rule failing must not stop the others; it is tried again on the next tick.
                log.LogError(ex, "Avtomatik xabar ({Rule}, {Period}) xato bilan to'xtadi", rule.Key, period);
            }
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
        if (!TimeOnly.TryParseExact(rule.SendTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            return null;
        var today = DateOnly.FromDateTime(localNow);
        var timeReached = TimeOnly.FromDateTime(localNow) >= at;
        if (!AutoMessageKind.IsMonthly(rule.Key))
            return timeReached ? Iso(today) : null;

        var due = new DateOnly(today.Year, today.Month, Math.Clamp(rule.DayOfMonth, 1, 28));
        var inWindow = (today == due && timeReached) || (today > due && today <= due.AddDays(2));
        return inWindow ? today.ToString("yyyy-MM", CultureInfo.InvariantCulture) : null;
    }

    // =====================================================================
    //  One run
    // =====================================================================

    /// <summary>Sends the rule's messages for <paramref name="period"/> and closes it.</summary>
    public async Task<AutoMessageRunResult> RunAsync(AutoMessageRule rule, string period, CancellationToken ct = default)
    {
        var items = await ItemsAsync(rule, period, ct);
        if (items is null)
        {
            log.LogInformation("Avtomatik xabar ({Rule}, {Period}): manba hali tayyor emas", rule.Key, period);
            return new AutoMessageRunResult(rule.Key, period, 0, 0, 0, 0, 0, Closed: false);
        }

        var result = await SendAsync(rule, period, items, ct);
        rule.LastPeriod = period;
        rule.LastRunAt = AppClock.NowInstant;
        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Avtomatik xabar ({Rule}, {Period}): o'quvchi={Pupils}, yuborildi={Sent}, oldin={Already}, xato={Failed}, ulanmagan={NoTelegram}",
            rule.Key, period, result.Pupils, result.Sent, result.AlreadySent, result.Failed, result.NoTelegram);
        return result;
    }

    /// <summary>The pupils a rule reaches for a period and the values of their tokens; null — source not ready.</summary>
    private Task<List<Item>?> ItemsAsync(AutoMessageRule rule, string period, CancellationToken ct) => rule.Key switch
    {
        AutoMessageKind.Absence => AbsenceItemsAsync(DateOnly.ParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture), ct),
        AutoMessageKind.Birthday => BirthdayItemsAsync(DateOnly.ParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture), ct),
        AutoMessageKind.Invoice => InvoiceItemsAsync(MonthOf(period), ct),
        AutoMessageKind.Debt => DebtItemsAsync(rule.MinAmount, ct),
        _ => Task.FromResult<List<Item>?>([]),
    };

    /// <summary>One pupil and the values of the rule's tokens (plain text, escaped when rendered).</summary>
    private sealed record Item(string StudentId, Dictionary<string, string> Values, string? AbsenceReasonId = null);

    private async Task<List<Item>?> AbsenceItemsAsync(DateOnly day, CancellationToken ct)
    {
        var notifier = new DailyAbsenceNotifier(db, telegram, NullLogger<DailyAbsenceNotifier>.Instance);
        var (rows, reasonIdOf) = await notifier.RowsAsync(day, ct);
        // Told already today (the nurse's button, or an earlier run) — not again.
        var pending = rows.Where(r => r.SentAt is null).ToList();
        var names = await NamesAsync([.. pending.Select(r => r.StudentId)], ct);
        return [.. pending.Select(r => new Item(r.StudentId, Values(names, r.StudentId, new()
        {
            ["{sinf}"] = r.RowName,
            ["{sana}"] = day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["{sabab}"] = r.ReasonName ?? "",
        }), reasonIdOf.GetValueOrDefault(r.StudentId)))];
    }

    private async Task<List<Item>?> BirthdayItemsAsync(DateOnly day, CancellationToken ct)
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

    private async Task<List<Item>?> DebtItemsAsync(decimal minAmount, CancellationToken ct)
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

    private sealed record Names(string FullName, string FirstName, string ClassName);

    private async Task<Dictionary<string, Names>> NamesAsync(List<string> ids, CancellationToken ct) =>
        (await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.FirstName, s.ClassName }).ToListAsync(ct))
        .ToDictionary(s => s.Id, s => new Names(s.FullName, FirstNameOf(s.FullName, s.FirstName), s.ClassName ?? ""),
            StringComparer.Ordinal);

    /// <summary>The pupil's own name: the first-name field, else the second word of "Surname Name Patronymic".</summary>
    private static string FirstNameOf(string fullName, string? firstName)
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
    //  Sending
    // =====================================================================

    private async Task<AutoMessageRunResult> SendAsync(AutoMessageRule rule, string period, List<Item> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.StudentId).ToList();
        var regs = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId) && r.TeacherId == null)
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.DistinctBy(r => r.ChatId).ToList(), StringComparer.Ordinal);
        var logged = await db.AutoMessageLog
            .Where(l => l.RuleKey == rule.Key && l.Period == period && ids.Contains(l.StudentId))
            .ToListAsync(ct);

        int sent = 0, already = 0, failed = 0, noTelegram = 0;
        foreach (var item in items)
        {
            var chats = regs.GetValueOrDefault(item.StudentId) ?? [];
            if (chats.Count == 0)
            {
                noTelegram++;
                if (!logged.Any(l => l.StudentId == item.StudentId && l.ChatId == null))
                    db.AutoMessageLog.Add(new AutoMessageLogEntry
                    {
                        RuleKey = rule.Key, Period = period, StudentId = item.StudentId, ChatId = null,
                        Status = BroadcastDelivery.NoTelegram, At = AppClock.NowInstant,
                    });
                await db.SaveChangesAsync(ct);
                continue;
            }

            var delivered = 0;
            foreach (var reg in chats)
            {
                var row = logged.FirstOrDefault(l => l.StudentId == item.StudentId && l.ChatId == reg.ChatId);
                if (row?.Status == BroadcastDelivery.Sent) { already++; delivered++; continue; }

                var values = new Dictionary<string, string>(item.Values, StringComparer.OrdinalIgnoreCase)
                {
                    ["{ota_ona}"] = string.IsNullOrWhiteSpace(reg.ParentName) ? "Ota-ona" : reg.ParentName,
                };
                var text = Render(rule.Text, values);
                var ok = await telegram.SendRichAsync(reg.ChatId, text, ct);
                if (ok) { sent++; delivered++; } else failed++;

                if (row is null)
                {
                    row = new AutoMessageLogEntry
                    {
                        RuleKey = rule.Key, Period = period, StudentId = item.StudentId, ChatId = reg.ChatId,
                    };
                    db.AutoMessageLog.Add(row);
                    logged.Add(row);
                }
                row.Status = ok ? BroadcastDelivery.Sent : BroadcastDelivery.Failed;
                row.Text = text;
                row.At = AppClock.NowInstant;
            }

            // The nurse's button reads absence_notices: a pupil the rule told is not told again by hand.
            if (rule.Key == AutoMessageKind.Absence && delivered > 0)
            {
                var day = DateOnly.ParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!await db.AbsenceNotices.AnyAsync(n => n.StudentId == item.StudentId && n.Date == day, ct))
                    db.AbsenceNotices.Add(new AbsenceNotice
                    {
                        StudentId = item.StudentId, Date = day, ReasonId = item.AbsenceReasonId,
                        Chats = delivered, SentBy = SystemUser, SentAt = AppClock.NowInstant,
                    });
            }
            // Saved pupil by pupil: a crash half-way must not lose what already went out.
            await db.SaveChangesAsync(ct);
        }

        return new AutoMessageRunResult(rule.Key, period, items.Count, sent, already, failed, noTelegram, Closed: true);
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
        var period = AutoMessageKind.IsMonthly(rule.Key) ? today.ToString("yyyy-MM", CultureInfo.InvariantCulture) : Iso(today);
        var items = await ItemsAsync(rule, period, ct) ?? [];
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
    //  Log
    // =====================================================================

    /// <summary>What went out, newest first; days by the school's calendar.</summary>
    public async Task<List<Dtos.AutoMessageLogRowDto>> LogAsync(
        string? rule, DateOnly? from, DateOnly? to, string? status, int max, CancellationToken ct = default)
    {
        var q = db.AutoMessageLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(rule)) q = q.Where(l => l.RuleKey == rule);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status);
        // The cash journal's day window: a little wide in SQL, exact by the school's day below.
        if (CashBoxDateWindow.Lower(from) is { } lower) q = q.Where(l => l.At >= lower);
        if (CashBoxDateWindow.Upper(to) is { } upper) q = q.Where(l => l.At <= upper);

        var rows = (await (from l in q
                           join s in db.Students.AsNoTracking() on l.StudentId equals s.Id
                           orderby l.At descending
                           select new { l, s.FullName, s.ClassName })
                .Take(max * 2).ToListAsync(ct))
            .Where(r => CashBoxDateWindow.Covers(r.l.At, from, to))
            .Take(max).ToList();
        var chats = rows.Where(r => r.l.ChatId != null).Select(r => r.l.ChatId!.Value).Distinct().ToList();
        var parents = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => chats.Contains(r.ChatId) && r.TeacherId == null)
                .Select(r => new { r.ChatId, r.StudentId, r.ParentName }).ToListAsync(ct))
            .GroupBy(r => (r.ChatId, r.StudentId)).ToDictionary(g => g.Key, g => g.First().ParentName);

        return [.. rows.Select(r => new Dtos.AutoMessageLogRowDto(
            r.l.Id, r.l.RuleKey, r.l.Period, r.l.StudentId, r.FullName, r.ClassName ?? "", r.l.ChatId,
            r.l.ChatId is { } c ? parents.GetValueOrDefault((c, r.l.StudentId)) : null,
            r.l.Status, r.l.Text, r.l.At))];
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly MonthOf(string period) =>
        DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>1 500 000 — spaces between thousands, no decimals.</summary>
    public static string Money(decimal amount) =>
        decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
}
