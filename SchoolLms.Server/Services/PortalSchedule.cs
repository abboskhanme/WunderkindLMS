using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;
using SchoolLms.Server.Models;

namespace SchoolLms.Server.Services;

/// <summary>
/// O'qituvchi/o'quvchi ilovalari uchun dars jadvali yordamchilari.
/// Joriy chorak/haftani sanadan aniqlash va sinfning haftaga biriktirilgan jadvalini olish.
/// </summary>
public static class PortalSchedule
{
    /// <summary>Bugungi sanaga qarab joriy chorak va hafta raqamini aniqlaydi.
    /// Sana hech bir chorakka tushmasa (ta'til) — (1, 1) qaytadi.</summary>
    public static async Task<(int Quarter, int Week)> CurrentQuarterWeekAsync(AppDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        foreach (var q in await db.Quarters.OrderBy(x => x.Quarter).ToListAsync())
        {
            if (string.CompareOrdinal(today, q.StartDate) < 0 || string.CompareOrdinal(today, q.EndDate) > 0)
                continue;
            var weeks = ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate);
            var wk = weeks.FirstOrDefault(w =>
                string.CompareOrdinal(today, w.StartISO) >= 0 && string.CompareOrdinal(today, w.EndISO) <= 0);
            return (q.Quarter, wk?.Week ?? 1);
        }
        return (1, 1);
    }

    /// <summary>Sinfning (chorak, hafta) ga biriktirilgan jadval darslari. Biriktirilmagan bo'lsa — bo'sh.</summary>
    public static async Task<List<ScheduleLesson>> LessonsForWeekAsync(
        AppDbContext db, string classId, int quarter, int week)
    {
        var a = await db.WeekAssignments.FirstOrDefaultAsync(
            x => x.ClassId == classId && x.Quarter == quarter && x.Week == week);
        if (a?.TemplateId is null) return new();
        var tpl = await db.ScheduleTemplates.Include(t => t.Lessons)
            .FirstOrDefaultAsync(t => t.Id == a.TemplateId);
        return tpl?.Lessons.ToList() ?? new();
    }

    /// <summary>
    /// O'quvchi guruhi (Student.SubGroup) ga mos darslarni filtrlaydi: SubGroup=0 (butun sinf)
    /// hammaga ko'rinadi; SubGroup=1/2 — faqat shu guruh o'quvchilariga. Guruhsiz o'quvchi
    /// (Student.SubGroup=0) bo'lingan darslarni ko'rmaydi (hech bir guruhda yo'q).
    /// </summary>
    public static IEnumerable<ScheduleLesson> ForStudent(IEnumerable<ScheduleLesson> lessons, int studentSubGroup) =>
        lessons.Where(l => l.SubGroup == 0 || l.SubGroup == studentSubGroup);

    /// <summary>Portal umumiy konteksti: choraklar, dars vaqtlari, davomat sabablari + joriy chorak/hafta.
    /// O'qituvchi va o'quvchi ilovalarining ikkalasi ham shu meta'dan foydalanadi.</summary>
    public static async Task<PortalMetaDto> BuildMetaAsync(AppDbContext db)
    {
        var quarters = await db.Quarters.OrderBy(q => q.Quarter)
            .Select(q => new QuarterPeriodDto(q.Quarter, q.StartDate, q.EndDate, q.GradesOpen)).ToListAsync();
        var lessonTimes = await db.LessonTimes.OrderBy(t => t.Period)
            .Select(t => new LessonTimeDto(t.Period, t.StartTime, t.EndTime)).ToListAsync();
        var reasons = await db.AbsenceReasons
            .Select(r => new AbsenceReasonDto(r.Id, r.Name, r.Short, r.IsLate)).ToListAsync();
        var (curQ, curW) = await CurrentQuarterWeekAsync(db);
        return new PortalMetaDto(quarters, lessonTimes, reasons, curQ, curW);
    }
}
