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
[Route("api/admin/teachers")]
public class TeachersController(AppDbContext db, AuditService audit) : ControllerBase
{
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
            HomeroomClass = p.HomeroomClass,
            SubjectIds = p.SubjectIds ?? new(),
            Salary = p.Salary,
            SalaryStartMonth = p.SalaryStartMonth ?? "",
            // Yangi o'qituvchiga standart — barcha bo'limlar ochiq (admin keyin cheklashi mumkin).
            Permissions = p.Permissions ?? TeacherPermissions.All.ToList(),
        };
        db.Teachers.Add(teacher);

        // O'qituvchiga "teacher" rolli tizim akkaunti generatsiya qilib biriktiramiz.
        var account = AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
        teacher.UserId = account.Id;

        if (teacher.Salary > 0)
        {
            var startNote = string.IsNullOrEmpty(teacher.SalaryStartMonth)
                ? "" : $" ({teacher.SalaryStartMonth} oydan)";
            audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "create",
                $"Oylik belgilandi: {AuditService.Money(teacher.Salary)} so'm{startNote}",
                after: new { teacher.Salary, teacher.SalaryStartMonth }, teacherId: teacher.Id);
        }

        await db.SaveChangesAsync();
        return teacher;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Teacher>> Update(string id, TeacherPayload p)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var oldSalary = teacher.Salary;
        var oldStart = teacher.SalaryStartMonth;
        teacher.FullName = p.FullName;
        teacher.BirthDate = p.BirthDate;
        teacher.Address = p.Address;
        teacher.Gender = p.Gender;
        teacher.Phone = p.Phone ?? "";
        teacher.HomeroomClass = p.HomeroomClass;
        teacher.SubjectIds = p.SubjectIds ?? new();
        teacher.Salary = p.Salary;
        teacher.SalaryStartMonth = p.SalaryStartMonth ?? "";
        // Ruxsatlar (bo'limlar) — berilsa yangilaymiz; berilmasa joriyini saqlaymiz.
        if (p.Permissions is not null) teacher.Permissions = p.Permissions;

        // Akkaunt nomini sinxronlaymiz va (ixtiyoriy) yangi parol o'rnatamiz.
        var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            var pwd = p.NewPassword.Trim();
            if (pwd.Length < 4) return BadRequest(new { message = "Parol kamida 4 belgidan iborat bo'lsin" });
            // Akkaunt yo'q bo'lsa — yaratib biriktiramiz.
            user ??= AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
            user.PasswordHash = PasswordHasher.Hash(pwd);
            user.PlainPassword = pwd; // admin profilda ko'rsatish uchun saqlanadi
        }
        if (user is not null) user.FullName = teacher.FullName;

        if (oldSalary != teacher.Salary || oldStart != teacher.SalaryStartMonth)
        {
            var parts = new List<string>();
            if (oldSalary != teacher.Salary)
                parts.Add($"oylik {AuditService.Money(oldSalary)} → {AuditService.Money(teacher.Salary)} so'm");
            if (oldStart != teacher.SalaryStartMonth)
                parts.Add($"boshlanish oyi {(string.IsNullOrEmpty(oldStart) ? "—" : oldStart)} → " +
                          $"{(string.IsNullOrEmpty(teacher.SalaryStartMonth) ? "—" : teacher.SalaryStartMonth)}");
            audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
                "Oylik o'zgartirildi: " + string.Join(", ", parts),
                before: new { Salary = oldSalary, SalaryStartMonth = oldStart },
                after: new { teacher.Salary, teacher.SalaryStartMonth }, teacherId: teacher.Id);
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
        teacher.ArchivedAt = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        teacher.ArchiveReason = (req.Reason ?? "").Trim();

        // Login bloklash — PasswordHash bo'shaltiriladi, PlainPassword tozalanadi.
        if (teacher.UserId is not null)
        {
            var user = await db.Users.FindAsync(teacher.UserId);
            if (user is not null) { user.PasswordHash = ""; user.PlainPassword = null; }
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
            if (newPwd.Length < 4) return BadRequest(new { message = "Parol kamida 4 belgidan iborat bo'lsin" });
            var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
            user ??= AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
            user.PasswordHash = PasswordHasher.Hash(newPwd);
            user.PlainPassword = newPwd;
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

        return new CredentialsDto(user.Email, user.PlainPassword ?? "", user.Role);
    }

    /// <summary>O'qituvchiga maosh berish — moliyaga chiqim (salary) sifatida yoziladi.</summary>
    [HttpPost("{id}/salary-payments")]
    public async Task<IActionResult> PaySalary(string id, SalaryPaymentRequest req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var tx = new FinanceTransaction
        {
            Date = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
            Direction = "expense",
            Category = "salary",
            Amount = req.Amount,
            TeacherId = teacher.Id,
            Note = string.IsNullOrWhiteSpace(req.Note) ? $"Oylik maosh — {teacher.FullName}" : req.Note,
        };
        db.FinanceTransactions.Add(tx);

        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "create",
            $"Maosh berildi: {AuditService.Money(req.Amount)} so'm",
            after: AuditService.Snapshot(tx), teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>O'qituvchiga berilgan maoshlar tarixi.</summary>
    [HttpGet("{id}/salary-history")]
    public async Task<ActionResult<SalaryHistoryDto>> SalaryHistory(string id)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var payments = await db.FinanceTransactions
            .Where(t => t.TeacherId == id && t.Direction == "expense" && t.Category == "salary")
            .OrderByDescending(t => t.Date)
            .Select(t => new PaymentDto(t.Date, t.Amount, t.Note, t.Month))
            .ToListAsync();

        return new SalaryHistoryDto(teacher.Id, teacher.FullName, teacher.Salary,
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
