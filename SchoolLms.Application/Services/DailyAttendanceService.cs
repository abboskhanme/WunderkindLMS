using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  DAVOMAT — sinf tanlanadi, DARS SOATI tanlanadi, uchta belgi qo'yiladi
//  ---------------------------------------------------------------------------
//  Mijoz, 2026-09-18: "bir kishi doimiy sinflar davomatini qiloladigan
//  bo'lsin ... barcha sinflarni eng qulay usulda davomatini qilolsin", va
//  o'sha kuni ikkinchi xat: "sinf tanlansa o'sha soatda darsiga ko'ra sinfni
//  davomat qilish mumkin bo'lsin ... shunchaki keldi-yashil, kelmadi-qizil,
//  sababli-sariq".
//
//  ISH OQIMI: kun → sinf → DARS SOATI → har o'quvchida uchta doira:
//  KELDI (yashil) · KELMADI (qizil) · SABABLI (sariq). Ustunni to'liq
//  belgilaydigan tugma ham bor (ekranda).
//
//  ---------------------------------------------------------------------------
//  UCHTA QAROR, VA NEGA
//  ---------------------------------------------------------------------------
//  1) YO'QLIK JURNALGA YOZILADI, YANGI JOYGA EMAS.
//     Har bir belgi `journal_entries.reason_id` ga tushadi — o'qituvchi qo'li
//     bilan yozganidek, AYNAN o'sha dars (fan + soat) qatoriga. Davomat foizi,
//     intizomiy ball, ota-onaning jurnali va barcha hisobotlar SHU qatordan
//     o'qiydi; ikkinchi manba ochilsa ikkitasi bir kun kelib bir-biriga zid
//     javob berardi.
//
//  2) UCHTA BELGI — KATALOGDAGI SABABGA BOG'LANADI.
//     Ekranda uchta tugma, bazada esa hamon mavjud "Davomat sabablari"
//     katalogi (`absence_reasons`) ishlatiladi: QIZIL → "Sababsiz" turi,
//     SARIQ → sababli turi (kasal/ruxsat/oilaviy). Katalogni tashlab,
//     "absent/excused" degan yangi ikkilik ustun qo'shish — jurnal, hisobot
//     va intizomiy ball o'qiydigan ma'noni ikkiga bo'lish bo'lardi.
//     Qaysi tur tanlanishi — <see cref="ResolveReasons"/> da, nomga qarab.
//
//  3) BAHOGA TEGILMAYDI.
//     Yozuv FAQAT `reason_id` ni o'zgartiradi. `JournalService.SetEntryAsync`
//     bahoni, uyga vazifani va xulqni ham ustiga yozadi — shuning uchun u
//     ISHLATILMAYDI. Darsni "o'tildi" (`lesson_notes.conducted`) deb ham
//     belgilamaymiz: buni O'QITUVCHI aytadi.
//
//  PUSH YUBORILMAYDI: mahsulot qoidasi — barcha xabar Telegram orqali
//  (CLAUDE.md, 2026-09-16). Eski FCM yo'li bu yerda chaqirilmaydi.
// ===========================================================================

/// <summary>Ro'yxatdagi bitta sinf — mas'ul xodim ko'radigan holat.</summary>
/// <param name="LessonCount">Shu kunda jadval bo'yicha nechta dars bor.</param>
/// <param name="MarkedLessons">Shulardan nechtasi belgilab bo'lingan.</param>
public record DailyAttendanceClassDto(
    string ClassId, string ClassName, int StudentCount,
    int LessonCount, int MarkedLessons, int AbsentCount, int LateCount);

/// <summary>Kunning ro'yxati: sinflar va umumiy yakun.</summary>
/// <param name="MarkedLessons">Shu kunda belgilangan DARS SOATLARI soni.</param>
/// <param name="TotalLessons">
/// Shu kunda jadval bo'yicha o'tiladigan jami dars soati. Sinf hisobi yetarli
/// emas: bitta sinfda 5 ta soat bo'ladi va "0/10 sinf" ish qay darajada
/// bitganini ko'rsatmaydi (mijoz, 2026-09-19).
/// </param>
public record DailyAttendanceOverviewDto(
    string Date, IReadOnlyList<DailyAttendanceClassDto> Classes,
    int MarkedClasses, int TotalClasses, int AbsentTotal, int LateTotal,
    int MarkedLessons, int TotalLessons);

/// <summary>Bitta dars soati va undagi belgilar.</summary>
/// <param name="SubGroup">
/// Bo'linish: 0 — butun sinf, 1/2 — faqat shu guruh (<c>ScheduleLesson.SubGroup</c>).
/// Til darsi ikkiga bo'linganda yarim sinf boshqa xonada bo'ladi — ularni
/// "kelmadi" deb belgilab qo'ymaslik uchun ro'yxat ham bo'linadi.
/// </param>
/// <param name="StudentIds">
/// AYNAN SHU darsda bo'ladigan o'quvchilar (bo'linish filtri qo'llangan).
/// Ekran umumiy ro'yxatdan shu id'larni ko'rsatadi.
/// </param>
/// <param name="Marks">
/// O'quvchi id'si → davomat sababi. Ro'yxatda YO'Q o'quvchi — keldi.
/// </param>
public record DailyAttendanceLessonDto(
    string SubjectId, string SubjectName, int Period, int SubGroup,
    string? StartTime, string? EndTime,
    bool Marked, string? MarkedByName, DateTimeOffset? MarkedAt,
    int AbsentCount, int LateCount,
    IReadOnlyList<string> StudentIds,
    IReadOnlyDictionary<string, string> Marks);

/// <summary>Ro'yxatdagi bitta o'quvchi.</summary>
public record DailyAttendanceStudentDto(string StudentId, string FullName);

/// <summary>
/// Sinfning bitta kuni: o'quvchilar ro'yxati va kunning har bir darsi.
/// Ekran bitta so'rov bilan hammasini oladi va soatdan soatga darrov o'tadi.
/// </summary>
/// <param name="AbsentReasonId">Ekrandagi QIZIL tugma yozadigan sabab.</param>
/// <param name="ExcusedReasonId">Ekrandagi SARIQ tugma yozadigan sabab.</param>
public record DailyAttendanceClassDayDto(
    string ClassId, string ClassName, string Date,
    IReadOnlyList<DailyAttendanceStudentDto> Students,
    IReadOnlyList<DailyAttendanceLessonDto> Lessons,
    string? AbsentReasonId, string? AbsentReasonName,
    string? ExcusedReasonId, string? ExcusedReasonName);

/// <summary>Bitta o'quvchining belgisi (saqlashda).</summary>
public record DailyAttendanceMarkInput(string StudentId, string? ReasonId);

/// <summary>Bitta DARS soatini saqlash. Ro'yxatda bo'lmagan o'quvchi — keldi.</summary>
/// <param name="SubGroup">Bo'linish (0 — butun sinf). Dars jadvalidagi qiymat bilan mos bo'lishi shart.</param>
public record SaveDailyAttendanceRequest(
    string ClassId, string Date, string SubjectId, int Period,
    List<DailyAttendanceMarkInput> Marks, int SubGroup = 0);

/// <summary>Davomat: kun ro'yxati, sinf kuni va dars soatini saqlash. Izoh — fayl boshida.</summary>
public sealed class DailyAttendanceService(IAppDbContext db)
{
    /// <summary>Kunning sinflar ro'yxati — qaysi sinfda nechta soat belgilangan.</summary>
    public async Task<DailyAttendanceOverviewDto> OverviewAsync(string date, CancellationToken ct = default)
    {
        var day = ParseDate(date);

        var classes = await db.Classes.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        var marks = await db.DailyAttendanceMarks.AsNoTracking()
            .Where(m => m.Date == date)
            .ToListAsync(ct);

        var counts = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived)
            .GroupBy(s => s.ClassName)
            .Select(g => new { ClassName = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countByName = counts.ToDictionary(x => x.ClassName ?? "", x => x.Count, StringComparer.Ordinal);

        var rows = new List<DailyAttendanceClassDto>();
        foreach (var c in classes)
        {
            var lessons = await DayLessonsAsync(c.Id, day, ct);
            var mine = marks.Where(m => m.ClassId == c.Id).ToList();
            rows.Add(new DailyAttendanceClassDto(
                c.Id, c.Name,
                countByName.GetValueOrDefault(c.Name, 0),
                lessons.Count,
                mine.Count,
                mine.Sum(m => m.AbsentCount),
                mine.Sum(m => m.LateCount)));
        }

        return new DailyAttendanceOverviewDto(
            date, rows,
            // "Belgilandi" = kunning BARCHA darslari belgilangan sinf.
            rows.Count(r => r.LessonCount > 0 && r.MarkedLessons >= r.LessonCount), rows.Count,
            rows.Sum(r => r.AbsentCount), rows.Sum(r => r.LateCount),
            rows.Sum(r => r.MarkedLessons), rows.Sum(r => r.LessonCount));
    }

    /// <summary>Bitta sinfning kuni — o'quvchilar va kunning barcha darslari.</summary>
    public async Task<DailyAttendanceClassDayDto?> ClassDayAsync(
        string classId, string date, CancellationToken ct = default)
    {
        var day = ParseDate(date);

        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct);
        if (cls is null) return null;

        var roster = await db.Students.AsNoTracking()
            .Where(s => s.ClassName == cls.Name && !s.IsArchived)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName, s.SubGroup })
            .ToListAsync(ct);
        var students = roster
            .Select(s => new DailyAttendanceStudentDto(s.Id, s.FullName))
            .ToList();

        var lessons = await DayLessonsAsync(classId, day, ct);
        var subjectIds = lessons.Select(l => l.SubjectId).Distinct().ToList();
        var subjectNames = subjectIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.Subjects.AsNoTracking()
                .Where(s => subjectIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, StringComparer.Ordinal, ct);

        var times = await db.LessonTimes.AsNoTracking()
            .ToDictionaryAsync(t => t.Period, t => t, ct);

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.ClassId == classId && e.Date == date && e.ReasonId != null
                        && e.OwnerKind == LessonOwnerKind.Class)
            .Select(e => new { e.StudentId, e.ReasonId, e.SubjectId, e.Period })
            .ToListAsync(ct);

        var marks = await db.DailyAttendanceMarks.AsNoTracking()
            .Where(m => m.ClassId == classId && m.Date == date)
            .ToListAsync(ct);

        var markerIds = marks.Select(m => m.MarkedBy).Distinct().ToList();
        var markerNames = markerIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.Users.AsNoTracking()
                .Where(u => markerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, StringComparer.Ordinal, ct);

        var lateIds = (await db.AbsenceReasons.AsNoTracking().Where(r => r.IsLate)
            .Select(r => r.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var lessonDtos = lessons.Select(l =>
        {
            var mine = entries
                .Where(e => e.SubjectId == l.SubjectId && e.Period == l.Period)
                .ToDictionary(e => e.StudentId, e => e.ReasonId!, StringComparer.Ordinal);
            var mark = marks.FirstOrDefault(m => m.SubjectId == l.SubjectId && m.Period == l.Period);
            times.TryGetValue(l.Period, out var time);

            // Bo'linish: 0 — butun sinf, 1/2 — faqat o'sha guruh
            // (`LessonRoster.ForLessonAsync` dagi qoida bilan bir xil).
            var lessonStudents = l.SubGroup == 0
                ? roster
                : roster.Where(x => x.SubGroup == l.SubGroup).ToList();

            return new DailyAttendanceLessonDto(
                l.SubjectId, subjectNames.GetValueOrDefault(l.SubjectId, "—"), l.Period, l.SubGroup,
                time?.StartTime, time?.EndTime,
                mark is not null,
                mark is null ? null : markerNames.GetValueOrDefault(mark.MarkedBy, "—"),
                mark?.MarkedAt,
                mine.Count(x => !lateIds.Contains(x.Value)),
                mine.Count(x => lateIds.Contains(x.Value)),
                [.. lessonStudents.Select(x => x.Id)],
                mine);
        }).ToList();

        var (absent, excused) = await ResolveReasons(ct);

        return new DailyAttendanceClassDayDto(
            cls.Id, cls.Name, date, students, lessonDtos,
            absent?.Id, absent?.Name, excused?.Id, excused?.Name);
    }

    /// <summary>
    /// Bitta DARS SOATINI saqlaydi: belgilangan o'quvchilarga sabab yoziladi,
    /// qolganlarniki tozalanadi va shu dars "belgilandi" deb qo'yiladi.
    /// Xato bo'lsa — matn qaytadi (<see cref="JournalService"/> naqshi).
    /// </summary>
    public async Task<string?> SaveAsync(
        SaveDailyAttendanceRequest req, string actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            return "Foydalanuvchi aniqlanmadi.";

        var day = ParseDate(req.Date);
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == req.ClassId, ct);
        if (cls is null) return "Sinf topilmadi.";

        var lessons = await DayLessonsAsync(req.ClassId, day, ct);
        if (lessons.Count == 0)
            return "Bu kunda jadval bo'yicha dars yo'q — davomat belgilanmaydi.";

        var lesson = lessons.FirstOrDefault(l =>
            l.SubjectId == req.SubjectId && l.Period == req.Period && l.SubGroup == req.SubGroup);
        if (lesson == default)
            return "Bu dars shu kunning jadvalida yo'q.";

        var quarter = await QuarterOfAsync(req.Date, ct);
        if (quarter is null) return "Bu sana hech qaysi chorakka kirmaydi.";

        var reasons = await db.AbsenceReasons.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r, StringComparer.Ordinal, ct);

        var marks = req.Marks ?? [];
        if (marks.Any(m => m.ReasonId is not null && !reasons.ContainsKey(m.ReasonId)))
            return "Noma'lum davomat sababi.";

        var roster = await db.Students.AsNoTracking()
            .Where(s => s.ClassName == cls.Name && !s.IsArchived)
            .Select(s => new { s.Id, s.SubGroup })
            .ToListAsync(ct);
        // Bo'lingan darsda faqat O'SHA guruh belgilanadi — qolgan yarim sinf
        // boshqa xonada va ularni "kelmadi" deb yozib qo'yish xato bo'lardi.
        var students = lesson.SubGroup == 0
            ? roster
            : roster.Where(s => s.SubGroup == lesson.SubGroup).ToList();
        var studentIds = students.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        if (marks.Any(m => !studentIds.Contains(m.StudentId)))
            return "Ro'yxatda yo'q o'quvchi belgilandi.";

        var byStudent = marks.ToDictionary(m => m.StudentId, m => m.ReasonId, StringComparer.Ordinal);

        var existing = await db.JournalEntries
            .Where(e => e.ClassId == req.ClassId && e.Date == req.Date
                        && e.SubjectId == req.SubjectId && e.Period == req.Period
                        && e.OwnerKind == LessonOwnerKind.Class)
            .ToListAsync(ct);

        foreach (var student in students)
        {
            byStudent.TryGetValue(student.Id, out var reasonId);
            var row = existing.FirstOrDefault(e => e.StudentId == student.Id);

            if (reasonId is null)
            {
                if (row is null) continue;
                // Faqat SABABNI olib tashlaymiz. Bahosi (yoki uyga vazifa /
                // xulq / o'zlashtirish) bor qator saqlanib qoladi — davomat
                // belgilash bahoni o'chirib yubormaydi.
                row.ReasonId = null;
                if (row.Grade is null && row.Homework == 0 && row.Behavior == 0 && row.Mastery is null)
                    db.JournalEntries.Remove(row);
                continue;
            }

            if (row is null)
            {
                db.JournalEntries.Add(new JournalEntry
                {
                    ClassId = req.ClassId,
                    SubjectId = req.SubjectId,
                    Quarter = quarter.Value,
                    StudentId = student.Id,
                    Date = req.Date,
                    Period = req.Period,
                    SubGroup = student.SubGroup,
                    OwnerKind = LessonOwnerKind.Class,
                    ReasonId = reasonId,
                });
            }
            else
            {
                row.ReasonId = reasonId;
            }
        }

        var absent = marks.Count(m => m.ReasonId is not null && !reasons[m.ReasonId].IsLate);
        var late = marks.Count(m => m.ReasonId is not null && reasons[m.ReasonId].IsLate);

        var mark = await db.DailyAttendanceMarks.FirstOrDefaultAsync(m =>
            m.ClassId == req.ClassId && m.Date == req.Date
            && m.SubjectId == req.SubjectId && m.Period == req.Period, ct);
        if (mark is null)
        {
            mark = new DailyAttendanceMark
            {
                ClassId = req.ClassId,
                Date = req.Date,
                SubjectId = req.SubjectId,
                Period = req.Period,
            };
            db.DailyAttendanceMarks.Add(mark);
        }
        mark.MarkedBy = actorId;
        mark.MarkedAt = AppClock.NowInstant;
        mark.AbsentCount = absent;
        mark.LateCount = late;

        await db.SaveChangesAsync(ct);
        return null;
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    /// <summary>
    /// Ekrandagi ikki tugma qaysi katalog sababini yozishini hal qiladi.
    ///
    /// <para>
    /// QIZIL ("kelmadi") — nomida "sababsiz" bo'lgan tur; topilmasa
    /// kech-emas turlardan oxirgisi. SARIQ ("sababli") — nomida "sababli"
    /// bo'lgani, bo'lmasa "ruxsat", bo'lmasa "kasal", bo'lmasa qolgan
    /// kech-emas turlardan birinchisi.
    /// </para>
    /// <para>
    /// Nomga qarab tanlashning sababi: katalog MAKTABNIKI — administrator
    /// turlarni qo'shadi, nomini o'zgartiradi va o'chiradi
    /// ("Davomat sabablari" sozlamasi). Kodga id yozib qo'yish o'sha
    /// ekranni buzardi, "is_excused" kabi yangi ustun esa katalogga
    /// ma'no qo'shib, uni sozlamadan chiqarib yuborardi. Ekran qaysi
    /// tur tanlanganini FOYDALANUVCHIGA KO'RSATADI (javobdagi nomlar),
    /// ya'ni tanlov yashirin qolmaydi.
    /// </para>
    /// </summary>
    private async Task<(AbsenceReason? Absent, AbsenceReason? Excused)> ResolveReasons(
        CancellationToken ct)
    {
        var all = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var usable = all.Where(r => !r.IsLate).ToList();
        if (usable.Count == 0) return (null, null);

        static bool Named(AbsenceReason r, string part) =>
            r.Name.Contains(part, StringComparison.OrdinalIgnoreCase);

        var absent = usable.FirstOrDefault(r => Named(r, "sababsiz")) ?? usable[^1];

        var excused = usable.FirstOrDefault(r => Named(r, "sababli") && r.Id != absent.Id)
            ?? usable.FirstOrDefault(r => Named(r, "ruxsat") && r.Id != absent.Id)
            ?? usable.FirstOrDefault(r => Named(r, "kasal") && r.Id != absent.Id)
            ?? usable.FirstOrDefault(r => r.Id != absent.Id);

        return (absent, excused);
    }

    private static DateOnly ParseDate(string date) =>
        DateOnly.TryParse(date, out var d) ? d : AppClock.Today;

    private async Task<int?> QuarterOfAsync(string date, CancellationToken ct)
    {
        var q = await db.Quarters.AsNoTracking().FirstOrDefaultAsync(x =>
            string.Compare(date, x.StartDate) >= 0 && string.Compare(date, x.EndDate) <= 0, ct);
        return q?.Quarter;
    }

    /// <summary>
    /// Kunning darslari — hafta shabloni orqali, `AttendanceController.GetDaily`
    /// bilan AYNAN bir xil yo'l: chorak → hafta → shu haftaga biriktirilgan
    /// shablon → o'sha kunning darslari.
    /// </summary>
    private async Task<List<(string SubjectId, int Period, int SubGroup)>> DayLessonsAsync(
        string classId, DateOnly day, CancellationToken ct)
    {
        var jsDay = (int)day.DayOfWeek; // 0 = yakshanba
        if (jsDay == 0) return [];
        var lessonDay = jsDay - 1; // dushanba = 0

        var date = day.ToString("yyyy-MM-dd");
        var q = await db.Quarters.AsNoTracking().FirstOrDefaultAsync(x =>
            string.Compare(date, x.StartDate) >= 0 && string.Compare(date, x.EndDate) <= 0, ct);
        if (q is null) return [];

        var week = ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate)
            .FirstOrDefault(w => string.CompareOrdinal(date, w.StartISO) >= 0
                                 && string.CompareOrdinal(date, w.EndISO) <= 0);
        if (week is null) return [];

        var assignment = await db.WeekAssignments.AsNoTracking().FirstOrDefaultAsync(a =>
            a.ClassId == classId && a.Quarter == q.Quarter && a.Week == week.Week
            && a.OwnerKind == LessonOwnerKind.Class, ct);
        if (assignment?.TemplateId is null) return [];

        var tpl = await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
            .FirstOrDefaultAsync(t => t.Id == assignment.TemplateId, ct);
        if (tpl is null) return [];

        return [.. tpl.Lessons
            .Where(l => l.Day == lessonDay)
            .OrderBy(l => l.Period).ThenBy(l => l.SubGroup)
            .Select(l => (l.SubjectId, l.Period, l.SubGroup))];
    }
}
