using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Hubs;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'qituvchilar davomati — har o'qituvchining kunlik ish davomati (keldi / kelmadi / kechikdi).
/// Board: o'qituvchilar × kunlar, for a date range. Tied to the timetable since 2026-10-15
/// (docs/modules/teacher-attendance-range.md): a teacher is marked only on a day they have lessons.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("teachers")]
[Route("api/admin/teacher-attendance")]
public class TeacherAttendanceController(
    AppDbContext db, TurnstileService turnstile, IHubContext<LiveHub> live) : ControllerBase
{
    private static readonly HashSet<string> Valid = new() { "present", "absent", "late" };

    /// <summary>Kunlik dashboard: har o'qituvchi holati, kelgan/ketgan vaqti, kechikishi + jamlama.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<TeacherAttendanceDashboardDto>> Dashboard([FromQuery] string? date)
    {
        var d = string.IsNullOrEmpty(date) || date.Length < 10 ? AppClock.Today.ToString("yyyy-MM-dd") : date[..10];
        return await turnstile.BuildDashboardAsync(db, d);
    }

    /// <summary>Turniket qurilmasidan so'nggi hodisalarni tortib olib davomatni yangilash.</summary>
    [HttpPost("sync")]
    public async Task<ActionResult<TurnstileSyncResultDto>> Sync()
    {
        var res = await turnstile.SyncAsync(db);
        if (res.Ok && res.EventsFetched > 0)
            await live.Clients.Group(LiveHub.Group("turnstile"))
                .SendAsync("turnstileChanged", new { at = AppClock.Iso() });
        return res;
    }

    /// <summary>The longest range the board accepts — three 31-day months, so a whole quarter fits.</summary>
    public const int MaxRangeDays = 93;

    /// <summary>
    /// Board for a date range <c>?from=yyyy-MM-dd&amp;to=yyyy-MM-dd</c> (old clients: <c>?month=yyyy-MM</c>,
    /// no parameters — the current month). Tied to the timetable (<see cref="TeacherDaySchedule"/>):
    /// teachers with lessons in the range, plus anyone with an entry in it so nothing recorded
    /// disappears; per day Sunday / holiday / quarter flags; per teacher the days they teach.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TeacherAttendanceBoardDto>> Board(
        [FromQuery] string? month, [FromQuery] string? from, [FromQuery] string? to)
    {
        DateOnly start, end;
        if (!string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to))
        {
            if (!TryDate(from, out start) || !TryDate(to, out end))
                return BadRequest(new { message = "Davr boshi va oxiri sana bo'lishi kerak (yyyy-MM-dd)" });
            if (start > end)
                return BadRequest(new { message = "Davr boshi oxiridan keyin bo'lmasligi kerak" });
            if (end.DayNumber - start.DayNumber + 1 > MaxRangeDays)
                return BadRequest(new { message = $"Davr {MaxRangeDays} kundan oshmasligi kerak" });
        }
        else
        {
            var m = string.IsNullOrEmpty(month) || month.Length < 7 ? AppClock.Now.ToString("yyyy-MM") : month[..7];
            if (!TryDate(m + "-01", out start))
                return BadRequest(new { message = "Oy noto'g'ri (yyyy-MM)" });
            end = start.AddMonths(1).AddDays(-1);
        }
        var fromIso = Iso(start);
        var toIso = Iso(end);

        var schedule = await TeacherDaySchedule.LoadAsync(db, start, end);
        var entries = await db.TeacherAttendances
            .Where(a => string.Compare(a.Date, fromIso) >= 0 && string.Compare(a.Date, toIso) <= 0)
            .Select(a => new TeacherAttendanceDto(a.TeacherId, a.Date, a.Status, a.Note))
            .ToListAsync();

        var listed = schedule.TeacherIds.Concat(entries.Select(e => e.TeacherId)).Distinct().ToList();
        var teachers = (await db.Teachers.Where(t => !t.IsArchived && listed.Contains(t.Id))
                .OrderBy(t => t.FullName).ToListAsync())
            .Select(t => new TeacherNameDto(t.Id, t.FullName, TeacherSalaryCalc.StartDateOf(t) ?? ""))
            .ToList();
        var shown = teachers.Select(t => t.Id).ToHashSet();

        var days = new List<TeacherAttendanceDayDto>();
        var lessons = new List<TeacherLessonCountDto>();
        foreach (var day in schedule.Days)
        {
            var iso = Iso(day.Date);
            var on = schedule.TeachersOn(day.Date).Where(x => shown.Contains(x.Key)).ToList();
            days.Add(new TeacherAttendanceDayDto(
                iso, day.IsSunday, day.IsHoliday, day.HolidayName ?? "", day.InQuarter, on.Count));
            lessons.AddRange(on.Select(x => new TeacherLessonCountDto(x.Key, iso, x.Value)));
        }

        // Chorak (dars jadvali) davrlari — davomat faqat shu kunlarda belgilanadi.
        var quarters = (await TeacherSalaryCalc.QuarterRangesAsync(db))
            .Select(q => new DateRangeDto(q.Start, q.End)).ToList();

        return new TeacherAttendanceBoardDto(teachers, entries, quarters, fromIso, toIso, days, lessons);
    }

    /// <summary>Sana biror chorak (dars jadvali) ichidami (choraklar yo'q = cheklov yo'q).</summary>
    private async Task<bool> InTeachingPeriodAsync(string date) =>
        TeacherSalaryCalc.InQuarter(date, await TeacherSalaryCalc.QuarterRangesAsync(db));

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Bitta kun-katakni belgilash. Status bo'sh / noto'g'ri bo'lsa — belgini o'chiradi.</summary>
    [HttpPut]
    public async Task<IActionResult> Set(SetTeacherAttendanceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TeacherId) || string.IsNullOrEmpty(req.Date) || req.Date.Length < 10)
            return BadRequest(new { message = "O'qituvchi va sana ko'rsatilishi shart" });

        var teacher = await db.Teachers.FindAsync(req.TeacherId);
        if (teacher is null) return NotFound();
        // O'qituvchi ishga kirgan sanadan OLDINGI kunlarga davomat belgilab bo'lmaydi.
        var start = TeacherSalaryCalc.StartDateOf(teacher);
        if (start is not null && string.CompareOrdinal(req.Date, start) < 0)
            return BadRequest(new { message = "O'qituvchi bu sanadan keyin ishga kirgan — davomat belgilab bo'lmaydi" });
        // Dars jadvali (chorak) davridan tashqari kunlarga davomat belgilanmaydi — agar belgi qo'yilayotgan bo'lsa.
        if (Valid.Contains(req.Status ?? "") && !await InTeachingPeriodAsync(req.Date))
            return BadRequest(new { message = "Bu kun dars jadvali (chorak) davrida emas — davomat belgilab bo'lmaydi" });
        // Tied to the timetable: a mark only on a day the teacher has lessons. Clearing is always allowed,
        // so an entry left on such a day (old data, a holiday declared later) can still be removed.
        if (Valid.Contains(req.Status ?? ""))
        {
            if (!TryDate(req.Date, out var day))
                return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
            if (!(await TeacherDaySchedule.LoadAsync(db, day, day)).Teaches(req.TeacherId, day))
                return BadRequest(new { message = "Bu kuni ustozning darsi yo'q" });
        }

        var existing = await db.TeacherAttendances
            .FirstOrDefaultAsync(a => a.TeacherId == req.TeacherId && a.Date == req.Date);

        var status = req.Status ?? "";
        if (!Valid.Contains(status))
        {
            // Bo'sh / noto'g'ri — belgini o'chiramiz.
            if (existing is not null) db.TeacherAttendances.Remove(existing);
        }
        else if (existing is null)
        {
            db.TeacherAttendances.Add(new TeacherAttendance
            {
                TeacherId = req.TeacherId,
                Date = req.Date,
                Status = status,
                Note = req.Note ?? "",
                Source = "manual",
            });
        }
        else
        {
            existing.Status = status;
            existing.Note = req.Note ?? "";
            existing.Source = "manual";
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Marks one day for the active teachers who TEACH that day (the day header toggle): a status
    /// sets all of them, an empty/invalid one clears their marks for that day. Teachers without lessons
    /// that day — and those whose every lesson went to a substitute — are left alone both ways; they are
    /// marked one cell at a time.</summary>
    [HttpPut("day")]
    public async Task<IActionResult> SetDay(SetTeacherAttendanceDayRequest req)
    {
        if (string.IsNullOrEmpty(req.Date) || req.Date.Length < 10)
            return BadRequest(new { message = "Sana ko'rsatilishi shart" });
        if (!TryDate(req.Date, out var day))
            return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });

        var teaching = (await TeacherDaySchedule.LoadAsync(db, day, day)).TeachersOn(day)
            .Where(x => x.Value > 0).Select(x => x.Key).ToHashSet();
        var existing = (await db.TeacherAttendances.Where(a => a.Date == req.Date).ToListAsync())
            .Where(a => teaching.Contains(a.TeacherId)).ToList();
        var status = req.Status ?? "";

        if (!Valid.Contains(status))
        {
            db.TeacherAttendances.RemoveRange(existing);
        }
        else if (!await InTeachingPeriodAsync(req.Date))
        {
            return BadRequest(new { message = "Bu kun dars jadvali (chorak) davrida emas" });
        }
        else if (teaching.Count == 0)
        {
            return BadRequest(new { message = "Bu kuni darsi bor ustoz yo'q" });
        }
        else
        {
            var byTeacher = existing.ToDictionary(a => a.TeacherId);
            var teachers = await db.Teachers.Where(t => !t.IsArchived && teaching.Contains(t.Id)).ToListAsync();
            foreach (var te in teachers)
            {
                // Ishga kirgan sanadan oldingi kun bo'lsa — o'tkazib yuboramiz.
                var start = TeacherSalaryCalc.StartDateOf(te);
                if (start is not null && string.CompareOrdinal(req.Date, start) < 0) continue;
                if (byTeacher.TryGetValue(te.Id, out var a)) a.Status = status;
                else db.TeacherAttendances.Add(new TeacherAttendance { TeacherId = te.Id, Date = req.Date, Status = status });
            }
        }

        await db.SaveChangesAsync();
        return NoContent();
    }
}
