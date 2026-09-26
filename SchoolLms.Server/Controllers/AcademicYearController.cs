using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("academicYear")]
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
            await db.Payments.CountAsync());
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
        // Arxivni MOLIYADAN ajratib bo'lmaydi: `Moliya/` papkasida hisob-faktura,
        // to'lov va chiqim, `oquvchilar.csv` da har bolaning qoldig'i,
        // `malumotlar.json` da esa ikkalasi ham to'liq yotadi. Shuning uchun bu
        // yuklab olish moliya ruxsatini talab qiladi — faqat `Moliya/` papkasini
        // olib qo'yish soxta himoya bo'lardi, JSON baribir hammasini berardi.
        if (!User.HasReadPerm(PermissionCheck.Finance)) return Forbid();

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

        // Jadval, jurnal va dars mavzusi qatorlarining egasi sinf ham, o'quv
        // guruhi ham bo'lishi mumkin (§2.1.4: guruh id'si o'sha `class_id`
        // ustunida turadi) — ikkalasining nomini bitta lug'atga yig'amiz, aks
        // holda guruh qatorlari arxivda NOMSIZ chiqardi.
        var ownerNameById = new Dictionary<string, string>(classById, StringComparer.Ordinal);
        foreach (var g in snap.StudyGroups) ownerNameById[g.Id.ToString()] = g.Name;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            AddCsv(zip, $"{folder}/Royxatlar/oquvchilar.csv",
                new[] { "F.I.SH", "Sinf", "Tug'ilgan kun", "Jinsi", "Ota-ona", "Telefon", "Qabul sanasi", "Balans" },
                snap.Students.Select(s => new[]
                {
                    s.FullName, s.ClassName, s.BirthDate, s.Gender, s.ParentFullName, s.ParentPhone,
                    s.EnrollmentDate,
                    // Qoldiq snapshot olingan LAHZADA hisoblangan va arxivga
                    // yozilgan (P1-21) — o'quvchi qatorida bunday ustun yo'q.
                    Money(snap.DerivedBalances.GetValueOrDefault(s.Id)),
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
                new[] { "O'quvchi", "Sinf/guruh", "Fan", "Chorak", "Sana", "Baho", "Sabab" },
                snap.Journal.Select(e => new[]
                {
                    Get(studentById, e.StudentId), Get(ownerNameById, e.ClassId), Get(subjectById, e.SubjectId),
                    e.Quarter.ToString(), e.Date, e.Grade?.ToString() ?? "",
                    e.ReasonId is null ? "" : Get(reasonById, e.ReasonId),
                }));
            AddCsv(zip, $"{folder}/Baholar/dars-mavzulari.csv",
                new[] { "Sinf/guruh", "Fan", "Chorak", "Sana", "Mavzu", "Uyga vazifa" },
                snap.LessonNotes.Select(n => new[]
                {
                    Get(ownerNameById, n.ClassId), Get(subjectById, n.SubjectId), n.Quarter.ToString(),
                    n.Date, n.Topic, n.Homework ?? "",
                }));

            AddCsv(zip, $"{folder}/Jadval/dars-jadvali.csv",
                new[] { "Egasi", "Turi", "Chorak", "Hafta", "Jadval (shablon)" },
                snap.WeekAssignments.Select(w => new[]
                {
                    Get(ownerNameById, w.ClassId),
                    w.OwnerKind == LessonOwnerKind.Group ? "Guruh" : "Sinf",
                    w.Quarter.ToString(), w.Week.ToString(),
                    w.TemplateId is null ? "" : Get(templateById, w.TemplateId),
                }));
            AddCsv(zip, $"{folder}/Jadval/choraklar.csv",
                new[] { "Chorak", "Boshlanishi", "Tugashi" },
                snap.Quarters.OrderBy(q => q.Quarter).Select(q => new[] { q.Quarter.ToString(), q.StartDate, q.EndDate }));

            // Moliya — P1-21 dan keyingi uchta manba: hisob-faktura, to'lov, chiqim.
            var categoryById = Dict(snap.FeeCategories, c => c.Id.ToString(), c => c.Name);
            AddCsv(zip, $"{folder}/Moliya/hisob-fakturalar.csv",
                new[] { "O'quvchi", "Toifa", "Oy", "Summa", "Chegirma", "To'lash muddati", "Holat" },
                snap.Invoices.OrderBy(i => i.PeriodMonth).Select(i => new[]
                {
                    Get(studentById, i.StudentId), Get(categoryById, i.CategoryId.ToString()),
                    i.PeriodMonth.ToString("yyyy-MM"), Money(i.Amount), Money(i.Discount),
                    i.DueOn.ToString("yyyy-MM-dd"), i.Status,
                }));
            AddCsv(zip, $"{folder}/Moliya/tolovlar.csv",
                new[] { "Chek", "Sana", "O'quvchi", "Summa", "Usul", "Izoh", "Storno" },
                snap.Payments.OrderBy(t => t.ReceivedAt).Select(t => new[]
                {
                    t.ReceiptNo.ToString(), AppClock.LocalDateOf(t.ReceivedAt).ToString("yyyy-MM-dd"),
                    Get(studentById, t.StudentId), Money(t.Amount), t.Method, t.Note ?? "",
                    t.ReversalOf is null ? "" : "ha",
                }));
            AddCsv(zip, $"{folder}/Moliya/chiqimlar.csv",
                new[] { "Sana", "Toifa", "Summa", "Izoh", "O'qituvchi" },
                snap.Expenses.OrderBy(e => e.OnDate).Select(e => new[]
                {
                    e.OnDate.ToString("yyyy-MM-dd"), e.Category, Money(e.Amount), e.Note ?? "",
                    e.TeacherId is null ? "" : Get(teacherById, e.TeacherId),
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
        /// <summary>O'quv guruhlari — jadval qatorlarining egasi bo'lishi mumkin (§2.1.4).</summary>
        public List<StudyGroup> StudyGroups { get; set; } = new();
        public List<QuarterPeriod> Quarters { get; set; } = new();
        public List<AbsenceReason> AbsenceReasons { get; set; } = new();
        public List<LessonTime> LessonTimes { get; set; } = new();
        // Moliya — P1-21 dan keyingi model (SPEC §3.7).
        public List<FeeCategory> FeeCategories { get; set; } = new();
        public List<StudentSubscription> Subscriptions { get; set; } = new();
        public List<Invoice> Invoices { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();
        public List<Expense> Expenses { get; set; } = new();
        /// <summary>
        /// O'quvchi → snapshot olingan lahzadagi HISOBLANGAN qoldiq. Ustun sifatida
        /// hech qayerda saqlanmaydi (P1-21), lekin arxiv "o'sha kuni qanday edi"
        /// degan savolga javob berishi kerak — shuning uchun snapshot ichida.
        /// </summary>
        public Dictionary<string, decimal> DerivedBalances { get; set; } = new();
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
            // G-11: arxiv eksportida guruh qatorlari NOM bilan chiqishi kerak —
            // `class_id` ustunida guruh id'si turadi va sinflar ro'yxatida u yo'q.
            StudyGroups = await db.StudyGroups.AsNoTracking().ToListAsync(),
            Quarters = await db.Quarters.AsNoTracking().ToListAsync(),
            AbsenceReasons = await db.AbsenceReasons.AsNoTracking().ToListAsync(),
            LessonTimes = await db.LessonTimes.AsNoTracking().ToListAsync(),
            // Moliya — P1-21 dan keyingi model. Jurnal (`ledger_entries`) arxivga
            // KIRMAYDI: u bir yil bilan chegaralanmaydigan, o'zgarmas buxgalteriya
            // tarixi va bazada abadiy qoladi (SPEC §4.1) — uni JSON'ga nusxalash
            // "arxivdagi nusxa haqiqiymi?" degan ikkinchi haqiqat manbaini yaratardi.
            FeeCategories = await db.FeeCategories.AsNoTracking().ToListAsync(),
            Subscriptions = await db.StudentSubscriptions.AsNoTracking().ToListAsync(),
            Invoices = await db.Invoices.AsNoTracking().ToListAsync(),
            Payments = await db.Payments.AsNoTracking().ToListAsync(),
            Expenses = await db.Expenses.AsNoTracking().ToListAsync(),
            DerivedBalances = await new StudentBalanceQuery(db).ForManyAsync(),
        };
        db.SchoolYearArchives.Add(new SchoolYearArchive
        {
            Year = string.IsNullOrEmpty(oldYear) ? "—" : oldYear,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            StudentsCount = snapshot.Students.Count,
            ClassesCount = snapshot.Classes.Count,
            JournalCount = snapshot.Journal.Count,
            FinanceCount = snapshot.Payments.Count,
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

            // Bitiruvchilarni tekshirish uchun kerak bo'ladigan ikki to'plam
            // OLDINDAN, bittadan so'rov bilan olinadi: sikl ichida so'rov
            // yuborilsa 11-sinfdagi har o'quvchi uchun uchtadan so'rov
            // ketardi (N+1).
            var studentsWithMoney = (await db.Invoices.Select(i => i.StudentId).Distinct().ToListAsync())
                .Concat(await db.Payments.Select(x => x.StudentId).Distinct().ToListAsync())
                .ToHashSet(StringComparer.Ordinal);
            var openSubscriptions = (await db.StudentSubscriptions.Where(x => x.EndsOn == null).ToListAsync())
                .GroupBy(x => x.StudentId)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var cls in classes.OrderByDescending(c => c.Grade))
            {
                var newGrade = cls.Grade + 1;
                var clsStudents = students.Where(s => s.ClassName == cls.Name).ToList();

                if (newGrade > MaxGrade)
                {
                    // Bitiruvchilar.
                    //
                    // P1-21: MOLIYAVIY YOZUVI BOR O'QUVCHI O'CHIRILMAYDI, ARXIVLANADI.
                    // `invoices` va `payments` unga RESTRICT bilan bog'langan
                    // (SPEC §4.1 — pul tarixi o'chmaydi), ya'ni `Remove` chaqirig'i
                    // FK xatosi bilan butun rollover'ni qaytarib yuborardi. Tarixi
                    // yo'q o'quvchi avvalgidek butunlay o'chadi.
                    foreach (var s in clsStudents)
                    {
                        if (studentsWithMoney.Contains(s.Id))
                        {
                            s.IsArchived = true;
                            s.ArchivedAt = AppClock.Today.ToString("yyyy-MM-dd");
                            s.ArchiveReason = "Bitirdi";
                            // Login bloklanadi, akkaunt o'chmaydi: to'lov qatorining
                            // `cashier_id` / audit havolalari tirik qolishi kerak.
                            if (s.UserId is not null)
                            {
                                var u = await db.Users.FindAsync(s.UserId);
                                if (u is not null) u.PasswordHash = "";
                            }
                        }
                        else
                        {
                            if (s.UserId is not null)
                            {
                                var u = await db.Users.FindAsync(s.UserId);
                                if (u is not null) db.Users.Remove(u);
                            }
                            db.Students.Remove(s);
                        }

                        // Kelasi yilga hisob yozilmasin — obuna har holda yopiladi.
                        foreach (var sub in openSubscriptions.GetValueOrDefault(s.Id) ?? [])
                            sub.EndsOn = AppClock.Today;

                        graduated++;
                    }
                    db.ScheduleTemplates.RemoveRange(
                        await db.ScheduleTemplates.Where(
                            t => t.ClassId == cls.Id && t.OwnerKind == LessonOwnerKind.Class).ToListAsync());
                    db.WeekAssignments.RemoveRange(
                        await db.WeekAssignments.Where(
                            w => w.ClassId == cls.Id && w.OwnerKind == LessonOwnerKind.Class).ToListAsync());
                    // `study_group_classes.class_id` FK'si RESTRICT (StudyGroupModel.cs).
                    // Bitiruvchi sinf biror o'quv guruhini boqayotgan bo'lsa (11-sinflardan
                    // yig'ilgan ingliz tili guruhi — odatiy holat) `Classes.Remove` BUTUN
                    // rollover'ni 23503 bilan qaytarib yuborardi. Guruhning o'zi 3b-qadamda
                    // arxivlanadi va a'zoliklari sana bilan yopiladi, ya'ni tarix joyida
                    // qoladi — bu yerda faqat "shu sinf boqadi" bog'lanishi uziladi.
                    db.StudyGroupClasses.RemoveRange(
                        await db.StudyGroupClasses.Where(g => g.ClassId == cls.Id).ToListAsync());
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
        // "Moliyani tozalash" — P1-21 da MA'NOSI O'ZGARDI.
        //
        // Ilgari bu butun kassa kitobini va o'quvchi qoldiqlarini o'chirardi.
        // Yangi modelda pul yozuvi o'zgarmas: `payments`, `payment_allocations`
        // va `ledger_entries` da `app_rw` rolida DELETE huquqi UMUMAN yo'q
        // (SPEC §4.1), ya'ni eski xulqni saqlash SQLSTATE 42501 bilan
        // yiqilardi — va agar yiqilmaganda ham, o'tgan yilning chekini
        // o'chirish aynan mijoz qo'rqqan firibgarlik bo'lardi.
        //
        // Shuning uchun bayroq endi TO'XTATADI, o'chirmaydi: barcha ochiq
        // obunalar yopiladi, ya'ni yangi yil hisob-fakturasi eski narx bilan
        // yozilmaydi. Tarix joyida qoladi va qarz ham o'z-o'zidan yo'qolmaydi.
        var subscriptionsClosed = 0;
        if (req.ClearFinance)
        {
            foreach (var sub in await db.StudentSubscriptions.Where(x => x.EndsOn == null).ToListAsync())
            {
                sub.EndsOn = AppClock.Today;
                subscriptionsClosed++;
            }
        }

        // 3b) O'QUV GURUHLARI — students-parity.md §2.1.4 ("Year rollover").
        //
        // Guruh bir o'quv yiliga tegishli: uni boqadigan sinflar ko'tariladi,
        // nomi ("5-sinf ingliz tili") ma'nosini yo'qotadi va bolalar qaytadan
        // taqsimlanadi. Shuning uchun har FAOL guruh arxivlanadi va a'zoliklar
        // o'tish sanasi bilan YOPILADI (o'chirilmaydi — tarix qoladi). Yangi
        // yil guruhlarini maktab "Nusxalash" tugmasi bilan o'zi ochadi (Q8).
        var rolloverDate = AppClock.Today;
        var archivedGroups = 0;
        foreach (var g in await db.StudyGroups.Where(x => !x.IsArchived).ToListAsync())
        {
            g.IsArchived = true;
            g.ArchivedAt = AppClock.Now;
            archivedGroups++;
        }
        var closedMemberships = 0;
        foreach (var m in await db.StudyGroupMembers.Where(x => x.LeftOn == null).ToListAsync())
        {
            m.LeftOn = m.JoinedOn > rolloverDate ? m.JoinedOn : rolloverDate;
            m.LeaveReason ??= "O'quv yili yakunlandi";
            closedMemberships++;
        }

        // 4) Joriy o'quv yilini yangilaymiz + audit.
        meta.CurrentYear = req.NewYear;
        audit.Record("AcademicYear", "current", "rollover",
            $"Yangi o'quv yiliga o'tildi: {(string.IsNullOrEmpty(oldYear) ? "—" : oldYear)} → {req.NewYear}" +
            $" (ko'tarildi: {promoted}, bitirdi: {graduated}, yopilgan obuna: {subscriptionsClosed}," +
            $" arxivlangan guruh: {archivedGroups}, yopilgan guruh a'zoligi: {closedMemberships})",
            after: new
            {
                req.NewYear, promoted, graduated, subscriptionsClosed,
                archivedGroups, closedMemberships,
            });

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
