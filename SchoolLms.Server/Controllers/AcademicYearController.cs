using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/academic-year")]
public class AcademicYearController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const int MaxGrade = 11;

    // Arxiv snapshot'i camelCase JSON sifatida saqlanadi.
    private static readonly JsonSerializerOptions ArchiveJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    // Saqlangan snapshot'ni qayta o'qish (camelCase → entity).
    private static readonly JsonSerializerOptions ArchiveReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Joriy o'quv yili va tozalanishi mumkin bo'lgan ma'lumotlar soni.</summary>
    [HttpGet]
    public async Task<ActionResult<AcademicYearInfoDto>> Info()
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        return new AcademicYearInfoDto(
            meta?.CurrentYear ?? "",
            await db.Students.CountAsync(),
            await db.Classes.CountAsync(),
            await db.JournalEntries.CountAsync(),
            await db.WeekAssignments.CountAsync(),
            await db.FinanceTransactions.CountAsync());
    }

    /// <summary>Arxivlangan o'quv yillari ro'yxati (katta JSON'siz).</summary>
    [HttpGet("archives")]
    public async Task<ActionResult<IEnumerable<ArchiveListItemDto>>> Archives() =>
        await db.SchoolYearArchives
            .OrderByDescending(a => a.Year).ThenByDescending(a => a.CreatedAt)
            .Select(a => new ArchiveListItemDto(
                a.Id, a.Year, a.CreatedAt, a.StudentsCount, a.ClassesCount, a.JournalCount, a.FinanceCount))
            .ToListAsync();

    /// <summary>
    /// Bir o'quv yiliga tegishli BARCHA ma'lumotni ZIP shaklida yuklab olish — har bo'lim
    /// alohida fayl, papkalarga ajratilgan (Royxatlar, Baholar, Jadval, Moliya) + to'liq JSON.
    /// </summary>
    [HttpGet("archives/{id}/download")]
    public async Task<IActionResult> DownloadZip(string id)
    {
        var a = await db.SchoolYearArchives.FindAsync(id);
        if (a is null) return NotFound();

        var snap = JsonSerializer.Deserialize<SnapshotData>(a.Data, ArchiveReadJson) ?? new SnapshotData();
        var folder = string.IsNullOrWhiteSpace(a.Year) || a.Year == "—" ? "arxiv" : a.Year.Replace("/", "-");

        var classById = Dict(snap.Classes, c => c.Id, c => c.Name);
        var subjectById = Dict(snap.Subjects, s => s.Id, s => s.Name);
        var studentById = Dict(snap.Students, s => s.Id, s => s.FullName);
        var teacherById = Dict(snap.Teachers, t => t.Id, t => t.FullName);
        var reasonById = Dict(snap.AbsenceReasons, r => r.Id, r => r.Name);
        var templateById = Dict(snap.ScheduleTemplates, t => t.Id, t => t.Name);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            AddCsv(zip, $"{folder}/Royxatlar/oquvchilar.csv",
                new[] { "F.I.SH", "Sinf", "Tug'ilgan kun", "Jinsi", "Ota-ona", "Telefon", "Qabul sanasi", "Balans" },
                snap.Students.Select(s => new[]
                {
                    s.FullName, s.ClassName, s.BirthDate, s.Gender, s.ParentFullName, s.ParentPhone,
                    s.EnrollmentDate, Money(s.Balance),
                }));
            AddCsv(zip, $"{folder}/Royxatlar/sinflar.csv",
                new[] { "Nomi", "Sinf (daraja)", "Til", "Xona", "Oylik to'lov" },
                snap.Classes.OrderBy(c => c.Grade).ThenBy(c => c.Name).Select(c => new[]
                {
                    c.Name, c.Grade.ToString(), c.Language, c.Room ?? "", Money(c.MonthlyFee),
                }));
            AddCsv(zip, $"{folder}/Royxatlar/oqituvchilar.csv",
                new[] { "F.I.SH", "Tug'ilgan kun", "Jinsi", "Sinf rahbarligi", "Oylik", "Oylik boshlanishi" },
                snap.Teachers.Select(t => new[]
                {
                    t.FullName, t.BirthDate, t.Gender, t.HomeroomClass, Money(t.Salary), t.SalaryStartMonth,
                }));
            AddCsv(zip, $"{folder}/Royxatlar/fanlar.csv",
                new[] { "Fan nomi" },
                snap.Subjects.OrderBy(s => s.Name).Select(s => new[] { s.Name }));

            AddCsv(zip, $"{folder}/Baholar/baholar.csv",
                new[] { "O'quvchi", "Sinf", "Fan", "Chorak", "Sana", "Baho", "Sabab" },
                snap.Journal.Select(e => new[]
                {
                    Get(studentById, e.StudentId), Get(classById, e.ClassId), Get(subjectById, e.SubjectId),
                    e.Quarter.ToString(), e.Date, e.Grade?.ToString() ?? "",
                    e.ReasonId is null ? "" : Get(reasonById, e.ReasonId),
                }));
            AddCsv(zip, $"{folder}/Baholar/dars-mavzulari.csv",
                new[] { "Sinf", "Fan", "Chorak", "Sana", "Mavzu", "Uyga vazifa" },
                snap.LessonNotes.Select(n => new[]
                {
                    Get(classById, n.ClassId), Get(subjectById, n.SubjectId), n.Quarter.ToString(),
                    n.Date, n.Topic, n.Homework ?? "",
                }));

            AddCsv(zip, $"{folder}/Jadval/dars-jadvali.csv",
                new[] { "Sinf", "Chorak", "Hafta", "Jadval (shablon)" },
                snap.WeekAssignments.Select(w => new[]
                {
                    Get(classById, w.ClassId), w.Quarter.ToString(), w.Week.ToString(),
                    w.TemplateId is null ? "" : Get(templateById, w.TemplateId),
                }));
            AddCsv(zip, $"{folder}/Jadval/choraklar.csv",
                new[] { "Chorak", "Boshlanishi", "Tugashi" },
                snap.Quarters.OrderBy(q => q.Quarter).Select(q => new[] { q.Quarter.ToString(), q.StartDate, q.EndDate }));

            AddCsv(zip, $"{folder}/Moliya/moliya.csv",
                new[] { "Sana", "Yo'nalish", "Toifa", "Summa", "Oy", "Izoh", "O'quvchi", "O'qituvchi" },
                snap.Finance.OrderBy(f => f.Date).Select(f => new[]
                {
                    f.Date, f.Direction == "income" ? "Kirim" : "Chiqim", f.Category, Money(f.Amount),
                    f.Month ?? "", f.Note ?? "",
                    f.StudentId is null ? "" : Get(studentById, f.StudentId),
                    f.TeacherId is null ? "" : Get(teacherById, f.TeacherId),
                }));
            AddCsv(zip, $"{folder}/Moliya/oylik-hisoblar.csv",
                new[] { "O'quvchi", "Oy", "Summa", "Sana" },
                snap.MonthlyCharges.Select(m => new[]
                {
                    Get(studentById, m.StudentId), m.Month, Money(m.Amount), m.Date,
                }));

            // To'liq xom ma'lumot (JSON)
            var jsonEntry = zip.CreateEntry($"{folder}/malumotlar.json", CompressionLevel.Optimal);
            using var jw = new StreamWriter(jsonEntry.Open(), new UTF8Encoding(true));
            jw.Write(a.Data);
        }

        return File(ms.ToArray(), "application/zip", $"{folder}-arxiv.zip");
    }

    private static string Money(decimal v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Get(IReadOnlyDictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) ? v : key;
    private static Dictionary<string, string> Dict<T>(IEnumerable<T> items, Func<T, string> key, Func<T, string> val)
    {
        var d = new Dictionary<string, string>();
        foreach (var i in items) d[key(i)] = val(i);
        return d;
    }
    private static void AddCsv(ZipArchive zip, string path, string[] header, IEnumerable<string[]> rows)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(true));
        w.WriteLine(string.Join(",", header.Select(Csv)));
        foreach (var r in rows) w.WriteLine(string.Join(",", r.Select(Csv)));
    }
    private static string Csv(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";

    /// <summary>Saqlangan arxiv snapshot'ini qayta o'qish uchun struktura.</summary>
    private sealed class SnapshotData
    {
        public List<Student> Students { get; set; } = new();
        public List<SchoolClass> Classes { get; set; } = new();
        public List<Subject> Subjects { get; set; } = new();
        public List<Teacher> Teachers { get; set; } = new();
        public List<JournalEntry> Journal { get; set; } = new();
        public List<LessonNote> LessonNotes { get; set; } = new();
        public List<ScheduleTemplate> ScheduleTemplates { get; set; } = new();
        public List<WeekAssignment> WeekAssignments { get; set; } = new();
        public List<QuarterPeriod> Quarters { get; set; } = new();
        public List<AbsenceReason> AbsenceReasons { get; set; } = new();
        public List<LessonTime> LessonTimes { get; set; } = new();
        public List<FinanceTransaction> Finance { get; set; } = new();
        public List<MonthlyCharge> MonthlyCharges { get; set; } = new();
    }

    /// <summary>
    /// Yangi o'quv yiliga o'tish: joriy yil ma'lumotlari arxivlanadi (snapshot), o'quvchilar
    /// (ixtiyoriy) keyingi sinfga ko'tariladi, tanlangan toifalar tozalanadi, joriy yil yangilanadi.
    /// </summary>
    [HttpPost("rollover")]
    public async Task<ActionResult<RolloverResultDto>> Rollover(RolloverRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewYear))
            return BadRequest(new { message = "Yangi o'quv yilini kiriting" });

        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        if (meta is null)
        {
            meta = new SchoolMeta { CurrentYear = "" };
            db.SchoolMeta.Add(meta);
        }
        var oldYear = meta.CurrentYear;

        // 1) Joriy yilni to'liq arxivlaymiz (mutatsiyadan OLDIN snapshot).
        var snapshot = new
        {
            Year = oldYear,
            Students = await db.Students.AsNoTracking().ToListAsync(),
            Classes = await db.Classes.AsNoTracking().ToListAsync(),
            Subjects = await db.Subjects.AsNoTracking().ToListAsync(),
            Teachers = await db.Teachers.AsNoTracking().ToListAsync(),
            Journal = await db.JournalEntries.AsNoTracking().ToListAsync(),
            LessonNotes = await db.LessonNotes.AsNoTracking().ToListAsync(),
            ScheduleTemplates = await db.ScheduleTemplates.Include(t => t.Lessons).AsNoTracking().ToListAsync(),
            WeekAssignments = await db.WeekAssignments.AsNoTracking().ToListAsync(),
            Quarters = await db.Quarters.AsNoTracking().ToListAsync(),
            AbsenceReasons = await db.AbsenceReasons.AsNoTracking().ToListAsync(),
            LessonTimes = await db.LessonTimes.AsNoTracking().ToListAsync(),
            Finance = await db.FinanceTransactions.AsNoTracking().ToListAsync(),
            MonthlyCharges = await db.MonthlyCharges.AsNoTracking().ToListAsync(),
        };
        db.SchoolYearArchives.Add(new SchoolYearArchive
        {
            Year = string.IsNullOrEmpty(oldYear) ? "—" : oldYear,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            StudentsCount = snapshot.Students.Count,
            ClassesCount = snapshot.Classes.Count,
            JournalCount = snapshot.Journal.Count,
            FinanceCount = snapshot.Finance.Count,
            Data = JsonSerializer.Serialize(snapshot, ArchiveJson),
        });

        var promoted = 0;
        var graduated = 0;

        // 2) O'quvchilarni keyingi sinfga ko'tarish (yuqori sinfdan boshlab — nom to'qnashuvi bo'lmaydi).
        if (req.PromoteStudents)
        {
            var classes = await db.Classes.ToListAsync();
            var students = await db.Students.ToListAsync();
            var teachers = await db.Teachers.ToListAsync();

            foreach (var cls in classes.OrderByDescending(c => c.Grade))
            {
                var newGrade = cls.Grade + 1;
                var clsStudents = students.Where(s => s.ClassName == cls.Name).ToList();

                if (newGrade > MaxGrade)
                {
                    // Bitiruvchilar — o'quvchi + akkaunt + oylik hisoblari o'chiriladi (arxivda saqlangan).
                    foreach (var s in clsStudents)
                    {
                        if (s.UserId is not null)
                        {
                            var u = await db.Users.FindAsync(s.UserId);
                            if (u is not null) db.Users.Remove(u);
                        }
                        db.MonthlyCharges.RemoveRange(
                            await db.MonthlyCharges.Where(c => c.StudentId == s.Id).ToListAsync());
                        db.Students.Remove(s);
                        graduated++;
                    }
                    db.ScheduleTemplates.RemoveRange(
                        await db.ScheduleTemplates.Where(t => t.ClassId == cls.Id).ToListAsync());
                    db.WeekAssignments.RemoveRange(
                        await db.WeekAssignments.Where(w => w.ClassId == cls.Id).ToListAsync());
                    db.Classes.Remove(cls);
                }
                else
                {
                    var oldName = cls.Name;
                    var newName = RenameClass(oldName, cls.Grade, newGrade);
                    cls.Grade = newGrade;
                    cls.Name = newName;
                    foreach (var s in clsStudents) { s.ClassName = newName; promoted++; }
                    foreach (var t in teachers.Where(t => t.HomeroomClass == oldName)) t.HomeroomClass = newName;
                }
            }
        }

        // 3) Tanlangan toifalarni tozalaymiz (yangi yil toza boshlanadi).
        if (req.ClearGrades)
        {
            db.JournalEntries.RemoveRange(await db.JournalEntries.ToListAsync());
            db.LessonNotes.RemoveRange(await db.LessonNotes.ToListAsync());
            db.QuarterGrades.RemoveRange(await db.QuarterGrades.ToListAsync());
            // Topshiriqlar, intizomiy ball, oylik baholash (feedback), LMS o'zlashtirish — hammasi nolga.
            db.AssignmentSubmissions.RemoveRange(await db.AssignmentSubmissions.ToListAsync());
            db.AssignmentMaterials.RemoveRange(await db.AssignmentMaterials.ToListAsync());
            db.TestQuestions.RemoveRange(await db.TestQuestions.ToListAsync());
            db.Assignments.RemoveRange(await db.Assignments.ToListAsync());
            db.DisciplinePoints.RemoveRange(await db.DisciplinePoints.ToListAsync());
            db.EvaluationGrades.RemoveRange(await db.EvaluationGrades.ToListAsync());
            db.LmsProgresses.RemoveRange(await db.LmsProgresses.ToListAsync());
            db.PickupRequests.RemoveRange(await db.PickupRequests.ToListAsync());
            // Eski yil faoliyati — sinf chati, e'lonlar, taklif/shikoyatlar, o'qituvchi davomati ham nolga.
            db.ChatMessages.RemoveRange(await db.ChatMessages.ToListAsync());
            db.Broadcasts.RemoveRange(await db.Broadcasts.ToListAsync());
            db.Feedbacks.RemoveRange(await db.Feedbacks.ToListAsync());
            db.TeacherAttendances.RemoveRange(await db.TeacherAttendances.ToListAsync());
        }
        // Yangi yil — o'quvchilar guruhlari ham reset bo'ladi (har yili qayta guruhlash).
        // Bu jurnal yozuvlari tozalansa, locked=false bo'lib admin yangi guruhga bo'la oladi.
        foreach (var s in await db.Students.ToListAsync()) s.SubGroup = 0;
        // Dars jadvalini tozalash — shablon (ScheduleTemplate) VA hafta biriktirishlari to'liq o'chadi
        // (avval faqat WeekAssignment o'chardi → shablonlar "yetim" bo'lib qolardi). Default: yoqilgan.
        if (req.ClearSchedule)
        {
            db.ScheduleTemplates.RemoveRange(await db.ScheduleTemplates.ToListAsync());
            db.WeekAssignments.RemoveRange(await db.WeekAssignments.ToListAsync());
        }
        if (req.ClearQuarters)
        {
            db.Quarters.RemoveRange(await db.Quarters.ToListAsync());
            // Bayram kunlari ham sanaga bog'liq (kalendar) — eski yil sanalari qolib ketmasin.
            db.Holidays.RemoveRange(await db.Holidays.ToListAsync());
        }
        if (req.ClearFinance)
        {
            db.FinanceTransactions.RemoveRange(await db.FinanceTransactions.ToListAsync());
            db.MonthlyCharges.RemoveRange(await db.MonthlyCharges.ToListAsync());
            foreach (var s in await db.Students.ToListAsync()) s.Balance = 0;
        }

        // 4) Joriy o'quv yilini yangilaymiz + audit.
        meta.CurrentYear = req.NewYear;
        audit.Record("AcademicYear", "current", "rollover",
            $"Yangi o'quv yiliga o'tildi: {(string.IsNullOrEmpty(oldYear) ? "—" : oldYear)} → {req.NewYear}" +
            $" (ko'tarildi: {promoted}, bitirdi: {graduated})",
            after: new { req.NewYear, promoted, graduated });

        await db.SaveChangesAsync();
        return new RolloverResultDto(oldYear, req.NewYear, promoted, graduated);
    }

    /// <summary>"1-A" (grade 1) → "2-A" (grade 2). Nomni boshidagi sinf raqamiga qarab yangilaydi.</summary>
    private static string RenameClass(string name, int oldGrade, int newGrade)
    {
        var prefix = oldGrade.ToString();
        return name.StartsWith(prefix) ? newGrade + name[prefix.Length..] : name;
    }
}
