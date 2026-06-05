using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quvchilarni baholash — baholash turlari (CRUD) va baholash jadvali (qatnashish + davomat
/// sabablari + turlar bo'yicha 1-5 baho). Faqat admin.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/student-evaluation")]
public class StudentEvaluationController(AppDbContext db) : ControllerBase
{
    // ---------- Baholash turlari ----------

    [HttpGet("types")]
    public async Task<ActionResult<IEnumerable<EvaluationTypeDto>>> GetTypes()
    {
        return await db.EvaluationTypes
            .OrderBy(t => t.CreatedAt)
            .Select(t => new EvaluationTypeDto(t.Id, t.Name, t.Description))
            .ToListAsync();
    }

    [HttpPost("types")]
    public async Task<ActionResult<EvaluationTypeDto>> CreateType(SaveEvaluationTypeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Tur nomi kerak" });
        var t = new EvaluationType
        {
            Name = req.Name.Trim(),
            Description = (req.Description ?? "").Trim(),
            CreatedAt = AppClock.Now.ToString("o"),
        };
        db.EvaluationTypes.Add(t);
        await db.SaveChangesAsync();
        return new EvaluationTypeDto(t.Id, t.Name, t.Description);
    }

    [HttpPut("types/{id}")]
    public async Task<ActionResult<EvaluationTypeDto>> UpdateType(string id, SaveEvaluationTypeRequest req)
    {
        var t = await db.EvaluationTypes.FindAsync(id);
        if (t is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Tur nomi kerak" });
        t.Name = req.Name.Trim();
        t.Description = (req.Description ?? "").Trim();
        await db.SaveChangesAsync();
        return new EvaluationTypeDto(t.Id, t.Name, t.Description);
    }

    [HttpDelete("types/{id}")]
    public async Task<IActionResult> DeleteType(string id)
    {
        var t = await db.EvaluationTypes.FindAsync(id);
        if (t is not null)
        {
            db.EvaluationTypes.Remove(t);
            // Shu turga qo'yilgan baholarni ham tozalaymiz (osilib qolmasin).
            var grades = await db.EvaluationGrades.Where(g => g.EvaluationTypeId == id).ToListAsync();
            db.EvaluationGrades.RemoveRange(grades);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    // ---------- Baholash jadvali ----------

    /// <summary>Oy ("YYYY-MM") + hafta (0=butun oy, 1..5) → sana oralig'i ("YYYY-MM-DD").</summary>
    private static (string start, string end) PeriodRange(string month, int week)
    {
        var y = int.Parse(month[..4]);
        var m = int.Parse(month.Substring(5, 2));
        var dim = DateTime.DaysInMonth(y, m);
        if (week <= 0) return ($"{month}-01", $"{month}-{dim:D2}");
        var startDay = Math.Min((week - 1) * 7 + 1, dim);
        var endDay = Math.Min(week * 7, dim);
        return ($"{month}-{startDay:D2}", $"{month}-{endDay:D2}");
    }

    /// <summary>
    /// Faol o'quvchilar jadvali (tanlangan oy/hafta bo'yicha): qatnashish (o'tilgan/qatnashgan),
    /// davomat sabablari taqsimoti (har o'qituvchi jurnalda qilgan davomat belgilaridan) va baholash
    /// turlari bo'yicha baholar (tanlangan oy). Qatnashish hisobi <see cref="Analytics"/> bilan bir xil:
    /// o'tilgan dars (LessonNote.Conducted, guruhga mos) minus o'sha darslardagi davomatsizlik
    /// ("kech keldi" mustasno) — faqat tanlangan davr ichida.
    /// </summary>
    [HttpGet("board")]
    public async Task<ActionResult<EvaluationBoardDto>> GetBoard(
        [FromQuery] string? month, [FromQuery] int week = 0, [FromQuery] string? subjectId = null)
    {
        // Mavjud oylar katalogi (o'tilgan dars + davomat belgilaridan) + joriy oy.
        var lessonMonths = await db.LessonNotes.Where(n => n.Conducted && n.Date.Length >= 7)
            .Select(n => n.Date.Substring(0, 7)).Distinct().ToListAsync();
        var markMonths = await db.JournalEntries.Where(e => e.ReasonId != null && e.Date.Length >= 7)
            .Select(e => e.Date.Substring(0, 7)).Distinct().ToListAsync();
        var current = AppClock.Now.ToString("yyyy-MM");
        var months = lessonMonths.Concat(markMonths).Append(current)
            .Distinct().OrderByDescending(x => x, StringComparer.Ordinal).ToList();

        // Tanlangan oy: berilmagan/noto'g'ri bo'lsa joriy oy.
        if (string.IsNullOrEmpty(month) || month.Length < 7 || !months.Contains(month))
            month = months.FirstOrDefault() ?? current;
        if (week < 0 || week > 5) week = 0;
        var (start, end) = PeriodRange(month, week);
        var monthPrefix = month + "-";

        var students = await db.Students.Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.SubGroup }).ToListAsync();
        var classes = await db.Classes.Select(c => new { c.Id, c.Name }).ToListAsync();
        var conducted = (await db.LessonNotes
                .Where(n => n.Conducted && n.Date.StartsWith(monthPrefix))
                .Select(n => new { n.ClassId, n.SubjectId, n.Date, n.Period, n.SubGroup }).ToListAsync())
            .Where(n => string.CompareOrdinal(n.Date, start) >= 0 && string.CompareOrdinal(n.Date, end) <= 0)
            .ToList();
        var marks = (await db.JournalEntries
                .Where(e => e.ReasonId != null && e.Date.StartsWith(monthPrefix))
                .Select(e => new { e.StudentId, e.SubjectId, e.Date, e.Period, e.ReasonId }).ToListAsync())
            .Where(e => string.CompareOrdinal(e.Date, start) >= 0 && string.CompareOrdinal(e.Date, end) <= 0)
            .ToList();
        var reasons = await db.AbsenceReasons.ToListAsync();
        var types = await db.EvaluationTypes.OrderBy(t => t.CreatedAt)
            .Select(t => new EvaluationTypeDto(t.Id, t.Name, t.Description)).ToListAsync();
        var subjects = await db.Subjects.OrderBy(s => s.Name)
            .Select(s => new SubjectDto(s.Id, s.Name)).ToListAsync();
        // Tanlangan fan: bo'sh/"all" = hamma fan (fanlar o'rtachasi, faqat ko'rish);
        // aniq fan id'si = shu fan baholari (tahrirlanadi).
        var selectedSubject = subjectId ?? "";
        var bySubject = !string.IsNullOrEmpty(selectedSubject) && selectedSubject != "all";
        // Baholar — tanlangan OY bo'yicha (hafta baho saqlanishiga ta'sir qilmaydi).
        var allGrades = await db.EvaluationGrades.Where(g => g.Month == month).ToListAsync();
        if (bySubject) allGrades = allGrades.Where(g => (g.SubjectId ?? "") == selectedSubject).ToList();

        var classIdByName = classes.GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var reasonMap = reasons.ToDictionary(r => r.Id);
        var lateSet = reasons.Where(r => r.IsLate).Select(r => r.Id).ToHashSet();

        // O'tilgan darslar sinf bo'yicha: (SubjectId, Date, Period, SubGroup)
        var conductedByClass = conducted
            .GroupBy(c => c.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(c => (c.SubjectId, c.Date, c.Period, c.SubGroup)).ToList());
        var marksByStudent = marks.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var gradesByStudent = allGrades.GroupBy(g => g.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = students.Select(s =>
        {
            // Shu o'quvchi qatnashadigan o'tilgan darslar (butun sinf yoki o'z guruhi).
            var studentConducted = new HashSet<(string, string, int)>();
            if (s.ClassName is not null && classIdByName.TryGetValue(s.ClassName, out var classId)
                && conductedByClass.TryGetValue(classId, out var classConducted))
            {
                foreach (var c in classConducted)
                    if (c.SubGroup == 0 || c.SubGroup == s.SubGroup)
                        studentConducted.Add((c.SubjectId, c.Date, c.Period));
            }
            var conductedCount = studentConducted.Count;

            var studentMarks = marksByStudent.GetValueOrDefault(s.Id) ?? [];
            // Davomatsizlik FAQAT o'tilgan darslarda (kech keldi mustasno) — qatnashish hisobi uchun.
            var absent = studentMarks.Count(m =>
                m.ReasonId != null && !lateSet.Contains(m.ReasonId)
                && studentConducted.Contains((m.SubjectId, m.Date, m.Period)));
            var attended = Math.Max(0, conductedCount - absent);

            // Davomat sabablari taqsimoti: har sababdan necha marta (barcha belgilar, kech keldi ham).
            var reasonCounts = studentMarks
                .Where(m => m.ReasonId != null && reasonMap.ContainsKey(m.ReasonId))
                .GroupBy(m => m.ReasonId!)
                .Select(g =>
                {
                    var r = reasonMap[g.Key];
                    return new AttendanceReasonCountDto(r.Id, r.Name, r.Short, r.IsLate, g.Count());
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            var grades = (gradesByStudent.GetValueOrDefault(s.Id) ?? [])
                .GroupBy(g => g.EvaluationTypeId)
                // Bitta fan tanlansa — aniq baho; "hamma fan" bo'lsa — fanlar bo'yicha o'rtacha (yaxlit).
                .ToDictionary(g => g.Key, g => bySubject ? g.First().Score : (int)Math.Round(g.Average(v => v.Score)));
            var avg = grades.Count > 0 ? Math.Round(grades.Values.Average(), 1) : 0;

            return new EvaluationRowDto(s.Id, s.FullName, s.ClassName, conductedCount, attended,
                reasonCounts, grades, avg);
        })
        .OrderBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();

        return new EvaluationBoardDto(months, month, week, types, rows,
            bySubject ? selectedSubject : "all", subjects);
    }

    /// <summary>
    /// Bitta o'quvchiga bitta tur bo'yicha BIR OYDA baho qo'yish (1-5). Har (o'quvchi, tur, oy)
    /// uchun yagona yozuv. Score bo'sh/oraliqdan tashqari = o'sha oy bahosini tozalash.
    /// </summary>
    [HttpPost("grade")]
    public async Task<IActionResult> SetGrade(SetEvaluationGradeRequest req)
    {
        if (string.IsNullOrEmpty(req.Month) || req.Month.Length < 7)
            return BadRequest(new { message = "Oy tanlanmagan" });
        var student = await db.Students.FindAsync(req.StudentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var type = await db.EvaluationTypes.FindAsync(req.TypeId);
        if (type is null) return NotFound(new { message = "Baholash turi topilmadi" });

        var subj = req.SubjectId ?? "";
        var existing = await db.EvaluationGrades.FirstOrDefaultAsync(g =>
            g.StudentId == req.StudentId && g.EvaluationTypeId == req.TypeId
            && g.Month == req.Month && g.SubjectId == subj);

        if (req.Score is null or < 1 or > 5)
        {
            if (existing is not null) db.EvaluationGrades.Remove(existing);
        }
        else if (existing is null)
        {
            db.EvaluationGrades.Add(new EvaluationGrade
            {
                StudentId = req.StudentId,
                EvaluationTypeId = req.TypeId,
                SubjectId = subj,
                Month = req.Month,
                Week = req.Week,
                Score = req.Score.Value,
                UpdatedAt = AppClock.Now.ToString("o"),
            });
        }
        else
        {
            existing.Score = req.Score.Value;
            existing.Week = req.Week;
            existing.UpdatedAt = AppClock.Now.ToString("o");
        }
        await db.SaveChangesAsync();
        return NoContent();
    }
}
