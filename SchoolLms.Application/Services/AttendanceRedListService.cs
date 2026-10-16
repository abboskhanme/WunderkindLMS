using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Attendance red list (client, 2026-10-15: "Qizil ro'yxat va ketma-ket dars qoldirish nazorati";
//  docs/modules/attendance-red-list.md).
//
//  This file: settings, lists, case detail and actions — what the admin page and the teacher's
//  "Sinfim · Qizil ro'yxat" read and write. The daily scan that opens cases is in
//  AttendanceRedListService.Scan.cs, the Telegram notices in AttendanceRedListService.Notify.cs,
//  the rules themselves in RedListCriteria.
//
//  Who sees what: staff read every case (the admin gate's read rule), only `attendanceCases`
//  writes; a teacher sees and works only the open cases of the pupils of the classes they lead
//  and the track groups they teach (TeacherScopeAsync) — anything else is "not found".
// ===========================================================================

/// <summary>A rule broken by a request — HTTP status and an Uzbek message for the screen.</summary>
public sealed class AttendanceCaseException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Who writes an action: users.id (from the token), the name to show and teacher | staff.</summary>
public sealed record RedListActor(string UserId, string Name, string Type);

/// <summary>Red-list cases, their actions and the criteria. File header.</summary>
public sealed partial class AttendanceRedListService(
    IAppDbContext db, TelegramService telegram, ILogger<AttendanceRedListService>? logger = null)
{
    /// <summary>The admin key that may work the queue and change the criteria (reading needs only staff access).</summary>
    public const string PermissionKey = "attendanceCases";

    /// <summary>audit_logs.entity_type of a case and of the criteria row.</summary>
    public const string AuditEntity = "AttendanceCase";
    public const string AuditEntitySettings = "AttendanceRedListSettings";

    public const int MaxCommentLength = 1000;

    /// <summary>The "Hal bo'ldi" tab shows cases closed within this many days.</summary>
    public const int ClosedListDays = 30;

    /// <summary>A call-back day may be at most this far ahead.</summary>
    public const int MaxCallbackDays = 60;

    private readonly ILogger log = logger ?? NullLogger<AttendanceRedListService>.Instance;
    private readonly ActorNames actors = new(db);

    // =====================================================================
    //  Settings
    // =====================================================================

    /// <summary>The criteria row (read-only copy); the defaults if the row is missing.</summary>
    public async Task<AttendanceRedListSettings> SettingsAsync(CancellationToken ct = default) =>
        await db.AttendanceRedListSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AttendanceRedListSettings();

    /// <summary>The criteria row for writing — created with the defaults when missing (the migration seeds it).</summary>
    private async Task<AttendanceRedListSettings> TrackedSettingsAsync(CancellationToken ct)
    {
        var settings = await db.AttendanceRedListSettings.FirstOrDefaultAsync(ct);
        if (settings is not null) return settings;
        settings = new AttendanceRedListSettings { UpdatedAt = AppClock.NowInstant };
        db.AttendanceRedListSettings.Add(settings);
        return settings;
    }

    public async Task<AttendanceRedListSettingsDto> SettingsDtoAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        return ToDto(s, s.UpdatedBy is null ? null : await actors.OfAsync(s.UpdatedBy, ct));
    }

    public async Task<AttendanceRedListSettingsDto> SaveSettingsAsync(
        SaveAttendanceRedListSettingsRequest req, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (req.ConsecutiveDays is < 2 or > 10)
            throw new AttendanceCaseException(400, "Ketma-ket kunlar 2 dan 10 gacha bo'lsin.");
        if (req.WindowDays is < 3 or > 60)
            throw new AttendanceCaseException(400, "Davr 3 dan 60 kungacha bo'lsin.");
        if (req.WindowUnexcused is < 2 or > 30 || req.WindowUnexcused > req.WindowDays)
            throw new AttendanceCaseException(400, "Davrdagi kunlar soni 2 dan 30 gacha va davrdan ko'p bo'lmasin.");
        if (req.EscalateAfterDays is < 1 or > 14)
            throw new AttendanceCaseException(400, "Rahbariyatga chiqarish muddati 1 dan 14 kungacha bo'lsin.");

        var s = await TrackedSettingsAsync(ct);
        var before = Snapshot(s);
        s.ConsecutiveDays = req.ConsecutiveDays;
        s.WindowDays = req.WindowDays;
        s.WindowUnexcused = req.WindowUnexcused;
        s.CountExcused = req.CountExcused;
        s.EscalateAfterDays = req.EscalateAfterDays;
        s.NotifyTeachers = req.NotifyTeachers;
        s.NotifyManagement = req.NotifyManagement;
        s.UpdatedBy = actorId;
        s.UpdatedAt = AppClock.NowInstant;

        var name = await actors.OfAsync(actorId, ct);
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntitySettings, AttendanceRedListSettings.SingletonId.ToString(CultureInfo.InvariantCulture), "update",
            $"Qizil ro'yxat mezonlari: ketma-ket {s.ConsecutiveDays} kun, {s.WindowDays} kunda {s.WindowUnexcused} kun"
            + (s.CountExcused ? ", sababli kunlar ham" : "") + $", {s.EscalateAfterDays} kundan keyin rahbariyatga.",
            actorId: actorId, actorName: name, before: before, after: Snapshot(s)));
        await db.SaveChangesAsync(ct);
        return ToDto(s, name);
    }

    // =====================================================================
    //  Lists
    // =====================================================================

    /// <summary>
    /// The admin list: every open case plus those closed in the last <see cref="ClosedListDays"/> days.
    /// Counts are of the whole set, the rows of the chosen tab and filters.
    /// </summary>
    /// <param name="status">aloqa_kerak | qayta_qongiroq | boglanib_bolmadi | hal_boldi | open (default).</param>
    public async Task<AttendanceCaseListDto> ListAsync(
        string? status, string? className, string? teacherId, bool overdueOnly, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var closedFrom = AppClock.NowInstant.AddDays(-ClosedListDays);
        var cases = await db.AttendanceCases.AsNoTracking()
            .Where(c => c.ClosedAt == null || c.ClosedAt >= closedFrom)
            .ToListAsync(ct);
        var rows = await RowsAsync(cases, settings, null, ct);

        var open = rows.Where(r => r.ClosedAt is null).ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["open"] = open.Count,
            ["overdue"] = open.Count(r => r.Overdue),
            [AttendanceCaseStatus.Resolved] = rows.Count(r => r.ClosedAt is not null),
        };
        foreach (var s in AttendanceCaseStatus.All.Where(s => s != AttendanceCaseStatus.Resolved))
            counts[s] = open.Count(r => r.Status == s);

        var tab = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim();
        IEnumerable<AttendanceCaseRowDto> shown = tab switch
        {
            "open" => open,
            AttendanceCaseStatus.Resolved => rows.Where(r => r.ClosedAt is not null),
            _ when AttendanceCaseStatus.IsValid(tab) => open.Where(r => r.Status == tab),
            _ => throw new AttendanceCaseException(400, "Noma'lum holat."),
        };
        if (!string.IsNullOrWhiteSpace(className)) shown = shown.Where(r => r.ClassName == className.Trim());
        if (!string.IsNullOrWhiteSpace(teacherId)) shown = shown.Where(r => r.Teachers.Any(t => t.Id == teacherId));
        if (overdueOnly) shown = shown.Where(r => r.Overdue);

        var ordered = tab == AttendanceCaseStatus.Resolved
            ? shown.OrderByDescending(r => r.ClosedAt)
            : shown.OrderByDescending(r => r.Overdue).ThenBy(r => StatusOrder(r.Status))
                .ThenByDescending(r => r.LastAbsentOn).ThenBy(r => r.FullName, StringComparer.Ordinal);

        return new AttendanceCaseListDto(
            counts, [.. ordered],
            [.. rows.Select(r => r.ClassName).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. rows.SelectMany(r => r.Teachers).DistinctBy(t => t.Id).OrderBy(t => t.FullName, StringComparer.Ordinal)],
            ClosedListDays);
    }

    /// <summary>Every open case's pupil — for links from the attendance report.</summary>
    public async Task<List<OpenAttendanceCaseDto>> OpenCasesAsync(CancellationToken ct = default) =>
        await db.AttendanceCases.AsNoTracking()
            .Where(c => c.ClosedAt == null)
            .Select(c => new OpenAttendanceCaseDto(c.StudentId, c.Id, c.Status))
            .ToListAsync(ct);

    /// <summary>One case with its live history; 404 when it does not exist.</summary>
    public async Task<AttendanceCaseDetailDto> DetailAsync(
        Guid id, IReadOnlyDictionary<string, string>? groupOf = null, CancellationToken ct = default)
    {
        var c = await db.AttendanceCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new AttendanceCaseException(404, "Holat topilmadi.");
        var settings = await SettingsAsync(ct);
        var row = (await RowsAsync([c], settings, groupOf, ct)).Single();
        var actions = await db.AttendanceCaseActions.AsNoTracking()
            .Where(a => a.CaseId == id && a.DeletedAt == null)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .ToListAsync(ct);
        return new AttendanceCaseDetailDto(row, [.. actions.Select(ToDto)]);
    }

    // =====================================================================
    //  Teacher side
    // =====================================================================

    /// <summary>The teacher's own open cases with their history, overdue first.</summary>
    public async Task<TeacherRedListDto> TeacherListAsync(string teacherId, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var (groups, groupOf) = await TeacherScopeAsync(teacherId, ct);
        if (groupOf.Count == 0)
            return new TeacherRedListDto(groups, 0, [], settings.ConsecutiveDays, settings.WindowDays,
                settings.WindowUnexcused, settings.CountExcused);

        var ids = groupOf.Keys.ToList();
        var cases = await db.AttendanceCases.AsNoTracking()
            .Where(c => c.ClosedAt == null && ids.Contains(c.StudentId))
            .ToListAsync(ct);
        var rows = await RowsAsync(cases, settings, groupOf, ct);
        var caseIds = cases.Select(c => c.Id).ToList();
        var actions = (await db.AttendanceCaseActions.AsNoTracking()
                .Where(a => caseIds.Contains(a.CaseId) && a.DeletedAt == null)
                .ToListAsync(ct))
            .GroupBy(a => a.CaseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Select(ToDto).ToList());

        var details = rows
            .OrderByDescending(r => r.Overdue).ThenBy(r => StatusOrder(r.Status))
            .ThenByDescending(r => r.LastAbsentOn).ThenBy(r => r.FullName, StringComparer.Ordinal)
            .Select(r => new AttendanceCaseDetailDto(r, actions.GetValueOrDefault(r.Id) ?? []))
            .ToList();
        return new TeacherRedListDto(groups, rows.Count(r => r.Overdue), details, settings.ConsecutiveDays,
            settings.WindowDays, settings.WindowUnexcused, settings.CountExcused);
    }

    /// <summary>An action by a teacher — only on an open case of one of their own pupils (else 404).</summary>
    public async Task<AttendanceCaseDetailDto> TeacherAddActionAsync(
        Teacher teacher, Guid caseId, AddAttendanceCaseActionRequest req, CancellationToken ct = default)
    {
        var (_, groupOf) = await TeacherScopeAsync(teacher.Id, ct);
        var studentId = await db.AttendanceCases.AsNoTracking()
            .Where(c => c.Id == caseId).Select(c => c.StudentId).FirstOrDefaultAsync(ct);
        // Another class's case answers exactly like a missing one: nothing about it leaks.
        if (studentId is null || !groupOf.ContainsKey(studentId))
            throw new AttendanceCaseException(404, "Holat topilmadi.");
        if (string.IsNullOrEmpty(teacher.UserId))
            throw new AttendanceCaseException(403, "O'qituvchi akkaunti topilmadi.");
        return await AddActionAsync(caseId, req,
            new RedListActor(teacher.UserId, teacher.FullName, AttendanceCaseActorType.Teacher), groupOf, ct);
    }

    /// <summary>
    /// The classes the teacher leads (class_homeroom_teachers) and the track groups they teach, and
    /// their active pupils — each pupil once, under the first of those groups (classes, then groups).
    /// The same circle as the homeroom debts card (HomeroomDebts).
    /// </summary>
    public async Task<(List<string> Groups, Dictionary<string, string> GroupOf)> TeacherScopeAsync(
        string teacherId, CancellationToken ct = default)
    {
        var classNames = (await HomeroomDirectory.ClassesOfAsync(db, teacherId, ct)).Select(c => c.Name).ToList();
        var tracks = await (from gt in db.StudyGroupTeachers.AsNoTracking()
                            join g in db.StudyGroups.AsNoTracking() on gt.GroupId equals g.Id
                            where gt.TeacherId == teacherId && g.IsTrack && !g.IsArchived
                            orderby g.Name
                            select new { g.Id, g.Name }).ToListAsync(ct);
        var groups = classNames.Concat(tracks.Select(t => t.Name)).Distinct(StringComparer.Ordinal).ToList();
        var groupOf = new Dictionary<string, string>(StringComparer.Ordinal);
        if (groups.Count == 0) return (groups, groupOf);

        var classPupils = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && classNames.Contains(s.ClassName))
            .Select(s => new { s.Id, s.ClassName })
            .ToListAsync(ct);
        foreach (var p in classPupils.OrderBy(p => classNames.IndexOf(p.ClassName))) groupOf.TryAdd(p.Id, p.ClassName);

        var trackIds = tracks.Select(t => t.Id).ToList();
        var trackName = tracks.ToDictionary(t => t.Id, t => t.Name);
        var members = await (from m in db.StudyGroupMembers.AsNoTracking()
                             join s in db.Students.AsNoTracking() on m.StudentId equals s.Id
                             where trackIds.Contains(m.GroupId) && m.LeftOn == null && !s.IsArchived
                             select new { m.StudentId, m.GroupId }).ToListAsync(ct);
        foreach (var m in members.OrderBy(m => trackName[m.GroupId], StringComparer.Ordinal))
            groupOf.TryAdd(m.StudentId, trackName[m.GroupId]);
        return (groups, groupOf);
    }

    // =====================================================================
    //  Actions
    // =====================================================================

    /// <summary>
    /// Writes an action and moves the case to its status; "hal_boldi" closes the case. A closed case
    /// takes no more actions (409) — a new run of absences opens a new case instead.
    /// </summary>
    public async Task<AttendanceCaseDetailDto> AddActionAsync(
        Guid caseId, AddAttendanceCaseActionRequest req, RedListActor actor,
        IReadOnlyDictionary<string, string>? groupOf = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor.UserId);

        var status = req.Status?.Trim();
        if (!AttendanceCaseStatus.IsValid(status))
            throw new AttendanceCaseException(400,
                "Holatni tanlang: Aloqa kerak, Qayta qo'ng'iroq, Bog'lanib bo'lmadi yoki Hal bo'ldi.");
        var comment = (req.Comment ?? string.Empty).Trim();
        if (comment.Length == 0)
            throw new AttendanceCaseException(400, "Izoh yozing: ota-ona bilan nima gaplashildi yoki nima qilindi.");
        if (comment.Length > MaxCommentLength)
            throw new AttendanceCaseException(400, $"Izoh {MaxCommentLength} belgidan oshmasin.");

        var today = AppClock.Today;
        DateOnly? callback = null;
        if (status == AttendanceCaseStatus.CallBack)
        {
            callback = req.CallbackOn ?? today.AddDays(1);
            if (callback < today || callback > today.AddDays(MaxCallbackDays))
                throw new AttendanceCaseException(400, $"Qayta qo'ng'iroq kuni bugundan {MaxCallbackDays} kungacha bo'lsin.");
        }

        var c = await db.AttendanceCases.FirstOrDefaultAsync(x => x.Id == caseId, ct)
                ?? throw new AttendanceCaseException(404, "Holat topilmadi.");
        if (c.ClosedAt is not null)
            throw new AttendanceCaseException(409,
                "Bu holat hal bo'lgan va yopilgan. O'quvchi yana dars qoldirsa, tizim yangi holat ochadi.");

        var before = Snapshot(c);
        var now = AppClock.NowInstant;
        db.AttendanceCaseActions.Add(new AttendanceCaseAction
        {
            CaseId = c.Id, Status = status!, Comment = comment, CallbackOn = callback,
            ActorId = actor.UserId, ActorName = actor.Name, ActorType = actor.Type, CreatedAt = now,
        });
        c.Status = status!;
        c.CallbackOn = callback;
        c.LastActionAt = now;
        if (status == AttendanceCaseStatus.Resolved)
        {
            c.ClosedAt = now;
            c.ClosedBy = actor.UserId;
        }

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntity, c.Id.ToString("D"), "action",
            $"Qizil ro'yxat: {StatusLabel(status!)}" + (callback is { } cb ? $" ({cb:dd.MM.yyyy})" : "") + $" — \"{Shorten(comment)}\".",
            actorId: actor.UserId, actorName: actor.Name, before: before, after: Snapshot(c), studentId: c.StudentId));
        await db.SaveChangesAsync(ct);
        return await DetailAsync(c.Id, groupOf, ct);
    }

    /// <summary>
    /// Soft-deletes a mistaken action and puts the case back to the status of the latest remaining one
    /// ("aloqa_kerak" when none is left). Deleting the "hal_boldi" action reopens the case — unless the
    /// pupil has a newer open case (409).
    /// </summary>
    public async Task<AttendanceCaseDetailDto> DeleteActionAsync(Guid actionId, string actorId, CancellationToken ct = default)
    {
        var action = await db.AttendanceCaseActions.FirstOrDefaultAsync(a => a.Id == actionId, ct)
                     ?? throw new AttendanceCaseException(404, "Amal topilmadi.");
        if (action.DeletedAt is not null)
            throw new AttendanceCaseException(409, "Bu amal allaqachon o'chirilgan.");
        var c = await db.AttendanceCases.FirstAsync(x => x.Id == action.CaseId, ct);
        var before = Snapshot(c);

        var latest = await db.AttendanceCaseActions.AsNoTracking()
            .Where(a => a.CaseId == c.Id && a.DeletedAt == null && a.Id != action.Id)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);
        var status = latest?.Status ?? AttendanceCaseStatus.ContactNeeded;
        if (c.ClosedAt is not null && status != AttendanceCaseStatus.Resolved)
        {
            if (await db.AttendanceCases.AnyAsync(x => x.StudentId == c.StudentId && x.ClosedAt == null && x.Id != c.Id, ct))
                throw new AttendanceCaseException(409,
                    "O'quvchida yangi ochiq holat bor — bu holatni qayta ochib bo'lmaydi, amal o'chirilmadi.");
            c.ClosedAt = null;
            c.ClosedBy = null;
        }
        else if (c.ClosedAt is null && latest is { Status: AttendanceCaseStatus.Resolved })
        {
            c.ClosedAt = latest.CreatedAt;
            c.ClosedBy = latest.ActorId;
        }
        c.Status = status;
        c.CallbackOn = latest?.CallbackOn;
        c.LastActionAt = latest?.CreatedAt;
        action.DeletedAt = AppClock.NowInstant;
        action.DeletedBy = actorId;

        db.AuditLogs.Add(AuditService.Entry(
            AuditEntity, c.Id.ToString("D"), "delete_action",
            $"Qizil ro'yxat amali o'chirildi (yumshoq): \"{Shorten(action.Comment)}\".",
            actorId: actorId, actorName: await actors.OfAsync(actorId, ct),
            before: before, after: Snapshot(c), studentId: c.StudentId));
        await db.SaveChangesAsync(ct);
        return await DetailAsync(c.Id, null, ct);
    }

    // =====================================================================
    //  Rows
    // =====================================================================

    /// <summary>
    /// Case rows with names, contacts, teachers and the latest action — a fixed number of queries
    /// whatever the number of cases.
    /// </summary>
    /// <param name="groupOf">The teacher's pupil → class / track group; null — the class.</param>
    private async Task<List<AttendanceCaseRowDto>> RowsAsync(
        IReadOnlyCollection<AttendanceCase> cases, AttendanceRedListSettings settings,
        IReadOnlyDictionary<string, string>? groupOf, CancellationToken ct)
    {
        if (cases.Count == 0) return [];
        var today = AppClock.Today;
        var ids = cases.Select(c => c.StudentId).Distinct(StringComparer.Ordinal).ToList();
        var caseIds = cases.Select(c => c.Id).ToList();

        var pupils = await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.IsArchived, s.ParentFullName, s.ParentPhone })
            .ToDictionaryAsync(s => s.Id, StringComparer.Ordinal, ct);
        var guardians = (await (from sg in db.StudentGuardians.AsNoTracking()
                                join g in db.Guardians.AsNoTracking() on sg.GuardianId equals g.Id
                                where ids.Contains(sg.StudentId)
                                orderby sg.IsPrimary descending, g.FullName
                                select new { sg.StudentId, g.FullName, g.Phone, sg.Relation, sg.RelationNote })
                .ToListAsync(ct))
            .GroupBy(g => g.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var teachers = await TeachersOfPupilsAsync(ids, ct);

        var live = db.AttendanceCaseActions.AsNoTracking().Where(a => a.DeletedAt == null && caseIds.Contains(a.CaseId));
        var latest = (await live
                .Where(a => !live.Any(b => b.CaseId == a.CaseId && b.CreatedAt > a.CreatedAt))
                .Select(a => new { a.CaseId, a.Id, a.Comment, a.ActorName, a.CreatedAt })
                .ToListAsync(ct))
            .GroupBy(a => a.CaseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Id).First());
        var closers = cases.Where(c => c.ClosedBy != null).Select(c => c.ClosedBy!).Distinct().ToList();
        var closerNames = await db.Users.AsNoTracking().Where(u => closers.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return [.. cases.Select(c =>
        {
            var p = pupils.GetValueOrDefault(c.StudentId);
            var (overdue, reason, idle) = OverdueOf(c, today, settings.EscalateAfterDays);
            var last = latest.GetValueOrDefault(c.Id);
            var contacts = new List<RedListContactDto>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void AddContact(string name, string phone, string? relation)
            {
                var key = string.Concat(phone.Where(char.IsDigit));
                if (key.Length == 0 || !seen.Add(key)) return;
                contacts.Add(new RedListContactDto(name, phone.Trim(), relation));
            }
            if (p is not null && !string.IsNullOrWhiteSpace(p.ParentPhone))
                AddContact(string.IsNullOrWhiteSpace(p.ParentFullName) ? "Ota-ona" : p.ParentFullName.Trim(), p.ParentPhone, null);
            foreach (var g in guardians.GetValueOrDefault(c.StudentId) ?? [])
                AddContact(g.FullName, g.Phone, RelationLabel(g.Relation, g.RelationNote));

            var className = p?.ClassName ?? "";
            return new AttendanceCaseRowDto(
                c.Id, c.StudentId, p?.FullName ?? "Noma'lum o'quvchi", className,
                groupOf?.GetValueOrDefault(c.StudentId) ?? className, p?.IsArchived ?? false,
                c.Status, c.Trigger, c.Streak, c.WindowCount,
                c.OpenedOn, c.LastAbsentOn, c.LastPresentOn,
                Returned: c.LastPresentOn is { } back && (c.LastAbsentOn is null || back > c.LastAbsentOn),
                [.. c.AbsentDates.OrderDescending()], c.CallbackOn,
                c.LastActionAt, last?.Comment, last?.ActorName, idle,
                overdue, reason,
                teachers.GetValueOrDefault(c.StudentId) ?? [], contacts,
                c.ClosedAt, c.ClosedBy is null ? null : closerNames.GetValueOrDefault(c.ClosedBy, "Noma'lum"));
        })];
    }

    /// <summary>
    /// Pupil → the teachers who follow them up: the homeroom teachers of their class and the
    /// teachers of the track groups they belong to (active ones only), by name.
    /// </summary>
    public async Task<Dictionary<string, List<HomeroomTeacherDto>>> TeachersOfPupilsAsync(
        IReadOnlyCollection<string> studentIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<HomeroomTeacherDto>>(StringComparer.Ordinal);
        if (studentIds.Count == 0) return result;
        var ids = studentIds.ToList();
        var pupils = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.ClassName }).ToListAsync(ct);
        var classNames = pupils.Select(p => p.ClassName).Distinct().ToList();
        var homeroom = (await (from h in db.ClassHomeroomTeachers.AsNoTracking()
                               join c in db.Classes.AsNoTracking() on h.ClassId equals c.Id
                               join t in db.Teachers.AsNoTracking() on h.TeacherId equals t.Id
                               where classNames.Contains(c.Name) && !c.IsArchived && !t.IsArchived
                               select new { c.Name, t.Id, t.FullName }).ToListAsync(ct))
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => new HomeroomTeacherDto(x.Id, x.FullName)).ToList(),
                StringComparer.Ordinal);
        var track = (await (from m in db.StudyGroupMembers.AsNoTracking()
                            join g in db.StudyGroups.AsNoTracking() on m.GroupId equals g.Id
                            join gt in db.StudyGroupTeachers.AsNoTracking() on g.Id equals gt.GroupId
                            join t in db.Teachers.AsNoTracking() on gt.TeacherId equals t.Id
                            where ids.Contains(m.StudentId) && m.LeftOn == null && g.IsTrack && !g.IsArchived
                                  && !t.IsArchived
                            select new { m.StudentId, t.Id, t.FullName }).ToListAsync(ct))
            .GroupBy(x => x.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => new HomeroomTeacherDto(x.Id, x.FullName)).ToList(),
                StringComparer.Ordinal);

        foreach (var p in pupils)
        {
            var list = (homeroom.GetValueOrDefault(p.ClassName) ?? [])
                .Concat(track.GetValueOrDefault(p.Id) ?? [])
                .DistinctBy(t => t.Id)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
            if (list.Count > 0) result[p.Id] = list;
        }
        return result;
    }

    // =====================================================================
    //  Rules and labels
    // =====================================================================

    /// <summary>
    /// Does an open case need management's eye: "unreachable" at once; "callback_due" once the agreed
    /// call-back day has passed; "no_action" after <paramref name="escalateAfterDays"/> calendar days
    /// without an action (counted from opening when nobody acted). A call-back agreed for a later day
    /// is waiting by agreement, not overdue.
    /// </summary>
    public static (bool Overdue, string? Reason, int DaysWithoutAction) OverdueOf(
        AttendanceCase c, DateOnly today, int escalateAfterDays)
    {
        var since = c.LastActionAt is { } at ? AppClock.LocalDateOf(at) : c.OpenedOn;
        var idle = Math.Max(0, today.DayNumber - since.DayNumber);
        if (c.ClosedAt is not null) return (false, null, idle);
        if (c.Status == AttendanceCaseStatus.Unreachable) return (true, "unreachable", idle);
        if (c.Status == AttendanceCaseStatus.CallBack && c.CallbackOn is { } callback)
            return callback < today ? (true, "callback_due", idle) : (false, null, idle);
        return idle >= escalateAfterDays ? (true, "no_action", idle) : (false, null, idle);
    }

    public static string StatusLabel(string status) => status switch
    {
        AttendanceCaseStatus.ContactNeeded => "Aloqa kerak",
        AttendanceCaseStatus.CallBack => "Qayta qo'ng'iroq",
        AttendanceCaseStatus.Unreachable => "Bog'lanib bo'lmadi",
        AttendanceCaseStatus.Resolved => "Hal bo'ldi",
        _ => status,
    };

    /// <summary>"ketma-ket 3 kun sababsiz kelmadi" / "so'nggi 14 kunda 5 kun sababsiz kelmadi".</summary>
    public static string ReasonText(string trigger, int streak, int windowCount, int windowDays, bool countExcused)
    {
        var what = countExcused ? "kelmadi" : "sababsiz kelmadi";
        return trigger == AttendanceCaseTrigger.Consecutive
            ? $"ketma-ket {streak} kun {what}"
            : $"so'nggi {windowDays} kunda {windowCount} kun {what}";
    }

    private static int StatusOrder(string status) => status switch
    {
        AttendanceCaseStatus.Unreachable => 0,
        AttendanceCaseStatus.ContactNeeded => 1,
        AttendanceCaseStatus.CallBack => 2,
        _ => 3,
    };

    private static string? RelationLabel(string relation, string? note) => relation switch
    {
        GuardianRelation.Father => "Otasi",
        GuardianRelation.Mother => "Onasi",
        GuardianRelation.Grandparent => "Bobosi / buvisi",
        GuardianRelation.Trustee => "Ishonchli shaxs",
        GuardianRelation.Other => string.IsNullOrWhiteSpace(note) ? "Boshqa" : note.Trim(),
        _ => "Ota-ona",
    };

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..77] + "...";

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static AttendanceCaseActionDto ToDto(AttendanceCaseAction a) =>
        new(a.Id, a.Status, a.Comment, a.CallbackOn, a.ActorName, a.ActorType, a.CreatedAt);

    private static AttendanceRedListSettingsDto ToDto(AttendanceRedListSettings s, string? updatedBy) => new(
        s.ConsecutiveDays, s.WindowDays, s.WindowUnexcused, s.CountExcused, s.EscalateAfterDays,
        s.NotifyTeachers, s.NotifyManagement, s.LastScanAt, ScanTimes, s.UpdatedAt, updatedBy);

    private static object Snapshot(AttendanceCase c) => new
    {
        c.Id, c.StudentId, c.Status, c.Trigger, c.Streak, c.WindowCount,
        OpenedOn = Iso(c.OpenedOn), LastAbsentOn = c.LastAbsentOn is { } a ? Iso(a) : null,
        CallbackOn = c.CallbackOn is { } cb ? Iso(cb) : null, c.ClosedAt, c.ClosedBy,
    };

    private static object Snapshot(AttendanceRedListSettings s) => new
    {
        s.ConsecutiveDays, s.WindowDays, s.WindowUnexcused, s.CountExcused, s.EscalateAfterDays,
        s.NotifyTeachers, s.NotifyManagement,
    };
}
