using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
public class ClassAnalyticsController(AppDbContext db) : ControllerBase
{
    private async Task<(List<Student>, List<Subject>)> LoadCommon() =>
        (await db.Students.ToListAsync(), await db.Subjects.ToListAsync());

    /// <summary>
    /// G-15: bitta sinfning o'zlashtirishi — sinf qatorlari + shu sinf
    /// o'quvchilarining guruh qatorlari, qoidasi
    /// <see cref="ClassAttainment"/> da. O'chirgich o'chiq bo'lsa qamrov
    /// bo'sh, ya'ni so'rovlar bugungisining aynan o'zi.
    /// </summary>
    private async Task<Analytics.ClassResult> BuildFor(
        SchoolClass cls, List<Student> students, List<Subject> subjects, ClassAttainment attainment)
    {
        var ownerIds = attainment.OwnerIdsForClass(
            cls.Id, ClassAttainment.StudentIdsOf(cls, students));
        var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
            .Where(t => ownerIds.Contains(t.ClassId)).ToListAsync();
        var entries = await db.JournalEntries.Where(e => ownerIds.Contains(e.ClassId)).ToListAsync();
        // Davomat maxraji: o'tildi YOKI davomat belgilangan (HeldLessons qoidasi).
        var notes = await HeldLessons.WithMarkedAsync(
            db, await db.LessonNotes.Where(n => ownerIds.Contains(n.ClassId)).ToListAsync(), ownerIds);
        var lateIds = await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync();
        return Analytics.BuildClass(
            cls, students, subjects, templates, entries, notes,
            lateReasonIds: lateIds, attainment: attainment);
    }

    [HttpGet("api/admin/classes/{classId}/performance")]
    public async Task<ActionResult<ClassPerformanceDataDto>> Performance(string classId)
    {
        var cls = await db.Classes.FindAsync(classId);
        if (cls is null) return new ClassPerformanceDataDto([], []);
        var (students, subjects) = await LoadCommon();
        var res = await BuildFor(cls, students, subjects, await ClassAttainment.BuildAsync(db));
        return new ClassPerformanceDataDto(res.Subjects, res.Rows);
    }

    [HttpGet("api/admin/classes/stats")]
    public async Task<ActionResult<Dictionary<string, ClassStatsDto>>> Stats()
    {
        var (students, subjects) = await LoadCommon();
        var classes = await db.Classes.ToListAsync();
        var attainment = await ClassAttainment.BuildAsync(db);
        var result = new Dictionary<string, ClassStatsDto>();
        var byStudent = new Dictionary<string, ClassStudentRowDto>();
        foreach (var cls in classes)
        {
            var rows = (await BuildFor(cls, students, subjects, attainment)).Rows;
            foreach (var r in rows) byStudent[r.Student.Id] = r;
            result[cls.Id] = Summarize(rows);
        }

        // Yo'nalish guruhlari (2026-09-26): Sinflar jadvalida sinflardan keyin qator sifatida
        // chiqadi. Har bir a'zoning ko'rsatkichi yuqorida o'z sinfi hisobida allaqachon bor
        // (sinf hisobi shu o'quvchining guruh darslarini ham qamraydi) — bu yerda faqat
        // guruh a'zolari bo'yicha o'rtachalanadi, qayta hisoblanmaydi. Kalit — guruh id'si.
        var trackMembers = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.LeftOn == null
                        && db.StudyGroups.Any(g => g.Id == m.GroupId && g.IsTrack && !g.IsArchived))
            .Select(m => new { m.GroupId, m.StudentId })
            .ToListAsync();
        foreach (var group in trackMembers.GroupBy(m => m.GroupId))
        {
            var rows = group.Select(m => byStudent.GetValueOrDefault(m.StudentId))
                .Where(r => r is not null).Select(r => r!).ToList();
            result[group.Key.ToString()] = Summarize(rows);
        }
        return result;
    }

    private static ClassStatsDto Summarize(List<ClassStudentRowDto> rows)
    {
        var n = rows.Count;
        var att = rows.Where(r => r.Attendance.HasValue).Select(r => r.Attendance!.Value).ToList();
        return new ClassStatsDto(
            n,
            n > 0 ? Math.Round(rows.Average(r => r.Average), 1) : 0,
            att.Count > 0 ? Math.Round(att.Average()) : null);
    }

    /// <summary>
    /// Maktab bo'yicha o'zlashtirish hisoboti: tanlangan sinflar va choraklar (birlashtirilgan)
    /// bo'yicha o'quvchilar a'lochi / yaxshi / muvaffaqiyat / o'zlashtirmaydiganlarga ajratiladi;
    /// sinf, parallel (daraja), ta'lim bosqichi va maktab bo'yicha jamlanadi.
    /// </summary>
    [HttpGet("api/admin/grades-report/school")]
    public async Task<ActionResult<GradesProgressReportDto>> SchoolGradesReport(
        [FromQuery] string? classIds, [FromQuery] string? quarters)
    {
        var classIdList = (classIds ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var quarterList = (quarters ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n is >= 1 and <= 4)
            .Distinct()
            .ToHashSet();
        if (classIdList.Count == 0 || quarterList.Count == 0)
            return new GradesProgressReportDto(0, 0, new());

        var (students, subjects) = await LoadCommon();
        var classes = await db.Classes.Where(c => classIdList.Contains(c.Id))
            .OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync();

        var lateIds = await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync();
        var attainment = await ClassAttainment.BuildAsync(db);
        var perClass = new List<(SchoolClass Cls, ClassStat Stat)>();
        foreach (var cls in classes)
        {
            // G-15: sinf + shu sinf o'quvchilarining guruhlari (o'chirgich
            // o'chiq bo'lsa — faqat sinf).
            var ownerIds = attainment.OwnerIdsForClass(
                cls.Id, ClassAttainment.StudentIdsOf(cls, students));
            var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
                .Where(t => ownerIds.Contains(t.ClassId)).ToListAsync();
            var entries = (await db.JournalEntries.Where(e => ownerIds.Contains(e.ClassId)).ToListAsync())
                .Where(e => quarterList.Contains(e.Quarter)).ToList();
            var notes = (await HeldLessons.WithMarkedAsync(
                    db, await db.LessonNotes.Where(n => ownerIds.Contains(n.ClassId)).ToListAsync(), ownerIds))
                .Where(n => quarterList.Contains(n.Quarter)).ToList();
            var qgrades = (await db.QuarterGrades.Where(g => ownerIds.Contains(g.ClassId)).ToListAsync())
                .Where(g => quarterList.Contains(g.Quarter)).ToList();
            var res = Analytics.BuildClass(
                cls, students, subjects, templates, entries, notes, qgrades, lateIds, attainment);
            perClass.Add((cls, ComputeStat(res)));
        }

        var rows = new List<GradesProgressRowDto>();
        string? prevLevel = null;
        int levelTotal = 0, schoolTotal = 0;
        foreach (var grade in perClass.Select(x => x.Cls.Grade).Distinct().OrderBy(g => g))
        {
            var group = perClass.Where(x => x.Cls.Grade == grade).OrderBy(x => x.Cls.Name).ToList();
            var level = LevelName(grade);
            if (prevLevel != null && level != prevLevel)
            {
                rows.Add(AggregateRow("level", prevLevel, levelTotal));
                levelTotal = 0;
            }
            foreach (var (cls, stat) in group) rows.Add(ClassRow(cls, stat));
            rows.Add(ParallelRow(grade, group.Select(x => x.Stat).ToList()));
            var gradeTotal = group.Sum(x => x.Stat.Total);
            levelTotal += gradeTotal;
            schoolTotal += gradeTotal;
            prevLevel = level;
        }
        if (prevLevel != null) rows.Add(AggregateRow("level", prevLevel, levelTotal));
        rows.Add(AggregateRow("school", "Maktab", schoolTotal));

        var totalStudents = perClass.Sum(x => x.Stat.Total);
        var noGrades = perClass.Sum(x => x.Stat.Total - x.Stat.Graded);
        return new GradesProgressReportDto(totalStudents, noGrades, rows);
    }

    /// <summary>
    /// Sinf bo'yicha hisobot uchun xom ma'lumot: sinf o'quvchilarining har bir fan bo'yicha
    /// har chorakdagi o'rtacha bahosi. Frontend bundan bir chorak (Group1) yoki butun davr
    /// (Group2) ko'rinishidagi hisobotni quradi.
    /// </summary>
    [HttpGet("api/admin/grades-report/class")]
    public async Task<ActionResult<ClassReportDto>> ClassReport([FromQuery] string classId)
    {
        var cls = await db.Classes.FindAsync(classId);
        if (cls is null) return NotFound();

        var students = await db.Students.Where(s => s.ClassName == cls.Name)
            .OrderBy(s => s.FullName).ToListAsync();
        var allSubjects = await db.Subjects.ToListAsync();
        // G-15: sinf qatorlari + shu sinf o'quvchilarining guruh qatorlari.
        var attainment = await ClassAttainment.BuildAsync(db);
        var ownerIds = attainment.OwnerIdsForClass(
            cls.Id, ClassAttainment.StudentIdsOf(cls, students));
        var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
            .Where(t => ownerIds.Contains(t.ClassId)).ToListAsync();
        var entries = await db.JournalEntries
            .Where(e => ownerIds.Contains(e.ClassId) && e.Grade != null).ToListAsync();
        var quarterGrades = await db.QuarterGrades.Where(g => ownerIds.Contains(g.ClassId)).ToListAsync();

        var fromSchedule = templates.SelectMany(t => t.Lessons).Select(l => l.SubjectId).Distinct().ToList();
        var subjectIds = fromSchedule.Count > 0 ? fromSchedule : allSubjects.Select(s => s.Id).ToList();
        // Guruh jadvali hali tuzilmagan bo'lsa ham uning fani ustun bo'lishi kerak.
        subjectIds = [.. subjectIds.Union(
            attainment.GroupSubjectsOf(students.Select(s => s.Id)), StringComparer.Ordinal)];
        var subjects = subjectIds
            .Select(id => allSubjects.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .Select(s => new SubjectDto(s!.Id, s.Name))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var homeroom = (await db.Teachers.FirstOrDefaultAsync(t => t.HomeroomClass == cls.Name))?.FullName ?? "";

        var studentDtos = students.Select(s =>
        {
            var avgs = new Dictionary<string, Dictionary<int, double>>();
            foreach (var subj in subjects)
            {
                var byQuarter = entries
                    .Where(e => e.StudentId == s.Id && e.SubjectId == subj.Id
                                && attainment.CountsFor(s.Id, e.ClassId, e.OwnerKind, e.Date))
                    .GroupBy(e => e.Quarter)
                    .ToDictionary(g => g.Key, g => Math.Round(g.Average(e => (double)e.Grade!.Value), 2));
                // Rasmiy chorak bahosi kunlik o'rtacha o'rnini bosadi. G-15: sinf va
                // guruh ikkalasi ham rasmiy baho qo'ygan bo'lsa — ikkalasining o'rtachasi
                // (`grades-report/subjects` bilan bir xil qoida).
                foreach (var g in quarterGrades
                             .Where(g => g.StudentId == s.Id && g.SubjectId == subj.Id
                                         && attainment.CountsFor(s.Id, g.ClassId, g.OwnerKind))
                             .GroupBy(g => g.Quarter))
                    byQuarter[g.Key] = g.Average(x => x.Grade);
                if (byQuarter.Count > 0) avgs[subj.Id] = byQuarter;
            }
            return new ClassReportStudentDto(s.Id, s.FullName, avgs);
        }).ToList();

        return new ClassReportDto(
            cls.Id, cls.Name, cls.Grade, LangLabel(cls.Language), homeroom, subjects, studentDtos);
    }

    /// <summary>
    /// O'zlashtirish (fanlar bo'yicha) — sinf × fan pivoti: har katakda tanlangan choraklar
    /// bo'yicha o'rtacha baho, sifat foizi va chorak yoyilmasi; oxirida maktab o'rtachasi.
    ///
    /// <para>Bu <c>grades-report</c> oilasining davomi: baho manbai va rasmiy chorak bahosining
    /// ustunligi <see cref="ClassReport"/> bilan aynan bir xil (<see cref="SubjectAttainmentReport"/>).
    /// O'rtacha va foizlar SERVERDA hisoblanadi.</para>
    /// </summary>
    /// <param name="classIds">Vergul bilan ajratilgan sinf id'lari.</param>
    /// <param name="quarters">Vergul bilan ajratilgan chorak raqamlari (1-4).</param>
    [HttpGet("api/admin/grades-report/subjects")]
    public async Task<ActionResult<SubjectAttainmentReportDto>> SubjectAttainment(
        [FromQuery] string? classIds, [FromQuery] string? quarters)
        => await SubjectAttainmentReport.BuildAsync(db, SplitIds(classIds), SplitQuarters(quarters));

    /// <summary>Vergulli ro'yxatni id'larga ajratadi (bo'sh elementlar tashlanadi).</summary>
    private static List<string> SplitIds(string? csv) =>
        [.. (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Vergulli ro'yxatni chorak raqamlariga ajratadi (1-4 dan tashqarisi tashlanadi).</summary>
    private static List<int> SplitQuarters(string? csv) =>
        [.. (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n is >= 1 and <= 4)
            .Distinct()];

    /// <summary>
    /// Bitta o'quvchining o'zlashtirish va qatnashish hisoboti: har fan bo'yicha chorak baholari
    /// + chorak bo'yicha qoldirilgan kunlar/darslar va kech qolishlar.
    /// </summary>
    [HttpGet("api/admin/grades-report/student")]
    public async Task<ActionResult<StudentReportDto>> StudentReport([FromQuery] string studentId)
    {
        var st = await db.Students.FindAsync(studentId);
        if (st is null) return NotFound();
        return await StudentReportBuilder.BuildAsync(db, st);
    }

    // ---------- O'zlashtirish hisoboti yordamchilari ----------

    private sealed class ClassStat
    {
        public int Total, Graded, Excellent, Good, Satisfactory, Poor;
        public List<string> ExcellentNames = new();
        public List<string> PoorNames = new();
        public double AvgRating, QualityPct, Otm;
        public bool HasGrades => Graded > 0;
        public double ExcellentPct => Pct(Excellent, Total);
        public double GoodPct => Pct(Good, Total);
        public double SatisfactoryPct => Pct(Satisfactory, Total);
        public double PoorPct => Pct(Poor, Total);
    }

    private static double Pct(int part, int total) => total > 0 ? Math.Round(part * 100.0 / total, 2) : 0;

    /// <summary>O'quvchini fan baholariga qarab ajratadi: 0=a'lochi,1=yaxshi,2=muvaffaqiyat,3=o'zlashtirmaydigan,-1=bahosiz.</summary>
    private static int Classify(IReadOnlyDictionary<string, double> grades)
    {
        var marks = grades.Values.Where(v => v > 0)
            .Select(v => (int)Math.Round(v, MidpointRounding.AwayFromZero)).ToList();
        if (marks.Count == 0) return -1;
        if (marks.Any(m => m <= 2)) return 3;
        if (marks.Any(m => m == 3)) return 2;
        if (marks.Any(m => m == 4)) return 1;
        return 0;
    }

    private static string ShortName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        var sur = parts[0].ToUpperInvariant();
        return parts.Length > 1 ? $"{sur} {char.ToUpperInvariant(parts[1][0])}." : sur;
    }

    private static string LangLabel(string lang) => lang switch
    {
        "uz" => "O'zbek tili",
        "ru" => "Rus tili",
        _ => lang,
    };

    private static string LevelName(int grade) =>
        grade <= 4 ? "Boshlang'ich ta'lim" : grade <= 9 ? "Umumiy o'rta ta'lim" : "O'rta ta'lim";

    private static ClassStat ComputeStat(Analytics.ClassResult res)
    {
        var stat = new ClassStat { Total = res.Rows.Count };
        double ratingSum = 0;
        foreach (var r in res.Rows)
        {
            var cat = Classify(r.Grades);
            if (cat == -1) continue;
            stat.Graded++;
            ratingSum += r.Average;
            switch (cat)
            {
                case 0: stat.Excellent++; stat.ExcellentNames.Add(ShortName(r.Student.FullName)); break;
                case 1: stat.Good++; break;
                case 2: stat.Satisfactory++; break;
                case 3: stat.Poor++; stat.PoorNames.Add(ShortName(r.Student.FullName)); break;
            }
        }
        stat.AvgRating = stat.Graded > 0 ? Math.Round(ratingSum / stat.Graded, 2) : 0;
        stat.QualityPct = Pct(stat.Excellent + stat.Good, stat.Total);
        // O'TM% taxminiy: (o'rtacha − 2) / 3 × 100 (emaktab'ning aniq SOR/SOCH formulasi yo'q).
        stat.Otm = stat.AvgRating > 0 ? Math.Round(Math.Clamp((stat.AvgRating - 2) / 3 * 100, 0, 100), 2) : 0;
        return stat;
    }

    private static GradesProgressRowDto ClassRow(SchoolClass cls, ClassStat s) => new(
        "class", cls.Name, LangLabel(cls.Language), s.Total, s.HasGrades,
        s.Excellent, s.ExcellentPct, string.Join("\n", s.ExcellentNames),
        s.Good, s.GoodPct,
        s.Satisfactory, s.SatisfactoryPct,
        s.Poor, s.PoorPct, string.Join("\n", s.PoorNames),
        s.AvgRating, s.QualityPct, s.Otm);

    private static GradesProgressRowDto ParallelRow(int grade, List<ClassStat> stats)
    {
        var graded = stats.Where(s => s.HasGrades).ToList();
        var total = stats.Sum(s => s.Total);
        double Avg(Func<ClassStat, double> sel) => graded.Count > 0 ? Math.Round(graded.Average(sel), 2) : 0;
        return new GradesProgressRowDto(
            "parallel", $"{grade}-parallel", "", total, graded.Count > 0,
            stats.Sum(s => s.Excellent), Avg(s => s.ExcellentPct), "",
            stats.Sum(s => s.Good), Avg(s => s.GoodPct),
            stats.Sum(s => s.Satisfactory), Avg(s => s.SatisfactoryPct),
            stats.Sum(s => s.Poor), Avg(s => s.PoorPct), "",
            Avg(s => s.AvgRating), Avg(s => s.QualityPct), Avg(s => s.Otm));
    }

    /// <summary>Ta'lim bosqichi yoki maktab qatori — faqat jami ko'rsatiladi, kategoriyalar bo'sh.</summary>
    private static GradesProgressRowDto AggregateRow(string kind, string label, int total) => new(
        kind, label, "", total, false, 0, 0, "", 0, 0, 0, 0, 0, 0, "", 0, 0, 0);

    [HttpGet("api/admin/students/rating")]
    public async Task<ActionResult<IEnumerable<StudentRatingRowDto>>> Rating() =>
        await RatingService.SchoolAsync(db);
}
