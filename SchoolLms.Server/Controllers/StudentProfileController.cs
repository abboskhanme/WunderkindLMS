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

    public const string MissingCoordinateMessage =
        "Koordinata kerak — xaritadan nuqta belgilang";

    public const string BadKindMessage =
        "Joylashuv turi noto'g'ri (home | school | pickup)";

    public const string BadPickupTimeMessage = "Vaqt HH:mm ko'rinishida bo'lishi kerak";

    public const string PickupWindowRequiredMessage =
        "Olib ketish nuqtasida vaqt oralig'i (boshi va oxiri) majburiy";

    public const string PickupWindowOrderMessage =
        "Vaqt oralig'ining oxiri boshidan oldin bo'la olmaydi";

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
    //  6. Uchta turdagi joylashuv (L-2)
    // =====================================================================

    /// <summary>
    /// O'quvchining barcha joylashuvlari — uchtagacha, bittadan turdan
    /// (<see cref="StudentLocationKind"/>). §2.8, L-2.
    ///
    /// <para>
    /// <b>ESKI (L-1) BILAN QANDAY SINXRON.</b> <c>home</c> turi
    /// <c>PUT {id}/locations/home</c> orqali saqlanganda ESKI
    /// <c>students.latitude/longitude/location_address</c> ustunlariga ham
    /// ko'chiriladi (<see cref="MirrorLegacyHome"/>) — mobil ilova va
    /// ota-ona Mini App'i hamon o'sha ustunlarni o'qiydi va ular
    /// o'zgarishsiz ishlayveradi. Agar xodim hali YANGI ekrandan bir marta
    /// ham saqlamagan bo'lsa-yu, o'quvchida ESKI ustunlarda qiymat bo'lsa
    /// (L-1 orqali yoki eski mobil ilovadan qolgan) — <c>home</c> qatori
    /// shu yerda O'SHA ustunlardan SINTEZ qilinadi (<c>IsLegacy = true</c>),
    /// ya'ni hech narsa yo'qolmaydi va migratsiya shart emas.
    /// </para>
    /// </summary>
    [HttpGet("{id}/locations")]
    public async Task<ActionResult<IReadOnlyList<StudentLocationEntryDto>>> Locations(
        string id, CancellationToken ct = default)
    {
        var st = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (st is null) return NotFound();

        var rows = await db.StudentLocations.AsNoTracking()
            .Where(l => l.StudentId == id).ToListAsync(ct);
        return BuildLocationEntries(st, rows);
    }

    /// <summary>
    /// Bitta turdagi joylashuvni saqlaydi — xodim xaritadan nuqta bosadi,
    /// manzilni (va `pickup` uchun vaqt oralig'ini) qo'lda yozadi.
    /// Bir turdan bittadan qator bo'ladi (<c>ux_student_locations_student_kind</c>) —
    /// mavjud bo'lsa yangilanadi, aks holda yaratiladi.
    /// </summary>
    [HttpPut("{id}/locations/{kind}")]
    public async Task<ActionResult<IReadOnlyList<StudentLocationEntryDto>>> SaveTypedLocation(
        string id, string kind, SaveTypedLocationRequest req, CancellationToken ct = default)
    {
        if (!StudentLocationKind.IsValid(kind)) return BadRequest(new { message = BadKindMessage });

        var st = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (st is null) return NotFound();

        if (req.Lat is null || req.Lng is null)
            return BadRequest(new { message = MissingCoordinateMessage });
        if (req.Lat is < -90 or > 90 || req.Lng is < -180 or > 180)
            return BadRequest(new { message = BadCoordinatesMessage });

        var (fromOk, pickupFrom) = ParseTime(req.PickupFrom);
        var (toOk, pickupTo) = ParseTime(req.PickupTo);
        if (!fromOk || !toOk) return BadRequest(new { message = BadPickupTimeMessage });
        if (kind == StudentLocationKind.Pickup && (pickupFrom is null || pickupTo is null))
            return BadRequest(new { message = PickupWindowRequiredMessage });
        if (pickupFrom is { } pf && pickupTo is { } pt && pt < pf)
            return BadRequest(new { message = PickupWindowOrderMessage });

        var name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim();

        var row = await db.StudentLocations
            .FirstOrDefaultAsync(l => l.StudentId == id && l.Kind == kind, ct);
        var before = row is null
            ? null
            : new { row.Name, row.Lat, row.Lng, PickupFrom = row.PickupFrom.ToString(), PickupTo = row.PickupTo.ToString() };
        if (row is null)
        {
            row = new StudentLocation { StudentId = id, Kind = kind, CreatedAt = DateTimeOffset.UtcNow };
            db.StudentLocations.Add(row);
        }
        row.Name = name;
        row.Lat = (decimal)req.Lat.Value;
        row.Lng = (decimal)req.Lng.Value;
        row.PickupFrom = pickupFrom;
        row.PickupTo = pickupTo;

        // `home` — eski ustunlar ham OYNAdosh yoziladi (yuqoridagi izohga qarang).
        if (kind == StudentLocationKind.Home) MirrorLegacyHome(st, req.Lat, req.Lng, name);

        audit.Record(LocationEntity, st.Id, "update",
            $"O'quvchi joylashuvi yangilandi — {kind} ({st.FullName})",
            before: before,
            after: new { row.Name, row.Lat, row.Lng, PickupFrom = row.PickupFrom.ToString(), PickupTo = row.PickupTo.ToString() },
            studentId: st.Id);

        await db.SaveChangesAsync(ct);

        var rows = await db.StudentLocations.AsNoTracking()
            .Where(l => l.StudentId == id).ToListAsync(ct);
        return BuildLocationEntries(st, rows);
    }

    /// <summary>
    /// Bitta turdagi joylashuvni o'chiradi. Qator yo'q bo'lsa ham 200 —
    /// amal IDEMPOTENT (xodim "Tozalash"ni ikki marta bossa xatolik chiqmasin).
    /// <c>home</c> o'chirilsa — ESKI ustunlar ham tozalanadi.
    /// </summary>
    [HttpDelete("{id}/locations/{kind}")]
    public async Task<ActionResult<IReadOnlyList<StudentLocationEntryDto>>> DeleteTypedLocation(
        string id, string kind, CancellationToken ct = default)
    {
        if (!StudentLocationKind.IsValid(kind)) return BadRequest(new { message = BadKindMessage });

        var st = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (st is null) return NotFound();

        var row = await db.StudentLocations
            .FirstOrDefaultAsync(l => l.StudentId == id && l.Kind == kind, ct);
        if (row is not null)
        {
            db.StudentLocations.Remove(row);
            audit.Record(LocationEntity, st.Id, "delete",
                $"O'quvchi joylashuvi o'chirildi — {kind} ({st.FullName})",
                before: new { row.Name, row.Lat, row.Lng, PickupFrom = row.PickupFrom.ToString(), PickupTo = row.PickupTo.ToString() },
                after: null, studentId: st.Id);
        }

        if (kind == StudentLocationKind.Home) MirrorLegacyHome(st, null, null, null);

        await db.SaveChangesAsync(ct);

        var rows = await db.StudentLocations.AsNoTracking()
            .Where(l => l.StudentId == id).ToListAsync(ct);
        return BuildLocationEntries(st, rows);
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
    /// `home` turini ESKI <c>students</c> ustunlariga ham ko'chiradi (L-2).
    /// SetLocation (L-1) bilan bir xil qoida: uchala maydon ham null bo'lsa
    /// — <c>LocationUpdatedAt</c> ham null (yozuv umuman yo'q ma'nosida).
    /// </summary>
    private static void MirrorLegacyHome(Student st, double? lat, double? lng, string? address)
    {
        st.Latitude = lat;
        st.Longitude = lng;
        st.LocationAddress = address;
        st.LocationUpdatedAt = lat is null && lng is null && address is null ? null : AppClock.Iso();
    }

    /// <summary>
    /// <c>student_locations</c> qatorlarini javobga tayyorlaydi; agar
    /// ORASIDA <c>home</c> yo'q bo'lsa-yu ESKI ustunlarda qiymat bo'lsa —
    /// o'shandan SINTEZ qilib qo'shadi (<c>IsLegacy = true</c>). Natija
    /// doim <see cref="StudentLocationKind.All"/> tartibida.
    /// </summary>
    private static List<StudentLocationEntryDto> BuildLocationEntries(
        Student st, List<StudentLocation> rows)
    {
        var result = rows.Select(r => new StudentLocationEntryDto(
            r.Kind, r.Name, (double)r.Lat, (double)r.Lng,
            FormatTime(r.PickupFrom), FormatTime(r.PickupTo), IsLegacy: false)).ToList();

        if (!result.Exists(r => r.Kind == StudentLocationKind.Home)
            && st.Latitude is { } lat && st.Longitude is { } lng)
        {
            result.Add(new StudentLocationEntryDto(
                StudentLocationKind.Home, st.LocationAddress, lat, lng, null, null, IsLegacy: true));
        }

        return [.. result.OrderBy(r => Array.IndexOf(StudentLocationKind.All, r.Kind))];
    }

    private static string? FormatTime(TimeOnly? t) => t?.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Bo'sh/`null` — ruxsat etiladi (vaqt ko'rsatilmagan). Noto'g'ri format — xato.</summary>
    private static (bool Ok, TimeOnly? Value) ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (true, null);
        return TimeOnly.TryParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var t) ? (true, t) : (false, null);
    }

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
