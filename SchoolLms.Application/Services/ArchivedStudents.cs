using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  An archived pupil is no longer the school's (client, 2026-10-08: "arxivdagi o'quvchi uchun to'lov
//  qilish, chegirma yoki abonement biriktirish yoki sinfga qo'shish butkul taqiqlansin ... arxivdagi
//  o'quvchilar bilan deyarli amallar bajarish mumkin bo'lmasligi kerak ... tizimga ham kirolmasligi").
//
//  Locked: anything that creates or raises something for the pupil — a payment, a discount, an
//  abonement or its change, a class, the profile, a contract, a certificate, points, a grade, a
//  guardian, an account or password. Open: reading, restoring from the archive, comments, and the
//  close-out of what is already there (storno, refund, ending an abonement or a discount, removing
//  a month) — otherwise a pupil who left could never be settled. Restore first to do anything else.
// ===========================================================================

public static class ArchivedStudents
{
    public const string Code = "student_archived";

    public static string Message(IReadOnlyCollection<string> names) =>
        (names.Count == 1 ? names.First() : string.Join(", ", names.Take(5)) + (names.Count > 5 ? $" va yana {names.Count - 5} ta" : ""))
        + " arxivda — maktab o'quvchisi emas, bu amal yopiq. Kerak bo'lsa, avval arxivdan qaytaring.";

    /// <summary>Names of the archived pupils among <paramref name="studentIds"/> (empty — none).</summary>
    public static async Task<List<string>> NamesAsync(
        IAppDbContext db, IReadOnlyCollection<string> studentIds, CancellationToken ct = default)
    {
        if (studentIds.Count == 0) return [];
        var ids = studentIds.ToList();
        return await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.IsArchived)
            .OrderBy(s => s.FullName)
            .Select(s => s.FullName)
            .ToListAsync(ct);
    }

    /// <summary>Throws <see cref="BillingRuleException"/> (409 <see cref="Code"/>) when the pupil is archived.</summary>
    public static async Task EnsureActiveAsync(IAppDbContext db, string? studentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return;
        var names = await NamesAsync(db, [studentId], ct);
        if (names.Count > 0) throw BillingRuleException.Conflict(Code, Message(names));
    }
}
