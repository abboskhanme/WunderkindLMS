using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'qituvchi/o'quvchi ilovalari uchun dars jadvali yordamchilari.
/// Joriy chorak/haftani sanadan aniqlash va sinfning haftaga biriktirilgan jadvalini olish.
/// </summary>
public static class PortalSchedule
{
    /// <summary>Bugungi sanaga qarab joriy chorak va hafta raqamini aniqlaydi.
    /// Sana hech bir chorakka tushmasa (ta'til) — (1, 1) qaytadi.</summary>
    public static async Task<(int Quarter, int Week)> CurrentQuarterWeekAsync(IAppDbContext db)
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        foreach (var q in await db.Quarters.OrderBy(x => x.Quarter).ToListAsync())
        {
            if (string.CompareOrdinal(today, q.StartDate) < 0 || string.CompareOrdinal(today, q.EndDate) > 0)
                continue;
            return (q.Quarter, WeekOf(ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate), today));
        }
        return (1, 1);
    }

    /// <summary>
    /// Sana chorakning qaysi haftasiga tushadi. Haftalar Du–Sha
    /// (<see cref="ScheduleMath.GetQuarterWeeks"/>), ya'ni YAKSHANBA hech qaysi
    /// oraliqqa kirmaydi — u o'zidan oldingi dushanbadan boshlangan haftaga
    /// tegishli (kalendar haftasi Du–Ya). Ilgari bu holatda 1-hafta qaytardi:
    /// har yakshanba o'qituvchi va ota-ona jadvali chorakning birinchi
    /// haftasiga sakrardi.
    /// </summary>
    public static int WeekOf(IReadOnlyList<ScheduleMath.WeekRange> weeks, string todayISO) =>
        weeks.LastOrDefault(w => string.CompareOrdinal(w.StartISO, todayISO) <= 0)?.Week
        ?? weeks.FirstOrDefault()?.Week
        ?? 1;

    /// <summary>
    /// EGAning (chorak, hafta) ga biriktirilgan jadval darslari. Biriktirilmagan
    /// bo'lsa — bo'sh.
    ///
    /// <para>
    /// <paramref name="ownerKind"/> sukut bo'yicha <c>class</c>, ya'ni bugungi
    /// har bir chaqiruv o'zgarishsiz ishlaydi. Guruh jadvalini olish uchun
    /// guruh id'si va <c>group</c> beriladi (students-parity.md §2.1.4:
    /// guruh id'si ham o'sha <c>class_id</c> ustunida turadi).
    /// </para>
    /// </summary>
    public static async Task<List<ScheduleLesson>> LessonsForWeekAsync(
        IAppDbContext db, string classId, int quarter, int week,
        string ownerKind = LessonOwnerKind.Class)
    {
        var a = await db.WeekAssignments.FirstOrDefaultAsync(
            x => x.ClassId == classId && x.Quarter == quarter && x.Week == week
                 && x.OwnerKind == ownerKind);
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

    /// <summary>
    /// O'QITUVCHINING (chorak, hafta) dagi barcha darslari — sinf va fan nomlari,
    /// dars vaqtlari bilan, kun va dars raqami bo'yicha tartiblangan.
    ///
    /// <para>
    /// O'qituvchi ilovasi (<c>GET /api/teacher/schedule</c>) va Telegram Mini App
    /// (<c>GET /api/tg/teacher/today</c>) ikkalasi ham shu yerdan oladi.
    /// Sikl ichida so'rov yo'q: shablonlar, sinf/fan nomlari va dars vaqtlari
    /// oldindan bittadan so'rov bilan yuklanadi.
    /// </para>
    /// </summary>
    public static async Task<List<TeacherLessonDto>> TeacherWeekAsync(
        IAppDbContext db, string teacherId, int quarter, int week)
    {
        // G-5/G-12: yig'ish endi `TeacherLessons` da — sinf VA guruh shablonlari
        // bitta joyda birlashadi. O'chirgich o'chiq bo'lsa guruh biriktirishlari
        // ko'rinmaydi va natija bugungi ro'yxatning aynan o'zi.
        var lessons = await TeacherLessons.ForWeekAsync(db, teacherId, quarter, week);

        // O'rindosh ustoz (2026-10-07): this week's substitutions on either side of the teacher.
        var monday = await LessonSubstitutionService.WeekMondayAsync(db, quarter, week);
        var subs = monday is { } m
            ? await db.LessonSubstitutions.AsNoTracking()
                .Where(s => s.CancelledAt == null && s.Date >= m && s.Date <= m.AddDays(5)
                            && (s.AbsentTeacherId == teacherId || s.SubstituteTeacherId == teacherId))
                .ToListAsync()
            : [];
        if (lessons.Count == 0 && subs.Count == 0) return [];

        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var times = await db.LessonTimes.ToDictionaryAsync(x => x.Period);
        var otherIds = subs.SelectMany(s => new[] { s.AbsentTeacherId, s.SubstituteTeacherId }).Distinct().ToList();
        var teacherNames = await db.Teachers.AsNoTracking().Where(t => otherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName);
        int DayOf(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

        var result = new List<TeacherLessonDto>();
        foreach (var l in lessons)
        {
            times.TryGetValue(l.Period, out var lt);
            var coveredBy = subs.FirstOrDefault(s => s.AbsentTeacherId == teacherId && DayOf(s.Date) == l.Day
                && s.Period == l.Period && s.OwnerId == l.Owner.Id && s.SubjectId == l.SubjectId && s.SubGroup == l.SubGroup);
            result.Add(new TeacherLessonDto(
                l.Day, l.Period, lt?.StartTime, lt?.EndTime,
                l.Owner.Id, l.Owner.Name,
                l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""), l.SubGroup,
                l.Owner.Kind,
                SubstitutedBy: coveredBy is null ? null : teacherNames.GetValueOrDefault(coveredBy.SubstituteTeacherId, "")));
        }
        if (subs.Any(s => s.SubstituteTeacherId == teacherId))
        {
            var owners = await LessonRoster.AllOwnersAsync(db);
            foreach (var s in subs.Where(s => s.SubstituteTeacherId == teacherId))
            {
                times.TryGetValue(s.Period, out var lt);
                result.Add(new TeacherLessonDto(
                    DayOf(s.Date), s.Period, lt?.StartTime, lt?.EndTime,
                    s.OwnerId, owners.TryGetValue(s.OwnerId, out var o) ? o.Name : "",
                    s.SubjectId, subjects.GetValueOrDefault(s.SubjectId, ""), s.SubGroup,
                    s.OwnerKind, SubstituteFor: teacherNames.GetValueOrDefault(s.AbsentTeacherId, "")));
            }
        }
        return [.. result.OrderBy(r => r.Day).ThenBy(r => r.Period)];
    }

    /// <summary>
    /// Dushanbadan nolga hisoblanadigan BUGUNGI kun raqami (0 = Du … 5 = Sha, 6 = Yak).
    /// <c>ScheduleLesson.Day</c> bilan bir xil sanoq.
    /// </summary>
    public static int TodayIndex() => ((int)AppClock.Now.DayOfWeek + 6) % 7;

    /// <summary>Portal umumiy konteksti: choraklar, dars vaqtlari, davomat sabablari + joriy chorak/hafta.
    /// O'qituvchi va o'quvchi ilovalarining ikkalasi ham shu meta'dan foydalanadi.</summary>
    public static async Task<PortalMetaDto> BuildMetaAsync(IAppDbContext db)
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
