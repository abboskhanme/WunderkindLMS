using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  HR topshiriqlari — AI orqali yaratish, phase 1 (docs/modules/hr-ai-tasks.md §4).
//
//  director's text / voice ──► IHrTaskAi ──► HrAiProposals.Read (code validates) ──► hr_ai_drafts
//      ──► card [Tasdiqlash | O'zgartirish | Bekor] ──► Tasdiqlash ──► HrTasksService (the director's
//      own HrPanel: the panel's validation, scope and notifications).
//
//  Approval lives in code: a draft becomes rows only on a button press by the draft's own director,
//  claimed in one conditional UPDATE so a double press creates nothing twice.
// ===========================================================================

public sealed class HrAiTaskService(
    IAppDbContext db, HrTasksService hr, IHrTaskAi ai, TelegramService telegram, ILogger logger)
{
    /// <summary>AI calls per director per hour — a cost guard (in memory: a restart forgives).</summary>
    public const int MaxRequestsPerHour = 30;

    /// <summary>After "O'zgartirish" the next message revises the draft for this long.</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(30);

    /// <summary>Telegram's <c>getFile</c> limit for bots.</summary>
    public const long MaxVoiceBytes = 20 * 1024 * 1024;

    private static readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> Calls = new();

    /// <summary>"Now" — replaceable in tests. An instant with offset 0.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    private DateTimeOffset Now => Clock().ToUniversalTime();

    // =====================================================================
    //  Messages
    // =====================================================================

    /// <summary>
    /// A private message to the bot. true — taken (the sender is a director and wrote text or sent a
    /// voice note); false — not ours, the bot answers as usual.
    /// </summary>
    public async Task<bool> HandleMessageAsync(JsonElement msg, CancellationToken ct = default)
    {
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatEl)) return false;
        if (!chat.TryGetProperty("type", out var type) || type.GetString() != "private") return false;
        var chatId = chatEl.GetInt64();

        var text = msg.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
        var voice = msg.TryGetProperty("voice", out var v) ? v : msg.TryGetProperty("audio", out var a) ? a : default;
        var isVoice = voice.ValueKind == JsonValueKind.Object;
        if (!isVoice && (text.Length == 0 || text.StartsWith('/'))) return false;

        var director = await DirectorAsync(chatId, ct);
        if (director is null) return false;
        var userId = director.Value.UserId;

        if (!ai.IsConfigured)
        {
            await telegram.SendHtmlAsync(chatId, HrAiTexts.AiOff, ct: ct);
            return true;
        }
        await ExpireAsync(userId, ct);
        if (!TakeCall(userId))
        {
            await telegram.SendHtmlAsync(chatId, HrAiTexts.TooMany, ct: ct);
            return true;
        }

        HrAiAudio? audio = null;
        if (isVoice)
        {
            var seconds = voice.TryGetProperty("duration", out var d) && d.TryGetInt32(out var s) ? s : 0;
            if (seconds > HrLimits.MaxVoiceSeconds)
            {
                await telegram.SendHtmlAsync(chatId, HrAiTexts.VoiceTooLong(HrLimits.MaxVoiceSeconds), ct: ct);
                return true;
            }
            var fileId = voice.TryGetProperty("file_id", out var f) ? f.GetString() : null;
            var bytes = fileId is null ? null : await telegram.DownloadFileAsync(fileId, MaxVoiceBytes, ct);
            if (bytes is null || bytes.Length == 0)
            {
                await telegram.SendHtmlAsync(chatId, HrAiTexts.VoiceFailed, ct: ct);
                return true;
            }
            var mime = voice.TryGetProperty("mime_type", out var m) && m.GetString() is { Length: > 0 } mt ? mt : "audio/ogg";
            audio = new HrAiAudio(bytes, mime);
        }

        await telegram.SendChatActionAsync(chatId, "typing", ct);
        var editing = await EditingDraftAsync(userId, ct);
        var tz = (await hr.GetOrCreateSettingsAsync(ct)).Zone;
        var localNow = HrValues.Local(Now, tz);
        var (employees, positions, employeeRefs, positionRefs) = await RefsAsync(ct);

        var current = editing is null ? null
            : HrAiProposals.ToAiJson(Proposal(editing), employeeRefs, positionRefs);
        var reply = await ai.ProposeAsync(new HrAiRequest(
            isVoice ? null : text, audio, localNow, tz.Id,
            [.. employees.Select(e => new HrAiEmployeeRef(e.Key, e.Value.Name, e.Value.Position))],
            [.. positions.Select(p => new HrAiPositionRef(p.Key, p.Value.Name))],
            current), ct);
        if (reply.Json is null)
        {
            logger.LogWarning("HR AI: javob yo'q — {Error}", reply.Error);
            await telegram.SendHtmlAsync(chatId, HrAiTexts.AiFailed, ct: ct);
            return true;
        }

        var outcome = HrAiProposals.Read(reply.Json, employees, positions, localNow);
        if (outcome.Proposal is null)
        {
            // A question keeps an "editing" draft open: the answer still revises it.
            await telegram.SendHtmlAsync(chatId, HrAiTexts.Question(outcome.Question!, isVoice ? outcome.Transcript : null), ct: ct);
            return true;
        }

        var now = Now;
        var draft = new HrAiDraft
        {
            UserId = userId, ChatId = chatId,
            Source = isVoice ? HrAiSources.Voice : HrAiSources.Text,
            InputText = isVoice ? outcome.Transcript ?? "" : text,
            Proposal = JsonSerializer.Serialize(outcome.Proposal, HrAiProposals.Json),
            Status = HrAiDraftStatuses.Pending, CreatedAt = now, UpdatedAt = now,
        };
        db.HrAiDrafts.Add(draft);
        if (editing is not null)
        {
            editing.Status = HrAiDraftStatuses.Replaced;
            editing.DecidedAt = now;
            editing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        if (editing?.MessageId is { } oldCard)
            await telegram.EditMessageHtmlAsync(editing.ChatId, oldCard,
                HrAiTexts.Decided(Proposal(editing), editing.Source, HrAiTexts.ReplacedLine), ct: ct);

        draft.MessageId = await telegram.SendHtmlForIdAsync(chatId, HrAiTexts.Card(outcome.Proposal, draft.Source),
            HrAiTexts.Keyboard(draft.Id), ct);
        if (draft.MessageId is null) logger.LogWarning("HR AI: loyiha {Draft} kartasi Telegramga yetmadi", draft.Id);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("HR AI: loyiha {Draft} ({Items} ta) direktor {User} uchun", draft.Id, outcome.Proposal.Items.Count, userId);
        return true;
    }

    // =====================================================================
    //  Buttons
    // =====================================================================

    /// <summary>A <c>callback_query</c>. true — it was one of ours (<c>hai:…</c>).</summary>
    public async Task<bool> HandleCallbackAsync(JsonElement cb, CancellationToken ct = default)
    {
        var data = cb.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
        var parts = data.Split(':');
        if (parts.Length != 3 || parts[0] != "hai") return false;
        var callbackId = cb.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var fromId = cb.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var f) ? f.GetInt64() : 0;

        var draft = await db.HrAiDrafts.FirstOrDefaultAsync(x => x.Id == parts[2], ct);
        var director = await DirectorAsync(fromId, ct);
        if (draft is null || director is null || director.Value.UserId != draft.UserId)
        {
            await telegram.AnswerCallbackAsync(callbackId, HrAiTexts.NotYours, ct);
            return true;
        }
        var now = Now;
        if (HrAiDraftStatuses.Open.Contains(draft.Status) && draft.CreatedAt < now.AddHours(-HrLimits.DraftTtlHours))
        {
            await SetStatusAsync(draft, HrAiDraftStatuses.Expired, now, ct);
            await telegram.AnswerCallbackAsync(callbackId, HrAiTexts.ExpiredToast, ct);
            await EditCardAsync(draft, HrAiTexts.ExpiredLine, ct);
            return true;
        }

        switch (parts[1])
        {
            case "ok":
                await ApproveAsync(draft, director.Value.Panel, callbackId, ct);
                break;
            case "no":
                if (!await ClaimAsync(draft, HrAiDraftStatuses.Cancelled, now, ct))
                {
                    await telegram.AnswerCallbackAsync(callbackId, HrAiTexts.AlreadyDecided, ct);
                    break;
                }
                await telegram.AnswerCallbackAsync(callbackId, "Bekor qilindi", ct);
                await EditCardAsync(draft, HrAiTexts.CancelledLine, ct);
                break;
            case "ed":
                if (!await ClaimAsync(draft, HrAiDraftStatuses.Editing, now, ct))
                {
                    await telegram.AnswerCallbackAsync(callbackId, HrAiTexts.AlreadyDecided, ct);
                    break;
                }
                await telegram.AnswerCallbackAsync(callbackId, null, ct);
                // The buttons stay: the director may still approve or cancel the card as it is.
                await EditCardAsync(draft, HrAiTexts.EditingLine, ct, HrAiTexts.Keyboard(draft.Id));
                await telegram.SendHtmlAsync(draft.ChatId, HrAiTexts.EditPrompt, ct: ct);
                break;
            default:
                await telegram.AnswerCallbackAsync(callbackId, null, ct);
                break;
        }
        return true;
    }

    /// <summary>
    /// Claims the draft (pending/editing → approving) in one statement, then creates every item
    /// through <see cref="HrTasksService"/> with the director's panel. A failing item is reported;
    /// the others stay created.
    /// </summary>
    private async Task ApproveAsync(HrAiDraft draft, HrPanel panel, string callbackId, CancellationToken ct)
    {
        if (!await ClaimAsync(draft, HrAiDraftStatuses.Approving, Now, ct))
        {
            await telegram.AnswerCallbackAsync(callbackId, HrAiTexts.AlreadyDecided, ct);
            return;
        }
        await telegram.AnswerCallbackAsync(callbackId, "Yaratilmoqda…", ct);

        var proposal = Proposal(draft);
        int tasks = 0, templates = 0;
        var errors = new List<string>();
        var taskIds = new List<string>();
        var templateIds = new List<string>();
        var n = 0;
        foreach (var item in proposal.Items)
        {
            n++;
            var label = proposal.Items.Count > 1 ? $"{n}-topshiriq: " : "";
            try
            {
                if (!item.Recurring)
                {
                    var ids = item.Employees.Select(e => e.Id).ToList();
                    if (ids.Count == 1)
                    {
                        var task = await hr.CreateTaskAsync(panel, new HrTaskCreateInput(ids[0], item.Title, item.Description,
                            item.Due, item.Kind, item.TargetAmount, item.Unit, null), ct);
                        taskIds.Add(task.Id);
                        tasks++;
                    }
                    else
                    {
                        var batch = await hr.CreateTaskBatchAsync(panel, new HrTaskBatchInput(ids,
                            [new HrTaskBatchItemInput(item.Title, item.Due, item.Description, item.Kind, item.TargetAmount, item.Unit, null)]), ct);
                        taskIds.AddRange(batch.TaskIds ?? []);
                        tasks += batch.Created;
                        foreach (var skip in batch.Skipped)
                            errors.Add(label + $"{NameOf(item, skip.EmployeeId)} — {(skip.Reason == "inactive" ? "faol emas" : "topilmadi")}");
                    }
                }
                else
                {
                    var targets = new List<(string Type, string? Employee, string? Position)>();
                    if (item.Position is { } pos) targets.Add((HrTargetTypes.Position, null, pos.Id));
                    else targets.AddRange(item.Employees.Select(e => (HrTargetTypes.Employee, (string?)e.Id, (string?)null)));
                    foreach (var target in targets)
                    {
                        var template = await hr.CreateTemplateAsync(panel, new HrTemplateCreateInput(
                            item.Title, item.Description, target.Type, target.Employee, target.Position, item.Recurrence,
                            item.Weekdays, item.DaysOfMonth, item.DueTime, item.Kind, item.TargetAmount, item.Unit, null), ct);
                        templateIds.Add(template.Id);
                        templates++;
                    }
                }
            }
            catch (HrTasksException ex)
            {
                errors.Add(label + ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never leave the draft "approving" with a silent card.
                logger.LogError(ex, "HR AI: loyiha {Draft}, {Item}-band yaratilmadi", draft.Id, n);
                errors.Add(label + "kutilmagan xato — panel orqali tekshiring");
            }
        }

        var now = Now;
        draft.Status = tasks + templates > 0 ? HrAiDraftStatuses.Approved : HrAiDraftStatuses.Failed;
        draft.Result = JsonSerializer.Serialize(new { taskIds, templateIds, errors }, HrAiProposals.Json);
        var joined = string.Join("; ", errors);
        draft.Error = joined.Length == 0 ? null : joined.Length > 500 ? joined[..500] : joined;
        draft.DecidedAt = now;
        draft.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await EditCardAsync(draft, HrAiTexts.ApprovedLine(tasks, templates, errors), ct);
        logger.LogInformation("HR AI: loyiha {Draft} tasdiqlandi — topshiriq {Tasks}, takroriy {Templates}, xato {Errors}",
            draft.Id, tasks, templates, errors.Count);
    }

    private static string NameOf(HrAiProposalItem item, string employeeId) =>
        item.Employees.FirstOrDefault(e => e.Id == employeeId)?.Name ?? "Xodim";

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>The Telegram account's user, if they are a director (admin/superadmin or HR role director).</summary>
    private async Task<(string UserId, HrPanel Panel)?> DirectorAsync(long telegramUserId, CancellationToken ct)
    {
        if (telegramUserId == 0) return null;
        var userId = await db.TelegramAccounts.AsNoTracking().Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => x.UserId).FirstOrDefaultAsync(ct);
        if (userId is null) return null;
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Role, u.Permissions }).FirstOrDefaultAsync(ct);
        if (user is null) return null;
        var panel = await hr.PanelAsync(new HrActor(user.Id, user.Role, user.Permissions), ct);
        return panel.IsDirector ? (user.Id, panel) : null;
    }

    /// <summary>Active employees and positions as e1…/p1… for the model; and id → ref for a revision.</summary>
    private async Task<(Dictionary<string, HrAiPerson> Employees, Dictionary<string, HrAiPerson> Positions,
        Dictionary<string, string> EmployeeRefs, Dictionary<string, string> PositionRefs)> RefsAsync(CancellationToken ct)
    {
        var positionRows = await db.HrPositions.AsNoTracking().Where(p => p.IsActive)
            .OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var positionName = positionRows.ToDictionary(p => p.Id, p => p.Name);
        var people = await db.HrEmployees.AsNoTracking().Where(e => e.IsActive)
            .OrderBy(e => e.FullName).Select(e => new { e.Id, e.FullName, e.PositionId }).ToListAsync(ct);

        var employees = new Dictionary<string, HrAiPerson>(StringComparer.Ordinal);
        var employeeRefs = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        foreach (var p in people)
        {
            var key = $"e{++i}";
            employees[key] = new HrAiPerson(p.Id, p.FullName,
                p.PositionId is null ? null : positionName.GetValueOrDefault(p.PositionId));
            employeeRefs[p.Id] = key;
        }
        var positions = new Dictionary<string, HrAiPerson>(StringComparer.Ordinal);
        var positionRefs = new Dictionary<string, string>(StringComparer.Ordinal);
        i = 0;
        foreach (var p in positionRows)
        {
            var key = $"p{++i}";
            positions[key] = new HrAiPerson(p.Id, p.Name);
            positionRefs[p.Id] = key;
        }
        return (employees, positions, employeeRefs, positionRefs);
    }

    /// <summary>The director's draft in "O'zgartirish" mode, pressed within <see cref="EditWindow"/>.</summary>
    private async Task<HrAiDraft?> EditingDraftAsync(string userId, CancellationToken ct)
    {
        var since = Now - EditWindow;
        return await db.HrAiDrafts
            .Where(x => x.UserId == userId && x.Status == HrAiDraftStatuses.Editing && x.UpdatedAt >= since)
            .OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
    }

    /// <summary>Open drafts past their 24 hours → expired; an "editing" one past its window → back to pending.</summary>
    private async Task ExpireAsync(string userId, CancellationToken ct)
    {
        var now = Now;
        var deadline = now.AddHours(-HrLimits.DraftTtlHours);
        await db.HrAiDrafts
            .Where(x => x.UserId == userId && HrAiDraftStatuses.Open.Contains(x.Status) && x.CreatedAt < deadline)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, HrAiDraftStatuses.Expired)
                .SetProperty(x => x.DecidedAt, (DateTimeOffset?)now).SetProperty(x => x.UpdatedAt, now), ct);
    }

    /// <summary>Open (pending/editing) → <paramref name="status"/> in one statement; false — someone was first.</summary>
    private async Task<bool> ClaimAsync(HrAiDraft draft, string status, DateTimeOffset now, CancellationToken ct)
    {
        var decided = status is HrAiDraftStatuses.Editing ? (DateTimeOffset?)null : now;
        var n = await db.HrAiDrafts
            .Where(x => x.Id == draft.Id && HrAiDraftStatuses.Open.Contains(x.Status))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
                .SetProperty(x => x.DecidedAt, decided).SetProperty(x => x.UpdatedAt, now), ct);
        await db.HrAiDrafts.Entry(draft).ReloadAsync(ct);
        return n > 0;
    }

    private async Task SetStatusAsync(HrAiDraft draft, string status, DateTimeOffset now, CancellationToken ct)
    {
        draft.Status = status;
        draft.DecidedAt = now;
        draft.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task EditCardAsync(HrAiDraft draft, string outcomeLine, CancellationToken ct, object? keyboard = null)
    {
        if (draft.MessageId is not { } messageId) return;
        await telegram.EditMessageHtmlAsync(draft.ChatId, messageId,
            HrAiTexts.Decided(Proposal(draft), draft.Source, outcomeLine), keyboard, ct);
    }

    private static HrAiProposal Proposal(HrAiDraft draft) =>
        JsonSerializer.Deserialize<HrAiProposal>(draft.Proposal, HrAiProposals.Json) ?? new HrAiProposal(null, []);

    /// <summary>The rate limit: true — the call may go (and is counted).</summary>
    private bool TakeCall(string userId)
    {
        var now = Now;
        var queue = Calls.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() < now.AddHours(-1)) queue.Dequeue();
            if (queue.Count >= MaxRequestsPerHour) return false;
            queue.Enqueue(now);
            return true;
        }
    }

    /// <summary>Tests: forget the rate-limit history of a user.</summary>
    internal static void ResetCalls(string userId) => Calls.TryRemove(userId, out _);
}
