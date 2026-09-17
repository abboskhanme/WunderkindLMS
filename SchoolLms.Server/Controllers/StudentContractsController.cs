using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quvchi shartnomalari reyestri — docs/modules/students-parity.md §2.10
/// (K-1 yozuv va ro'yxat, K-2 andozadan hosil qilish, K-3 imzolangan nusxa).
///
/// <para>
/// <b>Mavjud generator TEGILMAGAN.</b> <c>ContractsController</c> ota-ona
/// guruhiga (bir telefon — bir nechta farzand) Telegram orqali to'ldirilgan
/// .docx yuborishda davom etadi va <c>contracts</c> jadvaliga o'zgarishsiz
/// yozadi. Bu yerdagi yozuv — BOSHQA narsa: bitta BOLA bilan tuzilgan
/// shartnomaning o'zi.
/// </para>
/// <para>
/// <b>Nega yuborish oqimi bu yozuvni o'zi yozmaydi.</b>
/// <c>ux_student_contracts_number</c> — qisman UNIKAL indeks: bitta raqam
/// bitta yozuvda. Ikki farzandli ota-onaga bitta raqam bilan bitta fayl
/// ketadi, ya'ni bir raqamdan ikkita qator yozilishi kerak bo'lardi va
/// ikkinchisi 23505 bilan yiqilardi. Shuning uchun o'quvchi shartnomasi
/// ALOHIDA, bitta bola uchun hosil qilinadi (§2.10.1 dagi EduSchool oqimi
/// ham aynan shunday: <c>/students/contract/:id</c>).
/// </para>
/// <para>
/// <b>Pul yo'q.</b> Summa, to'lov turi va to'lov kuni bu yerga KIRMAYDI —
/// ular obunada (SPEC §3.7). Shartnoma yozuvi hujjatdan iborat.
/// </para>
/// <para>
/// <b>Ruxsat — <c>contracts</c>:</b> mavjud Shartnomalar ekrani bilan bir xil
/// kalit, chunki bu o'sha ekranning uchinchi tab'i.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("contracts")]
[Route("api/admin/student-contracts")]
public class StudentContractsController(
    AppDbContext db, ContractService contracts, AuditService audit) : ControllerBase
{
    /// <summary>
    /// Audit yozuvidagi entity turi. <c>AuditService.cs</c> shu to'lqinda
    /// muzlatilgan (bir nechta slice unga tegadi), shuning uchun konstanta shu
    /// yerda; wiring bosqichida u <c>AuditService</c> ga ko'chiriladi —
    /// <c>EntityCertificate</c> bilan yonma-yon.
    /// </summary>
    public const string AuditEntity = AuditService.EntityStudentContract;

    public const string StudentRequiredMessage = "O'quvchini tanlang";
    public const string StudentNotFoundMessage = "O'quvchi topilmadi";
    public const string NumberTakenMessage = "Bu shartnoma raqami allaqachon ishlatilgan";
    public const string PeriodMessage = "Tugash sanasi imzo sanasidan oldin bo'lmasin";
    public const string DateFormatMessage = "Sana YYYY-MM-DD ko'rinishida bo'lsin";
    public const string SourceMessage = "Manba 'generated' yoki 'uploaded' bo'lsin";
    public const string TemplateNotFoundMessage = "Andoza topilmadi";
    public const string TemplateFileMissingMessage = "Andoza fayli topilmadi";

    /// <summary><c>draft</c> — raqamsiz, <c>expired</c> — muddati o'tgan, <c>active</c> — qolgani.</summary>
    public const string StatusDraft = "draft";
    public const string StatusActive = "active";
    public const string StatusExpired = "expired";

    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 500;

    // =====================================================================
    //  1. REYESTR
    // =====================================================================

    /// <summary>
    /// Reyestr: qidiruv, sinf, manba, fayl bor/yo'q, holat va imzo sanasi
    /// oralig'i bo'yicha filtr (§2.10.3 K-1).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<StudentContractPageDto>> Search(
        [FromQuery] StudentContractFilter filter, CancellationToken ct = default)
    {
        if (!TryDate(filter.From, out var from)) return BadRequest(new { message = DateFormatMessage });
        if (!TryDate(filter.To, out var to)) return BadRequest(new { message = DateFormatMessage });

        var today = AppClock.Today;

        var q = from c in db.StudentContracts.AsNoTracking()
                join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
                select new { Contract = c, Student = s };

        if (!string.IsNullOrWhiteSpace(filter.StudentId))
            q = q.Where(x => x.Contract.StudentId == filter.StudentId);

        if (!string.IsNullOrWhiteSpace(filter.ClassName))
            q = q.Where(x => x.Student.ClassName == filter.ClassName);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = $"%{filter.Search.Trim()}%";
            q = q.Where(x => EF.Functions.ILike(x.Student.FullName, term)
                || (x.Contract.Number != null && EF.Functions.ILike(x.Contract.Number, term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Source))
        {
            if (!StudentContractSource.IsValid(filter.Source))
                return BadRequest(new { message = SourceMessage });
            q = q.Where(x => x.Contract.Source == filter.Source);
        }

        if (filter.HasFile is { } hasFile)
            q = hasFile
                ? q.Where(x => x.Contract.FileUrl != null)
                : q.Where(x => x.Contract.FileUrl == null);

        q = filter.Status switch
        {
            StatusDraft => q.Where(x => x.Contract.Number == null),
            StatusExpired => q.Where(x => x.Contract.Number != null
                && x.Contract.EndsOn != null && x.Contract.EndsOn < today),
            StatusActive => q.Where(x => x.Contract.Number != null
                && (x.Contract.EndsOn == null || x.Contract.EndsOn >= today)),
            _ => q,
        };

        if (from is { } f) q = q.Where(x => x.Contract.SignedOn != null && x.Contract.SignedOn >= f);
        if (to is { } t) q = q.Where(x => x.Contract.SignedOn != null && x.Contract.SignedOn <= t);

        var total = await q.CountAsync(ct);

        var page = Math.Max(1, filter.Page ?? 1);
        var pageSize = Math.Clamp(filter.PageSize ?? DefaultPageSize, 1, MaxPageSize);

        // Sanasi yo'q yozuv (qoralama) — oxirida. Postgres DESC da NULL'ni
        // BIRINCHI qo'yadi, ya'ni aniq aytilmasa qoralamalar tepaga chiqib
        // ketardi.
        var rows = await q
            .OrderBy(x => x.Contract.SignedOn == null)
            .ThenByDescending(x => x.Contract.SignedOn)
            .ThenByDescending(x => x.Contract.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = await ToDtosAsync(rows.Select(r => (r.Contract, r.Student)).ToList(), today, ct);
        return new StudentContractPageDto(items, total, page, pageSize);
    }

    /// <summary>
    /// Bitta o'quvchining shartnoma tarixi — kartochkadagi tab (§2.10.3 K-1).
    /// Marshrut <c>~/</c> bilan: yozuv o'quvchiga tegishli, lekin
    /// <c>StudentsController</c> boshqa slice'ning fayli.
    /// </summary>
    [HttpGet("~/api/admin/students/{studentId}/contracts")]
    public async Task<ActionResult<IEnumerable<StudentContractDto>>> ForStudent(
        string studentId, CancellationToken ct = default)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound();

        var rows = await db.StudentContracts.AsNoTracking()
            .Where(c => c.StudentId == studentId)
            .OrderBy(c => c.SignedOn == null)
            .ThenByDescending(c => c.SignedOn)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return (await ToDtosAsync(
            rows.Select(c => (c, student)).ToList(), AppClock.Today, ct)).ToList();
    }

    // =====================================================================
    //  2. YOZUV (K-1, K-3)
    // =====================================================================

    /// <summary>
    /// Yangi yozuv. Fayl AVVAL <c>POST /api/admin/uploads</c> orqali yuklanadi
    /// (K-3) va bu yerga faqat <c>/uploads/...</c> manzili keladi.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<StudentContractDto>> Create(
        SaveStudentContractRequest req, CancellationToken ct = default)
    {
        var studentId = (req.StudentId ?? "").Trim();
        if (studentId.Length == 0) return BadRequest(new { message = StudentRequiredMessage });

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return BadRequest(new { message = StudentNotFoundMessage });

        var source = (req.Source ?? "").Trim();
        // Qo'lda kiritilgan yozuv sukut bo'yicha "yuklangan": generator uni
        // hosil qilmagan, odam qog'ozdan ko'chirgan.
        if (source.Length == 0) source = StudentContractSource.Uploaded;
        if (!StudentContractSource.IsValid(source)) return BadRequest(new { message = SourceMessage });

        if (!TryDate(req.SignedOn, out var signedOn)) return BadRequest(new { message = DateFormatMessage });
        if (!TryDate(req.EndsOn, out var endsOn)) return BadRequest(new { message = DateFormatMessage });
        if (endsOn is { } e && signedOn is { } s2 && e < s2) return BadRequest(new { message = PeriodMessage });

        var number = Blank(req.Number);
        if (number is not null && await NumberTakenAsync(number, null, ct))
            return BadRequest(new { message = NumberTakenMessage });

        var templateId = Blank(req.TemplateId);
        if (templateId is not null && !await db.ContractTemplates.AnyAsync(t => t.Id == templateId, ct))
            return BadRequest(new { message = TemplateNotFoundMessage });

        var row = new StudentContract
        {
            StudentId = studentId,
            TemplateId = templateId,
            Number = number,
            SignedOn = signedOn,
            EndsOn = endsOn,
            FileUrl = Blank(req.FileUrl),
            Source = source,
            Comment = Blank(req.Comment),
            CreatedBy = CurrentUserId(),
            CreatedAt = AppClock.NowInstant,
        };
        db.StudentContracts.Add(row);

        audit.Record(AuditEntity, row.Id.ToString(), "create",
            $"Shartnoma qo'shildi: № {number ?? "raqamsiz"} ({student.FullName})",
            after: new { row.Number, SignedOn = req.SignedOn, row.Source },
            studentId: student.Id);

        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([(row, student)], AppClock.Today, ct))[0];
    }

    /// <summary>Yozuvni tahrirlash. Berilmagan maydon TEGILMAYDI.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StudentContractDto>> Update(
        Guid id, SaveStudentContractRequest req, CancellationToken ct = default)
    {
        var row = await db.StudentContracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return NotFound();

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == row.StudentId, ct);
        if (student is null) return NotFound();

        if (req.Source is not null)
        {
            var source = req.Source.Trim();
            if (!StudentContractSource.IsValid(source)) return BadRequest(new { message = SourceMessage });
            row.Source = source;
        }

        if (!TryDate(req.SignedOn, out var signedOn)) return BadRequest(new { message = DateFormatMessage });
        if (!TryDate(req.EndsOn, out var endsOn)) return BadRequest(new { message = DateFormatMessage });
        // Bo'sh satr — "tozala"; berilmagan (null) — "tegma".
        if (req.SignedOn is not null) row.SignedOn = signedOn;
        if (req.EndsOn is not null) row.EndsOn = endsOn;
        if (row.EndsOn is { } e && row.SignedOn is { } s && e < s)
            return BadRequest(new { message = PeriodMessage });

        if (req.Number is not null)
        {
            var number = Blank(req.Number);
            if (number is not null && await NumberTakenAsync(number, id, ct))
                return BadRequest(new { message = NumberTakenMessage });
            row.Number = number;
        }

        if (req.TemplateId is not null)
        {
            var templateId = Blank(req.TemplateId);
            if (templateId is not null && !await db.ContractTemplates.AnyAsync(t => t.Id == templateId, ct))
                return BadRequest(new { message = TemplateNotFoundMessage });
            row.TemplateId = templateId;
        }

        if (req.FileUrl is not null) row.FileUrl = Blank(req.FileUrl);
        if (req.Comment is not null) row.Comment = Blank(req.Comment);

        audit.Record(AuditEntity, row.Id.ToString(), "update",
            $"Shartnoma tahrirlandi: № {row.Number ?? "raqamsiz"} ({student.FullName})",
            after: new { row.Number, SignedOn = req.SignedOn, row.Source },
            studentId: student.Id);

        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([(row, student)], AppClock.Today, ct))[0];
    }

    /// <summary>
    /// Yozuvni o'chirish. Faylning O'ZI <c>uploads/</c> da qoladi — mavjud
    /// andoza o'chirish ham shunday ishlaydi va bir fayl bir nechta yozuvda
    /// ko'rsatilgan bo'lishi mumkin.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var row = await db.StudentContracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return NoContent();

        var name = await db.Students.AsNoTracking()
            .Where(s => s.Id == row.StudentId).Select(s => s.FullName).FirstOrDefaultAsync(ct);

        audit.Record(AuditEntity, row.Id.ToString(), "delete",
            $"Shartnoma o'chirildi: № {row.Number ?? "raqamsiz"} ({name ?? row.StudentId})",
            before: new { row.Number, row.Source },
            studentId: row.StudentId);

        db.StudentContracts.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // =====================================================================
    //  3. ANDOZADAN HOSIL QILISH (K-2)
    // =====================================================================

    /// <summary>
    /// Hosil qilishdan oldingi ko'rinish: keyingi bo'sh raqam, bugungi sana va
    /// andozaga tushadigan qiymatlar. Forma shu javob bilan to'ldiriladi va
    /// foydalanuvchi raqam/sanani o'zgartira oladi (§2.10.1 "review-and-edit").
    /// </summary>
    [HttpGet("preview")]
    public async Task<ActionResult<StudentContractPreviewDto>> Preview(
        [FromQuery] string studentId, CancellationToken ct = default)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound();

        var today = AppClock.Today;
        var number = await NextNumberAsync(ct);
        var tokens = StudentContractTokens.Build(
            student, await GuardiansAsync(student.Id, ct), number, today, null);

        return new StudentContractPreviewDto(
            student.Id, student.FullName, student.ClassName,
            number, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tokens.Select(kv => new ContractTokenDto(kv.Key, kv.Value)).ToList());
    }

    /// <summary>
    /// Andozani bitta o'quvchi uchun to'ldiradi, hosil bo'lgan .docx ni saqlaydi
    /// va reyestrga yozuv qo'shadi (K-2). Telegram orqali HECH NARSA
    /// yuborilmaydi — yuborish mavjud "Shartnomalar" ekranining ishi.
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult<StudentContractDto>> Generate(
        GenerateStudentContractRequest req, CancellationToken ct = default)
    {
        var studentId = (req.StudentId ?? "").Trim();
        if (studentId.Length == 0) return BadRequest(new { message = StudentRequiredMessage });

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return BadRequest(new { message = StudentNotFoundMessage });

        var templateId = Blank(req.TemplateId);
        var template = templateId is null
            ? null
            : await db.ContractTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null) return BadRequest(new { message = TemplateNotFoundMessage });

        var bytes = contracts.ReadTemplate(template.FileUrl);
        if (bytes is null) return BadRequest(new { message = TemplateFileMissingMessage });

        if (!TryDate(req.SignedOn, out var signedOnOrNull)) return BadRequest(new { message = DateFormatMessage });
        if (!TryDate(req.EndsOn, out var endsOn)) return BadRequest(new { message = DateFormatMessage });
        var signedOn = signedOnOrNull ?? AppClock.Today;
        if (endsOn is { } e && e < signedOn) return BadRequest(new { message = PeriodMessage });

        var number = Blank(req.Number) ?? await NextNumberAsync(ct);
        if (await NumberTakenAsync(number, null, ct))
            return BadRequest(new { message = NumberTakenMessage });

        // Fayl YOZUVDAN OLDIN yoziladi, chunki uning manzili yozuvning bir
        // qismi. Tekshiruvlar (o'quvchi, andoza, sana, raqam) shu satrgacha
        // bajarilgan, ya'ni bu yerdan keyin yiqilish uchun deyarli sabab yo'q;
        // qolgani — ikki hamkasb bir soniyada bir xil raqamni olishi, bunda
        // baza indeksi ushlaydi va `uploads/` da egasiz .docx qoladi. Faylni
        // o'chirish o'rniga shu holat qabul qilindi: andoza fayllari ham
        // shunday turadi va egasiz hujjatni keyin ko'rib chiqish mumkin,
        // noto'g'ri o'chirilganini esa qaytarib bo'lmaydi.
        var tokens = StudentContractTokens.Build(
            student, await GuardiansAsync(student.Id, ct), number, signedOn, endsOn);
        var fileUrl = contracts.SaveGenerated(contracts.FillTemplate(bytes, tokens));

        var row = new StudentContract
        {
            StudentId = student.Id,
            TemplateId = template.Id,
            Number = number,
            SignedOn = signedOn,
            EndsOn = endsOn,
            FileUrl = fileUrl,
            Source = StudentContractSource.Generated,
            Comment = Blank(req.Comment),
            CreatedBy = CurrentUserId(),
            CreatedAt = AppClock.NowInstant,
        };
        db.StudentContracts.Add(row);

        audit.Record(AuditEntity, row.Id.ToString(), "create",
            $"Shartnoma hosil qilindi: № {number} ({student.FullName})",
            after: new { row.Number, SignedOn = signedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            studentId: student.Id);

        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([(row, student)], AppClock.Today, ct))[0];
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>
    /// Keyingi shartnoma raqami.
    ///
    /// <para>
    /// <b>Qoida:</b> maktabda shartnoma raqami BITTA ketma-ketlik. Shuning
    /// uchun keyingisi = mavjud ikkala manbadagi eng katta BUTUN sondan bitta
    /// katta: <c>contracts.number</c> (mavjud generator yuborgan shartnomalar)
    /// va <c>student_contracts.number</c> ning sof raqamli qiymatlari. Raqamsiz
    /// yoki raqam bo'lmagan ("2026/14-A" kabi qo'lda kiritilgan) qiymatlar
    /// hisobga olinmaydi, lekin BAND deb qaraladi — natija band bo'lsa,
    /// bo'shigacha oshiriladi.
    /// </para>
    /// <para>
    /// Avtomatik/qo'lda tanlash sozlamasi (K-6) — P2, Batch C. Bugun raqam
    /// kiritilsa qo'lda, kiritilmasa avtomatik ishlaydi.
    /// </para>
    /// </summary>
    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var sent = await db.Contracts.AsNoTracking().AnyAsync(ct)
            ? await db.Contracts.AsNoTracking().MaxAsync(c => c.Number, ct)
            : 0;

        var used = await db.StudentContracts.AsNoTracking()
            .Where(c => c.Number != null).Select(c => c.Number!).ToListAsync(ct);

        var own = used
            .Select(n => int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();

        var taken = used.ToHashSet(StringComparer.Ordinal);
        var next = Math.Max(sent, own) + 1;
        while (taken.Contains(next.ToString(CultureInfo.InvariantCulture))) next++;
        return next.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<bool> NumberTakenAsync(string number, Guid? exceptId, CancellationToken ct) =>
        await db.StudentContracts.AsNoTracking()
            .AnyAsync(c => c.Number == number && (exceptId == null || c.Id != exceptId), ct);

    private async Task<List<(string Relation, string FullName, string Phone)>> GuardiansAsync(
        string studentId, CancellationToken ct)
    {
        var rows = await (from sg in db.StudentGuardians.AsNoTracking()
                          join g in db.Guardians.AsNoTracking() on sg.GuardianId equals g.Id
                          where sg.StudentId == studentId
                          orderby sg.IsPrimary descending, g.FullName
                          select new { sg.Relation, g.FullName, g.Phone }).ToListAsync(ct);
        return rows.Select(r => (r.Relation, r.FullName, r.Phone)).ToList();
    }

    private async Task<List<StudentContractDto>> ToDtosAsync(
        List<(StudentContract Contract, Student Student)> rows, DateOnly today, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var templateIds = rows.Select(r => r.Contract.TemplateId).OfType<string>().Distinct().ToList();
        var templates = templateIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.ContractTemplates.AsNoTracking()
                .Where(t => templateIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name.Length > 0 ? t.Name : t.FileName, ct);

        var userIds = rows.Select(r => r.Contract.CreatedBy).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return rows.Select(r => new StudentContractDto(
            r.Contract.Id,
            r.Contract.StudentId,
            r.Student.FullName,
            r.Student.ClassName,
            r.Contract.TemplateId,
            r.Contract.TemplateId is { } tid ? templates.GetValueOrDefault(tid) : null,
            r.Contract.Number,
            Iso(r.Contract.SignedOn),
            Iso(r.Contract.EndsOn),
            r.Contract.FileUrl,
            r.Contract.Source,
            StatusOf(r.Contract, today),
            r.Contract.Comment,
            r.Contract.CreatedBy,
            users.GetValueOrDefault(r.Contract.CreatedBy),
            r.Contract.CreatedAt.ToString("o"))).ToList();
    }

    /// <summary>Holat — HISOBLANADI (jadvalda bunday ustun yo'q).</summary>
    private static string StatusOf(StudentContract c, DateOnly today) =>
        c.Number is null ? StatusDraft
        : c.EndsOn is { } e && e < today ? StatusExpired
        : StatusActive;

    private static string? Iso(DateOnly? d) =>
        d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Bo'sh/probel satr — null (ustunlar "yo'q" ni null bilan ko'rsatadi).</summary>
    private static string? Blank(string? value)
    {
        var v = (value ?? "").Trim();
        return v.Length == 0 ? null : v;
    }

    /// <summary>
    /// ISO sanani o'qiydi. Bo'sh — <c>null</c> va <c>true</c> ("tozalash"),
    /// buzuq — <c>false</c> (chaqiruvchi 400 qaytaradi).
    /// </summary>
    private static bool TryDate(string? raw, out DateOnly? value)
    {
        value = null;
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return true;
        if (!DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private string CurrentUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? "";
}
