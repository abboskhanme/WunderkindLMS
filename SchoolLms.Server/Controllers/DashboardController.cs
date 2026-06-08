using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;

using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/dashboard")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminDashboardDto>> Get()
    {
        var studentsCount = await db.Students.CountAsync();
        var teachersCount = await db.Teachers.CountAsync();
        var classes = await db.Classes.OrderBy(c => c.Grade).ToListAsync();
        var students = await db.Students.ToListAsync();
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

        var stats = new AdminStatsDto(studentsCount, teachersCount, AvgGrade(entries), Rate(totalOpp, totalAbs));

        var topClasses = classes
            .Select(c => new TopClassDto(
                c.Id, c.Name,
                students.Count(s => s.ClassName == c.Name),
                AvgGrade(entries.Where(e => e.ClassId == c.Id))))
            .OrderByDescending(t => t.AverageGrade)
            .Take(5)
            .ToList();

        return new AdminDashboardDto(stats, classPerformance, topClasses);
    }
}
