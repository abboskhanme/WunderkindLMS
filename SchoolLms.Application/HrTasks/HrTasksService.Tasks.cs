using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

/// <summary>An uploaded file, already read with a bounded buffer (max + 1 bytes).</summary>
public sealed record HrUpload(byte[] Content, string FileName, string ContentType);

public sealed partial class HrTasksService
{
    private static readonly string[] TaskSorts = ["due_at", "created_at", "completed_at", "status", "title"];

    // =====================================================================
    //  Task list (panel "Topshiriqlar" and "Mening topshiriqlarim")
    // =====================================================================

    /// <summary>
    /// Old read_models.list_tasks: a DATE WINDOW on the local date of <c>due_at</c> (not the
    /// "today list" rule); <c>overdue</c> is independent of dates; status/source are multi-valued.
    /// </summary>
    private IQueryable<HrTask> TaskQuery(HrTaskFilter f, HrScope scope, TimeZoneInfo tz, DateTimeOffset now)
    {
        var q = db.HrTasks.AsNoTracking().AsQueryable();
        if (scope.IsLimited)
        {
            var ids = scope.IdList;
            q = q.Where(t => ids.Contains(t.EmployeeId));
        }
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var term = f.Q.Trim().ToLower();
            q = q.Where(t => t.Title.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(f.EmployeeId)) q = q.Where(t => t.EmployeeId == f.EmployeeId);
        if (!string.IsNullOrWhiteSpace(f.PositionId))
            q = q.Where(t => db.HrEmployees.Any(e => e.Id == t.EmployeeId && e.PositionId == f.PositionId));
        if (!string.IsNullOrWhiteSpace(f.Branch))
            q = q.Where(t => db.HrEmployees.Any(e => e.Id == t.EmployeeId && e.Branch == f.Branch));
        if (f.Accepted is { } accepted) q = accepted ? q.Where(t => t.AcceptedAt != null) : q.Where(t => t.AcceptedAt == null);
        if (f.Statuses is { Count: > 0 } statuses)
        {
            var list = statuses.ToList();
            q = q.Where(t => list.Contains(t.Status));
        }
        if (f.Sources is { Count: > 0 } sources)
        {
            var list = sources.ToList();
            q = q.Where(t => list.Contains(t.Source));
        }
        if (f.DateFrom is { } from)
        {
            var start = HrValues.DayStart(from, tz);
            q = q.Where(t => t.DueAt >= start);
        }
        if (f.DateTo is { } to)
        {
            var end = HrValues.DayStart(to.AddDays(1), tz);
            q = q.Where(t => t.DueAt < end);
        }
        if (f.Overdue)
            q = q.Where(t => (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned) && t.DueAt < now);
        return q;
    }

    private static IOrderedQueryable<HrTask> SortTasks(IQueryable<HrTask> q, string? sort)
    {
        var (field, desc) = SortOf(sort, TaskSorts, "-due_at");
        return (field, desc) switch
        {
            ("created_at", false) => q.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
            ("created_at", true) => q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id),
            ("completed_at", false) => q.OrderBy(t => t.CompletedAt == null).ThenBy(t => t.CompletedAt).ThenBy(t => t.Id),
            ("completed_at", true) => q.OrderBy(t => t.CompletedAt == null).ThenByDescending(t => t.CompletedAt).ThenByDescending(t => t.Id),
            ("status", false) => q.OrderBy(t => t.Status).ThenBy(t => t.Id),
            ("status", true) => q.OrderByDescending(t => t.Status).ThenByDescending(t => t.Id),
            ("title", false) => q.OrderBy(t => t.Title).ThenBy(t => t.Id),
            ("title", true) => q.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id),
            (_, false) => q.OrderBy(t => t.DueAt).ThenBy(t => t.Id),
            _ => q.OrderByDescending(t => t.DueAt).ThenByDescending(t => t.Id),
        };
    }

    public async Task<HrPage<HrTaskDto>> TasksAsync(HrPanel panel, HrTaskFilter filter, HrPaging paging, CancellationToken ct = default)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var query = ApplyView(TaskQuery(filter, panel.Scope, tz, Now), panel, filter.View);
        var (rows, hasMore, next, total) = await PageAsync(SortTasks(query, filter.Sort), query, paging, ct);
        return Page(await TaskDtosAsync(rows, ct), hasMore, next, total);
    }

    /// <summary>
    /// Separates the caller's own tasks from the staff's: both views leave out tasks assigned to the caller.
    /// "given" keeps only what the caller handed out (template tasks carry no creator, so the template's counts).
    /// </summary>
    private IQueryable<HrTask> ApplyView(IQueryable<HrTask> q, HrPanel panel, string? view)
    {
        if (view is not (HrTaskViews.Staff or HrTaskViews.Given)) return q;
        if (panel.Self is { } self)
        {
            var selfId = self.Id;
            q = q.Where(t => t.EmployeeId != selfId);
        }
        if (view == HrTaskViews.Given)
        {
            var userId = panel.Actor.UserId;
            q = q.Where(t => t.CreatedBy == userId
                || (t.TemplateId != null && db.HrTaskTemplates.Any(tpl => tpl.Id == t.TemplateId && tpl.CreatedBy == userId)));
        }
        return q;
    }

    /// <summary>The employee's OWN tasks, paged ("Mening topshiriqlarim"). The id comes from the account, never the query.</summary>
    public async Task<HrPage<HrTaskDto>> MyTasksAsync(HrEmployee me, HrTaskFilter filter, HrPaging paging, CancellationToken ct = default)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var own = filter with { EmployeeId = me.Id, PositionId = null, Branch = null };
        var query = TaskQuery(own, HrScope.Unlimited, tz, Now);
        var (rows, hasMore, next, total) = await PageAsync(SortTasks(query, own.Sort), query, paging, ct);
        return Page(await TaskDtosAsync(rows, ct), hasMore, next, total);
    }

    /// <summary>Panel card: + employee/position names, proof count, first voice note.</summary>
    private async Task<List<HrTaskDto>> TaskDtosAsync(IReadOnlyList<HrTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var taskIds = tasks.Select(t => t.Id).ToList();
        var employeeIds = tasks.Select(t => t.EmployeeId).Distinct().ToList();
        var employees = await db.HrEmployees.AsNoTracking().Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName, e.PositionId }).ToDictionaryAsync(e => e.Id, ct);
        var positionIds = employees.Values.Where(e => e.PositionId != null).Select(e => e.PositionId!).Distinct().ToList();
        var positions = await db.HrPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var proofCounts = await db.HrTaskProofs.AsNoTracking().Where(p => taskIds.Contains(p.TaskId))
            .GroupBy(p => p.TaskId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var voices = (await db.HrTaskAttachments.AsNoTracking()
                .Where(a => a.TaskId != null && taskIds.Contains(a.TaskId) && a.Kind == HrAttachmentKinds.Voice)
                .Select(a => new { a.TaskId, a.FilePath, a.CreatedAt, a.Id }).ToListAsync(ct))
            .GroupBy(a => a.TaskId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id, StringComparer.Ordinal).First().FilePath);

        return [.. tasks.Select(t =>
        {
            var employee = employees.GetValueOrDefault(t.EmployeeId);
            return ToDto(t,
                employee?.FullName,
                employee?.PositionId is { } pid ? positions.GetValueOrDefault(pid) : null,
                proofCounts.GetValueOrDefault(t.Id),
                voices.TryGetValue(t.Id, out var path) ? files.UrlFor(path) : null);
        })];
    }

    private static HrTaskDto ToDto(HrTask t, string? employeeName, string? positionName, int proofCount, string? voiceUrl) => new()
    {
        Id = t.Id, EmployeeId = t.EmployeeId, TemplateId = t.TemplateId, Source = t.Source, Title = t.Title,
        Description = t.Description, Kind = t.Kind, TargetAmount = t.TargetAmount, CurrentAmount = t.CurrentAmount,
        Unit = t.Unit, ProofRequired = t.ProofRequired, OverduePolicy = t.OverduePolicy, PeriodStart = t.PeriodStart,
        DueAt = t.DueAt, Status = t.Status, IsLate = t.IsLate, EmployeeComment = t.EmployeeComment,
        DirectorComment = t.DirectorComment, CompletedAt = t.CompletedAt, ReturnedAt = t.ReturnedAt,
        AcceptedAt = t.AcceptedAt, NeedsAcceptance = t.NeedsAcceptance, VoiceUrl = voiceUrl, CreatedBy = t.CreatedBy,
        CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt, EmployeeName = employeeName, PositionName = positionName,
        ProofCount = proofCount,
    };

    private async Task<HrTaskDto> TaskDtoAsync(HrTask task, CancellationToken ct) => (await TaskDtosAsync([task], ct))[0];

    private async Task<HrTask> ScopedTaskAsync(HrScope scope, string id, CancellationToken ct)
    {
        var task = await db.HrTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null || !scope.Allows(task.EmployeeId)) throw HrErrors.TaskNotFound();
        return task;
    }

    public async Task<HrTaskDetailDto> TaskAsync(HrPanel panel, string id, CancellationToken ct = default)
    {
        var task = await ScopedTaskAsync(panel.Scope, id, ct);
        return await DetailAsync(task, ct);
    }

    private async Task<HrTaskDetailDto> DetailAsync(HrTask task, CancellationToken ct)
    {
        var dto = await TaskDtoAsync(task, ct);
        var proofs = await db.HrTaskProofs.AsNoTracking().Where(p => p.TaskId == task.Id)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
        var progress = await db.HrTaskProgress.AsNoTracking().Where(p => p.TaskId == task.Id)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
        var attachments = await db.HrTaskAttachments.AsNoTracking().Where(a => a.TaskId == task.Id)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
        return new HrTaskDetailDto
        {
            Id = dto.Id, EmployeeId = dto.EmployeeId, TemplateId = dto.TemplateId, Source = dto.Source, Title = dto.Title,
            Description = dto.Description, Kind = dto.Kind, TargetAmount = dto.TargetAmount, CurrentAmount = dto.CurrentAmount,
            Unit = dto.Unit, ProofRequired = dto.ProofRequired, OverduePolicy = dto.OverduePolicy, PeriodStart = dto.PeriodStart,
            DueAt = dto.DueAt, Status = dto.Status, IsLate = dto.IsLate, EmployeeComment = dto.EmployeeComment,
            DirectorComment = dto.DirectorComment, CompletedAt = dto.CompletedAt, ReturnedAt = dto.ReturnedAt,
            AcceptedAt = dto.AcceptedAt, NeedsAcceptance = dto.NeedsAcceptance, VoiceUrl = dto.VoiceUrl,
            CreatedBy = dto.CreatedBy, CreatedAt = dto.CreatedAt, UpdatedAt = dto.UpdatedAt, EmployeeName = dto.EmployeeName,
            PositionName = dto.PositionName, ProofCount = dto.ProofCount,
            Proofs = [.. proofs.Select(p => new HrTaskProofDto(p.Id, p.TaskId, p.Kind, p.FilePath, p.OriginalName,
                p.ContentType, p.SizeBytes, ProofUrl(p), p.Text, p.CreatedAt))],
            Progress = [.. progress.Select(p => new HrTaskProgressDto(p.Id, p.TaskId, p.Amount, p.Note, p.CreatedAt))],
            Attachments = [.. attachments.Select(ToDto)],
        };
    }

    private string? ProofUrl(HrTaskProof p) =>
        !string.IsNullOrEmpty(p.Url) ? p.Url : !string.IsNullOrEmpty(p.FilePath) ? files.UrlFor(p.FilePath) : null;

    private HrTaskAttachmentDto ToDto(HrTaskAttachment a) => new(
        a.Id, a.Kind, files.UrlFor(a.FilePath), a.ContentType, a.SizeBytes, a.DurationSeconds, a.TaskId, a.OriginalName, a.CreatedAt);

    // =====================================================================
    //  Create (director / manager) — the employee is told IMMEDIATELY (old §4)
    // =====================================================================

    private HrTask NewDirectorTask(string employeeId, string? title, string? description, DateTimeOffset dueAt, string? kind,
        decimal? targetAmount, string? unit, bool proofRequired, string actorId, TimeZoneInfo tz, DateTimeOffset now)
    {
        var cleanTitle = HrValues.RequireText(title, "Sarlavha", HrLimits.TitleMax);
        if (cleanTitle.Length < 2) throw HrErrors.Validation("Sarlavha kamida 2 belgi bo'lishi kerak");
        var k = string.IsNullOrWhiteSpace(kind) ? HrTaskKinds.Check : kind;
        if (!HrTaskKinds.IsValid(k)) throw HrErrors.Validation($"kind noto'g'ri: '{kind}'");
        var (target, cleanUnit) = HrValues.KindAmount(k, targetAmount, unit);
        return new HrTask
        {
            EmployeeId = employeeId,
            Source = HrTaskSources.Director,
            Title = cleanTitle,
            Description = HrValues.CleanText(description, "Tavsif", HrLimits.DescriptionMax),
            Kind = k,
            TargetAmount = target,
            Unit = cleanUnit,
            ProofRequired = proofRequired,
            OverduePolicy = HrOverduePolicies.KeepOpen,
            PeriodStart = HrValues.LocalDate(dueAt, tz),
            DueAt = dueAt,
            Status = HrTaskStatuses.Open,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public async Task<HrTaskDto> CreateTaskAsync(HrPanel panel, HrTaskCreateInput input, CancellationToken ct = default)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var employee = await ScopedEmployeeAsync(panel.Scope, input.EmployeeId ?? "", ct);
        if (!employee.IsActive) throw HrErrors.EmployeeInactive(); // scope first (404), then activity (409)
        var now = Now;
        var task = NewDirectorTask(employee.Id, input.Title, input.Description, ParseInstant(input.DueAt, "dueAt", tz),
            input.Kind, input.TargetAmount, input.Unit, input.ProofRequired ?? true, panel.Actor.UserId, tz, now);
        db.HrTasks.Add(task);
        await db.SaveChangesAsync(ct);
        await SendToUserAsync(employee.UserId, HrTasksTexts.Assigned(task.Title, task.DueAt, tz), ct);
        return await TaskDtoAsync(task, ct);
    }

    /// <summary>
    /// "6 employees × 3 tasks = 18", one transaction. Inactive / out-of-scope employees are skipped;
    /// ONE Telegram message per employee (30 × 3 = 30 messages, not 90) + the voice note.
    /// </summary>
    public async Task<HrBatchResultDto> CreateTaskBatchAsync(HrPanel panel, HrTaskBatchInput input, CancellationToken ct = default)
    {
        var employeeIds = (input.EmployeeIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var items = input.Items ?? [];
        EnsureBatchLimits(employeeIds.Count, items.Count);
        if (employeeIds.Count == 0 || items.Count == 0) return new HrBatchResultDto(0, [], [], null);

        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var now = Now;
        var parsed = items.Select(i => (Item: i, DueAt: ParseInstant(i.DueAt, "dueAt", tz))).ToList();

        var draftIds = items.Where(i => !string.IsNullOrWhiteSpace(i.AttachmentId)).Select(i => i.AttachmentId!).Distinct().ToList();
        var drafts = await db.HrTaskAttachments.Where(a => draftIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var id in draftIds)
            if (!drafts.TryGetValue(id, out var draft) || draft.TaskId is not null) throw HrErrors.AttachmentNotFound();

        var employees = await db.HrEmployees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var skipped = new List<HrBatchSkippedDto>();
        var perEmployee = new List<(HrEmployee Employee, List<HrTask> Tasks)>();
        foreach (var employeeId in employeeIds)
        {
            if (!employees.TryGetValue(employeeId, out var employee) || !panel.Scope.Allows(employeeId))
            { skipped.Add(new HrBatchSkippedDto(employeeId, "not_found")); continue; }
            if (!employee.IsActive) { skipped.Add(new HrBatchSkippedDto(employeeId, "inactive")); continue; }
            var batch = parsed.Select(p => NewDirectorTask(employee.Id, p.Item.Title, p.Item.Description, p.DueAt,
                p.Item.Kind, p.Item.TargetAmount, p.Item.Unit, true, panel.Actor.UserId, tz, now)).ToList();
            perEmployee.Add((employee, batch));
        }
        var created = perEmployee.SelectMany(p => p.Tasks).ToList();
        if (created.Count == 0) return new HrBatchResultDto(0, skipped, [], null);

        db.HrTasks.AddRange(created);
        // ONE file, a row per task: the draft is copied onto each task and then removed
        foreach (var (_, batch) in perEmployee)
            for (var i = 0; i < items.Count; i++)
                if (!string.IsNullOrWhiteSpace(items[i].AttachmentId))
                {
                    var draft = drafts[items[i].AttachmentId!];
                    db.HrTaskAttachments.Add(new HrTaskAttachment
                    {
                        TaskId = batch[i].Id, Kind = draft.Kind, FilePath = draft.FilePath, OriginalName = draft.OriginalName,
                        ContentType = draft.ContentType, SizeBytes = draft.SizeBytes, DurationSeconds = draft.DurationSeconds,
                        CreatedBy = draft.CreatedBy, CreatedAt = now,
                    });
                }
        foreach (var draft in drafts.Values) db.HrTaskAttachments.Remove(draft);
        await db.SaveChangesAsync(ct);

        var firstVoice = items.Where(i => !string.IsNullOrWhiteSpace(i.AttachmentId))
            .Select(i => drafts[i.AttachmentId!]).FirstOrDefault(d => d.Kind == HrAttachmentKinds.Voice);
        (byte[], string, string)? voice = null;
        try
        {
            if (firstVoice is not null && telegram.IsConfigured && await files.ReadAsync(firstVoice.FilePath, ct) is { } bytes)
                voice = (bytes, Path.GetFileName(firstVoice.FilePath), firstVoice.ContentType);
        }
        catch (IOException ex)
        {
            // the tasks are saved; a missing voice file only drops the voice message
            logger.LogWarning(ex, "HR: ovozli izoh fayli o'qilmadi {Path}", firstVoice?.FilePath);
        }
        foreach (var (employee, batch) in perEmployee)
            await SendToUserAsync(employee.UserId, HrTasksTexts.AssignedMany(batch.Count, [.. batch.Select(t => t.Title)]), ct, voice);

        return new HrBatchResultDto(created.Count, skipped, [.. created.Select(t => t.Id)], null);
    }

    /// <summary>PATCH: title / description / dueAt (/ proofRequired). A new due time restarts reminders.</summary>
    public async Task<HrTaskDto> UpdateTaskAsync(HrPanel panel, string id, JsonElement body, CancellationToken ct = default)
    {
        var task = await ScopedTaskAsync(panel.Scope, id, ct);
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var now = Now;
        var title = Has(body, "title") ? Str(body, "title") : null;
        if (title is not null && title.Trim().Length < 2) throw HrErrors.Validation("Sarlavha kamida 2 belgi bo'lishi kerak");
        DateTimeOffset? dueAt = Has(body, "dueAt") && Str(body, "dueAt") is { } rawDue ? ParseInstant(rawDue, "dueAt", tz) : null;
        var previousDue = task.DueAt;
        var changed = task.UpdateDetails(now, title, Has(body, "description"), Str(body, "description"), dueAt);
        if (Bool(body, "proofRequired") is { } proof && proof != task.ProofRequired)
        {
            task.ProofRequired = proof;
            task.UpdatedAt = now;
            changed = true;
        }
        if (changed)
        {
            if (task.DueAt != previousDue) await ForgetRemindersAsync(task.Id, ct);
            await db.SaveChangesAsync(ct);
        }
        return await TaskDtoAsync(task, ct);
    }

    private async Task ForgetRemindersAsync(string taskId, CancellationToken ct)
    {
        var sent = await db.HrTaskReminders.Where(r => r.TaskId == taskId).ToListAsync(ct);
        foreach (var r in sent) db.HrTaskReminders.Remove(r);
    }

    /// <summary>Return a DONE task; the comment reaches the employee IMMEDIATELY.</summary>
    public async Task<HrTaskDto> ReturnTaskAsync(HrPanel panel, string id, string? comment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comment)) throw HrErrors.Validation("Qaytarish izohi kerak");
        if (comment.Length > 2000) throw HrErrors.Validation("Izoh 2000 belgidan oshmasligi kerak");
        var task = await ScopedTaskAsync(panel.Scope, id, ct);
        if (task.MarkReturned(Now, comment))
        {
            await db.SaveChangesAsync(ct);
            var userId = await db.HrEmployees.Where(e => e.Id == task.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
            if (userId is not null) await SendToUserAsync(userId, HrTasksTexts.Returned(task.Title, task.DirectorComment), ct);
        }
        return await TaskDtoAsync(task, ct);
    }

    public async Task<HrTaskDto> CancelTaskAsync(HrPanel panel, string id, string? comment, CancellationToken ct = default)
    {
        if (comment is { Length: > 2000 }) throw HrErrors.Validation("Izoh 2000 belgidan oshmasligi kerak");
        var task = await ScopedTaskAsync(panel.Scope, id, ct);
        if (task.Cancel(Now, comment)) await db.SaveChangesAsync(ct);
        return await TaskDtoAsync(task, ct);
    }

    // =====================================================================
    //  Attachments (a manager's voice note / file) — uploaded as a DRAFT first
    // =====================================================================

    private static string ExtensionOf(HrUpload upload)
    {
        var ext = Path.GetExtension(upload.FileName ?? "");
        if (!string.IsNullOrEmpty(ext)) return ext;
        return (upload.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "audio/webm" or "video/webm" => ".webm",
            "audio/ogg" or "audio/opus" => ".ogg",
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" or "audio/aac" => ".m4a",
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/wav" or "audio/x-wav" => ".wav",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            "application/pdf" => ".pdf",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            _ => ".bin",
        };
    }

    /// <summary>Only browser-executable types are refused (by MIME and by extension) — everything else is a valid file.</summary>
    private static bool IsBlocked(HrUpload upload) =>
        HrLimits.BlockedContentTypes.Contains((upload.ContentType ?? "").Split(';')[0].Trim())
        || HrLimits.BlockedExtensions.Contains(Path.GetExtension(upload.FileName ?? ""));

    public async Task<HrTaskAttachmentDto> UploadAttachmentAsync(
        HrPanel panel, HrUpload upload, string? kind, int? durationSeconds, CancellationToken ct = default)
    {
        var k = string.IsNullOrWhiteSpace(kind) ? HrAttachmentKinds.Voice : kind;
        if (!HrAttachmentKinds.IsValid(k)) throw HrErrors.Validation($"kind noto'g'ri: '{kind}'");
        if (upload.Content.Length > HrLimits.MaxAttachmentBytes) throw HrErrors.AttachmentTooLarge(HrLimits.MaxAttachmentBytes);
        if (upload.Content.Length == 0) throw HrErrors.Validation("Fayl bo'sh");
        if (IsBlocked(upload)) throw HrErrors.UnsupportedAttachmentType();
        // no ffprobe on our server — the client reports the recorded length (MediaRecorder knows it)
        if (durationSeconds is < 0) throw HrErrors.Validation("Ovoz davomiyligi manfiy bo'lishi mumkin emas");
        if (k == HrAttachmentKinds.Voice && durationSeconds > HrLimits.MaxVoiceSeconds)
            throw HrErrors.Validation($"Ovozli izoh {HrLimits.MaxVoiceSeconds / 60} daqiqadan uzun bo'lmasligi kerak");

        var now = Now;
        var path = await files.SaveAsync("attachments", upload.Content, ExtensionOf(upload), now, ct);
        var attachment = new HrTaskAttachment
        {
            TaskId = null,
            Kind = k,
            FilePath = path,
            OriginalName = HrValues.CleanText(Truncate(upload.FileName, 255), "Fayl nomi", 255),
            ContentType = Truncate((upload.ContentType ?? "").Split(';')[0].Trim(), 120) is { Length: > 0 } type ? type : "application/octet-stream",
            SizeBytes = upload.Content.Length,
            DurationSeconds = durationSeconds,
            CreatedBy = panel.Actor.UserId,
            CreatedAt = now,
        };
        db.HrTaskAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return ToDto(attachment);
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    /// <summary>The FILE is removed only with a draft — a bound file may be shared by N tasks.</summary>
    public async Task DeleteAttachmentAsync(HrPanel panel, string id, CancellationToken ct = default)
    {
        var attachment = await db.HrTaskAttachments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw HrErrors.AttachmentNotFound();
        if (panel.Scope.IsLimited && attachment.TaskId is { } taskId)
        {
            var owner = await db.HrTasks.Where(t => t.Id == taskId).Select(t => t.EmployeeId).FirstOrDefaultAsync(ct);
            if (owner is null || !panel.Scope.Allows(owner)) throw HrErrors.AttachmentNotFound();
        }
        var isDraft = attachment.TaskId is null;
        db.HrTaskAttachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        if (isDraft) files.Delete(attachment.FilePath);
    }

    // =====================================================================
    //  Reports (default period: first of the current month → today, local)
    // =====================================================================

    private async Task<(DateOnly From, DateOnly To, TimeZoneInfo Tz)> RangeAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var end = to ?? HrValues.LocalDate(Now, tz);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        return (start, end, tz);
    }

    private static double Percent(int part, int whole) => whole > 0 ? Math.Round(part * 100.0 / whole, 1, MidpointRounding.ToEven) : 0.0;

    public async Task<HrSummaryReportDto> SummaryAsync(HrScope scope, DateOnly? from, DateOnly? to, string? branch, CancellationToken ct = default)
    {
        var (start, end, tz) = await RangeAsync(from, to, ct);
        return await SummaryCoreAsync(scope.EmployeeIds, start, end, branch, tz, Now, ct);
    }

    private async Task<HrSummaryReportDto> SummaryCoreAsync(
        IReadOnlySet<string>? scopeIds, DateOnly from, DateOnly to, string? branch, TimeZoneInfo tz, DateTimeOffset now, CancellationToken ct)
    {
        var startAt = HrValues.DayStart(from, tz);
        var endAt = HrValues.DayStart(to.AddDays(1), tz);
        var q = db.HrTasks.AsNoTracking().Where(t => t.DueAt >= startAt && t.DueAt < endAt);
        if (!string.IsNullOrWhiteSpace(branch)) q = q.Where(t => db.HrEmployees.Any(e => e.Id == t.EmployeeId && e.Branch == branch));
        if (scopeIds is not null)
        {
            var ids = scopeIds.ToList();
            q = q.Where(t => ids.Contains(t.EmployeeId));
        }
        var rows = await q.Select(t => new { t.EmployeeId, t.Status, t.IsLate, t.Source, t.AcceptedAt, t.DueAt }).ToListAsync(ct);
        var total = rows.Count;
        var done = rows.Count(r => r.Status == HrTaskStatuses.Done);
        return new HrSummaryReportDto(
            from, to, total, done,
            rows.Count(r => r.Status == HrTaskStatuses.Missed),
            rows.Count(r => r.Status == HrTaskStatuses.Open),
            rows.Count(r => r.Status == HrTaskStatuses.Returned),
            rows.Count(r => r.Status == HrTaskStatuses.Cancelled),
            rows.Count(r => r.IsLate),
            rows.Count(r => HrTaskStatuses.IsActionable(r.Status) && r.DueAt < now),
            rows.Count(r => r.Source == HrTaskSources.Self),
            rows.Select(r => r.EmployeeId).Distinct().Count(),
            Percent(done, total),
            rows.Count(r => r.Source == HrTaskSources.Director && r.AcceptedAt == null && HrTaskStatuses.IsActionable(r.Status)));
    }

    /// <summary>Per employee (active ones, or anyone with tasks in the period); KPI = sums over numeric tasks.</summary>
    public async Task<HrEmployeeReportDto> EmployeeReportAsync(HrScope scope, DateOnly? from, DateOnly? to, string? branch, CancellationToken ct = default)
    {
        var (start, end, tz) = await RangeAsync(from, to, ct);
        var startAt = HrValues.DayStart(start, tz);
        var endAt = HrValues.DayStart(end.AddDays(1), tz);
        var employeesQ = db.HrEmployees.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(branch)) employeesQ = employeesQ.Where(e => e.Branch == branch);
        if (scope.IsLimited)
        {
            var ids = scope.IdList;
            employeesQ = employeesQ.Where(e => ids.Contains(e.Id));
        }
        var employees = await employeesQ.OrderBy(e => e.FullName).ThenBy(e => e.Id).ToListAsync(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var tasks = (await db.HrTasks.AsNoTracking()
                .Where(t => employeeIds.Contains(t.EmployeeId) && t.DueAt >= startAt && t.DueAt < endAt)
                .Select(t => new { t.EmployeeId, t.Status, t.IsLate, t.Kind, t.TargetAmount, t.CurrentAmount }).ToListAsync(ct))
            .GroupBy(t => t.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var positionIds = employees.Where(e => e.PositionId != null).Select(e => e.PositionId!).Distinct().ToList();
        var positions = await db.HrPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var items = new List<HrEmployeeReportRowDto>();
        foreach (var e in employees)
        {
            var mine = tasks.GetValueOrDefault(e.Id) ?? [];
            if (!e.IsActive && mine.Count == 0) continue;
            var numeric = mine.Where(t => t.Kind == HrTaskKinds.Numeric).ToList();
            decimal? kpiTarget = numeric.Count > 0 ? numeric.Sum(t => t.TargetAmount ?? 0) : null;
            decimal? kpiCurrent = numeric.Count > 0 ? numeric.Sum(t => t.CurrentAmount) : null;
            double? kpiPercent = kpiTarget is { } target && target != 0
                ? Math.Round((double)(kpiCurrent ?? 0) * 100 / (double)target, 1, MidpointRounding.ToEven)
                : null;
            var done = mine.Count(t => t.Status == HrTaskStatuses.Done);
            items.Add(new HrEmployeeReportRowDto(
                e.Id, e.FullName, e.PositionId is null ? null : positions.GetValueOrDefault(e.PositionId), mine.Count, done,
                mine.Count(t => t.Status == HrTaskStatuses.Missed), mine.Count(t => t.Status == HrTaskStatuses.Open),
                mine.Count(t => t.Status == HrTaskStatuses.Returned), mine.Count(t => t.IsLate), Percent(done, mine.Count),
                kpiTarget, kpiCurrent, kpiPercent) { Branch = e.Branch });
        }
        return new HrEmployeeReportDto(start, end, items);
    }

    public static readonly string[] ReportHeaders =
    [
        "Xodim", "Lavozim", "Bo'linma", "Jami", "Bajarildi", "Bajarilmadi", "Ochiq", "Qaytarilgan",
        "Kechikkan", "Bajarilish, %", "KPI maqsad", "KPI joriy", "KPI, %",
    ];

    private static string BranchLabel(string? branch) => branch switch
    {
        HrBranches.Center => "Markaz",
        HrBranches.School => "Maktab",
        _ => "",
    };

    public static List<string[]> ReportRows(HrEmployeeReportDto report) =>
    [
        .. report.Items.Select(r => new[]
        {
            r.FullName, r.PositionName ?? "", BranchLabel(r.Branch), r.TotalTasks.ToString(CultureInfo.InvariantCulture),
            r.DoneCount.ToString(CultureInfo.InvariantCulture), r.MissedCount.ToString(CultureInfo.InvariantCulture),
            r.OpenCount.ToString(CultureInfo.InvariantCulture), r.ReturnedCount.ToString(CultureInfo.InvariantCulture),
            r.LateCount.ToString(CultureInfo.InvariantCulture), r.DonePercent.ToString("0.0", CultureInfo.InvariantCulture),
            r.KpiTarget is { } kt ? HrTasksTexts.Num(kt) : "", r.KpiCurrent is { } kc ? HrTasksTexts.Num(kc) : "",
            r.KpiPercent is { } kp ? kp.ToString("0.0", CultureInfo.InvariantCulture) : "",
        }),
    ];

    /// <summary>The old <c>export.csv</c>: <c>;</c>-separated, CRLF, UTF-8 BOM (Excel).</summary>
    public static byte[] ReportCsv(HrEmployeeReportDto report)
    {
        static string Cell(string v) => v.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.Append(string.Join(";", ReportHeaders.Select(Cell))).Append("\r\n");
        foreach (var row in ReportRows(report)) sb.Append(string.Join(";", row.Select(Cell))).Append("\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Dashboard: today's numbers, the employee table, overdue tasks, today's self-created tasks.</summary>
    public async Task<HrDashboardDto> DashboardAsync(HrPanel panel, CancellationToken ct = default)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var now = Now;
        var today = HrValues.LocalDate(now, tz);
        var summary = await SummaryCoreAsync(panel.Scope.EmployeeIds, today, today, null, tz, now, ct);
        var employees = await EmployeeReportAsync(panel.Scope, today, today, null, ct);
        var overdueQ = TaskQuery(new HrTaskFilter(Overdue: true), panel.Scope, tz, now);
        var overdueTotal = await overdueQ.CountAsync(ct);
        var overdue = await overdueQ.OrderBy(t => t.DueAt).ThenBy(t => t.Id).Take(20).ToListAsync(ct);
        var dayStart = HrValues.DayStart(today, tz);
        var dayEnd = HrValues.DayStart(today.AddDays(1), tz);
        var selfQ = TaskQuery(new HrTaskFilter(Sources: [HrTaskSources.Self]), panel.Scope, tz, now)
            .Where(t => t.CreatedAt >= dayStart && t.CreatedAt < dayEnd);
        var self = await selfQ.OrderByDescending(t => t.CreatedAt).Take(20).ToListAsync(ct);
        return new HrDashboardDto(today, summary, employees, await TaskDtosAsync(overdue, ct), overdueTotal, await TaskDtosAsync(self, ct));
    }

    // =====================================================================
    //  The employee (/api/hr/me — Mini App + "Mening topshiriqlarim")
    // =====================================================================

    /// <summary>The caller's ACTIVE HR row, or 403 <c>hr_not_employee</c>.</summary>
    public async Task<HrEmployee> MeAsync(string userId, CancellationToken ct = default) =>
        await db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId && e.IsActive, ct)
        ?? throw HrErrors.NotEmployee();

    public async Task<HrMeDto> ProfileAsync(HrEmployee me, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        var position = me.PositionId is null ? null
            : await db.HrPositions.AsNoTracking().Where(p => p.Id == me.PositionId).Select(p => p.Name).FirstOrDefaultAsync(ct);
        return new HrMeDto(me.Id, me.FullName, position, settings.Timezone, HrValues.LocalDate(Now, settings.Zone));
    }

    /// <summary>
    /// The day list (old §15.1): open (<c>period_start ≤ D</c>) + closed that day; cancelled hidden.
    /// Order: overdue first, then due today, then the rest; by due time inside.
    /// </summary>
    public async Task<HrTaskListDto> DayAsync(HrEmployee me, DateOnly? date, CancellationToken ct = default)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var now = Now;
        var day = date ?? HrValues.LocalDate(now, tz);
        var start = HrValues.DayStart(day, tz);
        var end = HrValues.DayStart(day.AddDays(1), tz);
        var tasks = await db.HrTasks.AsNoTracking()
            .Where(t => t.EmployeeId == me.Id && t.Status != HrTaskStatuses.Cancelled
                && (((t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned) && t.PeriodStart <= day)
                    || (t.CompletedAt >= start && t.CompletedAt < end)
                    || (t.Status == HrTaskStatuses.Missed && t.DueAt >= start && t.DueAt < end)))
            .OrderBy(t => t.DueAt < start ? 0 : t.DueAt < end ? 1 : 2).ThenBy(t => t.DueAt).ThenBy(t => t.Id)
            .ToListAsync(ct);
        return new HrTaskListDto(day, await AppTasksAsync(tasks, now, ct));
    }

    private async Task<List<HrAppTaskDto>> AppTasksAsync(IReadOnlyList<HrTask> tasks, DateTimeOffset now, CancellationToken ct)
    {
        var ids = tasks.Select(t => t.Id).ToList();
        var proofs = (await db.HrTaskProofs.AsNoTracking().Where(p => ids.Contains(p.TaskId))
                .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct))
            .GroupBy(p => p.TaskId).ToDictionary(g => g.Key, g => g.ToList());
        var attachments = (await db.HrTaskAttachments.AsNoTracking().Where(a => a.TaskId != null && ids.Contains(a.TaskId))
                .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct))
            .GroupBy(a => a.TaskId!).ToDictionary(g => g.Key, g => g.ToList());
        return [.. tasks.Select(t =>
        {
            var mine = attachments.GetValueOrDefault(t.Id) ?? [];
            var voice = mine.FirstOrDefault(a => a.Kind == HrAttachmentKinds.Voice);
            return new HrAppTaskDto(
                t.Id, t.Title, t.Description, t.Kind, t.Status, t.Source, t.DueAt, t.IsOverdue(now), t.IsLate,
                t.ProofRequired, t.TargetAmount, t.Kind == HrTaskKinds.Numeric ? t.CurrentAmount : null, t.Unit,
                t.EmployeeComment, t.DirectorComment, t.CompletedAt,
                [.. (proofs.GetValueOrDefault(t.Id) ?? []).Select(p => new HrAppProofDto(p.Id, p.Kind, ProofUrl(p), p.OriginalName))],
                t.AcceptedAt, t.NeedsAcceptance,
                [.. mine.Select(a => new HrAppAttachmentDto(a.Id, a.Kind, files.UrlFor(a.FilePath), a.ContentType, a.SizeBytes, a.DurationSeconds))],
                voice is null ? null : files.UrlFor(voice.FilePath));
        })];
    }

    private async Task<HrAppTaskDto> AppTaskAsync(HrTask task, CancellationToken ct) => (await AppTasksAsync([task], Now, ct))[0];

    /// <summary>Own task or 403 <c>hr_task_not_owned</c> (old ensure_owner); unknown id 404.</summary>
    private async Task<HrTask> OwnTaskAsync(HrEmployee me, string id, CancellationToken ct)
    {
        var task = await db.HrTasks.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw HrErrors.TaskNotFound();
        if (task.EmployeeId != me.Id) throw HrErrors.TaskNotOwned();
        return task;
    }

    public async Task<HrAppTaskDto> MyTaskAsync(HrEmployee me, string id, CancellationToken ct = default) =>
        await AppTaskAsync(await OwnTaskAsync(me, id, ct), ct);

    /// <summary><c>source = self</c>; shown in the evening digest's "O'zi qo'shgan" line.</summary>
    public async Task<HrAppTaskDto> CreateSelfTaskAsync(HrEmployee me, HrSelfTaskInput input, CancellationToken ct = default)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var title = HrValues.RequireText(input.Title, "Sarlavha", HrLimits.TitleMax);
        if (title.Length < 2) throw HrErrors.Validation("Sarlavha kamida 2 belgi bo'lishi kerak");
        var dueAt = ParseInstant(input.DueAt, "dueAt", tz);
        var now = Now;
        var task = new HrTask
        {
            EmployeeId = me.Id,
            Source = HrTaskSources.Self,
            Title = title,
            Description = HrValues.CleanText(input.Description, "Tavsif", HrLimits.DescriptionMax),
            Kind = HrTaskKinds.Check,
            ProofRequired = true,
            OverduePolicy = HrOverduePolicies.KeepOpen,
            PeriodStart = HrValues.LocalDate(dueAt, tz),
            DueAt = dueAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.HrTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return await AppTaskAsync(task, ct);
    }

    /// <summary>"Bajardim": the proof count is read from the DATABASE, never from <c>proofIds</c>.</summary>
    public async Task<HrAppTaskDto> CompleteAsync(HrEmployee me, string id, HrCompleteInput? input, CancellationToken ct = default)
    {
        var comment = input?.Comment;
        if (comment is { Length: > 2000 }) throw HrErrors.Validation("Izoh 2000 belgidan oshmasligi kerak");
        var task = await OwnTaskAsync(me, id, ct);
        var proofs = await db.HrTaskProofs.CountAsync(p => p.TaskId == task.Id, ct);
        if (task.Complete(Now, comment, proofs)) await db.SaveChangesAsync(ct);
        return await AppTaskAsync(task, ct);
    }

    /// <summary>
    /// Undo a mistaken "done" — ONLY the same local day and before today's digest ran
    /// (<c>hr_job_runs</c> has no <c>digest</c> row for today). Later → 409.
    /// </summary>
    public async Task<HrAppTaskDto> ReopenAsync(HrEmployee me, string id, CancellationToken ct = default)
    {
        var task = await OwnTaskAsync(me, id, ct);
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var now = Now;
        var today = HrValues.LocalDate(now, tz);
        if (task.CompletedAt is not { } completedAt) throw HrErrors.TaskNotEditable("reopen");
        if (HrValues.LocalDate(completedAt, tz) != today) throw HrErrors.TaskNotEditable("reopen_window");
        if (await db.HrJobRuns.AnyAsync(r => r.JobName == HrJobs.Digest && r.RunDate == today, ct))
            throw HrErrors.TaskNotEditable("digest_sent");
        if (task.Reopen(now)) await db.SaveChangesAsync(ct);
        return await AppTaskAsync(task, ct);
    }

    /// <summary>"Ko'rdim" — not a status; the second call keeps the first time.</summary>
    public async Task<HrAppTaskDto> AcceptAsync(HrEmployee me, string id, CancellationToken ct = default)
    {
        var task = await OwnTaskAsync(me, id, ct);
        if (task.Accept(Now)) await db.SaveChangesAsync(ct);
        return await AppTaskAsync(task, ct);
    }

    /// <summary>A KPI step: the progress row and <c>current_amount</c> in ONE SaveChanges; target reached → done.</summary>
    public async Task<HrAppTaskDto> AddProgressAsync(HrEmployee me, string id, HrProgressInput input, CancellationToken ct = default)
    {
        if (input.Amount is not { } amount) throw HrErrors.InvalidAmount("Miqdor kiritilmagan");
        var note = HrValues.CleanText(input.Note, "Izoh", HrLimits.ProgressNoteMax);
        var task = await OwnTaskAsync(me, id, ct);
        var now = Now;
        task.AddAmount(now, amount);
        db.HrTaskProgress.Add(new HrTaskProgress { TaskId = task.Id, Amount = HrValues.StepAmount(amount), Note = note, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return await AppTaskAsync(task, ct);
    }

    /// <summary>A proof file: ≤ 10 MB, ≤ 5 per task, any type except browser-executable ones.</summary>
    public async Task<HrUploadedProofDto> UploadProofAsync(HrEmployee me, string taskId, HrUpload upload, CancellationToken ct = default)
    {
        var task = await OwnTaskAsync(me, taskId, ct);
        if (task.IsFinal) throw HrErrors.TaskNotEditable("proof");
        if (IsBlocked(upload)) throw HrErrors.UnsupportedProofType();
        if (upload.Content.Length > HrLimits.MaxProofBytes) throw HrErrors.ProofTooLarge(HrLimits.MaxProofBytes);
        if (upload.Content.Length == 0) throw HrErrors.Validation("Fayl bo'sh");
        var current = await db.HrTaskProofs.CountAsync(p => p.TaskId == task.Id, ct);
        if (current >= HrLimits.MaxProofsPerTask) throw HrErrors.ProofLimitExceeded(HrLimits.MaxProofsPerTask);

        var now = Now;
        var contentType = (upload.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        var path = await files.SaveAsync("proofs", upload.Content, ExtensionOf(upload), now, ct);
        var proof = new HrTaskProof
        {
            TaskId = task.Id,
            Kind = contentType.StartsWith("image/", StringComparison.Ordinal) ? HrProofKinds.Photo : HrProofKinds.File,
            FilePath = path,
            OriginalName = HrValues.CleanText(Truncate(upload.FileName, 200), "Fayl nomi", 200),
            ContentType = contentType.Length == 0 ? "application/octet-stream" : Truncate(contentType, 100),
            SizeBytes = upload.Content.Length,
            CreatedAt = now,
        };
        db.HrTaskProofs.Add(proof);
        await db.SaveChangesAsync(ct);
        return new HrUploadedProofDto(proof.Id, files.UrlFor(path));
    }

    /// <summary>"Natijam" — the current month; KPI rows from its numeric tasks (cancelled excluded).</summary>
    public async Task<HrAppSummaryDto> MySummaryAsync(HrEmployee me, CancellationToken ct = default)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var end = HrValues.LocalDate(Now, tz);
        var start = new DateOnly(end.Year, end.Month, 1);
        var startAt = HrValues.DayStart(start, tz);
        var endAt = HrValues.DayStart(end.AddDays(1), tz);
        var tasks = await db.HrTasks.AsNoTracking()
            .Where(t => t.EmployeeId == me.Id && t.DueAt >= startAt && t.DueAt < endAt)
            .OrderBy(t => t.DueAt).ThenBy(t => t.Id).ToListAsync(ct);
        var total = tasks.Count;
        var done = tasks.Count(t => t.Status == HrTaskStatuses.Done);
        var kpi = tasks.Where(t => t.Kind == HrTaskKinds.Numeric && t.Status != HrTaskStatuses.Cancelled)
            .Take(HrLimits.MaxLimit)
            .Select(t => new HrSummaryKpiDto(t.Id, t.Title, t.CurrentAmount, t.TargetAmount, t.Unit)).ToList();
        return new HrAppSummaryDto(start, end, total, done,
            tasks.Count(t => t.Status == HrTaskStatuses.Missed), tasks.Count(t => HrTaskStatuses.IsActionable(t.Status)),
            tasks.Count(t => t.IsLate), Percent(done, total), kpi);
    }
}
