using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  A homeroom teacher sees their own pupils' debts (client, 2026-10-12: "agar o'qituvchi sinf
//  rahbar ham bo'lsa profilida o'zini rahbarligidagi guruh yoki sinfini ko'radimi asosan
//  qarzdorliklar uchun").
//
//  Until now no teacher saw a pupil's money (P1-21, SPEC §4.3) and an ordinary subject teacher
//  still does not. The exception is narrow: only the pupils of the classes the teacher leads
//  (class_homeroom_teachers) and of the track groups they teach (a track group's "rahbar" is its
//  teacher — HomeroomDirectory). Read-only, and only what a reminder needs: the debt, the unpaid
//  months and the parent's phone — no payments, no discounts.
//
//  The debt is the admin "Qarzdorlar" report's own number (FinanceReportQueries.DebtorsAsync), so
//  the teacher and the accountant never see two different amounts for one pupil.
// ===========================================================================

/// <summary>The homeroom teacher's pupils with their debts.</summary>
/// <param name="Groups">The classes and track groups the teacher leads, in order ("7-A", "Aniq fanlar").</param>
/// <param name="Pupils">Every pupil, debtors first (largest debt first), then the rest by name.</param>
public record HomeroomDebtsDto(
    IReadOnlyList<string> Groups, int PupilCount, int DebtorCount, decimal TotalDebt,
    IReadOnlyList<HomeroomDebtRowDto> Pupils);

/// <param name="Group">The class or track group the pupil is listed under.</param>
/// <param name="Debt">0 = owes nothing.</param>
/// <param name="DaysOverdue">As in the "Qarzdorlar" report; 0 = not overdue.</param>
public record HomeroomDebtRowDto(
    string StudentId, string FullName, string Group, string? ParentPhone,
    decimal Debt, int DaysOverdue, IReadOnlyList<HomeroomUnpaidMonthDto> UnpaidMonths);

/// <summary>One unpaid invoice month and what is still owed for it.</summary>
public record HomeroomUnpaidMonthDto(DateOnly Month, decimal Amount);

/// <summary>Builds <see cref="HomeroomDebtsDto"/> for one teacher. File header.</summary>
public static class HomeroomDebts
{
    public static async Task<HomeroomDebtsDto> ForTeacherAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default)
    {
        // Who the teacher leads: classes first (grade, name), then track groups (name).
        var classes = await HomeroomDirectory.ClassesOfAsync(db, teacherId, ct);
        var classNames = classes.Select(c => c.Name).ToList();
        var tracks = await (from gt in db.StudyGroupTeachers.AsNoTracking()
                            join g in db.StudyGroups.AsNoTracking() on gt.GroupId equals g.Id
                            where gt.TeacherId == teacherId && g.IsTrack && !g.IsArchived
                            orderby g.Name
                            select new { g.Id, g.Name }).ToListAsync(ct);
        var groups = classNames.Concat(tracks.Select(t => t.Name)).Distinct(StringComparer.Ordinal).ToList();
        if (groups.Count == 0) return new HomeroomDebtsDto([], 0, 0, 0m, []);

        // Pupils, each once, under the first of their groups.
        var classPupils = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && classNames.Contains(s.ClassName))
            .Select(s => new { s.Id, s.FullName, s.ParentPhone, Group = s.ClassName })
            .ToListAsync(ct);
        var trackIds = tracks.Select(t => t.Id).ToList();
        var trackNames = tracks.ToDictionary(t => t.Id, t => t.Name);
        var trackPupils = (await (from m in db.StudyGroupMembers.AsNoTracking()
                                  join s in db.Students.AsNoTracking() on m.StudentId equals s.Id
                                  where trackIds.Contains(m.GroupId) && m.LeftOn == null && !s.IsArchived
                                  select new { s.Id, s.FullName, s.ParentPhone, m.GroupId }).ToListAsync(ct))
            .Select(x => new { x.Id, x.FullName, x.ParentPhone, Group = trackNames[x.GroupId] });

        var order = groups.Select((g, i) => (g, i)).ToDictionary(x => x.g, x => x.i, StringComparer.Ordinal);
        var pupils = classPupils.Concat(trackPupils)
            .OrderBy(p => order[p.Group])
            .DistinctBy(p => p.Id)
            .ToList();
        var ids = pupils.Select(p => p.Id).ToList();

        var reports = new FinanceReportQueries(db);
        var debtors = (await reports.DebtorsAsync(
                new DebtorReportQuery(IncludeArchived: false, StudentIds: ids), ct))
            .ToDictionary(d => d.StudentId, StringComparer.Ordinal);
        var months = await reports.UnpaidMonthsAsync(ids, ct);

        var rows = pupils
            .Select(p =>
            {
                debtors.TryGetValue(p.Id, out var d);
                return new HomeroomDebtRowDto(
                    p.Id, p.FullName, p.Group, string.IsNullOrWhiteSpace(p.ParentPhone) ? null : p.ParentPhone,
                    d?.Debt ?? 0m, d?.DaysOverdue ?? 0,
                    d is null ? [] : [.. months.GetValueOrDefault(p.Id, []).Select(m => new HomeroomUnpaidMonthDto(m.Month, m.Amount))]);
            })
            .OrderByDescending(r => r.Debt)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

        return new HomeroomDebtsDto(
            groups, rows.Count, rows.Count(r => r.Debt > 0m), rows.Sum(r => r.Debt), rows);
    }
}
