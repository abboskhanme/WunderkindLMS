using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Hr;

// ===========================================================================
//  A fine reaches the employee on Telegram (client, 2026-10-07: "qo'yilgan jarima ustozga ...
//  xabar bo'lib borishi"). Written and cancelled fines both send a message; a bonus does not.
//
//  Telegram is the only channel (CLAUDE.md). Recipients are the two links a person can have, merged
//  without duplicates — the same as NewsTelegramNotifier:
//    * telegram_registrations.teacher_id — the teacher shared their phone with the bot;
//    * telegram_accounts.user_id — the account signed in to the Mini App.
//  The HR task switches (hr_settings) do not apply: they gate the HR schedule, not this.
//
//  Never throws and never blocks the fine: TelegramService swallows transport errors, and a
//  person with no linked chat simply gets nothing (the fine is in their cabinet either way).
// ===========================================================================

/// <summary>Telegram message about a fine to the employee it was written for.</summary>
public sealed class PenaltyNotifier(IAppDbContext db, TelegramService telegram, ILogger<PenaltyNotifier> logger)
{
    private static readonly string[] Months =
    [
        "yanvar", "fevral", "mart", "aprel", "may", "iyun",
        "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr",
    ];

    /// <summary>A fine was written. Returns the number of chats it reached.</summary>
    public Task<int> NotifyCreatedAsync(PayrollAdjustmentDto fine, CancellationToken ct = default) =>
        SendAsync(fine, CreatedText(fine), ct);

    /// <summary>A fine was cancelled (<paramref name="mirror"/> is the reversal row).</summary>
    public Task<int> NotifyCancelledAsync(PayrollAdjustmentDto mirror, string? reason, CancellationToken ct = default) =>
        SendAsync(mirror, CancelledText(mirror, reason), ct);

    private async Task<int> SendAsync(PayrollAdjustmentDto row, string text, CancellationToken ct)
    {
        if (row.Kind != AdjustmentKind.Penalty || !telegram.IsConfigured) return 0;
        try
        {
            var chats = await RecipientsAsync(db, row.EmployeeKind, row.EmployeeId, ct);
            var sent = 0;
            foreach (var chat in chats)
                if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
            logger.LogInformation("Jarima xabari: adjustment={Id}, chats={Total}, sent={Sent}", row.Id, chats.Count, sent);
            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Jarima xabari yuborilmadi: adjustment={Id}", row.Id);
            return 0;
        }
    }

    /// <summary>The employee's chats: bot registration (teachers) and Mini App account, no duplicates.</summary>
    public static async Task<IReadOnlyCollection<long>> RecipientsAsync(
        IAppDbContext db, string employeeKind, string employeeId, CancellationToken ct = default)
    {
        var chats = new HashSet<long>();
        var userId = employeeKind == EmployeeKind.Staff ? employeeId : null;
        if (employeeKind == EmployeeKind.Teacher)
        {
            chats.UnionWith(await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.TeacherId == employeeId)
                .Select(r => r.ChatId)
                .ToListAsync(ct));
            userId = await db.Teachers.AsNoTracking()
                .Where(t => t.Id == employeeId)
                .Select(t => t.UserId)
                .FirstOrDefaultAsync(ct);
        }
        if (!string.IsNullOrEmpty(userId))
        {
            chats.UnionWith(await db.TelegramAccounts.AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => a.TelegramUserId)
                .ToListAsync(ct));
        }
        chats.Remove(0);
        return chats;
    }

    public static string CreatedText(PayrollAdjustmentDto fine)
    {
        var lines = new List<string>
        {
            "⚠️ Sizga jarima yozildi",
            "",
            $"Sabab: {fine.ReasonName}",
            $"Summa: {AuditService.Money(fine.Amount)} so'm",
            $"Oy: {Period(fine.PeriodYear, fine.PeriodMonth)}",
        };
        if (!string.IsNullOrWhiteSpace(fine.Comment)) lines.Add($"Izoh: {fine.Comment.Trim()}");
        lines.Add("");
        lines.Add($"Batafsil: «{TelegramPanelKeys.ButtonText}» → Bugun → Jarimalar.");
        return string.Join('\n', lines);
    }

    public static string CancelledText(PayrollAdjustmentDto mirror, string? reason)
    {
        var lines = new List<string>
        {
            "✅ Jarima bekor qilindi",
            "",
            $"Sabab: {mirror.ReasonName}",
            $"Summa: {AuditService.Money(mirror.Amount)} so'm",
            $"Oy: {Period(mirror.PeriodYear, mirror.PeriodMonth)}",
        };
        if (!string.IsNullOrWhiteSpace(reason)) lines.Add($"Bekor qilish sababi: {reason.Trim()}");
        return string.Join('\n', lines);
    }

    private static string Period(short year, short month) =>
        month is >= 1 and <= 12 ? $"{Months[month - 1]} {year}" : $"{year}-{month:D2}";
}
