using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Bitta o'quvchining o'zlashtirish va qatnashish hisoboti: har fan bo'yicha chorak baholari +
/// chorak bo'yicha qoldirilgan kunlar/darslar va kech qolishlar. Admin hisobotlari ham,
/// o'quvchi (oila) ilovasi ham shu yagona mantiqdan foydalanadi.
/// </summary>
public static class StudentReportBuilder
{
    public static async Task<StudentReportDto> BuildAsync(IAppDbContext db, Student st)
    {
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == st.ClassName);
        var allSubjects = await db.Subjects.ToListAsync();
        var templates = cls is null
            ? new List<ScheduleTemplate>()
            : await db.ScheduleTemplates.Include(t => t.Lessons).Where(t => t.ClassId == cls.Id).ToListAsync();
        var entries = await db.JournalEntries
            .Where(e => e.StudentId == st.Id && (cls == null || e.ClassId == cls.Id)).ToListAsync();
        var quarterGrades = await db.QuarterGrades
            .Where(g => g.StudentId == st.Id && (cls == null || g.ClassId == cls.Id)).ToListAsync();
        var reasonRows = await db.AbsenceReasons.ToListAsync();
        var lateIds = reasonRows.Where(r => r.IsLate).Select(r => r.Id).ToHashSet();
        var reasons = reasonRows.ToDictionary(r => r.Id, r => r.Name.ToLowerInvariant());

        var fromSchedule = templates.SelectMany(t => t.Lessons).Select(l => l.SubjectId).Distinct().ToList();
        var subjectIds = fromSchedule.Count > 0 ? fromSchedule : allSubjects.Select(s => s.Id).ToList();
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
