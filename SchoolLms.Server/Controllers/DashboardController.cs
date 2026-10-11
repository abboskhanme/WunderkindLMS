using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

using SchoolLms.Domain;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Direktor bosh sahifasi.
///
/// <para>
/// <b>O'quv guruhlari</b> (G-13, students-parity.md §2.1.4): guruh darsi ham o'tiladi va
/// unda ham davomat belgilanadi, ya'ni u qatnashuvchining SINFI qatoriga qo'shiladi
/// (guruhning o'zi alohida qator emas — §2.1.6 G-15). Cut-over o'chirgichi
/// (<c>group_lessons_enabled</c>) o'chiq bo'lsa guruh qatorlari BUTUNLAY chiqarib
/// tashlanadi va bu sahifadagi har bir raqam bugungisi bo'lib qoladi (§4.3).
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
// Bosh sahifa ham ruxsat bilan (mijoz, 2026-09-24): xodim uni "dashboard" ruxsati bilangina O'QIY oladi.
[AdminPerm("dashboard", GatedRead = true)]
[Route("api/admin/dashboard")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// <para>
    /// <b>Period filter</b> (client, 2026-10-08: "Bugun / Bu hafta / Bu oy" plus a custom range, for the
    /// whole page). With <paramref name="from"/>..<paramref name="to"/> every figure that happens on a
    /// date follows the period: average grade, attendance rate, the class chart and top classes,
    /// "Davomat analitikasi" (summed over the period's days), absentees and first payments. Headcounts,
    /// balances and class counts are the school's state right now and do not move.
    /// </para>
    /// <para>Without a period the page keeps its original windows (all-time grades, today's periods,
    /// the last 30 days of absences).</para>
    /// </summary>
    /// <param name="from">Period start, <c>yyyy-MM-dd</c> (inclusive). Must come with <paramref name="to"/>.</param>
    /// <param name="to">Period end, <c>yyyy-MM-dd</c> (inclusive).</param>
    [HttpGet]
    public async Task<ActionResult<AdminDashboardDto>> Get([FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        DateOnly? periodStart = null, periodEnd = null;
        if (from is not null || to is not null)
        {
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var start)
                || !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var end) || start > end)
                return BadRequest(new { message = "Davr noto'g'ri: from va to (yyyy-MM-dd) kerak, from ≤ to." });
            (periodStart, periodEnd) = (start, end);
        }
        var fromIso = periodStart?.ToString("yyyy-MM-dd");
        var toIso = periodEnd?.ToString("yyyy-MM-dd");

        var studentsCount = await db.Students.CountAsync(s => !s.IsArchived);
        var teachersCount = await db.Teachers.CountAsync();
        var classes = await db.Classes.OrderBy(c => c.Grade).ToListAsync();
        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync();

        // O'chirgich o'chiq — ODDIY guruh qatorlari YO'Q deb qaraladi (§4.3); yo'nalish
        // guruhi o'chirgichga qaramaydi (LessonRoster.GroupScopeAsync).
        var scope = await LessonRoster.GroupScopeAsync(db);
        var trackIds = scope.TrackIdList;
        var groupsOn = scope.AllGroups;
        var entriesQ = db.JournalEntries
            .Where(e => groupsOn || e.OwnerKind != LessonOwnerKind.Group || trackIds.Contains(e.ClassId));
        // `JournalEntry.Date` is a `yyyy-MM-dd` string, so the period compares as text.
        if (fromIso is not null)
            entriesQ = entriesQ.Where(e => string.Compare(e.Date, fromIso) >= 0 && string.Compare(e.Date, toIso) <= 0);
        var entries = await entriesQ.ToListAsync();

        // Davomat FAQAT bo'lgan darslar bo'yicha: o'tildi (Conducted) YOKI davomat belgilangan
        // (HeldLessons qoidasi). Bo'lmagan darslar hisobga olinmaydi.
        var heldLessons = (await HeldLessons.ListAsync(db, null, fromIso, toIso))
            .Where(n => scope.Allows(n.OwnerKind, n.ClassId))
            .ToList();
        var conductedByClass = heldLessons
            .GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(n => (n.SubjectId, n.Date, n.Period)).ToHashSet());
        // Held lessons per owner with the half they are for — the student-day count below needs it.
        var heldByOwner = heldLessons
            .GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // O'quvchi → uning faol guruhlari (o'chirgich o'chiq bo'lsa — bo'sh).
        var groupsByStudent = await GroupIdsByStudentAsync(db, scope);

        // "Kech keldi" turidagi sabablar davomatsizlik sifatida hisoblanmaydi.
        var lateReasonIds = (await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync())
            .ToHashSet();

        double AvgGrade(IEnumerable<JournalEntry> es)
        {
            var grades = es.Where(e => e.Grade.HasValue).Select(e => (double)e.Grade!.Value).ToList();
            return grades.Count > 0 ? Math.Round(grades.Average(), 1) : 0;
        }

        // Sinfning o'quvchilari va ularning guruh id'lari — pastdagi ikki hisob uchun.
        List<Student> StudentsOf(SchoolClass c) => [.. students.Where(s => s.ClassName == c.Name)];
        HashSet<string> GroupIdsOf(IEnumerable<Student> list) =>
            [.. list.SelectMany(s => groupsByStudent.GetValueOrDefault(s.Id) ?? [])];

        // Sinf bahosi: sinfning O'Z darslari (bugungi qoida) + shu sinf bolalarining
        // guruh darslaridagi baholari (G-15: guruh bahosi sinf ostida sanaladi).
        IEnumerable<JournalEntry> EntriesOf(SchoolClass c)
        {
            var list = StudentsOf(c);
            var groupIds = GroupIdsOf(list);
            var ids = list.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            return entries.Where(e => e.ClassId == c.Id
                || (groupIds.Contains(e.ClassId) && ids.Contains(e.StudentId)));
        }

        // Absent lessons on held lessons, per (student, date) — the student-day count below.
        var absentLessons = new Dictionary<(string StudentId, string Date), int>();
        foreach (var e in entries)
        {
            if (e.ReasonId is null || lateReasonIds.Contains(e.ReasonId)) continue;
            if (!conductedByClass.TryGetValue(e.ClassId, out var slots) || !slots.Contains((e.SubjectId, e.Date, e.Period))) continue;
            absentLessons[(e.StudentId, e.Date)] = absentLessons.GetValueOrDefault((e.StudentId, e.Date)) + 1;
        }

        // Sinf davomati — O'QUVCHI-KUN bo'yicha (mijoz, 2026-10-07: "bitta o'quvchi bir kishiga"):
        // har o'quvchining o'tilgan darsi bor har kuni bitta imkoniyat; o'sha kunning HAMMA darsida
        // yo'q bo'lsa — bitta "kelmadi". Bir qismida yo'q bo'lgani — keldi. Dars bo'lmasa — null.
        (long Opp, int Abs) ClassAttParts(SchoolClass c)
        {
            var classStudents = StudentsOf(c);
            if (classStudents.Count == 0) return (0, 0);

            long opp = 0;
            var abs = 0;
            var classLessons = heldByOwner.GetValueOrDefault(c.Id) ?? [];
            foreach (var s in classStudents)
            {
                var held = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var l in classLessons.Where(l => l.SubGroup == 0 || l.SubGroup == s.SubGroup))
                    held[l.Date] = held.GetValueOrDefault(l.Date) + 1;
                // Guruh darslari (o'chirgich yoqilgan bo'lsa) — o'quvchining o'z guruhlari.
                foreach (var groupId in groupsByStudent.GetValueOrDefault(s.Id) ?? [])
                    foreach (var l in heldByOwner.GetValueOrDefault(groupId) ?? [])
                        held[l.Date] = held.GetValueOrDefault(l.Date) + 1;

                foreach (var (date, lessons) in held)
                {
                    opp++;
                    if (absentLessons.GetValueOrDefault((s.Id, date)) >= lessons) abs++;
                }
            }

            return (opp, abs);
        }
        double? Rate(long opp, int abs) => opp > 0 ? Math.Round((double)(opp - abs) / opp * 100) : null;

        long totalOpp = 0;
        var totalAbs = 0;
        var classPerformance = classes.Select(c =>
        {
            var (opp, abs) = ClassAttParts(c);
            totalOpp += opp;
            totalAbs += abs;
            return new ClassPerformanceItemDto(c.Id, c.Name, AvgGrade(EntriesOf(c)), Rate(opp, abs));
        }).ToList();

        // ---- Bosh sahifadagi qolgan raqamlar ----
        var classNames = classes.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var unassigned = students.Count(s => string.IsNullOrWhiteSpace(s.ClassName)
                                             || !classNames.Contains(s.ClassName));
        var archivedCount = await db.Students.CountAsync(s => s.IsArchived);

        // Pul: qoldiq HISOBLANADI (P1-21 dan keyin `students.balance` yo'q).
        // Bitta so'rov — o'quvchilar soniga qarab so'rov soni o'zgarmaydi.
        var balances = await new StudentBalanceQuery(db)
            .ForManyAsync(students.Select(s => s.Id).ToList());
        var creditCount = balances.Count(b => b.Value > 0m);
        var debtorCount = balances.Count(b => b.Value < 0m);

        var paidAtLeastOnce = await db.Payments.AsNoTracking()
            .Where(p => p.ReversalOf == null)
            .Select(p => p.StudentId)
            .Distinct()
            .CountAsync();

        // ---- Vidjetlar paneli (2026-09-22): EduSchool'dagi qolgan hisoblar ----
        var unassignedIds = students
            .Where(s => string.IsNullOrWhiteSpace(s.ClassName) || !classNames.Contains(s.ClassName))
            .Select(s => s.Id).ToList();
        var leftFromClass = unassignedIds.Count == 0 ? 0 : await db.ClassMemberships.AsNoTracking()
            .Where(m => m.LeftOn != null && unassignedIds.Contains(m.StudentId))
            .Select(m => m.StudentId).Distinct().CountAsync();
        var unassignedSet = unassignedIds.ToHashSet(StringComparer.Ordinal);
        var waiting = students.Count(s => unassignedSet.Contains(s.Id) && s.TargetGrade != null);

        var today = AppClock.Today;
        var monthStart = AppClock.InstantOn(new DateOnly(today.Year, today.Month, 1));
        var firstPaymentThisMonth = await db.Payments.AsNoTracking()
            .Where(p => p.ReversalOf == null)
            .GroupBy(p => p.StudentId)
            .Where(g => g.Min(p => p.ReceivedAt) >= monthStart)
            .CountAsync();

        int? firstPaymentInPeriod = null;
        if (periodStart is { } ps && periodEnd is { } pe)
        {
            var (startAt, endAt) = (LocalMidnight(ps), LocalMidnight(pe.AddDays(1)));
            firstPaymentInPeriod = await db.Payments.AsNoTracking()
                .Where(p => p.ReversalOf == null)
                .GroupBy(p => p.StudentId)
                .Where(g => g.Min(p => p.ReceivedAt) >= startAt && g.Min(p => p.ReceivedAt) < endAt)
                .CountAsync();
        }

        var stats = new AdminStatsDto(
            studentsCount, teachersCount, AvgGrade(entries), Rate(totalOpp, totalAbs),
            unassigned, classes.Count, archivedCount,
            creditCount, debtorCount, paidAtLeastOnce,
            ActiveCount: studentsCount - unassigned,
            LeftFromClassCount: leftFromClass,
            WaitingCount: waiting,
            FirstPaymentThisMonthCount: firstPaymentThisMonth,
            MaleCount: students.Count(s => s.Gender == "male"),
            FemaleCount: students.Count(s => s.Gender == "female"),
            FirstPaymentInPeriodCount: firstPaymentInPeriod);

        var classHeadcounts = classes
            .Where(c => !c.IsArchived)
            .Select(c =>
            {
                var mine = students.Where(s => s.ClassName == c.Name).ToList();
                return new ClassHeadcountDto(
                    c.Id, c.Name, c.Grade, mine.Count,
                    mine.Count(s => s.Gender == "male"), mine.Count(s => s.Gender == "female"),
                    c.Capacity);
            })
            .ToList();

        var topClasses = classes
            .Select(c => new TopClassDto(
                c.Id, c.Name,
                students.Count(s => s.ClassName == c.Name),
                AvgGrade(EntriesOf(c))))
            .OrderByDescending(t => t.AverageGrade)
            .Take(5)
            .ToList();

        var groupSizes = await GroupSizesAsync(db, scope);

        // "Davomat belgilash" ekranida belgilangan darslar (HeldLessons qoidasi): bunday darsda
        // jurnalda yozuvi YO'Q o'quvchi — KELDI, "tekshirilmagan" emas. Ilgari bu ikki vidjet
        // faqat jurnal yozuvlarini ko'rardi va belgilangan darsdagi kelganlar "tekshirilmagan"
        // (bugungi soatlar) yoki umuman ko'rinmas (oxirgi kelgan kuni) bo'lib qolardi.
        var membersByGroup = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (studentId, groupIds) in groupsByStudent)
            foreach (var groupId in groupIds)
            {
                if (!membersByGroup.TryGetValue(groupId, out var list)) membersByGroup[groupId] = list = [];
                list.Add(studentId);
            }
        var classNameById = classes.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        List<string> RosterOf(HeldLesson m) => m.OwnerKind == LessonOwnerKind.Group
            ? membersByGroup.GetValueOrDefault(m.ClassId) ?? []
            : classNameById.TryGetValue(m.ClassId, out var name)
                ? [.. students.Where(s => s.ClassName == name && (m.SubGroup == 0 || s.SubGroup == m.SubGroup))
                    .Select(s => s.Id)]
                : [];
        // Absences are counted inside the period, but "Oxirgi kelgan" looks back at least 30 days
        // from its end — a pupil absent today must not read as never seen. No period: the last 30 days.
        var lookEnd = periodEnd ?? today;
        var lookStart = periodStart is { } early && early < lookEnd.AddDays(-30) ? early : lookEnd.AddDays(-30);
        var lookFrom = lookStart.ToString("yyyy-MM-dd");
        var markedLessons = (await HeldLessons.MarkedAsync(db, null, lookFrom, lookEnd.ToString("yyyy-MM-dd")))
            .Where(m => scope.Allows(m.OwnerKind, m.ClassId))
            .Select(m => (Lesson: m, Roster: RosterOf(m)))
            .ToList();

        // "Davomat analitikasi": today, or every day of the period up to today, summed per period.
        var lastDay = periodEnd is { } endDay && endDay < today ? endDay : today;
        var days = new List<string>();
        for (var d = periodStart ?? today; d <= lastDay; d = d.AddDays(1))
            days.Add(d.ToString("yyyy-MM-dd"));

        // Timetable lessons of those days (2026-10-07): a class nobody marked is expected too, so it
        // shows as "tekshirilmagan" instead of dropping out of the widget. Today only the started ones.
        var todayIso = today.ToString("yyyy-MM-dd");
        var now = TimeOnly.FromDateTime(AppClock.Now);
        var bells = await BellTimes.LoadAsync(db);
        bool Started(HeldLesson l) =>
            bells.Time(l.ClassId, l.Period) is not { } t || !TimeOnly.TryParse(t.StartTime, out var start) || start <= now;
        List<HeldLesson> scheduled = days.Count == 0 ? [] : (await HeldLessons.ScheduledAsync(db, null, days[0], days[^1]))
            .Where(l => scope.Allows(l.OwnerKind, l.ClassId) && (l.Date != todayIso || Started(l)))
            .ToList();

        var reasonRows = await db.AbsenceReasons.AsNoTracking().ToListAsync();
        var unexcusedIds = reasonRows.Where(AttendanceAnalytics.IsUnexcusedReason)
            .Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        return new AdminDashboardDto(
            stats, classPerformance, topClasses,
            AttendanceByPeriod(days, students, entries, lateReasonIds, groupSizes, classNameById, markedLessons, scheduled),
            await AbsentStudentsAsync(students, lateReasonIds, unexcusedIds, markedLessons, lookFrom, toIso, fromIso),
            classHeadcounts);
    }

    /// <summary>Local (Tashkent, always UTC+5) midnight of <paramref name="day"/> as a UTC instant —
    /// Npgsql accepts <c>timestamptz</c> parameters only with offset 0.</summary>
    private static DateTimeOffset LocalMidnight(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue) - TimeSpan.FromHours(5), TimeSpan.Zero);

    /// <summary>
    /// O'quvchi id → uning faol o'quv guruhlarining id'lari. O'chirgich o'chiq
    /// bo'lsa — bo'sh lug'at (§4.3).
    /// </summary>
    private static async Task<Dictionary<string, List<string>>> GroupIdsByStudentAsync(
        AppDbContext db, GroupLessonScope scope)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!scope.AnyGroup) return result;

        var groupIds = (await db.StudyGroups.AsNoTracking()
                .Where(g => !g.IsArchived).Select(g => g.Id).ToListAsync())
            .Where(id => scope.AllowsGroup(id.ToString())).ToHashSet();
        if (groupIds.Count == 0) return result;

        foreach (var m in await db.StudyGroupMembers.AsNoTracking()
                     .Where(m => m.LeftOn == null)
                     .Select(m => new { m.GroupId, m.StudentId }).ToListAsync())
        {
            if (!groupIds.Contains(m.GroupId)) continue;
            if (!result.TryGetValue(m.StudentId, out var list)) result[m.StudentId] = list = [];
            list.Add(m.GroupId.ToString());
        }
        return result;
    }

    /// <summary>
    /// Guruh id → faol a'zolar soni. O'chirgich o'chiq bo'lsa — bo'sh, ya'ni
    /// "kutilgan" ustuni bugungidek faqat sinflardan hisoblanadi.
    /// </summary>
    private static async Task<Dictionary<string, int>> GroupSizesAsync(AppDbContext db, GroupLessonScope scope)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!scope.AnyGroup) return result;

        var groupIds = (await db.StudyGroups.AsNoTracking()
                .Where(g => !g.IsArchived).Select(g => g.Id).ToListAsync())
            .Where(id => scope.AllowsGroup(id.ToString())).ToHashSet();
        if (groupIds.Count == 0) return result;

        foreach (var g in (await db.StudyGroupMembers.AsNoTracking()
                     .Where(m => m.LeftOn == null)
                     .Select(m => m.GroupId).ToListAsync())
                 .Where(groupIds.Contains)
                 .GroupBy(id => id))
            result[g.Key.ToString()] = g.Count();
        return result;
    }

    /// <summary>
    /// Davomat — dars soatlari kesimida: har bir kun alohida hisoblanadi (bugungi qoidaning
    /// o'zi) va <paramref name="days"/> bo'yicha qo'shiladi. Davr filtri bo'lmasa — faqat bugun.
    ///
    /// <para>
    /// "Tekshirilmagan" ATAYLAB alohida ustun: davomati belgilanmagan o'quvchini
    /// "keldi" deb hisoblash — eng oson va eng zararli xato. Direktor 100%
    /// ko'rib, aslida hech kim davomat qo'ymaganini bilmay qolardi.
    /// </para>
    /// </summary>
    /// <param name="days">The days to sum, <c>yyyy-MM-dd</c>, none after today.</param>
    /// <param name="groupSizes">
    /// Guruh id → faol a'zolar soni. Guruh darsi shu soatda bo'lsa, "kutilgan" ga
    /// SINFning emas, GURUHning soni qo'shiladi (G-13). O'chirgich o'chiq bo'lsa
    /// lug'at bo'sh va bu qo'shimcha umuman ishlamaydi.
    /// </param>
    /// <param name="classNameById">Sinf id → nomi (belgilangan sinf darsining "kutilgan" soni uchun).</param>
    /// <param name="markedLessons">
    /// "Davomat belgilash" ekranida belgilangan darslar va ularning ro'yxati. Shunday
    /// darsdagi HAR BIR o'quvchi tekshirilgan: yozuvi yo'q — keldi (HeldLessons qoidasi).
    /// </param>
    /// <param name="scheduled">Timetable lessons of <paramref name="days"/> (today: only the started
    /// ones) — their class (or group) is expected even when nobody marked it, so the gap shows as
    /// "tekshirilmagan".</param>
    private static List<AttendanceByPeriodDto> AttendanceByPeriod(
        IReadOnlyList<string> days,
        List<Student> students, List<JournalEntry> entries, HashSet<string> lateReasonIds,
        Dictionary<string, int> groupSizes, Dictionary<string, string> classNameById,
        List<(HeldLesson Lesson, List<string> Roster)> markedLessons, List<HeldLesson> scheduled)
    {
        // `JournalEntry.Date` — `yyyy-MM-dd` satri, shuning uchun solishtirish ham satrda.
        var daySet = days.ToHashSet(StringComparer.Ordinal);
        var entriesByDay = entries.Where(e => daySet.Contains(e.Date)).ToLookup(e => e.Date, StringComparer.Ordinal);
        var markedByDay = markedLessons.Where(m => daySet.Contains(m.Lesson.Date))
            .ToLookup(m => m.Lesson.Date, StringComparer.Ordinal);
        var scheduledByDay = scheduled.ToLookup(l => l.Date, StringComparer.Ordinal);
        var byClass = students.GroupBy(s => s.ClassName ?? "")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var classOf = students.ToDictionary(s => s.Id, s => s.ClassName, StringComparer.Ordinal);

        var rows = new List<AttendanceByPeriodDto>(10);
        for (var period = 1; period <= 10; period++)
        {
            int expectedSum = 0, presentSum = 0, absentSum = 0, uncheckedSum = 0;
            foreach (var day in days)
            {
                var all = entriesByDay[day].Where(e => e.Period == period).ToList();
                // Sinf va guruh yozuvlari ALOHIDA: sinf tarafi bugungi hisobning aynan o'zi.
                var slot = all.Where(e => e.OwnerKind != LessonOwnerKind.Group).ToList();
                var groupSlot = all.Where(e => e.OwnerKind == LessonOwnerKind.Group).ToList();
                var markedSlot = markedByDay[day].Where(m => m.Lesson.Period == period).ToList();
                var scheduledSlot = scheduledByDay[day].Where(l => l.Period == period).ToList();
                if (all.Count == 0 && markedSlot.Count == 0 && scheduledSlot.Count == 0) continue;

                var marked = all.Select(e => e.StudentId)
                    .Concat(markedSlot.SelectMany(m => m.Roster))
                    .Distinct(StringComparer.Ordinal).Count();
                var absent = all.Count(e => e.ReasonId != null && !lateReasonIds.Contains(e.ReasonId));

                // Kutilgan son — shu sinflardagi barcha o'quvchilar, belgilanmaganlar ham.
                var slotClassNames = slot
                    .Select(e => classOf.GetValueOrDefault(e.StudentId))
                    .Concat(markedSlot.Where(m => m.Lesson.OwnerKind != LessonOwnerKind.Group)
                        .Select(m => classNameById.GetValueOrDefault(m.Lesson.ClassId)))
                    .Concat(scheduledSlot.Where(l => l.OwnerKind != LessonOwnerKind.Group)
                        .Select(l => classNameById.GetValueOrDefault(l.ClassId)))
                    .Where(n => n is not null)
                    .ToHashSet(StringComparer.Ordinal);
                var totalInClasses = byClass
                    .Where(kv => slotClassNames.Contains(kv.Key))
                    .Sum(kv => kv.Value);
                // Guruh darsida esa — guruhning o'z a'zolari soni (sinfniki emas).
                var totalInGroups = groupSlot.Select(e => e.ClassId)
                    .Concat(markedSlot.Where(m => m.Lesson.OwnerKind == LessonOwnerKind.Group).Select(m => m.Lesson.ClassId))
                    .Concat(scheduledSlot.Where(l => l.OwnerKind == LessonOwnerKind.Group).Select(l => l.ClassId))
                    .Distinct(StringComparer.Ordinal)
                    .Sum(id => groupSizes.GetValueOrDefault(id));
                var expected = Math.Max(totalInClasses + totalInGroups, marked);

                expectedSum += expected;
                presentSum += marked - absent;
                absentSum += absent;
                uncheckedSum += Math.Max(0, expected - marked);
            }

            rows.Add(new AttendanceByPeriodDto(period, expectedSum, presentSum, absentSum, uncheckedSum));
        }
        return rows;
    }

    /// <summary>
    /// Davrda (filtr bo'lmasa — oxirgi 30 kunda) eng ko'p SABABSIZ dars qoldirganlar.
    /// Sababli yo'qlik va "kech keldi" bu ro'yxatga tushmaydi — aks holda
    /// kasal bo'lgan bola intizom muammosi bo'lib ko'rinardi.
    /// </summary>
    /// <param name="unexcusedIds">Sababsiz sabablar — <see cref="AttendanceAnalytics.IsUnexcusedReason"/>.
    /// Ro'yxat va vidjet sarlavhasi "sababsiz" deydi; ilgari kasal/oilaviy sabab ham sanalardi.</param>
    /// <param name="markedLessons">Belgilangan darslar: yo'qlik yozuvi yo'q o'quvchi o'sha kuni KELGAN
    /// ("Oxirgi kelgan" ustuni uchun) — "Davomat belgilash" ekranida keldi yozuv qoldirmaydi.</param>
    /// <param name="lookFrom">First day read — "Oxirgi kelgan" looks back this far.</param>
    /// <param name="lookTo">Last day read; null — no upper bound.</param>
    /// <param name="countFrom">Absences before this day are not counted (the period start); null — all read.</param>
    private async Task<List<AbsentStudentDto>> AbsentStudentsAsync(
        List<Student> students, HashSet<string> lateReasonIds, HashSet<string> unexcusedIds,
        List<(HeldLesson Lesson, List<string> Roster)> markedLessons,
        string lookFrom, string? lookTo, string? countFrom)
    {
        // Satr sanalar `yyyy-MM-dd` — leksikografik taqqoslash xronologik bilan bir xil.
        var recentQ = db.JournalEntries.AsNoTracking().Where(e => string.Compare(e.Date, lookFrom) >= 0);
        if (lookTo is not null) recentQ = recentQ.Where(e => string.Compare(e.Date, lookTo) <= 0);
        var recent = await recentQ
            .Select(e => new { e.StudentId, e.Date, e.ReasonId, e.ClassId, e.SubjectId, e.Period })
            .ToListAsync();

        var byStudent = recent.GroupBy(e => e.StudentId).ToDictionary(g => g.Key, g => g.ToList());
        var names = students.ToDictionary(s => s.Id, s => s);

        // Belgilangan darsda yo'qlik (kech keldidan boshqa) sababi bor katak — kelmagan.
        var absentAt = recent
            .Where(e => e.ReasonId != null && !lateReasonIds.Contains(e.ReasonId))
            .Select(e => (e.StudentId, e.ClassId, e.SubjectId, e.Date, e.Period))
            .ToHashSet();
        var seenOnMarked = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (m, roster) in markedLessons)
            foreach (var id in roster)
            {
                if (absentAt.Contains((id, m.ClassId, m.SubjectId, m.Date, m.Period))) continue;
                if (!seenOnMarked.TryGetValue(id, out var d) || string.CompareOrdinal(m.Date, d) > 0)
                    seenOnMarked[id] = m.Date;
            }

        return [.. byStudent
            .Select(kv =>
            {
                if (!names.TryGetValue(kv.Key, out var st)) return null;
                var missed = kv.Value
                    .Where(e => e.ReasonId != null && unexcusedIds.Contains(e.ReasonId)
                                && (countFrom is null || string.CompareOrdinal(e.Date, countFrom) >= 0))
                    .Select(e => e.Date).Distinct().Count();
                if (missed == 0) return null;
                var lastSeen = kv.Value
                    .Where(e => e.ReasonId == null || lateReasonIds.Contains(e.ReasonId))
                    .Select(e => e.Date)
                    .Append(seenOnMarked.GetValueOrDefault(kv.Key))
                    .Where(d => d is not null)
                    .DefaultIfEmpty(null!)
                    .Max();
                return new AbsentStudentDto(
                    st.Id, st.FullName, st.ClassName ?? "", missed,
                    lastSeen);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderByDescending(x => x.MissedDays)
            .ThenBy(x => x.FullName, StringComparer.Ordinal)];
        // Cheklov yo'q: bosh sahifa ro'yxatni sahifalab ko'rsatadi (20 tadan) —
        // 50 ta bilan kesilsa, keyingi sahifalar jim chala qolardi.
    }
}
