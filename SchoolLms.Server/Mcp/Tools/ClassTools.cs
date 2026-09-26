using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Classes and track/study groups. Section: <c>classes</c>.</summary>
[McpServerToolType]
public sealed class ClassTools(McpToolContext t)
{
    [McpServerTool(Name = "classes_list", Title = "Sinflar ro'yxati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Barcha faol sinflar: nomi, daraja (sinf raqami), til, xona, sig'im, o'quvchilar soni (o'g'il/qiz), sinf rahbari. "
        + "List of classes with pupil counts and homeroom teacher.")]
    public async Task<string> ClassesList(
        [Description("Arxivlangan sinflar ham kerakmi (sukut false)")] bool includeArchived = false,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Classes);
        var db = t.Db;
        var classes = await db.Classes.Where(c => includeArchived || !c.IsArchived)
            .OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync(ct);
        var counts = await db.Students.Where(s => !s.IsArchived)
            .GroupBy(s => new { s.ClassName, s.Gender })
            .Select(g => new { g.Key.ClassName, g.Key.Gender, Count = g.Count() }).ToListAsync(ct);
        var homeroom = await db.Teachers.Where(x => !x.IsArchived && x.HomeroomClass != "")
            .Select(x => new { x.HomeroomClass, x.FullName }).ToListAsync(ct);

        var rows = classes.Select(c => new
        {
            c.Id, c.Name, c.Grade, c.Language, c.Room, c.Capacity, c.IsArchived,
            pupils = counts.Where(x => x.ClassName == c.Name).Sum(x => x.Count),
            girls = counts.Where(x => x.ClassName == c.Name && x.Gender == "female").Sum(x => x.Count),
            homeroomTeacher = homeroom.FirstOrDefault(h => h.HomeroomClass == c.Name)?.FullName,
        }).ToList();
        return t.Json(new { total = rows.Count, classes = rows }, rows.Count);
    }

    [McpServerTool(Name = "track_groups_list", Title = "Yo'nalish va o'quv guruhlari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Yo'nalish guruhlari (track) va fan bo'yicha o'quv guruhlari: nomi, fani, o'qituvchilari, a'zolar soni. "
        + "Track groups and study groups with member counts and teachers.")]
    public async Task<string> TrackGroupsList(
        [Description("Faqat yo'nalish guruhlari (sukut false — hammasi)")] bool tracksOnly = false,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Groups);
        var db = t.Db;
        var groups = await db.StudyGroups.Where(g => !g.IsArchived && (!tracksOnly || g.IsTrack))
            .OrderByDescending(g => g.IsTrack).ThenBy(g => g.Name).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var members = await db.StudyGroupMembers.Where(m => ids.Contains(m.GroupId) && m.LeftOn == null)
            .GroupBy(m => m.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var teacherRows = await (from gt in db.StudyGroupTeachers
                                 join te in db.Teachers on gt.TeacherId equals te.Id
                                 where ids.Contains(gt.GroupId)
                                 select new { gt.GroupId, te.FullName }).ToListAsync(ct);
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var rows = groups.Select(g => new
        {
            g.Id, g.Name, g.IsTrack,
            subject = g.SubjectId is null ? null : subjects.GetValueOrDefault(g.SubjectId, g.SubjectId),
            g.Gender,
            members = members.FirstOrDefault(m => m.Key == g.Id)?.Count ?? 0,
            teachers = teacherRows.Where(x => x.GroupId == g.Id).Select(x => x.FullName).Distinct().ToList(),
        }).ToList();
        return t.Json(new { total = rows.Count, groups = rows }, rows.Count);
    }

    [McpServerTool(Name = "class_roster", Title = "Sinf yoki guruh tarkibi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta sinf (className) yoki guruh (groupId) o'quvchilari ro'yxati: id, ism, jinsi, tug'ilgan sana, telefon, ota-ona. "
        + "Roster of one class or one track/study group.")]
    public async Task<string> ClassRoster(
        [Description("Sinf nomi, masalan 7-B")] string? className = null,
        [Description("Yoki guruh id (track_groups_list dan)")] string? groupId = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Classes);
        var db = t.Db;
        if (!string.IsNullOrWhiteSpace(groupId))
        {
            var gid = StudentTools.ParseGuid(groupId, "groupId")!.Value;
            var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == gid, ct)
                ?? throw new McpException("Guruh topilmadi.");
            var rows = await (from m in db.StudyGroupMembers
                              join s in db.Students on m.StudentId equals s.Id
                              where m.GroupId == gid && m.LeftOn == null
                              orderby s.FullName
                              select new { s.Id, s.FullName, s.ClassName, s.Gender, s.BirthDate, s.Phone, s.ParentFullName, s.ParentPhone, m.JoinedOn })
                .ToListAsync(ct);
            return t.Json(new { group = group.Name, group.IsTrack, total = rows.Count, pupils = rows }, rows.Count);
        }
        if (string.IsNullOrWhiteSpace(className)) throw new McpException("className yoki groupId kerak.");
        var name = className.Trim();
        var list = await db.Students.Where(s => s.ClassName == name && !s.IsArchived).OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName, s.Gender, s.BirthDate, s.Phone, s.ParentFullName, s.ParentPhone, s.EnrollmentDate })
            .ToListAsync(ct);
        return t.Json(new { className = name, total = list.Count, pupils = list }, list.Count);
    }

    [McpServerTool(Name = "class_performance", Title = "Sinflar o'zlashtirishi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'zlashtirish va davomat: className berilsa — shu sinf o'quvchilari reytingi (o'rtacha baho, davomat %); "
        + "berilmasa — har bir sinf bo'yicha o'rtacha baho, o'rtacha davomat va eng yaxshi/eng past o'quvchilar. "
        + "Class performance: average grade and attendance per class or per pupil.")]
    public async Task<string> ClassPerformance(
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.ClassPerformance);
        // Whole-school rating reads every journal entry — cached 5 min (not per user; the
        // permission check above already ran), so an AI loop cannot re-scan the journal per call.
        var rows = await t.CachedAsync("rating", TimeSpan.FromMinutes(5), () => RatingService.SchoolAsync(t.Db));
        if (!string.IsNullOrWhiteSpace(className))
        {
            var name = className.Trim();
            var pupils = rows.Where(r => r.ClassName == name).OrderByDescending(r => r.Average)
                .Select((r, i) => new
                {
                    rank = i + 1, id = r.Student.Id, name = r.Student.FullName,
                    average = Math.Round(r.Average, 2), attendancePercent = r.Attendance is { } a ? Math.Round(a, 1) : (double?)null,
                }).ToList();
            return t.Json(new { className = name, pupils }, pupils.Count);
        }
        var byClass = rows.GroupBy(r => new { r.ClassName, r.Grade }).OrderBy(g => g.Key.Grade).ThenBy(g => g.Key.ClassName)
            .Select(g => new
            {
                className = g.Key.ClassName, grade = g.Key.Grade, pupils = g.Count(),
                averageGrade = Math.Round(g.Where(r => r.Average > 0).Select(r => r.Average).DefaultIfEmpty().Average(), 2),
                averageAttendance = Math.Round(g.Where(r => r.Attendance.HasValue).Select(r => r.Attendance!.Value).DefaultIfEmpty().Average(), 1),
                top = g.OrderByDescending(r => r.Average).Take(3).Select(r => new { r.Student.FullName, average = Math.Round(r.Average, 2) }),
                bottom = g.Where(r => r.Average > 0).OrderBy(r => r.Average).Take(3).Select(r => new { r.Student.FullName, average = Math.Round(r.Average, 2) }),
            }).ToList();
        return t.Json(new { classes = byClass }, byClass.Count);
    }
}
