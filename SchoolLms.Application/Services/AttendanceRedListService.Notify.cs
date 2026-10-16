using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Red-list notices on Telegram — staff only, never parents (docs/modules/attendance-red-list.md).
//
//  Telegram is the only channel (CLAUDE.md). Two messages, each behind its own switch:
//    · "new case" — to the homeroom teachers of the pupil's class and the teachers of the
//      pupil's track group, one message per teacher per scan listing their new cases;
//    · the daily digest — to staff holding the `attendanceCases` key, with the cases that need
//      management (could not reach the parents, call-back day passed, no action for N days).
//  A person's chats are the two links PenaltyNotifier uses: the bot registration (teachers) and
//  the Mini App account. Someone with no chat simply gets nothing — the case is on screen anyway.
//  Texts are neutral placeholders until the school approves its own.
// ===========================================================================

public sealed partial class AttendanceRedListService
{
    /// <summary>How many cases a digest lists by name.</summary>
    private const int DigestMaxLines = 15;

    /// <summary>Tells each teacher about their new cases; returns the chats reached.</summary>
    private async Task<int> NotifyTeachersAsync(
        IReadOnlyList<AttendanceCase> opened, AttendanceRedListSettings settings, CancellationToken ct)
    {
        if (opened.Count == 0 || !settings.NotifyTeachers || !telegram.IsConfigured) return 0;
        try
        {
            var ids = opened.Select(c => c.StudentId).ToList();
            var pupils = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.FullName, s.ClassName })
                .ToDictionaryAsync(s => s.Id, StringComparer.Ordinal, ct);
            var teachersOf = await TeachersOfPupilsAsync(ids, ct);
            var casesOf = opened
                .SelectMany(c => (teachersOf.GetValueOrDefault(c.StudentId) ?? []).Select(t => (TeacherId: t.Id, Case: c)))
                .GroupBy(x => x.TeacherId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Case).ToList(), StringComparer.Ordinal);
            var chats = await TeacherChatsAsync(casesOf.Keys.ToList(), ct);

            var sent = 0;
            foreach (var (teacherId, cases) in casesOf)
            {
                var lines = cases
                    .Select(c => (Pupil: pupils.GetValueOrDefault(c.StudentId), Case: c))
                    .Where(x => x.Pupil is not null)
                    .OrderBy(x => x.Pupil!.FullName, StringComparer.Ordinal)
                    .Select(x => $"• {x.Pupil!.FullName} ({x.Pupil.ClassName}) — "
                                 + ReasonText(x.Case.Trigger, x.Case.Streak, x.Case.WindowCount, settings.WindowDays,
                                     settings.CountExcused))
                    .ToList();
                if (lines.Count == 0) continue;
                var text = TeacherNotice(lines);
                var reached = false;
                foreach (var chat in chats.GetValueOrDefault(teacherId) ?? [])
                {
                    if (!await telegram.SendMessageAsync(chat, text, ct: ct)) continue;
                    sent++;
                    reached = true;
                }
                if (reached)
                    foreach (var c in cases) c.NotifiedAt ??= AppClock.NowInstant;
            }
            await db.SaveChangesAsync(ct);
            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cases are saved and on screen; a failed notice must not fail the scan.
            log.LogWarning(ex, "Qizil ro'yxat: o'qituvchilarga xabar yuborilmadi");
            return 0;
        }
    }

    /// <summary>The digest of cases needing management, to staff with the key; returns the chats reached.</summary>
    private async Task<int> SendDigestAsync(DateOnly today, AttendanceRedListSettings settings, CancellationToken ct)
    {
        if (!telegram.IsConfigured) return 0;
        try
        {
            var open = await (from c in db.AttendanceCases.AsNoTracking()
                              join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
                              where c.ClosedAt == null && !s.IsArchived
                              select new { Case = c, s.FullName, s.ClassName }).ToListAsync(ct);
            var overdue = open
                .Select(x => (x.FullName, x.ClassName, x.Case, Due: OverdueOf(x.Case, today, settings.EscalateAfterDays)))
                .Where(x => x.Due.Overdue)
                .OrderBy(x => x.Due.Reason == "unreachable" ? 0 : 1)
                .ThenByDescending(x => x.Due.DaysWithoutAction)
                .ThenBy(x => x.FullName, StringComparer.Ordinal)
                .ToList();
            if (overdue.Count == 0) return 0;

            var holders = await db.Users.AsNoTracking()
                .Where(u => u.Role == Roles.Staff && u.Permissions.Contains(PermissionKey))
                .Select(u => u.Id).ToListAsync(ct);
            var chats = await db.TelegramAccounts.AsNoTracking()
                .Where(a => holders.Contains(a.UserId) && a.TelegramUserId != 0)
                .Select(a => a.TelegramUserId).Distinct().ToListAsync(ct);
            if (chats.Count == 0) return 0;

            var lines = overdue.Take(DigestMaxLines).Select(x => $"• {x.FullName} ({x.ClassName}) — " + (x.Due.Reason switch
            {
                "unreachable" => "bog'lanib bo'lmadi",
                "callback_due" => $"qayta qo'ng'iroq kuni o'tdi ({x.Case.CallbackOn:dd.MM})",
                _ => x.Case.LastActionAt is null
                    ? $"{x.Due.DaysWithoutAction} kundan beri hech kim bog'lanmadi"
                    : $"{x.Due.DaysWithoutAction} kundan beri amal yo'q",
            })).ToList();
            var text = DigestText(open.Count, overdue.Count, lines);

            var sent = 0;
            foreach (var chat in chats)
                if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
            log.LogInformation("Qizil ro'yxat: rahbariyatga kunlik xabar — muddati o'tgan={Overdue}, chat={Sent}/{Total}",
                overdue.Count, sent, chats.Count);
            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Qizil ro'yxat: rahbariyatga kunlik xabar yuborilmadi");
            return 0;
        }
    }

    /// <summary>Teacher → chats: bot registrations and Mini App accounts, no duplicates (three queries for all).</summary>
    private async Task<Dictionary<string, HashSet<long>>> TeacherChatsAsync(List<string> teacherIds, CancellationToken ct)
    {
        var result = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        if (teacherIds.Count == 0) return result;
        void Add(string teacherId, long chat)
        {
            if (chat == 0) return;
            if (!result.TryGetValue(teacherId, out var set)) result[teacherId] = set = [];
            set.Add(chat);
        }

        foreach (var r in await db.TelegramRegistrations.AsNoTracking()
                     .Where(r => r.TeacherId != null && teacherIds.Contains(r.TeacherId))
                     .Select(r => new { r.TeacherId, r.ChatId }).ToListAsync(ct))
            Add(r.TeacherId!, r.ChatId);

        var users = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id) && t.UserId != null)
            .Select(t => new { t.Id, t.UserId }).ToListAsync(ct);
        var userIds = users.Select(u => u.UserId!).ToList();
        var accounts = (await db.TelegramAccounts.AsNoTracking()
                .Where(a => userIds.Contains(a.UserId))
                .Select(a => new { a.UserId, a.TelegramUserId }).ToListAsync(ct))
            .ToLookup(a => a.UserId, a => a.TelegramUserId, StringComparer.Ordinal);
        foreach (var u in users)
            foreach (var chat in accounts[u.UserId!])
                Add(u.Id, chat);
        return result;
    }

    /// <summary>The teacher's "new case" message (plain text).</summary>
    public static string TeacherNotice(IReadOnlyList<string> lines) =>
        string.Join('\n', new[] { "🔴 Qizil ro'yxat", "", "Ota-ona bilan bog'lanish kerak:" }
            .Concat(lines)
            .Concat(["", $"Natijani ilovada belgilang: «{TelegramPanelKeys.ButtonText}» → Bugun → Qizil ro'yxat."]));

    /// <summary>The management digest (plain text).</summary>
    public static string DigestText(int open, int overdue, IReadOnlyList<string> lines)
    {
        var text = new List<string>
        {
            "🔴 Qizil ro'yxat — e'tibor talab qiladi",
            "",
            $"Ochiq holatlar: {open.ToString(CultureInfo.InvariantCulture)}. Muddati o'tgan: {overdue.ToString(CultureInfo.InvariantCulture)}.",
        };
        text.AddRange(lines);
        if (overdue > lines.Count) text.Add($"… va yana {overdue - lines.Count} ta");
        text.Add("");
        text.Add("Batafsil: admin panel → Davomat → Qizil ro'yxat.");
        return string.Join('\n', text);
    }
}
