using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Attendance. Sections: <c>attendance</c>; boarding — <c>attendanceEvening</c>/<c>attendanceDorm</c>.</summary>
[McpServerToolType]
public sealed class AttendanceTools(McpToolContext t)
{
    [McpServerTool(Name = "attendance_daily", Title = "Kunlik davomat",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bir kunlik davomat: har bir sinf/yo'nalish guruhi bo'yicha o'quvchilar soni, darslar soni, belgilangan darslar, "
        + "kelmaganlar va kechikkanlar; umumiy yakun. Daily attendance overview per class for a date (default today).")]
    public async Task<string> AttendanceDaily(
        [Description("Sana YYYY-MM-DD (sukut — bugun)")] string? date = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.AttendanceDaily);
        var day = McpToolContext.Date(date, McpToolContext.Today, "date");
        var o = await new DailyAttendanceService(t.Db).OverviewAsync(day.ToString("yyyy-MM-dd"), ct);
        var classes = o.Classes.Select(c => new
        {
            c.ClassId, c.ClassName, kind = c.IsTrack ? "track" : c.OwnerKind, pupils = c.StudentCount,
            lessons = c.LessonCount, markedLessons = c.MarkedLessons, absent = c.AbsentCount, late = c.LateCount,
        }).ToList();
        return t.Json(new
        {
            o.Date, totalLessons = o.TotalLessons, markedLessons = o.MarkedLessons,
            markedClasses = o.MarkedClasses, totalClasses = o.TotalClasses, absent = o.AbsentTotal, late = o.LateTotal,
            classes,
        }, classes.Count);
    }

    [McpServerTool(Name = "attendance_class_day", Title = "Sinfning bir kunlik davomati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta sinf (yoki yo'nalish guruhi) ning bir kuni: har bir dars soati, kim belgilagani va qaysi o'quvchi "
        + "qaysi sabab bilan kelmagan/kechikkan. One class's lessons on a date with absent/late pupils per lesson.")]
    public async Task<string> AttendanceClassDay(
        [Description("Sinf nomi (masalan 5-A) yoki yo'nalish guruhi nomi")] string className,
        [Description("Sana YYYY-MM-DD (sukut — bugun)")] string? date = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.AttendanceDaily);
        var db = t.Db;
        var day = McpToolContext.Date(date, McpToolContext.Today, "date");
        var name = (className ?? "").Trim();
        var ownerId = await db.Classes.Where(c => c.Name == name).Select(c => c.Id).FirstOrDefaultAsync(ct)
            ?? (await db.StudyGroups.Where(g => g.IsTrack && g.Name == name).Select(g => g.Id).FirstOrDefaultAsync(ct)) switch
            {
                var g when g != Guid.Empty => g.ToString(),
                _ => null,
            };
        if (ownerId is null) throw new McpException($"«{name}» nomli sinf yoki yo'nalish guruhi topilmadi.");

        var d = await new DailyAttendanceService(db).ClassDayAsync(ownerId, day.ToString("yyyy-MM-dd"), ct);
        if (d is null) return t.Json(new { message = "Ma'lumot topilmadi." }, 0);
        var names = d.Students.ToDictionary(s => s.StudentId, s => s.FullName);
        var reasons = d.Reasons.ToDictionary(r => r.Id, r => r.Name);
        var lessons = d.Lessons.Select(l => new
        {
            l.Period, subject = l.SubjectName, l.StartTime, l.EndTime, l.SubGroup, l.Marked, markedBy = l.MarkedByName,
            absent = l.AbsentCount, late = l.LateCount,
            marks = l.Marks.Select(m => new
            {
                pupil = names.GetValueOrDefault(m.Key, m.Key),
                reason = reasons.GetValueOrDefault(m.Value, m.Value),
            }),
        }).ToList();
        return t.Json(new { d.ClassName, d.Date, pupils = d.Students.Count, lessons }, lessons.Count);
    }

    [McpServerTool(Name = "attendance_analytics", Title = "Davomat tahlili (davr)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Davr bo'yicha davomat tahlili (ko'pi bilan 92 kun): jami, sababli/sababsiz/kechikish, sinflar kesimi, kunlik trend, "
        + "sabablar taqsimoti. Attendance analytics for a date range: excused/unexcused/late, per class, daily trend, reasons.")]
    public async Task<string> AttendanceAnalyticsTool(
        [Description("Davr boshi YYYY-MM-DD (sukut — 30 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("Bitta sinf nomi (ixtiyoriy)")] string? className = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.AttendanceAnalytics);
        var (f, tt) = McpToolContext.Range(from, to, 30, 92);
        string? classId = null;
        if (!string.IsNullOrWhiteSpace(className))
            classId = await t.Db.Classes.Where(c => c.Name == className.Trim()).Select(c => c.Id).FirstOrDefaultAsync(ct)
                      ?? throw new McpException("Sinf topilmadi.");
        var a = await AttendanceAnalytics.BuildAsync(t.Db, classId, f.ToString("yyyy-MM-dd"), tt.ToString("yyyy-MM-dd"), null);
        return t.Json(new
        {
            a.From, a.To, pupils = a.StudentsTotal, total = a.Total,
            classes = a.Classes.Select(c => new { c.ClassName, c.Grade, c.Students, kind = c.OwnerKind, tally = c.Tally }),
            reasons = a.Reasons.Select(r => new { r.Name, r.IsLate, r.Unexcused, r.Count }),
            trend = a.Trend.Select(p => new { p.Date, p.Tally.Present, p.Tally.Absent, p.Tally.Excused, p.Tally.Unexcused, p.Tally.Late, p.Tally.PresentPct }),
        }, a.Classes.Count);
    }

    [McpServerTool(Name = "boarding_attendance", Title = "Kechki dars va yotoqxona davomati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kechki dars (session=evening) yoki yotoqxona (session=dorm) davomati bir kun uchun: bo'limlar, "
        + "yotoqxona abonementi borlar, belgilanganlar, yo'qlar va har bir o'quvchi holati. Evening study / dormitory check for a date.")]
    public async Task<string> BoardingAttendance(
        [Description("evening | dorm")] string session = "evening",
        [Description("Sana YYYY-MM-DD (sukut — bugun)")] string? date = null,
        CancellationToken ct = default)
    {
        if (!BoardingSession.All.Contains(session)) throw new McpException("session: evening yoki dorm.");
        // Only the session's own grant — daytime attendance does NOT open evening/dorm data.
        t.Require(session == BoardingSession.Dorm ? McpAreas.BoardingDorm : McpAreas.BoardingEvening);
        var day = McpToolContext.Date(date, McpToolContext.Today, "date");
        // Only the read path (DayAsync) is used — it touches neither Telegram nor the logger,
        // so no messaging dependency is handed to a read-only tool.
        var svc = new BoardingAttendanceService(t.Db, null!, NullLogger<BoardingAttendanceService>.Instance);
        var d = await svc.DayAsync(day, session, ct);
        var sections = d.Sections.Select(s => new
        {
            s.Title, s.Kind, s.Eligible, s.Marked, s.Absent,
            pupils = s.Students.Where(p => p.Eligible).Select(p => new { p.FullName, p.ClassName, status = p.Status ?? "belgilanmagan" }),
        }).ToList();
        return t.Json(new { d.Date, d.Session, d.Eligible, d.Marked, d.Absent, sections }, sections.Count);
    }
}
