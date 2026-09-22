using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Telegram Mini App — O'QITUVCHI ekranlari. SPEC §6 Faza 3.
// ===========================================================================
//
//  BU YERDA YO'Q, CHUNKI MAVJUDI TO'G'RIDAN-TO'G'RI ISHLAYDI (rol = teacher):
//    GET  /api/teacher/me        · GET /api/teacher/meta   · GET /api/teacher/school
//    GET  /api/teacher/classes   · GET /api/teacher/schedule?quarter=&week=
//    GET  /api/teacher/salary    — MAOSH JAMLAMASI aynan shu (SalaryLedgerDto)
//    PUT  /api/teacher/journal   — bir tegishlik yo'qlamaning YOZISH tomoni
//    GET  /api/teacher/pickups   · POST /api/teacher/pickups/{id}/accept
//  Mini App tokeni oddiy login tokeni bilan bir xil, ya'ni bu endpointlar
//  hech qanday o'zgarishsiz ochiladi. Ularni `/api/tg/...` ostida takrorlash
//  ikkinchi yuzani va ikkinchi xatolik manbaini yaratardi.
//
//  BU YERDA BOR, CHUNKI MAVJUDI YO'Q EDI:
//    · bosh sahifa jamlamasi (bitta chaqiruv — telefon tarmog'ida muhim);
//    · YO'QLAMA RO'YXATI: o'quvchilar + shu darsning joriy jurnal holati +
//      sabablar ro'yxati BITTA javobda (ilgari uchta chaqiruv edi);
//    · so'nggi jurnal yozuvlari (hech qayerda yo'q edi);
//    · o'qilmagan chat xabarlari SONI — buning uchun `chat_reads` jadvali
//      qo'shildi: ilgari faqat "oxirgi xabar vaqti" bor edi va sonini
//      frontend taxmin qilardi.
// ===========================================================================

/// <summary>Telegram Mini App'ning o'qituvchi yuzasi (`/api/tg/teacher`).</summary>
[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/tg/teacher")]
public sealed class TelegramTeacherController(
    AppDbContext db, ChatService chat) : ControllerBase
{
    /// <summary>So'nggi jurnal yozuvlari ro'yxatining sukut bo'yicha uzunligi.</summary>
    private const int RecentDefault = 30;

    /// <summary>So'nggi jurnal yozuvlari ro'yxatining eng katta uzunligi.</summary>
    private const int RecentMax = 100;

    private string? Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>Tokendagi foydalanuvchi bo'yicha joriy o'qituvchi.</summary>
    private async Task<Teacher?> MeAsync(CancellationToken ct) =>
        Uid is null ? null : await db.Teachers.FirstOrDefaultAsync(t => t.UserId == Uid, ct);

    // =====================================================================
    //  Bosh sahifa
    // =====================================================================

    /// <summary>
    /// Bugungi darslar + sinf rahbarligidagi pickup so'rovlari + o'qilmagan
    /// xabarlar soni — bitta chaqiruvda.
    /// </summary>
    [HttpGet("today")]
    public async Task<ActionResult<TgTeacherTodayDto>> Today(CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });

        var (quarter, week) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var day = PortalSchedule.TodayIndex();

        // Jadval ruxsati yo'q o'qituvchi bugungi darslarini ko'rmaydi — bu
        // `GET /api/teacher/schedule` dagi qoidaning aynan o'zi (403 emas,
        // bo'sh ro'yxat: bosh sahifa butunlay yiqilmasligi kerak).
        var lessons = t.Permissions.Contains(TeacherPermissions.Schedule)
            ? (await PortalSchedule.TeacherWeekAsync(db, t.Id, quarter, week))
                .Where(l => l.Day == day).ToList()
            : [];

        var pickups = string.IsNullOrEmpty(t.HomeroomClass)
            ? []
            : (await db.PickupRequests.AsNoTracking()
                    .Where(p => p.ClassName == t.HomeroomClass && p.CreatedAt.StartsWith(today))
                    .OrderByDescending(p => p.CreatedAt).Take(50).ToListAsync(ct))
                .Select(PickupService.ToDto).ToList();

        var unread = t.Permissions.Contains(TeacherPermissions.Messages)
            ? (await UnreadAsync(ct)).Sum(x => x.Unread)
            : 0;

        return new TgTeacherTodayDto(today, quarter, week, lessons, pickups, unread);
    }

    // =====================================================================
    //  Bir tegishlik yo'qlama
    // =====================================================================

    /// <summary>
    /// Bitta dars uchun yo'qlama ro'yxati: sinf o'quvchilari (guruh bo'yicha
    /// filtrlangan), ularning shu darsdagi JORIY jurnal holati, ruxsat etilgan
    /// sabablar va dars mavzusi.
    ///
    /// <para>
    /// <b>Yozish bu yerda YO'Q</b> — u <c>PUT /api/teacher/journal</c> da va
    /// o'sha endpoint o'zgarmasdan ishlaydi. Ikkinchi yozish yo'li ochilsa,
    /// "kelajakdagi darsga baho qo'yib bo'lmaydi" kabi qoidalar ikki joyda
    /// turib qolardi.
    /// </para>
    /// </summary>
    [HttpGet("roster")]
    public async Task<ActionResult<TgRosterDto>> Roster(
        [FromQuery] string classId, [FromQuery] string subjectId,
        [FromQuery] int quarter, [FromQuery] string? date, [FromQuery] int period,
        [FromQuery] int? subGroup, CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!t.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        if (string.IsNullOrWhiteSpace(classId) || string.IsNullOrWhiteSpace(subjectId))
            return BadRequest(new { message = "classId va subjectId kerak" });
        if (period <= 0) return BadRequest(new { message = "period kerak" });
        if (subGroup is < 0 or > 2) return BadRequest(new { message = "subGroup 0, 1 yoki 2 bo'lishi kerak" });

        // Faqat o'zi dars beradigan ega+fan — `TeacherPortalController.Authorized` bilan
        // bir xil qoida, bitta manbadan (G-12).
        if (!await TeachesAsync(t.Id, classId, subjectId, ct)) return Forbid();

        var owner = await LessonRoster.OwnerAsync(db, classId, ct);
        if (owner is null) return NotFound(new { message = "Sinf topilmadi" });
        // Guruh darsida sinf ichidagi bo'linish yo'q — server rad etadi (§2.1.4).
        if (owner.IsGroup && subGroup is not null and not 0)
            return BadRequest(new { message = JournalService.SubGroupOnGroupMessage });
        var subjectName = await db.Subjects.AsNoTracking()
            .Where(s => s.Id == subjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";

        var day = string.IsNullOrWhiteSpace(date) ? AppClock.Today.ToString("yyyy-MM-dd") : date;

        // Ro'yxat — egadan (G-5): sinfda bugungi so'rovning aynan o'zi, guruhda faol a'zolar.
        var students = (await LessonRoster.ForLessonAsync(db, owner, ct: ct))
            .Select(s => new { s.Id, s.FullName, s.SubGroup }).ToList();

        var entries = (await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == classId && e.SubjectId == subjectId
                            && e.Date == day && e.Period == period
                            && e.OwnerKind == owner.Kind)
                .ToListAsync(ct))
            .ToDictionary(e => e.StudentId);

        var reasonRows = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var reasons = reasonRows.ToDictionary(r => r.Id);

        // G-2: bo'lingan darsda shu katakda 1- va 2-guruhning ALOHIDA izohi bor. Filtrsiz
        // `FirstOrDefault` 2-guruh o'qituvchisiga 1-guruhning mavzusini ko'rsatishi mumkin edi.
        // Guruh: so'rovda aniq berilgan bo'lsa — o'sha; aks holda jadvaldan (o'qituvchining
        // shu kun va dars raqamidagi O'Z darsi); aniqlab bo'lmasa — butun sinf (0).
        // Guruh darsida bo'linish yo'q, ya'ni har doim 0.
        var sg = owner.IsGroup
            ? 0
            : subGroup ?? await TeacherSubGroupAsync(t.Id, owner, subjectId, day, period, ct);
        var note = (await db.LessonNotes.AsNoTracking()
                .Where(n => n.ClassId == classId && n.SubjectId == subjectId
                            && n.Date == day && n.Period == period
                            && n.OwnerKind == owner.Kind
                            && (n.SubGroup == 0 || n.SubGroup == sg))
                .ToListAsync(ct))
            .OrderByDescending(n => n.SubGroup == sg)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var rows = students.Select(s =>
        {
            entries.TryGetValue(s.Id, out var e);
            AbsenceReason? r = null;
            if (e?.ReasonId is not null) reasons.TryGetValue(e.ReasonId, out r);
            return new TgRosterStudentDto(
                s.Id, s.FullName, owner.IsGroup ? 0 : s.SubGroup,
                e?.ReasonId, r?.Name, r?.IsLate ?? false, e?.Grade);
        }).ToList();

        return new TgRosterDto(
            owner.Id, owner.Name, subjectId, subjectName,
            day, period, quarter,
            note?.Conducted ?? false, note?.Topic, note?.Homework,
            [.. reasonRows.Select(r => new AbsenceReasonDto(r.Id, r.Name, r.Short, r.IsLate))],
            rows, owner.Kind);
    }

    /// <summary>O'qituvchi yaqinda kiritgan jurnal yozuvlari (baho yoki davomat sababi).</summary>
    [HttpGet("journal/recent")]
    public async Task<ActionResult<IEnumerable<TgJournalRecentDto>>> RecentJournal(
        [FromQuery] int? limit, CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!t.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();

        var take = Math.Clamp(limit ?? RecentDefault, 1, RecentMax);

        // O'qituvchi qaysi (ega, fan) juftliklarida dars beradi — jadval shablonlaridan.
        // O'chirgich o'chiq bo'lsa guruh juftliklari CHIQMAYDI (G-12, §4.3).
        var pairs = await TeacherOwnerAccess.PairsAsync(db, t.Id, ct);
        if (pairs.Count == 0) return new List<TgJournalRecentDto>();

        var ownerIds = pairs.Select(p => p.OwnerId).Distinct().ToList();
        var subjectIds = pairs.Select(p => p.SubjectId).Distinct().ToList();

        // Bazadan KENGROQ to'plam olinadi (ega × fan dekart ko'paytmasi), keyin
        // haqiqiy juftliklar bo'yicha siqiladi. Muqobil variant — har juftlik uchun
        // alohida so'rov, ya'ni o'nlab so'rov; bu esa bittasi.
        var raw = await db.JournalEntries.AsNoTracking()
            .Where(e => ownerIds.Contains(e.ClassId) && subjectIds.Contains(e.SubjectId)
                        && (e.Grade != null || e.ReasonId != null))
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Period)
            .Take(take * 4)
            .ToListAsync(ct);

        var rows = raw.Where(e => pairs.Contains((e.ClassId, e.OwnerKind, e.SubjectId))).Take(take).ToList();
        if (rows.Count == 0) return new List<TgJournalRecentDto>();

        // Ega nomi: sinf ham, guruh ham bir xil ustunda turadi (§2.1.4).
        var classNames = (await LessonRoster.AllOwnersAsync(db, ct))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Name, StringComparer.Ordinal);
        var subjectNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var studentIds = rows.Select(e => e.StudentId).Distinct().ToList();
        var studentNames = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);

        return rows.Select(e =>
        {
            AbsenceReason? r = null;
            if (e.ReasonId is not null) reasons.TryGetValue(e.ReasonId, out r);
            return new TgJournalRecentDto(
                e.Date, e.Period,
                e.ClassId, classNames.GetValueOrDefault(e.ClassId, ""),
                e.SubjectId, subjectNames.GetValueOrDefault(e.SubjectId, ""),
                e.StudentId, studentNames.GetValueOrDefault(e.StudentId, ""),
                e.Grade, r?.Name, r?.IsLate ?? false);
        }).ToList();
    }

    // =====================================================================
    //  Chat: o'qilmaganlar
    // =====================================================================

    /// <summary>Har kanal bo'yicha o'qilmagan xabarlar soni (o'zi yozgani sanalmaydi).</summary>
    [HttpGet("chat/unread")]
    public async Task<ActionResult<IEnumerable<TgChatUnreadDto>>> ChatUnread(CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!t.Permissions.Contains(TeacherPermissions.Messages)) return Forbid();

        return await UnreadAsync(ct);
    }

    /// <summary>Kanalni o'qilgan deb belgilaydi (hozirgi vaqt yoziladi).</summary>
    [HttpPost("chat/{channel}/read")]
    public async Task<IActionResult> MarkRead(string channel, CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!t.Permissions.Contains(TeacherPermissions.Messages)) return Forbid();

        var uid = Uid!;
        // Begona sinfning kanalini "o'qilgan" deb belgilab bo'lmaydi — belgi
        // o'z-o'zicha zararsiz, lekin u a'zolik ro'yxatini jimgina kengaytirardi.
        if (!await chat.CanAccessAsync(uid, "teacher", channel)) return Forbid();

        var row = await db.ChatReads.FirstOrDefaultAsync(x => x.UserId == uid && x.Channel == channel, ct);
        if (row is null)
        {
            row = new ChatRead { UserId = uid, Channel = channel };
            db.ChatReads.Add(row);
        }
        row.ReadAt = AppClock.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // =====================================================================
    //  Ichki
    // =====================================================================

    /// <summary>
    /// Kanal bo'yicha o'qilmaganlar. Uchta so'rov (kanallar, o'qish belgilari,
    /// xabarlar), kanal soniga bog'liq emas.
    /// </summary>
    private async Task<List<TgChatUnreadDto>> UnreadAsync(CancellationToken ct)
    {
        var uid = Uid!;
        var channels = await chat.ClassNamesForUserAsync(uid, "teacher");
        if (channels.Count == 0) return [];

        var readAt = await db.ChatReads.AsNoTracking()
            .Where(x => x.UserId == uid && channels.Contains(x.Channel))
            .ToDictionaryAsync(x => x.Channel, x => x.ReadAt, ct);

        // O'z xabari o'qilmagan bo'la olmaydi — shuning uchun jo'natuvchi bo'yicha filtr.
        var messages = await db.ChatMessages.AsNoTracking()
            .Where(m => channels.Contains(m.ClassName))
            .Select(m => new { m.ClassName, m.CreatedAt, m.SenderUserId, m.SenderName })
            .ToListAsync(ct);

        return [.. channels.Select(c =>
        {
            var mine = messages.Where(m => m.ClassName == c).ToList();
            var since = readAt.GetValueOrDefault(c);
            var unread = mine.Count(m => m.SenderUserId != uid && m.CreatedAt > since);
            var last = mine.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
            return new TgChatUnreadDto(c, unread, last?.CreatedAt.ToString("o"), last?.SenderName);
        })];
    }

    /// <summary>
    /// O'qituvchining shu egadagi, shu fandan, shu hafta kuni va dars raqamidagi darsi qaysi
    /// guruhga (0/1/2) tegishli. Manba — <see cref="TeachesAsync"/> bilan bir xil (eganing
    /// shablonlari). Topilmasa yoki shablonlarda har xil bo'lsa — 0 (butun sinf).
    /// </summary>
    private Task<int> TeacherSubGroupAsync(
        string teacherId, LessonOwner owner, string subjectId, string date, int period, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var d)) return Task.FromResult(0);
        var dayIndex = ((int)d.DayOfWeek + 6) % 7; // Dushanba = 0, ScheduleLesson.Day bilan bir xil
        return TeacherOwnerAccess.SubGroupAsync(db, teacherId, owner, subjectId, dayIndex, period, ct);
    }

    /// <summary>
    /// O'qituvchi shu egada (sinf yoki o'quv guruhi) shu fanni o'qitadimi.
    /// Qoida <see cref="TeacherOwnerAccess"/> da — o'qituvchi web portali bilan
    /// BITTA manba (G-12).
    /// </summary>
    private Task<bool> TeachesAsync(string teacherId, string classId, string subjectId, CancellationToken ct) =>
        TeacherOwnerAccess.TeachesAsync(db, teacherId, classId, subjectId, ct);

    /// <summary>Maktab yangiliklari — xodim auditoriyasi (sales-marketing.md §5.5).</summary>
    [HttpGet("news")]
    public async Task<ActionResult<IReadOnlyList<NewsFeedDto>>> News([FromQuery] int? take, CancellationToken ct) =>
        Ok(await NewsFeedQuery.ListAsync(db, NewsFeedAudience.Employee, take, ct));
}
