using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;
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
    AppDbContext db, ChatService chat, IWebHostEnvironment env, ReferenceCache refCache,
    TelegramService telegram, FcmService fcm) : ControllerBase
{
    /// <summary>Berilgan foydalanuvchining qurilmalariga push yuboradi (fire-and-forget).</summary>
    private Task PushToUserAsync(string userId, string title, string body) =>
        AppPush.ToUserAsync(db, fcm, userId, title, body);

    private static PickupRequestDto PickupDto(PickupRequest p) => PickupService.ToDto(p);

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
            if (phone.Length == 0) return null;
            // `NormalizePhone` SQL'ga o'girilmaydi — ilgari u `Where` ichida turardi va
            // ota-onaning HAR bir so'rovi 500 bilan yiqilardi (EF "could not be translated").
            // Shuning uchun faqat (id, telefon) juftliklari olinadi va solishtirish xotirada.
            var candidates = await db.Students.AsNoTracking()
                .Where(s => !s.IsArchived && s.ParentPhone != "")
                .OrderBy(s => s.FullName)
                .Select(s => new { s.Id, s.ParentPhone })
                .ToListAsync();
            var match = candidates.FirstOrDefault(s => NormalizePhone(s.ParentPhone) == phone);
            return match is null ? null : await db.Students.FindAsync(match.Id);
        }

        return await db.Students.FirstOrDefaultAsync(s => s.UserId == uid);
    }

    /// <summary>Telefon raqamidan faqat raqamlarni qoldiradi (taqqoslash uchun).</summary>
    private static string NormalizePhone(string? p) =>
        new string((p ?? "").Where(char.IsDigit).ToArray());

    // =====================================================================
    //  §5.5 — `show_learning_progress_in_parent_dashboard`
    //
    //  Bayroq FAQAT OTA-ONAGA tegishli. O'quvchining o'zi va admin shu
    //  endpointlardan foydalanadi (bu controller uchtasiga ham xizmat qiladi),
    //  va bolaning o'z bahosini undan yashirish §5.5 da ham, mijozning
    //  so'rovida ham yo'q: bayroqning maqsadi "yomon chorakda ota-onalar
    //  kabinetini vaqtincha yopish".
    //
    //  Tekshiruv SERVERDA: bayroqni faqat ekranda yashirish — yolg'on.
    //  Mini App unga qo'shimcha (GET /api/tg/me dagi `showLearningProgress`)
    //  orqali ekranni ham yopadi, lekin qulf shu yerda.
    // =====================================================================

    /// <summary>Ota-onaga o'zlashtirish yopilganda qaytariladigan matn.</summary>
    private const string ProgressHiddenMessage =
        "Maktab ota-onalar uchun o'zlashtirish ma'lumotini vaqtincha yopgan.";

    /// <summary>
    /// Shu so'rovda baholar yashirinishi kerakmi. Faqat <c>parent</c> roli uchun
    /// va faqat bayroq o'chirilgan bo'lsa.
    /// </summary>
    private async Task<bool> ProgressHiddenAsync(CancellationToken ct = default)
    {
        if (!User.IsInRole("parent")) return false;
        var meta = await db.SchoolMeta.AsNoTracking()
            .Select(m => new { m.ShowLearningProgressInParentDashboard })
            .FirstOrDefaultAsync(ct);
        // Qator yo'q bo'lsa — entity sukuti (`true`), ya'ni ko'rinadi.
        return !(meta?.ShowLearningProgressInParentDashboard ?? true);
    }

    private ObjectResult ProgressHidden() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = ProgressHiddenMessage });

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
        var stored = Application.Services.UploadGuard.SafeName(file);
        await using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored));
        await file.CopyToAsync(fs);
        return $"/uploads/{stored}";
    }

    /// <summary>
    /// Ilova (ota-ona/o'quvchi) uchun Telegram bot holati va shu o'quvchi botda ro'yxatdan o'tganmi.
    /// Ilova birinchi kirilganda — agar <c>configured=true</c> va <c>registered=false</c> bo'lsa —
    /// foydalanuvchini botga (<c>deepLink</c>) yo'naltirib, e'lon olish uchun ro'yxatdan o'tishni taklif qiladi.
    /// </summary>
    [HttpGet("telegram")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult<object>> TelegramInfo([FromQuery] string? studentId)
    {
        var s = User.IsInRole("parent") ? await TargetAsync(null) : await MeAsync();
        if (s is null) return NotFound();
        var registered = await db.TelegramRegistrations.AnyAsync(r => r.StudentId == s.Id);
        var username = telegram.BotUsername ?? "";
        return Ok(new
        {
            configured = telegram.IsConfigured,
            botUsername = username,
            botName = telegram.BotName ?? "",
            deepLink = string.IsNullOrEmpty(username) ? "" : $"https://t.me/{username}",
            registered,
        });
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
        if (image is not null && Application.Services.UploadGuard.Validate(image) is { } imgError)
            return BadRequest(new { message = imgError });

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
            CreatedAt = AppClock.Now,
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
            s.ParentFullName, s.ParentPhone, s.EnrollmentDate,
            s.BirthCertificateUrl, s.ParentPassportUrl);
    }

    /// <summary>
    /// O'quvchining TO'LIQ "shaxsiy daftari" — admin ko'radigan detal sahifasi bilan AYNAN bir xil
    /// (<see cref="StudentProfileBuilder"/>): profil + shaxsiy ma'lumot (manzil, chegirma, guruh,
    /// hujjatlar, balans), fan×chorak baholar va o'rtacha, davomat (qoldirgan/kech + sabablar),
    /// intizomiy ball va tarixi, topshiriqlar ballari, OYLIK BAHOLASH (turlar×oy), uy vazifa/xulq
    /// jamlamasi va oylik trend — bularning bari bitta javobda.
    /// student — o'ziniki; parent — farzandiniki; admin — <c>?studentId=...</c>.
    /// </summary>
    [HttpGet("notebook")]
    public async Task<ActionResult<StudentNotebookDto>> Notebook([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        if (await ProgressHiddenAsync()) return ProgressHidden();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await StudentProfileBuilder.BuildAsync(db, s);
    }

    /// <summary>Maktab meta'si (chorak/dars vaqtlari/sabablar + joriy chorak/hafta) —
    /// hammaga bir xil, `studentId` shart emas.</summary>
    [HttpGet("meta")]
    public async Task<ActionResult<PortalMetaDto>> Meta() => await refCache.MetaAsync();

    /// <summary>Joriy maktab nomi — ilova brendingi/sarlavhasi uchun.</summary>
    [HttpGet("school")]
    public async Task<ActionResult<SchoolNameDto>> School()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        return new SchoolNameDto(m?.Name ?? "");
    }

    /// <summary>Bayram/dam olish kunlari — bu sanalarda dars bo'lmaydi (jadvalda "Bayram" deb ko'rsating).</summary>
    [HttpGet("holidays")]
    public async Task<ActionResult<IEnumerable<HolidayDto>>> Holidays() =>
        await db.Holidays.OrderBy(h => h.Date).Select(h => new HolidayDto(h.Date, h.Name)).ToListAsync();

    /// <summary>Web (PWA) push uchun Firebase web config + VAPID — brauzer FCM token olishi uchun.
    /// Bitta Firebase loyiha barcha ilovalar uchun (teacher/student) ishlaydi.</summary>
    [HttpGet("push-config")]
    public async Task<ActionResult<PushClientConfigDto>> PushConfig()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        var sa = m?.FcmServiceAccountJson ?? "";
        var web = (m?.FcmWebConfigJson ?? "").Trim();
        var vapid = (m?.FcmVapidKey ?? "").Trim();
        var enabled = FcmService.IsConfigured(sa) && web.Length > 0 && vapid.Length > 0;
        return new PushClientConfigDto(enabled, web, vapid);
    }

    // ---------- Farzandni olib ketish (pickup) ----------

    /// <summary>
    /// "Farzandimni olishga keldim" — pickup so'rovi yaratadi va sinf rahbariga push yuboradi.
    /// Allaqachon kutilayotgan (pending) so'rov bo'lsa — o'shani qaytaradi (takror yaratmaydi).
    /// </summary>
    [HttpPost("pickup")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult<PickupRequestDto>> CreatePickup(CreatePickupRequest req)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var s = await ResolveOwnStudentAsync(req.StudentId, uid);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });

        // Pickup KUNLIK va takror bosishga chidamli — mantiq `PickupService` da,
        // chunki Telegram Mini App (`/api/tg/parent/...`) ham aynan shu yerdan yuradi.
        var pr = await PickupService.EnsureTodayAsync(db, s, uid ?? s.UserId ?? "");
        await PickupService.NotifyHomeroomAsync(db, fcm, s);
        return PickupDto(pr);
    }

    /// <summary>Farzandning oxirgi pickup so'rovi holati (yo'q bo'lsa — null).</summary>
    [HttpGet("pickup")]
    public async Task<ActionResult<PickupRequestDto?>> GetPickup([FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return Ok((PickupRequestDto?)null);
        // Faqat bugungi so'rov — har kuni holatni qaytadan boshlaymiz.
        var pr = await PickupService.TodayAsync(db, s.Id);
        return Ok(pr is null ? null : PickupDto(pr));
    }

    /// <summary>So'rovchining o'z farzandini topadi (multi-farzand: studentId bilan, egalik tekshiriladi).</summary>
    private async Task<Student?> ResolveOwnStudentAsync(string? studentId, string? uid)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return await TargetAsync(null);
        var s = await db.Students.FindAsync(studentId);
        if (s is null) return null;
        if (User.IsInRole("student")) return s.UserId == uid ? s : null;
        if (User.IsInRole("parent"))
        {
            var user = uid is null ? null : await db.Users.FindAsync(uid);
            return NormalizePhone(user?.Email) == NormalizePhone(s.ParentPhone) ? s : null;
        }
        return s;
    }

    /// <summary>
    /// O'quvchining intizomiy balli: qoldi (100 dan boshlanadi) + rag'bat(+)/jazo(−) + tarix.
    /// Tarix qo'lda kiritilgan ballar va jurnal davomati (sabab balli != 0) yozuvlaridan iborat.
    /// </summary>
    [HttpGet("discipline")]
    public async Task<ActionResult<StudentDisciplineDto>> Discipline([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        var manual = await db.DisciplinePoints.Where(p => p.StudentId == s.Id).ToListAsync();
        var drNames = await db.DisciplineReasons.ToDictionaryAsync(r => r.Id, r => r.Name);
        var absReasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => new { r.Name, r.Points });

        var items = manual.Select(p => new DisciplinePointDto(
            p.Id, p.StudentId,
            string.IsNullOrEmpty(p.ReasonName) ? drNames.GetValueOrDefault(p.ReasonId, "—") : p.ReasonName,
            p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual")).ToList();

        var journal = await db.JournalEntries
            .Where(e => e.StudentId == s.Id && e.ReasonId != null).ToListAsync();
        foreach (var e in journal)
        {
            if (e.ReasonId is null || !absReasons.TryGetValue(e.ReasonId, out var r) || r.Points == 0) continue;
            items.Add(new DisciplinePointDto(e.Id, s.Id, r.Name, r.Points, "Jurnal davomati", e.Date, "", "attendance"));
        }

        var plus = items.Where(i => i.Points > 0).Sum(i => i.Points);
        var minus = items.Where(i => i.Points < 0).Sum(i => -i.Points);
        var ordered = items.OrderByDescending(i => i.CreatedAt, StringComparer.Ordinal).ToList();
        return new StudentDisciplineDto(100 + plus - minus, plus, minus, ordered);
    }

    // ---------- Jadval (o'z sinfi + guruhlari) ----------

    /// <summary>
    /// O'quvchining haftalik jadvali. G-18: manba endi <see cref="PupilTimetable"/> —
    /// sinf darslari va FAOL GURUH darslari bitta ro'yxatda, kun va dars raqami
    /// bo'yicha tartiblangan. O'chirgich o'chiq bo'lsa ro'yxatda faqat sinf darslari
    /// bo'ladi, ya'ni bugungi javobning aynan o'zi.
    /// </summary>
    [HttpGet("schedule")]
    public async Task<ActionResult<IEnumerable<StudentLessonDto>>> Schedule(
        [FromQuery] int? quarter, [FromQuery] int? week, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        var (curQ, curW) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var lessons = await PupilTimetable.ForWeekAsync(db, s, quarter ?? curQ, week ?? curW);
        if (lessons.Count == 0) return new List<StudentLessonDto>();

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var teachers = await db.Teachers.ToDictionaryAsync(x => x.Id, x => x.FullName);
        var times = await db.LessonTimes.ToDictionaryAsync(x => x.Period);

        return lessons
            .Select(l =>
            {
                times.TryGetValue(l.Period, out var lt);
                return new StudentLessonDto(
                    l.Day, l.Period, lt?.StartTime, lt?.EndTime,
                    l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""),
                    l.TeacherId, teachers.GetValueOrDefault(l.TeacherId, ""),
                    // `subGroup` bu endpointda bugun ham 0 bo'lib qaytadi — o'zgartirmaymiz.
                    OwnerKind: l.Owner.Kind, OwnerName: l.Owner.IsGroup ? l.Owner.Name : null);
            })
            .ToList();
    }

    // ---------- Baholar va davomat (o'ziniki) ----------

    [HttpGet("grades")]
    public async Task<ActionResult<StudentReportDto>> Grades([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        if (await ProgressHiddenAsync()) return ProgressHidden();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await StudentReportBuilder.BuildAsync(db, s);
    }

    /// <summary>
    /// O'quvchi reytingi (admin "Reyting"i bilan bir xil hisob — o'rtacha baho bo'yicha):
    /// <b>o'z sinfini to'liq</b>, <b>maktab bo'yicha esa faqat TOP 15</b> ko'radi.
    /// O'z qatori `MeStudentId` bilan, maktab o'rni (top 15 dan tashqarida bo'lsa ham) `MeSchoolRank` bilan beriladi.
    /// Parent farzandi nomidan; admin uchun `?studentId=` shart.
    /// </summary>
    [HttpGet("rating")]
    public async Task<ActionResult<PortalRatingDto>> Rating([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        // Reyting o'rtacha BAHO ustidan qurilgan — ya'ni o'zlashtirish.
        if (await ProgressHiddenAsync()) return ProgressHidden();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        // O'rtacha baho bo'yicha kamayish tartibida — adminnikidek (index = o'rin).
        var school = (await RatingService.SchoolAsync(db))
            .OrderByDescending(r => r.Average)
            .ToList();

        static PortalRatingRowDto Map(StudentRatingRowDto r, int i) =>
            new(i + 1, r.Student.Id, r.Student.FullName, r.ClassName, r.Average, r.Attendance);

        var classRows = school
            .Where(r => r.ClassName == s.ClassName)
            .Select(Map).ToList();                       // o'z sinfi — to'liq
        var schoolRows = school.Take(15).Select(Map).ToList(); // maktab — top 15

        var meIdx = school.FindIndex(r => r.Student.Id == s.Id);
        int? meSchoolRank = meIdx >= 0 ? meIdx + 1 : null;

        return new PortalRatingDto(s.Id, classRows, schoolRows, meSchoolRank, school.Count);
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
        // G-18: uy vazifa va mavzular endi sinf darslaridan ham, FAOL GURUH
        // darslaridan ham keladi. O'chirgich o'chiq bo'lsa ega faqat sinf.
        var owners = await LessonRoster.OwnersOfAsync(db, s);
        if (owners.Count == 0) return new List<HomeworkItemDto>();
        var ownerIds = owners.Select(o => o.Id).ToList();
        var ownerById = owners.ToDictionary(o => o.Id, StringComparer.Ordinal);

        var (curQ, _) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var q = quarter ?? curQ;

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        // Bo'linish (SubGroup) filtri faqat SINF izohlariga: guruh darsida
        // bo'linish yo'q, u a'zolarning hammasiga tegishli.
        var notes = (await db.LessonNotes
                .Where(n => ownerIds.Contains(n.ClassId) && n.Quarter == q)
                .ToListAsync())
            .Where(n => n.OwnerKind == LessonOwnerKind.Group
                        || n.SubGroup == 0 || n.SubGroup == s.SubGroup)
            .ToList();

        // O'quvchining shu chorakdagi jurnal yozuvlari (baho/davomat sababi) —
        // (ega, Date, Period, SubjectId) bo'yicha kalit.
        var entries = await db.JournalEntries
            .Where(e => ownerIds.Contains(e.ClassId) && e.Quarter == q && e.StudentId == s.Id)
            .ToListAsync();
        var entryMap = entries
            .GroupBy(e => (e.ClassId, e.Date, e.Period, e.SubjectId))
            .ToDictionary(g => g.Key, g => g.First());

        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id);

        // §5.5 — bu yerda butun javob YOPILMAYDI: uyga vazifa va mavzu ota-onaga kerak,
        // yopiladigani faqat BAHO. Shuning uchun qator qoladi, `Grade` bo'shaydi.
        var hideGrades = await ProgressHiddenAsync();

        return notes
            .OrderBy(n => n.Date, StringComparer.Ordinal).ThenBy(n => n.Period)
            .Select(n =>
            {
                entryMap.TryGetValue((n.ClassId, n.Date, n.Period, n.SubjectId), out var en);
                AbsenceReason? r = null;
                if (en?.ReasonId is not null) reasons.TryGetValue(en.ReasonId, out r);
                var owner = ownerById.GetValueOrDefault(n.ClassId);
                return new HomeworkItemDto(
                    n.Date, n.Period, n.SubjectId, subjects.GetValueOrDefault(n.SubjectId, ""),
                    n.Topic, n.Homework, n.Conducted,
                    hideGrades ? null : en?.Grade, en?.ReasonId, r?.Name, r?.IsLate ?? false,
                    n.OwnerKind, owner is { IsGroup: true } ? owner.Name : null);
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
        // G-18: qatorlar sinf VA guruh darslaridan quriladi.
        var owners = await LessonRoster.OwnersOfAsync(db, s);
        if (owners.Count == 0) return new List<StudentJournalRowDto>();
        var ownerIds = owners.Select(o => o.Id).ToList();

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

        // Sinf + faol guruh darslari, bo'linish filtri bilan (`PupilTimetable`).
        var lessons = await PupilTimetable.ForWeekAsync(db, s, q, w);
        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var teachers = await db.Teachers.ToDictionaryAsync(x => x.Id, x => x.FullName);
        var times = await db.LessonTimes.ToDictionaryAsync(x => x.Period);

        // Izoh va yozuv kaliti EGA bilan birga: guruh darsi o'z izohini oladi.
        var notes = await db.LessonNotes
            .Where(n => ownerIds.Contains(n.ClassId) && n.Quarter == q)
            .ToListAsync();
        var noteMap = notes
            .GroupBy(n => (n.ClassId, n.Date, n.Period, n.SubjectId, n.SubGroup))
            .ToDictionary(g => g.Key, g => g.First());

        var entries = await db.JournalEntries
            .Where(e => ownerIds.Contains(e.ClassId) && e.Quarter == q && e.StudentId == s.Id)
            .ToListAsync();
        var entryMap = entries
            .GroupBy(e => (e.ClassId, e.Date, e.Period, e.SubjectId))
            .ToDictionary(g => g.Key, g => g.First());

        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id);

        // §5.5 — jadval, mavzu va davomat qoladi, faqat baho bo'shaydi (Homework bilan bir xil sabab).
        var hideGrades = await ProgressHiddenAsync();

        var rows = new List<StudentJournalRowDto>();
        foreach (var l in lessons)
        {
            var date = ScheduleMath.AddDaysISO(monday, l.Day);
            // Hafta chorak chetiga qisilgan bo'lsa kunni tashqarida qoldiramiz.
            if (string.CompareOrdinal(date, wk.StartISO) < 0 ||
                string.CompareOrdinal(date, wk.EndISO) > 0) continue;

            noteMap.TryGetValue((l.Owner.Id, date, l.Period, l.SubjectId, l.SubGroup), out var n);
            entryMap.TryGetValue((l.Owner.Id, date, l.Period, l.SubjectId), out var en);
            AbsenceReason? r = null;
            if (en?.ReasonId is not null) reasons.TryGetValue(en.ReasonId, out r);
            times.TryGetValue(l.Period, out var lt);

            rows.Add(new StudentJournalRowDto(
                date, l.Period, q, w,
                lt?.StartTime, lt?.EndTime,
                l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""),
                l.TeacherId, teachers.GetValueOrDefault(l.TeacherId, ""),
                n?.Topic ?? "", n?.Homework, n?.Conducted ?? false,
                hideGrades ? null : en?.Grade, en?.ReasonId, r?.Name, r?.IsLate ?? false,
                l.Owner.Kind, l.Owner.IsGroup ? l.Owner.Name : null));
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
        // G-18: guruh darsidagi davomatsizlik ham shu ro'yxatda.
        var owners = await LessonRoster.OwnersOfAsync(db, s);
        if (owners.Count == 0) return new StudentAttendanceFullDto(
            new StudentAttendanceDto(new(), new(), new(), new(), new()),
            new List<StudentAbsenceRowDto>());
        var ownerIds = owners.Select(o => o.Id).ToList();

        var report = await StudentReportBuilder.BuildAsync(db, s);

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var reasonRows = await db.AbsenceReasons.ToListAsync();
        var reasons = reasonRows.ToDictionary(r => r.Id);

        var rowsQuery = db.JournalEntries
            .Where(e => ownerIds.Contains(e.ClassId) && e.StudentId == s.Id && e.ReasonId != null);
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
            s.ParentFullName, s.ParentPhone, s.EnrollmentDate,
            s.BirthCertificateUrl, s.ParentPassportUrl);
        var meta = await refCache.MetaAsync();

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        // G-18: bugungi darslar va baholar sinfdan ham, FAOL GURUHlardan ham.
        var owners = await LessonRoster.OwnersOfAsync(db, s);
        var ownerIds = owners.Select(o => o.Id).ToList();
        var ownerById = owners.ToDictionary(o => o.Id, StringComparer.Ordinal);

        // Bugungi darslar (joriy chorak + joriy hafta, kun = today.DayOfWeek 0=Du..5=Sha).
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var apiDay = ((int)AppClock.Now.DayOfWeek + 6) % 7; // C# Sun=0..Sat=6 → Mon=0..Sun=6 (sun=6=ignore)

        var todayLessons = new List<StudentLessonDto>();
        var todayGrades = new List<HomeworkItemDto>();
        if (owners.Count > 0)
        {
            var lessons = await PupilTimetable.ForWeekAsync(db, s, meta.CurrentQuarter, meta.CurrentWeek);
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
                        l.TeacherId, teachers.GetValueOrDefault(l.TeacherId, ""), l.SubGroup,
                        l.Owner.Kind, l.Owner.IsGroup ? l.Owner.Name : null);
                })
                .ToList();

            // Bugungi baholar — shu o'quvchining bugungi jurnal yozuvlari (Grade != null).
            // §5.5 — ota-onaga yopilgan bo'lsa ro'yxat BO'SH qoladi (bu yerda qatorning
            // O'ZI baho; bahosiz qator "bo'sh nishon" bo'lib ekranni chalg'itardi).
            var entries = new List<JournalEntry>();
            if (!await ProgressHiddenAsync())
                entries = await db.JournalEntries
                    .Where(e => ownerIds.Contains(e.ClassId) && e.StudentId == s.Id
                                && e.Date == today && e.Grade != null)
                    .ToListAsync();
            // G-2: bo'lingan darsda bir (sana, dars, fan) uchun 1- va 2-guruhning ALOHIDA izohi
            // bor — ToDictionary ular bilan yiqilardi (HTTP 500). O'quvchiga faqat butun sinf
            // izohi yoki O'Z guruhiniki tegishli; ikkalasi bo'lsa o'z guruhiniki ustun.
            // Guruh darsida bo'linish yo'q — uning izohi a'zolarning hammasiga tegishli.
            var subGroup = s.SubGroup;
            var notes = (await db.LessonNotes
                    .Where(n => ownerIds.Contains(n.ClassId) && n.Date == today)
                    .ToListAsync())
                .Where(n => n.OwnerKind == LessonOwnerKind.Group
                            || n.SubGroup == 0 || n.SubGroup == subGroup)
                .ToList();
            var noteMap = notes
                .GroupBy(n => (n.ClassId, n.Date, n.Period, n.SubjectId))
                .ToDictionary(g => g.Key, g => g
                    .OrderByDescending(n => n.SubGroup == subGroup)
                    .ThenBy(n => n.Id, StringComparer.Ordinal)
                    .First());
            var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id);

            todayGrades = entries
                .OrderBy(e => e.Period)
                .Select(e =>
                {
                    noteMap.TryGetValue((e.ClassId, e.Date, e.Period, e.SubjectId), out var n);
                    AbsenceReason? r = null;
                    if (e.ReasonId is not null) reasons.TryGetValue(e.ReasonId, out r);
                    var owner = ownerById.GetValueOrDefault(e.ClassId);
                    return new HomeworkItemDto(
                        e.Date, e.Period, e.SubjectId, subjects.GetValueOrDefault(e.SubjectId, ""),
                        n?.Topic ?? "", n?.Homework, n?.Conducted ?? true,
                        e.Grade, e.ReasonId, r?.Name, r?.IsLate ?? false,
                        e.OwnerKind, owner is { IsGroup: true } ? owner.Name : null);
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

        // Qoldiq HISOBLANADI (P1-21): o'quvchi qatorida saqlanmaydi.
        var balance = await new StudentBalanceQuery(db).ForAsync(s.Id);

        return new StudentDashboardDto(
            profile, meta, todayLessons, todayGrades, pending, balance, monthlyFee);
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
            st = new UserSettings { UserId = uid };
            db.UserSettings.Add(st);
        }
        if (!string.IsNullOrWhiteSpace(req.Language)) st.Language = req.Language!.Trim();
        if (!string.IsNullOrWhiteSpace(req.Theme)) st.Theme = req.Theme!.Trim();
        if (req.NotificationsEnabled.HasValue) st.NotificationsEnabled = req.NotificationsEnabled.Value;
        st.UpdatedAt = AppClock.Now;
        await db.SaveChangesAsync();
        return new UserSettingsDto(st.Language, st.Theme, st.NotificationsEnabled);
    }

    /// <summary>
    /// O'quvchi/ota-ona ilova ichida o'z parolini almashtiradi. Joriy parol bilan tasdiqlanadi,
    /// yangi parol kamida 8 belgi. Faqat o'zinikiga (admin bu yerda impersonate qila olmaydi).
    /// Login (email) o'zgarmagani uchun joriy token amal qilaveradi — qayta kirish shart emas.
    /// </summary>
    [HttpPut("password")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();
        var user = await db.Users.FindAsync(uid);
        if (user is null) return Unauthorized();

        if (!PasswordHasher.Verify(req.CurrentPassword ?? "", user.PasswordHash))
            return BadRequest(new { message = "Joriy parol noto'g'ri" });

        var newPwd = (req.NewPassword ?? "").Trim();
        if (newPwd.Length < 8)
            return BadRequest(new { message = "Yangi parol kamida 8 belgidan iborat bo'lsin" });

        user.SetOwnPassword(newPwd);
        await db.SaveChangesAsync();
        return Ok(new { message = "Parol almashtirildi" });
    }

    // ---------- Joylashuv (GPS) ----------

    /// <summary>
    /// Uy joylashuvini yangilash — mobil ilova GPS dan keladi (latitude/longitude, ixtiyoriy address).
    /// Ilova foydalanuvchisi (o'quvchi/ota-ona — bitta akkaunt) o'z joylashuvini kiritadi (admin impersonate emas).
    /// Saqlangan joylashuv admin "Ilova → Joylashuv" xaritasida (Leaflet) ko'rinadi.
    /// </summary>
    [HttpPut("location")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult> UpdateLocation(UpdateLocationRequest req)
    {
        var s = await TargetAsync(null);   // akkauntga bog'langan o'quvchi
        if (s is null) return NotFound();
        if (req.Latitude is < -90 or > 90 || req.Longitude is < -180 or > 180)
            return BadRequest(new { message = "Koordinatalar noto'g'ri" });
        s.Latitude = req.Latitude;
        s.Longitude = req.Longitude;
        s.LocationAddress = (req.Address ?? "").Trim();
        s.LocationUpdatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Joriy saqlangan joylashuvni o'qish (ilova xaritada ko'rsatishi uchun). Hali yo'q bo'lsa null'lar.</summary>
    [HttpGet("location")]
    public async Task<ActionResult<StudentLocationDto>> GetLocation([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return new StudentLocationDto(s.Latitude, s.Longitude, s.LocationAddress, s.LocationUpdatedAt);
    }

    // ---------- Avtobus GPS (o'quvchi/ota-ona — FAQAT ertalabki oyna) ----------

    /// <summary>XAVFSIZLIK: o'quvchi/ota-ona avtobus joylashuvini faqat ertalab shu oynada ko'radi
    /// (06:00–09:00, Asia/Tashkent). Tashqarida hech narsa ko'rinmaydi.</summary>
    private const int BusVisibleFromHour = 6;
    private const int BusVisibleToHour = 9; // [06:00, 09:00) — 9:00 dan keyin ko'rinmaydi

    private static bool BusWindowOpen()
    {
        var h = AppClock.Now.Hour;
        return h >= BusVisibleFromHour && h < BusVisibleToHour;
    }

    /// <summary>Faol avtobuslarning jonli joylashuvi — FAQAT ertalabki oynada (06:00–09:00). Oynadan
    /// tashqarida Available=false va bo'sh ro'yxat qaytadi (mobil ilova xaritani yashiradi).</summary>
    [HttpGet("buses")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult<StudentBusesDto>> Buses()
    {
        var now = AppClock.Now;
        var iso = now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (!BusWindowOpen())
            return new StudentBusesDto(false, BusVisibleFromHour, BusVisibleToHour, iso, Array.Empty<StudentBusDto>());

        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var onlineMin = meta?.GpsOnlineMinutes ?? 5;

        var buses = await db.Buses.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
        var ids = buses.Select(b => b.Id).ToList();
        var latest = (await db.BusLocations.Where(l => ids.Contains(l.BusId)).ToListAsync())
            .GroupBy(l => l.BusId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.RecordedAt, StringComparer.Ordinal).First());

        var list = buses.Select(b =>
        {
            latest.TryGetValue(b.Id, out var l);
            var online = l is not null && DateTime.TryParse(l.RecordedAt, out var t)
                         && (now - t).TotalMinutes <= onlineMin;
            return new StudentBusDto(b.Id, b.Name, b.PlateNumber, b.DriverName, b.DriverPhone,
                b.Route, l?.Latitude, l?.Longitude, l?.Speed, l?.RecordedAt, online);
        }).ToList();

        return new StudentBusesDto(true, BusVisibleFromHour, BusVisibleToHour, iso, list);
    }

    /// <summary>Bitta avtobusning BUGUNGI izi (yo'nalish chizig'i + to'xtashlar) — FAQAT ertalabki oynada.
    /// Faqat bugungi kun (tarixiy harakatni ko'rib bo'lmaydi). Oynadan tashqarida bo'sh iz qaytadi.</summary>
    [HttpGet("buses/{id}/track")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult<BusTrackDto>> BusTrack(string id)
    {
        var d = AppClock.Today.ToString("yyyy-MM-dd");
        if (!BusWindowOpen())
            return new BusTrackDto(d, new(), new(), 0, 0, 0);

        var bus = await db.Buses.FirstOrDefaultAsync(b => b.Id == id && b.IsActive);
        if (bus is null) return NotFound();

        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var points = await db.BusLocations
            .Where(l => l.BusId == id && l.RecordedAt.StartsWith(d))
            .ToListAsync();
        return GpsService.Analyze(d, points, meta?.GpsStopRadiusM ?? 60, meta?.GpsStopMinMinutes ?? 3);
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
        var deviceName = (req.DeviceName ?? "").Trim();
        var appId = (req.AppId ?? "").Trim();

        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
        if (existing is null)
        {
            db.DeviceTokens.Add(new DeviceToken
            {
                UserId = uid,
                Token = token,
                Platform = platform,
                DeviceName = deviceName,
                AppId = appId,
            });
        }
        else
        {
            existing.UserId = uid;
            existing.Platform = platform;
            if (deviceName.Length > 0) existing.DeviceName = deviceName;
            if (appId.Length > 0) existing.AppId = appId;
            existing.LastSeenAt = AppClock.Now;
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
        if (await ProgressHiddenAsync()) return ProgressHidden();
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
        if (Application.Services.UploadGuard.Validate(file) is { } error)
            return BadRequest(new { message = error });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = Application.Services.UploadGuard.SafeName(file);
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
        // G-18: progres sinf va FAOL GURUH darslaridan yig'iladi.
        return await SubjectProgressService.ForStudentAsync(db, s, q);
    }

    /// <summary>Bitta fanga kirilganda — darslar ro'yxati (yashil = o'tilgan, qizil = hali yo'q).</summary>
    [HttpGet("subjects-progress/{subjectId}")]
    public async Task<ActionResult<SubjectProgressDetailDto>> SubjectProgressDetail(
        string subjectId, [FromQuery] int? quarter, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var (curQ, _) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var dto = await SubjectProgressService.ForStudentSubjectAsync(
            db, s, quarter ?? curQ, subjectId);
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
            .Include(x => x.Modules).ThenInclude(m => m.Topics)
            .Where(x => x.ClassId == cls.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        var topicIds = subjects
            .SelectMany(x => x.Modules.SelectMany(m => m.Topics.Select(t => t.Id))).ToList();
        var completed = (await db.LmsProgresses
            .Where(p => p.StudentId == s.Id && topicIds.Contains(p.TopicId))
            .Select(p => p.TopicId).ToListAsync()).ToHashSet();

        return subjects.Select(x =>
        {
            var allTopics = x.Modules.SelectMany(m => m.Topics).ToList();
            return new StudentLmsSubjectDto(
                x.Id, x.Title, x.Description, x.UnlockMode, x.BatchSize,
                allTopics.Count, allTopics.Count(t => completed.Contains(t.Id)));
        }).ToList();
    }

    /// <summary>
    /// Fanning to'liq mavzu ketma-ketligi (modul, keyin mavzu tartibida) ustidan ochilish
    /// bayroqlarini hisoblaydi. Ochilish konfiguratsiyasi fan (subject) darajasida turadi.
    /// </summary>
    private (List<LmsTopic> Ordered, HashSet<string> Completed, Func<int, bool> IsUnlocked) BuildUnlock(
        LmsSubject subject, HashSet<string> completedIds)
    {
        var ordered = subject.Modules
            .OrderBy(m => m.Order)
            .SelectMany(m => m.Topics.OrderBy(t => t.Order))
            .ToList();

        bool IsUnlocked(int i) => subject.UnlockMode switch
        {
            "sequential" => i == 0 || completedIds.Contains(ordered[i - 1].Id),
            "batch" => i < subject.BatchSize ||
                            completedIds.Contains(ordered[Math.Max(0, i - subject.BatchSize)].Id),
            _ => true, // "all"
        };

        return (ordered, completedIds, IsUnlocked);
    }

    /// <summary>Bitta mavzuni — ochilish holatiga qarab kontentni yashirib — DTO ga aylantiradi.</summary>
    private static StudentLmsTopicDto ToTopicDto(LmsTopic t, bool unlocked, bool completed) =>
        new StudentLmsTopicDto(
            t.Id, t.ModuleId, t.Title, t.Description,
            unlocked ? t.VideoUrl : null,
            unlocked ? t.TextContent : null,
            t.Order,
            // Qulflangan mavzularda kontent (video/matn/material) ko'rsatilmaydi
            unlocked
                ? t.Materials.Select(m => new LmsMaterialRowDto(m.Id, m.Name, m.Url, m.Size, m.ContentType)).ToList()
                : new List<LmsMaterialRowDto>(),
            unlocked, completed);

    /// <summary>Fanning modullari — har modul ichida mavzular, ochilish tartibi va progress bilan.</summary>
    [HttpGet("lms/subjects/{subjectId}/modules")]
    public async Task<ActionResult<IEnumerable<StudentLmsModuleDto>>> LmsModules(
        string subjectId, [FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return User.IsInRole("admin") ? NeedStudentId() : NotFound();

        var subject = await db.LmsSubjects
            .Include(x => x.Modules).ThenInclude(m => m.Topics).ThenInclude(t => t.Materials)
            .FirstOrDefaultAsync(x => x.Id == subjectId);
        if (subject is null) return NotFound();

        // Sinfga tegishli ekanini tekshiramiz
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null || subject.ClassId != cls.Id) return Forbid();

        var completedIds = (await db.LmsProgresses
            .Where(p => p.StudentId == s.Id &&
                subject.Modules.SelectMany(m => m.Topics).Select(t => t.Id).Contains(p.TopicId))
            .Select(p => p.TopicId).ToListAsync()).ToHashSet();

        var (ordered, _, isUnlocked) = BuildUnlock(subject, completedIds);
        // Mavzu id -> global indeks (modul.Order, keyin mavzu.Order bo'yicha)
        var idxOf = ordered.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);

        return subject.Modules.OrderBy(m => m.Order).Select(m =>
        {
            var mTopics = m.Topics.OrderBy(t => t.Order)
                .Select(t => ToTopicDto(t, isUnlocked(idxOf[t.Id]), completedIds.Contains(t.Id)))
                .ToList();
            return new StudentLmsModuleDto(
                m.Id, m.Title, m.Description, m.Order,
                mTopics.Count, mTopics.Count(t => t.IsCompleted), mTopics);
        }).ToList();
    }

    /// <summary>
    /// Fanning barcha mavzulari (tekis ro'yxat, global tartibda) — eski mijozlar uchun.
    /// Ochilish tartibi va o'quvchi progressi bilan; har mavzu o'z ModuleId'sini olib yuradi.
    /// </summary>
    [HttpGet("lms/subjects/{subjectId}/topics")]
    public async Task<ActionResult<IEnumerable<StudentLmsTopicDto>>> LmsTopics(
        string subjectId, [FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return User.IsInRole("admin") ? NeedStudentId() : NotFound();

        var subject = await db.LmsSubjects
            .Include(x => x.Modules).ThenInclude(m => m.Topics).ThenInclude(t => t.Materials)
            .FirstOrDefaultAsync(x => x.Id == subjectId);
        if (subject is null) return NotFound();

        // Sinfga tegishli ekanini tekshiramiz
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null || subject.ClassId != cls.Id) return Forbid();

        var completedIds = (await db.LmsProgresses
            .Where(p => p.StudentId == s.Id &&
                subject.Modules.SelectMany(m => m.Topics).Select(t => t.Id).Contains(p.TopicId))
            .Select(p => p.TopicId).ToListAsync()).ToHashSet();

        var (ordered, _, isUnlocked) = BuildUnlock(subject, completedIds);

        var result = new List<StudentLmsTopicDto>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i];
            result.Add(ToTopicDto(t, isUnlocked(i), completedIds.Contains(t.Id)));
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
            .Include(t => t.Module).ThenInclude(m => m.Subject)
            .FirstOrDefaultAsync(t => t.Id == topicId);
        if (topic is null) return NotFound();

        // Sinfga tegishli ekanini tekshiramiz
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName);
        if (cls is null || topic.Module.Subject.ClassId != cls.Id) return Forbid();

        // Fanning to'liq mavzu ketma-ketligini (modul + mavzu tartibida) yuklaymiz
        var subject = topic.Module.Subject;
        subject.Modules = await db.LmsModules
            .Include(m => m.Topics)
            .Where(m => m.SubjectId == topic.Module.SubjectId)
            .OrderBy(m => m.Order).ToListAsync();

        var ordered = subject.Modules
            .OrderBy(m => m.Order)
            .SelectMany(m => m.Topics.OrderBy(t => t.Order))
            .ToList();
        var completedIds = (await db.LmsProgresses
            .Where(p => p.StudentId == s.Id && ordered.Select(t => t.Id).Contains(p.TopicId))
            .Select(p => p.TopicId).ToListAsync()).ToHashSet();

        var (_, _, isUnlocked) = BuildUnlock(subject, completedIds);
        var idx = ordered.FindIndex(t => t.Id == topicId);

        if (idx < 0 || !isUnlocked(idx))
            return StatusCode(403, new { message = "Bu mavzu hali ochilmagan" });

        return new StudentLmsTopicDto(
            topic.Id, topic.ModuleId, topic.Title, topic.Description,
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
