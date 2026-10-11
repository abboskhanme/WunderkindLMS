using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using System.Globalization;

using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("attendance")]
[Route("api/admin/attendance")]
public class AttendanceController(AppDbContext db) : ControllerBase
{
    // Davomat ro'yxatida pul KO'RSATILMAYDI — `Balance` null qoladi (P1-21).
    private static StudentDto Map(Student s) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate);

    /// <summary>
    /// Bir kunning davomati — EGA bo'yicha: sinf yoki o'quv guruhi
    /// (students-parity.md §2.1.4, G-13). Guruh egasi cut-over o'chirgichi
    /// yoqilgandagina javob beradi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DailyAttendanceDto>> GetDaily(
        [FromQuery] string classId, [FromQuery] string date)
    {
        var owner = await LessonRoster.OwnerAsync(db, classId);
        if (owner is null) return new DailyAttendanceDto(0, []);
        // Yo'nalish guruhi o'chirgichga qaramaydi (LessonRoster.LessonsLiveAsync).
        if (!await LessonRoster.LessonsLiveAsync(db, owner))
            return new DailyAttendanceDto(0, []);

        // Faqat faol o'quvchilar (mijoz, 2026-09-29: arxivdagilar bilan hech qayerda ishlanmaydi).
        var students = await LessonRoster.ForLessonAsync(db, owner);
        var total = students.Count;

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new DailyAttendanceDto(total, []);

        var jsDay = (int)d.DayOfWeek; // 0=Yakshanba ... 6=Shanba
        if (jsDay == 0) return new DailyAttendanceDto(total, []);
        var lessonDay = jsDay - 1; // Dushanba=0 ... Shanba=5

        var q = await db.Quarters.FirstOrDefaultAsync(x =>
            string.Compare(date, x.StartDate) >= 0 && string.Compare(date, x.EndDate) <= 0);
        if (q is null) return new DailyAttendanceDto(total, []);

        var week = ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate)
            .FirstOrDefault(w => string.CompareOrdinal(date, w.StartISO) >= 0 && string.CompareOrdinal(date, w.EndISO) <= 0);
        if (week is null) return new DailyAttendanceDto(total, []);

        var assignment = await db.WeekAssignments.FirstOrDefaultAsync(a =>
            a.ClassId == classId && a.Quarter == q.Quarter && a.Week == week.Week
            && a.OwnerKind == owner.Kind);
        if (assignment?.TemplateId is null) return new DailyAttendanceDto(total, []);

        var tpl = await db.ScheduleTemplates.Include(t => t.Lessons)
            .FirstOrDefaultAsync(t => t.Id == assignment.TemplateId);
        if (tpl is null) return new DailyAttendanceDto(total, []);

        var dayLessons = tpl.Lessons.Where(l => l.Day == lessonDay).OrderBy(l => l.Period).ToList();
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => r.Name);
        // "Kech keldi" turidagi sabablar yo'qlik (absent) sifatida hisoblanmaydi.
        var lateReasonIds = (await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync())
            .ToHashSet();

        var entries = await db.JournalEntries
            .Where(e => e.ClassId == classId && e.Quarter == q.Quarter && e.Date == date
                        && e.ReasonId != null && e.OwnerKind == owner.Kind)
            .ToListAsync();

        // Qaysi dars BELGILANGANI — mas'ul xodim ekranidan ("Davomat
        // belgilash") keladi. Yo'qsiz dars bo'sh ko'rinadi, shuning uchun
        // "belgilandi" ni faqat shu jadval ayta oladi.
        var markedLessons = (await db.DailyAttendanceMarks.AsNoTracking()
                .Where(m => m.ClassId == classId && m.Date == date)
                .Select(m => new { m.SubjectId, m.Period })
                .ToListAsync())
            .Select(m => $"{m.SubjectId}|{m.Period}")
            .ToHashSet();

        var result = new List<SubjectAttendanceDto>();
        foreach (var l in dayLessons)
        {
            // Fan KUNIGA ikki marta bo'lishi mumkin (masalan 1- va 4-soat Kimyo) — yozuv dars
            // raqami bilan ham ajratiladi, aks holda bir darsdagi yo'qlik ikkinchisiga ham tushardi.
            var subjEntries = entries.Where(e => e.SubjectId == l.SubjectId && e.Period == l.Period).ToList();
            var reasonCounts = subjEntries
                .GroupBy(e => e.ReasonId!)
                .Select(g => new ReasonCountDto(reasons.GetValueOrDefault(g.Key, "?"), g.Count()))
                .ToList();
            var absent = subjEntries.Count(e => !lateReasonIds.Contains(e.ReasonId!));
            result.Add(new SubjectAttendanceDto(
                l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""), l.Period,
                total, total - absent, absent, reasonCounts,
                markedLessons.Contains($"{l.SubjectId}|{l.Period}")));
        }

        // People, not lessons (2026-10-07): each student's day from the day's held lessons.
        var held = await HeldLessons.ListAsync(db, [classId], date, date);
        var rows = (await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == classId && e.Date == date && e.OwnerKind == owner.Kind)
                .Select(e => new { e.SubjectId, e.Period, e.StudentId, e.ReasonId })
                .ToListAsync())
            .GroupBy(e => (e.SubjectId, e.Period, e.StudentId))
            .ToDictionary(g => g.Key, g => g.First().ReasonId);
        var days = StudentDays.Build(held, students.Select(s => (s.Id, s.SubGroup)), rows, lateReasonIds);
        var people = StudentDayCounts.Of(days.Values);

        return new DailyAttendanceDto(total, result, people.Absent, people.Partial, people.Late);
    }

    /// <summary>
    /// Davomat analitikasi — zavuch ekrani: davr bo'yicha jami, sinflar kesimi, tanlangan
    /// kunning dars soatlari kesimi, kunlik trend va sabablar taqsimoti.
    ///
    /// <para>Foizlar SERVERDA hisoblanadi (brauzerda emas) va "tekshirilmagan" hech qachon
    /// "keldi"ga qo'shilmaydi — ta'rif <see cref="AttendanceAnalytics"/> da.</para>
    /// </summary>
    /// <param name="classId">Bitta sinf; berilmasa — butun maktab.</param>
    /// <param name="from">Davr boshi "yyyy-MM-dd"; berilmasa — bugundan ikki hafta orqaga.</param>
    /// <param name="to">Davr oxiri "yyyy-MM-dd"; berilmasa — bugun.</param>
    /// <param name="day">Dars soatlari kesimi uchun kun; berilmasa — davrning oxirgi kuni.</param>
    [HttpGet("analytics")]
    public async Task<ActionResult<AttendanceAnalyticsDto>> Analytics(
        [FromQuery] string? classId, [FromQuery] string? from,
        [FromQuery] string? to, [FromQuery] string? day)
        => await AttendanceAnalytics.BuildAsync(db, classId, from, to, day);

    /// <summary>
    /// Davomat analitikasi Excel'da — dars qoldirganlar, har bir kun sababi bilan, sabablar va dars
    /// soatlari (mijoz, 2026-10-11). Filtrlar ekrandagining o'zi.
    /// </summary>
    [HttpGet("analytics/export")]
    public async Task<IActionResult> AnalyticsExport(
        [FromQuery] string? classId, [FromQuery] string? from, [FromQuery] string? to)
    {
        var d = await AttendanceAnalytics.BuildAsync(db, classId, from, to, null, AttendanceAnalyticsExport.MaxEvents);
        return File(AttendanceAnalyticsExport.Build(d),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"davomat_{d.From}_{d.To}.xlsx");
    }

    /// <summary>Bitta fan/kun bo'yicha har bir o'quvchining holati (ega — sinf yoki guruh).</summary>
    /// <param name="period">Dars raqami — fan kunda ikki marta bo'lsa aynan shu soat
    /// (berilmasa — eski xulq: shu fanning kun bo'yi yozuvlari).</param>
    /// <remarks>"Kech keldi" (<c>IsLate</c>) turidagi sabab — KELDI: <c>Absent = false</c>, sabab
    /// nomi esa baribir qaytadi (ekran "Keldi · Kech qoldi" deb ko'rsatadi) — jadvaldagi
    /// "keldi" soni bilan bir xil.</remarks>
    [HttpGet("subject")]
    public async Task<ActionResult<IEnumerable<StudentStatusDto>>> GetSubjectDetail(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] string date,
        [FromQuery] int? period = null)
    {
        var owner = await LessonRoster.OwnerAsync(db, classId);
        if (owner is null) return new List<StudentStatusDto>();
        if (!await LessonRoster.LessonsLiveAsync(db, owner))
            return new List<StudentStatusDto>();

        var students = await LessonRoster.ForLessonAsync(db, owner);

        var q = await db.Quarters.FirstOrDefaultAsync(x =>
            string.Compare(date, x.StartDate) >= 0 && string.Compare(date, x.EndDate) <= 0);

        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => r.Name);

        var entries = q is null
            ? new List<JournalEntry>()
            : await db.JournalEntries.Where(e =>
                e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == q.Quarter &&
                e.Date == date && e.ReasonId != null && e.OwnerKind == owner.Kind
                && (period == null || e.Period == period)).ToListAsync();
        var lateIds = (await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync())
            .ToHashSet();

        return students.Select(s =>
        {
            var e = entries.FirstOrDefault(x => x.StudentId == s.Id);
            return new StudentStatusDto(Map(s), e is not null && !lateIds.Contains(e.ReasonId!),
                e?.ReasonId is null ? null : reasons.GetValueOrDefault(e.ReasonId));
        }).ToList();
    }

    // Kunduzgi davomatni BELGILASH endpointlari — DailyAttendanceController (2026-10-11: alohida
    // `attendanceMark` ruxsati, "kunduzgi davomat faqat hamshirada").
}
