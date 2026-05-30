using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;
using SchoolLms.Server.Models;
using SchoolLms.Server.Services;
using System.Security.Claims;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quvchi (oila) ilovasi API'si — `/api/student/*`. Asosiy foydalanuvchi `student` roli (o'z
/// ma'lumotini o'zi ko'radi), lekin `admin` roli ham `?studentId=...` so'rovi orqali istalgan
/// o'quvchining ma'lumotini ko'ra oladi (admin'ga alohida ko'rinish endpointlari qilmaslik uchun).
/// Mutatsiyalar (xabar yuborish, vazifa topshirish, fayl yuklash, shaxsiy sozlamani saqlash)
/// — faqat `student` rolida: admin boshqa odam nomidan amal qila olmaydi.
/// </summary>
[ApiController]
[Authorize(Roles = "student,parent,admin")]
[Route("api/student")]
public class StudentPortalController(
    AppDbContext db, ChatService chat, IWebHostEnvironment env, ReferenceCache refCache) : ControllerBase
{
    /// <summary>
    /// Maqsadli o'quvchini topadi.
    /// • student → o'z akkauntidan (UserId bo'yicha)
    /// • parent  → logini (email) telefon raqami sifatida Student.ParentPhone bilan taqqoslanadi
    /// • admin   → ?studentId=... query param orqali istalgan o'quvchi
    /// </summary>
    private async Task<Student?> TargetAsync(string? studentId)
    {
        if (User.IsInRole("admin"))
        {
            if (string.IsNullOrWhiteSpace(studentId)) return null;
            return await db.Students.FindAsync(studentId);
        }
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return null;

        if (User.IsInRole("parent"))
        {
            var user = await db.Users.FindAsync(uid);
            if (user is null) return null;
            var phone = NormalizePhone(user.Email);
            return await db.Students
                .Where(s => !s.IsArchived)
                .FirstOrDefaultAsync(s => NormalizePhone(s.ParentPhone) == phone);
        }

        return await db.Students.FirstOrDefaultAsync(s => s.UserId == uid);
    }

    /// <summary>Telefon raqamidan faqat raqamlarni qoldiradi (taqqoslash uchun).</summary>
    private static string NormalizePhone(string? p) =>
        new string((p ?? "").Where(char.IsDigit).ToArray());

    /// <summary>
    /// Mutatsiya (yozish) amallari uchun — FAQAT student rolida; admin impersonate qila olmaydi.
    /// </summary>
    private async Task<Student?> MeAsync()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return uid is null ? null : await db.Students.FirstOrDefaultAsync(s => s.UserId == uid);
    }

    /// <summary>O'quvchining sinf id'sini (nomidan) topadi.</summary>
    private async Task<string?> ClassIdOf(Student s) =>
        (await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName))?.Id;

    /// <summary>Admin uchun: studentId berilmagan bo'lsa 400 javobi.</summary>
    private ActionResult NeedStudentId() =>
        BadRequest(new { message = "Admin chaqiruvi uchun ?studentId=... kerak" });

    /// <summary>Yuklangan faylni `uploads/` ga saqlab, `/uploads/...` manzilini qaytaradi.</summary>
    private async Task<string> SaveUploadAsync(IFormFile file)
    {
        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(file.FileName)}";
        await using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored));
        await file.CopyToAsync(fs);
        return $"/uploads/{stored}";
    }

    /// <summary>
    /// Ota-ona (o'quvchi akkaunti orqali) taklif yoki shikoyat yuboradi. Faqat student rolida.
    /// Admin "Taklif va shikoyatlar" bo'limida ko'radi.
    /// </summary>
    [HttpPost("feedback")]
    [Authorize(Roles = "student,parent")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> SubmitFeedback(
        [FromForm] string type, [FromForm] string text, IFormFile? image)
    {
        // Student o'zi, parent farzandi nomidan yuboradi.
        var s = User.IsInRole("parent") ? await TargetAsync(null) : await MeAsync();
        if (s is null) return Unauthorized();
        var body = (text ?? "").Trim();
        if (body.Length == 0) return BadRequest(new { message = "Matn bo'sh" });
        if (image is not null && image.Length > 20_000_000)
            return BadRequest(new { message = "Rasm 20 MB dan katta" });

        var feedbackType = type == "complaint" ? "complaint" : "suggestion";
        var senderName = s.ParentFullName ?? "";
        db.Feedbacks.Add(new Feedback
        {
            StudentId = s.Id,
            ParentName = senderName,
            SenderRole = "parent",
            SenderName = senderName,
            Type = feedbackType,
            Text = body,
            ImageUrl = image is { Length: > 0 } ? await SaveUploadAsync(image) : null,
            CreatedAt = DateTime.UtcNow,
            Status = "new",
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Oshxona (kunlik menyu — butun maktab, faqat ko'rish) ----------

    /// <summary>Bitta kun oshxona menyusi (nonushta/tushlik/kechki).</summary>
    [HttpGet("canteen/{date}")]
    public async Task<ActionResult<DayMenuDto>> CanteenDay(string date)
    {
        var dishes = await db.Dishes.Where(d => d.Date == date).ToListAsync();
        return CanteenMenu.BuildDay(date, dishes);
    }

    /// <summary>Sana oralig'i bo'yicha kunlik menyular (start..end, ISO sanalar).</summary>
    [HttpGet("canteen")]
    public async Task<ActionResult<IEnumerable<DayMenuDto>>> CanteenRange(
        [FromQuery] string start, [FromQuery] string end)
    {
        if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
            return BadRequest(new { message = "start va end kerak" });
        var dishes = await db.Dishes
            .Where(d => string.Compare(d.Date, start) >= 0 && string.Compare(d.Date, end) <= 0)
            .ToListAsync();
        var result = new List<DayMenuDto>();
        var cur = start;
        // Cheksiz oraliqdan himoya — eng ko'pi 120 kun.
        for (var i = 0; string.CompareOrdinal(cur, end) <= 0 && i < 120; i++)
        {
            result.Add(CanteenMenu.BuildDay(cur, dishes.Where(d => d.Date == cur)));
            cur = ScheduleMath.AddDaysISO(cur, 1);
        }
        return result;
    }

    [HttpGet("me")]
    public async Task<ActionResult<StudentProfileDto>> Profile([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return new StudentProfileDto(
            s.Id, s.FullName, s.ClassName, s.BirthDate, s.Gender,
            s.ParentFullName, s.ParentPhone, s.EnrollmentDate);
    }

    /// <summary>Maktab meta'si (chorak/dars vaqtlari/sabablar + joriy chorak/hafta) —
    /// hammaga bir xil, `studentId` shart emas.</summary>
    [HttpGet("meta")]
    public async Task<ActionResult<PortalMetaDto>> Meta() => await refCache.MetaAsync();

    // ---------- Jadval (o'z sinfi) ----------

    [HttpGet("schedule")]
    public async Task<ActionResult<IEnumerable<StudentLessonDto>>> Schedule(
        [FromQuery] int? quarter, [FromQuery] int? week, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null) return new List<StudentLessonDto>();

        var (curQ, curW) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var lessons = await PortalSchedule.LessonsForWeekAsync(db, cls.Id, quarter ?? curQ, week ?? curW);
        // O'quvchi guruhiga mos darslar (SubGroup=0 yoki o'z guruhi).
        lessons = PortalSchedule.ForStudent(lessons, s.SubGroup).ToList();

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var teachers = await db.Teachers.ToDictionaryAsync(x => x.Id, x => x.FullName);
        var times = await db.LessonTimes.ToDictionaryAsync(x => x.Period);

        return lessons
            .OrderBy(l => l.Day).ThenBy(l => l.Period)
            .Select(l =>
            {
                times.TryGetValue(l.Period, out var lt);
                return new StudentLessonDto(
                    l.Day, l.Period, lt?.StartTime, lt?.EndTime,
                    l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""),
                    l.TeacherId, teachers.GetValueOrDefault(l.TeacherId, ""));
            })
            .ToList();
    }

    // ---------- Baholar va davomat (o'ziniki) ----------

    [HttpGet("grades")]
    public async Task<ActionResult<StudentReportDto>> Grades([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await StudentReportBuilder.BuildAsync(db, s);
    }

    // ---------- Uyga vazifa va dars mavzulari (o'z sinfi) ----------

    /// <summary>
    /// Sinf dars mavzulari + uyga vazifalari (chorak bo'yicha). Har qator uchun shu o'quvchining
    /// o'sha (sana + dars raqami + fan) jurnal yozuvi (baho / davomat sababi) ham qo'shib beriladi —
    /// shuning uchun bosh sahifa "bugungi baholar" va baholar ekrani alohida endpointsiz ishlaydi.
    /// </summary>
    [HttpGet("homework")]
    public async Task<ActionResult<IEnumerable<HomeworkItemDto>>> Homework(
        [FromQuery] int? quarter, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null) return new List<HomeworkItemDto>();

        var (curQ, _) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var q = quarter ?? curQ;

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var notes = await db.LessonNotes
            .Where(n => n.ClassId == cls.Id && n.Quarter == q &&
                        (n.SubGroup == 0 || n.SubGroup == s.SubGroup))
            .ToListAsync();

        // O'quvchining shu chorakdagi jurnal yozuvlari (baho/davomat sababi) — (Date, Period, SubjectId) bo'yicha kalit.
        var entries = await db.JournalEntries
            .Where(e => e.ClassId == cls.Id && e.Quarter == q && e.StudentId == s.Id)
            .ToListAsync();
        var entryMap = entries.ToDictionary(e => (e.Date, e.Period, e.SubjectId));

        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id);

        return notes
            .OrderBy(n => n.Date, StringComparer.Ordinal).ThenBy(n => n.Period)
            .Select(n =>
            {
                entryMap.TryGetValue((n.Date, n.Period, n.SubjectId), out var en);
                AbsenceReason? r = null;
                if (en?.ReasonId is not null) reasons.TryGetValue(en.ReasonId, out r);
                return new HomeworkItemDto(
                    n.Date, n.Period, n.SubjectId, subjects.GetValueOrDefault(n.SubjectId, ""),
                    n.Topic, n.Homework, n.Conducted,
                    en?.Grade, en?.ReasonId, r?.Name, r?.IsLate ?? false);
            })
            .ToList();
    }

    /// <summary>
    /// O'quvchi jurnali — chorak (ixtiyoriy hafta) bo'yicha sinfning haftalik jadvali asosida
    /// qatorlar (sana + dars raqami + fan + o'qituvchi + mavzu/uyga vazifa + shu o'quvchining bahosi/sababi).
    /// Hafta ko'rsatilmasa joriy hafta ishlatiladi.
    /// </summary>
    [HttpGet("journal")]
    public async Task<ActionResult<IEnumerable<StudentJournalRowDto>>> Journal(
        [FromQuery] int? quarter, [FromQuery] int? week, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null) return new List<StudentJournalRowDto>();

        var (curQ, curW) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var q = quarter ?? curQ;
        var w = week ?? curW;

        // Hafta sanalari (Du..Sha) — chorakka qisilgan.
        var qp = await db.Quarters.FirstOrDefaultAsync(x => x.Quarter == q);
        if (qp is null) return new List<StudentJournalRowDto>();
        var weeks = ScheduleMath.GetQuarterWeeks(qp.StartDate, qp.EndDate);
        var wk = weeks.FirstOrDefault(x => x.Week == w);
        if (wk is null) return new List<StudentJournalRowDto>();
        var monday = ScheduleMath.MondayOfISO(wk.StartISO);

        var lessons = await PortalSchedule.LessonsForWeekAsync(db, cls.Id, q, w);
        // Faqat o'quvchi guruhiga tegishli darslar (SubGroup=0 yoki o'z guruhi).
        lessons = PortalSchedule.ForStudent(lessons, s.SubGroup).ToList();
        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var teachers = await db.Teachers.ToDictionaryAsync(x => x.Id, x => x.FullName);
        var times = await db.LessonTimes.ToDictionaryAsync(x => x.Period);

        var notes = await db.LessonNotes
            .Where(n => n.ClassId == cls.Id && n.Quarter == q)
            .ToListAsync();
        var noteMap = notes.ToDictionary(n => (n.Date, n.Period, n.SubjectId, n.SubGroup));

        var entries = await db.JournalEntries
            .Where(e => e.ClassId == cls.Id && e.Quarter == q && e.StudentId == s.Id)
            .ToListAsync();
        var entryMap = entries.ToDictionary(e => (e.Date, e.Period, e.SubjectId));

        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id);

        var rows = new List<StudentJournalRowDto>();
        foreach (var l in lessons.OrderBy(x => x.Day).ThenBy(x => x.Period))
        {
            var date = ScheduleMath.AddDaysISO(monday, l.Day);
            // Hafta chorak chetiga qisilgan bo'lsa kunni tashqarida qoldiramiz.
            if (string.CompareOrdinal(date, wk.StartISO) < 0 ||
                string.CompareOrdinal(date, wk.EndISO) > 0) continue;

            noteMap.TryGetValue((date, l.Period, l.SubjectId, l.SubGroup), out var n);
            entryMap.TryGetValue((date, l.Period, l.SubjectId), out var en);
            AbsenceReason? r = null;
            if (en?.ReasonId is not null) reasons.TryGetValue(en.ReasonId, out r);
            times.TryGetValue(l.Period, out var lt);

            rows.Add(new StudentJournalRowDto(
                date, l.Period, q, w,
                lt?.StartTime, lt?.EndTime,
                l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""),
                l.TeacherId, teachers.GetValueOrDefault(l.TeacherId, ""),
                n?.Topic ?? "", n?.Homework, n?.Conducted ?? false,
                en?.Grade, en?.ReasonId, r?.Name, r?.IsLate ?? false));
        }
        return rows;
    }

    /// <summary>
    /// O'quvchi davomati — chorak bo'yicha umumlashtirilgan ko'rsatkichlar + kunlik
    /// davomatsizlik/kech qolish yozuvlari ro'yxati.
    /// </summary>
    [HttpGet("attendance")]
    public async Task<ActionResult<StudentAttendanceFullDto>> Attendance(
        [FromQuery] int? quarter, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null) return new StudentAttendanceFullDto(
            new StudentAttendanceDto(new(), new(), new(), new(), new()),
            new List<StudentAbsenceRowDto>());

        var report = await StudentReportBuilder.BuildAsync(db, s);

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var reasonRows = await db.AbsenceReasons.ToListAsync();
        var reasons = reasonRows.ToDictionary(r => r.Id);

        var rowsQuery = db.JournalEntries
            .Where(e => e.ClassId == cls.Id && e.StudentId == s.Id && e.ReasonId != null);
        if (quarter.HasValue) rowsQuery = rowsQuery.Where(e => e.Quarter == quarter.Value);

        var rows = (await rowsQuery.ToListAsync())
            .OrderByDescending(e => e.Date, StringComparer.Ordinal).ThenByDescending(e => e.Period)
            .Select(e =>
            {
                reasons.TryGetValue(e.ReasonId!, out var r);
                var name = r?.Name ?? "";
                return new StudentAbsenceRowDto(
                    e.Date, e.Period, e.Quarter,
                    e.SubjectId, subjects.GetValueOrDefault(e.SubjectId, ""),
                    e.ReasonId!, name, r?.IsLate ?? false,
                    name.ToLowerInvariant().Contains("kasal"));
            })
            .ToList();

        return new StudentAttendanceFullDto(report.Attendance, rows);
    }

    /// <summary>
    /// Bosh sahifa uchun YAGONA chaqiruv — profil + meta + bugungi darslar + bugungi baholar +
    /// bajarilmagan topshiriqlar soni + balans. Bir o'rinda hammasi (Flutter Dashboard ekraniga mos).
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<StudentDashboardDto>> Dashboard([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        var profile = new StudentProfileDto(
            s.Id, s.FullName, s.ClassName, s.BirthDate, s.Gender,
            s.ParentFullName, s.ParentPhone, s.EnrollmentDate);
        var meta = await refCache.MetaAsync();

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);

        // Bugungi darslar (joriy chorak + joriy hafta, kun = today.DayOfWeek 0=Du..5=Sha).
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var apiDay = ((int)DateTime.Now.DayOfWeek + 6) % 7; // C# Sun=0..Sat=6 → Mon=0..Sun=6 (sun=6=ignore)

        var todayLessons = new List<StudentLessonDto>();
        var todayGrades = new List<HomeworkItemDto>();
        if (cls is not null)
        {
            var lessons = await PortalSchedule.LessonsForWeekAsync(db, cls.Id, meta.CurrentQuarter, meta.CurrentWeek);
            // O'quvchi guruhi bo'yicha filtr.
            lessons = PortalSchedule.ForStudent(lessons, s.SubGroup).ToList();
            var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
            var teachers = await db.Teachers.ToDictionaryAsync(x => x.Id, x => x.FullName);
            var times = await db.LessonTimes.ToDictionaryAsync(x => x.Period);

            todayLessons = lessons
                .Where(l => l.Day == apiDay)
                .OrderBy(l => l.Period)
                .Select(l =>
                {
                    times.TryGetValue(l.Period, out var lt);
                    return new StudentLessonDto(
                        l.Day, l.Period, lt?.StartTime, lt?.EndTime,
                        l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""),
                        l.TeacherId, teachers.GetValueOrDefault(l.TeacherId, ""), l.SubGroup);
                })
                .ToList();

            // Bugungi baholar — shu o'quvchining bugungi jurnal yozuvlari (Grade != null).
            var entries = await db.JournalEntries
                .Where(e => e.ClassId == cls.Id && e.StudentId == s.Id && e.Date == today && e.Grade != null)
                .ToListAsync();
            var notes = await db.LessonNotes
                .Where(n => n.ClassId == cls.Id && n.Date == today)
                .ToListAsync();
            var noteMap = notes.ToDictionary(n => (n.Date, n.Period, n.SubjectId));
            var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id);

            todayGrades = entries
                .OrderBy(e => e.Period)
                .Select(e =>
                {
                    noteMap.TryGetValue((e.Date, e.Period, e.SubjectId), out var n);
                    AbsenceReason? r = null;
                    if (e.ReasonId is not null) reasons.TryGetValue(e.ReasonId, out r);
                    return new HomeworkItemDto(
                        e.Date, e.Period, e.SubjectId, subjects.GetValueOrDefault(e.SubjectId, ""),
                        n?.Topic ?? "", n?.Homework, n?.Conducted ?? true,
                        e.Grade, e.ReasonId, r?.Name, r?.IsLate ?? false);
                })
                .ToList();
        }

        // Bajarilmagan topshiriqlar soni.
        int pending = 0;
        var classId = cls?.Id;
        if (classId is not null)
        {
            var assignments = await AssignmentService.ListForStudentAsync(db, classId, s.Id);
            pending = assignments.Count(a => !a.Completed);
        }

        var monthlyFee = cls?.MonthlyFee ?? 0m;

        return new StudentDashboardDto(
            profile, meta, todayLessons, todayGrades, pending, s.Balance, monthlyFee);
    }

    // ---------- Foydalanuvchi sozlamalari (til, tema, bildirishnoma) ----------

    /// <summary>Foydalanuvchi sozlamasini qaytaradi. Student — o'ziniki; admin — `?studentId` o'quvchining
    /// foydalanuvchisi (o'quvchiga akkaunt biriktirilmagan bo'lsa default qaytadi).</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings([FromQuery] string? studentId)
    {
        string? targetUid;
        if (User.IsInRole("admin"))
        {
            if (string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
            var st = await db.Students.FindAsync(studentId);
            if (st is null) return NotFound();
            targetUid = st.UserId;
        }
        else
        {
            targetUid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
        if (string.IsNullOrWhiteSpace(targetUid))
            return new UserSettingsDto("uz", "system", true);
        var us = await db.UserSettings.FindAsync(targetUid);
        return us is null
            ? new UserSettingsDto("uz", "system", true)
            : new UserSettingsDto(us.Language, us.Theme, us.NotificationsEnabled);
    }

    /// <summary>Sozlamani yangilash (qator yo'q bo'lsa yaratiladi). Faqat student rolida — o'ziniki.</summary>
    [HttpPut("settings")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult<UserSettingsDto>> SaveSettings(SaveUserSettingsRequest req)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();
        var st = await db.UserSettings.FindAsync(uid);
        if (st is null)
        {
            st = new Models.UserSettings { UserId = uid };
            db.UserSettings.Add(st);
        }
        if (!string.IsNullOrWhiteSpace(req.Language)) st.Language = req.Language!.Trim();
        if (!string.IsNullOrWhiteSpace(req.Theme)) st.Theme = req.Theme!.Trim();
        if (req.NotificationsEnabled.HasValue) st.NotificationsEnabled = req.NotificationsEnabled.Value;
        st.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new UserSettingsDto(st.Language, st.Theme, st.NotificationsEnabled);
    }

    // ---------- Joylashuv (GPS) ----------

    /// <summary>
    /// O'quvchi o'z uy joylashuvini yangilash — mobil ilova GPS dan keladi (latitude/longitude,
    /// ixtiyoriy address). Faqat student rolida (admin impersonate emas).
    /// </summary>
    [HttpPut("location")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult> UpdateLocation(UpdateLocationRequest req)
    {
        var s = await MeAsync();
        if (s is null) return NotFound();
        if (req.Latitude is < -90 or > 90 || req.Longitude is < -180 or > 180)
            return BadRequest(new { message = "Koordinatalar noto'g'ri" });
        s.Latitude = req.Latitude;
        s.Longitude = req.Longitude;
        s.LocationAddress = (req.Address ?? "").Trim();
        s.LocationUpdatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ---------- Push bildirishnoma (qurilma tokeni) ----------

    /// <summary>Push (FCM/APNs/Web) qurilma tokenini ro'yxatdan o'tkazadi (yangi bo'lsa qo'shadi,
    /// mavjud bo'lsa LastSeenAt yangilanadi). Token boshqa foydalanuvchiga bog'langan bo'lsa
    /// joriy foydalanuvchiga ko'chiriladi (qurilma boshqa akkauntga kirgan deb hisoblanadi).
    /// Faqat student rolida — token tokendagi foydalanuvchiga bog'lanadi.</summary>
    [HttpPost("notifications/register")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult> RegisterDevice(RegisterDeviceRequest req)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();
        var token = req.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token)) return BadRequest(new { message = "Token bo'sh" });
        var platform = string.IsNullOrWhiteSpace(req.Platform) ? "android" : req.Platform!.Trim().ToLowerInvariant();

        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
        if (existing is null)
        {
            db.DeviceTokens.Add(new Models.DeviceToken
            {
                UserId = uid,
                Token = token,
                Platform = platform,
            });
        }
        else
        {
            existing.UserId = uid;
            existing.Platform = platform;
            existing.LastSeenAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Qurilma tokenini o'chiradi (logout/disable). Topilmasa ham 200 qaytaradi.</summary>
    [HttpDelete("notifications/register")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult> UnregisterDevice([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest(new { message = "Token bo'sh" });
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var d = await db.DeviceTokens.FirstOrDefaultAsync(x => x.Token == token && x.UserId == uid);
        if (d is not null)
        {
            db.DeviceTokens.Remove(d);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    // ---------- To'lovlar (o'ziniki) ----------

    [HttpGet("finance")]
    public async Task<ActionResult<StudentLedgerDto>> Finance([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await StudentLedger.BuildAsync(db, s);
    }

    // ---------- Guruh chati (o'z sinfi) ----------

    [HttpGet("chat")]
    public async Task<ActionResult<IEnumerable<ChatMessageDto>>> Chat(
        [FromQuery] string? since, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        if (string.IsNullOrEmpty(s.ClassName)) return new List<ChatMessageDto>();
        return await chat.GetMessagesAsync(s.ClassName, ChatService.ParseSince(since));
    }

    /// <summary>Chatga xabar yuborish — faqat student rolida (admin o'zining /api/admin/messages
    /// orqali yozadi; bu yerda admin impersonate qila olmaydi).</summary>
    [HttpPost("chat")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult<ChatMessageDto>> SendChat(SendChatRequest req)
    {
        var s = await MeAsync();
        if (s is null) return NotFound();
        if (string.IsNullOrEmpty(s.ClassName)) return BadRequest(new { message = "Sinf biriktirilmagan" });
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var dto = await chat.PostAsync(s.ClassName, uid, req.Text);
        return dto is null ? BadRequest(new { message = "Xabar bo'sh" }) : dto;
    }

    // ---------- Topshiriqlar / testlar (o'z sinfi) ----------

    /// <summary>O'z sinfiga berilgan topshiriqlar — har birida o'z holati (bajardi/ball).</summary>
    [HttpGet("assignments")]
    public async Task<ActionResult<IEnumerable<StudentAssignmentDto>>> Assignments(
        [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var classId = await ClassIdOf(s);
        if (classId is null) return new List<StudentAssignmentDto>();
        return await AssignmentService.ListForStudentAsync(db, classId, s.Id);
    }

    /// <summary>
    /// "Topshiriq ballari" — o'quvchiga berilgan topshiriqlar va uning har biridagi bali (+ yig'ma).
    /// Ota-ona ham shu orqali farzandi ballarini ko'radi.
    /// </summary>
    [HttpGet("assignment-scores")]
    public async Task<ActionResult<StudentAssignmentScoresDto>> AssignmentScores([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var classId = await ClassIdOf(s);
        if (classId is null) return new StudentAssignmentScoresDto(0, 0, 0, 0, new());
        return await AssignmentService.ScoresForStudentAsync(db, classId, s.Id);
    }

    /// <summary>Topshiriq tafsiloti (test bo'lsa — to'g'ri javobsiz savollar).</summary>
    [HttpGet("assignments/{id}")]
    public async Task<ActionResult<StudentAssignmentDetailDto>> Assignment(
        string id, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var classId = await ClassIdOf(s);
        if (classId is null) return NotFound();
        var dto = await AssignmentService.GetForStudentAsync(db, id, classId, s.Id);
        return dto is null ? NotFound() : dto;
    }

    /// <summary>Topshiriqni topshirish — faqat student rolida (admin o'zi o'rniga topshira olmaydi).</summary>
    [HttpPost("assignments/{id}/submit")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult<SubmitResultDto>> Submit(string id, SubmitAssignmentRequest req)
    {
        var s = await MeAsync();
        if (s is null) return NotFound();
        var classId = await ClassIdOf(s);
        if (classId is null) return NotFound();
        var res = await AssignmentService.SubmitAsync(db, id, classId, s.Id, req);
        return res is null ? NotFound() : res;
    }

    /// <summary>O'quvchi javobi sifatida fayl yuklash (rasm/PDF/video, maks ~20MB).
    /// Faqat student rolida — admin yuklamaydi.</summary>
    [HttpPost("uploads")]
    [Authorize(Roles = "student")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<UploadedFileDto>> Upload(IFormFile file)
    {
        var s = await MeAsync();
        if (s is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest(new { message = "Fayl bo'sh" });
        if (file.Length > 20_000_000) return BadRequest(new { message = "Fayl 20 MB dan katta" });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(file.FileName)}";
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        return new UploadedFileDto(file.FileName, $"/uploads/{stored}", file.Length, file.ContentType ?? "");
    }

    /* ─── Fan progresi (dars o'tilishiga qarab — LMS'siz) ──────
       Progress = o'tilgan darslar / chorakdagi reja darslar. O'qituvchi jurnalda
       "dars o'tildi" deb belgilashidan kelib chiqadi. */

    /// <summary>Barcha fanlar bo'yicha umumiy + har bir fan progresi (joriy/berilgan chorak).</summary>
    [HttpGet("subjects-progress")]
    public async Task<ActionResult<StudentSubjectsProgressDto>> SubjectsProgress(
        [FromQuery] int? quarter, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var (curQ, _) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var q = quarter ?? curQ;
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null) return new StudentSubjectsProgressDto(q, 0, 0, 0, new());
        return await SubjectProgressService.ForStudentAsync(db, cls.Id, q, s.SubGroup);
    }

    /// <summary>Bitta fanga kirilganda — darslar ro'yxati (yashil = o'tilgan, qizil = hali yo'q).</summary>
    [HttpGet("subjects-progress/{subjectId}")]
    public async Task<ActionResult<SubjectProgressDetailDto>> SubjectProgressDetail(
        string subjectId, [FromQuery] int? quarter, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null) return NotFound();
        var (curQ, _) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var dto = await SubjectProgressService.ForStudentSubjectAsync(
            db, cls.Id, quarter ?? curQ, s.SubGroup, subjectId);
        return dto is null ? NotFound() : dto;
    }

    /* ─── LMS (Ta'lim) ──────────────────────────────────────── */

    /// <summary>O'quvchining sinfi uchun LMS fanlar ro'yxati (progress bilan).</summary>
    [HttpGet("lms/subjects")]
    public async Task<ActionResult<IEnumerable<StudentLmsSubjectDto>>> LmsSubjects([FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return User.IsInRole("admin") ? NeedStudentId() : NotFound();

        // O'quvchining sinf id'sini topamiz
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null) return Ok(Array.Empty<StudentLmsSubjectDto>());

        var subjects = await db.LmsSubjects
            .Include(x => x.Topics)
            .Where(x => x.ClassId == cls.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        var topicIds = subjects.SelectMany(x => x.Topics.Select(t => t.Id)).ToList();
        var completed = (await db.LmsProgresses
            .Where(p => p.StudentId == s.Id && topicIds.Contains(p.TopicId))
            .Select(p => p.TopicId).ToListAsync()).ToHashSet();

        return subjects.Select(x => new StudentLmsSubjectDto(
            x.Id, x.Title, x.Description, x.UnlockMode, x.BatchSize,
            x.Topics.Count, x.Topics.Count(t => completed.Contains(t.Id)))).ToList();
    }

    /// <summary>Fanning mavzulari — ochilish tartibi va o'quvchi progressi bilan.</summary>
    [HttpGet("lms/subjects/{subjectId}/topics")]
    public async Task<ActionResult<IEnumerable<StudentLmsTopicDto>>> LmsTopics(
        string subjectId, [FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return User.IsInRole("admin") ? NeedStudentId() : NotFound();

        var subject = await db.LmsSubjects
            .Include(x => x.Topics).ThenInclude(t => t.Materials)
            .FirstOrDefaultAsync(x => x.Id == subjectId);
        if (subject is null) return NotFound();

        // Sinfga tegishli ekanini tekshiramiz
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null || subject.ClassId != cls.Id) return Forbid();

        var topics = subject.Topics.OrderBy(t => t.Order).ToList();
        var completedIds = (await db.LmsProgresses
            .Where(p => p.StudentId == s.Id && topics.Select(t => t.Id).Contains(p.TopicId))
            .Select(p => p.TopicId).ToListAsync()).ToHashSet();

        var result = new List<StudentLmsTopicDto>();
        for (var i = 0; i < topics.Count; i++)
        {
            var t = topics[i];
            var unlocked = subject.UnlockMode switch
            {
                "sequential" => i == 0 || completedIds.Contains(topics[i - 1].Id),
                "batch" => i < subject.BatchSize ||
                                completedIds.Contains(topics[Math.Max(0, i - subject.BatchSize)].Id),
                _ => true, // "all"
            };
            // Qulflangan mavzularda kontent (video/matn/material) ko'rsatilmaydi
            result.Add(new StudentLmsTopicDto(
                t.Id, t.SubjectId, t.Title, t.Description,
                unlocked ? t.VideoUrl : null,
                unlocked ? t.TextContent : null,
                t.Order,
                unlocked
                    ? t.Materials.Select(m => new LmsMaterialRowDto(m.Id, m.Name, m.Url, m.Size, m.ContentType)).ToList()
                    : new List<LmsMaterialRowDto>(),
                unlocked, completedIds.Contains(t.Id)));
        }
        return result;
    }

    /// <summary>
    /// Bitta mavzu tafsiloti — faqat ochiq (unlocked) mavzu uchun. Qulflangan bo'lsa 403.
    /// Video, matn va materiallar shu endpoint orqali olinadi.
    /// </summary>
    [HttpGet("lms/topics/{topicId}")]
    public async Task<ActionResult<StudentLmsTopicDto>> LmsTopicDetail(
        string topicId, [FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return User.IsInRole("admin") ? NeedStudentId() : NotFound();

        var topic = await db.LmsTopics
            .Include(t => t.Materials)
            .Include(t => t.Subject)
            .FirstOrDefaultAsync(t => t.Id == topicId);
        if (topic is null) return NotFound();

        // Sinfga tegishli ekanini tekshiramiz
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null || topic.Subject.ClassId != cls.Id) return Forbid();

        // Ochilish tartibini hisoblaymiz
        var subject = topic.Subject;
        var allTopics = await db.LmsTopics
            .Where(t => t.SubjectId == subject.Id).OrderBy(t => t.Order).ToListAsync();
        var completedIds = (await db.LmsProgresses
            .Where(p => p.StudentId == s.Id && allTopics.Select(t => t.Id).Contains(p.TopicId))
            .Select(p => p.TopicId).ToListAsync()).ToHashSet();

        var idx = allTopics.FindIndex(t => t.Id == topicId);
        var unlocked = subject.UnlockMode switch
        {
            "sequential" => idx == 0 || completedIds.Contains(allTopics[idx - 1].Id),
            "batch" => idx < subject.BatchSize ||
                            completedIds.Contains(allTopics[Math.Max(0, idx - subject.BatchSize)].Id),
            _ => true,
        };

        if (!unlocked)
            return StatusCode(403, new { message = "Bu mavzu hali ochilmagan" });

        return new StudentLmsTopicDto(
            topic.Id, topic.SubjectId, topic.Title, topic.Description,
            topic.VideoUrl, topic.TextContent, topic.Order,
            topic.Materials.Select(m => new LmsMaterialRowDto(m.Id, m.Name, m.Url, m.Size, m.ContentType)).ToList(),
            true, completedIds.Contains(topic.Id));
    }

    /// <summary>Mavzuni tugallangan deb belgilash — ochilish mantig'i uchun zarur. Parent ham chaqira oladi.</summary>
    [HttpPost("lms/topics/{topicId}/complete")]
    public async Task<IActionResult> CompleteLmsTopic(string topicId)
    {
        // Student o'zi yoki parent farzandi nomidan belgilaydi
        var s = User.IsInRole("parent") ? await TargetAsync(null) : await MeAsync();
        if (s is null) return Forbid();
        if (!await db.LmsProgresses.AnyAsync(p => p.StudentId == s.Id && p.TopicId == topicId))
        {
            db.LmsProgresses.Add(new LmsProgress { StudentId = s.Id, TopicId = topicId });
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}
