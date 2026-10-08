using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Several accounts on one Telegram (client, 2026-10-08: "bitta oila farzandlari uchun 2 xil login
//  parol bilan kirsa ham boshqa farzandini akkauntiga o'tishi oson bo'lishi kerak ... o'qituvchi va
//  ota-ona akkauntini ham almashtirish oson bo'lishi kerak").
//
//  A Telegram is signed in to ONE account at a time (`TelegramAccount.UserId`, unchanged). Next to it,
//  `telegram_saved_accounts` lists every account added on that Telegram — each once, with its OWN
//  login and password — so switching needs no password. Leaving an account drops it from the list;
//  an archived account drops out the moment it is met.
// ===========================================================================

public static class TelegramSavedAccounts
{
    /// <summary>Adds the account to the Telegram's list, or marks it as just used.</summary>
    public static async Task RememberAsync(IAppDbContext db, long telegramUserId, string userId, CancellationToken ct = default)
    {
        var row = await db.TelegramSavedAccounts
            .FirstOrDefaultAsync(s => s.TelegramUserId == telegramUserId && s.UserId == userId, ct);
        if (row is null)
            db.TelegramSavedAccounts.Add(new TelegramSavedAccount { TelegramUserId = telegramUserId, UserId = userId });
        else
            row.LastUsedAt = AppClock.Now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (row is null)
        {
            // Two opens at once added the same pair — the other one won, the list is right.
        }
    }

    /// <summary>Drops the account from the Telegram's list (no-op when absent).</summary>
    public static async Task ForgetAsync(IAppDbContext db, long telegramUserId, string userId, CancellationToken ct = default)
    {
        var rows = await db.TelegramSavedAccounts
            .Where(s => s.TelegramUserId == telegramUserId && s.UserId == userId).ToListAsync(ct);
        if (rows.Count == 0) return;
        db.TelegramSavedAccounts.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The Telegram's accounts, most recently used first.</summary>
    public static async Task<List<TelegramSavedAccount>> ListAsync(IAppDbContext db, long telegramUserId, CancellationToken ct = default) =>
        await db.TelegramSavedAccounts.AsNoTracking()
            .Where(s => s.TelegramUserId == telegramUserId)
            .OrderByDescending(s => s.LastUsedAt)
            .ToListAsync(ct);

    /// <summary>
    /// The chat's school notifications follow its accounts (client, 2026-10-08: phone sharing removed
    /// from the bot). Every notifier reads <c>telegram_registrations</c>; this keeps the rows of a
    /// chat in step with the accounts signed in on it: a pupil's login → the pupil and the family
    /// (<see cref="GuardianAccess.FamilyOfAsync"/>), a teacher's → the teacher. Rows written by the
    /// old phone sharing (they carry the phone) are never touched; rows written here carry none and
    /// go when their account leaves the chat.
    /// </summary>
    public static async Task SyncRegistrationsAsync(IAppDbContext db, long chatId, CancellationToken ct = default)
    {
        var userIds = await db.TelegramSavedAccounts.AsNoTracking()
            .Where(s => s.TelegramUserId == chatId).Select(s => s.UserId).ToListAsync(ct);
        var link = await db.TelegramAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.TelegramUserId == chatId, ct);
        if (link is not null && !userIds.Contains(link.UserId)) userIds.Add(link.UserId);

        var studentIds = new HashSet<string>(StringComparer.Ordinal);
        var family = new GuardianAccess(db);
        foreach (var own in await db.Students.AsNoTracking()
                     .Where(s => s.UserId != null && userIds.Contains(s.UserId) && !s.IsArchived)
                     .Select(s => s.Id).ToListAsync(ct))
            foreach (var member in await family.FamilyOfAsync(own, ct))
                studentIds.Add(member.Id);
        var teacherIds = (await db.Teachers.AsNoTracking()
                .Where(t => t.UserId != null && userIds.Contains(t.UserId) && !t.IsArchived)
                .Select(t => t.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var rows = await db.TelegramRegistrations.Where(r => r.ChatId == chatId).ToListAsync(ct);
        var gone = rows.Where(r => string.IsNullOrEmpty(r.Phone)
                                   && (r.TeacherId is { } teacher ? !teacherIds.Contains(teacher) : !studentIds.Contains(r.StudentId)))
            .ToList();
        if (gone.Count > 0)
        {
            db.TelegramRegistrations.RemoveRange(gone);
            await db.SaveChangesAsync(ct); // before the adds — a unique (student, chat) pair may come back
        }
        var kept = rows.Except(gone).ToList();

        var name = link?.DisplayName ?? "";
        var added = false;
        foreach (var id in studentIds.Where(id => !kept.Any(r => r.StudentId == id)))
        {
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = id, ChatId = chatId, ParentName = name, Phone = "" });
            added = true;
        }
        // One teacher row per chat: the unique (student, chat) pair has an empty student for teachers.
        if (!kept.Any(r => r.StudentId == "")
            && teacherIds.FirstOrDefault(id => !kept.Any(r => r.TeacherId == id)) is { } teacherId)
        {
            db.TelegramRegistrations.Add(new TelegramRegistration { TeacherId = teacherId, StudentId = "", ChatId = chatId, ParentName = name, Phone = "" });
            added = true;
        }
        if (added) await db.SaveChangesAsync(ct);
    }

    /// <summary>"5-A sinf · O'quvchi", "O'qituvchi", … — under the name in the switcher.</summary>
    public static async Task<Dictionary<string, string>> SubtitlesAsync(
        IAppDbContext db, IReadOnlyCollection<AppUser> users, CancellationToken ct = default)
    {
        var ids = users.Select(u => u.Id).ToList();
        var classes = await db.Students.AsNoTracking()
            .Where(s => s.UserId != null && ids.Contains(s.UserId))
            .Select(s => new { s.UserId, s.ClassName })
            .ToListAsync(ct);
        var classOf = classes.GroupBy(c => c.UserId!).ToDictionary(g => g.Key, g => g.First().ClassName, StringComparer.Ordinal);
        return users.ToDictionary(u => u.Id, u => u.Role switch
        {
            Roles.Student => classOf.GetValueOrDefault(u.Id) is { Length: > 0 } cls ? $"{cls} sinf · O'quvchi" : "O'quvchi",
            Roles.Teacher => "O'qituvchi",
            "parent" => "Ota-ona",
            Roles.Cashier => "Kassir",
            Roles.Admin or Roles.SuperAdmin => "Administrator",
            _ => "Xodim",
        }, StringComparer.Ordinal);
    }
}
