using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>School-wide snapshot. Section: <c>dashboard</c>.</summary>
[McpServerToolType]
public sealed class OverviewTools(McpToolContext t)
{
    [McpServerTool(Name = "school_overview", Title = "Maktab bo'yicha umumiy ko'rsatkichlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Maktabning umumiy holati: faol o'quvchilar, sinflar, yo'nalish guruhlari, o'qituvchilar va xodimlar soni, "
        + "hamda tanlangan kun (sukut — bugun) davomati: nechta dars belgilangan, nechta o'quvchi kelmagan/kechikkan. "
        + "School overview: counts of pupils, classes, tracks, teachers, staff and the day's attendance.")]
    public async Task<string> SchoolOverview(
        [Description("Sana YYYY-MM-DD (ixtiyoriy, sukut — bugun)")] string? date = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Dashboard);
        var db = t.Db;
        var day = McpToolContext.Date(date, McpToolContext.Today, "date");

        var pupils = await db.Students.CountAsync(s => !s.IsArchived, ct);
        var girls = await db.Students.CountAsync(s => !s.IsArchived && s.Gender == "female", ct);
        var classes = await db.Classes.CountAsync(c => !c.IsArchived, ct);
        var tracks = await db.StudyGroups.CountAsync(g => g.IsTrack && !g.IsArchived, ct);
        var groups = await db.StudyGroups.CountAsync(g => !g.IsTrack && !g.IsArchived, ct);
        var teachers = await db.Teachers.CountAsync(x => !x.IsArchived, ct);
        var staff = await db.Users.CountAsync(u => u.Role == Roles.Staff, ct);
        var att = await new DailyAttendanceService(db).OverviewAsync(day.ToString("yyyy-MM-dd"), ct);

        return t.Json(new
        {
            date = day.ToString("yyyy-MM-dd"),
            pupils = new { active = pupils, girls, boys = pupils - girls },
            classes,
            trackGroups = tracks,
            studyGroups = groups,
            teachers,
            staff,
            attendance = new
            {
                lessonsScheduled = att.TotalLessons,
                lessonsMarked = att.MarkedLessons,
                classesMarked = att.MarkedClasses,
                classesTotal = att.TotalClasses,
                absent = att.AbsentTotal,
                late = att.LateTotal,
            },
        }, 1);
    }
}
