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
[Route("api/admin/dashboard")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminDashboardDto>> Get()
    {
        var studentsCount = await db.Students.CountAsync(s => !s.IsArchived);
        var teachersCount = await db.Teachers.CountAsync();
        var classes = await db.Classes.OrderBy(c => c.Grade).ToListAsync();
        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync();

        // O'chirgich o'chiq — guruh qatorlari YO'Q deb qaraladi (§4.3).
        var groupsOn = await LessonRoster.GroupLessonsEnabledAsync(db);
        var entries = await db.JournalEntries
            .Where(e => groupsOn || e.OwnerKind != LessonOwnerKind.Group).ToListAsync();

        // Davomat FAQAT o'tilgan darslar bo'yicha (Conducted=true). O'tilmagan darslar hisobga olinmaydi.
        var conductedByClass = (await db.LessonNotes
                .Where(n => n.Conducted && (groupsOn || n.OwnerKind != LessonOwnerKind.Group)).ToListAsync())
            .GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(n => (n.SubjectId, n.Date, n.Period)).ToHashSet());

        // O'quvchi → uning faol guruhlari (o'chirgich o'chiq bo'lsa — bo'sh).
        var groupsByStudent = await GroupIdsByStudentAsync(db, groupsOn);

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

        // Sinf davomati: o'tilgan darslar × o'quvchilar = imkoniyatlar; davomatsizliklar ayriladi.
        // O'tilgan dars bo'lmasa — ma'lumot yo'q (null), o'rtachaga qo'shilmaydi.
        (long Opp, int Abs) ClassAttParts(SchoolClass c)
        {
            var classStudents = StudentsOf(c);
            if (classStudents.Count == 0) return (0, 0);

            long opp = 0;
            var abs = 0;

            if (conductedByClass.TryGetValue(c.Id, out var set) && set.Count > 0)
            {
                opp += (long)set.Count * classStudents.Count;
                abs += entries.Count(e => e.ClassId == c.Id && e.ReasonId != null
                    && !lateReasonIds.Contains(e.ReasonId) && set.Contains((e.SubjectId, e.Date, e.Period)));
            }

            // Guruh darslari: har guruh uchun SHU SINFdan nechta bola qatnashadi.
            foreach (var groupId in GroupIdsOf(classStudents))
            {
                if (!conductedByClass.TryGetValue(groupId, out var groupSet) || groupSet.Count == 0) continue;
                var mine = classStudents
                    .Where(s => (groupsByStudent.GetValueOrDefault(s.Id) ?? []).Contains(groupId))
                    .Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
                if (mine.Count == 0) continue;

                opp += (long)groupSet.Count * mine.Count;
                abs += entries.Count(e => e.ClassId == groupId && mine.Contains(e.StudentId)
                    && e.ReasonId != null && !lateReasonIds.Contains(e.ReasonId)
                    && groupSet.Contains((e.SubjectId, e.Date, e.Period)));
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

        var stats = new AdminStatsDto(
            studentsCount, teachersCount, AvgGrade(entries), Rate(totalOpp, totalAbs),
            unassigned, classes.Count, archivedCount,
            creditCount, debtorCount, paidAtLeastOnce,
            ActiveCount: studentsCount - unassigned,
            LeftFromClassCount: leftFromClass,
            WaitingCount: waiting,
            FirstPaymentThisMonthCount: firstPaymentThisMonth,
            MaleCount: students.Count(s => s.Gender == "male"),
            FemaleCount: students.Count(s => s.Gender == "female"));

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

        var groupSizes = await GroupSizesAsync(db, groupsOn);
        return new AdminDashboardDto(
            stats, classPerformance, topClasses,
            AttendanceByPeriod(students, entries, lateReasonIds, groupSizes),
            await AbsentStudentsAsync(students, lateReasonIds),
            classHeadcounts);
    }

    /// <summary>
    /// O'quvchi id → uning faol o'quv guruhlarining id'lari. O'chirgich o'chiq
    /// bo'lsa — bo'sh lug'at (§4.3).
    /// </summary>
    private static async Task<Dictionary<string, List<string>>> GroupIdsByStudentAsync(
        AppDbContext db, bool groupsOn)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!groupsOn) return result;

        var groupIds = (await db.StudyGroups.AsNoTracking()
            .Where(g => !g.IsArchived).Select(g => g.Id).ToListAsync()).ToHashSet();
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
    private static async Task<Dictionary<string, int>> GroupSizesAsync(AppDbContext db, bool groupsOn)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!groupsOn) return result;

        var groupIds = (await db.StudyGroups.AsNoTracking()
            .Where(g => !g.IsArchived).Select(g => g.Id).ToListAsync()).ToHashSet();
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
    /// Bugungi davomat — dars soatlari kesimida.
    ///
    /// <para>
    /// "Tekshirilmagan" ATAYLAB alohida ustun: davomati belgilanmagan o'quvchini
    /// "keldi" deb hisoblash — eng oson va eng zararli xato. Direktor 100%
    /// ko'rib, aslida hech kim davomat qo'ymaganini bilmay qolardi.
    /// </para>
    /// </summary>
    /// <param name="groupSizes">
    /// Guruh id → faol a'zolar soni. Guruh darsi shu soatda bo'lsa, "kutilgan" ga
    /// SINFning emas, GURUHning soni qo'shiladi (G-13). O'chirgich o'chiq bo'lsa
    /// lug'at bo'sh va bu qo'shimcha umuman ishlamaydi.
    /// </param>
    private static List<AttendanceByPeriodDto> AttendanceByPeriod(
        List<Student> students, List<JournalEntry> entries, HashSet<string> lateReasonIds,
        Dictionary<string, int> groupSizes)
    {
        // `JournalEntry.Date` — `yyyy-MM-dd` satri, shuning uchun solishtirish ham satrda.
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var todays = entries.Where(e => e.Date == today).ToList();
        var byClass = students.GroupBy(s => s.ClassName ?? "")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var rows = new List<AttendanceByPeriodDto>(10);
        for (var period = 1; period <= 10; period++)
        {
            var all = todays.Where(e => e.Period == period).ToList();
            // Sinf va guruh yozuvlari ALOHIDA: sinf tarafi bugungi hisobning aynan o'zi.
            var slot = all.Where(e => e.OwnerKind != LessonOwnerKind.Group).ToList();
            var groupSlot = all.Where(e => e.OwnerKind == LessonOwnerKind.Group).ToList();

            var marked = all.Select(e => e.StudentId).Distinct().Count();
            var absent = all.Count(e => e.ReasonId != null && !lateReasonIds.Contains(e.ReasonId));
            var present = marked - absent;

            // Kutilgan son — shu sinflardagi barcha o'quvchilar, belgilanmaganlar ham.
            var totalInClasses = byClass
                .Where(kv => slot.Any(e => students.Any(s => s.Id == e.StudentId && s.ClassName == kv.Key)))
                .Sum(kv => kv.Value);
            // Guruh darsida esa — guruhning o'z a'zolari soni (sinfniki emas).
            var totalInGroups = groupSlot.Select(e => e.ClassId).Distinct(StringComparer.Ordinal)
                .Sum(id => groupSizes.GetValueOrDefault(id));
            var expected = Math.Max(totalInClasses + totalInGroups, marked);

            rows.Add(new AttendanceByPeriodDto(
                period, expected, present, absent, Math.Max(0, expected - marked)));
        }
        return rows;
    }

    /// <summary>
    /// Oxirgi 30 kunda eng ko'p SABABSIZ dars qoldirganlar.
    /// Sababli yo'qlik va "kech keldi" bu ro'yxatga tushmaydi — aks holda
    /// kasal bo'lgan bola intizom muammosi bo'lib ko'rinardi.
    /// </summary>
    private async Task<List<AbsentStudentDto>> AbsentStudentsAsync(
        List<Student> students, HashSet<string> lateReasonIds)
    {
        // Satr sanalar `yyyy-MM-dd` — leksikografik taqqoslash xronologik bilan bir xil.
        var from = AppClock.Today.AddDays(-30).ToString("yyyy-MM-dd");
        var recent = await db.JournalEntries.AsNoTracking()
            .Where(e => string.Compare(e.Date, from) >= 0)
            .Select(e => new { e.StudentId, e.Date, e.ReasonId })
            .ToListAsync();

        var byStudent = recent.GroupBy(e => e.StudentId).ToDictionary(g => g.Key, g => g.ToList());
        var names = students.ToDictionary(s => s.Id, s => s);

        return [.. byStudent
            .Select(kv =>
            {
                if (!names.TryGetValue(kv.Key, out var st)) return null;
                var missed = kv.Value
                    .Where(e => e.ReasonId != null && !lateReasonIds.Contains(e.ReasonId))
                    .Select(e => e.Date).Distinct().Count();
                if (missed == 0) return null;
                var lastSeen = kv.Value
                    .Where(e => e.ReasonId == null || lateReasonIds.Contains(e.ReasonId))
                    .Select(e => e.Date)
                    .DefaultIfEmpty(null!)
                    .Max();
                return new AbsentStudentDto(
                    st.Id, st.FullName, st.ClassName ?? "", missed,
                    lastSeen);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderByDescending(x => x.MissedDays)
            .ThenBy(x => x.FullName, StringComparer.Ordinal)
            .Take(50)];
    }
}
