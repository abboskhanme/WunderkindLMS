using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/students")]
public class StudentsController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const int MinPasswordLength = 8;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";

    /// <summary>
    /// Faol (arxivlanmagan) o'quvchilar ro'yxati. <paramref name="includeArchived"/>=true bo'lsa
    /// arxivlangan o'quvchilar ham qaytadi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Student>>> GetAll([FromQuery] bool includeArchived = false)
    {
        var q = db.Students.AsQueryable();
        if (!includeArchived) q = q.Where(s => !s.IsArchived);
        return await q.OrderBy(s => s.FullName).ToListAsync();
    }

    /// <summary>Faqat arxivlangan o'quvchilar ro'yxati.</summary>
    [HttpGet("archived")]
    public async Task<ActionResult<IEnumerable<Student>>> GetArchived() =>
        await db.Students.Where(s => s.IsArchived)
            .OrderByDescending(s => s.ArchivedAt).ThenBy(s => s.FullName).ToListAsync();

    /// <summary>"Familiya Ism Sharifi" — parts'ni birlashtirish (bo'sh qismlar tashlanadi).</summary>
    private static string JoinName(string? last, string? first, string? middle) =>
        string.Join(' ', new[] { last, first, middle }
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrEmpty(x)));

    [HttpPost]
    public async Task<ActionResult<Student>> Create(StudentPayload p)
    {
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == p.ClassName);
        var enrollment = string.IsNullOrWhiteSpace(p.EnrollmentDate)
            ? DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd")
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
            Balance = 0,
            DiscountPct = Math.Clamp(p.DiscountPct ?? 0, 0, 100),
            DiscountAmount = Math.Max(0m, p.DiscountAmount ?? 0m),
            DiscountNote = (p.DiscountNote ?? "").Trim(),
        };
        db.Students.Add(student);

        // O'quvchiga "student" rolli tizim akkaunti generatsiya qilib biriktiramiz.
        var account = AccountFactory.CreateAccountFor(db, "student", student.FullName);
        student.UserId = account.Id;

        // Kelgan oyidan joriy oygacha har oy uchun hisob (qarz) yoziladi.
        // Amount = TO'LIQ sinf narxi, Discount = chegirma; balans faqat (Amount - Discount) ga kamayadi.
        if (cls is not null && cls.MonthlyFee > 0)
        {
            var discount = TuitionService.DiscountFor(cls.MonthlyFee, student.DiscountPct, student.DiscountAmount);
            var effective = cls.MonthlyFee - discount;
            var month = enrollment[..7];
            var current = TuitionService.CurrentMonth();
            while (string.CompareOrdinal(month, current) <= 0)
            {
                db.MonthlyCharges.Add(new MonthlyCharge
                {
                    StudentId = student.Id,
                    Month = month,
                    Amount = cls.MonthlyFee,
                    Discount = discount,
                    Date = $"{month}-01",
                });
                student.Balance -= effective;
                month = TuitionService.NextMonth(month);
            }
        }

        // Chegirma berilgan bo'lsa — audit yozuvi.
        if (student.DiscountPct > 0 || student.DiscountAmount > 0)
            audit.Record(AuditService.EntityStudentDiscount, student.Id, "create",
                DiscountSummary("O'quvchi yaratildi", student.FullName, 0, 0, student.DiscountPct, student.DiscountAmount, student.DiscountNote),
                after: DiscountSnapshot(student), studentId: student.Id);

        await db.SaveChangesAsync();
        return student;
    }

    /// <summary>Chegirma o'zgarishi audit izohini tuzadi.</summary>
    private static string DiscountSummary(
        string action, string studentName,
        int oldPct, decimal oldAmount, int newPct, decimal newAmount, string note)
    {
        var changed = (oldPct != newPct ? $"{oldPct}% → {newPct}%" : $"{newPct}%")
                    + ", "
                    + (oldAmount != newAmount
                        ? $"{AuditService.Money(oldAmount)} → {AuditService.Money(newAmount)} so'm"
                        : $"{AuditService.Money(newAmount)} so'm");
        var n = string.IsNullOrWhiteSpace(note) ? "" : $" — \"{note}\"";
        return $"{action}: chegirma {changed}{n} ({studentName})";
    }

    /// <summary>Chegirma snapshot'i (audit Before/After uchun).</summary>
    private static object DiscountSnapshot(Student s) => new
    {
        s.DiscountPct,
        s.DiscountAmount,
        s.DiscountNote,
    };

    /// <summary>
    /// O'quvchini tahrirlash. Chegirma (foiz/summa) o'zgarsa va
    /// <paramref name="applyDiscount"/> = true bo'lsa, yangi chegirma joriy oy MonthlyCharge'iga
    /// ham qo'llanadi (oylik summa qayta hisoblanadi, balans deltaga moslab to'g'rilanadi).
    /// false bo'lsa — joriy oy eski summada qoladi, yangi chegirma keyingi accrual'dan amal qiladi.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, StudentPayload p, [FromQuery] bool applyDiscount = false)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        var oldPct = student.DiscountPct;
        var oldAmount = student.DiscountAmount;
        var oldNote = student.DiscountNote;

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

        // Chegirma — berilgan maydonlar yangilanadi (null = avvalgi saqlanadi).
        if (p.DiscountPct.HasValue) student.DiscountPct = Math.Clamp(p.DiscountPct.Value, 0, 100);
        if (p.DiscountAmount.HasValue) student.DiscountAmount = Math.Max(0m, p.DiscountAmount.Value);
        if (p.DiscountNote is not null) student.DiscountNote = p.DiscountNote.Trim();

        // Akkaunt nomini sinxronlaymiz va (ixtiyoriy) yangi parol o'rnatamiz.
        var user = student.UserId is null ? null : await db.Users.FindAsync(student.UserId);
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            var pwd = p.NewPassword.Trim();
            if (pwd.Length < MinPasswordLength) return BadRequest(new { message = WeakPasswordMessage });
            // Akkaunt yo'q bo'lsa — yaratib biriktiramiz.
            user ??= AccountFactory.CreateAccountFor(db, "student", student.FullName);
            student.UserId = user.Id;
            user.PasswordHash = PasswordHasher.Hash(pwd);
        }
        if (user is not null) user.FullName = student.FullName;

        // Chegirma o'zgardimi? Audit + (xohlasa) joriy oyga qo'llash.
        var discountChanged = oldPct != student.DiscountPct
                              || oldAmount != student.DiscountAmount
                              || oldNote != student.DiscountNote;
        if (discountChanged)
        {
            var applied = false;
            if (applyDiscount)
            {
                var month = TuitionService.CurrentMonth();
                var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == student.ClassName);
                var charge = await db.MonthlyCharges
                    .FirstOrDefaultAsync(c => c.StudentId == student.Id && c.Month == month);
                if (cls is not null && charge is not null)
                {
                    // Yangi va eski "effektiv" summalar farqi balansga qo'shiladi.
                    // Amount HAR DOIM joriy sinf narxi bo'lishi kerak (eski yozuvlar tuzatiladi),
                    // Discount esa yangi chegirma summasi.
                    var newDiscount = TuitionService.DiscountFor(cls.MonthlyFee, student.DiscountPct, student.DiscountAmount);
                    var newEffective = cls.MonthlyFee - newDiscount;
                    var oldEffective = charge.Amount - charge.Discount;
                    var delta = newEffective - oldEffective;
                    if (delta != 0 || charge.Amount != cls.MonthlyFee || charge.Discount != newDiscount)
                    {
                        charge.Amount = cls.MonthlyFee;
                        charge.Discount = newDiscount;
                        student.Balance -= delta;   // delta > 0 (kamroq chegirma → ko'proq to'lash) → balans kamayadi
                        applied = true;
                    }
                }
            }

            var summary = DiscountSummary("Chegirma o'zgartirildi", student.FullName,
                oldPct, oldAmount, student.DiscountPct, student.DiscountAmount, student.DiscountNote);
            summary += applied
                ? " — joriy oy hisobi yangi summaga to'g'rilandi"
                : (applyDiscount ? " — joriy oy hisobi topilmadi/o'zgarmadi" : " — keyingi oydan amal qiladi");
            audit.Record(AuditService.EntityStudentDiscount, student.Id, "update", summary,
                before: new { DiscountPct = oldPct, DiscountAmount = oldAmount, DiscountNote = oldNote },
                after: DiscountSnapshot(student), studentId: student.Id);
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        // Bog'liq oylik hisob yozuvlarini ham olib tashlaymiz (orfan qolmasligi uchun).
        db.MonthlyCharges.RemoveRange(db.MonthlyCharges.Where(c => c.StudentId == id));
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
    /// O'quvchini arxivga ko'chirish: <c>IsArchived=true</c>, sana saqlanadi, sabab yoziladi,
    /// akkaunt login bloklanadi (PasswordHash bo'shaltiriladi). Tarixiy ma'lumotlar saqlanadi.
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id, ArchiveStudentRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        if (student.IsArchived)
            return BadRequest(new { message = "O'quvchi allaqachon arxivda" });

        student.IsArchived = true;
        student.ArchivedAt = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        student.ArchiveReason = (req.Reason ?? "").Trim();

        // Login bloklash — PasswordHash bo'shaltiriladi (login imkonsiz bo'ladi).
        if (student.UserId is not null)
        {
            var user = await db.Users.FindAsync(student.UserId);
            if (user is not null)
                user.PasswordHash = "";
        }

        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"O'quvchi arxivga ko'chirildi ({student.FullName})"
                + (string.IsNullOrWhiteSpace(student.ArchiveReason) ? "" : $": \"{student.ArchiveReason}\""),
            studentId: student.Id);

        await db.SaveChangesAsync();
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
                user.PasswordHash = PasswordHasher.Hash(newPwd);
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

        // Parol xavfsizlik uchun saqlanmaydi — bo'sh qaytadi. Ko'rsatish kerak bo'lsa reset-password.
        return new CredentialsDto(user.Email, "", user.Role);
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

    /// <summary>O'quvchiga to'lov kiritish — balansga qo'shiladi va moliyaga kirim sifatida yoziladi.
    /// <paramref name="req"/> ichida Month ("YYYY-MM") berilsa, to'lov shu oy uchun hisoblanadi.</summary>
    [HttpPost("{id}/payments")]
    public async Task<IActionResult> AddPayment(string id, PaymentRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        student.Balance += req.Amount;

        var month = string.IsNullOrWhiteSpace(req.Month) ? null : req.Month.Trim();

        // To'lovni moliyaviy kirim (o'quvchi to'lovi) sifatida qayd etamiz.
        var tx = new FinanceTransaction
        {
            Date = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
            Direction = "income",
            Category = "tuition",
            Amount = req.Amount,
            StudentId = student.Id,
            Month = month,
            Note = month is null
                ? $"O'quvchi to'lovi — {student.FullName}"
                : $"O'quvchi to'lovi ({month}) — {student.FullName}",
        };
        db.FinanceTransactions.Add(tx);

        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "create",
            $"To'lov qabul qilindi: +{AuditService.Money(req.Amount)} so'm"
                + (month is null ? "" : $" ({month} uchun)"),
            after: AuditService.Snapshot(tx), studentId: student.Id);

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>O'quvchi to'lov tarixi: oylar bo'yicha hisoblangan/to'langan holat.</summary>
    [HttpGet("{id}/ledger")]
    public async Task<ActionResult<StudentLedgerDto>> Ledger(string id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        return await StudentLedger.BuildAsync(db, student);
    }
}
