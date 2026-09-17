using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

/// <summary>
/// Maktab bo'yicha o'quvchilar reytingi (o'rtacha baho + davomat) — admin "Reyting" sahifasi va
/// o'quvchi/parent portali uchun umumiy manba. Hisoblash <see cref="Analytics.BuildClass"/> orqali.
///
/// <para>
/// <b>G-15.</b> Guruh darsidagi baho ham o'quvchining SINF RAHBARLIGIDAGI sinfi ostida
/// sanaladi — guruh reytingda alohida qator bo'lmaydi (<see cref="ClassAttainment"/>).
/// O'chirgich o'chiq bo'lsa qamrov bo'sh va so'rovlar bugungisining aynan o'zi.
/// </para>
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
        var attainment = await ClassAttainment.BuildAsync(db);

        var result = new List<StudentRatingRowDto>();
        foreach (var cls in classes)
        {
            // Sinf id'si + shu sinf o'quvchilarining faol guruhlari. O'chirgich
            // o'chiq bo'lsa ro'yxatda faqat sinf id'si bo'ladi.
            var ownerIds = attainment.OwnerIdsForClass(
                cls.Id, ClassAttainment.StudentIdsOf(cls, students));
            var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
                .Where(t => ownerIds.Contains(t.ClassId)).ToListAsync();
            var entries = await db.JournalEntries.Where(e => ownerIds.Contains(e.ClassId)).ToListAsync();
            var notes = await db.LessonNotes.Where(n => ownerIds.Contains(n.ClassId)).ToListAsync();
            var rows = Analytics.BuildClass(
                cls, students, subjects, templates, entries, notes,
                lateReasonIds: lateIds, attainment: attainment).Rows;
            result.AddRange(rows.Select(r =>
                new StudentRatingRowDto(r.Student, cls.Name, cls.Grade, r.Average, r.Attendance)));
        }
        return result;
    }
}
