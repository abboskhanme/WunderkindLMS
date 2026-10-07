using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Xodimlar samaradorligi — KPI (client, 2026-10-07: "hodimlar va maktab rahbariyatining
//  samaradorlik ko'rsatkichini o'lchash"). One row per active HR employee for a month; every
//  component is a 0–100 score or null when there is nothing to measure, and the total is the
//  weighted mean of the components that have a score — but only when at least one of the first
//  five (the work itself) has one; fines and attendance never make a score on their own:
//
//    tasks           HR tasks due in the month: done on time / all (cancelled left out)
//    lessonAnalysis  mean score % of the teacher's completed lesson observations
//    journal         lessons marked conducted / planned so far, for the quarter (activity report)
//    parentNps       parents' NPS of this teacher in the month, (NPS + 100) / 2
//    staffNps        teachers' NPS of this manager in the month, (NPS + 100) / 2
//    penalties       100 − 20 per fine of the month (cancelled ones do not count)
//    attendance      teachers: 100 − 10 per absent day − 3 per late day
// ===========================================================================

public record KpiWeightsDto(int Tasks, int LessonAnalysis, int Journal, int ParentNps, int StaffNps, int Penalties, int Attendance);

public record KpiRowDto(
    string EmployeeId, string FullName, string? Position, string HrRole, string? TeacherId, double? Total,
    double? Tasks, int TasksOnTime, int TasksTotal,
    double? LessonAnalysis, int Observations,
    double? Journal,
    double? ParentNps, int? ParentNpsRaw, int ParentAnswers,
    double? StaffNps, int? StaffNpsRaw, int StaffAnswers,
    double? Penalties, int PenaltyCount,
    double? Attendance, int AbsentDays, int LateDays);

public record KpiReportDto(
    string Month, int? Quarter, KpiWeightsDto Weights, List<KpiRowDto> Rows,
    int? SchoolNps, int SchoolAnswers, int? ManagementNps, int ManagementAnswers);

public static class KpiService
{
    public const string Permission = "kpi";
    public const int PenaltyStep = 20;
    public const int AbsentStep = 10;
    public const int LateStep = 3;

    public static KpiWeightsDto ToDto(KpiSettings s) => new(
        s.WeightTasks, s.WeightLessonAnalysis, s.WeightJournal, s.WeightParentNps, s.WeightStaffNps, s.WeightPenalties, s.WeightAttendance);

    public static async Task<KpiSettings> SettingsAsync(IAppDbContext db, CancellationToken ct = default) =>
        await db.KpiSettings.FirstOrDefaultAsync(ct) ?? new KpiSettings();

    public static async Task<string?> SaveWeightsAsync(IAppDbContext db, KpiWeightsDto w, string actorId, CancellationToken ct = default)
    {
        int[] all = [w.Tasks, w.LessonAnalysis, w.Journal, w.ParentNps, w.StaffNps, w.Penalties, w.Attendance];
        if (all.Any(x => x is < 0 or > 100)) return "Har bir vazn 0..100 oralig'ida bo'lsin";
        if (all.Sum() == 0) return "Kamida bitta ko'rsatkichga vazn bering";
        var s = await db.KpiSettings.FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new KpiSettings();
            db.KpiSettings.Add(s);
        }
        (s.WeightTasks, s.WeightLessonAnalysis, s.WeightJournal, s.WeightParentNps, s.WeightStaffNps, s.WeightPenalties, s.WeightAttendance) =
            (w.Tasks, w.LessonAnalysis, w.Journal, w.ParentNps, w.StaffNps, w.Penalties, w.Attendance);
        s.UpdatedAt = AppClock.NowInstant;
        s.UpdatedBy = actorId;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>The weighted mean of the scored components; null when none has a score.</summary>
    public static double? Total(params (double? Score, int Weight)[] parts)
    {
        var used = parts.Where(p => p.Score is not null && p.Weight > 0).ToList();
        var weight = used.Sum(p => p.Weight);
        return weight == 0 ? null : Math.Round(used.Sum(p => p.Score!.Value * p.Weight) / weight, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>The instant a local (Tashkent) date begins.</summary>
    public static DateTimeOffset LocalMidnight(DateOnly date)
    {
        var local = AppClock.Now;
        return AppClock.NowInstant.AddDays(date.DayNumber - DateOnly.FromDateTime(local).DayNumber) - local.TimeOfDay;
    }

    private static double? Round(double? value) => value is { } v ? Math.Round(v, 1, MidpointRounding.AwayFromZero) : null;

    private static double? FromNps(int? nps) => nps is { } n ? (n + 100) / 2.0 : null;

    public static async Task<KpiReportDto> BuildAsync(IAppDbContext db, string month, CancellationToken ct = default)
    {
        var start = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = start.AddMonths(1);
        var since = LocalMidnight(start);
        var until = LocalMidnight(end);
        var now = AppClock.NowInstant;
        var settings = await SettingsAsync(db, ct);

        var employees = await (from e in db.HrEmployees.AsNoTracking()
                               where e.IsActive
                               join p in db.HrPositions.AsNoTracking() on e.PositionId equals p.Id into pos
                               from p in pos.DefaultIfEmpty()
                               orderby e.FullName
                               select new { e.Id, e.UserId, e.FullName, e.HrRole, Position = p != null ? p.Name : null }).ToListAsync(ct);
        var teacherByUser = (await db.Teachers.AsNoTracking().Where(t => t.UserId != null && !t.IsArchived)
                .Select(t => new { t.Id, t.UserId }).ToListAsync(ct))
            .GroupBy(t => t.UserId!).ToDictionary(g => g.Key, g => g.First().Id);

        // HR tasks due in the month — the ones not yet due count only once done.
        var tasks = (await db.HrTasks.AsNoTracking()
                .Where(t => t.DueAt >= since && t.DueAt < until && t.Status != HrTaskStatuses.Cancelled)
                .Select(t => new { t.EmployeeId, t.Status, t.IsLate, t.DueAt }).ToListAsync(ct))
            .Where(t => t.DueAt <= now || t.Status == HrTaskStatuses.Done)
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => (OnTime: g.Count(t => t.Status == HrTaskStatuses.Done && !t.IsLate), Total: g.Count()));

        var observations = (await db.LessonObservations.AsNoTracking()
                .Where(o => o.Status == LessonObservationStatus.Completed && o.LessonDate >= start && o.LessonDate < end)
                .Select(o => new { o.TeacherId, o.ScorePercent }).ToListAsync(ct))
            .GroupBy(o => o.TeacherId)
            .ToDictionary(g => g.Key, g => (Mean: (double)g.Average(o => o.ScorePercent), Count: g.Count()));

        // Journal: the quarter of the month's last day (or today, inside the current month).
        var probe = end.AddDays(-1) < AppClock.Today ? end.AddDays(-1) : AppClock.Today;
        var probeIso = probe.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var quarter = (await db.Quarters.AsNoTracking().ToListAsync(ct))
            .FirstOrDefault(q => string.CompareOrdinal(probeIso, q.StartDate) >= 0 && string.CompareOrdinal(probeIso, q.EndDate) <= 0)?.Quarter;
        var journal = quarter is { } qn
            ? (await TeacherActivityReport.BuildOverviewAsync(db, qn)).Where(r => r.DonePct is not null).ToDictionary(r => r.TeacherId, r => (double)r.DonePct!.Value)
            : [];

        var parentScores = await PollService.PersonScoresAsync(db, PollTarget.Teachers, since, until, ct);
        var staffScores = await PollService.PersonScoresAsync(db, PollTarget.Management, since, until, ct);

        var year = (short)start.Year;
        var monthNo = (short)start.Month;
        var reversed = db.PayrollAdjustments.Where(x => x.ReversalOf != null).Select(x => x.ReversalOf);
        var fines = await db.PayrollAdjustments.AsNoTracking()
            .Where(x => x.Kind == AdjustmentKind.Penalty && x.ReversalOf == null && x.PeriodYear == year && x.PeriodMonth == monthNo
                        && !reversed.Contains(x.Id))
            .Select(x => new { x.TeacherId, x.UserId }).ToListAsync(ct);

        var attendance = (await db.TeacherAttendances.AsNoTracking()
                .Where(a => a.Date.StartsWith(month) && (a.Status == "absent" || a.Status == "late"))
                .Select(a => new { a.TeacherId, a.Status }).ToListAsync(ct))
            .GroupBy(a => a.TeacherId)
            .ToDictionary(g => g.Key, g => (Absent: g.Count(a => a.Status == "absent"), Late: g.Count(a => a.Status == "late")));

        var rows = new List<KpiRowDto>();
        foreach (var e in employees)
        {
            var teacherId = teacherByUser.GetValueOrDefault(e.UserId);
            var t = tasks.GetValueOrDefault(e.Id);
            double? taskScore = t.Total == 0 ? null : 100.0 * t.OnTime / t.Total;

            double? laScore = null;
            var observed = 0;
            double? journalScore = null;
            double? attendanceScore = null;
            var (absent, late) = (0, 0);
            if (teacherId is not null)
            {
                if (observations.TryGetValue(teacherId, out var o)) (laScore, observed) = (o.Mean, o.Count);
                if (journal.TryGetValue(teacherId, out var j)) journalScore = Math.Min(100, j);
                (absent, late) = attendance.GetValueOrDefault(teacherId);
                attendanceScore = Math.Max(0, 100 - AbsentStep * absent - LateStep * late);
            }

            var parent = teacherId is null ? [] : parentScores.GetValueOrDefault(teacherId) ?? [];
            var parentNps = PollService.NpsOf(parent).Nps;
            var staff = staffScores.GetValueOrDefault(e.Id) ?? [];
            var staffNps = PollService.NpsOf(staff).Nps;

            var fineCount = fines.Count(f => (teacherId is not null && f.TeacherId == teacherId) || f.UserId == e.UserId);
            double penaltyScore = Math.Max(0, 100 - PenaltyStep * fineCount);

            // Fines and attendance only adjust a measured score — alone they would give a "perfect"
            // 100 to someone the system knows nothing about.
            (double? Score, int Weight)[] work =
            [
                (taskScore, settings.WeightTasks), (laScore, settings.WeightLessonAnalysis), (journalScore, settings.WeightJournal),
                (FromNps(parentNps), settings.WeightParentNps), (FromNps(staffNps), settings.WeightStaffNps),
            ];
            var total = work.Any(p => p.Score is not null && p.Weight > 0)
                ? Total([.. work, (penaltyScore, settings.WeightPenalties), (attendanceScore, settings.WeightAttendance)])
                : null;

            rows.Add(new KpiRowDto(
                e.Id, e.FullName, e.Position, e.HrRole, teacherId, total,
                Round(taskScore), t.OnTime, t.Total,
                Round(laScore), observed,
                journalScore,
                FromNps(parentNps), parentNps, parent.Count,
                FromNps(staffNps), staffNps, staff.Count,
                penaltyScore, fineCount,
                attendanceScore, absent, late));
        }

        // School-wide numbers for the cards: parents' NPS of the school, teachers' NPS of the management.
        var school = await (from a in db.PollAnswers.AsNoTracking()
                            join q in db.PollQuestions.AsNoTracking() on a.QuestionId equals q.Id
                            join r in db.PollResponses.AsNoTracking() on a.ResponseId equals r.Id
                            join p in db.Polls.AsNoTracking() on q.PollId equals p.Id
                            where q.Kind == PollQuestionKind.Nps && q.Target == PollTarget.School && p.Audience == PollAudience.Parents
                                  && a.Score != null && r.SubmittedAt >= since && r.SubmittedAt < until
                            select a.Score!.Value).ToListAsync(ct);
        var management = staffScores.Values.SelectMany(v => v).ToList();

        return new KpiReportDto(month, quarter, ToDto(settings),
            [.. rows.OrderByDescending(r => r.Total ?? -1).ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)],
            PollService.NpsOf(school).Nps, school.Count, PollService.NpsOf(management).Nps, management.Count);
    }
}
