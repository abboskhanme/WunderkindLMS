using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Bitta o'quvchining o'zlashtirish va qatnashish hisoboti: har fan bo'yicha chorak baholari +
/// chorak bo'yicha qoldirilgan kunlar/darslar va kech qolishlar. Admin hisobotlari ham,
/// o'quvchi (oila) ilovasi ham shu yagona mantiqdan foydalanadi.
///
/// <para>
/// <b>G-15 — guruh darslari.</b> Qamrov endi o'quvchining SINFI + FAOL GURUHLARI
/// (<see cref="ClassAttainment"/>): guruhda qo'yilgan baho ham, guruh darsidagi davomatsizlik
/// ham shu hisobotga kiradi va fan o'rtachasi ikkalasining birgalikdagi o'rtachasi bo'ladi.
/// O'chirgich o'chiq bo'lsa qamrov faqat sinfdan iborat, ya'ni so'rov bugungisining aynan o'zi.
/// </para>
/// </summary>
public static class StudentReportBuilder
{
    public static async Task<StudentReportDto> BuildAsync(IAppDbContext db, Student st)
    {
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == st.ClassName);
        var attainment = await ClassAttainment.ForStudentAsync(db, st);
        var groups = attainment.GroupsOf(st.Id);
        // Bo'sh ro'yxat = "ega topilmadi" — bugungi kod bunday holatda o'quvchining
        // BARCHA yozuvlarini oladi (`cls == null` shoxi), shuni saqlaymiz.
        var ownerIds = ClassAttainment.OwnerIdsFor(cls?.Id, groups);
        var unscoped = ownerIds.Count == 0;

        var allSubjects = await db.Subjects.ToListAsync();
        var templates = unscoped
            ? new List<ScheduleTemplate>()
            : await db.ScheduleTemplates.Include(t => t.Lessons)
                .Where(t => ownerIds.Contains(t.ClassId)).ToListAsync();
        var entries = (await db.JournalEntries
                .Where(e => e.StudentId == st.Id && (unscoped || ownerIds.Contains(e.ClassId)))
                .ToListAsync())
            .Where(e => attainment.CountsFor(st.Id, e.ClassId, e.OwnerKind))
            .ToList();
        var quarterGrades = (await db.QuarterGrades
                .Where(g => g.StudentId == st.Id && (unscoped || ownerIds.Contains(g.ClassId)))
                .ToListAsync())
            .Where(g => attainment.CountsFor(st.Id, g.ClassId, g.OwnerKind))
            .ToList();
        var reasonRows = await db.AbsenceReasons.ToListAsync();
        var lateIds = reasonRows.Where(r => r.IsLate).Select(r => r.Id).ToHashSet();
        var reasons = reasonRows.ToDictionary(r => r.Id, r => r.Name.ToLowerInvariant());

        var fromSchedule = templates.SelectMany(t => t.Lessons).Select(l => l.SubjectId).Distinct().ToList();
        var subjectIds = fromSchedule.Count > 0 ? fromSchedule : allSubjects.Select(s => s.Id).ToList();
        // Guruh jadvali hali tuzilmagan bo'lsa ham guruh fani ro'yxatda bo'lishi kerak.
        subjectIds = [.. subjectIds.Union(attainment.GroupSubjectsOf([st.Id]), StringComparer.Ordinal)];
        var subjects = subjectIds
            .Select(id => allSubjects.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .Select(s => new SubjectDto(s!.Id, s.Name))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var grades = new Dictionary<string, Dictionary<int, double>>();
        foreach (var subj in subjects)
        {
            var byQ = entries.Where(e => e.SubjectId == subj.Id && e.Grade != null)
                .GroupBy(e => e.Quarter)
                .ToDictionary(g => g.Key, g => Math.Round(g.Average(e => (double)e.Grade!.Value), 2));
            // Rasmiy chorak bahosi kunlik o'rtacha o'rnini bosadi (kiritilgan chorak uchun).
            foreach (var qg in quarterGrades.Where(g => g.SubjectId == subj.Id))
                byQ[qg.Quarter] = qg.Grade;
            if (byQ.Count > 0) grades[subj.Id] = byQ;
        }

        var absences = entries.Where(e => e.ReasonId != null).ToList();
        bool IsLate(JournalEntry e) => lateIds.Contains(e.ReasonId!);
        bool IsIll(JournalEntry e) => reasons.TryGetValue(e.ReasonId!, out var n) && n.Contains("kasal");
        Dictionary<int, int> PerQ(Func<JournalEntry, bool> pred) =>
            absences.Where(pred).GroupBy(e => e.Quarter).ToDictionary(g => g.Key, g => g.Count());
        Dictionary<int, int> PerQDays(Func<JournalEntry, bool> pred) =>
            absences.Where(pred).GroupBy(e => e.Quarter)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Date).Distinct().Count());

        var attendance = new StudentAttendanceDto(
            PerQDays(e => !IsLate(e)), PerQDays(IsIll),
            PerQ(e => !IsLate(e)), PerQ(IsIll), PerQ(IsLate));

        var homeroom = cls is null
            ? ""
            : (await db.Teachers.FirstOrDefaultAsync(t => t.HomeroomClass == cls.Name))?.FullName ?? "";

        return new StudentReportDto(
            st.Id, st.FullName, st.ClassName, homeroom, st.ParentFullName, subjects, grades, attendance);
    }
}
