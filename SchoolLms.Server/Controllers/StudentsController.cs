using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/students")]
public class StudentsController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const int MinPasswordLength = 8;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";

    /// <summary>
    /// Faol (arxivlanmagan) o'quvchilar ro'yxati. <paramref name="includeArchived"/>=true bo'lsa
    /// arxivlangan o'quvchilar ham qaytadi.
    ///
    /// <para>
    /// <b>P1-21:</b> javob endi entity emas, <see cref="StudentDto"/> — chunki
    /// <c>balance</c> ustuni o'chdi va qoldiq HISOBLANADI. Butun ro'yxat uchun
    /// bitta <see cref="StudentBalanceQuery.ForManyAsync"/> chaqiriladi (ikkita
    /// so'rov), o'quvchi boshiga so'rov YO'Q.
    /// </para>
    /// </summary>
    /// <param name="certificateTypeIds">
    /// §2.3 — sertifikat turi bo'yicha filtr: vergul bilan ajratilgan id'lar,
    /// ular orasida YOKI ("IELTS yoki SAT bor bolalar").
    /// </param>
    /// <param name="certificateTeacherId">§2.3 — sertifikatni BERGAN o'qituvchi bo'yicha filtr.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentDto>>> GetAll(
        [FromQuery] bool includeArchived = false,
        [FromQuery] string? certificateTypeIds = null,
        [FromQuery] string? certificateTeacherId = null,
        CancellationToken ct = default)
    {
        var q = db.Students.AsNoTracking();
        if (!includeArchived) q = q.Where(s => !s.IsArchived);
        q = WithCertificateFilter(q, certificateTypeIds, certificateTeacherId);
        var students = await q.OrderBy(s => s.FullName).ToListAsync(ct);
        return await WithBalancesAsync(students, ct);
    }

    /// <summary>
    /// §2.3 — sertifikat filtrlari. Ikkalasi ham berilmasa so'rov TEGILMAYDI, ya'ni
    /// sahifa bugungiday ishlaydi (qo'shimcha JOIN ham, qo'shimcha sub-select ham yo'q).
    ///
    /// <para>
    /// Filtrlash SERVERDA, qo'shimcha so'rov (<c>EXISTS</c>) bilan bo'ladi: sertifikat
    /// egalarining id'lari ilovaga tortilmaydi, ya'ni ro'yxat qancha o'ssa ham xotira
    /// bir xil qoladi.
    /// </para>
    /// </summary>
    private IQueryable<Student> WithCertificateFilter(
        IQueryable<Student> q, string? certificateTypeIds, string? certificateTeacherId)
    {
        var holders = CertificateService.HolderStudentIds(
            db, CertificateService.ParseIds(certificateTypeIds), certificateTeacherId);

        return holders is null ? q : q.Where(s => holders.Contains(s.Id));
    }

    /// <summary>O'quvchi shaxsiy daftari — bitta o'quvchi haqida barcha ma'lumot (profil, o'zlashtirish, davomat, intizom, topshiriqlar, oylik baholash, uy vazifa/xulq).</summary>
    [HttpGet("{id}/profile")]
    public async Task<ActionResult<StudentNotebookDto>> GetProfile(string id)
    {
        var st = await db.Students.FirstOrDefaultAsync(s => s.Id == id);
        if (st is null) return NotFound();
        return await StudentProfileBuilder.BuildAsync(db, st);
    }

    /// <summary>
    /// Faqat arxivlangan o'quvchilar ro'yxati (qoldig'i bilan — qarz arxivda ham qarz).
    /// Sertifikat filtrlari bu yerda ham ishlaydi: bitgan bolaning IELTS'i ham hujjat,
    /// va "Arxiv" tab'iga o'tganda filtr jimgina o'chib qolmasligi kerak.
    /// </summary>
    [HttpGet("archived")]
    public async Task<ActionResult<IEnumerable<StudentDto>>> GetArchived(
        [FromQuery] string? certificateTypeIds = null,
        [FromQuery] string? certificateTeacherId = null,
        CancellationToken ct = default)
    {
        var q = WithCertificateFilter(
            db.Students.AsNoTracking().Where(s => s.IsArchived),
            certificateTypeIds, certificateTeacherId);
        var students = await q
            .OrderByDescending(s => s.ArchivedAt).ThenBy(s => s.FullName).ToListAsync(ct);
        return await WithBalancesAsync(students, ct);
    }

    /// <summary>
    /// O'quvchilarni DTO'ga o'girib, har biriga HISOBLANGAN qoldiqni qo'yadi.
    /// Qoldiq bitta partiyada olinadi — sikl ichida so'rov yo'q (N+1 yo'q).
    /// </summary>
    private async Task<List<StudentDto>> WithBalancesAsync(
        IReadOnlyList<Student> students, CancellationToken ct)
    {
        if (students.Count == 0) return [];

        var balances = await new StudentBalanceQuery(db)
            .ForManyAsync([.. students.Select(s => s.Id)], ct);

        return [.. students.Select(s => ToDto(s, balances.GetValueOrDefault(s.Id)))];
    }

    /// <summary>Entity → DTO. Qoldiq har doim tashqaridan beriladi (u ustun emas).</summary>
    private static StudentDto ToDto(Student s, decimal balance) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, balance,
        s.SubGroup,
        s.LastName, s.FirstName, s.MiddleName, s.BirthCertificateUrl,
        s.ParentLastName, s.ParentFirstName, s.ParentMiddleName, s.ParentPassportUrl,
        s.IsArchived, s.ArchivedAt, s.ArchiveReason,
        s.Phone, s.Language, s.DocumentUrl);

    /// <summary>Bo'sh/probel — null; aks holda chetlari kesilgan matn.</summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// §2.3 (S-8) — o'qish tili <c>students.language</c> check constraint'i
    /// bilan bir xil ro'yxatdan bo'lishi kerak. Bo'sh — to'g'ri (ko'rsatilmagan).
    /// </summary>
    internal static string? BadLanguage(string? language)
    {
        var value = (language ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0 || StudentImportService.Languages.Contains(value)) return null;
        return $"O'qish tili noto'g'ri: \"{language!.Trim()}\" "
               + $"({string.Join(" | ", StudentImportService.Languages)})";
    }

    /// <summary>"Familiya Ism Sharifi" — parts'ni birlashtirish (bo'sh qismlar tashlanadi).</summary>
    private static string JoinName(string? last, string? first, string? middle) =>
        string.Join(' ', new[] { last, first, middle }
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrEmpty(x)));

    [HttpPost]
    public async Task<ActionResult<StudentDto>> Create(StudentPayload p)
    {
        if (BadRelation(p.Guardians) is { } badRelation)
            return BadRequest(new { message = badRelation });
        if (BadLanguage(p.Language) is { } badLanguage)
            return BadRequest(new { message = badLanguage });

        var student = AddStudent(p);
        // §2.3 (S-8): forma vasiy ro'yxati bilan kelsa, ASOSIY vasiy eski
        // `parent_*` ustunlariga ko'chadi — shu ikkovi hech qachon
        // ayrilmasligi kerak (ota-ona portali telefon bo'yicha topadi).
        GuardianSync.MirrorPrimaryInput(student, p.Guardians);
        await db.SaveChangesAsync();
        // SPEC §3.2: ota-ona raqamidan vasiy qatorini va bog'lanishni chiqaramiz.
        // Busiz bugun qo'shilgan o'quvchining ota-onasi Telegram Mini App'da
        // hech narsa ko'rmasdi (migratsiyadagi backfill faqat eskilarini ko'chiradi).
        await GuardianSync.EnsureAsync(db, student);
        // §2.3 (S-8) — ikkinchi vasiy va vasiylik turlari. Ro'yxat bo'sh
        // bo'lsa bu qadam hech narsa qilmaydi (bugungi xatti-harakat).
        await GuardianSync.ApplyAsync(db, student, p.Guardians);
        // Yangi o'quvchining qoldig'i 0: obuna hali ochilmagan, hisob-faktura yo'q.
        return ToDto(student, 0m);
    }

    /// <summary>
    /// §2.3 (S-8) — vasiylik turi ro'yxatdan tashqarimi. Tashqari bo'lsa
    /// tushunarli 400 qaytadi: bazadagi <c>ck_student_guardians_relation</c>
    /// ga borib 23514 bilan yiqilish foydalanuvchiga hech narsa aytmasdi.
    /// </summary>
    internal static string? BadRelation(IReadOnlyList<StudentGuardianInput>? guardians)
    {
        foreach (var g in guardians ?? [])
        {
            var value = (g.Relation ?? "").Trim();
            if (value.Length > 0 && !GuardianRelation.IsStorable(value.ToLowerInvariant()))
                return $"Vasiylik turi noto'g'ri: \"{value}\" ({string.Join(" | ", GuardianRelation.Stored)})";
        }
        return null;
    }

    /// <summary>
    /// <see cref="StudentPayload"/>'dan Student yaratib (tizim akkaunti bilan) db kontekstiga
    /// qo'shadi. SaveChanges QILMAYDI — chaqiruvchi (bitta yaratish yoki ommaviy import)
    /// hammasini qo'shib bo'lgach bir marta saqlaydi.
    ///
    /// <para>
    /// <b>P1-21: bu yerda endi PUL YOZILMAYDI.</b> Ilgari o'quvchi yaratilishi bilanoq
    /// kelgan oyidan joriy oygacha <c>monthly_charges</c> qatorlari yozilib, qoldiq
    /// kamayardi. Yangi modelda narxni obuna belgilaydi (SPEC §3.7): admin
    /// "Moliya → Obunalar" da toifa va summani tanlaydi, oylik hisob-fakturani esa
    /// <c>BillingAccrualService</c> yozadi. Ya'ni yangi o'quvchining qarzi 0 bo'lib
    /// tug'iladi va obuna ochilgandan keyin hisoblana boshlaydi.
    /// </para>
    /// </summary>
    private Student AddStudent(StudentPayload p)
    {
        var enrollment = string.IsNullOrWhiteSpace(p.EnrollmentDate)
            ? AppClock.Today.ToString("yyyy-MM-dd")
            : p.EnrollmentDate;

        // FISH parts berilsa ulardan FullName yig'iladi. Aks holda eski yagona FullName ishlatiladi.
        var lastName = (p.LastName ?? "").Trim();
        var firstName = (p.FirstName ?? "").Trim();
        var middleName = (p.MiddleName ?? "").Trim();
        var fullName = (lastName + firstName + middleName) == ""
            ? (p.FullName ?? "").Trim()
            : JoinName(lastName, firstName, middleName);

        var parentLast = (p.ParentLastName ?? "").Trim();
        var parentFirst = (p.ParentFirstName ?? "").Trim();
        var parentMiddle = (p.ParentMiddleName ?? "").Trim();
        var parentFull = (parentLast + parentFirst + parentMiddle) == ""
            ? (p.ParentFullName ?? "").Trim()
            : JoinName(parentLast, parentFirst, parentMiddle);

        var student = new Student
        {
            FullName = fullName,
            LastName = lastName,
            FirstName = firstName,
            MiddleName = middleName,
            BirthDate = p.BirthDate,
            Address = p.Address,
            Gender = p.Gender,
            BirthCertificateUrl = string.IsNullOrWhiteSpace(p.BirthCertificateUrl) ? null : p.BirthCertificateUrl,
            ParentFullName = parentFull,
            ParentLastName = parentLast,
            ParentFirstName = parentFirst,
            ParentMiddleName = parentMiddle,
            ParentPhone = p.ParentPhone,
            ParentPassportUrl = string.IsNullOrWhiteSpace(p.ParentPassportUrl) ? null : p.ParentPassportUrl,
            ClassName = p.ClassName,
            EnrollmentDate = enrollment,
            // §2.3 (S-8) — hammasi ixtiyoriy: yuborilmasa null bo'lib qoladi,
            // ya'ni bugungi payload bilan yaratilgan o'quvchi bugungiday.
            Phone = Trimmed(p.Phone),
            Language = Trimmed(p.Language)?.ToLowerInvariant(),
            DocumentUrl = Trimmed(p.DocumentUrl),
        };
        db.Students.Add(student);

        // O'quvchiga "student" rolli tizim akkaunti generatsiya qilib biriktiramiz.
        var account = AccountFactory.CreateAccountFor(db, "student", student.FullName);
        student.UserId = account.Id;

        return student;
    }

    /// <summary>
    /// O'quvchini tahrirlash.
    ///
    /// <para>
    /// <b>P1-21: chegirma bu yerdan olib tashlandi</b> va u bilan birga
    /// <c>?applyDiscount=</c> parametri ham. Sabab mijoz javobida (SPEC §8.1 Q5):
    /// har qanday chegirma DIREKTOR TASDIG'INI talab qiladi. O'quvchi kartochkasidagi
    /// maydon esa tasdiqsiz chegirma berishning ochiq yo'li edi. Yangi joyi —
    /// <c>POST /api/admin/billing/discounts</c> (tasdiq navbati bilan).
    /// Sinf o'zgarishi ham endi pulga tegmaydi: narxni obuna belgilaydi.
    /// </para>
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, StudentPayload p)
    {
        if (BadRelation(p.Guardians) is { } badRelation)
            return BadRequest(new { message = badRelation });
        if (BadLanguage(p.Language) is { } badLanguage)
            return BadRequest(new { message = badLanguage });

        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        var oldClassName = student.ClassName;

        // O'quvchi FISH — parts berilsa ulardan FullName yig'iladi.
        if (p.LastName is not null || p.FirstName is not null || p.MiddleName is not null)
        {
            student.LastName = (p.LastName ?? "").Trim();
            student.FirstName = (p.FirstName ?? "").Trim();
            student.MiddleName = (p.MiddleName ?? "").Trim();
            student.FullName = JoinName(student.LastName, student.FirstName, student.MiddleName);
        }
        else
        {
            student.FullName = p.FullName;
        }
        student.BirthDate = p.BirthDate;
        student.Address = p.Address;
        student.Gender = p.Gender;
        if (p.BirthCertificateUrl is not null)
            student.BirthCertificateUrl = string.IsNullOrWhiteSpace(p.BirthCertificateUrl) ? null : p.BirthCertificateUrl;
        // Ota-ona FISH — parts berilsa ulardan ParentFullName yig'iladi.
        if (p.ParentLastName is not null || p.ParentFirstName is not null || p.ParentMiddleName is not null)
        {
            student.ParentLastName = (p.ParentLastName ?? "").Trim();
            student.ParentFirstName = (p.ParentFirstName ?? "").Trim();
            student.ParentMiddleName = (p.ParentMiddleName ?? "").Trim();
            student.ParentFullName = JoinName(student.ParentLastName, student.ParentFirstName, student.ParentMiddleName);
        }
        else
        {
            student.ParentFullName = p.ParentFullName;
        }
        student.ParentPhone = p.ParentPhone;
        if (p.ParentPassportUrl is not null)
            student.ParentPassportUrl = string.IsNullOrWhiteSpace(p.ParentPassportUrl) ? null : p.ParentPassportUrl;
        student.ClassName = p.ClassName;
        if (!string.IsNullOrWhiteSpace(p.EnrollmentDate)) student.EnrollmentDate = p.EnrollmentDate;

        // §2.3 (S-8) — YANGI maydonlar. `null` = TEGMA (eski mijoz ularni
        // umuman yubormaydi), bo'sh satr = tozala. Aynan shu qoida bilan
        // yuqoridagi ikkita rasm maydoni ham ishlaydi.
        if (p.Phone is not null) student.Phone = Trimmed(p.Phone);
        if (p.Language is not null) student.Language = Trimmed(p.Language)?.ToLowerInvariant();
        if (p.DocumentUrl is not null) student.DocumentUrl = Trimmed(p.DocumentUrl);

        // Asosiy vasiy va eski `parent_*` ustunlari bir qadamda (S-8).
        GuardianSync.MirrorPrimaryInput(student, p.Guardians);

        // Akkaunt nomini sinxronlaymiz va (ixtiyoriy) yangi parol o'rnatamiz.
        var user = student.UserId is null ? null : await db.Users.FindAsync(student.UserId);
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            var pwd = p.NewPassword.Trim();
            if (pwd.Length < MinPasswordLength) return BadRequest(new { message = WeakPasswordMessage });
            // Akkaunt yo'q bo'lsa — yaratib biriktiramiz.
            user ??= AccountFactory.CreateAccountFor(db, "student", student.FullName);
            student.UserId = user.Id;
            user.SetInitialPassword(pwd);
        }
        if (user is not null) user.FullName = student.FullName;

        // Sinf o'zgarishi PULGA TEGMAYDI (P1-21): narx `student_subscriptions`
        // da, uni "Moliya → Obunalar" ekrani boshqaradi. Sinfni o'zgartirish
        // narxni jimgina qayta yozib yuborsa, obunadagi kelishilgan summa
        // yo'qolardi.
        var classChanged = !string.Equals(oldClassName, student.ClassName, StringComparison.Ordinal);

        // Audit — sinf o'zgarishi. Obuna summasi shu bilan avtomatik
        // o'zgarmaydi; kerak bo'lsa admin obunani alohida tahrirlaydi.
        if (classChanged)
        {
            audit.Record(AuditService.EntityStudentClass, student.Id, "update",
                $"O'quvchi yangilandi (sinf: {oldClassName} → {student.ClassName}) ({student.FullName})",
                before: new { Class = oldClassName },
                after: new { Class = student.ClassName },
                studentId: student.Id);
        }

        // C-1: sinf o'zgarishi endi `class_memberships` da ham qoladi.
        //
        // `students.class_name` HAQIQAT MANBAI bo'lib qoladi — yuqorida u
        // allaqachon yozilgan va hech bir mavjud so'rov o'zgarmaydi. Yangilik
        // shu: o'zgarish sanali a'zolik yozuvida ham ko'rinadi, ya'ni o'quvchi
        // kartochkasidagi "Sinf va guruhlar" tarixi to'g'ri bo'ladi.
        //
        // Nega tranzaksiya: a'zolik xizmati ikki marta saqlaydi (avval eskisini
        // yopadi, keyin yangisini ochadi) —
        // `ux_class_memberships_one_active` qisman unikal indeksi bir lahzada
        // ikkita faol qatorni ko'rmasligi kerak. Ikkalasi bitta tranzaksiyada
        // ketadi, ya'ni yarim o'tkazish bo'lmaydi.
        await using var tx = classChanged ? await db.Database.BeginTransactionAsync() : null;

        await db.SaveChangesAsync();

        if (classChanged)
        {
            await new ClassMembershipService(db)
                .SyncFromClassNameAsync(student, student.ClassName, CurrentUserId);
        }

        if (tx is not null) await tx.CommitAsync();

        // Ota-ona raqami/ismi o'zgargan bo'lishi mumkin — vasiy bog'lanishini tekislaymiz.
        // Bir tomonlama: o'quvchi qatoridan vasiyga (GuardianSync izohiga qarang).
        await GuardianSync.EnsureAsync(db, student);
        // §2.3 (S-8) — ikkinchi vasiy, vasiylik turi va izohi. Ro'yxat
        // yuborilmasa (eski mijoz) bu qadam hech narsa qilmaydi.
        await GuardianSync.ApplyAsync(db, student, p.Guardians);
        return NoContent();
    }

    /// <summary>JWT'dagi foydalanuvchi id'si — a'zolik yozuvining muallifi.</summary>
    private string? CurrentUserId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    /// <summary>
    /// O'quvchi kartochkasining <b>"Sinf va guruhlar"</b> tab'i (G-10, §2.1.6).
    ///
    /// <para>
    /// Sinf va guruh a'zoliklari — TARIXI bilan: qachon qo'shilgan, qachon va
    /// nega chiqqan, nechа kun turgan. Faol qatorlar birinchi.
    /// </para>
    /// <para>
    /// <c>className</c> alohida qaytadi va u <c>students.class_name</c> ning
    /// o'zi: migratsiya backfill'i faqat nomi sinf katalogiga MOS tushgan
    /// o'quvchilarni qamragan (§3.1), ya'ni eski o'quvchida a'zolik qatori
    /// umuman bo'lmasligi mumkin. Ekran bunday holatni yashirmasligi kerak.
    /// </para>
    /// </summary>
    [HttpGet("{id}/memberships")]
    public async Task<ActionResult<StudentMembershipsDto>> GetMemberships(
        string id, CancellationToken ct = default)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return NotFound();

        var today = AppClock.Today;

        var classRows = await db.ClassMemberships.AsNoTracking()
            .Where(m => m.StudentId == id)
            .Join(db.Classes.AsNoTracking(), m => m.ClassId, c => c.Id, (m, c) => new
            {
                m.Id, m.ClassId, c.Name, c.Grade, m.JoinedOn, m.LeftOn, m.LeaveReason,
            })
            .ToListAsync(ct);

        var groupRows = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.StudentId == id)
            .Join(db.StudyGroups.AsNoTracking(), m => m.GroupId, g => g.Id, (m, g) => new
            {
                m.Id, m.GroupId, GroupName = g.Name, g.SubjectId, g.IsArchived,
                m.JoinedOn, m.LeftOn, m.LeaveReason,
            })
            .ToListAsync(ct);

        var subjectIds = groupRows.Select(r => r.SubjectId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking()
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return new StudentMembershipsDto(
            student.Id,
            student.ClassName,
            [.. classRows
                .OrderBy(r => r.LeftOn != null).ThenByDescending(r => r.JoinedOn)
                .Select(r => new StudentClassMembershipDto(
                    r.Id, r.ClassId, r.Name, r.Grade, r.JoinedOn, r.LeftOn, r.LeaveReason,
                    DaysBetween(r.JoinedOn, r.LeftOn, today)))],
            [.. groupRows
                .OrderBy(r => r.LeftOn != null).ThenByDescending(r => r.JoinedOn)
                .Select(r => new StudentGroupMembershipDto(
                    r.Id, r.GroupId, r.GroupName, r.SubjectId,
                    subjects.GetValueOrDefault(r.SubjectId, ""), r.IsArchived,
                    r.JoinedOn, r.LeftOn, r.LeaveReason,
                    DaysBetween(r.JoinedOn, r.LeftOn, today)))]);
    }

    /// <summary>
    /// A'zolikda o'tgan kunlar. Yopilmagan a'zolik BUGUNGACHA sanaladi; birinchi
    /// kun ham hisobga kiradi (shu kuni qo'shilib shu kuni chiqqan bola — 1 kun).
    /// </summary>
    private static int DaysBetween(DateOnly joined, DateOnly? left, DateOnly today)
    {
        var end = left ?? today;
        return end < joined ? 0 : end.DayNumber - joined.DayNumber + 1;
    }

    /// <summary>
    /// O'quvchini butunlay o'chirish.
    ///
    /// <para>
    /// <b>Moliyaviy tarixi bor o'quvchi O'CHIRILMAYDI</b> (P1-21, SPEC §4.1):
    /// <c>invoices</c> va <c>payments</c> unga RESTRICT bilan bog'langan, ya'ni
    /// o'chirish urinishida baza FK xatosi bilan yiqilardi. Tushunarli javob
    /// beramiz va arxivlashni taklif qilamiz — pul yozuvi hech qachon
    /// o'chmaydi, o'quvchi esa arxivda tarixi bilan qoladi.
    /// </para>
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        if (await db.Invoices.AnyAsync(i => i.StudentId == id)
            || await db.Payments.AnyAsync(pay => pay.StudentId == id))
            return Conflict(new
            {
                message = "Moliyaviy yozuvi (hisob-faktura yoki to'lov) bor o'quvchini o'chirib "
                          + "bo'lmaydi — uni arxivga ko'chiring.",
            });

        // Biriktirilgan tizim akkauntini ham o'chiramiz.
        if (student.UserId is not null)
        {
            var user = await db.Users.FindAsync(student.UserId);
            if (user is not null) db.Users.Remove(user);
        }
        db.Students.Remove(student);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* ---------- Arxiv ---------- */

    /// <summary>
    /// O'quvchini arxivga ko'chirish: <c>IsArchived=true</c>, sana saqlanadi, sabab yoziladi
    /// (erkin matn + katalog qatori, §2.2), akkaunt login bloklanadi. Tarixiy ma'lumot saqlanadi.
    ///
    /// <para>
    /// Qarzi bor o'quvchi RAD ETILADI (§5.5 <c>archive_only_non_debtor_students</c>, §9 Q4).
    /// Qoida va uni chetlab o'tish <see cref="StudentArchiveService"/> da — ommaviy arxivlash
    /// bilan BIR joyda, aks holda ikkitasining biridan teshik ochilardi.
    /// </para>
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(
        string id, ArchiveStudentRequest req, CancellationToken ct = default)
    {
        var student = await db.Students.FindAsync([id], ct);
        if (student is null) return NotFound();
        if (student.IsArchived)
            return BadRequest(new { message = "O'quvchi allaqachon arxivda" });

        var archive = new StudentArchiveService(db);
        var isSuperAdmin = User.IsInRole(Roles.SuperAdmin);

        // Erkin matn MAJBURIY: katalog qatori "nega" ni guruhlaydi, matn tafsilotni yozadi (§2.2).
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length == 0)
            return BadRequest(new { message = StudentArchiveService.ReasonRequiredMessage });
        if (!await archive.ReasonIsUsableAsync(req.ArchiveReasonId, ct))
            return BadRequest(new { message = StudentArchiveService.ReasonNotFoundMessage });

        if (await archive.DebtorGuardAppliesAsync(isSuperAdmin, req.Force, ct))
        {
            var blocked = await archive.DebtorsAmongAsync([student], ct);
            if (blocked.Count > 0)
                return BadRequest(new BulkArchiveResultDto(
                    0, blocked, isSuperAdmin,
                    isSuperAdmin
                        ? StudentArchiveService.DebtorOverrideHintMessage
                        : StudentArchiveService.DebtorBlockedMessage));
        }

        // Login bloklash — PasswordHash bo'shaltiriladi (akkaunt qatori qoladi).
        (await archive.ApplyAsync(student, reason, req.ArchiveReasonId, ct))?.BlockLogin();

        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"O'quvchi arxivga ko'chirildi ({student.FullName}): \"{reason}\""
                + (isSuperAdmin && req.Force ? " — qarzdorlik to'sig'i chetlab o'tildi" : ""),
            studentId: student.Id);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Arxivdan qaytarish: <c>IsArchived=false</c>, arxiv maydonlari tozalanadi. Ixtiyoriy
    /// <c>NewPassword</c> berilsa akkauntga yangi parol o'rnatiladi (aks holda parol bloklangicha qoladi).
    /// </summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id, RestoreStudentRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        if (!student.IsArchived)
            return BadRequest(new { message = "O'quvchi arxivda emas" });

        student.IsArchived = false;
        student.ArchivedAt = null;
        student.ArchiveReason = null;
        // Katalog havolasi ham bo'shaydi — o'quvchi qaytdi, ya'ni "nega ketgani" endi yo'q.
        // Bo'shatilmasa katalogdagi qator "ishlatilgan" bo'lib qolar va o'chirilmasdi (§2.2).
        student.ArchiveReasonId = null;
        student.ArchivedWithClass = false;

        var newPwd = (req?.NewPassword ?? "").Trim();
        if (!string.IsNullOrEmpty(newPwd))
        {
            if (newPwd.Length < MinPasswordLength) return BadRequest(new { message = WeakPasswordMessage });
            if (student.UserId is not null)
            {
                var user = await db.Users.FindAsync(student.UserId);
                if (user is null)
                {
                    user = AccountFactory.CreateAccountFor(db, "student", student.FullName);
                    student.UserId = user.Id;
                }
                user.SetInitialPassword(newPwd);
            }
        }

        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"O'quvchi arxivdan qaytarildi ({student.FullName})",
            studentId: student.Id);

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>O'quvchining tizim akkaunti (login/parol). Akkaunt yo'q bo'lsa — yaratib biriktiradi.</summary>
    [HttpGet("{id}/credentials")]
    public async Task<ActionResult<CredentialsDto>> Credentials(string id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        var user = student.UserId is null ? null : await db.Users.FindAsync(student.UserId);
        if (user is null)
        {
            user = AccountFactory.CreateAccountFor(db, "student", student.FullName);
            student.UserId = user.Id;
            await db.SaveChangesAsync();
        }

        // Foydalanuvchi hali kirmagan bo'lsa dastlabki parol ko'rsatiladi; kirgach bo'sh (faqat reset-password).
        return new CredentialsDto(user.Email, user.InitialPassword ?? "", user.Role);
    }

    /// <summary>O'quvchiga yangi tasodifiy parol generatsiya qiladi va BIR MARTA qaytaradi
    /// (DB'da faqat hash saqlanadi).</summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<CredentialsDto>> ResetPassword(string id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        var user = student.UserId is null ? null : await db.Users.FindAsync(student.UserId);
        if (user is null)
        {
            user = AccountFactory.CreateAccountFor(db, "student", student.FullName);
            student.UserId = user.Id;
        }
        var pwd = AccountFactory.GeneratePassword();
        user.PasswordHash = PasswordHasher.Hash(pwd);
        await db.SaveChangesAsync();
        return new CredentialsDto(user.Email, pwd, user.Role);
    }

    /// <summary>
    /// Barcha (faol) o'quvchilarni login/parol bilan Excel (.xlsx) ga eksport qiladi.
    /// Parol FAQAT foydalanuvchi hali kirmagan bo'lsa ko'rinadi (kirgach bo'sh). Faqat superadmin.
    /// Ustunlar: F.I.SH., Sinf, Ota-ona, Telefon, Login, Parol.
    /// </summary>
    [HttpGet("export")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> Export()
    {
        var students = await db.Students.Where(s => !s.IsArchived)
            .OrderBy(s => s.ClassName).ThenBy(s => s.FullName).ToListAsync();
        var userIds = students.Where(s => s.UserId != null).Select(s => s.UserId!).ToList();
        var byId = (await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id);

        var headers = new[] { "F.I.SH.", "Sinf", "Ota-ona", "Telefon", "Login", "Parol" };
        var rows = students.Select(s =>
        {
            byId.TryGetValue(s.UserId ?? "", out var u);
            return (IReadOnlyList<string>)new[]
            {
                s.FullName, s.ClassName, s.ParentFullName, s.ParentPhone,
                u?.Email ?? "", u?.InitialPassword ?? "",
            };
        });

        var bytes = ExcelExport.Build("O'quvchilar", headers, rows);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"oquvchilar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /* ---------- Excel'dan ommaviy import ---------- */

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Import shabloni ustunlari (1-varaq). Tartibi import o'qishi bilan AYNAN bir xil bo'lishi shart.
    private static readonly string[] ImportHeaders =
    {
        "F.I.SH (o'quvchi)*", "Sinf*", "Tug'ilgan sana (YYYY-MM-DD)", "Jinsi (o'g'il/qiz)",
        "Manzil", "Ota-ona F.I.SH", "Ota-ona telefoni", "Qabul sanasi (YYYY-MM-DD)",
        // P1-21: "Chegirma %" va "Chegirma summa" ustunlari olib tashlandi —
        // chegirma endi direktor tasdig'i bilan beriladi (SPEC §8.1 Q5),
        // ya'ni uni Excel orqali jimgina kiritib bo'lmaydi.
    };

    /// <summary>
    /// O'quvchilarni ommaviy kiritish uchun bo'sh Excel shabloni (.xlsx). 1-varaq "O'quvchilar" —
    /// to'ldiriladigan sarlavhalar; 2-varaq "Yo'riqnoma" — maydonlar izohi va MAVJUD sinflar ro'yxati.
    /// Import faqat 1-varaqni o'qiydi, shu sababli yo'riqnoma import'ga ta'sir qilmaydi.
    /// </summary>
    [HttpGet("import-template")]
    public async Task<IActionResult> ImportTemplate()
    {
        var classes = await db.Classes.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();

        var info = new List<IReadOnlyList<string>>
        {
            new[] { "F.I.SH (o'quvchi)*", "Majburiy. Masalan: Aliyev Vali Aliyevich" },
            new[] { "Sinf*", "Majburiy — pastdagi ro'yxatdagi aniq nom" },
            new[] { "Tug'ilgan sana", "YYYY-MM-DD, masalan 2015-03-21" },
            new[] { "Jinsi", "o'g'il yoki qiz (bo'sh bo'lsa — o'g'il)" },
            new[] { "Manzil", "ixtiyoriy" },
            new[] { "Ota-ona F.I.SH", "ixtiyoriy" },
            new[] { "Ota-ona telefoni", "masalan +998901234567" },
            new[] { "Qabul sanasi", "YYYY-MM-DD (bo'sh bo'lsa — bugun)" },
            new[] { "", "" },
            new[] { "Chegirma", "Bu yerda EMAS: Moliya → Chegirmalar (direktor tasdiqlaydi)" },
            new[] { "Oylik to'lov", "Bu yerda EMAS: Moliya → Obunalar (toifa va summa)" },
            new[] { "", "" },
            new[] { "Mavjud sinflar:", classes.Count == 0 ? "(sinf yaratilmagan)" : "" },
        };
        info.AddRange(classes.Select(c => (IReadOnlyList<string>)new[] { c, "" }));

        var bytes = ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec("O'quvchilar", ImportHeaders, Array.Empty<IReadOnlyList<string>>()),
            new ExcelExport.SheetSpec("Yo'riqnoma", new[] { "Maydon", "Izoh" }, info),
        });
        return File(bytes, XlsxMime, "oquvchilar_shablon.xlsx");
    }

    /// <summary>
    /// To'ldirilgan Excel (.xlsx) shablonidan o'quvchilarni ommaviy yaratadi. Har qator alohida
    /// tekshiriladi: F.I.SH va Sinf majburiy, Sinf mavjud bo'lishi shart. To'g'ri qatorlar yaratiladi
    /// (tizim akkaunti bilan), xato qatorlar raqami/sababi bilan qaytariladi (qisman import).
    /// Import PUL YOZMAYDI (P1-21) — obuna keyin "Moliya → Obunalar" da ochiladi.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<StudentImportResultDto>> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmagan" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat .xlsx (Excel) fayl qabul qilinadi" });

        List<string[]> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            rows = ExcelImport.ReadRows(stream, ImportHeaders.Length);
        }
        catch
        {
            return BadRequest(new { message = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring" });
        }

        // Sinflar oldindan yuklab olinadi (har qatorda DB so'rovi bo'lmasligi uchun).
        var classByName = (await db.Classes.ToListAsync())
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var errors = new List<StudentImportRowErrorDto>();
        var imported = new List<Student>();
        int created = 0, skipped = 0;

        // 0-qator — sarlavha; ma'lumot 1-indeksdan boshlanadi (Excel'dagi 2-qator).
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var excelRow = i + 1; // Excel'da 1-asosli qator raqami

            if (r.All(string.IsNullOrWhiteSpace)) { skipped++; continue; }

            var fullName = r[0].Trim();
            var className = r[1].Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            { errors.Add(new StudentImportRowErrorDto(excelRow, "F.I.SH bo'sh")); continue; }
            if (string.IsNullOrWhiteSpace(className))
            { errors.Add(new StudentImportRowErrorDto(excelRow, "Sinf bo'sh")); continue; }
            if (!classByName.TryGetValue(className, out var cls))
            { errors.Add(new StudentImportRowErrorDto(excelRow, $"Sinf topilmadi: \"{className}\"")); continue; }

            var payload = new StudentPayload(
                FullName: fullName,
                BirthDate: NormalizeDate(r[2]),
                Address: r[4].Trim(),
                Gender: NormalizeGender(r[3]),
                ParentFullName: r[5].Trim(),
                ParentPhone: r[6].Trim(),
                ClassName: cls.Name,
                EnrollmentDate: NormalizeDate(r[7]) is { Length: > 0 } e ? e : null);

            imported.Add(AddStudent(payload));
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync();
            // Butun partiya uchun BITTA marta (SPEC §3.2). `EnsureManyAsync` ikkita
            // so'rov yuboradi — importdagi har qator uchun alohida emas.
            await GuardianSync.EnsureManyAsync(db, imported);
        }
        return new StudentImportResultDto(created, errors.Count, skipped, errors);
    }

    private static string NormalizeGender(string raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        // qiz/female/ayol → female; qolgan hammasi (bo'sh, o'g'il, erkak, male, ...) → male
        return v is "qiz" or "female" or "ayol" or "f" or "q" or "2" ? "female" : "male";
    }

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy",
    };

    /// <summary>Sanani "YYYY-MM-DD" ga keltiradi. Excel matn sanasini ham, raqamli (OADate) sanasini ham qabul qiladi.</summary>
    private static string NormalizeDate(string raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return "";
        if (DateTime.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd");
        if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var oa) && oa is > 1 and < 600000)
        {
            try { return DateTime.FromOADate(oa).ToString("yyyy-MM-dd"); } catch { /* e'tiborsiz */ }
        }
        return v; // ixtiyoriy maydon — noma'lum format bo'lsa, kiritilganicha qoladi
    }

    private static int? ParseIntOrNull(string raw)
    {
        var v = (raw ?? "").Replace("%", "").Trim();
        return int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static decimal? ParseDecimalOrNull(string raw)
    {
        var v = (raw ?? "").Replace(" ", "").Replace(",", "").Trim();
        return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>
    /// <b>410 Gone.</b> Pul bu yerdan qabul qilinmaydi (P1-21) — kassaga o'ting:
    /// <c>POST /api/cash/payments</c>.
    ///
    /// <para>
    /// Nega endpoint butunlay o'chirilmadi, 410 qaytaradi: eski mobil ilova va
    /// yorliqlar hali shu manzilga uradi. 404 "manzil noto'g'ri" degan ma'no
    /// berardi va foydalanuvchi qayta urinishga harakat qilardi; 410 esa
    /// "bu yo'l YOPILDI" deydi va javob tanasida yangi manzilni ko'rsatadi.
    /// </para>
    /// <para>
    /// Eski yo'l bilan to'lovda na kassir, na smena, na chek raqami bor edi va
    /// yozuvni keyin tahrirlash mumkin edi — SPEC §4.1/§4.2 ni buzadigan
    /// aynan shu. Yangi yo'lda to'lov ochiq smenaga, JWT'dagi kassirga va
    /// uzluksiz chek raqamiga bog'lanadi hamda o'zgarmas bo'ladi.
    /// </para>
    /// </summary>
    [HttpPost("{id}/payments")]
    public IActionResult AddPayment(string id) =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            code = "endpoint_retired",
            message = "To'lov endi kassa orqali qabul qilinadi: POST /api/cash/payments "
                      + "(ochiq smena, kassir va chek raqami bilan).",
            replacement = "/api/cash/payments",
        });

    /// <summary>
    /// O'quvchi to'lov tarixi: oylar bo'yicha hisoblangan/to'langan holat.
    ///
    /// <para>
    /// <b>Ruxsat (F0.02):</b> bu moliya hisoboti — admin va direktor (SPEC §4.3).
    /// Ilgari <c>[AdminPerm("students")]</c> ostida edi, ya'ni HAR QANDAY xodim
    /// (GET hammaga ochiq, <c>AdminPermAttribute:40-42</c>) istalgan o'quvchining
    /// to'lov daftarini o'qiy olardi. Ro'yxatdagi <c>Balans</c> ustuni joyida
    /// qoladi (Students moduli qarori) — yopilgani DAFTAR: kim qachon qancha
    /// to'lagani.
    /// </para>
    /// </summary>
    [HttpGet("{id}/ledger")]
    [Authorize(Roles = Roles.FinanceStaff)]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<StudentLedgerDto>> Ledger(string id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        return await StudentLedger.BuildAsync(db, student);
    }
}
