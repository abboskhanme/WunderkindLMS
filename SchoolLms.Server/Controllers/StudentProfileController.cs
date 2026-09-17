using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quvchi KARTOCHKASI — docs/modules/students-parity.md §2.3 (S-10, S-12)
/// va §2.8 (L-1).
///
/// <para>
/// <b>Nega alohida controller.</b> <c>StudentsController</c> boshqa slice'ga
/// tegishli (§4.2), <c>AttendanceController</c> esa jurnal/davomat slice'iniki.
/// Kartochka uchun kerak bo'lgan o'qishlar shu yerda yig'ilgan, ya'ni birorta
/// mavjud fayl o'zgarmaydi va birlashtirish (merge) konflikti chiqmaydi.
/// Marshrut esa o'sha o'quvchiniki: <c>/api/admin/students/{id}/...</c>.
/// </para>
///
/// <para>
/// <b>PUL QOIDASI (§4 — "money rule").</b> Bu controller birorta summa
/// qaytarmaydi. Kartochkadagi balans va hisob-kitob moliya rolining orqasida
/// qoladi (<c>/api/student/billing</c>, <c>{id}/ledger</c>), shuning uchun
/// <see cref="Card"/> javobida `balance` har doim null. Faoliyat tarixida ham
/// moliyaviy yozuvlar va pul maydonlari moliya huquqisiz KO'RINMAYDI.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/students")]
public class StudentProfileController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Joylashuv o'zgarishi audit jurnalida shu tur bilan yoziladi.</summary>
    public const string LocationEntity = "StudentLocation";

    public const string BadCoordinatesMessage =
        "Koordinata noto'g'ri: kenglik −90..90, uzunlik −180..180 oralig'ida bo'lishi kerak";

    public const string HalfCoordinateMessage =
        "Kenglik va uzunlik birga beriladi — faqat bittasi yetarli emas";

    /// <summary>Faoliyat tarixida bir so'rovda qaytariladigan eng ko'p qator.</summary>
    private const int MaxActivity = 200;

    /// <summary>
    /// MOLIYAVIY audit turlari. Moliya huquqi bo'lmagan xodim kartochkaning
    /// "Faoliyat tarixi" tab'ida bularni umuman ko'rmaydi — pul faqat moliya
    /// ekranlarida.
    /// </summary>
    private static readonly HashSet<string> FinanceEntities = new(StringComparer.Ordinal)
    {
        AuditService.EntityFinanceTransaction,
        AuditService.EntityTeacherSalary,
        AuditService.EntityClassFee,
        AuditService.EntityStudentDiscount,
        AuditService.EntityLedgerEntry,
        AuditService.EntityPayment,
        AuditService.EntityExpense,
        AuditService.EntityInvoice,
        AuditService.EntityAnomalyFlag,
        AuditService.EntityDebtorAction,
        AuditService.EntityDebtorStatus,
        AuditService.EntityCashHandover,
        AuditService.EntityExpenseAttachment,
    };

    /// <summary>
    /// Pul ko'rsatadigan maydon nomlari — moliya huquqisiz `before`/`after`
    /// dan olib tashlanadi. Moliyaviy BO'LMAGAN yozuv ham summa olib yurishi
    /// mumkin (masalan import yakuni), shuning uchun tur bo'yicha filtr
    /// yetarli emas.
    /// </summary>
    private static readonly HashSet<string> MoneyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "amount", "balance", "salary", "monthlyFee", "fee", "price", "sum", "total",
        "paid", "debt", "credit", "discount", "discountAmount", "discountPct",
        "remaining", "charged", "cash",
    };

    private bool CanSeeMoney => FinanceMatrix.IsAllowed(FinanceAction.ViewBillingReports, User);

    // =====================================================================
    //  1. Kartochkaning chap paneli
    // =====================================================================

    /// <summary>
    /// Kartochka sarlavhasi: shaxs, holat tagi, login, joylashuv va maktabdagi
    /// kunlar soni. Ro'yxatdagi oynalar (tahrirlash, arxivlash) shu javobdagi
    /// <c>student</c> bilan ishlaydi.
    /// </summary>
    [HttpGet("{id}/card")]
    public async Task<ActionResult<StudentCardDto>> Card(string id, CancellationToken ct = default)
    {
        var st = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (st is null) return NotFound();

        var status = st.StatusId is { } statusId
            ? await db.StudentStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == statusId, ct)
            : null;

        var login = st.UserId is null
            ? null
            : await db.Users.AsNoTracking().Where(u => u.Id == st.UserId)
                .Select(u => u.Email).FirstOrDefaultAsync(ct);

        var homeroom = string.IsNullOrEmpty(st.ClassName)
            ? ""
            : await db.Teachers.AsNoTracking().Where(t => t.HomeroomClass == st.ClassName)
                .Select(t => t.FullName).FirstOrDefaultAsync(ct) ?? "";

        return new StudentCardDto(
            // `Balance = null` — ataylab: pul kartochkaning moliya tab'ida.
            new StudentDto(
                st.Id, st.FullName, st.BirthDate, st.Address, st.Gender,
                st.ParentFullName, st.ParentPhone, st.ClassName, st.EnrollmentDate,
                null, st.SubGroup,
                st.LastName, st.FirstName, st.MiddleName, st.BirthCertificateUrl,
                st.ParentLastName, st.ParentFirstName, st.ParentMiddleName, st.ParentPassportUrl,
                st.IsArchived, st.ArchivedAt, st.ArchiveReason),
            st.Phone, st.Language, st.DocumentUrl,
            st.StatusId, status?.Name, status?.Color,
            login, homeroom,
            st.Latitude, st.Longitude, st.LocationAddress, st.LocationUpdatedAt,
            ActiveDays(st));
    }

    // =====================================================================
    //  2. Jadval
    // =====================================================================

    /// <summary>
    /// O'quvchining bir haftalik jadvali — sinf darslari va FAOL GURUH
    /// darslari birga (<see cref="PupilTimetable"/>). O'chirgich o'chiq bo'lsa
    /// ro'yxatda faqat sinf darslari bo'ladi.
    ///
    /// <para>
    /// Xodim <c>/api/student/schedule</c> dan foydalana olmaydi: u endpoint
    /// faqat <c>student,parent,admin</c> rollariga ochiq. Shuning uchun
    /// kartochkaning o'z marshruti bor.
    /// </para>
    /// </summary>
    [HttpGet("{id}/timetable")]
    public async Task<ActionResult<StudentTimetableDto>> Timetable(
        string id, [FromQuery] int? quarter, [FromQuery] int? week, CancellationToken ct = default)
    {
        var st = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (st is null) return NotFound();

        var (curQ, curW) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var q = quarter ?? curQ;
        var w = week ?? curW;

        var period = await db.Quarters.AsNoTracking().FirstOrDefaultAsync(x => x.Quarter == q, ct);
        var weeks = period is null
            ? []
            : ScheduleMath.GetQuarterWeeks(period.StartDate, period.EndDate);
        if (weeks.Count > 0) w = Math.Clamp(w, 1, weeks.Count);
        var shown = weeks.FirstOrDefault(x => x.Week == w);

        var lessons = await PupilTimetable.ForWeekAsync(db, st, q, w, ct);
        if (lessons.Count == 0)
            return new StudentTimetableDto(q, w, weeks.Count, shown?.StartISO, shown?.EndISO, []);

        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var teachers = await db.Teachers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var times = await db.LessonTimes.AsNoTracking().ToDictionaryAsync(x => x.Period, ct);

        var rows = lessons.Select(l =>
        {
            times.TryGetValue(l.Period, out var lt);
            return new StudentLessonDto(
                l.Day, l.Period, lt?.StartTime, lt?.EndTime,
                l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""),
                l.TeacherId, teachers.GetValueOrDefault(l.TeacherId, ""),
                l.SubGroup,
                OwnerKind: l.Owner.Kind,
                OwnerName: l.Owner.IsGroup ? l.Owner.Name : null);
        }).ToList();

        return new StudentTimetableDto(q, w, weeks.Count, shown?.StartISO, shown?.EndISO, rows);
    }

    // =====================================================================
    //  3. Davomat — oraliq hisoboti
    // =====================================================================

    /// <summary>
    /// Berilgan oraliqdagi davomat: jami va HAR FAN kesimida
    /// (§2.3.1 "attendance range report").
    ///
    /// <para>
    /// Ta'rif <c>StudentProfileBuilder</c> dagi bilan bir xil: maxraj —
    /// O'TILGAN darslar (<c>lesson_notes.conducted</c>), "kech keldi" belgisi
    /// yo'qlik sifatida sanalmaydi. Ikki ekran bir xil foizni ko'rsatishi shart.
    /// </para>
    /// </summary>
    [HttpGet("{id}/attendance-range")]
    public async Task<ActionResult<StudentAttendanceRangeDto>> AttendanceRange(
        string id, [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct = default)
    {
        var st = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (st is null) return NotFound();

        var (start, end) = NormalizeRange(from, to);

        var cls = await db.Classes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == st.ClassName, ct);
        var attainment = await ClassAttainment.ForStudentAsync(db, st, ct);
        var ownerIds = attainment.OwnerIdsForStudent(st.Id, cls?.Id);

        var reasons = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var reasonMap = reasons.ToDictionary(r => r.Id);
        var lateSet = reasons.Where(r => r.IsLate).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        // O'tilgan darslar — o'quvchi qamroviga tushadiganlari.
        var notes = ownerIds.Count == 0
            ? []
            : await db.LessonNotes.AsNoTracking()
                .Where(n => n.Conducted && ownerIds.Contains(n.ClassId)
                            && string.Compare(n.Date, start) >= 0
                            && string.Compare(n.Date, end) <= 0)
                .Select(n => new { n.ClassId, n.OwnerKind, n.SubjectId, n.Date, n.Period, n.SubGroup })
                .ToListAsync(ct);

        var conducted = notes
            .Where(n => attainment.CountsFor(st.Id, n.ClassId, n.OwnerKind, n.Date)
                        && (n.OwnerKind == LessonOwnerKind.Group
                            || n.SubGroup == 0 || n.SubGroup == st.SubGroup))
            .Select(n => (n.SubjectId, n.Date, n.Period))
            .ToHashSet();

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.StudentId == st.Id && e.ReasonId != null
                        && string.Compare(e.Date, start) >= 0
                        && string.Compare(e.Date, end) <= 0)
            .Select(e => new { e.SubjectId, e.Date, e.Period, e.ReasonId })
            .ToListAsync(ct);

        // Belgi FAQAT o'tilgan darsga tegishli bo'lsa sanaladi — aks holda
        // maxraj bilan surat boshqa-boshqa darslarni sanardi.
        var marked = entries
            .Where(e => conducted.Contains((e.SubjectId, e.Date, e.Period)))
            .ToList();

        var subjectNames = await db.Subjects.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var perSubject = conducted
            .GroupBy(c => c.SubjectId)
            .Select(g =>
            {
                var planned = g.Count();
                var subjectMarks = marked.Where(m => m.SubjectId == g.Key).ToList();
                var late = subjectMarks.Count(m => lateSet.Contains(m.ReasonId!));
                var absent = subjectMarks.Count - late;
                var attended = Math.Max(0, planned - absent);
                return new StudentAttendanceSubjectDto(
                    g.Key, subjectNames.GetValueOrDefault(g.Key, "—"),
                    planned, attended, absent, late,
                    planned > 0 ? (int)Math.Round((double)attended / planned * 100) : 0);
            })
            .OrderBy(x => x.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalPlanned = conducted.Count;
        var totalLate = marked.Count(m => lateSet.Contains(m.ReasonId!));
        var totalAbsent = marked.Count - totalLate;
        var totalAttended = Math.Max(0, totalPlanned - totalAbsent);

        // Sabablar taqsimoti — oraliqdagi BARCHA belgilar bo'yicha (o'tilgan
        // darsga bog'lanmagani ham ko'rinsin: xodim buni ko'rib jurnalni
        // to'g'rilaydi).
        var reasonCounts = entries
            .Where(e => reasonMap.ContainsKey(e.ReasonId!))
            .GroupBy(e => e.ReasonId!)
            .Select(g =>
            {
                var r = reasonMap[g.Key];
                return new AttendanceReasonCountDto(r.Id, r.Name, r.Short, r.IsLate, g.Count());
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        // Kun kesimi — oy kalendari uchun. Faqat dars O'TILGAN kunlar qatnashadi.
        var days = conducted
            .GroupBy(c => c.Date)
            .Select(g =>
            {
                var dayMarks = marked.Where(m => m.Date == g.Key).ToList();
                var late = dayMarks.Count(m => lateSet.Contains(m.ReasonId!));
                return new StudentAttendanceDayDto(g.Key, g.Count(), dayMarks.Count - late, late);
            })
            .OrderBy(x => x.Date, StringComparer.Ordinal)
            .ToList();

        return new StudentAttendanceRangeDto(
            start, end, totalPlanned, totalAttended, totalAbsent, totalLate,
            totalPlanned > 0 ? (int)Math.Round((double)totalAttended / totalPlanned * 100) : 0,
            perSubject, reasonCounts, days);
    }

    // =====================================================================
    //  4. Faoliyat tarixi (S-12)
    // =====================================================================

    /// <summary>
    /// Shu o'quvchiga tegishli audit yozuvlari: kim, qachon, nima o'zgardi.
    ///
    /// <para>
    /// <b>Moliya darvozasi.</b> Moliya hisobotlarini ko'rish huquqi
    /// bo'lmagan foydalanuvchiga moliyaviy turdagi yozuvlar umuman
    /// berilmaydi, qolgan yozuvlarning `before`/`after` idan esa pul
    /// maydonlari olib tashlanadi. Ya'ni bu tab qarzni ko'rsatishning
    /// aylanma yo'liga aylanmaydi.
    /// </para>
    /// </summary>
    [HttpGet("{id}/activity")]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> Activity(
        string id, [FromQuery] int? limit, CancellationToken ct = default)
    {
        if (!await db.Students.AsNoTracking().AnyAsync(s => s.Id == id, ct)) return NotFound();

        var take = Math.Clamp(limit ?? 100, 1, MaxActivity);
        var money = CanSeeMoney;

        var q = db.AuditLogs.AsNoTracking().Where(a => a.StudentId == id);
        if (!money) q = q.Where(a => !FinanceEntities.Contains(a.EntityType));

        var rows = await q
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(a => new AuditLogDto(
            a.Id, a.EntityType, a.EntityId, a.Action, a.Timestamp,
            a.ActorName, a.Summary,
            money ? a.Before : Redact(a.Before),
            money ? a.After : Redact(a.After),
            a.StudentId, a.TeacherId)).ToList();
    }

    // =====================================================================
    //  5. Joylashuv (L-1)
    // =====================================================================

    /// <summary>
    /// O'quvchining uy joylashuvini xodim qo'lda qo'yadi — xaritadan nuqta va
    /// yozma manzil (§2.8, L-1).
    ///
    /// <para>
    /// Bugungacha bu ustunlarni faqat eski mobil ilova yozardi
    /// (<c>PUT /api/student/location</c>), ya'ni amalda hech kim: xarita
    /// ekrani faqat eski ma'lumotni ko'rsatardi. Yangi ustun qo'shilmadi —
    /// mavjud <c>latitude</c>/<c>longitude</c>/<c>location_address</c>
    /// to'ldiriladi.
    /// </para>
    /// </summary>
    [HttpPut("{id}/location")]
    public async Task<ActionResult<StudentCardDto>> SetLocation(
        string id, SaveStudentLocationRequest req, CancellationToken ct = default)
    {
        var st = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (st is null) return NotFound();

        var address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim();

        if (req.Latitude is null != (req.Longitude is null))
            return BadRequest(new { message = HalfCoordinateMessage });

        if (req.Latitude is { } lat && req.Longitude is { } lng
            && (lat is < -90 or > 90 || lng is < -180 or > 180))
            return BadRequest(new { message = BadCoordinatesMessage });

        var before = new { st.Latitude, st.Longitude, Address = st.LocationAddress };

        st.Latitude = req.Latitude;
        st.Longitude = req.Longitude;
        st.LocationAddress = address;
        // Koordinata ham, manzil ham bo'shatilsa — yozuv umuman yo'q.
        st.LocationUpdatedAt = req.Latitude is null && address is null ? null : AppClock.Iso();

        audit.Record(LocationEntity, st.Id, "update",
            $"O'quvchi joylashuvi yangilandi ({st.FullName})",
            before: before,
            after: new { st.Latitude, st.Longitude, Address = st.LocationAddress },
            studentId: st.Id);

        await db.SaveChangesAsync(ct);
        return await Card(id, ct);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>Maktabda necha kun — qabuldan bugungacha (arxivda esa arxiv sanasigacha).</summary>
    private static int ActiveDays(Student st)
    {
        if (!DateOnly.TryParseExact(st.EnrollmentDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) return 0;
        var end = AppClock.Today;
        if (st.IsArchived && DateOnly.TryParseExact(st.ArchivedAt ?? "", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var archived)) end = archived;
        return Math.Max(0, end.DayNumber - start.DayNumber);
    }

    /// <summary>
    /// Oraliqni to'g'rilaydi: berilmagan bo'lsa — oxirgi 30 kun; teskari
    /// berilgan bo'lsa — joyi almashtiriladi (xodim sanalarni chalkashtirsa
    /// ekran bo'sh qolmasin).
    /// </summary>
    private static (string From, string To) NormalizeRange(string? from, string? to)
    {
        var today = AppClock.Today;
        var start = Parse(from) ?? today.AddDays(-30);
        var end = Parse(to) ?? today;
        if (start > end) (start, end) = (end, start);
        return (start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));
    }

    private static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// `before`/`after` JSON'idan pul maydonlarini olib tashlaydi. Yaroqsiz
    /// JSON bo'lsa — butunlay tashlanadi (ko'rsatilmagan ma'lumot sizib
    /// chiqqanidan yaxshiroq).
    /// </summary>
    private static string? Redact(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            foreach (var key in obj.Select(p => p.Key).Where(MoneyKeys.Contains).ToList())
                obj.Remove(key);
            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
