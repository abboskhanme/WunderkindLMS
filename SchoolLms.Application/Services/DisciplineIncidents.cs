using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Xulq-atvor — the rules the teacher side needs (docs/modules/behaviour.md, 2026-10-15).
//
//  WHO MAY RECORD AN INCIDENT FOR A PUPIL
//  --------------------------------------
//  A teacher reaches a pupil when they reach the pupil's class (homeroom or any lesson there) or one
//  of the pupil's active study groups (attached or a lesson there) — TeacherOwnerAccess.ReachesAsync,
//  the rule that already decides whose roster a teacher sees — or when they cover a lesson of that
//  class or group TODAY (a substitute records what happens in the lesson they take, not later).
//
//  THE SCORE
//  ---------
//  100 + manual points + attendance-reason points once per pupil, day and reason
//  (AttendanceDisciplineDays) — the same number as Ballar nazorati, the profile and the portal.
// ===========================================================================

/// <summary>Score totals and teacher reach for behaviour incidents.</summary>
public static class DisciplineIncidents
{
    /// <summary>Every pupil starts here (DisciplinePoint, DisciplineController).</summary>
    public const int BaseScore = 100;

    /// <summary>A teacher outside their pupils (403 body).</summary>
    public const string NotYourPupilMessage = "Bu o'quvchi sizning sinf yoki guruhingizda emas.";

    /// <summary>Today in the ISO prefix form of <see cref="DisciplinePoint.CreatedAt"/>.</summary>
    public static string TodayIso => AppClock.Today.ToString("yyyy-MM-dd");

    /// <summary>
    /// Plus and minus per pupil (manual entries and attendance-reason days). Pupils without any
    /// entry are absent from the dictionary — read with <c>GetValueOrDefault</c>.
    /// </summary>
    public static async Task<Dictionary<string, (int Plus, int Minus)>> TotalsAsync(
        IAppDbContext db, IReadOnlyCollection<string> studentIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, (int Plus, int Minus)>(StringComparer.Ordinal);
        if (studentIds.Count == 0) return result;

        void Apply(string id, int points)
        {
            var (plus, minus) = result.GetValueOrDefault(id);
            if (points > 0) plus += points;
            else if (points < 0) minus -= points;
            result[id] = (plus, minus);
        }

        var manual = await db.DisciplinePoints.AsNoTracking()
            .Where(p => studentIds.Contains(p.StudentId))
            .Select(p => new { p.StudentId, p.Points })
            .ToListAsync(ct);
        foreach (var m in manual) Apply(m.StudentId, m.Points);

        var reasonPoints = await db.AbsenceReasons.AsNoTracking()
            .Where(r => r.Points != 0)
            .ToDictionaryAsync(r => r.Id, r => r.Points, ct);
        if (reasonPoints.Count > 0)
        {
            var marks = await db.JournalEntries.AsNoTracking()
                .Where(e => studentIds.Contains(e.StudentId) && e.ReasonId != null)
                .Select(e => new AttendanceDisciplineDays.Mark(e.Id, e.StudentId, e.Date, e.ReasonId!))
                .ToListAsync(ct);
            foreach (var d in AttendanceDisciplineDays.Collapse(marks))
                if (reasonPoints.TryGetValue(d.ReasonId, out var pts)) Apply(d.StudentId, pts);
        }

        return result;
    }

    /// <summary>Sum of today's manual points per pupil (every author).</summary>
    public static async Task<Dictionary<string, int>> TodayAsync(
        IAppDbContext db, IReadOnlyCollection<string> studentIds, CancellationToken ct = default)
    {
        if (studentIds.Count == 0) return new(StringComparer.Ordinal);
        var today = TodayIso;
        var rows = await db.DisciplinePoints.AsNoTracking()
            .Where(p => studentIds.Contains(p.StudentId) && p.CreatedAt.StartsWith(today))
            .Select(p => new { p.StudentId, p.Points })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Points), StringComparer.Ordinal);
    }

    /// <summary>
    /// May this teacher record an incident for this pupil? See the file header: the pupil's class or an
    /// active group of theirs the teacher reaches, or a lesson of theirs the teacher covers today.
    /// </summary>
    public static async Task<bool> TeacherReachesAsync(
        IAppDbContext db, Teacher teacher, Student student, CancellationToken ct = default)
    {
        var owners = new List<string>();
        if (!string.IsNullOrWhiteSpace(student.ClassName))
        {
            var classId = await db.Classes.AsNoTracking()
                .Where(c => c.Name == student.ClassName).Select(c => c.Id).FirstOrDefaultAsync(ct);
            if (classId is not null) owners.Add(classId);
        }
        owners.AddRange((await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudentId == student.Id && m.LeftOn == null)
                .Select(m => m.GroupId).ToListAsync(ct))
            .Select(g => g.ToString()));

        foreach (var ownerId in owners)
            if (await TeacherOwnerAccess.ReachesAsync(db, teacher, ownerId, ct)) return true;

        if (owners.Count == 0) return false;
        var today = AppClock.Today;
        return await db.LessonSubstitutions.AsNoTracking().AnyAsync(s =>
            s.CancelledAt == null && s.SubstituteTeacherId == teacher.Id
            && owners.Contains(s.OwnerId) && s.Date == today, ct);
    }

    /// <summary>A teacher takes back only their own entry, and only on the day it was made.</summary>
    public static bool TeacherMayDelete(DisciplinePoint point, string userId) =>
        !string.IsNullOrEmpty(userId) && point.CreatedByUserId == userId
        && point.CreatedAt.StartsWith(TodayIso, StringComparison.Ordinal);
}
