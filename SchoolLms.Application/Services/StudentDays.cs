namespace SchoolLms.Application.Services;

// ===========================================================================
//  One student, one day (client, 2026-10-07: "7 soat dars bo'lsa va biror o'quvchi umuman kelmasa
//  7 ta odam kelmagan bo'lib chiqyapti ... bitta o'quvchi bir kishiga yurish kerak").
//
//  Absences are stored per lesson (journal_entries.reason_id). Every report that counts PEOPLE
//  collapses a student's held lessons of a date into one day:
//
//    unchecked — none of the day's held lessons was checked for the student;
//    absent    — absent in EVERY checked lesson (did not come that day);
//    partial   — absent in some lessons only — the client decided: counts as present ("kelgan"),
//                shown apart as "dars qoldirgan";
//    present   — otherwise; "late" when any lesson carries a late reason and the day is not absent.
//
//  A held lesson with no row for the student counts as present only when it was marked on the
//  "Davomat belgilash" screen (HeldLessons rule); otherwise that lesson is unchecked.
// ===========================================================================

public sealed class StudentDay
{
    public const string Absent = "absent";
    public const string Partial = "partial";
    public const string Present = "present";
    public const string Unchecked = "unchecked";

    public int Held { get; private set; }
    public int Checked { get; private set; }
    public int AbsentLessons { get; private set; }
    public int LateLessons { get; private set; }

    /// <summary>Absence reason → lessons with it (late reasons excluded).</summary>
    public Dictionary<string, int> Reasons { get; } = new(StringComparer.Ordinal);

    /// <summary>Every reason used that day, late ones too — for "how many days had reason X".</summary>
    public HashSet<string> AllReasons { get; } = new(StringComparer.Ordinal);

    /// <summary>The lesson numbers the student missed that day (2026-10-09: "qaysi darslarda").
    /// Filled only when the caller passes the period.</summary>
    public SortedSet<int> AbsentPeriods { get; } = [];

    /// <summary>The lesson numbers the student was late to.</summary>
    public SortedSet<int> LatePeriods { get; } = [];

    /// <param name="hasRow">The student has a journal row for this lesson.</param>
    /// <param name="marked">The lesson was marked on the daily-marking screen (no row = present).</param>
    /// <param name="period">The lesson number, for <see cref="AbsentPeriods"/>; 0 — not tracked.</param>
    public void Add(bool hasRow, bool marked, string? reasonId, ISet<string> lateIds, int period = 0)
    {
        Held++;
        if (!hasRow && !marked) return;
        Checked++;
        if (reasonId is null) return;
        AllReasons.Add(reasonId);
        if (lateIds.Contains(reasonId))
        {
            LateLessons++;
            if (period > 0) LatePeriods.Add(period);
            return;
        }
        AbsentLessons++;
        if (period > 0) AbsentPeriods.Add(period);
        Reasons[reasonId] = Reasons.GetValueOrDefault(reasonId) + 1;
    }

    public string Status =>
        Checked == 0 ? Unchecked
        : AbsentLessons >= Checked ? Absent
        : AbsentLessons > 0 ? Partial
        : Present;

    /// <summary>Came (present or partial) and was late to at least one lesson.</summary>
    public bool IsLate => LateLessons > 0 && Status is Present or Partial;

    /// <summary>The day's absence reason — the one most of its absent lessons carry.</summary>
    public string? MainReason => Reasons
        .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => kv.Key).FirstOrDefault();
}

/// <summary>Per-day counts of PEOPLE for one owner (class or group) and date.</summary>
/// <param name="Absent">Did not come at all.</param>
/// <param name="Partial">Came, but missed some lessons.</param>
/// <param name="Late">Came and was late at least once.</param>
public sealed record StudentDayCounts(int Absent, int Partial, int Late, int Unchecked)
{
    public static StudentDayCounts Of(IEnumerable<StudentDay> days)
    {
        var list = days.ToList();
        return new StudentDayCounts(
            list.Count(d => d.Status == StudentDay.Absent),
            list.Count(d => d.Status == StudentDay.Partial),
            list.Count(d => d.IsLate),
            list.Count(d => d.Status == StudentDay.Unchecked));
    }
}

public static class StudentDays
{
    /// <summary>
    /// The students' days for one owner and one date: each held lesson applies to a student when
    /// it is the whole class (<c>SubGroup = 0</c>) or the student's own half.
    /// </summary>
    /// <param name="rows">(subject, period, student) → reason of the journal row (null = present).</param>
    public static Dictionary<string, StudentDay> Build(
        IEnumerable<HeldLesson> held,
        IEnumerable<(string Id, int SubGroup)> students,
        IReadOnlyDictionary<(string SubjectId, int Period, string StudentId), string?> rows,
        ISet<string> lateIds)
    {
        var lessons = held.ToList();
        var result = new Dictionary<string, StudentDay>(StringComparer.Ordinal);
        foreach (var (id, subGroup) in students)
        {
            var day = new StudentDay();
            foreach (var l in lessons.Where(l => l.SubGroup == 0 || l.SubGroup == subGroup))
            {
                var has = rows.TryGetValue((l.SubjectId, l.Period, id), out var reason);
                day.Add(has, l.Marked, reason, lateIds);
            }
            if (day.Held > 0) result[id] = day;
        }
        return result;
    }
}
