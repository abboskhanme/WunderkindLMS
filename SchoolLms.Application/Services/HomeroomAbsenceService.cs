using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  "Sinfim · Kelmaganlar" — the homeroom teacher explains a missed day (mijoz, 2026-10-26:
//  "sinf rahbarlari o'zlariga biriktirilgan o'quvchilar kelmagan kunda ular nega kelmagani
//  haqida izoh yoza olsin ... izoh ham sabab ham o'zgartira olsin").
//
//    · The list is the day as the attendance report sees it (AttendanceAnalytics.BuildAsync —
//      one definition of "absent" and "some lessons"), narrowed to my pupils
//      (HomeroomDirectory.PupilsAsync — the red list's circle).
//    · The comment is one row per pupil and day (absence_comments); empty text removes it.
//    · A new reason is written into the day's JOURNAL rows that carry an absence reason — the
//      journal stays the only source, so the percentage, discipline points, the parent's journal
//      and the red list all follow. Late marks are left alone: the pupil was in class.
//    · Another class's pupil answers 404, a day that is not a missed day 409. Every change is
//      audited (AttendanceDay).
// ===========================================================================

public sealed class HomeroomAbsenceService(IAppDbContext db)
{
    public const int MaxCommentLength = 1000;

    /// <summary>How far back a day may be opened — enough for "last week's Monday", not for history.</summary>
    public const int MaxDaysBack = 60;

    /// <summary>The missed days of my pupils on <paramref name="date"/>.</summary>
    public async Task<HomeroomAbsencesDto> ListAsync(string teacherId, DateOnly date, CancellationToken ct = default)
    {
        CheckDate(date);
        var (groups, groupOf) = await HomeroomDirectory.PupilsAsync(db, teacherId, ct);
        var reasons = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var reasonRows = reasons
            .Where(r => !r.IsLate)
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new HomeroomAbsenceReasonDto(r.Id, r.Name, AttendanceAnalytics.IsUnexcusedReason(r)))
            .ToList();
        if (groupOf.Count == 0) return new HomeroomAbsencesDto(date, groups, reasonRows, []);

        var rows = await RowsAsync(groupOf, date, reasons, ct);
        return new HomeroomAbsencesDto(date, groups, reasonRows, rows);
    }

    /// <summary>Saves the reason and/or the comment of one pupil's missed day; returns the row as it is now.</summary>
    public async Task<HomeroomAbsenceDto> SaveAsync(
        Teacher teacher, string studentId, DateOnly date, SaveHomeroomAbsenceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        CheckDate(date);
        if (string.IsNullOrEmpty(teacher.UserId))
            throw new AttendanceCaseException(403, "O'qituvchi akkaunti topilmadi.");

        var (_, groupOf) = await HomeroomDirectory.PupilsAsync(db, teacher.Id, ct);
        // Another class's pupil answers exactly like a missing one.
        if (!groupOf.TryGetValue(studentId, out var group))
            throw new AttendanceCaseException(404, "O'quvchi topilmadi.");

        var comment = (req.Comment ?? string.Empty).Trim();
        if (comment.Length > MaxCommentLength)
            throw new AttendanceCaseException(400, $"Izoh {MaxCommentLength} belgidan oshmasin.");

        var reasons = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var reasonId = string.IsNullOrWhiteSpace(req.ReasonId) ? null : req.ReasonId.Trim();
        var newReason = reasonId is null ? null : reasons.FirstOrDefault(r => r.Id == reasonId);
        if (reasonId is not null && (newReason is null || newReason.IsLate))
            throw new AttendanceCaseException(400, "Kelmaganlik sababini ro'yxatdan tanlang.");

        var scope = new Dictionary<string, string>(StringComparer.Ordinal) { [studentId] = group };
        var before = (await RowsAsync(scope, date, reasons, ct)).FirstOrDefault()
            ?? throw new AttendanceCaseException(409, "Bu kuni o'quvchi kelmagan deb belgilanmagan.");

        var iso = Iso(date);
        var lateIds = reasons.Where(r => r.IsLate).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var reasonChanged = false;
        if (newReason is not null)
        {
            var entries = await db.JournalEntries
                .Where(e => e.StudentId == studentId && e.Date == iso && e.ReasonId != null)
                .ToListAsync(ct);
            foreach (var e in entries.Where(e => !lateIds.Contains(e.ReasonId!) && e.ReasonId != newReason.Id))
            {
                e.ReasonId = newReason.Id;
                reasonChanged = true;
            }
        }

        var row = await db.AbsenceComments.FirstOrDefaultAsync(c => c.StudentId == studentId && c.Date == date, ct);
        var commentChanged = comment != (row?.Comment ?? string.Empty);
        if (commentChanged)
        {
            if (comment.Length == 0)
            {
                db.AbsenceComments.Remove(row!);
            }
            else
            {
                if (row is null)
                {
                    row = new AbsenceComment { StudentId = studentId, Date = date };
                    db.AbsenceComments.Add(row);
                }
                row.Comment = comment;
                row.UpdatedBy = teacher.UserId;
                row.UpdatedByName = teacher.FullName;
                row.UpdatedAt = AppClock.NowInstant;
            }
        }

        if (!reasonChanged && !commentChanged) return before;

        var names = reasons.ToDictionary(r => r.Id, r => r.Name, StringComparer.Ordinal);
        string? Name(string? id) => id is null ? null : names.GetValueOrDefault(id);
        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityAttendanceDay, $"{studentId}:{iso}", "update",
            $"Kelmagan kun ({iso}) — {before.FullName}: "
                + string.Join(", ", new[]
                {
                    reasonChanged ? $"sabab «{Name(before.ReasonId) ?? "—"}» → «{newReason!.Name}»" : null,
                    commentChanged ? (comment.Length == 0 ? "izoh o'chirildi" : "izoh yozildi") : null,
                }.Where(s => s is not null)),
            teacher.UserId, teacher.FullName,
            before: new { Sabab = Name(before.ReasonId), Izoh = before.Comment },
            after: new { Sabab = reasonChanged ? newReason!.Name : Name(before.ReasonId), Izoh = comment.Length == 0 ? null : comment },
            studentId: studentId, teacherId: teacher.Id));

        await db.SaveChangesAsync(ct);
        return (await RowsAsync(scope, date, reasons, ct)).First();
    }

    // ------------------------------------------------------------------

    /// <summary>The missed days (whole or partial) of the given pupils on one date, by group then name.</summary>
    private async Task<List<HomeroomAbsenceDto>> RowsAsync(
        IReadOnlyDictionary<string, string> groupOf, DateOnly date, List<AbsenceReason> reasons, CancellationToken ct)
    {
        var iso = Iso(date);
        var unexcused = reasons.Where(AttendanceAnalytics.IsUnexcusedReason).Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);
        var days = new Dictionary<string, StudentDay>(StringComparer.Ordinal);
        await AttendanceAnalytics.BuildAsync(db, null, iso, iso, iso, maxEvents: 0,
            onStudentDay: (studentId, _, day) =>
            {
                if (groupOf.ContainsKey(studentId)
                    && day.Status is StudentDay.Absent or StudentDay.Partial)
                    days[studentId] = day;
            });
        if (days.Count == 0) return [];

        var ids = days.Keys.ToList();
        var names = await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, StringComparer.Ordinal, ct);
        var comments = await db.AbsenceComments.AsNoTracking()
            .Where(c => c.Date == date && ids.Contains(c.StudentId))
            .ToDictionaryAsync(c => c.StudentId, StringComparer.Ordinal, ct);

        return [.. days
            .Where(kv => names.ContainsKey(kv.Key))
            .Select(kv =>
            {
                var day = kv.Value;
                var c = comments.GetValueOrDefault(kv.Key);
                return new HomeroomAbsenceDto(
                    kv.Key, names[kv.Key], groupOf[kv.Key], day.Status, [.. day.AbsentPeriods], day.Held,
                    day.MainReason, day.MainReason is { } main && unexcused.Contains(main),
                    c?.Comment, c?.UpdatedByName, c?.UpdatedAt);
            })
            .OrderBy(r => r.Group, StringComparer.Ordinal)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)];
    }

    private static void CheckDate(DateOnly date)
    {
        var today = AppClock.Today;
        if (date > today)
            throw new AttendanceCaseException(400, "Kelajakdagi kun tanlab bo'lmaydi.");
        if (date < today.AddDays(-MaxDaysBack))
            throw new AttendanceCaseException(400, $"Faqat oxirgi {MaxDaysBack} kun ichidagi kunlar.");
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
