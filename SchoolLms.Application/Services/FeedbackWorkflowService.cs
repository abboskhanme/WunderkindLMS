using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Taklif va shikoyatlar as a workflow (client, 2026-10-07; FeedbackWorkflow.cs):
//
//    sender (parent / teacher, Mini App) ──▶ Qabul qilindi ──▶ Jarayonda ──▶ Hal qilindi
//                                    │ department + responsible person (bot message)
//                                    └ every status change and the answer go back to the sender
//
//  The school side works the inbox in the admin panel (perm "feedback"). Telegram is the only
//  channel; a message that cannot be sent never blocks the change itself.
// ===========================================================================

public record FeedbackDepartmentMemberDto(string UserId, string FullName, bool HasAccess);

public record FeedbackDepartmentDto(
    Guid Id, string Name, int Position, bool IsActive, List<FeedbackDepartmentMemberDto> Members);

/// <summary>Department form. <see cref="MemberUserIds"/> — the responsible people.</summary>
public record FeedbackDepartmentPayload(string Name, int Position, bool IsActive, List<string>? MemberUserIds);

/// <summary>A department as the sender picks it (no people).</summary>
public record FeedbackDepartmentOptionDto(Guid Id, string Name);

public record FeedbackEventDto(
    Guid Id, string? Status, string? StatusLabel, string? DepartmentName, string? AssigneeName,
    string? Note, bool IsPublic, string ActorName, DateTimeOffset CreatedAt);

/// <summary>
/// One step of the change the inbox asks for. Null fields stay as they are; <see cref="Answer"/> is the
/// text the sender sees; <see cref="Note"/> is an internal comment.
/// </summary>
public record FeedbackUpdateRequest(
    string? Status = null, Guid? DepartmentId = null, string? AssigneeId = null,
    bool ClearAssignee = false, string? Note = null, string? Answer = null);

/// <summary>An item as its sender sees it in the app ("mening murojaatlarim").</summary>
public record MyFeedbackDto(
    string Id, string Type, string Text, string? ImageUrl, string Status, string StatusLabel,
    string? DepartmentName, string? Answer, string CreatedAt, string? UpdatedAt, string? ResolvedAt,
    List<MyFeedbackStepDto> Steps);

/// <summary>A status step the sender sees: when it became "Jarayonda", "Hal qilindi"…</summary>
public record MyFeedbackStepDto(string Status, string StatusLabel, DateTimeOffset At, string? Note);

public static class FeedbackWorkflowService
{
    public const int MaxText = 2000;

    /// <summary>The first history step of a new item — the controller adds it with the item.</summary>
    public static FeedbackEvent Received(Feedback item, string actorId, string actorName) => new()
    {
        FeedbackId = item.Id,
        Status = FeedbackStatus.New,
        DepartmentId = item.DepartmentId,
        ActorId = actorId,
        ActorName = actorName,
        CreatedAt = AppClock.NowInstant,
    };

    /// <summary>The active departments a sender can choose from.</summary>
    public static Task<List<FeedbackDepartmentOptionDto>> OptionsAsync(IAppDbContext db, CancellationToken ct = default) =>
        db.FeedbackDepartments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Position).ThenBy(d => d.Name)
            .Select(d => new FeedbackDepartmentOptionDto(d.Id, d.Name))
            .ToListAsync(ct);

    /// <summary>A department id the sender chose: must exist and be active; anything else is ignored.</summary>
    public static async Task<Guid?> ChosenDepartmentAsync(IAppDbContext db, string? raw, CancellationToken ct = default)
    {
        if (!Guid.TryParse(raw, out var id)) return null;
        return await db.FeedbackDepartments.AnyAsync(d => d.Id == id && d.IsActive, ct) ? id : null;
    }

    // =====================================================================
    //  Inbox — status, routing, answer
    // =====================================================================

    /// <summary>
    /// Applies one step. Returns an error text (nothing changed) or null. <paramref name="changes"/>
    /// tells the caller whom to notify.
    /// </summary>
    public static async Task<(string? Error, FeedbackChanges Changes)> UpdateAsync(
        IAppDbContext db, Feedback item, FeedbackUpdateRequest request, string actorId, string actorName,
        CancellationToken ct = default)
    {
        var changes = new FeedbackChanges();
        string? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            status = request.Status.Trim();
            if (!FeedbackStatus.All.Contains(status)) return ($"Noma'lum holat: {status}", changes);
            if (status == item.Status) status = null;
        }

        Guid? department = null;
        if (request.DepartmentId is { } dep && dep != item.DepartmentId)
        {
            if (!await db.FeedbackDepartments.AnyAsync(d => d.Id == dep, ct)) return ("Bo'lim topilmadi", changes);
            department = dep;
        }

        string? assignee = null;
        if (!string.IsNullOrWhiteSpace(request.AssigneeId) && request.AssigneeId != item.AssigneeId)
        {
            var ok = await db.Users.AnyAsync(u => u.Id == request.AssigneeId
                && (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || u.Role == Roles.Staff), ct);
            if (!ok) return ("Mas'ul xodim topilmadi", changes);
            assignee = request.AssigneeId;
        }

        var answer = Clip(request.Answer);
        var note = Clip(request.Note);
        var clearAssignee = request.ClearAssignee && item.AssigneeId is not null && assignee is null;
        if (status is null && department is null && assignee is null && !clearAssignee && answer is null && note is null)
            return ("O'zgarish yo'q", changes);

        var now = AppClock.Now;
        if (status is not null)
        {
            item.Status = status;
            item.ResolvedAt = status == FeedbackStatus.Resolved ? now : null;
            changes.StatusChanged = true;
        }
        if (department is not null)
        {
            item.DepartmentId = department;
            // A new department starts without a person unless one is named in the same step.
            if (assignee is null) item.AssigneeId = null;
            changes.Routed = true;
        }
        if (assignee is not null)
        {
            item.AssigneeId = assignee;
            changes.Routed = true;
        }
        if (clearAssignee) item.AssigneeId = null;
        if (answer is not null)
        {
            item.Answer = answer;
            changes.Answered = true;
        }
        item.UpdatedAt = now;

        db.FeedbackEvents.Add(new FeedbackEvent
        {
            FeedbackId = item.Id,
            Status = status,
            DepartmentId = department,
            AssigneeId = assignee,
            Note = answer ?? note,
            IsPublic = answer is not null,
            ActorId = actorId,
            ActorName = actorName,
            CreatedAt = AppClock.NowInstant,
        });
        return (null, changes);
    }

    public sealed class FeedbackChanges
    {
        public bool StatusChanged { get; set; }
        public bool Routed { get; set; }
        public bool Answered { get; set; }
    }

    /// <summary>The item's whole history, oldest first (the inbox detail).</summary>
    public static async Task<List<FeedbackEventDto>> HistoryAsync(IAppDbContext db, string feedbackId, CancellationToken ct = default)
    {
        var rows = await db.FeedbackEvents.AsNoTracking()
            .Where(e => e.FeedbackId == feedbackId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
        var depIds = rows.Where(r => r.DepartmentId != null).Select(r => r.DepartmentId!.Value).Distinct().ToList();
        var deps = await db.FeedbackDepartments.AsNoTracking().Where(d => depIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var userIds = rows.Where(r => r.AssigneeId != null).Select(r => r.AssigneeId!).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return [.. rows.Select(r => new FeedbackEventDto(
            r.Id, r.Status, r.Status is null ? null : FeedbackStatus.Label(r.Status),
            r.DepartmentId is { } d ? deps.GetValueOrDefault(d) : null,
            r.AssigneeId is { } a ? users.GetValueOrDefault(a) : null,
            r.Note, r.IsPublic, r.ActorName, r.CreatedAt))];
    }

    // =====================================================================
    //  Sender — "mening murojaatlarim"
    // =====================================================================

    /// <summary>
    /// The sender's own items, newest first. A teacher's older items (sent before the account was
    /// recorded) are found by the teacher id.
    /// </summary>
    public static async Task<List<MyFeedbackDto>> MineAsync(
        IAppDbContext db, string userId, string? teacherId, CancellationToken ct = default)
    {
        var items = await db.Feedbacks.AsNoTracking()
            .Where(f => f.SenderUserId == userId || (teacherId != null && f.SenderUserId == null && f.TeacherId == teacherId))
            .OrderByDescending(f => f.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
        if (items.Count == 0) return [];

        var ids = items.Select(f => f.Id).ToList();
        var steps = await db.FeedbackEvents.AsNoTracking()
            .Where(e => ids.Contains(e.FeedbackId) && (e.Status != null || e.IsPublic))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
        var byItem = steps.GroupBy(s => s.FeedbackId).ToDictionary(g => g.Key, g => g.ToList());
        var depIds = items.Where(f => f.DepartmentId != null).Select(f => f.DepartmentId!.Value).Distinct().ToList();
        var deps = await db.FeedbackDepartments.AsNoTracking().Where(d => depIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        return [.. items.Select(f => new MyFeedbackDto(
            f.Id, f.Type, f.Text, f.ImageUrl, f.Status, FeedbackStatus.Label(f.Status),
            f.DepartmentId is { } d ? deps.GetValueOrDefault(d) : null,
            f.Answer, f.CreatedAt.ToString("o"), f.UpdatedAt?.ToString("o"), f.ResolvedAt?.ToString("o"),
            byItem.TryGetValue(f.Id, out var s)
                ? [.. s.Select(e => new MyFeedbackStepDto(
                    e.Status ?? f.Status, FeedbackStatus.Label(e.Status ?? f.Status), e.CreatedAt, e.IsPublic ? e.Note : null))]
                : []))];
    }

    // =====================================================================
    //  Departments
    // =====================================================================

    public static async Task<List<FeedbackDepartmentDto>> DepartmentsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var deps = await db.FeedbackDepartments.AsNoTracking().OrderBy(d => d.Position).ThenBy(d => d.Name).ToListAsync(ct);
        var members = await (
                from m in db.FeedbackDepartmentMembers.AsNoTracking()
                join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                select new { m.DepartmentId, u.Id, u.FullName, u.Role, u.Permissions })
            .ToListAsync(ct);
        return [.. deps.Select(d => new FeedbackDepartmentDto(
            d.Id, d.Name, d.Position, d.IsActive,
            [.. members.Where(m => m.DepartmentId == d.Id)
                .OrderBy(m => m.FullName)
                .Select(m => new FeedbackDepartmentMemberDto(m.Id, m.FullName, HasAccess(m.Role, m.Permissions)))]))];
    }

    /// <summary>Creates or updates a department and replaces its people. Error text or null.</summary>
    public static async Task<string?> SaveDepartmentAsync(
        IAppDbContext db, FeedbackDepartment row, FeedbackDepartmentPayload p, bool isNew, CancellationToken ct = default)
    {
        var name = (p.Name ?? "").Trim();
        if (name.Length == 0) return "Bo'lim nomini kiriting";
        if (name.Length > 100) return "Bo'lim nomi 100 belgidan oshmasin";
        var lowered = name.ToLowerInvariant();
        if (await db.FeedbackDepartments.AnyAsync(d => d.Name.ToLower() == lowered && d.Id != row.Id, ct))
            return $"«{name}» nomli bo'lim allaqachon bor";

        var wanted = (p.MemberUserIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var valid = await db.Users
            .Where(u => wanted.Contains(u.Id) && (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || u.Role == Roles.Staff))
            .Select(u => u.Id).ToListAsync(ct);
        if (valid.Count != wanted.Count) return "Mas'ul faqat xodim yoki administrator bo'la oladi";

        row.Name = name;
        row.Position = p.Position;
        row.IsActive = p.IsActive;
        if (isNew)
        {
            row.CreatedAt = AppClock.NowInstant;
            db.FeedbackDepartments.Add(row);
        }

        var existing = await db.FeedbackDepartmentMembers.Where(m => m.DepartmentId == row.Id).ToListAsync(ct);
        foreach (var gone in existing.Where(m => !wanted.Contains(m.UserId))) db.FeedbackDepartmentMembers.Remove(gone);
        foreach (var add in wanted.Where(u => existing.All(m => m.UserId != u)))
            db.FeedbackDepartmentMembers.Add(new FeedbackDepartmentMember { DepartmentId = row.Id, UserId = add });
        return null;
    }

    /// <summary>Can this person work the inbox (admin, or staff with the "feedback" permission)?</summary>
    public static bool HasAccess(string role, List<string> permissions) =>
        role is Roles.Admin or Roles.SuperAdmin || (role == Roles.Staff && permissions.Contains(InboxPermission));

    public const string InboxPermission = "feedback";

    private static string? Clip(string? text)
    {
        var t = (text ?? "").Trim();
        return t.Length == 0 ? null : t[..Math.Min(t.Length, MaxText)];
    }
}

/// <summary>
/// Telegram side of the workflow: the department's people (or the inbox staff) hear about a new or
/// re-routed item, the sender hears about every status change and the answer. Never throws.
/// </summary>
public sealed class FeedbackNotifier(IAppDbContext db, TelegramService telegram, ILogger<FeedbackNotifier> logger)
{
    private static string Kind(Feedback f) => f.Type == "complaint" ? "shikoyat" : "taklif";

    /// <summary>New item — to its department's people, else to everyone who works the inbox.</summary>
    public async Task<int> NewAsync(Feedback item, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured) return 0;
        try
        {
            var dep = item.DepartmentId is { } d
                ? await db.FeedbackDepartments.AsNoTracking().Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(ct)
                : null;
            var users = await DepartmentPeopleAsync(item.DepartmentId, ct);
            if (users.Count == 0) users = await InboxPeopleAsync(ct);
            var text = $"📨 Yangi {Kind(item)}{(dep is null ? "" : $" — {dep}")}\n\n"
                     + $"Kimdan: {await SenderLineAsync(item, ct)}\n\n{Short(item.Text, 1500)}\n\n"
                     + "Ko'rish: LMS → Boshqaruv → Taklif va shikoyatlar.";
            return await SendToUsersAsync(users, text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Shikoyat xabari yuborilmadi: feedback={Id}", item.Id);
            return 0;
        }
    }

    /// <summary>Re-routed — to the new person, or to the new department's people.</summary>
    public async Task<int> RoutedAsync(Feedback item, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured) return 0;
        try
        {
            List<string> users = item.AssigneeId is { } a ? [a] : await DepartmentPeopleAsync(item.DepartmentId, ct);
            if (users.Count == 0) return 0;
            var dep = item.DepartmentId is { } d
                ? await db.FeedbackDepartments.AsNoTracking().Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(ct)
                : null;
            var text = $"📌 Sizga {Kind(item)} biriktirildi{(dep is null ? "" : $" — {dep}")}\n\n"
                     + $"Kimdan: {await SenderLineAsync(item, ct)}\n\n{Short(item.Text, 1500)}\n\n"
                     + "Ko'rish: LMS → Boshqaruv → Taklif va shikoyatlar.";
            return await SendToUsersAsync(users, text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Shikoyat biriktirish xabari yuborilmadi: feedback={Id}", item.Id);
            return 0;
        }
    }

    /// <summary>Status or answer changed — to the sender.</summary>
    public async Task<int> SenderAsync(Feedback item, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured) return 0;
        try
        {
            var chats = new HashSet<long>();
            if (!string.IsNullOrEmpty(item.SenderUserId))
                chats.UnionWith(await db.TelegramAccounts.AsNoTracking()
                    .Where(x => x.UserId == item.SenderUserId).Select(x => x.TelegramUserId).ToListAsync(ct));
            if (chats.Count == 0 && !string.IsNullOrEmpty(item.TeacherId))
                chats.UnionWith(await db.TelegramRegistrations.AsNoTracking()
                    .Where(r => r.TeacherId == item.TeacherId).Select(r => r.ChatId).ToListAsync(ct));
            chats.Remove(0);

            var lines = new List<string>
            {
                $"📬 Murojaatingiz: {FeedbackStatus.Label(item.Status)}",
                "",
                $"«{Short(item.Text, 200)}»",
            };
            if (!string.IsNullOrWhiteSpace(item.Answer))
            {
                lines.Add("");
                lines.Add($"Javob: {item.Answer}");
            }
            lines.Add("");
            lines.Add($"Batafsil: «{TelegramPanelKeys.ButtonText}» → Taklif va shikoyatlar.");
            var text = string.Join('\n', lines);
            var sent = 0;
            foreach (var chat in chats)
                if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Murojaat holati xabari yuborilmadi: feedback={Id}", item.Id);
            return 0;
        }
    }

    private async Task<List<string>> DepartmentPeopleAsync(Guid? departmentId, CancellationToken ct) =>
        departmentId is not { } d
            ? []
            : await db.FeedbackDepartmentMembers.AsNoTracking().Where(m => m.DepartmentId == d).Select(m => m.UserId).ToListAsync(ct);

    /// <summary>Who works the inbox when a department has nobody: admins and staff with the permission.</summary>
    private Task<List<string>> InboxPeopleAsync(CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Admin || u.Role == Roles.SuperAdmin
                        || (u.Role == Roles.Staff && u.Permissions.Contains(FeedbackWorkflowService.InboxPermission)))
            .Select(u => u.Id).ToListAsync(ct);

    private async Task<int> SendToUsersAsync(List<string> userIds, string text, CancellationToken ct)
    {
        var chats = await db.TelegramAccounts.AsNoTracking()
            .Where(a => userIds.Contains(a.UserId)).Select(a => a.TelegramUserId).Distinct().ToListAsync(ct);
        var sent = 0;
        foreach (var chat in chats.Where(c => c != 0))
            if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
        return sent;
    }

    /// <summary>"O'qituvchi X" or "Ota-ona Y (Ali Valiyev, 5-A)" — the person needs to know whose child.</summary>
    private async Task<string> SenderLineAsync(Feedback f, CancellationToken ct)
    {
        if (f.SenderRole == "teacher") return $"O'qituvchi {f.SenderName}";
        var child = string.IsNullOrEmpty(f.StudentId)
            ? null
            : await db.Students.AsNoTracking().Where(s => s.Id == f.StudentId)
                .Select(s => new { s.FullName, s.ClassName }).FirstOrDefaultAsync(ct);
        var who = string.IsNullOrWhiteSpace(f.SenderName) ? "Ota-ona" : $"Ota-ona {f.SenderName}";
        return child is null ? who : $"{who} ({child.FullName}, {child.ClassName})";
    }

    private static string Short(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
