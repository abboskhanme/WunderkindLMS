using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  DAVOMAT STATISTIKASI UCHUN "DARS BO'LDIMI" — 2026-09-26
// ===========================================================================
//
//  MUAMMO (prod, faqat o'qib tekshirilgan)
//  --------------------------------------
//  Mas'ul xodim "Davomat belgilash" ekranida davomat qiladi: yo'qlik
//  `journal_entries.reason_id` ga, "shu dars belgilandi" belgisi esa
//  `daily_attendance_marks` ga yoziladi. `DailyAttendanceService` darsni
//  ATAYLAB "o'tildi" (`lesson_notes.conducted`) qilmaydi — buni o'qituvchi
//  aytadi. Davomat statistikasi esa maxrajni FAQAT `conducted` dan olardi,
//  ya'ni o'qituvchi ptichka qo'ymagan maktabda davomat foizi umuman chiqmasdi.
//
//  QOIDA
//  -----
//  DAVOMAT STATISTIKASI uchun dars BO'LGAN deb hisoblanadi, agar:
//    · `lesson_notes.conducted = true` (o'qituvchi "dars o'tildi" dedi), YOKI
//    · o'sha (sana, ega id'si `class_id` da, fan, dars raqami) uchun
//      `daily_attendance_marks` qatori bor (davomat belgilangan) —
//      yo'nalish guruhi darslari ham shu jumladan.
//
//  O'QITUVCHI ISHI VA MAOSH BU QOIDAGA KIRMAYDI: `TeacherActivityReport`,
//  maosh va "o'tilgan dars" hisoblari faqat `lesson_notes.conducted` ni
//  o'qiydi. Bu yordamchi `lesson_notes` ga HECH NARSA yozmaydi.
//
//  BELGIDA YO'Q IKKI NARSA
//  -----------------------
//  `daily_attendance_marks` da `owner_kind` va bo'linish (`sub_group`) yo'q:
//    · ega turi id'dan aniqlanadi — `study_groups` da bo'lsa guruh, aks holda sinf
//      (guruh id'si uuid, sinf id'si bilan to'qnashmaydi);
//    · bo'linish o'sha kunning DARS JADVALIDAN olinadi (chorak → hafta →
//      biriktirilgan shablon → shu kun, shu soat, shu fan) —
//      `DailyAttendanceService.DayLessonsAsync` bilan bir xil yo'l. Topilmasa — 0.
// ===========================================================================

/// <summary>Davomat statistikasi uchun "bo'lgan" bitta dars katagi.</summary>
/// <param name="Marked">
/// Davomat "Davomat belgilash" ekranida belgilangan (<c>daily_attendance_marks</c>). Bunday darsda
/// jurnalda yozuvi YO'Q o'quvchi — KELDI (ekran qoidasi: "ro'yxatda yo'q o'quvchi — keldi"),
/// "tekshirilmagan" emas.
/// </param>
public sealed record HeldLesson(
    string ClassId, string OwnerKind, string SubjectId, string Date, int Period, int SubGroup, int Quarter,
    bool Marked = false);

/// <summary>
/// "Bu dars davomat statistikasida bo'lgan deb sanaladimi" — yagona javob beruvchi.
/// Qoida fayl boshidagi izohda.
/// </summary>
public static class HeldLessons
{
    /// <summary>
    /// Bo'lgan darslar: <c>conducted</c> dars izohlari + davomat belgilangan darslar
    /// (izohsiz). <paramref name="ownerIds"/> null — hamma ega; sanalar "yyyy-MM-dd",
    /// null — chegarasiz (ikkala uchi ham kiradi).
    /// </summary>
    public static async Task<List<HeldLesson>> ListAsync(
        IAppDbContext db, IReadOnlyCollection<string>? ownerIds = null,
        string? from = null, string? to = null, CancellationToken ct = default)
    {
        var ids = ownerIds?.ToList();
        var notesQ = db.LessonNotes.AsNoTracking().Where(n => n.Conducted);
        if (ids is not null) notesQ = notesQ.Where(n => ids.Contains(n.ClassId));
        if (from is not null) notesQ = notesQ.Where(n => string.Compare(n.Date, from) >= 0);
        if (to is not null) notesQ = notesQ.Where(n => string.Compare(n.Date, to) <= 0);
        var notes = await notesQ
            .Select(n => new HeldLesson(n.ClassId, n.OwnerKind, n.SubjectId, n.Date, n.Period, n.SubGroup, n.Quarter))
            .ToListAsync(ct);

        var marked = await MarkedAsync(db, ids, from, to, ct);
        if (marked.Count == 0) return notes;

        var markedKeys = marked.Select(m => (m.ClassId, m.SubjectId, m.Date, m.Period)).ToHashSet();
        var noted = notes.Select(n => (n.ClassId, n.SubjectId, n.Date, n.Period)).ToHashSet();
        var result = notes
            .Select(n => markedKeys.Contains((n.ClassId, n.SubjectId, n.Date, n.Period)) ? n with { Marked = true } : n)
            .ToList();
        result.AddRange(marked.Where(m => !noted.Contains((m.ClassId, m.SubjectId, m.Date, m.Period))));
        return result;
    }

    /// <summary>
    /// Mavjud dars izohlari ro'yxatiga davomat belgilangan, lekin izohi (yoki
    /// <c>conducted</c> izohi) yo'q darslar uchun XOTIRADAGI <c>Conducted = true</c>
    /// izohlarni qo'shadi — <see cref="Analytics.BuildClass"/> kabi izoh ro'yxatini
    /// qabul qiladigan hisoblagichlar uchun. Bazaga hech narsa yozilmaydi.
    /// </summary>
    public static async Task<List<LessonNote>> WithMarkedAsync(
        IAppDbContext db, IReadOnlyList<LessonNote> notes, IReadOnlyCollection<string>? ownerIds,
        CancellationToken ct = default)
    {
        var marked = await MarkedAsync(db, ownerIds?.ToList(), null, null, ct);
        if (marked.Count == 0) return [.. notes];

        var conducted = notes.Where(n => n.Conducted)
            .Select(n => (n.ClassId, n.SubjectId, n.Date, n.Period)).ToHashSet();
        var result = new List<LessonNote>(notes);
        foreach (var m in marked)
        {
            if (conducted.Contains((m.ClassId, m.SubjectId, m.Date, m.Period))) continue;
            result.Add(new LessonNote
            {
                ClassId = m.ClassId, OwnerKind = m.OwnerKind, SubjectId = m.SubjectId,
                Date = m.Date, Period = m.Period, SubGroup = m.SubGroup, Quarter = m.Quarter,
                Conducted = true,
            });
        }
        return result;
    }

    /// <summary>
    /// Davomat belgilangan darslar — ega turi va bo'linishi aniqlangan holda.
    /// Bo'lingan (1/2) darsda ikkala yarim bir soatda bir fan o'tsa — ikkala katak ham.
    /// </summary>
    public static async Task<List<HeldLesson>> MarkedAsync(
        IAppDbContext db, List<string>? ownerIds, string? from, string? to, CancellationToken ct = default)
    {
        var q = db.DailyAttendanceMarks.AsNoTracking();
        if (ownerIds is not null) q = q.Where(m => ownerIds.Contains(m.ClassId));
        if (from is not null) q = q.Where(m => string.Compare(m.Date, from) >= 0);
        if (to is not null) q = q.Where(m => string.Compare(m.Date, to) <= 0);
        var marks = await q.Select(m => new { m.ClassId, m.SubjectId, m.Date, m.Period }).ToListAsync(ct);
        if (marks.Count == 0) return [];

        var markOwners = marks.Select(m => m.ClassId).Distinct().ToList();
        var groupGuids = markOwners.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToList();
        var groupIds = groupGuids.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await db.StudyGroups.AsNoTracking().Where(g => groupGuids.Contains(g.Id))
                .Select(g => g.Id).ToListAsync(ct)).Select(g => g.ToString()).ToHashSet(StringComparer.Ordinal);

        var quarters = await db.Quarters.AsNoTracking().ToListAsync(ct);
        var assignments = await db.WeekAssignments.AsNoTracking()
            .Where(a => markOwners.Contains(a.ClassId) && a.TemplateId != null)
            .Select(a => new { a.ClassId, a.OwnerKind, a.Quarter, a.Week, a.TemplateId })
            .ToListAsync(ct);
        var templateIds = assignments.Select(a => a.TemplateId!).Distinct().ToList();
        var lessonsByTemplate = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                .Where(t => templateIds.Contains(t.Id)).ToListAsync(ct))
            .ToDictionary(t => t.Id, t => t.Lessons, StringComparer.Ordinal);

        var result = new List<HeldLesson>();
        foreach (var m in marks)
        {
            var kind = groupIds.Contains(m.ClassId) ? LessonOwnerKind.Group : LessonOwnerKind.Class;
            var quarter = quarters.FirstOrDefault(x =>
                string.CompareOrdinal(m.Date, x.StartDate) >= 0 && string.CompareOrdinal(m.Date, x.EndDate) <= 0);

            var subGroups = new List<int>();
            if (quarter is not null && kind == LessonOwnerKind.Class
                && DateOnly.TryParse(m.Date, out var day) && day.DayOfWeek != DayOfWeek.Sunday)
            {
                var week = ScheduleMath.GetQuarterWeeks(quarter.StartDate, quarter.EndDate)
                    .FirstOrDefault(w => string.CompareOrdinal(m.Date, w.StartISO) >= 0
                                         && string.CompareOrdinal(m.Date, w.EndISO) <= 0);
                var a = week is null
                    ? null
                    : assignments.FirstOrDefault(x => x.ClassId == m.ClassId && x.OwnerKind == kind
                                                      && x.Quarter == quarter.Quarter && x.Week == week.Week);
                if (a is not null && lessonsByTemplate.TryGetValue(a.TemplateId!, out var lessons))
                {
                    var lessonDay = (int)day.DayOfWeek - 1; // dushanba = 0
                    subGroups = [.. lessons
                        .Where(l => l.Day == lessonDay && l.Period == m.Period && l.SubjectId == m.SubjectId)
                        .Select(l => l.SubGroup).Distinct().Order()];
                }
            }
            if (subGroups.Count == 0 || subGroups.Contains(0)) subGroups = [0];

            foreach (var sg in subGroups)
                result.Add(new HeldLesson(m.ClassId, kind, m.SubjectId, m.Date, m.Period, sg, quarter?.Quarter ?? 0, Marked: true));
        }
        return result;
    }
}
