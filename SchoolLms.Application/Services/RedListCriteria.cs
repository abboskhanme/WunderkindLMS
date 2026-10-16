using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  When does a pupil go on the red list (docs/modules/attendance-red-list.md, 2026-10-15)?
//
//  The input is the attendance report's own pupil-days (StudentDay — one per pupil and date, the
//  rule every attendance screen uses). Each becomes one of three kinds:
//
//    Missed  — absent the whole day with an unexcused main reason (any reason when the school
//              counts excused days too) — the same "sababsiz kun" the absentees card counts;
//    Excused — absent the whole day with an excused reason, not counted: it ends a run;
//    Came    — present or partly present (missed some lessons): ends a run.
//
//  A day nobody checked, and a day without lessons (weekend, holiday), is not a day at all: it
//  neither extends nor ends a run.
//
//  Two rules, either opens a case:
//    consecutive — the latest run of Missed days is at least ConsecutiveDays long and ended inside
//                  the window (an old run the pupil has long recovered from does not open one);
//    window      — at least WindowUnexcused Missed days in the last WindowDays calendar days.
//
//  After a case is resolved only days AFTER that case's last counted absence are read for the
//  pupil, so resolving it does not reopen it on the next scan; a new run does.
// ===========================================================================

/// <summary>A pupil-day as the red list sees it.</summary>
public enum RedListDay
{
    Missed,
    Excused,
    Came,
}

/// <summary>One pupil's numbers as of a scan.</summary>
/// <param name="LatestRun">Length of the run of Missed days that ends on <see cref="LastAbsentOn"/>.</param>
/// <param name="AbsentDates">Missed days, newest first (at most <see cref="RedListCriteria.MaxAbsentDates"/>).</param>
public sealed record RedListVerdict(
    int LatestRun, int WindowCount, DateOnly? LastAbsentOn, DateOnly? LastPresentOn,
    IReadOnlyList<DateOnly> AbsentDates, bool ConsecutiveMet, bool WindowMet)
{
    public bool Opens => ConsecutiveMet || WindowMet;

    /// <summary>Which rule opens the case — consecutive when both are met (the sharper signal).</summary>
    public string Trigger => ConsecutiveMet ? AttendanceCaseTrigger.Consecutive : AttendanceCaseTrigger.Window;
}

/// <summary>The red-list rules — pure, so every edge is testable without a database. File header.</summary>
public static class RedListCriteria
{
    /// <summary>How many absence dates a case keeps for the screens.</summary>
    public const int MaxAbsentDates = 20;

    /// <summary>The kind of a counted pupil-day; null — nothing was checked that day (neutral).</summary>
    public static RedListDay? Classify(StudentDay day, ISet<string> unexcusedReasonIds, bool countExcused) =>
        day.Status switch
        {
            StudentDay.Unchecked => null,
            StudentDay.Absent => countExcused || (day.MainReason is { } reason && unexcusedReasonIds.Contains(reason))
                ? RedListDay.Missed
                : RedListDay.Excused,
            _ => RedListDay.Came,
        };

    /// <param name="days">The pupil's classified days (any order; neutral days are simply absent).</param>
    /// <param name="today">The scan's school day; later days are ignored.</param>
    /// <param name="since">Only days after this one count (the last resolved case's last absence); null — all.</param>
    public static RedListVerdict Evaluate(
        IEnumerable<(DateOnly Date, RedListDay Kind)> days, DateOnly today, AttendanceRedListSettings settings,
        DateOnly? since)
    {
        var list = days
            .Where(d => d.Date <= today && (since is null || d.Date > since.Value))
            .OrderByDescending(d => d.Date)
            .ToList();

        DateOnly? lastAbsent = null, lastPresent = null;
        var latestRun = 0;
        var runOpen = false;
        foreach (var (date, kind) in list)
        {
            if (kind == RedListDay.Came) lastPresent ??= date;
            if (lastAbsent is null)
            {
                if (kind != RedListDay.Missed) continue;
                lastAbsent = date;
                latestRun = 1;
                runOpen = true;
                continue;
            }
            if (!runOpen) continue;
            if (kind == RedListDay.Missed) latestRun++;
            else runOpen = false;
        }

        var windowStart = today.AddDays(-(settings.WindowDays - 1));
        var missed = list.Where(d => d.Kind == RedListDay.Missed).Select(d => d.Date).ToList();
        var windowCount = missed.Count(d => d >= windowStart);
        var recent = lastAbsent is { } last && last >= windowStart;

        return new RedListVerdict(
            latestRun, windowCount, lastAbsent, lastPresent,
            [.. missed.Take(MaxAbsentDates)],
            ConsecutiveMet: recent && latestRun >= settings.ConsecutiveDays,
            WindowMet: windowCount >= settings.WindowUnexcused);
    }
}
