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
[AdminPerm("teachers")]
[Route("api/admin/teachers")]
public class TeachersController(AppDbContext db, AuditService audit, IExpenseService expenses)
    : ControllerBase
{
    private const int MinPasswordLength = 8;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";

    /// <summary>
    /// Faol (arxivlanmagan) o'qituvchilar. <paramref name="includeArchived"/>=true bo'lsa hammasi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Teacher>>> GetAll([FromQuery] bool includeArchived = false)
    {
        var q = db.Teachers.AsQueryable();
        if (!includeArchived) q = q.Where(t => !t.IsArchived);
        return await q.OrderBy(t => t.FullName).ToListAsync();
    }

    /// <summary>Faqat arxivlangan o'qituvchilar.</summary>
    [HttpGet("archived")]
    public async Task<ActionResult<IEnumerable<Teacher>>> GetArchived() =>
        await db.Teachers.Where(t => t.IsArchived)
            .OrderByDescending(t => t.ArchivedAt).ThenBy(t => t.FullName).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<Teacher>> Create(TeacherPayload p)
    {
        var teacher = new Teacher
        {
            FullName = p.FullName,
            BirthDate = p.BirthDate,
            Address = p.Address,
            Gender = p.Gender,
            Phone = p.Phone ?? "",
            PhotoUrl = p.PhotoUrl,
            HomeroomClass = p.HomeroomClass,
            SubjectIds = p.SubjectIds ?? new(),
            // Oylik endi avtomatik (jadval + toifa narxi) — qo'lda summa kiritilmaydi; toifa tanlanadi.
            Category = p.Category ?? "",
            SalaryStartDate = p.SalaryStartDate ?? "",
            SalaryStartMonth = !string.IsNullOrEmpty(p.SalaryStartDate) && p.SalaryStartDate.Length >= 7
                ? p.SalaryStartDate[..7]
                : p.SalaryStartMonth ?? "",
            // Yangi o'qituvchiga standart — barcha bo'limlar ochiq (admin keyin cheklashi mumkin).
            Permissions = p.Permissions ?? TeacherPermissions.All.ToList(),
        };
        db.Teachers.Add(teacher);

        // O'qituvchiga "teacher" rolli tizim akkaunti generatsiya qilib biriktiramiz.
        var account = AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
        teacher.UserId = account.Id;

        if (!string.IsNullOrEmpty(teacher.Category))
            audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "create",
                $"Toifa belgilandi: {teacher.Category}", teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return teacher;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Teacher>> Update(string id, TeacherPayload p)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var oldCategory = teacher.Category;
        var oldStart = teacher.SalaryStartMonth;
        teacher.FullName = p.FullName;
        teacher.BirthDate = p.BirthDate;
        teacher.Address = p.Address;
        teacher.Gender = p.Gender;
        teacher.Phone = p.Phone ?? "";
        teacher.PhotoUrl = p.PhotoUrl;
        teacher.HomeroomClass = p.HomeroomClass;
        teacher.SubjectIds = p.SubjectIds ?? new();
        // Toifa — berilsa yangilaymiz (oylik avtomatik shu toifa narxidan hisoblanadi).
        if (p.Category is not null) teacher.Category = p.Category;
        teacher.SalaryStartDate = p.SalaryStartDate ?? "";
        teacher.SalaryStartMonth = !string.IsNullOrEmpty(p.SalaryStartDate) && p.SalaryStartDate.Length >= 7
            ? p.SalaryStartDate[..7]
            : p.SalaryStartMonth ?? "";
        // Ruxsatlar (bo'limlar) — berilsa yangilaymiz; berilmasa joriyini saqlaymiz.
        if (p.Permissions is not null) teacher.Permissions = p.Permissions;

        // Akkaunt nomini sinxronlaymiz va (ixtiyoriy) yangi parol o'rnatamiz.
        var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            var pwd = p.NewPassword.Trim();
            if (pwd.Length < MinPasswordLength) return BadRequest(new { message = WeakPasswordMessage });
            // Akkaunt yo'q bo'lsa — yaratib biriktiramiz.
            user ??= AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
            user.SetInitialPassword(pwd);
        }
        if (user is not null) user.FullName = teacher.FullName;

        if (oldCategory != teacher.Category || oldStart != teacher.SalaryStartMonth)
        {
            var parts = new List<string>();
            if (oldCategory != teacher.Category)
                parts.Add($"toifa {(string.IsNullOrEmpty(oldCategory) ? "—" : oldCategory)} → " +
                          $"{(string.IsNullOrEmpty(teacher.Category) ? "—" : teacher.Category)}");
            if (oldStart != teacher.SalaryStartMonth)
                parts.Add($"boshlanish oyi {(string.IsNullOrEmpty(oldStart) ? "—" : oldStart)} → " +
                          $"{(string.IsNullOrEmpty(teacher.SalaryStartMonth) ? "—" : teacher.SalaryStartMonth)}");
            audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
                "Oylik sozlamasi: " + string.Join(", ", parts),
                before: new { Category = oldCategory, SalaryStartMonth = oldStart },
                after: new { teacher.Category, teacher.SalaryStartMonth }, teacherId: teacher.Id);
        }

        await db.SaveChangesAsync();
        return teacher;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        // Biriktirilgan tizim akkauntini ham o'chiramiz.
        if (teacher.UserId is not null)
        {
            var user = await db.Users.FindAsync(teacher.UserId);
            if (user is not null) db.Users.Remove(user);
        }
        db.Teachers.Remove(teacher);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* ---------- Arxiv ---------- */

    /// <summary>
    /// O'qituvchini arxivga ko'chirish: <c>IsArchived=true</c>, sana/sabab saqlanadi, akkaunt
    /// login bloklanadi (PasswordHash bo'shaltiriladi). Tarixiy ma'lumotlar (jurnal, hisobot) saqlanadi.
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id, ArchiveTeacherRequest req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        if (teacher.IsArchived) return BadRequest(new { message = "O'qituvchi allaqachon arxivda" });

        teacher.IsArchived = true;
        teacher.ArchivedAt = AppClock.Today.ToString("yyyy-MM-dd");
        teacher.ArchiveReason = (req.Reason ?? "").Trim();

        // Login bloklash — PasswordHash bo'shaltiriladi (login imkonsiz bo'ladi).
        if (teacher.UserId is not null)
        {
            var user = await db.Users.FindAsync(teacher.UserId);
            if (user is not null) user.BlockLogin();
        }

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi arxivga ko'chirildi ({teacher.FullName})"
                + (string.IsNullOrWhiteSpace(teacher.ArchiveReason) ? "" : $": \"{teacher.ArchiveReason}\""),
            teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Arxivdan qaytarish: <c>IsArchived=false</c>, arxiv maydonlari tozalanadi. Ixtiyoriy
    /// <c>NewPassword</c> berilsa akkauntga yangi parol o'rnatiladi (aks holda parol bloklangicha qoladi).
    /// </summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id, RestoreTeacherRequest req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        if (!teacher.IsArchived) return BadRequest(new { message = "O'qituvchi arxivda emas" });

        teacher.IsArchived = false;
        teacher.ArchivedAt = null;
        teacher.ArchiveReason = null;

        var newPwd = (req?.NewPassword ?? "").Trim();
        if (!string.IsNullOrEmpty(newPwd))
        {
            if (newPwd.Length < MinPasswordLength) return BadRequest(new { message = WeakPasswordMessage });
            var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
            user ??= AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
            user.SetInitialPassword(newPwd);
        }

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi arxivdan qaytarildi ({teacher.FullName})", teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>O'qituvchining tizim akkaunti (login/parol). Akkaunt yo'q bo'lsa — yaratib biriktiradi.</summary>
    [HttpGet("{id}/credentials")]
    public async Task<ActionResult<CredentialsDto>> Credentials(string id)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
        if (user is null)
        {
            user = AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
            await db.SaveChangesAsync();
        }

        // O'qituvchi hali kirmagan bo'lsa dastlabki parol ko'rinadi; kirgach bo'sh (faqat reset-password).
        return new CredentialsDto(user.Email, user.InitialPassword ?? "", user.Role);
    }

    /// <summary>O'qituvchiga yangi tasodifiy parol generatsiya qiladi va BIR MARTA qaytaradi
    /// (DB'da faqat hash saqlanadi).</summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<CredentialsDto>> ResetPassword(string id)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
        if (user is null)
        {
            user = AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
        }
        var pwd = AccountFactory.GeneratePassword();
        user.PasswordHash = PasswordHasher.Hash(pwd);
        await db.SaveChangesAsync();
        return new CredentialsDto(user.Email, pwd, user.Role);
    }

    /// <summary>
    /// Barcha (faol) o'qituvchilarni login/parol bilan Excel (.xlsx) ga eksport qiladi.
    /// Parol FAQAT o'qituvchi hali kirmagan bo'lsa ko'rinadi (kirgach bo'sh). Faqat superadmin.
    /// Ustunlar: F.I.SH., Telefon, Sinf rahbarligi, Login, Parol.
    /// </summary>
    [HttpGet("export")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> Export()
    {
        var teachers = await db.Teachers.Where(t => !t.IsArchived)
            .OrderBy(t => t.FullName).ToListAsync();
        var userIds = teachers.Where(t => t.UserId != null).Select(t => t.UserId!).ToList();
        var byId = (await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id);

        var headers = new[] { "F.I.SH.", "Telefon", "Sinf rahbarligi", "Login", "Parol" };
        var rows = teachers.Select(t =>
        {
            byId.TryGetValue(t.UserId ?? "", out var u);
            return (IReadOnlyList<string>)new[]
            {
                t.FullName, t.Phone, t.HomeroomClass, u?.Email ?? "", u?.InitialPassword ?? "",
            };
        });

        var bytes = ExcelExport.Build("O'qituvchilar", headers, rows);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"oqituvchilar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// O'qituvchiga maosh berish — <c>expenses</c> qatori va
    /// <c>debit expense:salary / credit cash|bank</c> jurnal juftligi (P1-21).
    ///
    /// <para>
    /// Ilgari bu yassi <c>finance_transactions</c> ga yozardi va JURNALGA
    /// UMUMAN TUSHMASDI — ya'ni berilgan maosh P&amp;L da ko'rinmasdi va sof
    /// foyda butun aylanmaga teng bo'lib chiqardi. Endi u boshqa har qanday
    /// chiqim bilan bir xil yo'ldan o'tadi: <see cref="IExpenseService"/>.
    /// </para>
    /// <para>
    /// Ikki oqibat bor va ikkalasi ham ataylab:
    /// (1) chegaradan (<c>billing_settings.expense_approval_threshold</c>) katta
    ///     maosh DIREKTOR TASDIG'INI kutadi va shu paytgacha "berilgan" deb
    ///     hisoblanmaydi (SPEC §4.5);
    /// (2) xato yozuv endi o'chirilmaydi, storno qilinadi
    ///     (<c>POST /api/admin/expenses/{{id}}/reverse</c>).
    /// </para>
    /// </summary>
    [HttpPost("{id}/salary-payments")]
    [BillingFault]
    public async Task<ActionResult<ExpenseDto>> PaySalary(
        string id, SalaryPaymentRequest req, CancellationToken ct)
    {
        var teacher = await db.Teachers.FindAsync([id], ct);
        if (teacher is null) return NotFound();

        if (req.Amount <= 0)
            return BadRequest(new { message = "Maosh summasi musbat bo'lishi kerak" });

        var note = string.IsNullOrWhiteSpace(req.Note)
            ? $"Oylik maosh — {teacher.FullName}"
            : req.Note.Trim();

        // Kim berayotgani JWT'dan olinadi (SPEC §4.4), so'rov tanasidan emas.
        var expense = await expenses.CreateAsync(
            new CreateExpenseRequest(
                OnDate: AppClock.Today,
                Category: SalaryPaymentQuery.SalaryCategory,
                Amount: req.Amount,
                Method: string.IsNullOrWhiteSpace(req.Method) ? PaymentMethod.Transfer : req.Method,
                Note: note,
                TeacherId: teacher.Id),
            FinanceActor.RequireUserId(User), ct);

        audit.Record(AuditService.EntityTeacherSalary, expense.Id.ToString(), "create",
            $"Maosh berildi: {AuditService.Money(req.Amount)} so'm"
                + (expense.Status == ExpenseStatus.Pending ? " — direktor tasdig'ini kutmoqda" : ""),
            after: new { expense.OnDate, expense.Category, expense.Amount, expense.Note, expense.Status },
            teacherId: teacher.Id);
        await db.SaveChangesAsync(ct);

        return Ok(expense);
    }

    /// <summary>O'qituvchiga berilgan maoshlar tarixi (jurnalga tushgan, storno qilinmagan).</summary>
    [HttpGet("{id}/salary-history")]
    public async Task<ActionResult<SalaryHistoryDto>> SalaryHistory(string id, CancellationToken ct)
    {
        var teacher = await db.Teachers.FindAsync([id], ct);
        if (teacher is null) return NotFound();

        var payments = (await new SalaryPaymentQuery(db).ForTeacherAsync(id, ct: ct))
            .Select(t => new PaymentDto(t.OnDate.ToString("yyyy-MM-dd"), t.Amount, t.Note, t.Month))
            .ToList();

        var monthly = await TeacherSalaryCalc.MonthlyAsync(db, teacher);
        return new SalaryHistoryDto(teacher.Id, teacher.FullName, monthly,
            payments.Sum(p => p.Amount), payments);
    }

    /// <summary>
    /// O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha): jami berilgan, qoldiq va
    /// har oyda qancha oylik berilgani. Oylar davr (from..to) bo'yicha, oy = to'lov sanasi oyi.
    /// </summary>
    [HttpGet("{id}/salary-ledger")]
    public async Task<ActionResult<SalaryLedgerDto>> SalaryLedger(
        string id, [FromQuery] string? from, [FromQuery] string? to)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        return await SchoolLms.Application.Services.SalaryLedger.BuildAsync(db, teacher, from, to);
    }
}
