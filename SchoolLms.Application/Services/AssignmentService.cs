using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Topshiriq/test (boy model) o'qish/yozish mantig'i. O'qituvchi yaratadi (format, ko'p sinf,
/// materiallar, muddat, baholash, test savollari); admin va o'quvchi ko'radi.
/// </summary>
public static class AssignmentService
{
    private static IQueryable<Assignment> Query(IAppDbContext db) =>
        db.Assignments.Include(a => a.Materials).Include(a => a.Questions);

    /// <summary>O'qituvchining o'zi yaratgan topshiriqlari (yangidan eskiga).</summary>
    public static async Task<List<AssignmentDto>> ListForTeacherAsync(IAppDbContext db, string userId)
    {
        var list = await Query(db).Where(a => a.CreatedByUserId == userId)
            .OrderByDescending(a => a.CreatedAt).ToListAsync();
        return await ToDtosAsync(db, list);
    }

    /// <summary>Barcha topshiriqlar (admin ko'radi).</summary>
    public static async Task<List<AssignmentDto>> ListAllAsync(IAppDbContext db)
    {
        var list = await Query(db).OrderByDescending(a => a.CreatedAt).ToListAsync();
        return await ToDtosAsync(db, list);
    }

    /// <summary>Berilgan sinfga tegishli topshiriqlar (o'quvchi ko'radi).</summary>
    public static async Task<List<AssignmentDto>> ListForClassAsync(IAppDbContext db, string classId)
    {
        var all = await Query(db).ToListAsync();
        var list = all.Where(a => a.ClassIds.Contains(classId))
            .OrderByDescending(a => a.CreatedAt).ToList();
        return await ToDtosAsync(db, list);
    }

    public static async Task<Assignment?> FindAsync(IAppDbContext db, string id) =>
        await Query(db).FirstOrDefaultAsync(a => a.Id == id);

    public static async Task<AssignmentDto> CreateAsync(IAppDbContext db, string userId, SaveAssignmentRequest req)
    {
        var a = new Assignment
        {
            CreatedByUserId = userId,
            SubjectId = req.SubjectId,
            Title = (req.Title ?? "").Trim(),
            Description = (req.Description ?? "").Trim(),
            Format = string.IsNullOrWhiteSpace(req.Format) ? "written" : req.Format,
            ClassIds = req.ClassIds ?? new(),
            StartDate = Empty(req.StartDate),
            DueDate = Empty(req.DueDate),
            LateAccept = req.LateAccept,
            LatePenaltyPct = req.LatePenaltyPct,
            MaxScore = req.MaxScore > 0 ? req.MaxScore : 100,
            AutoGrade = req.AutoGrade,
            CreatedAt = DateTime.UtcNow,
        };
        Apply(a, req);
        db.Assignments.Add(a);
        await db.SaveChangesAsync();
        return await ToDtoAsync(db, a);
    }

    public static async Task<bool> UpdateAsync(IAppDbContext db, string id, SaveAssignmentRequest req)
    {
        var a = await Query(db).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return false;

        a.SubjectId = req.SubjectId;
        a.Title = (req.Title ?? "").Trim();
        a.Description = (req.Description ?? "").Trim();
        a.Format = string.IsNullOrWhiteSpace(req.Format) ? "written" : req.Format;
        a.ClassIds = req.ClassIds ?? new();
        a.StartDate = Empty(req.StartDate);
        a.DueDate = Empty(req.DueDate);
        a.LateAccept = req.LateAccept;
        a.LatePenaltyPct = req.LatePenaltyPct;
        a.MaxScore = req.MaxScore > 0 ? req.MaxScore : 100;
        a.AutoGrade = req.AutoGrade;

        // Materiallar va savollarni qaytadan yozamiz (eskini o'chirib, yangisini qo'shamiz).
        db.AssignmentMaterials.RemoveRange(a.Materials);
        db.TestQuestions.RemoveRange(a.Questions);
        a.Materials = new();
        a.Questions = new();
        Apply(a, req);

        await db.SaveChangesAsync();
        return true;
    }

    public static async Task<bool> DeleteAsync(IAppDbContext db, string id)
    {
        var a = await db.Assignments.FindAsync(id);
        if (a is null) return false;
        // Bajarish holatlarini ham olib tashlaymiz (material/savollar cascade bilan o'chadi).
        db.AssignmentSubmissions.RemoveRange(db.AssignmentSubmissions.Where(x => x.AssignmentId == id));
        db.Assignments.Remove(a);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Topshiriq natijalari: maqsadli sinflar o'quvchilari + har birining holati (bajardi/yo'q).</summary>
    public static async Task<AssignmentResultDto?> GetResultsAsync(IAppDbContext db, string assignmentId)
    {
        var a = await db.Assignments.FindAsync(assignmentId);
        if (a is null) return null;

        var classNames = await db.Classes.Where(c => a.ClassIds.Contains(c.Id))
            .Select(c => c.Name).ToListAsync();
        var students = await db.Students.Where(s => classNames.Contains(s.ClassName))
            .OrderBy(s => s.ClassName).ThenBy(s => s.FullName).ToListAsync();
        var byStudent = (await db.AssignmentSubmissions.Where(x => x.AssignmentId == assignmentId).ToListAsync())
            .ToDictionary(x => x.StudentId);

        var rows = students.Select(s =>
        {
            byStudent.TryGetValue(s.Id, out var sub);
            return new SubmissionRowDto(
                s.Id, s.FullName, s.ClassName, sub?.Completed ?? false, sub?.SubmittedAt,
                sub?.Score, sub?.AnswerText, sub?.FileUrl);
        }).ToList();

        return new AssignmentResultDto(
            a.Id, a.Title, a.Format, a.MaxScore, rows.Count, rows.Count(r => r.Completed), rows);
    }

    /// <summary>
    /// Admin "Topshiriqlar bali" — bitta sinf bo'yicha ball jadvali: ustunlar = shu sinfga berilgan
    /// topshiriqlar, qatorlar = sinf o'quvchilari, kataklar = har bir o'quvchining bali/holati.
    /// </summary>
    public static async Task<AssignmentScoreboardDto?> GetScoreboardAsync(IAppDbContext db, string classId)
    {
        var cls = await db.Classes.FindAsync(classId);
        if (cls is null) return null;

        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var assignments = (await Query(db).OrderBy(a => a.CreatedAt).ToListAsync())
            .Where(a => a.ClassIds.Contains(classId))
            .ToList();

        var students = await db.Students
            .Where(s => s.ClassName == cls.Name && !s.IsArchived)
            .OrderBy(s => s.FullName).ToListAsync();

        var assignmentIds = assignments.Select(a => a.Id).ToHashSet();
        var studentIds = students.Select(s => s.Id).ToList();
        // (AssignmentId, StudentId) -> submission
        var subs = (await db.AssignmentSubmissions
                .Where(x => studentIds.Contains(x.StudentId))
                .ToListAsync())
            .Where(x => assignmentIds.Contains(x.AssignmentId))
            .ToDictionary(x => (x.AssignmentId, x.StudentId));

        var columns = assignments.Select(a => new AssignmentScoreColumnDto(
            a.Id, a.Title, subjects.GetValueOrDefault(a.SubjectId, ""), a.Format, a.MaxScore, a.DueDate)).ToList();

        var rows = students.Select(s =>
        {
            var cells = new List<AssignmentScoreCellDto>();
            var totalScore = 0; var totalMax = 0; var graded = 0;
            foreach (var a in assignments)
            {
                subs.TryGetValue((a.Id, s.Id), out var sub);
                cells.Add(new AssignmentScoreCellDto(a.Id, sub?.Completed ?? false, sub?.Score));
                if (sub?.Score is int sc)
                {
                    totalScore += sc; totalMax += a.MaxScore; graded++;
                }
            }
            return new AssignmentScoreRowDto(s.Id, s.FullName, s.ClassName, cells, totalScore, totalMax, graded);
        }).ToList();

        return new AssignmentScoreboardDto(cls.Id, cls.Name, columns, rows);
    }

    /// <summary>O'quvchi/ota-ona "Topshiriq ballari" — o'quvchiga berilgan topshiriqlar + uning bali.</summary>
    public static async Task<StudentAssignmentScoresDto> ScoresForStudentAsync(
        IAppDbContext db, string classId, string studentId)
    {
        var all = await Query(db).ToListAsync();
        var mine = all.Where(a => a.ClassIds.Contains(classId))
            .OrderByDescending(a => a.CreatedAt).ToList();
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var subs = (await db.AssignmentSubmissions.Where(x => x.StudentId == studentId).ToListAsync())
            .ToDictionary(x => x.AssignmentId);

        var items = mine.Select(a =>
        {
            subs.TryGetValue(a.Id, out var sub);
            return new StudentAssignmentScoreDto(
                a.Id, subjects.GetValueOrDefault(a.SubjectId, ""), a.Title, a.Format,
                a.MaxScore, sub?.Score, sub?.Completed ?? false, a.DueDate, sub?.SubmittedAt);
        }).ToList();

        var graded = items.Where(i => i.Score.HasValue).ToList();
        return new StudentAssignmentScoresDto(
            items.Count, graded.Count,
            graded.Sum(i => i.Score!.Value), graded.Sum(i => i.MaxScore), items);
    }

    /// <summary>O'quvchining bajarish holatini belgilaydi (upsert).</summary>
    public static async Task<bool> SetSubmissionAsync(
        IAppDbContext db, string assignmentId, string studentId, bool completed, int? score)
    {
        if (await db.Assignments.FindAsync(assignmentId) is null) return false;
        var sub = await db.AssignmentSubmissions
            .FirstOrDefaultAsync(x => x.AssignmentId == assignmentId && x.StudentId == studentId);
        if (sub is null)
        {
            sub = new AssignmentSubmission { AssignmentId = assignmentId, StudentId = studentId };
            db.AssignmentSubmissions.Add(sub);
        }
        sub.Completed = completed;
        sub.Score = score;
        sub.SubmittedAt = completed ? (sub.SubmittedAt ?? DateTime.UtcNow.ToString("o")) : null;
        await db.SaveChangesAsync();
        return true;
    }

    // ---------- O'quvchi (mobil) uchun — to'g'ri javoblar OSHKOR QILINMAYDI ----------

    public static async Task<List<StudentAssignmentDto>> ListForStudentAsync(
        IAppDbContext db, string classId, string studentId)
    {
        var all = await Query(db).ToListAsync();
        var mine = all.Where(a => a.ClassIds.Contains(classId))
            .OrderByDescending(a => a.CreatedAt).ToList();
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var subs = (await db.AssignmentSubmissions.Where(x => x.StudentId == studentId).ToListAsync())
            .ToDictionary(x => x.AssignmentId);

        return mine.Select(a =>
        {
            subs.TryGetValue(a.Id, out var sub);
            return new StudentAssignmentDto(
                a.Id, subjects.GetValueOrDefault(a.SubjectId, ""), a.Title, a.Description, a.Format,
                a.StartDate, a.DueDate, a.LateAccept, a.LatePenaltyPct, a.MaxScore,
                a.Questions.Count, a.Materials.Select(Mat).ToList(),
                sub?.Completed ?? false, sub?.SubmittedAt, sub?.Score);
        }).ToList();
    }

    public static async Task<StudentAssignmentDetailDto?> GetForStudentAsync(
        IAppDbContext db, string assignmentId, string classId, string studentId)
    {
        var a = await Query(db).FirstOrDefaultAsync(x => x.Id == assignmentId);
        if (a is null || !a.ClassIds.Contains(classId)) return null;
        var subjectName = (await db.Subjects.FindAsync(a.SubjectId))?.Name ?? "";
        var sub = await db.AssignmentSubmissions
            .FirstOrDefaultAsync(x => x.AssignmentId == assignmentId && x.StudentId == studentId);
        var questions = a.Questions.OrderBy(q => q.Order)
            .Select(q => new StudentTestQuestionDto(q.Id, q.Text, q.Options)).ToList();
        return new StudentAssignmentDetailDto(
            a.Id, subjectName, a.Title, a.Description, a.Format,
            a.StartDate, a.DueDate, a.LateAccept, a.LatePenaltyPct, a.MaxScore,
            a.Materials.Select(Mat).ToList(), questions,
            sub?.Completed ?? false, sub?.SubmittedAt, sub?.Score, sub?.AnswerText, sub?.FileUrl);
    }

    public static async Task<SubmitResultDto?> SubmitAsync(
        IAppDbContext db, string assignmentId, string classId, string studentId, SubmitAssignmentRequest req)
    {
        var a = await Query(db).FirstOrDefaultAsync(x => x.Id == assignmentId);
        if (a is null || !a.ClassIds.Contains(classId)) return null;

        var sub = await db.AssignmentSubmissions
            .FirstOrDefaultAsync(x => x.AssignmentId == assignmentId && x.StudentId == studentId);
        if (sub is null)
        {
            sub = new AssignmentSubmission { AssignmentId = assignmentId, StudentId = studentId };
            db.AssignmentSubmissions.Add(sub);
        }

        int? score = null, correct = null, total = null;
        if (a.Format == "test")
        {
            var ans = (req.Answers ?? new()).GroupBy(x => x.QuestionId)
                .ToDictionary(g => g.Key, g => g.First().SelectedIndex);
            total = a.Questions.Count;
            correct = a.Questions.Count(q => ans.TryGetValue(q.Id, out var sel) && sel == q.CorrectIndex);
            score = total > 0 ? (int)Math.Round((double)correct.Value / total.Value * a.MaxScore) : 0;
            sub.Score = score;
        }
        else
        {
            sub.AnswerText = string.IsNullOrWhiteSpace(req.AnswerText) ? null : req.AnswerText.Trim();
            sub.FileUrl = string.IsNullOrWhiteSpace(req.FileUrl) ? null : req.FileUrl;
        }
        sub.Completed = true;
        sub.SubmittedAt = DateTime.UtcNow.ToString("o");
        await db.SaveChangesAsync();
        return new SubmitResultDto(true, score, correct, total);
    }

    private static AssignmentMaterialDto Mat(AssignmentMaterial m) =>
        new(m.Id, m.Name, m.Url, m.Size, m.ContentType);

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>So'rovdagi material va savollarni topshiriqqa qo'shadi.</summary>
    private static void Apply(Assignment a, SaveAssignmentRequest req)
    {
        foreach (var m in req.Materials ?? new())
            a.Materials.Add(new AssignmentMaterial
            {
                AssignmentId = a.Id,
                Name = m.Name,
                Url = m.Url,
                Size = m.Size,
                ContentType = m.ContentType,
            });

        if (a.Format == "test")
        {
            var order = 0;
            foreach (var q in req.Questions ?? new())
            {
                if (string.IsNullOrWhiteSpace(q.Text)) continue;
                a.Questions.Add(new TestQuestion
                {
                    AssignmentId = a.Id,
                    Text = q.Text.Trim(),
                    Options = (q.Options ?? new()).Select(o => o ?? "").ToList(),
                    CorrectIndex = q.CorrectIndex,
                    Order = order++,
                });
            }
        }
    }

    private static async Task<List<AssignmentDto>> ToDtosAsync(IAppDbContext db, List<Assignment> list)
    {
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var classes = await db.Classes.ToDictionaryAsync(c => c.Id, c => c.Name);
        return list.Select(a => ToDto(a, subjects, classes)).ToList();
    }

    private static async Task<AssignmentDto> ToDtoAsync(IAppDbContext db, Assignment a)
    {
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var classes = await db.Classes.ToDictionaryAsync(c => c.Id, c => c.Name);
        return ToDto(a, subjects, classes);
    }

    private static AssignmentDto ToDto(
        Assignment a, Dictionary<string, string> subjects, Dictionary<string, string> classes) => new(
        a.Id, a.CreatedByUserId, a.SubjectId, subjects.GetValueOrDefault(a.SubjectId, ""), a.Title,
        a.Description, a.Format, a.ClassIds,
        a.ClassIds.Select(id => classes.GetValueOrDefault(id, "")).Where(n => n.Length > 0).ToList(),
        a.StartDate, a.DueDate, a.LateAccept, a.LatePenaltyPct, a.MaxScore, a.AutoGrade,
        a.CreatedAt.ToString("o"),
        a.Materials.Select(m => new AssignmentMaterialDto(m.Id, m.Name, m.Url, m.Size, m.ContentType)).ToList(),
        a.Questions.OrderBy(q => q.Order)
            .Select(q => new TestQuestionDto(q.Id, q.Text, q.Options, q.CorrectIndex, q.Order)).ToList());
}
