using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  A homeroom teacher sees their own pupils' debts (client, 2026-10-12: "agar o'qituvchi sinf
//  rahbar ham bo'lsa profilida o'zini rahbarligidagi guruh yoki sinfini ko'radimi asosan
//  qarzdorliklar uchun").
//
//  Until now no teacher saw a pupil's money (P1-21, SPEC §4.3) and an ordinary subject teacher
//  still does not. The exception is narrow: only the pupils of the classes the teacher leads
//  (class_homeroom_teachers) and of the track groups they teach (a track group's "rahbar" is its
//  teacher — HomeroomDirectory). Read-only, and only what a reminder needs: WHO owes, the unpaid
//  months and the parent's phone — no payments, no discounts, and NO AMOUNTS (client, 2026-10-30:
//  "sinf rahbarlarida o'quvchi qarzdorligi summasi ko'rinmasin, faqat qarzdor bo'lib ko'rinsin").
//  No sum leaves the server here — not in the JSON, not in the Excel, not in the Telegram caption.
//
//  Who is a debtor comes from the admin "Qarzdorlar" report (FinanceReportQueries.DebtorsAsync), so
//  the teacher and the accountant never disagree on who owes.
// ===========================================================================

/// <summary>The homeroom teacher's pupils with their debts.</summary>
/// <param name="Groups">The classes and track groups the teacher leads, in order ("7-A", "Aniq fanlar").</param>
/// <param name="Pupils">Every pupil, debtors first (longest overdue first), then the rest by name.</param>
public record HomeroomDebtsDto(
    IReadOnlyList<string> Groups, int PupilCount, int DebtorCount,
    IReadOnlyList<HomeroomDebtRowDto> Pupils);

/// <param name="Group">The class or track group the pupil is listed under.</param>
/// <param name="IsDebtor">Owes something — how much is not shown to the teacher (2026-10-30).</param>
/// <param name="DaysOverdue">As in the "Qarzdorlar" report; 0 = not overdue.</param>
/// <param name="LastComment">The latest live debt comment (debtor_actions, 2026-10-29) — the admin's or the
/// homeroom teacher's, it is one history. null = nobody has written yet.</param>
/// <param name="LastCommentAt">When it was written.</param>
/// <param name="LastCommentBy">Who wrote it.</param>
/// <param name="CommentCount">Live comments on the pupil.</param>
public record HomeroomDebtRowDto(
    string StudentId, string FullName, string Group, string? ParentPhone,
    bool IsDebtor, int DaysOverdue, IReadOnlyList<HomeroomUnpaidMonthDto> UnpaidMonths,
    string? LastComment = null, DateTimeOffset? LastCommentAt = null, string? LastCommentBy = null,
    int CommentCount = 0);

/// <summary>One unpaid invoice month (no amount — 2026-10-30).</summary>
public record HomeroomUnpaidMonthDto(DateOnly Month);

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
        if (groups.Count == 0) return new HomeroomDebtsDto([], 0, 0, []);

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

        // Debt comments (client, 2026-10-29: "sinf rahbari ota-ona bilan bog'lanib aniqlashtirib izoh yozishi
        // kerak") — the same debtor_actions the admin's "Qarzdorlar → Izoh" writes and reads.
        var comments = (await db.DebtorActions.AsNoTracking()
                .Where(a => ids.Contains(a.StudentId) && a.DeletedAt == null)
                .Select(a => new { a.StudentId, a.Comment, a.CreatedAt, a.CreatedBy })
                .ToListAsync(ct))
            .GroupBy(a => a.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (Last: g.MaxBy(a => a.CreatedAt)!, Count: g.Count()), StringComparer.Ordinal);
        var authorIds = comments.Values.Select(c => c.Last.CreatedBy).Distinct(StringComparer.Ordinal).ToList();
        var authors = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var rows = pupils
            .Select(p =>
            {
                debtors.TryGetValue(p.Id, out var d);
                var commented = comments.TryGetValue(p.Id, out var c);
                return new HomeroomDebtRowDto(
                    p.Id, p.FullName, p.Group, string.IsNullOrWhiteSpace(p.ParentPhone) ? null : p.ParentPhone,
                    d is not null && d.Debt > 0m, d?.DaysOverdue ?? 0,
                    d is null ? [] : [.. months.GetValueOrDefault(p.Id, []).Select(m => new HomeroomUnpaidMonthDto(m.Month))],
                    commented ? c.Last.Comment : null,
                    commented ? c.Last.CreatedAt : null,
                    commented ? authors.GetValueOrDefault(c.Last.CreatedBy, "Noma'lum") : null,
                    commented ? c.Count : 0);
            })
            .OrderByDescending(r => r.IsDebtor)
            .ThenByDescending(r => r.DaysOverdue)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

        return new HomeroomDebtsDto(
            groups, rows.Count, rows.Count(r => r.IsDebtor), rows);
    }

    // =====================================================================
    //  Filter and Excel (client, 2026-10-12: "sinf va guruh bo'yicha filterlash ... excelga yuklab olish")
    // =====================================================================

    /// <summary>The rows the screen shows: one class / group (null = all of them), debtors only or everyone.</summary>
    public static IReadOnlyList<HomeroomDebtRowDto> Rows(HomeroomDebtsDto dto, string? group, bool everyone) =>
        [.. dto.Pupils.Where(p => (everyone || p.IsDebtor)
                                  && (string.IsNullOrWhiteSpace(group) || string.Equals(p.Group, group, StringComparison.Ordinal)))];

    private static readonly string[] Months =
    [
        "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
        "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr",
    ];

    /// <summary>The same list as a workbook: title, one row per pupil, a total.</summary>
    public static byte[] Workbook(HomeroomDebtsDto dto, string? group, bool everyone, string teacherName)
    {
        var rows = Rows(dto, group, everyone);
        var scope = string.IsNullOrWhiteSpace(group) ? string.Join(", ", dto.Groups) : group;
        var title = $"{teacherName} — {scope} · {(everyone ? "barcha o'quvchilar" : "qarzdorlar")} · "
                    + AppClock.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        IReadOnlyList<string> headers =
            ["№", "O'quvchi", "Sinf / guruh", "Holat", "To'lanmagan oylar", "Kechikkan, kun", "Ota-ona telefoni", "Oxirgi izoh"];
        var table = rows.Select((p, i) => (IReadOnlyList<ExcelExport.XlsxCell>)
        [
            (i + 1).ToString(CultureInfo.InvariantCulture),
            p.FullName,
            p.Group,
            p.IsDebtor ? "Qarzdor" : "Qarzi yo'q",
            string.Join("; ", p.UnpaidMonths.Select(m =>
                $"{Months[m.Month.Month - 1]} {m.Month.Year}")),
            p.DaysOverdue > 0 ? p.DaysOverdue.ToString(CultureInfo.InvariantCulture) : "",
            p.ParentPhone,
            p.LastComment,
        ]).ToList();
        IReadOnlyList<ExcelExport.XlsxCell> totals =
            ["", $"Jami: {rows.Count(r => r.IsDebtor)} ta qarzdor"];

        return ExcelExport.BuildTables(
        [
            new ExcelExport.TableSpec("Qarzdorlar", headers, table, totals, title, [6, 36, 16, 15, 44, 14, 20, 50]),
        ]);
    }

    /// <summary><c>qarzdorlar_7-A_2026-10-12.xlsx</c> — characters Windows refuses become "-".</summary>
    public static string FileName(HomeroomDebtsDto dto, string? group)
    {
        var scope = string.IsNullOrWhiteSpace(group) ? string.Join("-", dto.Groups) : group;
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|', ' ', ',']).ToHashSet();
        var clean = new string([.. scope.Select(c => bad.Contains(c) ? '-' : c)]).Trim('-');
        return $"qarzdorlar_{(clean.Length == 0 ? "sinfim" : clean)}_{AppClock.Today:yyyy-MM-dd}.xlsx";
    }
}
