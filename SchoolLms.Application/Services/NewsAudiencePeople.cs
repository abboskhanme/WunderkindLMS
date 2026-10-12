using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  WHO a news item's Telegram message is for — by name, not just by chat id (client, 2026-10-12:
//  "Telegram: 120/150 bunda aynan kimlarga borgan bormaganini bilsa bo'ladimi").
//
//  The chats are exactly NewsTelegramNotifier.RecipientsAsync's (same two sources, same audience
//  rules); this only says whose they are:
//    * parent   — bot registration (parent name + the pupil) or a Mini App parent account (its
//                 name + the guardian's children);
//    * employee — a teacher's bot registration, or a Mini App account with a staff role;
//    * student  — a Mini App pupil account.
//  One chat can be several of these (a teacher who is also a parent): it is listed once, under
//  the first, with every detail.
//
//  And who the message cannot reach at all: active pupils whose parents have no chat, and
//  teachers / staff with none — "Telegramga ulanmagan", as in the announcement detail. Pupils'
//  own accounts are optional, so a pupil without one is not listed.
// ===========================================================================

/// <summary>One row of a news item's delivery list (the "Batafsil" dialog).</summary>
/// <param name="Status">sent | failed | no_telegram.</param>
public sealed record NewsRecipientDto(string Kind, string Name, string? Detail, string? ChatId, string Status);

/// <summary>A news item's delivery list. <paramref name="HasDetails"/> false — sent before the list was kept.</summary>
public sealed record NewsRecipientsDto(bool HasDetails, IReadOnlyList<NewsRecipientDto> Recipients);

/// <summary>One person in a news item's Telegram audience. <paramref name="ChatId"/> null = not connected.</summary>
public sealed record NewsAudienceEntry(long? ChatId, string Kind, string Name, string? Detail);

/// <summary>Resolves a news item's audience to people. File header.</summary>
public static class NewsAudiencePeople
{
    private const string ParentRole = "parent";

    private static readonly string[] EmployeeRoles =
        [Roles.Teacher, Roles.Staff, Roles.Admin, Roles.SuperAdmin, Roles.Cashier];

    private sealed class Chat(string kind, string name)
    {
        public string Kind { get; } = kind;
        public string Name { get; set; } = name;
        public List<string> Details { get; } = [];
    }

    public static async Task<List<NewsAudienceEntry>> ResolveAsync(
        IAppDbContext db, NewsItem news, CancellationToken ct = default)
    {
        var chats = new Dictionary<long, Chat>();
        var missing = new List<NewsAudienceEntry>();

        void Add(long chatId, string kind, string? name, string? detail)
        {
            if (chatId == 0) return;
            if (!chats.TryGetValue(chatId, out var chat)) chats[chatId] = chat = new Chat(kind, name?.Trim() ?? "");
            else if (string.IsNullOrWhiteSpace(chat.Name) && !string.IsNullOrWhiteSpace(name)) chat.Name = name.Trim();
            if (!string.IsNullOrWhiteSpace(detail) && !chat.Details.Contains(detail)) chat.Details.Add(detail);
        }

        static string Pupil(string fullName, string className) =>
            string.IsNullOrWhiteSpace(className) ? fullName : $"{fullName} ({className})";

        if (news.ForParent)
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);

            var regs = await (from r in db.TelegramRegistrations.AsNoTracking()
                              where r.TeacherId == null
                              join s in db.Students.AsNoTracking() on r.StudentId equals s.Id into js
                              from s in js.DefaultIfEmpty()
                              select new { r.ChatId, r.ParentName, r.StudentId, Pupil = s == null ? null : s.FullName, Class = s == null ? "" : s.ClassName })
                .ToListAsync(ct);
            foreach (var r in regs)
            {
                Add(r.ChatId, NewsRecipientKind.Parent, r.ParentName, r.Pupil is null ? null : Pupil(r.Pupil, r.Class));
                covered.Add(r.StudentId);
            }

            var accounts = await (from a in db.TelegramAccounts.AsNoTracking()
                                  join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                                  where u.Role == ParentRole
                                  select new { a.TelegramUserId, UserId = u.Id, u.FullName }).ToListAsync(ct);
            var parentUserIds = accounts.Select(a => a.UserId).Distinct().ToList();
            var children = parentUserIds.Count == 0
                ? []
                : await (from g in db.Guardians.AsNoTracking()
                         where g.UserId != null && parentUserIds.Contains(g.UserId)
                         join l in db.StudentGuardians.AsNoTracking() on g.Id equals l.GuardianId
                         join s in db.Students.AsNoTracking() on l.StudentId equals s.Id
                         where !s.IsArchived
                         orderby s.ClassName, s.FullName
                         select new { UserId = g.UserId!, s.Id, s.FullName, s.ClassName }).ToListAsync(ct);
            foreach (var a in accounts)
            {
                var kids = children.Where(c => c.UserId == a.UserId).ToList();
                if (kids.Count == 0) Add(a.TelegramUserId, NewsRecipientKind.Parent, a.FullName, null);
                foreach (var k in kids)
                {
                    Add(a.TelegramUserId, NewsRecipientKind.Parent, a.FullName, Pupil(k.FullName, k.ClassName));
                    covered.Add(k.Id);
                }
            }

            var unreachable = await db.Students.AsNoTracking()
                .Where(s => !s.IsArchived)
                .Select(s => new { s.Id, s.FullName, s.ClassName, s.ParentFullName })
                .ToListAsync(ct);
            missing.AddRange(unreachable
                .Where(s => !covered.Contains(s.Id))
                .OrderBy(s => s.ClassName, StringComparer.Ordinal).ThenBy(s => s.FullName, StringComparer.Ordinal)
                .Select(s => new NewsAudienceEntry(null, NewsRecipientKind.Parent,
                    string.IsNullOrWhiteSpace(s.ParentFullName) ? "Ota-ona" : s.ParentFullName.Trim(), Pupil(s.FullName, s.ClassName))));
        }

        if (news.ForEmployee)
        {
            var coveredTeachers = new HashSet<string>(StringComparer.Ordinal);
            var coveredUsers = new HashSet<string>(StringComparer.Ordinal);

            var regs = await (from r in db.TelegramRegistrations.AsNoTracking()
                              where r.TeacherId != null
                              join t in db.Teachers.AsNoTracking() on r.TeacherId equals t.Id into jt
                              from t in jt.DefaultIfEmpty()
                              select new { r.ChatId, TeacherId = r.TeacherId!, Name = t == null ? r.ParentName : t.FullName })
                .ToListAsync(ct);
            foreach (var r in regs)
            {
                Add(r.ChatId, NewsRecipientKind.Employee, r.Name, RoleLabel(Roles.Teacher));
                coveredTeachers.Add(r.TeacherId);
            }

            var accounts = await (from a in db.TelegramAccounts.AsNoTracking()
                                  join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                                  where EmployeeRoles.Contains(u.Role)
                                  select new { a.TelegramUserId, UserId = u.Id, u.FullName, u.Role }).ToListAsync(ct);
            foreach (var a in accounts)
            {
                Add(a.TelegramUserId, NewsRecipientKind.Employee, a.FullName, RoleLabel(a.Role));
                coveredUsers.Add(a.UserId);
            }

            var teachers = await db.Teachers.AsNoTracking()
                .Where(t => !t.IsArchived)
                .Select(t => new { t.Id, t.FullName, t.UserId })
                .ToListAsync(ct);
            missing.AddRange(teachers
                .Where(t => !coveredTeachers.Contains(t.Id) && (t.UserId is null || !coveredUsers.Contains(t.UserId)))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => new NewsAudienceEntry(null, NewsRecipientKind.Employee, t.FullName, RoleLabel(Roles.Teacher))));

            // Staff accounts other than teachers (a teacher's account is covered by the teacher above).
            var staff = await db.Users.AsNoTracking()
                .Where(u => EmployeeRoles.Contains(u.Role) && u.Role != Roles.Teacher)
                .Select(u => new { u.Id, u.FullName, u.Role })
                .ToListAsync(ct);
            missing.AddRange(staff
                .Where(u => !coveredUsers.Contains(u.Id))
                .OrderBy(u => u.FullName, StringComparer.Ordinal)
                .Select(u => new NewsAudienceEntry(null, NewsRecipientKind.Employee, u.FullName, RoleLabel(u.Role))));
        }

        if (news.ForStudent)
        {
            var accounts = await (from a in db.TelegramAccounts.AsNoTracking()
                                  join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                                  where u.Role == Roles.Student
                                  join s in db.Students.AsNoTracking() on (string?)u.Id equals s.UserId into js
                                  from s in js.DefaultIfEmpty()
                                  select new { a.TelegramUserId, u.FullName, Class = s == null ? null : s.ClassName })
                .ToListAsync(ct);
            foreach (var a in accounts)
                Add(a.TelegramUserId, NewsRecipientKind.Student, a.FullName, a.Class);
        }

        return
        [
            .. chats.Select(kv => new NewsAudienceEntry(
                kv.Key, kv.Value.Kind, kv.Value.Name,
                kv.Value.Details.Count == 0 ? null : string.Join(", ", kv.Value.Details))),
            .. missing,
        ];
    }

    /// <summary>The role as the school says it.</summary>
    public static string RoleLabel(string role) => role switch
    {
        Roles.Teacher => "O'qituvchi",
        Roles.Admin => "Administrator",
        Roles.SuperAdmin => "Rahbariyat",
        Roles.Cashier => "Kassir",
        _ => "Xodim",
    };
}
