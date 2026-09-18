using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Sertifikatlar registri (§2.3) — o'quvchining IELTS/SAT/olimpiada hujjatlari.
///
/// <para>
/// <b>Ruxsat — <c>students</c></b>, <c>CertificateTypesController</c> dagi sabab bilan
/// bir xil. <c>AdminPerm</c> o'qishni xodimga ochadi, YOZISHNI esa faqat <c>students</c>
/// ruxsatiga bog'laydi: sertifikat qo'shish — o'quvchi kartochkasiga yozish.
/// </para>
///
/// <para>
/// <b>Fayl bu yerdan YUKLANMAYDI.</b> Skaner qilingan hujjat mavjud
/// <c>UploadsController</c> (<c>POST /api/admin/uploads</c>, <c>UploadGuard</c> bilan)
/// orqali yuklanadi va bu yerga faqat qaytgan <c>/uploads/...</c> manzili keladi. Ikkinchi
/// yuklash yo'li ochilsa, <c>UploadGuard</c> ning allowlist'i bir joyda yangilanib, ikkinchi
/// joyda eskirib qolardi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/certificates")]
public class CertificatesController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Joriy foydalanuvchi (JWT'dan) — <c>Certificate.CreatedBy</c> uchun.</summary>
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>
    /// Registr ro'yxati. Barcha filtrlar ixtiyoriy; birortasi berilmasa — hammasi,
    /// eng yangisi tepada.
    /// </summary>
    /// <param name="expiringInDays">"Muddati tugayapti" filtri: shu necha kun ichida
    /// tugaydiganlar (muddati o'tib ketganlar ham). IELTS ikki yil amal qiladi —
    /// bu ekrandagi yagona vaqtga bog'liq savol.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CertificateDto>>> GetAll(
        [FromQuery] string? studentId, [FromQuery] Guid? typeId, [FromQuery] string? teacherId,
        [FromQuery] string? subjectId, [FromQuery] string? className,
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? expiringInDays,
        [FromQuery] string? search,
        CancellationToken ct = default)
        => await CertificateService.ListAsync(
            db, studentId, typeId, teacherId, subjectId, className,
            CertificateService.Parse(from), CertificateService.Parse(to),
            expiringInDays, search, ct);

    /// <summary>
    /// "Natijalar" tab'i — bitta tur bo'yicha o'quvchi × ball jadvali (§2.3).
    /// <c>is_scored</c> ustuni aynan shu ekran uchun bor.
    /// </summary>
    [HttpGet("results")]
    public async Task<ActionResult<CertificateResultsDto>> Results(
        [FromQuery] Guid typeId, [FromQuery] string? className, CancellationToken ct = default)
    {
        var result = await CertificateService.ResultsAsync(db, typeId, className, ct);
        if (result is null) return NotFound(new { message = "Sertifikat turi topilmadi" });
        return result;
    }

    /// <summary>
    /// Z-2 — joriy filtrlar bilan RO'YXATNING .xlsx eksporti. O'quvchilar ro'yxati
    /// eksportidan (<c>StudentSearchController.Export</c>) mustaqil: bu registr, import
    /// shabloni bilan bog'liq emas, shuning uchun ustunlar ham import bilan mos EMAS —
    /// ular ekrandagi jadval ustunlarining o'zi.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? studentId, [FromQuery] Guid? typeId, [FromQuery] string? teacherId,
        [FromQuery] string? subjectId, [FromQuery] string? className,
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? expiringInDays,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var rows = await CertificateService.ListAsync(
            db, studentId, typeId, teacherId, subjectId, className,
            CertificateService.Parse(from), CertificateService.Parse(to),
            expiringInDays, search, ct);

        var headers = new[]
        {
            "O'quvchi", "Sinf", "Turi", "Fan(lar)", "O'qituvchi", "Raqami",
            "Ball", "Berilgan", "Muddati", "Fayl", "Izoh",
        };

        var data = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.StudentName,
            r.ClassName,
            r.TypeName,
            // Z-3 — bitta hujjatda bir nechta fan bo'lishi mumkin, vergul bilan.
            r.SubjectNames.Count > 0 ? string.Join(", ", r.SubjectNames) : "",
            r.TeacherName ?? "",
            r.Number ?? "",
            r.Score?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
            r.IssuedOn,
            r.ExpiresOn ?? "",
            r.FileUrl ?? "",
            r.Comment ?? "",
        });

        var bytes = ExcelExport.Build("Sertifikatlar", headers, data);
        return File(bytes, XlsxMime, $"sertifikatlar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Sertifikat BERGAN o'qituvchilar — o'quvchilar ro'yxatidagi
    /// <c>certificateTeacherId</c> filtrining tanlovi. Barcha o'qituvchilar emas:
    /// hech qachon sertifikat bermagan o'qituvchi bu filtrda bo'sh natija beradi.
    /// </summary>
    [HttpGet("teachers")]
    public async Task<ActionResult<IEnumerable<TeacherNameDto>>> IssuingTeachers(
        CancellationToken ct = default)
    {
        var ids = await db.Certificates.AsNoTracking()
            .Where(c => c.TeacherId != null)
            .Select(c => c.TeacherId!)
            .Distinct()
            .ToListAsync(ct);

        return await db.Teachers.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .OrderBy(t => t.FullName)
            .Select(t => new TeacherNameDto(t.Id, t.FullName, ""))
            .ToListAsync(ct);
    }

    [HttpPost]
    public async Task<ActionResult<CertificateDto>> Create(
        CertificatePayload p, CancellationToken ct = default)
    {
        var row = new Certificate { CreatedBy = Uid };

        if (await CertificateService.ApplyAsync(db, row, p, ct) is { } error)
            return BadRequest(new { message = error });

        db.Certificates.Add(row);
        audit.Record(CertificateService.AuditEntity, row.Id.ToString(), "create",
            await SummaryAsync(row, "qo'shildi", ct),
            after: Snapshot(row), studentId: row.StudentId, teacherId: row.TeacherId);

        await db.SaveChangesAsync(ct);
        return await ReadBackAsync(row.Id, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CertificateDto>> Update(
        Guid id, CertificatePayload p, CancellationToken ct = default)
    {
        var row = await db.Certificates.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return NotFound();

        var before = Snapshot(row);
        if (await CertificateService.ApplyAsync(db, row, p, ct) is { } error)
            return BadRequest(new { message = error });

        audit.Record(CertificateService.AuditEntity, row.Id.ToString(), "update",
            await SummaryAsync(row, "tahrirlandi", ct),
            before: before, after: Snapshot(row),
            studentId: row.StudentId, teacherId: row.TeacherId);

        await db.SaveChangesAsync(ct);
        return await ReadBackAsync(row.Id, ct);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var row = await db.Certificates.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return NotFound();

        var summary = await SummaryAsync(row, "o'chirildi", ct);
        var before = Snapshot(row);
        db.Certificates.Remove(row);

        audit.Record(CertificateService.AuditEntity, row.Id.ToString(), "delete", summary,
            before: before, studentId: row.StudentId, teacherId: row.TeacherId);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------------
    //  Yordamchilar
    // ------------------------------------------------------------------

    /// <summary>
    /// Saqlangan qatorni NOMLARI bilan qaytaradi. Qayta o'qiymiz, chunki ro'yxat
    /// DTO'si o'quvchi/tur/fan/o'qituvchi nomlarini ham o'z ichiga oladi va ekran
    /// yangi qatorni darrov to'liq ko'rsatishi kerak.
    /// </summary>
    private async Task<ActionResult<CertificateDto>> ReadBackAsync(Guid id, CancellationToken ct)
    {
        var list = await CertificateService.ListAsync(db, ct: ct);
        var dto = list.FirstOrDefault(c => c.Id == id);
        return dto is null ? NotFound() : dto;
    }

    /// <summary>Audit uchun o'qiladigan bir qator: kim, qaysi tur, qachon.</summary>
    private async Task<string> SummaryAsync(Certificate row, string verb, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking()
            .Where(s => s.Id == row.StudentId).Select(s => s.FullName).FirstOrDefaultAsync(ct);
        var type = await db.CertificateTypes.AsNoTracking()
            .Where(t => t.Id == row.TypeId).Select(t => t.Name).FirstOrDefaultAsync(ct);

        var score = row.Score is { } s2 ? $", ball {s2:0.##}" : "";
        return $"Sertifikat {verb}: {student ?? row.StudentId} — «{type ?? "?"}»"
             + $"{score} ({CertificateService.Fmt(row.IssuedOn)})";
    }

    /// <summary>Audit jurnalidagi before/after uchun qator surati.</summary>
    private static object Snapshot(Certificate c) => new
    {
        c.StudentId,
        c.TypeId,
        c.SubjectId,
        c.TeacherId,
        c.Number,
        c.Score,
        IssuedOn = CertificateService.Fmt(c.IssuedOn),
        ExpiresOn = c.ExpiresOn is { } e ? CertificateService.Fmt(e) : null,
        c.FileUrl,
        c.Comment,
    };
}
