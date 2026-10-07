using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Hr;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'rindosh ustoz (client, 2026-10-07; LessonSubstitution.cs). The admin picks a date and the
//  teacher who does not teach, sees that teacher's lessons on that date (from the weekly timetable)
//  and names a free teacher for each. Everything else reads the rows:
//    * PortalSchedule  — the substitute's week shows the lesson, the absent teacher's shows who covers;
//    * journal access  — the substitute may fill that class+subject on that date (CoversAsync);
//    * pay             — MonthDeltaAsync: −1 lesson for the absent teacher (unless the whole day is
//                        already "absent"), +1 for the substitute at their own rate.
// ===========================================================================

public record SubstitutionLessonDto(
    int Period, string? StartTime, string? EndTime, string OwnerKind, string OwnerId, string OwnerName,
    string SubjectId, string SubjectName, int SubGroup,
    Guid? SubstitutionId, string? SubstituteTeacherId, string? SubstituteTeacherName);

/// <summary>A teacher the admin can choose; <see cref="BusyPeriods"/> — periods they already teach that day.</summary>
public record SubstitutionTeacherOptionDto(string Id, string FullName, List<int> BusyPeriods);

public record SubstitutionDayDto(
    string Date, string TeacherId, string TeacherName, List<SubstitutionLessonDto> Lessons, List<SubstitutionTeacherOptionDto> Teachers);

public record SubstitutionCreateItem(int Period, string OwnerId, string SubjectId, int SubGroup, string SubstituteTeacherId);

public record SubstitutionCreateRequest(string Date, string AbsentTeacherId, List<SubstitutionCreateItem> Items, string? Reason);

public record SubstitutionDto(
    Guid Id, string Date, int Period, string OwnerKind, string OwnerId, string OwnerName, string SubjectId, string SubjectName,
    int SubGroup, string AbsentTeacherId, string AbsentTeacherName, string SubstituteTeacherId, string SubstituteTeacherName,
    string? Reason, string CreatedByName, DateTimeOffset CreatedAt);

public static class LessonSubstitutionService
{
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static int Dow(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

    /// <summary>(quarter, week, day) of a date, or null when it is in no quarter or on a Sunday.</summary>
    public static async Task<(int Quarter, int Week, int Day)?> PlaceAsync(IAppDbContext db, DateOnly date, CancellationToken ct = default)
    {
        if (Dow(date) > 5) return null;
        var iso = Iso(date);
        var quarter = (await db.Quarters.AsNoTracking().ToListAsync(ct))
            .FirstOrDefault(q => string.CompareOrdinal(iso, q.StartDate) >= 0 && string.CompareOrdinal(iso, q.EndDate) <= 0);
        if (quarter is null) return null;
        var week = PortalSchedule.WeekOf(ScheduleMath.GetQuarterWeeks(quarter.StartDate, quarter.EndDate), iso);
        return (quarter.Quarter, week, Dow(date));
    }

    /// <summary>The timetable's lessons on a date — one teacher's, or everybody's (null).</summary>
    public static async Task<List<OwnedLesson>> LessonsOnAsync(IAppDbContext db, string? teacherId, DateOnly date, CancellationToken ct = default)
    {
        if (await PlaceAsync(db, date, ct) is not { } place) return [];
        return [.. (await TeacherLessons.ForWeekAsync(db, teacherId, place.Quarter, place.Week, ct)).Where(l => l.Day == place.Day)];
    }

    // =====================================================================
    //  Admin
    // =====================================================================

    /// <summary>One teacher's lessons on a date with their current substitutes, and who is free when.</summary>
    public static async Task<SubstitutionDayDto?> DayAsync(IAppDbContext db, string teacherId, DateOnly date, CancellationToken ct = default)
    {
        var teacher = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teacherId, ct);
        if (teacher is null) return null;
        var all = await LessonsOnAsync(db, null, date, ct);
        var own = all.Where(l => l.TeacherId == teacherId).OrderBy(l => l.Period).ToList();
        var standing = await db.LessonSubstitutions.AsNoTracking()
            .Where(s => s.Date == date && s.CancelledAt == null).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var times = await db.LessonTimes.AsNoTracking().ToDictionaryAsync(t => t.Period, ct);
        var teachers = await db.Teachers.AsNoTracking().Where(t => !t.IsArchived).OrderBy(t => t.FullName)
            .Select(t => new { t.Id, t.FullName }).ToListAsync(ct);
        var names = teachers.ToDictionary(t => t.Id, t => t.FullName);

        var lessons = own.Select(l =>
        {
            var sub = standing.FirstOrDefault(s => s.Period == l.Period && s.OwnerId == l.Owner.Id && s.SubjectId == l.SubjectId && s.SubGroup == l.SubGroup);
            times.TryGetValue(l.Period, out var lt);
            return new SubstitutionLessonDto(
                l.Period, lt?.StartTime, lt?.EndTime, l.Owner.Kind, l.Owner.Id, l.Owner.Name,
                l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""), l.SubGroup,
                sub?.Id, sub?.SubstituteTeacherId, sub is null ? null : names.GetValueOrDefault(sub.SubstituteTeacherId));
        }).ToList();

        var options = teachers.Where(t => t.Id != teacherId).Select(t => new SubstitutionTeacherOptionDto(
            t.Id, t.FullName,
            [.. all.Where(l => l.TeacherId == t.Id).Select(l => l.Period)
                .Concat(standing.Where(s => s.SubstituteTeacherId == t.Id).Select(s => s.Period))
                .Distinct().Order()])).ToList();

        return new SubstitutionDayDto(Iso(date), teacher.Id, teacher.FullName, lessons, options);
    }

    /// <summary>
    /// Names substitutes for one teacher's lessons on a date. A lesson that already has a substitute
    /// is re-assigned (the old row is cancelled). Error text or the created rows — the caller saves,
    /// inside a transaction (the old rows are already saved as cancelled here).
    /// </summary>
    public static async Task<(string? Error, List<LessonSubstitution> Created)> CreateAsync(
        IAppDbContext db, SubstitutionCreateRequest req, string actorId, CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(req.Date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return ("Sana noto'g'ri (yyyy-MM-dd)", []);
        if (req.Items is not { Count: > 0 }) return ("Kamida bitta darsga o'rindosh tanlang", []);
        if (await PlaceAsync(db, date, ct) is null) return ("Bu sana o'quv choragida emas yoki yakshanba", []);
        if (!await db.Teachers.AnyAsync(t => t.Id == req.AbsentTeacherId, ct)) return ("O'qituvchi topilmadi", []);

        var all = await LessonsOnAsync(db, null, date, ct);
        var standing = await db.LessonSubstitutions.Where(s => s.Date == date && s.CancelledAt == null).ToListAsync(ct);
        var substitutes = req.Items.Select(i => i.SubstituteTeacherId).Distinct().ToList();
        var active = await db.Teachers.AsNoTracking().Where(t => substitutes.Contains(t.Id) && !t.IsArchived)
            .ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var now = AppClock.NowInstant;
        var created = new List<LessonSubstitution>();

        foreach (var item in req.Items)
        {
            var lesson = all.FirstOrDefault(l => l.TeacherId == req.AbsentTeacherId && l.Period == item.Period
                && l.Owner.Id == item.OwnerId && l.SubjectId == item.SubjectId && l.SubGroup == item.SubGroup);
            if (lesson is null) return ($"{item.Period}-dars bu ustozning shu kungi jadvalida yo'q", []);
            if (item.SubstituteTeacherId == req.AbsentTeacherId) return ("O'rindosh boshqa ustoz bo'lishi kerak", []);
            if (!active.TryGetValue(item.SubstituteTeacherId, out var subName)) return ("O'rindosh ustoz topilmadi", []);
            var busy = all.Any(l => l.TeacherId == item.SubstituteTeacherId && l.Period == item.Period)
                || standing.Any(s => s.SubstituteTeacherId == item.SubstituteTeacherId && s.Period == item.Period
                                     && !(s.OwnerId == item.OwnerId && s.SubjectId == item.SubjectId && s.SubGroup == item.SubGroup))
                || created.Any(c => c.SubstituteTeacherId == item.SubstituteTeacherId && c.Period == item.Period);
            if (busy) return ($"{subName} {item.Period}-darsda band", []);

            foreach (var old in standing.Where(s => s.Period == item.Period && s.OwnerId == item.OwnerId
                                                    && s.SubjectId == item.SubjectId && s.SubGroup == item.SubGroup))
            {
                old.CancelledAt = now;
                old.CancelledBy = actorId;
            }
            created.Add(new LessonSubstitution
            {
                Date = date, Period = item.Period, OwnerKind = lesson.Owner.Kind, OwnerId = lesson.Owner.Id,
                SubjectId = lesson.SubjectId, SubGroup = lesson.SubGroup,
                AbsentTeacherId = req.AbsentTeacherId, SubstituteTeacherId = item.SubstituteTeacherId,
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()[..Math.Min(req.Reason.Trim().Length, 300)],
                CreatedBy = actorId, CreatedAt = now,
            });
        }
        // The old rows step aside first — the unique index allows one standing row per lesson.
        await db.SaveChangesAsync(ct);
        db.LessonSubstitutions.AddRange(created);
        return (null, created);
    }

    public static async Task<LessonSubstitution?> CancelAsync(IAppDbContext db, Guid id, string actorId, CancellationToken ct = default)
    {
        var row = await db.LessonSubstitutions.FirstOrDefaultAsync(s => s.Id == id && s.CancelledAt == null, ct);
        if (row is null) return null;
        row.CancelledAt = AppClock.NowInstant;
        row.CancelledBy = actorId;
        return row;
    }

    public static async Task<List<SubstitutionDto>> ListAsync(
        IAppDbContext db, DateOnly from, DateOnly to, string? teacherId, CancellationToken ct = default)
    {
        var q = db.LessonSubstitutions.AsNoTracking().Where(s => s.CancelledAt == null && s.Date >= from && s.Date <= to);
        if (!string.IsNullOrWhiteSpace(teacherId)) q = q.Where(s => s.AbsentTeacherId == teacherId || s.SubstituteTeacherId == teacherId);
        var rows = await q.OrderByDescending(s => s.Date).ThenBy(s => s.Period).Take(500).ToListAsync(ct);
        return await ToDtosAsync(db, rows, ct);
    }

    public static async Task<List<SubstitutionDto>> ToDtosAsync(IAppDbContext db, List<LessonSubstitution> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return [];
        var teacherIds = rows.SelectMany(r => new[] { r.AbsentTeacherId, r.SubstituteTeacherId }).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var owners = await LessonRoster.AllOwnersAsync(db, ct);
        var userIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return [.. rows.Select(r => new SubstitutionDto(
            r.Id, Iso(r.Date), r.Period, r.OwnerKind, r.OwnerId, owners.TryGetValue(r.OwnerId, out var o) ? o.Name : "",
            r.SubjectId, subjects.GetValueOrDefault(r.SubjectId, ""), r.SubGroup,
            r.AbsentTeacherId, teachers.GetValueOrDefault(r.AbsentTeacherId, ""),
            r.SubstituteTeacherId, teachers.GetValueOrDefault(r.SubstituteTeacherId, ""),
            r.Reason, users.GetValueOrDefault(r.CreatedBy, ""), r.CreatedAt))];
    }

    // =====================================================================
    //  Reads for the journal, the schedule and pay
    // =====================================================================

    /// <summary>
    /// May this teacher, as a substitute, open (date null — any standing substitution in the last
    /// 30 days or ahead) or write (that date) the class+subject journal?
    /// </summary>
    public static Task<bool> CoversAsync(IAppDbContext db, string teacherId, string ownerId, string subjectId, string? date, CancellationToken ct = default)
    {
        var q = db.LessonSubstitutions.AsNoTracking().Where(s => s.CancelledAt == null && s.SubstituteTeacherId == teacherId
                                                                  && s.OwnerId == ownerId && s.SubjectId == subjectId);
        if (date is not null)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return Task.FromResult(false);
            return q.AnyAsync(s => s.Date == d, ct);
        }
        var since = AppClock.Today.AddDays(-30);
        return q.AnyAsync(s => s.Date >= since, ct);
    }

    /// <summary>A standing substitution in this class (any subject) in the last 30 days or ahead — the roster read.</summary>
    public static Task<bool> CoversClassAsync(IAppDbContext db, string teacherId, string ownerId, CancellationToken ct = default)
    {
        var since = AppClock.Today.AddDays(-30);
        return db.LessonSubstitutions.AsNoTracking().AnyAsync(s => s.CancelledAt == null && s.SubstituteTeacherId == teacherId
                                                                    && s.OwnerId == ownerId && s.Date >= since, ct);
    }

    /// <summary>
    /// Lessons each teacher gains (as substitute) minus loses (as the absent one) per month "yyyy-MM".
    /// A lesson lost on a day already marked absent is not counted twice.
    /// </summary>
    public static async Task<Dictionary<(string TeacherId, string Month), int>> MonthDeltaAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.LessonSubstitutions.AsNoTracking().Where(s => s.CancelledAt == null)
            .Select(s => new { s.Date, s.AbsentTeacherId, s.SubstituteTeacherId }).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var absentDays = (await db.TeacherAttendances.AsNoTracking().Where(a => a.Status == "absent")
                .Select(a => new { a.TeacherId, a.Date }).ToListAsync(ct))
            .Select(a => (a.TeacherId, a.Date)).ToHashSet();
        var delta = new Dictionary<(string, string), int>();
        foreach (var r in rows)
        {
            var month = r.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            delta[(r.SubstituteTeacherId, month)] = delta.GetValueOrDefault((r.SubstituteTeacherId, month)) + 1;
            if (!absentDays.Contains((r.AbsentTeacherId, Iso(r.Date))))
                delta[(r.AbsentTeacherId, month)] = delta.GetValueOrDefault((r.AbsentTeacherId, month)) - 1;
        }
        return delta;
    }

    /// <summary>Monday of a timetable week (quarter, week); null when unknown.</summary>
    public static async Task<DateOnly?> WeekMondayAsync(IAppDbContext db, int quarter, int week, CancellationToken ct = default)
    {
        var q = await db.Quarters.AsNoTracking().FirstOrDefaultAsync(x => x.Quarter == quarter, ct);
        if (q is null) return null;
        var range = ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate).FirstOrDefault(w => w.Week == week);
        if (range is null || !DateOnly.TryParseExact(range.StartISO, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return null;
        return start.AddDays(-Dow(start));
    }
}

/// <summary>Bot messages to the substitute: named for lessons, or told the substitution is off.</summary>
public sealed class SubstitutionNotifier(IAppDbContext db, TelegramService telegram, ILogger<SubstitutionNotifier> logger)
{
    private static readonly string[] Days = ["dushanba", "seshanba", "chorshanba", "payshanba", "juma", "shanba", "yakshanba"];

    public async Task<int> AssignedAsync(IReadOnlyList<SubstitutionDto> rows, CancellationToken ct = default) =>
        await SendAsync(rows, "🔁 Sizga o'rindosh dars biriktirildi", true, ct);

    public async Task<int> CancelledAsync(IReadOnlyList<SubstitutionDto> rows, CancellationToken ct = default) =>
        await SendAsync(rows, "❎ O'rindosh dars bekor qilindi", false, ct);

    private async Task<int> SendAsync(IReadOnlyList<SubstitutionDto> rows, string title, bool withHint, CancellationToken ct)
    {
        if (!telegram.IsConfigured || rows.Count == 0) return 0;
        var sent = 0;
        try
        {
            var times = await db.LessonTimes.AsNoTracking().ToDictionaryAsync(t => t.Period, ct);
            foreach (var group in rows.GroupBy(r => r.SubstituteTeacherId))
            {
                var lines = new List<string> { title, "" };
                foreach (var r in group.OrderBy(r => r.Date).ThenBy(r => r.Period))
                {
                    var d = DateOnly.ParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    times.TryGetValue(r.Period, out var lt);
                    var time = lt is null ? "" : $" ({lt.StartTime}–{lt.EndTime})";
                    lines.Add($"• {d:dd.MM.yyyy} ({Days[((int)d.DayOfWeek + 6) % 7]}), {r.Period}-dars{time}: {r.OwnerName} · {r.SubjectName}");
                    lines.Add($"  Asosiy ustoz: {r.AbsentTeacherName}");
                }
                if (withHint)
                {
                    lines.Add("");
                    lines.Add($"Jurnalni to'ldirish: «{TelegramPanelKeys.ButtonText}» → Bugun.");
                }
                var text = string.Join('\n', lines);
                foreach (var chat in await PenaltyNotifier.RecipientsAsync(db, EmployeeKind.Teacher, group.Key, ct))
                    if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "O'rindosh dars xabari yuborilmadi");
        }
        return sent;
    }
}
