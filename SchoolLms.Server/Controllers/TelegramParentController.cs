using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Telegram Mini App — OTA-ONA ekranlari. SPEC §6 Faza 3.
// ===========================================================================
//
//  NEGA ALOHIDA YUZA KERAK BO'LDI
//  ------------------------------
//  `/api/student/*` va `/api/student/billing` ota-onani TELEFON RAQAMI orqali
//  bitta farzandga bog'laydi (`FirstOrDefault`). Ikki farzandli ota-ona
//  ikkinchisini KO'RA OLMAYDI — docs/PENDING_WIRING.md §15 da yozilgan va
//  SPEC §6 Faza 3 ning qabul mezoni aynan shu. Shuning uchun bu yerdagi HAR
//  BIR endpoint yo'lida `{studentId}` bor va egalik `student_guardians`
//  bo'yicha tekshiriladi.
//
//  ISHNI O'ZI QILMAYDI
//  -------------------
//  Har action mavjud xizmatni chaqiradi: `StudentReportBuilder`,
//  `PortalSchedule`, `StudentAttendance` (jurnal), `IInvoiceService`,
//  `IReceiptService`, `PickupService`, `ReferenceCache`. Yangi hisob-kitob
//  faqat bitta joyda — bosh sahifaning jamlamasida, va u ham mavjud
//  natijalarni yig'ishdan iborat.
//
//  BU YERDA YO'Q, CHUNKI MAVJUDI TO'G'RIDAN-TO'G'RI ISHLAYDI (rol = parent):
//    GET /api/student/meta      · GET /api/student/school
//    GET /api/student/holidays  · GET /api/student/canteen[?start=&end=]
//    GET /api/student/canteen/{date}
//  Ular o'quvchiga bog'liq emas (butun maktab uchun bir xil), shuning uchun
//  ularni `/api/tg/...` ostida takrorlash ortiqcha yuza bo'lardi.
//
//  EGALIK BUZILSA — 404, 403 EMAS. Begona bolaning MAVJUDLIGI ham ma'lumot
//  (`PortalFinanceController` dagi bilan bir xil qoida).
// ===========================================================================

/// <summary>Telegram Mini App'ning ota-ona yuzasi (`/api/tg/parent`).</summary>
[ApiController]
[Authorize(Roles = "parent")]
[Route("api/tg/parent")]
public sealed class TelegramParentController(
    AppDbContext db,
    ReferenceCache refCache,
    IInvoiceService invoices,
    IReceiptService receipts,
    FcmService fcm) : ControllerBase
{
    /// <summary>E'lonlar "yaqinda" deb sanaladigan oyna (bosh sahifadagi hisoblagich uchun).</summary>
    private const int AnnouncementWindowDays = 14;

    /// <summary>E'lonlar ro'yxatining eng ko'p uzunligi.</summary>
    private const int AnnouncementLimit = 50;

    private const string PdfMime = "application/pdf";

    private GuardianAccess Access => new(db);

    private string? Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // =====================================================================
    //  Farzandlar
    // =====================================================================

    /// <summary>
    /// Vasiyning farzandlari — Mini App'dagi almashtirgich shu ro'yxatdan quriladi.
    /// Qarzlar BITTA to'plamli so'rov bilan olinadi (sikl ichida hisoblanmaydi).
    /// </summary>
    [HttpGet("children")]
    public async Task<ActionResult<IEnumerable<TgChildDto>>> Children(CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        return await Access.ChildCardsAsync(uid, ct);
    }

    /// <summary>Bitta farzandning bosh sahifasi — bir chaqiruvda hammasi.</summary>
    [HttpGet("children/{studentId}/overview")]
    public async Task<ActionResult<TgChildOverviewDto>> Overview(string studentId, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Farzand topilmadi" });

        var meta = await refCache.MetaAsync();
        var report = await StudentReportBuilder.BuildAsync(db, child);
        // Kartani almashtirgich bilan BIR XIL manbadan olamiz — ikki ekranda
        // bitta bolaning qarzi har xil ko'rinmasligi uchun.
        var card = (await Access.ChildCardsAsync(uid, ct)).First(c => c.StudentId == child.Id);

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == child.ClassName, ct);
        var (todayLessons, todayGrades) = cls is null
            ? (new List<StudentLessonDto>(), new List<HomeworkItemDto>())
            : await TodayAsync(cls.Id, child, meta, ct);

        var billing = await invoices.ForStudentAsync(child.Id, ct);
        var pickup = await PickupService.TodayAsync(db, child.Id, ct);

        var since = AppClock.Now.AddDays(-AnnouncementWindowDays);
        var recentAnnouncements = await AnnouncementsFor(child.ClassName)
            .CountAsync(x => x.CreatedAt >= since, ct);

        var q = meta.CurrentQuarter;
        return new TgChildOverviewDto(
            card, meta, todayLessons, todayGrades,
            report.Attendance.MissedDays.GetValueOrDefault(q),
            report.Attendance.MissedLessons.GetValueOrDefault(q),
            report.Attendance.LateCount.GetValueOrDefault(q),
            billing?.Debt ?? 0m, billing?.Credit ?? 0m,
            pickup is null ? null : PickupService.ToDto(pickup),
            recentAnnouncements);
    }

    // =====================================================================
    //  Ta'lim: davomat, baholar, jadval
    // =====================================================================

    /// <summary>Davomat: chorak bo'yicha jamlama + so'nggi kunlar ro'yxati.</summary>
    [HttpGet("children/{studentId}/attendance")]
    public async Task<ActionResult<StudentAttendanceFullDto>> Attendance(
        string studentId, [FromQuery] int? quarter, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Farzand topilmadi" });

        var report = await StudentReportBuilder.BuildAsync(db, child);

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == child.ClassName, ct);
        if (cls is null) return new StudentAttendanceFullDto(report.Attendance, []);

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, ct);

        var query = db.JournalEntries
            .Where(e => e.ClassId == cls.Id && e.StudentId == child.Id && e.ReasonId != null);
        if (quarter.HasValue) query = query.Where(e => e.Quarter == quarter.Value);

        var rows = (await query.ToListAsync(ct))
            .OrderByDescending(e => e.Date, StringComparer.Ordinal).ThenByDescending(e => e.Period)
            .Select(e =>
            {
                reasons.TryGetValue(e.ReasonId!, out var r);
                var name = r?.Name ?? "";
                return new StudentAbsenceRowDto(
                    e.Date, e.Period, e.Quarter,
                    e.SubjectId, subjects.GetValueOrDefault(e.SubjectId, ""),
                    e.ReasonId!, name, r?.IsLate ?? false,
                    name.Contains("kasal", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        return new StudentAttendanceFullDto(report.Attendance, rows);
    }

    /// <summary>Baholar: fan × chorak o'rtachalari (rasmiy chorak bahosi kunlikning o'rnini bosadi).</summary>
    [HttpGet("children/{studentId}/grades")]
    public async Task<ActionResult<StudentReportDto>> Grades(string studentId, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Farzand topilmadi" });

        return await StudentReportBuilder.BuildAsync(db, child);
    }

    /// <summary>
    /// Haftalik jadval. Chorak/hafta berilmasa — joriysi. Kun 0 = dushanba,
    /// ya'ni "bugun" ni frontend <c>Day</c> bo'yicha ajratadi.
    /// </summary>
    [HttpGet("children/{studentId}/schedule")]
    public async Task<ActionResult<IEnumerable<StudentLessonDto>>> Schedule(
        string studentId, [FromQuery] int? quarter, [FromQuery] int? week, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Farzand topilmadi" });

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == child.ClassName, ct);
        if (cls is null) return new List<StudentLessonDto>();

        var (curQ, curW) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        return await WeekLessonsAsync(cls.Id, child, quarter ?? curQ, week ?? curW);
    }

    // =====================================================================
    //  Pul
    // =====================================================================

    /// <summary>
    /// Moliya kartochkasi: oylar × toifalar, qarz qatorlari va to'lovlar tarixi.
    /// Javob shakli <c>GET /api/student/billing</c> bilan AYNAN bir xil
    /// (<see cref="PortalFinanceController.ToPortal"/>) — farqi faqat egalikni
    /// vasiylik bo'yicha aniqlashida.
    /// </summary>
    [HttpGet("children/{studentId}/finance")]
    [Produces("application/json")]
    public async Task<ActionResult<PortalFinanceDto>> Finance(string studentId, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Farzand topilmadi" });

        var card = await invoices.ForStudentAsync(child.Id, ct);
        if (card is null) return NotFound(new { message = "Farzand topilmadi" });

        return Ok(PortalFinanceController.ToPortal(card));
    }

    /// <summary>
    /// Chek PDF'i. Chek shu farzandga tegishli bo'lmasa — 404 (403 emas:
    /// begona chekning mavjudligi ham ma'lumot).
    /// </summary>
    [HttpGet("children/{studentId}/receipts/{paymentId:guid}.pdf")]
    public async Task<IActionResult> Receipt(string studentId, Guid paymentId, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Chek topilmadi" });

        var owns = await db.Payments.AsNoTracking()
            .AnyAsync(p => p.Id == paymentId && p.StudentId == child.Id, ct);
        if (!owns) return NotFound(new { message = "Chek topilmadi" });

        byte[] pdf;
        try { pdf = await receipts.RenderPdfAsync(paymentId, ct); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Chek topilmadi" }); }

        Response.Headers.ContentDisposition = $"inline; filename=\"chek-{paymentId}.pdf\"";
        return File(pdf, PdfMime);
    }

    // =====================================================================
    //  E'lonlar va pickup
    // =====================================================================

    /// <summary>
    /// Farzand sinfiga tegishli e'lonlar (butun maktabga yuborilganlari ham).
    /// "Tanlangan o'quvchilar" qamrovi bu yerda KO'RINMAYDI: `broadcasts`
    /// qatorida qabul qiluvchilar ro'yxati saqlanmaydi, faqat "Tanlangan (N)"
    /// degan yorliq bor — ya'ni u e'lon shu bolaga tegishlimi degan savolga
    /// javob berib bo'lmaydi.
    /// </summary>
    [HttpGet("children/{studentId}/announcements")]
    public async Task<ActionResult<IEnumerable<BroadcastDto>>> Announcements(
        string studentId, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Farzand topilmadi" });

        var list = await AnnouncementsFor(child.ClassName)
            .OrderByDescending(b => b.CreatedAt)
            .Take(AnnouncementLimit)
            .ToListAsync(ct);

        return list.Select(b => new BroadcastDto(
            b.Id, b.ClassName, b.Text, b.SenderName, b.CreatedAt.ToString("o"),
            b.RecipientCount, b.SentCount)).ToList();
    }

    /// <summary>Bugungi pickup so'rovi holati (bo'lmasa null).</summary>
    [HttpGet("children/{studentId}/pickup")]
    public async Task<ActionResult<PickupRequestDto?>> GetPickup(string studentId, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Farzand topilmadi" });

        var pr = await PickupService.TodayAsync(db, child.Id, ct);
        return Ok(pr is null ? null : PickupService.ToDto(pr));
    }

    /// <summary>
    /// "Farzandimni olishga keldim". Takror bosilsa yangi qator yaratilmaydi —
    /// bugungi kutilayotgan so'rov qaytadi (<see cref="PickupService"/>).
    /// </summary>
    [HttpPost("children/{studentId}/pickup")]
    public async Task<ActionResult<PickupRequestDto>> CreatePickup(string studentId, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var child = await Access.ChildAsync(uid, studentId, ct);
        if (child is null) return NotFound(new { message = "Farzand topilmadi" });

        var pr = await PickupService.EnsureTodayAsync(db, child, uid, ct);
        await PickupService.NotifyHomeroomAsync(db, fcm, child, ct);
        return PickupService.ToDto(pr);
    }

    // =====================================================================
    //  Ichki
    // =====================================================================

    /// <summary>Sinf jadvalidan o'quvchi guruhiga mos haftalik darslar.</summary>
    private async Task<List<StudentLessonDto>> WeekLessonsAsync(
        string classId, Student child, int quarter, int week)
    {
        var lessons = PortalSchedule.ForStudent(
            await PortalSchedule.LessonsForWeekAsync(db, classId, quarter, week), child.SubGroup).ToList();

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var teachers = await db.Teachers.ToDictionaryAsync(x => x.Id, x => x.FullName);
        var times = await db.LessonTimes.ToDictionaryAsync(x => x.Period);

        return [.. lessons
            .OrderBy(l => l.Day).ThenBy(l => l.Period)
            .Select(l =>
            {
                times.TryGetValue(l.Period, out var lt);
                return new StudentLessonDto(
                    l.Day, l.Period, lt?.StartTime, lt?.EndTime,
                    l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""),
                    l.TeacherId, teachers.GetValueOrDefault(l.TeacherId, ""), l.SubGroup);
            })];
    }

    /// <summary>Bugungi darslar va bugungi baholar (bosh sahifa uchun).</summary>
    private async Task<(List<StudentLessonDto> Lessons, List<HomeworkItemDto> Grades)> TodayAsync(
        string classId, Student child, PortalMetaDto meta, CancellationToken ct)
    {
        // C# Sun=0..Sat=6 → Mon=0..Sun=6; yakshanba (6) jadvalda yo'q.
        var apiDay = ((int)AppClock.Now.DayOfWeek + 6) % 7;
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var week = await WeekLessonsAsync(classId, child, meta.CurrentQuarter, meta.CurrentWeek);
        var lessons = week.Where(l => l.Day == apiDay).OrderBy(l => l.Period).ToList();

        var entries = await db.JournalEntries
            .Where(e => e.ClassId == classId && e.StudentId == child.Id && e.Date == today && e.Grade != null)
            .ToListAsync(ct);
        if (entries.Count == 0) return (lessons, []);

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var notes = (await db.LessonNotes.Where(n => n.ClassId == classId && n.Date == today).ToListAsync(ct))
            .ToDictionary(n => (n.Date, n.Period, n.SubjectId));
        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, ct);

        var grades = entries
            .OrderBy(e => e.Period)
            .Select(e =>
            {
                notes.TryGetValue((e.Date, e.Period, e.SubjectId), out var n);
                AbsenceReason? r = null;
                if (e.ReasonId is not null) reasons.TryGetValue(e.ReasonId, out r);
                return new HomeworkItemDto(
                    e.Date, e.Period, e.SubjectId, subjects.GetValueOrDefault(e.SubjectId, ""),
                    n?.Topic ?? "", n?.Homework, n?.Conducted ?? true,
                    e.Grade, e.ReasonId, r?.Name, r?.IsLate ?? false);
            })
            .ToList();

        return (lessons, grades);
    }

    /// <summary>
    /// Sinfga tegishli e'lonlar so'rovi. `broadcasts.class_name` da qamrov YORLIG'I
    /// turadi: sinf nomi, "&lt;sinf&gt; — qarzdorlar" yoki "Barcha sinflar[ — qarzdorlar]"
    /// (<c>MessagesController.SendBroadcast</c>).
    /// </summary>
    private IQueryable<Broadcast> AnnouncementsFor(string className) =>
        db.Broadcasts.AsNoTracking().Where(b =>
            b.ClassName == className
            || b.ClassName.StartsWith(className + " —")
            || b.ClassName.StartsWith("Barcha sinflar"));
}
