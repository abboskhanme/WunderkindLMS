using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Who teaches on which day (client, 2026-10-15: "davomatda faqat o'sha kuni darsi bor ustozlar
//  chiqishi"; docs/modules/teacher-attendance-range.md). Teacher attendance is tied to the
//  timetable: on a given day only the teachers with lessons that day are expected at school.
//
//  Source: the template ASSIGNED to that week (TeacherLessons.ForWeekAsync — class and group
//  owners, the group-lessons switch respected), the same as LessonSubstitutionService.LessonsOnAsync
//  and the teacher's own schedule. Not the weekday "main" template salary reads (ByWeekdayAsync).
//  On top of it:
//    * Sundays, days outside every quarter and holidays have no lessons;
//    * a standing (not cancelled) substitution moves one lesson to the substitute. The absent
//      teacher stays listed on that day even if their count drops to 0 — they must still be
//      markable absent.
//
//  Each (quarter, week) in the range is resolved once, so the query count grows with the weeks in
//  the range, not with its days or teachers.
// ===========================================================================

/// <summary>One calendar day of a <see cref="TeacherDaySchedule"/>.</summary>
/// <param name="HolidayName">The holiday's name ("" if it has none); null — not a holiday.</param>
/// <param name="InQuarter">Inside some quarter (Sunday or not).</param>
public sealed record TeachingDay(DateOnly Date, bool IsSunday, string? HolidayName, bool InQuarter)
{
    public bool IsHoliday => HolidayName is not null;

    /// <summary>The timetable runs on this day (it may still have no lessons assigned).</summary>
    public bool IsTeachingDay => !IsSunday && !IsHoliday && InQuarter;
}

/// <summary>
/// Per date of a range: the teachers who have lessons that day and how many. Built by
/// <see cref="LoadAsync"/>; the file header explains the rules.
/// </summary>
public sealed class TeacherDaySchedule
{
    private static readonly IReadOnlyDictionary<string, int> None = new Dictionary<string, int>();

    private readonly List<TeachingDay> _days;
    private readonly Dictionary<DateOnly, Dictionary<string, int>> _lessons;

    private TeacherDaySchedule(List<TeachingDay> days, Dictionary<DateOnly, Dictionary<string, int>> lessons)
    {
        _days = days;
        _lessons = lessons;
    }

    /// <summary>Every calendar day of the range, Sundays included, in order.</summary>
    public IReadOnlyList<TeachingDay> Days => _days;

    /// <summary>
    /// teacherId → lessons that teacher teaches on <paramref name="date"/> (own lessons not given
    /// to a substitute + lessons covered for others). A listed teacher may have 0 — all their
    /// lessons went to substitutes. Empty when nobody teaches or the date is outside the range.
    /// </summary>
    public IReadOnlyDictionary<string, int> TeachersOn(DateOnly date) =>
        _lessons.TryGetValue(date, out var byTeacher) ? byTeacher : None;

    /// <summary>The teacher is listed on that day (has or had a lesson there) — may be marked.</summary>
    public bool Teaches(string teacherId, DateOnly date) => TeachersOn(date).ContainsKey(teacherId);

    /// <summary>Every teacher listed on at least one day of the range.</summary>
    public IEnumerable<string> TeacherIds => _lessons.Values.SelectMany(d => d.Keys).Distinct(StringComparer.Ordinal);

    /// <summary>Builds the schedule for [<paramref name="from"/>..<paramref name="to"/>] (both inclusive).</summary>
    public static async Task<TeacherDaySchedule> LoadAsync(
        IAppDbContext db, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var days = new List<TeachingDay>();
        var lessons = new Dictionary<DateOnly, Dictionary<string, int>>();
        if (from > to) return new TeacherDaySchedule(days, lessons);

        var fromIso = Iso(from);
        var toIso = Iso(to);
        var quarters = await db.Quarters.AsNoTracking().ToListAsync(ct);
        var holidays = (await db.Holidays.AsNoTracking()
                .Where(h => string.Compare(h.Date, fromIso) >= 0 && string.Compare(h.Date, toIso) <= 0)
                .Select(h => new { h.Date, h.Name }).ToListAsync(ct))
            .GroupBy(h => h.Date, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

        // One ForWeekAsync per (quarter, week) — a week's lessons serve all six of its days.
        var weeks = new Dictionary<(int Quarter, int Week), List<OwnedLesson>>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var iso = Iso(d);
            var inQuarter = quarters.Any(q =>
                string.CompareOrdinal(iso, q.StartDate) >= 0 && string.CompareOrdinal(iso, q.EndDate) <= 0);
            var day = new TeachingDay(d, d.DayOfWeek == DayOfWeek.Sunday,
                holidays.TryGetValue(iso, out var name) ? name : null, inQuarter);
            days.Add(day);
            if (!day.IsTeachingDay || LessonSubstitutionService.Place(quarters, d) is not { } place) continue;

            if (!weeks.TryGetValue((place.Quarter, place.Week), out var weekLessons))
                weeks[(place.Quarter, place.Week)] = weekLessons =
                    await TeacherLessons.ForWeekAsync(db, null, place.Quarter, place.Week, ct);

            var byTeacher = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var l in weekLessons)
                if (l.Day == place.Day && !string.IsNullOrEmpty(l.TeacherId))
                    byTeacher[l.TeacherId] = byTeacher.GetValueOrDefault(l.TeacherId) + 1;
            if (byTeacher.Count > 0) lessons[d] = byTeacher;
        }

        // Standing substitutions on teaching days: the lesson moves to the substitute.
        var teachingDays = days.Where(x => x.IsTeachingDay).Select(x => x.Date).ToHashSet();
        var substitutions = await db.LessonSubstitutions.AsNoTracking()
            .Where(s => s.CancelledAt == null && s.Date >= from && s.Date <= to)
            .Select(s => new { s.Date, s.AbsentTeacherId, s.SubstituteTeacherId })
            .ToListAsync(ct);
        foreach (var s in substitutions)
        {
            if (!teachingDays.Contains(s.Date)) continue;
            if (!lessons.TryGetValue(s.Date, out var byTeacher))
                lessons[s.Date] = byTeacher = new Dictionary<string, int>(StringComparer.Ordinal);
            byTeacher[s.SubstituteTeacherId] = byTeacher.GetValueOrDefault(s.SubstituteTeacherId) + 1;
            byTeacher[s.AbsentTeacherId] = Math.Max(0, byTeacher.GetValueOrDefault(s.AbsentTeacherId) - 1);
        }

        return new TeacherDaySchedule(days, lessons);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
