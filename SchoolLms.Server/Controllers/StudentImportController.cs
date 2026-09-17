using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quvchilarni Excel'dan IKKI BOSQICHLI import — §2.3 (S-3):
/// <c>shablon</c> → <c>tekshirish</c> → <c>tasdiqlash</c>.
///
/// <para>
/// <b>Yarim import bo'lmaydi.</b> Tasdiqlashda fayl yangidan tekshiriladi va
/// bitta ham xato bo'lsa HECH NARSA yozilmaydi (barcha qatorlar bitta
/// <c>SaveChanges</c> da). Mavjud bir bosqichli
/// <c>POST /api/admin/students/import</c> tegilmagan va o'z joyida ishlaydi —
/// yangisi QO'SHIMCHA yo'l.
/// </para>
/// <para>
/// <b>Marshrut prefiksi.</b> <c>import/</c> ostida uchta segment
/// (<c>import/shablon</c>, <c>import/tekshirish</c>, <c>import/tasdiqlash</c>) —
/// mavjud bitta segmentli <c>import</c> bilan to'qnashmaydi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/students/import")]
public class StudentImportController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Yuklanadigan faylning eng katta hajmi (10 MB) — mavjud import bilan bir xil.</summary>
    private const int MaxUpload = 10 * 1024 * 1024;

    /// <summary>
    /// Audit yozuvidagi entity turi (wiring bosqichida <c>AuditService</c> ga
    /// ko'chiriladi — shu to'lqinda u fayl muzlatilgan).
    /// </summary>
    public const string AuditEntity = "StudentImport";

    public const string NoFileMessage = "Fayl tanlanmagan";
    public const string NotXlsxMessage = "Faqat .xlsx (Excel) fayl qabul qilinadi";

    /// <summary>
    /// Yangi shablon (.xlsx): 1-varaq — ustunlar, 2-varaq — yo'riqnoma,
    /// mavjud sinflar va mavjud holatlar.
    /// </summary>
    [HttpGet("shablon")]
    public async Task<IActionResult> Template(CancellationToken ct = default)
    {
        var classes = await db.Classes.AsNoTracking()
            .OrderBy(c => c.Grade).ThenBy(c => c.Name).Select(c => c.Name).ToListAsync(ct);
        var statuses = await db.StudentStatuses.AsNoTracking()
            .Where(s => s.IsActive).OrderBy(s => s.Position).ThenBy(s => s.Name)
            .Select(s => s.Name).ToListAsync(ct);

        return File(StudentImportSheet.Template(classes, statuses), XlsxMime, "oquvchilar_shablon.xlsx");
    }

    /// <summary>
    /// 1-bosqich — BUTUN faylni tekshiradi va nima bo'lishini aytadi.
    /// Bazaga HECH NARSA yozilmaydi.
    /// </summary>
    [HttpPost("tekshirish")]
    [RequestSizeLimit(MaxUpload)]
    public async Task<ActionResult<StudentImportPreviewDto>> Validate(
        IFormFile? file, CancellationToken ct = default)
    {
        if (Reject(file) is { } bad) return BadRequest(new { message = bad });

        await using var stream = file!.OpenReadStream();
        var plan = await new StudentImportService(db).ValidateAsync(stream, ct);
        return plan.ToPreview();
    }

    /// <summary>
    /// 2-bosqich — yozadi. Fayl QAYTA tekshiriladi (tekshiruvdan keyin sinf
    /// o'chirilgan yoki holat faolsizlantirilgan bo'lishi mumkin) va bitta ham
    /// xato bo'lsa <c>400</c> qaytadi, hech narsa yozilmaydi.
    ///
    /// <para>
    /// Yangi o'quvchiga tizim akkaunti yaratiladi (mavjud import bilan bir
    /// xil), vasiy qatorlari esa BUTUN PARTIYA uchun bir marta tekislanadi
    /// (<see cref="GuardianSync.EnsureManyAsync"/>) — qator boshiga so'rov
    /// yo'q. Import PUL YOZMAYDI (P1-21): obuna "Moliya → Obunalar" da
    /// ochiladi.
    /// </para>
    /// </summary>
    [HttpPost("tasdiqlash")]
    [RequestSizeLimit(MaxUpload)]
    public async Task<ActionResult<StudentImportCommitDto>> Commit(
        IFormFile? file, CancellationToken ct = default)
    {
        if (Reject(file) is { } bad) return BadRequest(new { message = bad });

        await using var stream = file!.OpenReadStream();
        var plan = await new StudentImportService(db).ValidateAsync(stream, ct);
        if (!plan.Ok) return BadRequest(plan.ToPreview());

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var touched = new List<Student>(plan.Items.Count);

        foreach (var item in plan.Items)
        {
            var student = item.Existing;
            if (student is null)
            {
                student = new Student
                {
                    FullName = item.FullName,
                    LastName = item.LastName,
                    FirstName = item.FirstName,
                    MiddleName = item.MiddleName,
                    ClassName = item.ClassName,
                    BirthDate = item.BirthDate ?? "",
                    Gender = item.Gender ?? "male",
                    Address = item.Address ?? "",
                    ParentFullName = item.ParentFullName ?? "",
                    ParentLastName = item.ParentLastName ?? "",
                    ParentFirstName = item.ParentFirstName ?? "",
                    ParentMiddleName = item.ParentMiddleName ?? "",
                    ParentPhone = item.ParentPhone ?? "",
                    EnrollmentDate = item.EnrollmentDate ?? today,
                    Phone = item.Phone,
                    Language = item.Language,
                    StatusId = item.StatusId,
                };
                db.Students.Add(student);

                var account = AccountFactory.CreateAccountFor(db, "student", student.FullName);
                student.UserId = account.Id;
            }
            else
            {
                // BO'SH KATAK TOZALAMAYDI — faqat to'ldirilgan ustunlar
                // yoziladi (eksport → import aylanishi ma'lumot yo'qotmasin).
                student.FullName = item.FullName;
                student.LastName = item.LastName;
                student.FirstName = item.FirstName;
                student.MiddleName = item.MiddleName;
                student.ClassName = item.ClassName;
                if (item.BirthDate is { } birthDate) student.BirthDate = birthDate;
                if (item.Gender is { } gender) student.Gender = gender;
                if (item.Address is { Length: > 0 } address) student.Address = address;
                if (item.ParentFullName is { } parentFullName)
                {
                    student.ParentFullName = parentFullName;
                    student.ParentLastName = item.ParentLastName ?? "";
                    student.ParentFirstName = item.ParentFirstName ?? "";
                    student.ParentMiddleName = item.ParentMiddleName ?? "";
                }
                if (item.ParentPhone is { } parentPhone) student.ParentPhone = parentPhone;
                if (item.EnrollmentDate is { } enrollment) student.EnrollmentDate = enrollment;
                if (item.Phone is { } phone) student.Phone = phone;
                if (item.Language is { } language) student.Language = language;
                if (item.StatusId is { } statusId) student.StatusId = statusId;
            }
            touched.Add(student);
        }

        audit.Record(AuditEntity, file!.FileName, "create",
            $"Excel'dan import: {plan.CreatedCount} ta yangi, {plan.UpdatedCount} ta yangilandi "
            + $"(fayl: {file.FileName})");

        await db.SaveChangesAsync(ct);

        // Vasiy qatorlari — butun partiya uchun bir marta (SPEC §3.2).
        await GuardianSync.EnsureManyAsync(db, touched);

        return new StudentImportCommitDto(plan.CreatedCount, plan.UpdatedCount, plan.Skipped);
    }

    /// <summary>Fayl umuman qabul qilinadimi. null = qabul qilinadi.</summary>
    private static string? Reject(IFormFile? file)
    {
        if (file is null || file.Length == 0) return NoFileMessage;
        return file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? null : NotXlsxMessage;
    }
}
