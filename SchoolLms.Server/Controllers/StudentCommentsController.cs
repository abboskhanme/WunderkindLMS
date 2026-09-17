using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quvchi haqidagi izohlar — docs/modules/students-parity.md §2.3 (S-11).
///
/// <para>
/// <b>Ball EMAS.</b> Intizomiy ball 100 dan ayriladi, hisobotga tushadi va
/// ota-onaga xabar ketishi mumkin (<c>DisciplineController</c>). Izoh esa
/// ballsiz kuzatuv: "onasi bilan gaplashildi", "olimpiadaga tayyorlanmoqda".
/// Shuning uchun alohida jadval va alohida marshrut (StudentComments.cs
/// dagi izoh).
/// </para>
/// <para>
/// <b>Kim tahrirlay oladi:</b> izohni YOZGAN xodim, hamda admin/superadmin.
/// EduSchool'da bu alohida ruxsatlar (<c>editStudentComments</c>,
/// <c>deleteStudentComments</c>); bizda ruxsat ro'yxati kengaytirilmadi
/// (§4.2 — yangi ruxsat kaliti wiring ishi), shuning uchun qoida
/// MUALLIFLIK asosida ishlaydi: o'zgalarning izohini oddiy xodim
/// o'zgartira olmaydi.
/// </para>
/// <para>
/// <b>Rasm va fayl</b> mavjud <c>POST /api/admin/uploads</c> orqali
/// yuklanadi; bu yerda faqat manzil (<c>/uploads/...</c>) saqlanadi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/students")]
public class StudentCommentsController(AppDbContext db) : ControllerBase
{
    public const string BodyRequiredMessage = "Izoh matnini yozing";
    public const string KindMessage = "Izoh turi: ijobiy yoki salbiy";
    public const string ForbiddenMessage =
        "Bu izohni faqat uni yozgan xodim yoki administrator o'zgartira oladi";

    /// <summary>Izoh matnining eng katta uzunligi — tasodifiy "butun hujjatni yopishtirish" dan himoya.</summary>
    private const int MaxBody = 4000;

    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    private bool IsAdmin => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin);

    /// <summary>
    /// Bitta o'quvchining izohlari — eng yangisi tepada.
    /// </summary>
    /// <param name="kind">
    /// <c>positive</c> | <c>negative</c> — §2.3.1 dagi tur bo'yicha filtr.
    /// Berilmasa hammasi.
    /// </param>
    [HttpGet("{studentId}/comments")]
    public async Task<ActionResult<IEnumerable<StudentCommentDto>>> GetForStudent(
        string studentId, [FromQuery] string? kind = null, CancellationToken ct = default)
    {
        if (!await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId, ct)) return NotFound();

        var q = db.StudentComments.AsNoTracking().Where(c => c.StudentId == studentId);
        var wanted = (kind ?? "").Trim().ToLowerInvariant();
        if (StudentCommentKind.IsValid(wanted)) q = q.Where(c => c.Kind == wanted);

        var rows = await q.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    [HttpPost("{studentId}/comments")]
    public async Task<ActionResult<StudentCommentDto>> Create(
        string studentId, SaveStudentCommentRequest req, CancellationToken ct = default)
    {
        if (!await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId, ct)) return NotFound();

        var kind = (req.Kind ?? "").Trim().ToLowerInvariant();
        if (!StudentCommentKind.IsValid(kind)) return BadRequest(new { message = KindMessage });

        var body = (req.Body ?? "").Trim();
        if (body.Length == 0) return BadRequest(new { message = BodyRequiredMessage });
        if (body.Length > MaxBody)
            return BadRequest(new { message = $"Izoh juda uzun (eng ko'pi {MaxBody} belgi)" });

        // Muallif JWT'dan — so'rov tanasidan EMAS (SPEC §4.4). Akkauntsiz
        // (tizim) chaqiruv bo'lmaydi: FK `users` ga RESTRICT bilan bog'langan.
        if (Uid.Length == 0) return Forbid();

        var row = new StudentComment
        {
            StudentId = studentId,
            Kind = kind,
            Body = body,
            ImageUrl = Blank(req.ImageUrl),
            FileUrl = Blank(req.FileUrl),
            CreatedBy = Uid,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudentComments.Add(row);
        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([row], ct))[0];
    }

    [HttpPut("comments/{id:guid}")]
    public async Task<ActionResult<StudentCommentDto>> Update(
        Guid id, SaveStudentCommentRequest req, CancellationToken ct = default)
    {
        var row = await db.StudentComments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return NotFound();
        if (!CanEdit(row)) return BadRequest(new { message = ForbiddenMessage });

        var kind = (req.Kind ?? "").Trim().ToLowerInvariant();
        if (kind.Length > 0)
        {
            if (!StudentCommentKind.IsValid(kind)) return BadRequest(new { message = KindMessage });
            row.Kind = kind;
        }

        var body = (req.Body ?? "").Trim();
        if (body.Length == 0) return BadRequest(new { message = BodyRequiredMessage });
        if (body.Length > MaxBody)
            return BadRequest(new { message = $"Izoh juda uzun (eng ko'pi {MaxBody} belgi)" });
        row.Body = body;

        // null = tegilmaydi; bo'sh satr = olib tashlanadi.
        if (req.ImageUrl is not null) row.ImageUrl = Blank(req.ImageUrl);
        if (req.FileUrl is not null) row.FileUrl = Blank(req.FileUrl);

        row.UpdatedAt = AppClock.NowInstant;
        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([row], ct))[0];
    }

    [HttpDelete("comments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var row = await db.StudentComments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return NoContent();
        if (!CanEdit(row)) return BadRequest(new { message = ForbiddenMessage });

        db.StudentComments.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private bool CanEdit(StudentComment row) =>
        IsAdmin || (Uid.Length > 0 && row.CreatedBy == Uid);

    /// <summary>Mualliflar nomini bitta so'rovda oladi (sikl ichida so'rov yo'q).</summary>
    private async Task<List<StudentCommentDto>> ToDtosAsync(
        IReadOnlyList<StudentComment> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var authorIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return [.. rows.Select(r => new StudentCommentDto(
            r.Id, r.StudentId, r.Kind, r.Body, r.ImageUrl, r.FileUrl,
            r.CreatedBy, names.GetValueOrDefault(r.CreatedBy, "Xodim"),
            r.CreatedAt, r.UpdatedAt, CanEdit(r)))];
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
