using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  "Fanlar kesimida" — pupils × subjects for one period (unit B4).
//  Spec: docs/modules/admission-and-testing.md §3.3 screen 12, §6.6.
// ===========================================================================
//
//  A REPORT, NOT A TABLE (§10 "Reports, not storage"). Everything here is read
//  from `seasonal_marks`, `students`, `classes` and `subjects`; nothing is
//  written.
//
//  WHO IS A ROW
//  ------------
//  The page says "Tanlangan sinflarda o'quvchi topilmadi" when the answer is
//  empty — the rows are the PUPILS of the chosen classes, marked or not, so a
//  gap shows as "—" instead of the pupil vanishing. Two sources, one row each:
//
//    1. the active pupils in the chosen classes today (the class rule of
//       `LessonRoster.Query`: `students.class_name = classes.name`), and
//    2. pupils holding a mark of THIS period whose class snapshot is one of the
//       chosen classes — pupils who have since moved class or been archived.
//       Without them a past period would silently lose every pupil who left.
//
//  A pupil in both keeps their class of today. A cell shows the pupil's mark in
//  that subject for the period, whichever class it was entered in: the mark is
//  unique per (pupil, subject, period), the class on it is a snapshot (§8.6).
//
//  SIZE
//  ----
//  The row set is bounded by the chosen classes (tens of pupils each), so it is
//  built and ordered in memory, and scores are fetched for the requested page
//  only — one query, never one per pupil.
// ===========================================================================

/// <summary>The §6.6 pivot, paged for the screen or whole for the export.</summary>
public sealed class SeasonalPivotQuery(IAppDbContext db)
{
    /// <summary>More than any school has; a bound on the IN lists.</summary>
    public const int MaxClasses = 100;

    /// <summary>A bound on the width of the sheet.</summary>
    public const int MaxSubjects = 50;

    public const string ClassesRequiredMessage = "Kamida bitta sinf tanlang";
    public const string SubjectsRequiredMessage = "Kamida bitta fan tanlang";
    public const string ClassMissingMessage = "Tanlangan sinflardan biri topilmadi — sahifani yangilang";
    public const string SubjectMissingMessage = "Tanlangan fanlardan biri topilmadi — sahifani yangilang";

    public static string TooManyClassesMessage => $"Bir vaqtda ko'pi bilan {MaxClasses} ta sinf tanlanadi";

    public static string TooManySubjectsMessage => $"Bir vaqtda ko'pi bilan {MaxSubjects} ta fan tanlanadi";

    /// <summary>One page of pupils with their scores.</summary>
    public async Task<SeasonalPivotPageDto> PageAsync(SeasonalPivotFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (page, limit) = SeasonalMarkService.Paging(filter.Page, filter.Limit);

        var sheet = await SheetAsync(filter, ct);
        var window = sheet.Pupils.Skip((page - 1) * limit).Take(limit).ToList();
        var items = await WithScoresAsync(sheet, window, ct);
        return new SeasonalPivotPageDto(sheet.Columns, items, sheet.Pupils.Count, page, limit);
    }

    /// <summary>Every row of the selection — the export.</summary>
    public async Task<(IReadOnlyList<SeasonalPivotColumnDto> Columns, IReadOnlyList<SeasonalPivotRowDto> Rows)> AllAsync(
        SeasonalPivotFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var sheet = await SheetAsync(filter, ct);
        return (sheet.Columns, await WithScoresAsync(sheet, sheet.Pupils, ct));
    }

    // ---------------------------------------------------------------------

    private sealed record PivotPupil(string Id, string FullName, string ClassName, int Grade);

    private sealed record Sheet(
        SeasonalPeriod Period,
        List<string> SubjectIds,
        List<SeasonalPivotColumnDto> Columns,
        List<PivotPupil> Pupils);

    /// <summary>Validates the selection and builds the ordered row list (no scores yet).</summary>
    private async Task<Sheet> SheetAsync(SeasonalPivotFilter f, CancellationToken ct)
    {
        var period = SeasonalPeriod.From(f);
        var classIds = Ids(f.ClassIds);
        var subjectIds = Ids(f.SubjectIds);
        if (classIds.Count == 0) throw SeasonalMarkException.Invalid(ClassesRequiredMessage);
        if (subjectIds.Count == 0) throw SeasonalMarkException.Invalid(SubjectsRequiredMessage);
        if (classIds.Count > MaxClasses) throw SeasonalMarkException.Invalid(TooManyClassesMessage);
        if (subjectIds.Count > MaxSubjects) throw SeasonalMarkException.Invalid(TooManySubjectsMessage);

        var classes = await db.Classes.AsNoTracking()
            .Where(c => classIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Grade })
            .ToListAsync(ct);
        if (classes.Count != classIds.Count) throw SeasonalMarkException.NotFound(ClassMissingMessage);

        var columns = await db.Subjects.AsNoTracking()
            .Where(s => subjectIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .Select(s => new SeasonalPivotColumnDto(s.Id, s.Name))
            .ToListAsync(ct);
        if (columns.Count != subjectIds.Count) throw SeasonalMarkException.NotFound(SubjectMissingMessage);

        // 1. Today's active pupils of the chosen classes.
        var names = classes.Select(c => c.Name).ToList();
        var gradeByName = classes
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Grade, StringComparer.Ordinal);
        var current = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && names.Contains(s.ClassName))
            .Select(s => new { s.Id, s.FullName, s.ClassName })
            .ToListAsync(ct);

        var pupils = current.ToDictionary(
            s => s.Id,
            s => new PivotPupil(s.Id, s.FullName, s.ClassName, gradeByName.GetValueOrDefault(s.ClassName)),
            StringComparer.Ordinal);

        // 2. Pupils marked in these classes this period who are no longer on that roster.
        var key = period.Key;
        var departed = await (
            from m in db.SeasonalMarks.AsNoTracking()
            where m.PeriodKey == key && classIds.Contains(m.ClassId) && subjectIds.Contains(m.SubjectId)
            join s in db.Students.AsNoTracking() on m.StudentId equals s.Id
            select new { s.Id, s.FullName, m.ClassId })
            .Distinct()
            .ToListAsync(ct);

        var classById = classes.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var d in departed.OrderBy(d => classById[d.ClassId].Grade).ThenBy(d => classById[d.ClassId].Name))
        {
            if (pupils.ContainsKey(d.Id)) continue;
            var cls = classById[d.ClassId];
            pupils[d.Id] = new PivotPupil(d.Id, d.FullName, cls.Name, cls.Grade);
        }

        var ordered = pupils.Values
            .OrderBy(p => p.Grade)
            .ThenBy(p => p.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        return new Sheet(period, subjectIds, columns, ordered);
    }

    /// <summary>The scores of the given pupils — one query for the whole window.</summary>
    private async Task<List<SeasonalPivotRowDto>> WithScoresAsync(
        Sheet sheet, IReadOnlyList<PivotPupil> window, CancellationToken ct)
    {
        if (window.Count == 0) return [];

        var key = sheet.Period.Key;
        var ids = window.Select(p => p.Id).ToList();
        var subjectIds = sheet.SubjectIds;
        var marks = await db.SeasonalMarks.AsNoTracking()
            .Where(m => m.PeriodKey == key && m.Score != null
                        && ids.Contains(m.StudentId) && subjectIds.Contains(m.SubjectId))
            .Select(m => new { m.StudentId, m.SubjectId, Score = m.Score!.Value })
            .ToListAsync(ct);

        var byPupil = marks
            .GroupBy(m => m.StudentId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, decimal>)g.ToDictionary(m => m.SubjectId, m => m.Score, StringComparer.Ordinal),
                StringComparer.Ordinal);

        var none = new Dictionary<string, decimal>(StringComparer.Ordinal);
        return [.. window.Select(p => new SeasonalPivotRowDto(
            p.Id, p.FullName, p.ClassName, byPupil.GetValueOrDefault(p.Id) ?? none))];
    }

    /// <summary>Trimmed, non-empty, distinct ids in the order they came.</summary>
    private static List<string> Ids(IEnumerable<string>? raw) =>
        [.. (raw ?? []).Select(SeasonalMarkService.Clean).OfType<string>().Distinct(StringComparer.Ordinal)];
}
