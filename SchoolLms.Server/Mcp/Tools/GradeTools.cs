using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Journal marks, seasonal marks, block tests. Sections: <c>journal</c>, <c>seasonalMarks</c>, <c>exams</c>.</summary>
[McpServerToolType]
public sealed class GradeTools(McpToolContext t)
{
    [McpServerTool(Name = "journal_marks", Title = "Jurnal baholari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sinf (yoki yo'nalish/o'quv guruhi) jurnali: fan va chorak bo'yicha har bir o'quvchining kunlik baholari, "
        + "davomat belgilari, o'rtacha va chorak bahosi. Journal marks for a class/group, subject and quarter.")]
    public async Task<string> JournalMarks(
        [Description("Sinf yoki guruh nomi, masalan 6-A")] string className,
        [Description("Fan nomi, masalan Matematika")] string subject,
        [Description("Chorak 1..4")] int quarter,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Journal);
        if (quarter is < 1 or > 4) throw new McpException("quarter 1..4 bo'lsin.");
        var db = t.Db;
        var name = (className ?? "").Trim();
        var ownerId = await db.Classes.Where(c => c.Name == name).Select(c => c.Id).FirstOrDefaultAsync(ct);
        if (ownerId is null)
        {
            var g = await db.StudyGroups.Where(x => x.Name == name).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            ownerId = g?.ToString() ?? throw new McpException($"«{name}» sinf/guruh topilmadi.");
        }
        var subj = await db.Subjects.Where(s => s.Name.ToLower() == (subject ?? "").Trim().ToLower())
            .Select(s => new { s.Id, s.Name }).FirstOrDefaultAsync(ct)
            ?? throw new McpException($"«{subject}» fani topilmadi.");

        var entries = await JournalService.GetEntriesAsync(db, ownerId, subj.Id, quarter);
        var quarterGrades = (await JournalService.GetQuarterGradesAsync(db, ownerId, subj.Id, quarter))
            .ToDictionary(q => q.StudentId);
        var ids = entries.Select(e => e.StudentId).Union(quarterGrades.Keys).Distinct().ToList();
        var names = await db.Students.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var pupils = ids.Select(id =>
        {
            var mine = entries.Where(e => e.StudentId == id).OrderBy(e => e.Date).ThenBy(e => e.Period).ToList();
            var marks = mine.Where(e => e.Grade != null).ToList();
            return new
            {
                pupil = names.GetValueOrDefault(id, id),
                average = marks.Count == 0 ? (double?)null : Math.Round(marks.Average(e => e.Grade!.Value), 2),
                quarterGrade = quarterGrades.GetValueOrDefault(id)?.Grade,
                marks = marks.Select(e => new { e.Date, grade = e.Grade }),
                absences = mine.Where(e => e.ReasonId != null)
                    .Select(e => new { e.Date, reason = reasons.GetValueOrDefault(e.ReasonId!, e.ReasonId!) }),
            };
        }).OrderBy(p => p.pupil).ToList();
        return t.Json(new { className = name, subject = subj.Name, quarter, pupils }, pupils.Count);
    }

    [McpServerTool(Name = "seasonal_marks", Title = "Mavsumiy baholar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Mavsumiy (oylik/choraklik/yillik) baholash natijalari: o'quvchi, sinf, fan, davr, ball, izoh. "
        + "Filtrlar: yil, oy, chorak, sinf, fan, ism. Sahifalangan. Seasonal assessment marks.")]
    public async Task<string> SeasonalMarks(
        [Description("Yil, masalan 2026")] int? year = null,
        [Description("Oy 1..12")] int? month = null,
        [Description("Chorak 1..4")] int? quarter = null,
        [Description("Sinf nomi")] string? className = null,
        [Description("Fan nomi")] string? subject = null,
        [Description("O'quvchi ismi bo'lagi")] string? search = null,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.SeasonalMarks);
        var db = t.Db;
        string? classId = null, subjectId = null;
        if (!string.IsNullOrWhiteSpace(className))
            classId = await db.Classes.Where(c => c.Name == className.Trim()).Select(c => c.Id).FirstOrDefaultAsync(ct)
                      ?? throw new McpException("Sinf topilmadi.");
        if (!string.IsNullOrWhiteSpace(subject))
            subjectId = await db.Subjects.Where(s => s.Name.ToLower() == subject.Trim().ToLower()).Select(s => s.Id).FirstOrDefaultAsync(ct)
                        ?? throw new McpException("Fan topilmadi.");
        // AuditService is a constructor dependency used only by the write methods; the page query never calls it.
        var svc = new SeasonalMarkService(db, new AuditService(db, new HttpContextAccessor()));
        var res = await svc.PageAsync(new SeasonalMarkFilter
        {
            Year = year, Month = month, Quarter = quarter, ClassId = classId, SubjectId = subjectId, Search = search,
            Page = McpToolContext.Page(page), Limit = McpToolContext.PageSize(pageSize),
        }, ct);
        var items = res.Items.Select(r => new
        {
            pupil = r.Student.FullName, className = r.Class.Name, subject = r.Subject.Name,
            period = r.PeriodLabel, r.Score, r.Comment, r.UpdatedAt,
        }).ToList();
        return t.Json(new { total = res.Total, page = res.Page, pageSize = res.Limit, items }, items.Count);
    }

    [McpServerTool(Name = "block_test_results", Title = "Blok test natijalari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Blok testlar: examId berilmasa — oxirgi testlar ro'yxati (sana, sinf darajasi, qatnashchilar, o'rtacha ball); "
        + "examId berilsa — shu test bo'yicha har bir o'quvchi natijasi va fanlar kesimi. Block test results.")]
    public async Task<string> BlockTestResults(
        [Description("Test id (ro'yxatdan)")] string? examId = null,
        [Description("Nechta test (ro'yxat rejimi, 1..100, sukut 20)")] int? limit = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Exams);
        var db = t.Db;
        if (string.IsNullOrWhiteSpace(examId))
        {
            var take = Math.Clamp(limit ?? 20, 1, 100);
            var exams = await db.Exams.Where(e => e.Kind == ExamKind.Block)
                .OrderByDescending(e => e.ExamDate).ThenByDescending(e => e.Id).Take(take)
                .Select(e => new { e.Id, e.Title, e.ExamDate, e.Grade }).ToListAsync(ct);
            var ids = exams.Select(e => e.Id).ToList();
            var stats = await db.ExamParticipants.Where(p => ids.Contains(p.ExamId))
                .GroupBy(p => p.ExamId)
                .Select(g => new
                {
                    g.Key,
                    Participants = g.Count(),
                    Finished = g.Count(p => p.Status == ExamParticipantStatus.Finished),
                    Avg = g.Average(p => p.TotalPoints),
                }).ToListAsync(ct);
            var rows = exams.Select(e =>
            {
                var s = stats.FirstOrDefault(x => x.Key == e.Id);
                return new
                {
                    e.Id, e.Title, date = e.ExamDate, grade = e.Grade, participants = s?.Participants ?? 0,
                    finished = s?.Finished ?? 0, averagePoints = s?.Avg is { } a ? Math.Round(a, 2) : (decimal?)null,
                };
            }).ToList();
            return t.Json(new { exams = rows }, rows.Count);
        }

        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId, ct) ?? throw new McpException("Test topilmadi.");
        if (exam.Kind != ExamKind.Block) t.Require(McpAreas.Admission); // entrance tests belong to Qabul
        var sections = await db.ExamSections.Where(s => s.ExamId == exam.Id).OrderBy(s => s.Order).ToListAsync(ct);
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var participants = await db.ExamParticipants.Where(p => p.ExamId == exam.Id).ToListAsync(ct);
        var pids = participants.Select(p => p.Id).ToList();
        var scores = await db.ExamSectionScores.Where(s => pids.Contains(s.ParticipantId)).ToListAsync(ct);
        var sids = participants.Where(p => p.StudentId != null).Select(p => p.StudentId!).ToList();
        var names = await db.Students.Where(s => sids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => new { s.FullName, s.ClassName }, ct);

        var results = participants.OrderByDescending(p => p.TotalPoints).Take(McpToolContext.MaxPageSize).Select(p => new
        {
            pupil = p.StudentId is { } sid && names.TryGetValue(sid, out var n) ? n.FullName : "—",
            className = p.StudentId is { } sid2 && names.TryGetValue(sid2, out var n2) ? n2.ClassName : null,
            p.Status, p.TotalPoints, p.CorrectCount, p.QuestionCount,
            bySubject = sections.Select(s => new
            {
                subject = subjects.GetValueOrDefault(s.SubjectId, s.SubjectId),
                points = scores.FirstOrDefault(x => x.ParticipantId == p.Id && x.SectionId == s.Id)?.Points,
                max = s.MaxScore,
            }),
        }).ToList();
        return t.Json(new { exam = new { exam.Id, exam.Title, exam.ExamDate, exam.Grade }, participants = participants.Count, results }, results.Count);
    }
}
