using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// "Dars jadvali → Oylik hisoblash": har TOIFA uchun bir soat dars narxi (admin kiritadi) va
/// shu narx + dars jadvalidan har o'qituvchining oylik maoshi avtomatik hisoblanadi.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/salary-rates")]
public class SalaryRatesController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SalaryRatesDto>> Get([FromQuery] string? month)
    {
        var m = string.IsNullOrEmpty(month) || month.Length < 7 ? AppClock.Now.ToString("yyyy-MM") : month[..7];

        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var byWeekdayAll = await TeacherSalaryCalc.LessonsByWeekdayAsync(db);
        var quarters = await TeacherSalaryCalc.QuarterRangesAsync(db);
        var teachers = await db.Teachers.Where(t => !t.IsArchived)
            .OrderBy(t => t.FullName).ToListAsync();

        // Tanlangan oydagi kelmagan (absent) kunlar — o'qituvchi bo'yicha.
        var absentByTeacher = (await db.TeacherAttendances
                .Where(a => a.Status == "absent" && a.Date.StartsWith(m))
                .Select(a => new { a.TeacherId, a.Date }).ToListAsync())
            .GroupBy(a => a.TeacherId)
            .ToDictionary(g => g.Key, g => (IEnumerable<string>)g.Select(x => x.Date).ToList());

        var rows = teachers.Select(t =>
        {
            var byWeekday = byWeekdayAll.GetValueOrDefault(t.Id) ?? new int[6];
            var weekly = byWeekday.Sum();
            var startDate = TeacherSalaryCalc.StartDateOf(t);
            // Tanlangan oydagi reja darslar (chorak davri + ishga kirgan oyda qisman; kelishidan oldin 0).
            var planned = TeacherSalaryCalc.PlannedLessonsForMonth(byWeekday, m, startDate, quarters);
            var absentRaw = absentByTeacher.GetValueOrDefault(t.Id) ?? Enumerable.Empty<string>();
            var absent = startDate is { Length: >= 10 } && startDate[..7] == m
                ? absentRaw.Where(d => string.CompareOrdinal(d, startDate) >= 0).ToList()
                : absentRaw.ToList();
            var missed = TeacherSalaryCalc.MissedLessons(byWeekday, absent, quarters);
            var salary = TeacherSalaryCalc.MonthlyForMonth(byWeekday, t.Category, meta, m, startDate, absent, t.BonusPct, quarters);
            return new TeacherPayrollRowDto(
                t.Id, t.FullName, t.Category, weekly, planned, missed, t.BonusPct, salary);
        }).ToList();

        return new SalaryRatesDto(
            meta?.SalaryRateOliy ?? 0, meta?.SalaryRate1 ?? 0, meta?.SalaryRate2 ?? 0,
            meta?.SalaryRateMutaxasis ?? 0, TeacherSalaryCalc.WeeksPerMonth, m, rows);
    }

    /// <summary>Bitta o'qituvchining tanlangan oydagi maosh tafsiloti: reja, kelmagan kunlar (qachon),
    /// chegirma, jami (net), berilgan va qoldiq.</summary>
    [HttpGet("{teacherId}")]
    public async Task<ActionResult<TeacherSalaryDetailDto>> Detail(string teacherId, [FromQuery] string? month)
    {
        var t = await db.Teachers.FindAsync(teacherId);
        if (t is null) return NotFound();

        var m = string.IsNullOrEmpty(month) || month.Length < 7 ? AppClock.Now.ToString("yyyy-MM") : month[..7];
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var byWeekday = (await TeacherSalaryCalc.LessonsByWeekdayAsync(db)).GetValueOrDefault(t.Id) ?? new int[6];
        var quarters = await TeacherSalaryCalc.QuarterRangesAsync(db);
        var rate = TeacherSalaryCalc.RateFor(meta, t.Category);
        var weekly = byWeekday.Sum();
        var startDate = TeacherSalaryCalc.StartDateOf(t);
        // Reja qisman (chorak davri + ishga kirgan oyda kelgan kunidan oy oxirigacha).
        var monthlyLessons = TeacherSalaryCalc.PlannedLessonsForMonth(byWeekday, m, startDate, quarters);
        var partialMonth = startDate is { Length: >= 10 } && startDate[..7] == m;

        var absent = await db.TeacherAttendances
            .Where(a => a.TeacherId == t.Id && a.Status == "absent" && a.Date.StartsWith(m))
            .OrderBy(a => a.Date)
            .Select(a => new { a.Date, a.Note }).ToListAsync();
        // Ishga kirgan oyda — faqat kelgan kunidan keyingi yo'qliklar.
        if (partialMonth) absent = absent.Where(a => string.CompareOrdinal(a.Date, startDate!) >= 0).ToList();

        var absentDays = absent.Select(a =>
        {
            var lessons = 0;
            if (DateOnly.TryParse(a.Date, out var d) && TeacherSalaryCalc.InQuarter(a.Date, quarters))
            {
                var wd = ((int)d.DayOfWeek + 6) % 7; // Dushanba=0..Shanba=5, Yakshanba=6
                if (wd < 6) lessons = byWeekday[wd];
            }
            return new AbsentDayDto(a.Date, lessons, a.Note);
        }).ToList();

        var missed = absentDays.Sum(x => x.Lessons);
        var net = Math.Max(0, monthlyLessons - missed);

        var paid = await db.FinanceTransactions
            .Where(x => x.TeacherId == t.Id && x.Direction == "expense" && x.Category == "salary"
                        && x.Date.StartsWith(m))
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        var baseSalary = net * rate;                              // davomatga moslangan, ustamasiz
        var bonusAmount = baseSalary * t.BonusPct / 100m;         // ustama summasi
        var netSalary = baseSalary + bonusAmount;                 // jami (ustama bilan)
        return new TeacherSalaryDetailDto(
            t.Id, t.FullName, t.Category, m, startDate ?? "", partialMonth, rate, weekly, monthlyLessons,
            monthlyLessons * rate, missed, missed * rate,
            baseSalary, t.BonusPct, bonusAmount, netSalary,
            paid, netSalary - paid, absentDays);
    }

    /// <summary>O'qituvchining ustama foizini belgilash (0 = ustama yo'q). Oylik maoshga shu foiz qo'shiladi.</summary>
    [HttpPut("{teacherId}/bonus")]
    public async Task<IActionResult> SetBonus(string teacherId, SetTeacherBonusRequest req)
    {
        var t = await db.Teachers.FindAsync(teacherId);
        if (t is null) return NotFound();
        if (req.BonusPct < 0 || req.BonusPct > 1000)
            return BadRequest(new { message = "Ustama foizi 0–1000 oralig'ida bo'lsin" });
        t.BonusPct = req.BonusPct;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bir nechta tanlangan o'qituvchiga bir vaqtda ustama foizini tayinlash (0 = olib tashlash).</summary>
    [HttpPut("bonus")]
    public async Task<IActionResult> SetBonusBulk(SetBonusBulkRequest req)
    {
        if (req.BonusPct < 0 || req.BonusPct > 1000)
            return BadRequest(new { message = "Ustama foizi 0–1000 oralig'ida bo'lsin" });
        var ids = req.TeacherIds ?? new();
        if (ids.Count == 0) return BadRequest(new { message = "O'qituvchi tanlanmagan" });
        var teachers = await db.Teachers.Where(t => ids.Contains(t.Id)).ToListAsync();
        foreach (var t in teachers) t.BonusPct = req.BonusPct;
        await db.SaveChangesAsync();
        return Ok(new { updated = teachers.Count });
    }

    [HttpPut]
    public async Task<IActionResult> Save(SalaryRatesRequest req)
    {
        if (req.Oliy < 0 || req.T1 < 0 || req.T2 < 0 || req.Mutaxasis < 0)
            return BadRequest(new { message = "Narx manfiy bo'lishi mumkin emas" });

        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        if (meta is null)
        {
            meta = new SchoolMeta();
            db.SchoolMeta.Add(meta);
        }
        meta.SalaryRateOliy = req.Oliy;
        meta.SalaryRate1 = req.T1;
        meta.SalaryRate2 = req.T2;
        meta.SalaryRateMutaxasis = req.Mutaxasis;

        audit.Record(AuditService.EntityTeacherSalary, meta.Id, "update",
            "Toifa soat narxlari yangilandi: " +
            $"Oliy {AuditService.Money(req.Oliy)}, 1-toifa {AuditService.Money(req.T1)}, " +
            $"2-toifa {AuditService.Money(req.T2)}, Mutaxasis {AuditService.Money(req.Mutaxasis)} so'm/soat");

        await db.SaveChangesAsync();
        return NoContent();
    }
}
