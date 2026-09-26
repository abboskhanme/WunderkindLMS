using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Weekly timetable (main templates). Section: <c>schedule</c>.</summary>
[McpServerToolType]
public sealed class TimetableTools(McpToolContext t)
{
    private static readonly string[] DayNames = ["Dushanba", "Seshanba", "Chorshanba", "Payshanba", "Juma", "Shanba", "Yakshanba"];

    [McpServerTool(Name = "timetable", Title = "Dars jadvali",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Haftalik dars jadvali: sinf (className), yo'nalish/o'quv guruhi (groupName) yoki o'qituvchi (teacher — ism bo'lagi) bo'yicha; "
        + "ixtiyoriy kun filtri (weekday: 1=Dushanba … 6=Shanba). Har bir dars: kun, soat (period), vaqt, fan, o'qituvchi, sinf/guruh, bo'linish. "
        + "Weekly timetable by class, group or teacher, optionally for one weekday.")]
    public async Task<string> Timetable(
        [Description("Sinf nomi, masalan 9-A")] string? className = null,
        [Description("Guruh nomi (yo'nalish yoki o'quv guruhi)")] string? groupName = null,
        [Description("O'qituvchi ismi (bo'lagi ham bo'ladi)")] string? teacher = null,
        [Description("Hafta kuni: 1=Dushanba … 6=Shanba (ixtiyoriy)")] int? weekday = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Schedule);
        if (string.IsNullOrWhiteSpace(className) && string.IsNullOrWhiteSpace(groupName) && string.IsNullOrWhiteSpace(teacher))
            throw new McpException("className, groupName yoki teacher dan kamida bittasi kerak.");
        if (weekday is < 1 or > 7) throw new McpException("weekday 1..7 oralig'ida bo'lsin.");

        var db = t.Db;
        var lessons = await TeacherLessons.MainLessonsAsync(db, ct);
        var teachers = await db.Teachers.ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var times = (await db.LessonTimes.ToListAsync(ct)).GroupBy(x => x.Period).ToDictionary(g => g.Key, g => g.First());

        IEnumerable<OwnedLesson> q = lessons;
        if (!string.IsNullOrWhiteSpace(className))
            q = q.Where(l => l.Owner.IsClass && string.Equals(l.Owner.Name, className.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(groupName))
            q = q.Where(l => l.Owner.IsGroup && l.Owner.Name.Contains(groupName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(teacher))
        {
            var needle = teacher.Trim();
            var ids = teachers.Where(p => p.Value.Contains(needle, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToHashSet();
            q = q.Where(l => ids.Contains(l.TeacherId));
        }
        if (weekday is { } wd) q = q.Where(l => l.Day == wd - 1);

        var rows = q.OrderBy(l => l.Day).ThenBy(l => l.Period).ThenBy(l => l.Owner.Name)
            .Select(l => new
            {
                weekday = l.Day + 1,
                day = l.Day is >= 0 and < 7 ? DayNames[l.Day] : l.Day.ToString(),
                period = l.Period,
                start = times.GetValueOrDefault(l.Period)?.StartTime,
                end = times.GetValueOrDefault(l.Period)?.EndTime,
                subject = subjects.GetValueOrDefault(l.SubjectId, l.SubjectId),
                teacher = teachers.GetValueOrDefault(l.TeacherId),
                owner = l.Owner.Name,
                ownerKind = l.Owner.IsTrack ? "track" : l.Owner.Kind,
                subGroup = l.SubGroup,
            }).Take(600).ToList();
        return t.Json(new { total = rows.Count, lessons = rows }, rows.Count);
    }
}
