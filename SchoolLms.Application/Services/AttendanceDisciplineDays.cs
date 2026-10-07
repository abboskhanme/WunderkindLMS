namespace SchoolLms.Application.Services;

// ===========================================================================
//  Attendance marks as DISCIPLINE events — once per student, day and reason (client, 2026-10-07:
//  "bitta o'quvchi bir kishiga yurish kerak"; points "kuniga bir marta").
//
//  The journal holds one row per lesson, so a pupil absent from all seven lessons of a day carries
//  seven "sababsiz" rows. As a discipline event that is ONE absence: the reason's points apply once
//  for that day (−5, not −35), a late mark likewise. Two different reasons on one day (late in the
//  first lesson, then sababsiz) are two events.
//
//  Every place that turns journal marks into points or discipline counts goes through here —
//  Ballar nazorati (+ Excel), the points history, Harakatlar, the attendance-discipline report,
//  the student profile, the student portal and the MCP tool — so the rule lives in one spot.
// ===========================================================================

public static class AttendanceDisciplineDays
{
    /// <summary>One journal row with a reason: its id (the archive link), the pupil, the day and the reason.</summary>
    public sealed record Mark(string Id, string StudentId, string Date, string ReasonId);

    /// <summary>
    /// One discipline event: a pupil, a day, a reason. <see cref="Id"/> is the first journal row of the
    /// group (stable — the smallest id), <see cref="Lessons"/> how many lessons it covered.
    /// </summary>
    public sealed record Day(string Id, string StudentId, string Date, string ReasonId, int Lessons);

    /// <summary>Collapses lesson marks to one per (student, date, reason).</summary>
    public static List<Day> Collapse(IEnumerable<Mark> marks) =>
    [
        .. marks
            .GroupBy(m => (m.StudentId, m.Date, m.ReasonId))
            .Select(g => new Day(
                g.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal).First(),
                g.Key.StudentId, g.Key.Date, g.Key.ReasonId, g.Count())),
    ];

    /// <summary>The history note of an attendance event — how many lessons it covered when more than one.</summary>
    public static string Note(int lessons) => lessons > 1 ? $"Jurnal davomati · {lessons} dars" : "Jurnal davomati";
}
