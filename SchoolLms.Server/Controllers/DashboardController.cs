using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

using SchoolLms.Domain;
using SchoolLms.Application.Billing;

namespace SchoolLms.Server.Controllers;

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
        var entries = await db.JournalEntries.ToListAsync();

        // Davomat FAQAT o'tilgan darslar bo'yicha (Conducted=true). O'tilmagan darslar hisobga olinmaydi.
        var conductedByClass = (await db.LessonNotes.Where(n => n.Conducted).ToListAsync())
            .GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(n => (n.SubjectId, n.Date, n.Period)).ToHashSet());

        // "Kech keldi" turidagi sabablar davomatsizlik sifatida hisoblanmaydi.
        var lateReasonIds = (await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync())
            .ToHashSet();

        double AvgGrade(IEnumerable<JournalEntry> es)
        {
            var grades = es.Where(e => e.Grade.HasValue).Select(e => (double)e.Grade!.Value).ToList();
            return grades.Count > 0 ? Math.Round(grades.Average(), 1) : 0;
        }

        // Sinf davomati: o'tilgan darslar × o'quvchilar = imkoniyatlar; davomatsizliklar ayriladi.
        // O'tilgan dars bo'lmasa — ma'lumot yo'q (null), o'rtachaga qo'shilmaydi.
        (long Opp, int Abs) ClassAttParts(SchoolClass c)
        {
            if (!conductedByClass.TryGetValue(c.Id, out var set) || set.Count == 0) return (0, 0);
            var studentsN = students.Count(s => s.ClassName == c.Name);
            if (studentsN == 0) return (0, 0);
            var abs = entries.Count(e => e.ClassId == c.Id && e.ReasonId != null
                && !lateReasonIds.Contains(e.ReasonId) && set.Contains((e.SubjectId, e.Date, e.Period)));
            return ((long)set.Count * studentsN, abs);
        }
        double? Rate(long opp, int abs) => opp > 0 ? Math.Round((double)(opp - abs) / opp * 100) : null;

        long totalOpp = 0;
        var totalAbs = 0;
        var classPerformance = classes.Select(c =>
        {
            var (opp, abs) = ClassAttParts(c);
            totalOpp += opp;
            totalAbs += abs;
            return new ClassPerformanceItemDto(
                c.Id, c.Name, AvgGrade(entries.Where(e => e.ClassId == c.Id)), Rate(opp, abs));
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

        var stats = new AdminStatsDto(
            studentsCount, teachersCount, AvgGrade(entries), Rate(totalOpp, totalAbs),
            unassigned, classes.Count, archivedCount,
            creditCount, debtorCount, paidAtLeastOnce);

        var topClasses = classes
            .Select(c => new TopClassDto(
                c.Id, c.Name,
                students.Count(s => s.ClassName == c.Name),
                AvgGrade(entries.Where(e => e.ClassId == c.Id))))
            .OrderByDescending(t => t.AverageGrade)
            .Take(5)
            .ToList();

        return new AdminDashboardDto(
            stats, classPerformance, topClasses,
            AttendanceByPeriod(students, entries, lateReasonIds),
            await AbsentStudentsAsync(students, lateReasonIds));
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
    private static List<AttendanceByPeriodDto> AttendanceByPeriod(
        List<Student> students, List<JournalEntry> entries, HashSet<string> lateReasonIds)
    {
        // `JournalEntry.Date` — `yyyy-MM-dd` satri, shuning uchun solishtirish ham satrda.
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var todays = entries.Where(e => e.Date == today).ToList();
        var byClass = students.GroupBy(s => s.ClassName ?? "")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var rows = new List<AttendanceByPeriodDto>(10);
        for (var period = 1; period <= 10; period++)
        {
            var slot = todays.Where(e => e.Period == period).ToList();
            // Shu soatda darsi bo'lgan sinflar — jurnalda yozuvi borlaridan.
            var classIds = slot.Select(e => e.ClassId).Distinct().ToList();
            var expected = classIds.Count == 0
                ? 0
                : students.Count(s => slot.Any(e => e.StudentId == s.Id))
                  + 0; // pastda `unchecked` bilan to'ldiriladi
            var marked = slot.Select(e => e.StudentId).Distinct().Count();
            var absent = slot.Count(e => e.ReasonId != null && !lateReasonIds.Contains(e.ReasonId));
            var present = marked - absent;

            // Kutilgan son — shu sinflardagi barcha o'quvchilar, belgilanmaganlar ham.
            var totalInClasses = byClass
                .Where(kv => slot.Any(e => students.Any(s => s.Id == e.StudentId && s.ClassName == kv.Key)))
                .Sum(kv => kv.Value);
            expected = Math.Max(totalInClasses, marked);

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
