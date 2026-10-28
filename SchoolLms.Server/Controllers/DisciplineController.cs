using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using System.Security.Claims;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Intizomiy ball — har o'quvchi 100 balldan boshlaydi. Sabablar IKKI manbadan:
/// "other" — mustaqil intizomiy sabablar (shu yerda CRUD); "attendance" — davomat sabablari
/// (<see cref="AbsenceReason"/>, jurnalda ishlatiladi) — ularning balli shu yerda belgilanadi va
/// jurnalda shu sabab bilan davomat qo'yilsa qoldiga avtomatik ta'sir qiladi. Faqat admin.
/// Teachers record incidents through <see cref="TeacherDisciplineController"/> (2026-10-15,
/// docs/modules/behaviour.md); editing, coverage and export stay here.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("discipline")]
[Route("api/admin/discipline")]
public class DisciplineController(
    AppDbContext db, TelegramService telegram, ILogger<DisciplineParentNotifier> notifierLogger,
    AuditService audit)
    : ControllerBase
{
    private const int BaseScore = DisciplineIncidents.BaseScore;
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>
    /// Ota-onaga xabar yuboruvchi. DI'da ro'yxatdan o'tkazilmagan — <c>Program.cs</c> shu
    /// to'lqinda umumiy (konflikt) fayl, xizmat esa holatsiz va ikkita bog'liqlikka ega.
    /// <c>FinanceReportQueries</c> bilan bir xil naqsh (docs/PENDING_WIRING.md §3a).
    /// </summary>
    private DisciplineParentNotifier Notifier => new(db, telegram, notifierLogger);

    // ---------- Ball sabablar (birlashgan) ----------

    /// <summary>
    /// Barcha sabablar: mustaqil intizomiy ("other") + davomat sabablari ("attendance").
    ///
    /// <para>
    /// Davomat sabablarida <c>notifyParent</c> har doim <c>false</c> va <c>isActive</c> har
    /// doim <c>true</c>: ular <see cref="AbsenceReason"/> jadvalidan keladi, u yerda bunday
    /// ustunlar YO'Q va ATAYLAB yo'q — jurnaldagi har bir kechikish uchun ota-onaga xabar
    /// yuboradigan tizim aynan §6.3 ogohlantirgan narsa.
    /// </para>
    /// <para>
    /// Sukut bo'yicha faolsizlantirilgan sabablar HAM qaytadi — "Harakatlar" lentasining
    /// sabab filtri tarixdagi har bir sababni ko'rsatishi kerak, va bu endpoint oldin ham
    /// shunday ishlagan. <paramref name="activeOnly"/> = true — faqat faollari. Ball QO'YISH
    /// esa serverda ham tekshiriladi (<see cref="AddPoint"/>), ya'ni ekran unutsa ham
    /// faolsiz sabab bilan yangi yozuv tushmaydi.
    /// </para>
    /// </summary>
    [HttpGet("reasons")]
    public async Task<ActionResult<IEnumerable<DisciplineReasonDto>>> GetReasons(
        [FromQuery] bool activeOnly = false)
    {
        var other = (await db.DisciplineReasons.AsNoTracking()
            .Where(r => !activeOnly || r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync())
            .Select(Dto).ToList();
        var attendance = await db.AbsenceReasons.OrderBy(r => r.Name)
            .Select(r => new DisciplineReasonDto(r.Id, r.Name, r.Points, "attendance", false, null, true))
            .ToListAsync();
        return other.Concat(attendance).ToList();
    }

    [HttpPost("reasons")]
    public async Task<ActionResult<DisciplineReasonDto>> CreateReason(SaveDisciplineReasonRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Sabab nomi kerak" });
        if (TypedPoints(req, out var points) is { } typeError) return BadRequest(new { message = typeError });
        var owners = await OwnersAsync(req.OwnerUserIds);
        if (owners is null) return BadRequest(new { message = "Mas'ul o'qituvchi topilmadi" });
        if (req.Period is { } period && !DisciplinePeriod.IsValid(period))
            return BadRequest(new { message = "Muddat noto'g'ri (none, week yoki month)" });
        var r = new DisciplineReason
        {
            Name = req.Name.Trim(),
            Points = points,
            // Sukut bo'yicha O'CHIQ — §6.3: maktab har bir sabab uchun ataylab yoqsin.
            NotifyParent = req.NotifyParent ?? false,
            Description = Trimmed(req.Description),
            IsActive = req.IsActive ?? true,
            OwnerUserIds = owners,
            Period = req.Period ?? DisciplinePeriod.None,
        };
        db.DisciplineReasons.Add(r);
        await db.SaveChangesAsync();
        return Dto(r);
    }

    [HttpPut("reasons/{id}")]
    public async Task<ActionResult<DisciplineReasonDto>> UpdateReason(string id, SaveDisciplineReasonRequest req)
    {
        var r = await db.DisciplineReasons.FindAsync(id);
        if (r is null) return NotFound();
        if (TypedPoints(req, out var points) is { } typeError) return BadRequest(new { message = typeError });
        if (req.OwnerUserIds is not null)
        {
            var owners = await OwnersAsync(req.OwnerUserIds);
            if (owners is null) return BadRequest(new { message = "Mas'ul o'qituvchi topilmadi" });
            r.OwnerUserIds = owners;
        }
        if (req.Period is { } period)
        {
            if (!DisciplinePeriod.IsValid(period))
                return BadRequest(new { message = "Muddat noto'g'ri (none, week yoki month)" });
            r.Period = period;
        }
        r.Name = (req.Name ?? "").Trim();
        r.Points = points;
        // Berilmagan maydon O'ZGARMAYDI — eski mijoz sababni tahrirlaganda xabar
        // bayrog'ini tasodifan o'chirib yubormasin.
        if (req.NotifyParent is { } notify) r.NotifyParent = notify;
        if (req.Description is not null) r.Description = Trimmed(req.Description);
        if (req.IsActive is { } active) r.IsActive = active;
        await db.SaveChangesAsync();
        return Dto(r);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The owner list as stored: distinct, blanks dropped; null when an id is not a teacher or staff
    /// account. No list (an older client) is an empty one — only admins can give such a reason.
    /// </summary>
    private async Task<List<string>?> OwnersAsync(List<string>? ids)
    {
        var owners = (ids ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (owners.Count == 0) return owners;
        var known = await db.Users.CountAsync(u =>
            owners.Contains(u.Id) && (u.Role == Roles.Teacher || u.Role == Roles.Staff));
        return known == owners.Count ? owners : null;
    }

    /// <summary>Admin and superadmin give every reason; a staff account only the reasons it owns.</summary>
    private bool IsFullAdmin => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin);

    private bool MayUse(DisciplineReason r) => DisciplineOwners.MayUse(r, Uid, IsFullAdmin);

    private ObjectResult NotOwner() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = DisciplineOwners.NotOwnerMessage });

    /// <summary>
    /// Who can be made an owner of a reason: teachers (homeroom teachers too) and staff accounts
    /// (a deputy director, ...) — the "Mas'ullar" picker of the reason form.
    /// </summary>
    [HttpGet("owner-candidates")]
    public async Task<ActionResult<IEnumerable<DisciplineOwnerCandidateDto>>> OwnerCandidates(CancellationToken ct)
    {
        var homerooms = await db.ClassHomeroomTeachers.AsNoTracking()
            .Join(db.Teachers, h => h.TeacherId, t => t.Id, (h, t) => t.UserId)
            .Distinct().ToListAsync(ct);
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => t.UserId != null && t.UserId != "")
            .Select(t => new { t.UserId, t.FullName, t.IsArchived }).ToListAsync(ct);
        var staff = await db.Users.AsNoTracking().Where(u => u.Role == Roles.Staff)
            .Select(u => new { u.Id, u.FullName, u.Position }).ToListAsync(ct);

        return teachers
            .Select(t => new DisciplineOwnerCandidateDto(
                t.UserId!, t.FullName, Roles.Teacher,
                homerooms.Contains(t.UserId) ? "Sinf rahbari" : "O'qituvchi", t.IsArchived))
            .Concat(staff.Select(s => new DisciplineOwnerCandidateDto(
                s.Id, s.FullName, Roles.Staff, string.IsNullOrWhiteSpace(s.Position) ? "Xodim" : s.Position, false)))
            .OrderBy(c => c.FullName, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// The stored points of a reason form: with <c>type</c> (the "Ijobiy / Salbiy" selector) the sign
    /// comes from the type and 0 is refused; without it (an older client) the points as sent.
    /// </summary>
    private static string? TypedPoints(SaveDisciplineReasonRequest req, out int points)
    {
        points = req.Points;
        if (req.Type is null) return null;
        if (!DisciplineType.IsValid(req.Type)) return "Sabab turi noto'g'ri (ijobiy yoki salbiy)";
        if (req.Points == 0) return "Ball 0 bo'lmasin";
        points = DisciplineType.Signed(req.Type, req.Points);
        return null;
    }

    private DisciplineReasonDto Dto(DisciplineReason r) =>
        new(r.Id, r.Name, r.Points, "other", r.NotifyParent, r.Description, r.IsActive, r.OwnerUserIds, r.Period,
            MayUse(r));

    [HttpDelete("reasons/{id}")]
    public async Task<IActionResult> DeleteReason(string id)
    {
        var r = await db.DisciplineReasons.FindAsync(id);
        if (r is not null) { db.DisciplineReasons.Remove(r); await db.SaveChangesAsync(); }
        return NoContent();
    }

    /// <summary>Davomat sababiga ball belgilash (nomi Sozlamalar/jurnal tarafida boshqariladi).</summary>
    [HttpPut("reasons/attendance/{id}")]
    public async Task<ActionResult<DisciplineReasonDto>> SetAttendancePoints(string id, SetReasonPointsRequest req)
    {
        var r = await db.AbsenceReasons.FindAsync(id);
        if (r is null) return NotFound();
        r.Points = req.Points;
        await db.SaveChangesAsync();
        return new DisciplineReasonDto(r.Id, r.Name, r.Points, "attendance", false, null, true);
    }

    // ---------- Ballar nazorati ----------

    /// <summary>
    /// Faol o'quvchilar jamlamasi: plus/minus/qoldi. Qoldi = 100 + qo'lda kiritilgan ballar
    /// + jurnal davomati ballari (sabab balli != 0 bo'lganlar).
    /// </summary>
    [HttpGet("scores")]
    public async Task<ActionResult<IEnumerable<DisciplineScoreRowDto>>> GetScores()
        => await BuildScoresAsync();

    /// <summary>
    /// Ballar nazorati Excel (.xlsx) ga — ekrandagi FILTRLAR bilan bir xil qatorlar.
    /// Filtrlar sahifada mijoz tarafida qo'llanadi (<c>/scores</c> butun ro'yxatni qaytaradi),
    /// shuning uchun eksport ham xuddi shu filtrlarni qabul qiladi — yuklangan fayl ekranda
    /// ko'rinib turgan narsaga aynan mos bo'lishi uchun.
    /// </summary>
    [HttpGet("scores/export")]
    public async Task<IActionResult> ExportScores(
        [FromQuery] string? className, [FromQuery] string? search,
        [FromQuery] int? minPoints, [FromQuery] int? maxPoints, [FromQuery] string? sort)
    {
        var rows = FilterScores(await BuildScoresAsync(), className, search, minPoints, maxPoints, sort);

        var headers = new[] { "F.I.SH.", "Sinf", "Rag'bat (+)", "Jazo (−)", "Qoldi" };
        var cells = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.FullName, r.ClassName, r.Plus.ToString(), r.Minus.ToString(), r.Remaining.ToString(),
        });

        var bytes = ExcelExport.Build("Ballar nazorati", headers, cells);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ballar_nazorati_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Ekrandagi filtrlarning server tarafdagi nusxasi (eksport uchun). <c>sort</c> qiymatlari
    /// sahifadagi tanlov bilan bir xil: class | remaining_desc | remaining_asc | plus_desc | minus_desc.
    /// </summary>
    private static List<DisciplineScoreRowDto> FilterScores(
        List<DisciplineScoreRowDto> rows, string? className, string? search,
        int? minPoints, int? maxPoints, string? sort)
    {
        var q = (search ?? "").Trim();
        var list = rows.Where(r =>
                (string.IsNullOrWhiteSpace(className) || className == "all" || r.ClassName == className)
                && (q.Length == 0 || r.FullName.Contains(q, StringComparison.OrdinalIgnoreCase))
                && (minPoints is null || r.Remaining >= minPoints)
                && (maxPoints is null || r.Remaining <= maxPoints))
            .ToList();

        return sort switch
        {
            "remaining_desc" => list.OrderByDescending(r => r.Remaining).ToList(),
            "remaining_asc" => list.OrderBy(r => r.Remaining).ToList(),
            "plus_desc" => list.OrderByDescending(r => r.Plus).ToList(),
            "minus_desc" => list.OrderByDescending(r => r.Minus).ToList(),
            _ => list, // "class" — BuildScoresAsync allaqachon sinf+F.I.SH bo'yicha tartiblagan
        };
    }

    private async Task<List<DisciplineScoreRowDto>> BuildScoresAsync()
    {
        var students = await db.Students.Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync();
        var manual = await db.DisciplinePoints.Select(p => new { p.StudentId, p.Points }).ToListAsync();
        var absPts = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => r.Points);
        // One event per pupil, day and reason (AttendanceDisciplineDays): seven absent lessons, one penalty.
        var journal = await db.JournalEntries.Where(e => e.ReasonId != null)
            .Select(e => new { e.StudentId, e.ReasonId, e.Date }).Distinct().ToListAsync();

        var plusBy = new Dictionary<string, int>();
        var minusBy = new Dictionary<string, int>();
        void Apply(string sid, int pts)
        {
            if (pts > 0) plusBy[sid] = plusBy.GetValueOrDefault(sid) + pts;
            else if (pts < 0) minusBy[sid] = minusBy.GetValueOrDefault(sid) + (-pts);
        }
        foreach (var m in manual) Apply(m.StudentId, m.Points);
        foreach (var j in journal)
            if (j.ReasonId is not null && absPts.TryGetValue(j.ReasonId, out var p)) Apply(j.StudentId, p);

        return students.Select(s =>
        {
            var plus = plusBy.GetValueOrDefault(s.Id);
            var minus = minusBy.GetValueOrDefault(s.Id);
            return new DisciplineScoreRowDto(s.Id, s.FullName, s.ClassName, plus, minus, BaseScore + plus - minus);
        })
        .OrderBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    /// <summary>
    /// O'quvchiga qo'lda ball kiritadi (sabab "other" yoki "attendance" bo'lishi mumkin).
    ///
    /// <para>
    /// Sababda <c>notify_parent</c> yoqilgan bo'lsa — ota-onaga TELEGRAM xabari ketadi
    /// (§6.3, 4-qadam). Qaror va matn <see cref="DisciplineParentNotifier"/> da;
    /// javobdagi <c>notifiedParents</c> — HAQIQATAN yuborilgan chatlar soni, ya'ni 0
    /// "sabab bayrog'i o'chiq" ni ham, "ota-ona botga ulanmagan" ni ham anglatishi mumkin.
    /// Xabar yuborilmagani ball yozilmaganini ANGLATMAYDI.
    /// </para>
    /// </summary>
    [ActiveStudent]
    [HttpPost("points")]
    public async Task<ActionResult<DisciplinePointDto>> AddPoint(
        AddDisciplinePointRequest req, CancellationToken ct = default)
    {
        var student = await db.Students.FindAsync([req.StudentId], ct);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        string name;
        int pts;
        var dr = await db.DisciplineReasons.FindAsync([req.ReasonId], ct);
        if (dr is not null)
        {
            // Faolsizlantirilgan sabab bilan YANGI ball qo'yilmaydi; eski yozuvlar joyida qoladi.
            if (!dr.IsActive) return BadRequest(new { message = "Bu sabab faol emas" });
            if (!MayUse(dr)) return NotOwner();
            if ((await DisciplineIncidents.PeriodTakenAsync(db, dr, [student.Id], AppClock.Today, ct: ct)).Count > 0)
                return Conflict(new { message = DisciplineIncidents.PeriodTakenMessage(dr) });
            name = dr.Name; pts = dr.Points;
        }
        else
        {
            var ar = await db.AbsenceReasons.FindAsync([req.ReasonId], ct);
            if (ar is null) return BadRequest(new { message = "Sabab tanlanmadi" });
            name = ar.Name; pts = ar.Points;
        }

        var user = await db.Users.FindAsync([Uid], ct);
        var p = new DisciplinePoint
        {
            StudentId = student.Id,
            ReasonId = req.ReasonId,
            ReasonName = name,
            Points = pts,
            Note = (req.Note ?? "").Trim(),
            CreatedAt = AppClock.Now.ToString("o"),
            CreatedBy = user?.FullName ?? "Administrator",
            CreatedByUserId = string.IsNullOrEmpty(Uid) ? null : Uid,
        };
        db.DisciplinePoints.Add(p);
        // EntityDisciplinePoint was defined and never written until 2026-10-15 (audit-log.md).
        audit.Record(AuditService.EntityDisciplinePoint, p.Id, "create",
            $"Intizomiy ball: {student.FullName} — {name} ({Signed(pts)})"
            + (p.Note.Length > 0 ? $": \"{p.Note}\"" : ""),
            after: PointView(p), studentId: student.Id);
        await db.SaveChangesAsync(ct);

        // Xabar YOZUVDAN KEYIN: yuborish muvaffaqiyatsiz bo'lsa ham ball saqlanib qolsin.
        // `dr is null` — davomat sababi, unda `notify_parent` ustuni umuman yo'q.
        var notified = dr is null ? 0 : await Notifier.NotifyAsync(student, dr, p, ct);

        return new DisciplinePointDto(
            p.Id, p.StudentId, name, pts, p.Note, p.CreatedAt, p.CreatedBy, "manual", notified, p.ReasonId);
    }

    /// <summary>
    /// Bitta sabab bilan SINFNING HAR BIR faol o'quvchisiga alohida ball yozuvi qo'shadi
    /// (C-6, students-parity.md §2.2.3) — EduSchool'dagi <c>editBehaviorIncidents</c> /
    /// <c>POST /behavior-incidents/class</c> ning nusxasi. Faqat mustaqil intizomiy sabab
    /// ("other"): davomat sababi jurnal orqali, dars kesimida qo'yiladi — bu yerdan emas.
    ///
    /// <para>
    /// Har bir yozuv <see cref="AddPoint"/> bilan bir xil qoidaga bo'ysunadi (sabab faol
    /// bo'lishi shart, xabar <c>notify_parent</c> yoqilgan bo'lsa ketadi) — faqat bitta
    /// so'rovda BIR NECHA o'quvchiga takrorlanadi.
    /// </para>
    /// </summary>
    [HttpPost("points/class")]
    public async Task<ActionResult<ClassDisciplinePointResultDto>> AddPointForClass(
        AddClassDisciplinePointRequest req, CancellationToken ct = default)
    {
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.ClassId, ct);
        if (cls is null) return NotFound(new { message = "Sinf topilmadi" });

        var dr = await db.DisciplineReasons.FindAsync([req.ReasonId], ct);
        if (dr is null) return BadRequest(new { message = "Sabab tanlanmadi" });
        if (!dr.IsActive) return BadRequest(new { message = "Bu sabab faol emas" });
        if (!MayUse(dr)) return NotOwner();

        var students = await db.Students
            .Where(s => !s.IsArchived && s.ClassName == cls.Name).ToListAsync(ct);
        if (students.Count == 0) return BadRequest(new { message = "Bu sinfda faol o'quvchi yo'q" });

        // A periodic reason skips the pupils who already have it in this week / month.
        var taken = await DisciplineIncidents.PeriodTakenAsync(
            db, dr, students.Select(s => s.Id).ToList(), AppClock.Today, ct: ct);
        var skipped = students.RemoveAll(s => taken.Contains(s.Id));
        if (students.Count == 0) return Conflict(new { message = DisciplineIncidents.PeriodTakenMessage(dr) });

        var user = await db.Users.FindAsync([Uid], ct);
        var note = (req.Note ?? "").Trim();
        var author = user?.FullName ?? "Administrator";

        var pairs = new List<(Student Student, DisciplinePoint Point)>();
        foreach (var student in students)
        {
            var p = new DisciplinePoint
            {
                StudentId = student.Id,
                ReasonId = dr.Id,
                ReasonName = dr.Name,
                Points = dr.Points,
                Note = note,
                CreatedAt = AppClock.Now.ToString("o"),
                CreatedBy = author,
                CreatedByUserId = string.IsNullOrEmpty(Uid) ? null : Uid,
            };
            db.DisciplinePoints.Add(p);
            pairs.Add((student, p));
            audit.Record(AuditService.EntityDisciplinePoint, p.Id, "create",
                $"Intizomiy ball (sinf {cls.Name}): {student.FullName} — {dr.Name} ({Signed(dr.Points)})"
                + (note.Length > 0 ? $": \"{note}\"" : ""),
                after: PointView(p), studentId: student.Id);
        }
        await db.SaveChangesAsync(ct);

        // Xabarlar YOZUVDAN KEYIN: bittasi yuborilmasa ham, ballarning hammasi allaqachon saqlangan.
        var items = new List<DisciplinePointDto>();
        var notifiedTotal = 0;
        foreach (var (student, p) in pairs)
        {
            var notified = await Notifier.NotifyAsync(student, dr, p, ct);
            notifiedTotal += notified;
            items.Add(new DisciplinePointDto(
                p.Id, p.StudentId, dr.Name, dr.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual", notified, p.ReasonId));
        }

        return new ClassDisciplinePointResultDto(items.Count, notifiedTotal, items, skipped);
    }

    /// <summary>O'quvchining ball tarixi: qo'lda kiritilgan (o'chirsa bo'ladi) + jurnal davomati (faqat ko'rish).</summary>
    [HttpGet("points")]
    public async Task<ActionResult<IEnumerable<DisciplinePointDto>>> GetPoints([FromQuery] string studentId)
    {
        var manual = await db.DisciplinePoints.Where(p => p.StudentId == studentId).ToListAsync();
        var drNames = await db.DisciplineReasons.ToDictionaryAsync(r => r.Id, r => r.Name);
        var absReasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => new { r.Name, r.Points });

        var result = manual.Select(p => new DisciplinePointDto(
            p.Id, p.StudentId,
            string.IsNullOrEmpty(p.ReasonName) ? drNames.GetValueOrDefault(p.ReasonId, "—") : p.ReasonName,
            p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual", 0, p.ReasonId, p.UpdatedAt, p.UpdatedBy)).ToList();

        // Jurnal davomati — sabab balli != 0 bo'lganlari (qoldiga ta'sir qilgani uchun ko'rsatamiz).
        // Bitta kun + bitta sabab = bitta qator (AttendanceDisciplineDays), nechta darsligi izohda.
        var journal = await db.JournalEntries
            .Where(e => e.StudentId == studentId && e.ReasonId != null)
            .Select(e => new AttendanceDisciplineDays.Mark(e.Id, e.StudentId, e.Date, e.ReasonId!)).ToListAsync();
        foreach (var d in AttendanceDisciplineDays.Collapse(journal))
        {
            if (!absReasons.TryGetValue(d.ReasonId, out var r) || r.Points == 0) continue;
            result.Add(new DisciplinePointDto(
                d.Id, studentId, r.Name, r.Points, AttendanceDisciplineDays.Note(d.Lessons), d.Date, "", "attendance"));
        }

        return result.OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();
    }

    // ---------- Harakatlar (maktab bo'ylab lenta) ----------

    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    /// <summary>Davr berilmasa ko'riladigan oxirgi kunlar soni.</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>
    /// Maktab bo'ylab "nima bo'ldi" lentasi — <c>GET points?studentId=</c> dan farqli ravishda
    /// BITTA o'quvchiga bog'lanmagan. Ikki manba birlashtiriladi: qo'lda kiritilgan ballar va
    /// jurnal davomati (sabab balli != 0 bo'lganlari) — ikkinchisi <c>source: "attendance"</c>
    /// bilan belgilanadi.
    ///
    /// <para>Filtrlar (hammasi ixtiyoriy): <c>from</c>/<c>to</c> (YYYY-MM-DD, ikkalasi ham
    /// KIRADI), <c>className</c>, <c>reasonId</c>, <c>author</c> (qo'lda kiritgan xodim),
    /// <c>sign</c> = positive|negative, <c>source</c> = manual|attendance, <c>search</c> (F.I.SH).
    /// Davr BUTUNLAY berilmasa — oxirgi 30 kun (<see cref="DefaultWindowDays"/>), butun tarixni
    /// har so'rovda skanerlamaslik uchun.</para>
    ///
    /// <para>Sabab bo'yicha filtr <c>ReasonId</c> ustidan ishlaydi, ekranga esa yozuv paytidagi
    /// NUSXA (<c>ReasonName</c>/<c>Points</c>) chiqadi — sabab keyin tahrirlansa ham tarix
    /// o'zgarmasligi uchun (<see cref="DisciplinePoint"/>).</para>
    /// </summary>
    [HttpGet("feed")]
    public async Task<ActionResult<DisciplineFeedDto>> GetFeed(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? className,
        [FromQuery] string? reasonId, [FromQuery] string? author, [FromQuery] string? sign,
        [FromQuery] string? source, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        var (feed, error) = await BuildFeedAsync(new FeedFilters(from, to, className, reasonId, author, sign, source, search));
        if (error is not null) return BadRequest(new { message = error });
        var (ordered, classNames) = feed;

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var authors = (await db.DisciplinePoints.AsNoTracking()
                .Select(p => p.CreatedBy).Distinct().ToListAsync())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();

        return new DisciplineFeedDto(
            ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            ordered.Count, page, pageSize,
            ordered.Count(r => r.Points > 0), ordered.Count(r => r.Points < 0), ordered.Sum(r => r.Points),
            authors, classNames);
    }

    /// <summary>
    /// "Harakatlar" as .xlsx — every row of the feed under the SAME filters (no paging), newest first,
    /// so the file matches the screen (docs/modules/behaviour.md).
    /// </summary>
    [HttpGet("feed/export")]
    public async Task<IActionResult> ExportFeed(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? className,
        [FromQuery] string? reasonId, [FromQuery] string? author, [FromQuery] string? sign,
        [FromQuery] string? source, [FromQuery] string? search)
    {
        var (feed, error) = await BuildFeedAsync(new FeedFilters(from, to, className, reasonId, author, sign, source, search));
        if (error is not null) return BadRequest(new { message = error });

        var headers = new[] { "Sana", "F.I.SH.", "Sinf", "Turi", "Sabab", "Ball", "Izoh", "Kim qo'ydi", "Manba" };
        var cells = feed.Rows.Select(r => (IReadOnlyList<string>)new[]
        {
            ExportTime(r.CreatedAt), r.FullName, r.ClassName,
            r.Points < 0 ? "Salbiy" : "Ijobiy", r.ReasonName,
            Signed(r.Points), r.Note, r.CreatedBy,
            r.Source == "attendance" ? "Jurnal davomati" : "Qo'lda",
        });

        var bytes = ExcelExport.Build("Harakatlar", headers, cells);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"harakatlar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>"2026-10-15T10:12:00.000" → "15.10.2026 10:12"; a journal day stays a day.</summary>
    private static string ExportTime(string iso)
    {
        if (iso.Length < 10 || !DateOnly.TryParseExact(iso[..10], "yyyy-MM-dd", out var day)) return iso;
        var text = day.ToString("dd.MM.yyyy");
        return iso.Length >= 16 && iso[10] == 'T' ? $"{text} {iso.Substring(11, 5)}" : text;
    }

    /// <summary>The feed's query string, shared by the page and its export.</summary>
    private sealed record FeedFilters(
        string? From, string? To, string? ClassName, string? ReasonId, string? Author,
        string? Sign, string? Source, string? Search);

    /// <summary>
    /// Every feed row matching the filters, newest first, plus the class list for the filter. The rules
    /// are described on <see cref="GetFeed"/>; the error is a 400 message (bad or reversed dates).
    /// </summary>
    private async Task<((List<DisciplineFeedRowDto> Rows, List<string> ClassNames) Feed, string? Error)> BuildFeedAsync(
        FeedFilters f)
    {
        if (!TryDay(f.From, out var fromDay)) return (default, "Davr boshi noto'g'ri (YYYY-MM-DD)");
        if (!TryDay(f.To, out var toDay)) return (default, "Davr oxiri noto'g'ri (YYYY-MM-DD)");
        if (fromDay is not null && toDay is not null && toDay < fromDay)
            return (default, "Davr oxiri boshidan oldin bo'lishi mumkin emas");
        if (fromDay is null && toDay is null) fromDay = AppClock.Today.AddDays(-DefaultWindowDays);

        var fromText = fromDay?.ToString("yyyy-MM-dd");
        var toText = toDay?.ToString("yyyy-MM-dd");
        // Qo'lda kiritilgan yozuvda vaqt ham bor ("2026-09-16T10:12:..."), shuning uchun yuqori
        // chegara — ERTANGI kun va qat'iy kichik. Aks holda oxirgi kunning yozuvlari tushib qolardi.
        var toExclusive = toDay?.AddDays(1).ToString("yyyy-MM-dd");

        var students = await db.Students.AsNoTracking()
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.IsArchived }).ToListAsync();
        var byStudent = students.ToDictionary(s => s.Id);

        var wantClass = string.IsNullOrWhiteSpace(f.ClassName) || f.ClassName == "all" ? null : f.ClassName.Trim();
        var term = (f.Search ?? "").Trim();
        var (reasonId, author, sign, source) = (f.ReasonId, f.Author, f.Sign, f.Source);

        // Arxivlangan o'quvchi ham chiqadi: uning yozuvi TARIX, va u sinfdan ketgani bilan
        // o'sha kuni bo'lgan voqea bo'lmagan bo'lib qolmaydi.
        (string FullName, string ClassName)? Student(string id)
        {
            if (!byStudent.TryGetValue(id, out var s)) return null;
            if (wantClass is not null && s.ClassName != wantClass) return null;
            if (term.Length > 0 && !s.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)) return null;
            return (s.FullName, s.ClassName);
        }

        var rows = new List<DisciplineFeedRowDto>();

        if (source != "attendance")
        {
            var q = db.DisciplinePoints.AsNoTracking().AsQueryable();
            if (fromText is { } start) q = q.Where(p => string.Compare(p.CreatedAt, start) >= 0);
            if (toExclusive is { } end) q = q.Where(p => string.Compare(p.CreatedAt, end) < 0);
            if (!string.IsNullOrWhiteSpace(reasonId)) q = q.Where(p => p.ReasonId == reasonId);
            if (!string.IsNullOrWhiteSpace(author)) q = q.Where(p => p.CreatedBy == author);
            if (sign == "positive") q = q.Where(p => p.Points > 0);
            else if (sign == "negative") q = q.Where(p => p.Points < 0);

            var drNames = await db.DisciplineReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);
            foreach (var p in await q.ToListAsync())
            {
                if (Student(p.StudentId) is not { } s) continue;
                rows.Add(new DisciplineFeedRowDto(
                    p.Id, p.StudentId, s.FullName, s.ClassName,
                    string.IsNullOrEmpty(p.ReasonName) ? drNames.GetValueOrDefault(p.ReasonId, "—") : p.ReasonName,
                    p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual", p.ReasonId, p.UpdatedAt, p.UpdatedBy));
            }
        }

        // Jurnal davomatida MUALLIF yo'q (belgini o'qituvchi jurnalda qo'yadi, yozuvda saqlanmaydi) —
        // shuning uchun xodim bo'yicha filtr tanlanganda bu manba umuman qatnashmaydi.
        if (source != "manual" && string.IsNullOrWhiteSpace(author))
        {
            var absReasons = await db.AbsenceReasons.AsNoTracking()
                .ToDictionaryAsync(r => r.Id, r => new { r.Name, r.Points });
            var q = db.JournalEntries.AsNoTracking().Where(e => e.ReasonId != null);
            if (fromText is { } start) q = q.Where(e => string.Compare(e.Date, start) >= 0);
            if (toText is { } end) q = q.Where(e => string.Compare(e.Date, end) <= 0);
            if (!string.IsNullOrWhiteSpace(reasonId)) q = q.Where(e => e.ReasonId == reasonId);

            // Bitta o'quvchi, bitta kun, bitta sabab — lentada bitta qator (AttendanceDisciplineDays).
            var marks = await q.Select(e => new AttendanceDisciplineDays.Mark(e.Id, e.StudentId, e.Date, e.ReasonId!)).ToListAsync();
            foreach (var d in AttendanceDisciplineDays.Collapse(marks))
            {
                if (!absReasons.TryGetValue(d.ReasonId, out var r) || r.Points == 0) continue;
                if (sign == "positive" && r.Points < 0) continue;
                if (sign == "negative" && r.Points > 0) continue;
                if (Student(d.StudentId) is not { } s) continue;
                rows.Add(new DisciplineFeedRowDto(
                    d.Id, d.StudentId, s.FullName, s.ClassName, r.Name, r.Points,
                    AttendanceDisciplineDays.Note(d.Lessons), d.Date, "", "attendance"));
            }
        }

        // Ikki manba bitta kalit bilan tartiblanadi: qo'lda kiritilganda to'liq vaqt bor,
        // jurnalda faqat sana — ISO satr sifatida taqqoslash ikkalasida ham to'g'ri ishlaydi.
        var ordered = rows
            .OrderByDescending(r => r.CreatedAt, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        var classNames = students
            .Where(s => !s.IsArchived && !string.IsNullOrWhiteSpace(s.ClassName))
            .Select(s => s.ClassName).Distinct()
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

        return ((ordered, classNames), null);
    }

    private static bool TryDay(string? value, out DateOnly? day)
    {
        day = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", out var parsed)) return false;
        day = parsed;
        return true;
    }

    // ---------- Harakatni tahrirlash (EduSchool editBehaviorIncidents) ----------

    /// <summary>
    /// Edits one manual incident — reason and note (<see cref="UpdateDisciplinePointRequest"/>). Points
    /// follow the reason: a different reason re-snapshots its name and points and must be active; the
    /// same reason keeps the stored snapshot. No parent message on an edit — the parent heard about the
    /// incident when it was recorded. Audited (before / after) on the pupil's activity history.
    /// </summary>
    [HttpPut("points/{id}")]
    public async Task<ActionResult<DisciplinePointDto>> UpdatePoint(
        string id, UpdateDisciplinePointRequest req, CancellationToken ct = default)
    {
        var p = await db.DisciplinePoints.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { message = "Yozuv topilmadi" });

        var archived = await ArchivedStudents.NamesAsync(db, [p.StudentId], ct);
        if (archived.Count > 0)
            return Conflict(new { code = ArchivedStudents.Code, message = ArchivedStudents.Message(archived) });

        var reasonId = (req.ReasonId ?? "").Trim();
        if (reasonId.Length == 0) return BadRequest(new { message = "Sabab tanlanmadi" });

        var before = new { p.ReasonId, p.ReasonName, p.Points, p.Note };
        if (reasonId != p.ReasonId)
        {
            var dr = await db.DisciplineReasons.FindAsync([reasonId], ct);
            if (dr is not null)
            {
                if (!dr.IsActive) return BadRequest(new { message = "Bu sabab faol emas" });
                if (!MayUse(dr)) return NotOwner();
                // The entry keeps its date, so the window is the one it was recorded in.
                var day = p.CreatedAt.Length >= 10 && DateOnly.TryParseExact(p.CreatedAt[..10], "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
                    ? d : AppClock.Today;
                if ((await DisciplineIncidents.PeriodTakenAsync(db, dr, [p.StudentId], day, p.Id, ct)).Count > 0)
                    return Conflict(new { message = DisciplineIncidents.PeriodTakenMessage(dr) });
                (p.ReasonName, p.Points) = (dr.Name, dr.Points);
            }
            else
            {
                var ar = await db.AbsenceReasons.FindAsync([reasonId], ct);
                if (ar is null) return BadRequest(new { message = "Sabab tanlanmadi" });
                (p.ReasonName, p.Points) = (ar.Name, ar.Points);
            }
            p.ReasonId = reasonId;
        }
        p.Note = (req.Note ?? "").Trim();

        if (before.ReasonId != p.ReasonId || before.Note != p.Note)
        {
            var user = await db.Users.FindAsync([Uid], ct);
            p.UpdatedAt = AppClock.Now.ToString("o");
            p.UpdatedBy = user?.FullName ?? "Administrator";
            audit.Record(AuditService.EntityDisciplinePoint, p.Id, "update",
                $"Xulq-atvor yozuvi tahrirlandi: {p.ReasonName} ({Signed(p.Points)}) — {await StudentNameAsync(p.StudentId, ct)}",
                before: before, after: new { p.ReasonId, p.ReasonName, p.Points, p.Note },
                studentId: p.StudentId);
            await db.SaveChangesAsync(ct);
        }

        return new DisciplinePointDto(
            p.Id, p.StudentId, p.ReasonName, p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual", 0,
            p.ReasonId, p.UpdatedAt, p.UpdatedBy);
    }

    /// <summary>Qo'lda kiritilgan ball yozuvini o'chiradi (jurnal davomatini emas). Audited.</summary>
    [HttpDelete("points/{id}")]
    public async Task<IActionResult> DeletePoint(string id)
    {
        var p = await db.DisciplinePoints.FindAsync(id);
        if (p is not null)
        {
            audit.Record(AuditService.EntityDisciplinePoint, p.Id, "delete",
                $"Xulq-atvor yozuvi o'chirildi: {p.ReasonName} ({Signed(p.Points)}) — {await StudentNameAsync(p.StudentId)}",
                before: new { p.ReasonId, p.ReasonName, p.Points, p.Note, p.CreatedAt, p.CreatedBy },
                studentId: p.StudentId);
            db.DisciplinePoints.Remove(p);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    private async Task<string> StudentNameAsync(string studentId, CancellationToken ct = default) =>
        await db.Students.AsNoTracking().Where(s => s.Id == studentId)
            .Select(s => s.FullName).FirstOrDefaultAsync(ct) ?? studentId;

    private static string Signed(int points) => points > 0 ? $"+{points}" : points.ToString();

    // ---------- Belgilanmagan sinflar (EduSchool getNotAssignedClasses) ----------

    /// <summary>
    /// Coverage: for every class with active pupils, how many manual incidents they got in the period
    /// (both days included; default — from the 1st of the current month to today). Classes with none —
    /// "belgilanmagan sinflar" — come first. Attendance-reason points do not count: they come from the
    /// nurse's roll-call, not from someone recording behaviour. A pupil counts in their current class.
    /// </summary>
    [HttpGet("coverage")]
    public async Task<ActionResult<DisciplineCoverageDto>> Coverage(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct = default)
    {
        if (!TryDay(from, out var fromDay)) return BadRequest(new { message = "Davr boshi noto'g'ri (YYYY-MM-DD)" });
        if (!TryDay(to, out var toDay)) return BadRequest(new { message = "Davr oxiri noto'g'ri (YYYY-MM-DD)" });
        var last = toDay ?? AppClock.Today;
        var first = fromDay ?? new DateOnly(last.Year, last.Month, 1);
        if (last < first) return BadRequest(new { message = "Davr oxiri boshidan oldin bo'lishi mumkin emas" });

        var fromText = first.ToString("yyyy-MM-dd");
        var toExclusive = last.AddDays(1).ToString("yyyy-MM-dd");

        var classes = await db.Classes.AsNoTracking().Where(c => !c.IsArchived)
            .Select(c => new { c.Id, c.Name, c.Grade }).ToListAsync(ct);
        var students = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && s.ClassName != "")
            .Select(s => new { s.Id, s.ClassName }).ToListAsync(ct);
        var classOf = students.ToDictionary(s => s.Id, s => s.ClassName, StringComparer.Ordinal);
        var pupils = students.GroupBy(s => s.ClassName).ToDictionary(g => g.Key, g => g.Count());

        var points = await db.DisciplinePoints.AsNoTracking()
            .Where(p => string.Compare(p.CreatedAt, fromText) >= 0 && string.Compare(p.CreatedAt, toExclusive) < 0)
            .Select(p => new { p.StudentId, p.Points, p.CreatedAt }).ToListAsync(ct);
        var byClass = points
            .Where(p => classOf.ContainsKey(p.StudentId))
            .GroupBy(p => classOf[p.StudentId])
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = classes
            .Where(c => pupils.ContainsKey(c.Name))
            .Select(c =>
            {
                var list = byClass.GetValueOrDefault(c.Name) ?? [];
                return new DisciplineCoverageRowDto(
                    c.Id, c.Name, c.Grade, pupils[c.Name], list.Count,
                    list.Count(p => p.Points > 0), list.Count(p => p.Points < 0),
                    list.Count == 0 ? null : list.Max(p => p.CreatedAt));
            })
            .OrderBy(r => r.Incidents == 0 ? 0 : 1)
            .ThenBy(r => r.Grade).ThenBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DisciplineCoverageDto(
            fromText, last.ToString("yyyy-MM-dd"), rows.Count, rows.Count(r => r.Incidents == 0), rows);
    }

    /// <summary>A point as the audit shows it (Uzbek keys, audit-log.md).</summary>
    private static Dictionary<string, object?> PointView(DisciplinePoint p) => new()
    {
        ["Sabab"] = p.ReasonName, ["Ball"] = p.Points, ["Izoh"] = p.Note, ["Qo'ygan"] = p.CreatedBy,
    };
}
