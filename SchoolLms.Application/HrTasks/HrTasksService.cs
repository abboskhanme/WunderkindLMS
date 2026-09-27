using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  HR topshiriqlari — the application service (docs/modules/hr-tasks.md §5–§7).
//  A port of the old WunderkindHR use_cases/*.py + read_models.py: every rule,
//  limit, message and number is the old one. Split into partial files:
//    HrTasksService.cs            scope, mapping, paging, settings (this file)
//    HrTasksService.People.cs     positions, employees, managers, templates
//    HrTasksService.Tasks.cs      tasks, lifecycle, proofs, attachments, reports, /me
//    HrTasksService.Jobs.cs       generation, morning list, reminders, digest, overdue
//
//  SCOPE (old §2, REJA-2026-09-03): director = whole organisation; manager =
//  self + the whole subtree of hr_employee_managers (walked cycle-safe);
//  anything outside the scope answers 404, never 403. The permission key
//  (hrTasks) only OPENS the panel — which rows is decided here.
// ===========================================================================

/// <summary>The caller of a panel endpoint (built from the JWT claims by the controller).</summary>
public sealed record HrActor(string UserId, string Role, IReadOnlyCollection<string> Permissions)
{
    public bool IsAdmin => Role is Roles.Admin or Roles.SuperAdmin;
}

/// <summary>
/// Which HR employees a panel user sees. <see cref="EmployeeIds"/> null = unlimited;
/// an EMPTY set = nothing (never confuse the two — null opens the whole organisation).
/// </summary>
public sealed class HrScope(IReadOnlySet<string>? employeeIds)
{
    public static readonly HrScope Unlimited = new(null);
    public static readonly HrScope Nothing = new(new HashSet<string>());

    public IReadOnlySet<string>? EmployeeIds { get; } = employeeIds;
    public bool IsLimited => EmployeeIds is not null;
    public bool Allows(string? employeeId) => EmployeeIds is null || (employeeId is not null && EmployeeIds.Contains(employeeId));

    /// <summary>Only for the employee a manager has JUST created (old <c>HrScope.including</c>).</summary>
    public HrScope Including(string employeeId) =>
        EmployeeIds is null ? this : new HrScope(new HashSet<string>(EmployeeIds) { employeeId });

    public List<string> IdList => EmployeeIds is null ? [] : [.. EmployeeIds];
}

/// <summary>A resolved panel caller: scope, own HR row, director flag.</summary>
public sealed record HrPanel(HrActor Actor, HrScope Scope, HrEmployee? Self, bool IsDirector);

public sealed partial class HrTasksService(
    IAppDbContext db, IHrTelegramSender telegram, IHrFileStore files, ILogger<HrTasksService> logger)
{
    /// <summary>"Now" — replaceable in tests. Always an instant with offset 0 (Npgsql rule).</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    private DateTimeOffset Now => Clock().ToUniversalTime();

    private static readonly string[] Actionable = [HrTaskStatuses.Open, HrTaskStatuses.Returned];

    /// <summary>Roles an HR employee may have (hr-tasks.md §2).</summary>
    public static readonly string[] EmployeeUserRoles = [Roles.Staff, Roles.Teacher, Roles.Admin, Roles.SuperAdmin, Roles.Cashier];

    // =====================================================================
    //  Settings (single row, created with the old defaults when missing)
    // =====================================================================

    public async Task<HrSettings> GetOrCreateSettingsAsync(CancellationToken ct = default)
    {
        var found = await db.HrSettings.FirstOrDefaultAsync(s => s.Id == HrSettings.SingletonId, ct);
        if (found is not null) return found;
        var created = new HrSettings { UpdatedAt = Now };
        db.HrSettings.Add(created);
        try
        {
            await db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException)
        {
            // two first callers at once — the other one won; ours is discarded
            db.HrSettings.Entry(created).State = EntityState.Detached;
            return await db.HrSettings.FirstAsync(s => s.Id == HrSettings.SingletonId, ct);
        }
    }

    public async Task<HrSettingsDto> SettingsAsync(HrPanel panel, CancellationToken ct = default)
    {
        EnsureDirector(panel);
        return ToDto(await GetOrCreateSettingsAsync(ct));
    }

    /// <summary><c>PUT</c> — the whole object; omitted reminder fields fall back to the old defaults.</summary>
    public async Task<HrSettingsDto> UpdateSettingsAsync(HrPanel panel, HrSettingsInput input, CancellationToken ct = default)
    {
        EnsureDirector(panel);
        var s = await GetOrCreateSettingsAsync(ct);
        if (input.Timezone is { } tz && !string.IsNullOrWhiteSpace(tz))
        {
            var name = tz.Trim();
            if (name.Length > 64 || !HrValues.IsKnownZone(name)) throw HrErrors.Validation($"Vaqt mintaqasi noto'g'ri: '{name}'");
            s.Timezone = name;
        }
        s.DigestTime = ParseTime(input.DigestTime, "digestTime") ?? throw HrErrors.Validation("digestTime kerak (HH:mm)");
        s.DigestWeekdays = HrSettings.CleanWeekdays(input.DigestWeekdays ?? []);
        s.MorningListEnabled = input.MorningListEnabled ?? true;
        s.MorningTime = ParseTime(input.MorningTime, "morningTime") ?? throw HrErrors.Validation("morningTime kerak (HH:mm)");
        s.GenerationTime = ParseTime(input.GenerationTime, "generationTime") ?? throw HrErrors.Validation("generationTime kerak (HH:mm)");
        s.CloseOverdueTime = ParseTime(input.CloseOverdueTime, "closeOverdueTime") ?? throw HrErrors.Validation("closeOverdueTime kerak (HH:mm)");
        s.ReminderEnabled = input.ReminderEnabled ?? true;
        s.ReminderOffsets = HrSettings.CleanReminderOffsets(
            input.ReminderOffsets ?? [.. HrSettings.DefaultReminderOffsets.Select(x => (int)x)]);
        s.RemindEndOfDay = input.RemindEndOfDay ?? false;
        s.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    private static HrSettingsDto ToDto(HrSettings s) => new(
        s.Timezone, Hm(s.DigestTime), [.. s.DigestWeekdays.Select(x => (int)x)], s.MorningListEnabled,
        Hm(s.MorningTime), Hm(s.GenerationTime), Hm(s.CloseOverdueTime), s.ReminderEnabled,
        [.. s.ReminderOffsets.Select(x => (int)x)], s.RemindEndOfDay, s.UpdatedAt);

    // =====================================================================
    //  Scope
    // =====================================================================

    /// <summary>
    /// admin/superadmin → unlimited; staff with an HR row <c>hr_role = director</c> → unlimited;
    /// staff with an HR row → self + subtree; staff WITHOUT an HR row → nothing (fail-closed).
    /// </summary>
    public async Task<HrPanel> PanelAsync(HrActor actor, CancellationToken ct = default)
    {
        var self = await db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId, ct);
        if (actor.IsAdmin) return new HrPanel(actor, HrScope.Unlimited, self, true);
        if (self is { HrRole: HrRoles.Director }) return new HrPanel(actor, HrScope.Unlimited, self, true);
        if (self is null) return new HrPanel(actor, HrScope.Nothing, null, false);
        return new HrPanel(actor, new HrScope(await SubtreeAsync(self.Id, ct)), self, false);
    }

    public HrAccessDto Access(HrPanel panel) => new(
        panel.IsDirector,
        panel.Actor.IsAdmin || panel.Actor.Permissions.Contains(HrTaskAccess.PermissionKey),
        panel.Scope.IsLimited ? "subtree" : "all",
        panel.Self?.Id,
        panel.Self?.HrRole);

    private static void EnsureDirector(HrPanel panel)
    {
        if (!panel.IsDirector) throw HrErrors.DirectorOnly();
    }

    /// <summary>Self + everyone below (any depth, several managers). Cycle-safe: each node once.</summary>
    public static HashSet<string> Subtree(IEnumerable<(string EmployeeId, string ManagerId)> edges, string root)
    {
        var children = edges.GroupBy(e => e.ManagerId).ToDictionary(g => g.Key, g => g.Select(e => e.EmployeeId).ToList());
        var seen = new HashSet<string> { root };
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            if (!children.TryGetValue(queue.Dequeue(), out var kids)) continue;
            foreach (var kid in kids)
                if (seen.Add(kid)) queue.Enqueue(kid);
        }
        return seen;
    }

    private async Task<List<(string EmployeeId, string ManagerId)>> EdgesAsync(CancellationToken ct) =>
        [.. (await db.HrEmployeeManagers.AsNoTracking().Select(m => new { m.EmployeeId, m.ManagerId }).ToListAsync(ct))
            .Select(m => (m.EmployeeId, m.ManagerId))];

    private async Task<HashSet<string>> SubtreeAsync(string root, CancellationToken ct) =>
        Subtree(await EdgesAsync(ct), root);

    // =====================================================================
    //  Paging — `{items, nextCursor, hasMore, total?}`. The cursor is opaque.
    // =====================================================================

    private static int Cap(int limit) => limit <= 0 ? HrLimits.DefaultLimit : Math.Min(limit, HrLimits.MaxLimit);

    private static int CursorOffset(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(
                cursor.Replace('-', '+').Replace('_', '/').PadRight(cursor.Length + (4 - cursor.Length % 4) % 4, '=')));
            return raw.StartsWith("o:", StringComparison.Ordinal) && int.TryParse(raw[2..], out var n) && n > 0 ? n : 0;
        }
        catch (FormatException) { return 0; } // a broken cursor restarts the list, never a 500 (old rule)
    }

    private static string Cursor(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"o:{offset}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<(List<T> Rows, bool HasMore, int Offset, int? Total)> PageAsync<T>(
        IQueryable<T> ordered, IQueryable<T> unordered, HrPaging paging, CancellationToken ct)
    {
        var limit = Cap(paging.Limit);
        var offset = CursorOffset(paging.Cursor);
        var rows = await ordered.Skip(offset).Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        int? total = paging.WithTotal ? await unordered.CountAsync(ct) : null;
        return (rows, hasMore, offset + rows.Count, total);
    }

    private static HrPage<TOut> Page<TOut>(IReadOnlyList<TOut> items, bool hasMore, int nextOffset, int? total) =>
        new(items, hasMore ? Cursor(nextOffset) : null, hasMore, total);

    // =====================================================================
    //  Parsing helpers (JSON bodies)
    // =====================================================================

    /// <summary>"HH:mm" or "HH:mm:ss"; null/blank → null; garbage → 422.</summary>
    public static TimeOnly? ParseTime(string? raw, string field)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (TimeOnly.TryParseExact(raw.Trim(), ["HH:mm", "HH:mm:ss", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return t;
        throw HrErrors.Validation($"{field} noto'g'ri vaqt (HH:mm kutilgan): '{raw}'");
    }

    public static string? Hm(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Hm(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// An instant from the client. With an offset/Z — as given; WITHOUT one it is Tashkent wall
    /// time (old <c>_aware</c>): reading it as UTC would move the deadline by 5 hours.
    /// </summary>
    public static DateTimeOffset ParseInstant(string? raw, string field, TimeZoneInfo tz)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw HrErrors.Validation($"{field} kerak");
        var text = raw.Trim();
        var timePart = text.Contains('T') ? text[(text.IndexOf('T') + 1)..] : text.Contains(' ') ? text[(text.IndexOf(' ') + 1)..] : "";
        var hasOffset = text.EndsWith('Z') || text.EndsWith('z') || timePart.Contains('+') || timePart.LastIndexOf('-') > 0;
        if (hasOffset)
        {
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
                return withOffset.ToUniversalTime();
        }
        else if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, tz), TimeSpan.Zero);
        }
        throw HrErrors.Validation($"{field} noto'g'ri sana-vaqt: '{raw}'");
    }

    private static bool Has(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out _);

    private static string? Str(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }

    private static bool? Bool(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw HrErrors.Validation($"{name} true/false bo'lishi kerak"),
        };
    }

    private static decimal? Dec(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s)) return s;
        throw HrErrors.InvalidAmount($"Miqdor son bo'lishi kerak ({name})");
    }

    private static List<int>? Ints(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Array) throw HrErrors.Validation($"{name} ro'yxat bo'lishi kerak");
        var list = new List<int>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var n))
                throw HrErrors.Validation($"{name} butun sonlar ro'yxati bo'lishi kerak");
            list.Add(n);
        }
        return list;
    }

    private static List<string>? Strings(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Array) throw HrErrors.Validation($"{name} ro'yxat bo'lishi kerak");
        return [.. v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText())];
    }

    /// <summary><c>?sort=-due_at</c> / <c>dueAt</c> → (snake field, descending); unknown → fallback (old rule: no 422).</summary>
    private static (string Field, bool Desc) SortOf(string? raw, IReadOnlyCollection<string> allowed, string fallback)
    {
        var (fbField, fbDesc) = (fallback.TrimStart('-'), fallback.StartsWith('-'));
        if (string.IsNullOrWhiteSpace(raw)) return (fbField, fbDesc);
        var text = raw.Trim();
        var desc = text.StartsWith('-');
        var name = ToSnake(text.TrimStart('-', '+'));
        return allowed.Contains(name) ? (name, desc) : (fbField, fbDesc);
    }

    private static string ToSnake(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsUpper(ch)) { if (sb.Length > 0) sb.Append('_'); sb.Append(char.ToLowerInvariant(ch)); }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private static DateTimeOffset? LinkedAt(DateTime? localLinkedAt)
    {
        if (localLinkedAt is not { } dt) return null;
        var tz = HrValues.Zone("Asia/Tashkent");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), tz), TimeSpan.Zero);
    }

    // =====================================================================
    //  Telegram — every chat of a user (telegram_accounts), never throws
    // =====================================================================

    /// <summary>userId → Telegram chat ids (private chat id == Telegram user id).</summary>
    private async Task<Dictionary<string, List<long>>> ChatsAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await db.TelegramAccounts.AsNoTracking()
            .Where(a => ids.Contains(a.UserId))
            .Select(a => new { a.UserId, a.TelegramUserId })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(r => r.TelegramUserId).Distinct().ToList());
    }

    /// <summary>Sends <paramref name="html"/> to every chat of the user; returns messages delivered.</summary>
    private async Task<int> SendToUserAsync(string userId, string html, CancellationToken ct, (byte[] Bytes, string Name, string Type)? voice = null)
    {
        if (!telegram.IsConfigured || string.IsNullOrEmpty(html)) return 0;
        try
        {
            var chats = await ChatsAsync([userId], ct);
            if (!chats.TryGetValue(userId, out var chatIds)) return 0;
            var sent = 0;
            foreach (var chat in chatIds)
            {
                if (await telegram.SendHtmlAsync(chat, Truncate(html), ct)) sent++;
                if (voice is { } v)
                    await telegram.SendVoiceAsync(chat, v.Bytes, v.Name, v.Type, HrTasksTexts.VoiceCaption, ct);
            }
            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // a Telegram failure never fails the request that caused the message
            logger.LogWarning(ex, "HR: Telegram xabari yuborilmadi (user {UserId})", userId);
            return 0;
        }
    }

    private static string Truncate(string text) =>
        text.Length <= HrTasksTexts.TelegramMaxLen ? text : text[..HrTasksTexts.TelegramMaxLen];
}
