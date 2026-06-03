using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

/// <summary>
/// Maktab bo'yicha o'quvchilar reytingi (o'rtacha baho + davomat) — admin "Reyting" sahifasi va
/// o'quvchi/parent portali uchun umumiy manba. Hisoblash <see cref="Analytics.BuildClass"/> orqali.
/// </summary>
public static class RatingService
{
    /// <summary>Barcha sinflar bo'yicha har bir o'quvchining qatori (o'rtacha baho + davomat).</summary>
    public static async Task<List<StudentRatingRowDto>> SchoolAsync(IAppDbContext db)
    {
        var students = await db.Students.ToListAsync();
        var subjects = await db.Subjects.ToListAsync();
        var classes = await db.Classes.ToListAsync();
        var lateIds = await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync();

        var result = new List<StudentRatingRowDto>();
        foreach (var cls in classes)
        {
            var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
                .Where(t => t.ClassId == cls.Id).ToListAsync();
            var entries = await db.JournalEntries.Where(e => e.ClassId == cls.Id).ToListAsync();
            var notes = await db.LessonNotes.Where(n => n.ClassId == cls.Id).ToListAsync();
            var rows = Analytics.BuildClass(cls, students, subjects, templates, entries, notes, lateReasonIds: lateIds).Rows;
            result.AddRange(rows.Select(r =>
                new StudentRatingRowDto(r.Student, cls.Name, cls.Grade, r.Average, r.Attendance)));
        }
        return result;
    }
}
