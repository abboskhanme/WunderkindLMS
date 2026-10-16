using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  "O'quvchilar oqimi" — who came, who started paying, who left and why
//  (docs/modules/student-flow.md, 2026-10-15).
//
//  Arrivals, departures and returns are `student_events` rows; "activated" is not stored —
//  it is the pupil's FIRST payment (not a reversal row), the same rule as the dashboard's
//  "Birinchi to'lov qilganlar". Headcounts are walked back from today: active pupils now,
//  minus what happened after the day in question.
//
//  With a class filter the walk uses only arrivals, departures and returns in that class —
//  moves between classes are not events, so a class headcount is an estimate (the doc says so).
// ===========================================================================

public static class StudentFlowReport
{
    /// <summary>Longest period the report accepts — three school years.</summary>
    public const int MaxDays = 1100;

    public const string OtherReason = "Boshqa";
    public const string NoClass = "Sinfsiz";

    public static async Task<StudentFlowReportDto> BuildAsync(
        IAppDbContext db, DateOnly from, DateOnly to, string? classId, CancellationToken ct = default)
    {
        var cls = string.IsNullOrEmpty(classId) ? null
            : await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct);
        (string Id, string Name)? scoped = cls is null ? null : (cls.Id, cls.Name);

        var events = await InClass(db.StudentEvents.AsNoTracking(), scoped)
            .Where(e => e.OnDate >= from && e.OnDate <= to)
            .OrderBy(e => e.OnDate).ThenBy(e => e.CreatedAt)
            .ToListAsync(ct);

        // ----- headcounts: today's active pupils, walked back -----
        var activeNow = cls is null
            ? await db.Students.CountAsync(s => !s.IsArchived, ct)
            : await db.Students.CountAsync(s => !s.IsArchived && s.ClassName == cls.Name, ct);
        var later = await InClass(db.StudentEvents.AsNoTracking(), scoped)
            .Where(e => e.OnDate > to)
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int LaterOf(string kind) => later.FirstOrDefault(x => x.Kind == kind)?.Count ?? 0;
        var headcountEnd = activeNow - (LaterOf(StudentEventKind.Enrolled) + LaterOf(StudentEventKind.Returned) - LaterOf(StudentEventKind.Left));

        var enrolled = events.Where(e => e.Kind == StudentEventKind.Enrolled).ToList();
        var left = events.Where(e => e.Kind == StudentEventKind.Left).ToList();
        var returned = events.Where(e => e.Kind == StudentEventKind.Returned).ToList();
        var net = enrolled.Count + returned.Count - left.Count;

        // ----- activated: first payment in the period -----
        var startAt = AppClock.InstantOn(from);
        var endAt = AppClock.InstantOn(to.AddDays(1));
        var firstPayments = await db.Payments.AsNoTracking()
            .Where(p => p.ReversalOf == null)
            .GroupBy(p => p.StudentId)
            .Select(g => new { StudentId = g.Key, First = g.Min(p => p.ReceivedAt) })
            .Where(x => x.First >= startAt && x.First < endAt)
            .ToListAsync(ct);
        var payerIds = firstPayments.Select(x => x.StudentId).ToList();
        var payers = await db.Students.AsNoTracking()
            .Where(s => payerIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName })
            .ToDictionaryAsync(s => s.Id, ct);
        var activated = firstPayments
            .Where(x => payers.ContainsKey(x.StudentId)
                        && (cls is null || payers[x.StudentId].ClassName == cls.Name))
            .OrderBy(x => x.First)
            .Select(x => new StudentFlowRowDto(
                null, x.StudentId, payers[x.StudentId].FullName, Blank(payers[x.StudentId].ClassName),
                Iso(AppClock.LocalDateOf(x.First)), null, null, null, "payment", null, true))
            .ToList();

        // ----- lookups for the lists -----
        var reasons = await db.StudentArchiveReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var ids = events.Select(e => e.StudentId).Distinct().ToList();
        var existing = (await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var grades = (await db.Classes.AsNoTracking().Select(c => new { c.Name, c.Grade }).ToListAsync(ct))
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Grade, StringComparer.Ordinal);

        string? ReasonName(Guid? id) => id is { } r ? reasons.GetValueOrDefault(r) : null;
        StudentFlowRowDto Row(StudentEvent e) => new(
            e.Id, e.StudentId, e.StudentName, e.ClassName, Iso(e.OnDate),
            e.ReasonId, ReasonName(e.ReasonId), e.ReasonText, e.Source, e.ActorName, existing.Contains(e.StudentId));

        // ----- by reason: catalog name; none (or a name no longer in the catalog) → "Boshqa" -----
        var byReason = left
            .GroupBy(e => ReasonName(e.ReasonId) ?? OtherReason)
            .Select(g => new StudentFlowReasonDto(
                g.Key == OtherReason ? null : g.First().ReasonId, g.Key, g.Count(),
                Math.Round(100.0 * g.Count() / left.Count, 1)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();

        // ----- by class (the class at the moment of the event) -----
        var byClass = events
            .GroupBy(e => Blank(e.ClassName) ?? NoClass)
            .Select(g =>
            {
                var n = g.Count(e => e.Kind == StudentEventKind.Enrolled);
                var l = g.Count(e => e.Kind == StudentEventKind.Left);
                var r = g.Count(e => e.Kind == StudentEventKind.Returned);
                return new StudentFlowClassDto(g.Select(e => e.ClassId).FirstOrDefault(id => id != null), g.Key, n, l, r, n + r - l);
            })
            .OrderBy(c => c.ClassName == NoClass ? 1 : 0)
            .ThenBy(c => grades.GetValueOrDefault(c.ClassName, int.MaxValue))
            .ThenBy(c => c.ClassName, StringComparer.CurrentCulture)
            .ToList();

        return new StudentFlowReportDto(
            Iso(from), Iso(to), cls?.Id, cls?.Name,
            new StudentFlowTotalsDto(
                enrolled.Count, activated.Count, left.Count, returned.Count, net,
                headcountEnd - net, headcountEnd),
            byReason, byClass,
            [.. enrolled.Select(Row)], activated, [.. left.Select(Row)], [.. returned.Select(Row)]);
    }

    /// <summary>
    /// The card's enrolment day was edited — the pupil's first arrival follows it (no row — nothing
    /// to move). Tracked: the caller's SaveChanges writes it.
    /// </summary>
    public static async Task MoveEnrolmentAsync(IAppDbContext db, string studentId, DateOnly day, CancellationToken ct = default)
    {
        var first = await db.StudentEvents
            .Where(e => e.StudentId == studentId && e.Kind == StudentEventKind.Enrolled)
            .OrderBy(e => e.OnDate).ThenBy(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (first is not null) first.OnDate = day;
    }

    /// <summary>Events in one class: by id, or by name when the id was unknown (backfill, deleted class).</summary>
    private static IQueryable<StudentEvent> InClass(IQueryable<StudentEvent> q, (string Id, string Name)? cls) =>
        cls is not { } c ? q : q.Where(e => e.ClassId == c.Id || (e.ClassId == null && e.ClassName == c.Name));

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
