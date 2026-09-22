using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  "Mavsumiy baholash hisoboti" — per-teacher coverage (unit B4).
//  Spec: docs/modules/admission-and-testing.md §2.4, §3.3 screen 13, §6.6.
// ===========================================================================
//
//  A REPORT, NOT A TABLE (§10). Built from the schedule, today's roster and
//  `seasonal_marks`; nothing is stored.
//
//  WHAT ONE UNIT OF COVERAGE IS — A SLOT
//  -------------------------------------
//  A slot is one pupil in one (class, subject) a teacher teaches. It is marked
//  when that pupil has a mark in that subject for the period (score, comment,
//  or both — a stored row is a mark).
//
//    totalStudents = slots         marked   = marked slots
//    unmarked      = total − marked percent = min(marked / total × 100, 100)
//
//  §6.6 phrases `totalStudents` as "the distinct count of pupils across the
//  (class, subject) pairs". Counting SLOTS rather than heads is the reading
//  that keeps the screen honest: a teacher of Maths AND Physics in 5-A owes 60
//  marks, not 30, and with heads a finished teacher would show 60 "marked" of
//  30 pupils and −30 "unmarked". The drill-down lists one row per slot
//  (`CoverageDetailRowDto` has a subject), so the three numbers are also
//  exactly the row counts of its three tabs. The `min(…, 100)` cap is kept
//  from EduSchool's formula; with slots it can no longer be exceeded — a mark
//  of a pupil who has left the class simply is not one of the slots.
//
//  SUB-GROUPS
//  ----------
//  In a split lesson (ScheduleLesson.SubGroup 1/2) a teacher is responsible
//  only for the pupils of their half — the journal's roster rule
//  (`LessonRoster.ForLessonAsync`). A teacher with any whole-class lesson of the
//  pair gets the whole class.
//
//  WHICH SCHEDULE, WHICH ROSTER
//  ----------------------------
//  Today's. The schedule has no academic-year column and the roster no
//  history, so a past period is measured against who teaches whom NOW. That is
//  what the numbers can honestly say; for last year's coverage, export it at
//  the time.
// ===========================================================================

/// <summary>Per-teacher coverage of one period, its drill-down and its export.</summary>
public sealed class SeasonalCoverageReport(IAppDbContext db)
{
    public const string TeacherRequiredMessage = "O'qituvchi tanlanmagan";
    public const string TeacherNotFoundMessage = "O'qituvchi topilmadi";

    /// <summary>One page of teacher rows, by name.</summary>
    public async Task<SeasonalPageDto<SeasonalCoverageRowDto>> PageAsync(
        SeasonalCoverageFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (page, limit) = SeasonalMarkService.Paging(filter.Page, filter.Limit);

        var rows = await AllAsync(filter, ct);
        return new SeasonalPageDto<SeasonalCoverageRowDto>(
            [.. rows.Skip((page - 1) * limit).Take(limit)], rows.Count, page, limit);
    }

    /// <summary>Every teacher row of the filter — the export, and the source of the page.</summary>
    public async Task<IReadOnlyList<SeasonalCoverageRowDto>> AllAsync(
        SeasonalCoverageFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var period = SeasonalPeriod.From(filter);
        var teacherIds = filter.TeacherIds
            .Select(SeasonalMarkService.Clean).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

        var (teachers, slots) = await SlotsAsync(period, teacherIds.Count == 0 ? null : teacherIds, ct);
        var slotsByTeacher = slots.ToLookup(s => s.TeacherId, StringComparer.Ordinal);

        // Every teacher who teaches something is listed — one whose classes have
        // no pupils yet shows 0 of 0, not nothing.
        return [.. teachers
            .Select(t =>
            {
                var own = slotsByTeacher[t.Key].ToList();
                var marked = own.Count(s => s.HasMark);
                return new SeasonalCoverageRowDto(t.Key, t.Value, own.Count, marked, own.Count - marked, Percent(marked, own.Count));
            })
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.TeacherId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The pupils behind one teacher's numbers — one row per slot, filtered by
    /// <c>hasMark</c>, ordered class → subject → pupil.
    /// </summary>
    public async Task<SeasonalPageDto<SeasonalCoverageDetailRowDto>> DetailAsync(
        SeasonalCoverageDetailFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (page, limit) = SeasonalMarkService.Paging(filter.Page, filter.Limit);
        var teacherId = SeasonalMarkService.Clean(filter.TeacherId)
                        ?? throw SeasonalMarkException.Invalid(TeacherRequiredMessage);
        var period = SeasonalPeriod.From(filter);

        if (!await db.Teachers.AsNoTracking().AnyAsync(t => t.Id == teacherId, ct))
            throw SeasonalMarkException.NotFound(TeacherNotFoundMessage);

        var (_, slots) = await SlotsAsync(period, [teacherId], ct);
        IEnumerable<Slot> matching = slots;
        if (filter.HasMark is { } hasMark) matching = matching.Where(s => s.HasMark == hasMark);

        var ordered = matching
            .OrderBy(s => s.ClassGrade)
            .ThenBy(s => s.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.StudentId, StringComparer.Ordinal)
            .ToList();

        return new SeasonalPageDto<SeasonalCoverageDetailRowDto>(
            [.. ordered.Skip((page - 1) * limit).Take(limit).Select(s => new SeasonalCoverageDetailRowDto(
                s.StudentId, s.FullName, s.ClassName, s.SubjectId, s.SubjectName, s.HasMark, s.Score))],
            ordered.Count, page, limit);
    }

    /// <summary>
    /// <c>min(marked / total × 100, 100)</c> to a whole number, half away from
    /// zero (EduSchool: <c>Math.min(j/y*100,100).toFixed()</c>); 0 with no pupils.
    /// </summary>
    public static int Percent(int marked, int total) =>
        total <= 0 ? 0 : (int)Math.Round(Math.Min(marked * 100m / total, 100m), MidpointRounding.AwayFromZero);

    // ---------------------------------------------------------------------

    private sealed record Slot(
        string TeacherId,
        string StudentId, string FullName,
        string ClassId, string ClassName, int ClassGrade,
        string SubjectId, string SubjectName,
        bool HasMark, decimal? Score);

    /// <summary>
    /// The teachers of the schedule (id → name) and every slot they own for the
    /// period. Three queries whatever the school's size — lessons (with their
    /// names), pupils, marks — never one per teacher or per class.
    /// </summary>
    private async Task<(Dictionary<string, string> Teachers, List<Slot> Slots)> SlotsAsync(
        SeasonalPeriod period, IReadOnlyCollection<string>? teacherIds, CancellationToken ct)
    {
        var lessons = await SeasonalMarkService.TaughtLessonsAsync(db, teacherIds, ct);
        var teachers = lessons
            .GroupBy(l => l.TeacherId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().TeacherFullName, StringComparer.Ordinal);
        if (lessons.Count == 0) return (teachers, []);

        // (teacher, class, subject) → which pupils: the whole class if any lesson
        // of the pair is whole-class, otherwise only the halves the teacher takes.
        var pairs = lessons
            .GroupBy(l => (l.TeacherId, l.ClassId, l.SubjectId))
            .Select(g => new
            {
                Lesson = g.First(),
                WholeClass = g.Any(l => l.SubGroup == 0),
                SubGroups = g.Select(l => l.SubGroup).ToHashSet(),
            })
            .ToList();

        // Active pupils of those classes — the class rule of `LessonRoster.Query`
        // (`class_name = classes.name`), batched into one query for all classes.
        var classNames = pairs.Select(p => p.Lesson.ClassName).Distinct(StringComparer.Ordinal).ToList();
        var pupils = (await db.Students.AsNoTracking()
                .Where(s => !s.IsArchived && classNames.Contains(s.ClassName))
                .Select(s => new { s.Id, s.FullName, s.ClassName, s.SubGroup })
                .ToListAsync(ct))
            .ToLookup(s => s.ClassName, StringComparer.Ordinal);

        var key = period.Key;
        var subjectIds = pairs.Select(p => p.Lesson.SubjectId).Distinct(StringComparer.Ordinal).ToList();
        var marks = (await db.SeasonalMarks.AsNoTracking()
                .Where(m => m.PeriodKey == key && subjectIds.Contains(m.SubjectId))
                .Select(m => new { m.StudentId, m.SubjectId, m.Score })
                .ToListAsync(ct))
            .ToDictionary(m => (m.StudentId, m.SubjectId), m => m.Score);

        var slots = new List<Slot>();
        foreach (var pair in pairs)
        {
            var l = pair.Lesson;
            foreach (var p in pupils[l.ClassName])
            {
                if (!pair.WholeClass && !pair.SubGroups.Contains(p.SubGroup)) continue;
                var hasMark = marks.TryGetValue((p.Id, l.SubjectId), out var score);
                slots.Add(new Slot(
                    l.TeacherId, p.Id, p.FullName,
                    l.ClassId, l.ClassName, l.ClassGrade,
                    l.SubjectId, l.SubjectName,
                    hasMark, score));
            }
        }

        return (teachers, slots);
    }
}
