using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Sertifikat TURLARI katalogi — Sozlamalarda boshqariladi (§2.3).
///
/// <para>
/// <b>Ruxsat — <c>students</c>.</b> §2.3: EduSchool bu bo'limga ALOHIDA ruxsat bermaydi,
/// menyu yozuvi <c>getStudents</c> ga bog'langan. Bizdagi ekvivalenti —
/// <c>AdminPerm("students")</c>, ya'ni <c>StudentsController</c> dagi AYNAN o'sha darvoza:
/// o'qish xodimga ochiq, YOZISH esa faqat <c>students</c> ruxsati bo'lganda. Sertifikat
/// — o'quvchining hujjati; uni ko'ra oladigan odam o'quvchilar ro'yxatini ham ko'radi,
/// teskarisi ham to'g'ri.
/// </para>
///
/// <para>
/// <b>Jadval BO'SH holda yetkaziladi.</b> Migratsiya birorta tur seed qilmaydi — IELTS'mi,
/// olimpiadami, buni maktab o'zi biladi. Shuning uchun ekranning bo'sh holati "hali yo'q"
/// emas, "birinchi turni qo'shing" degan chaqiriq bo'lishi kerak (CertificateTypesPage.tsx).
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/certificate-types")]
public class CertificateTypesController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>
    /// Turlar ro'yxati — har birida NECHTA hujjat borligi bilan.
    /// </summary>
    /// <param name="activeOnly">true = faqat faol turlar (forma tanlovi uchun).</param>
    /// <param name="scoredOnly">true = faqat ballik turlar ("Natijalar" tab'i uchun).</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CertificateTypeDto>>> GetAll(
        [FromQuery] bool activeOnly = false, [FromQuery] bool scoredOnly = false,
        CancellationToken ct = default)
    {
        var q = db.CertificateTypes.AsNoTracking();
        if (activeOnly) q = q.Where(t => t.IsActive);
        if (scoredOnly) q = q.Where(t => t.IsScored);

        // Hujjatlar soni SERVERDA sanaladi: "o'chirib bo'ladimi" savolining javobi
        // brauzerga taxmin qilib qoldirilmaydi.
        var counts = await db.Certificates.AsNoTracking()
            .GroupBy(c => c.TypeId)
            .Select(g => new { TypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TypeId, x => x.Count, ct);

        var types = await q.OrderBy(t => t.Name).ToListAsync(ct);
        return types.Select(t => ToDto(t, counts.GetValueOrDefault(t.Id))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CertificateTypeDto>> Create(
        CertificateTypePayload p, CancellationToken ct = default)
    {
        if (await CertificateService.ValidateTypeAsync(db, p.Name, null, ct) is { } error)
            return BadRequest(new { message = error });

        var type = new CertificateType
        {
            Name = p.Name.Trim(),
            IsScored = p.IsScored,
            IsActive = p.IsActive,
        };
        db.CertificateTypes.Add(type);

        audit.Record(CertificateService.AuditEntityType, type.Id.ToString(), "create",
            $"Sertifikat turi qo'shildi: «{type.Name}»"
            + (type.IsScored ? " (ballik)" : ""),
            after: new { type.Name, type.IsScored, type.IsActive });

        await db.SaveChangesAsync(ct);
        return ToDto(type, 0);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CertificateTypeDto>> Update(
        Guid id, CertificateTypePayload p, CancellationToken ct = default)
    {
        var type = await db.CertificateTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound();

        if (await CertificateService.ValidateTypeAsync(db, p.Name, id, ct) is { } error)
            return BadRequest(new { message = error });

        var used = await db.Certificates.CountAsync(c => c.TypeId == id, ct);

        // ISHLATILGAN turning "ballik" bayrog'ini o'zgartirish — mavjud hujjatlarni
        // qoidaga ZID holatga tushiradi: ballik emas deb belgilangan turda ball bilan
        // yozilgan hujjatlar qolib ketardi (baza buni ushlamaydi, §2.3). Shuning uchun
        // to'sib, nima qilish kerakligini aytamiz.
        if (used > 0 && type.IsScored != p.IsScored)
            return BadRequest(new
            {
                message = $"«{type.Name}» turida {used} ta sertifikat bor — «ball qo'yiladi» "
                        + "belgisini endi o'zgartirib bo'lmaydi. Yangi tur oching va "
                        + "bu turni «Faol emas» qilib qo'ying.",
            });

        var before = new { type.Name, type.IsScored, type.IsActive };
        type.Name = p.Name.Trim();
        type.IsScored = p.IsScored;
        type.IsActive = p.IsActive;

        audit.Record(CertificateService.AuditEntityType, type.Id.ToString(), "update",
            $"Sertifikat turi tahrirlandi: «{type.Name}»",
            before: before, after: new { type.Name, type.IsScored, type.IsActive });

        await db.SaveChangesAsync(ct);
        return ToDto(type, used);
    }

    /// <summary>
    /// Turni o'chirish — FAQAT hech qayerda ishlatilmagan bo'lsa (§2.3 dagi
    /// <c>on delete restrict</c>). Aks holda 400 va nima qilish kerakligi.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var type = await db.CertificateTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound();

        if (await CertificateService.DeleteTypeBlockedAsync(db, type, ct) is { } blocked)
            return BadRequest(new { message = blocked });

        db.CertificateTypes.Remove(type);

        audit.Record(CertificateService.AuditEntityType, type.Id.ToString(), "delete",
            $"Sertifikat turi o'chirildi: «{type.Name}»",
            before: new { type.Name, type.IsScored, type.IsActive });

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static CertificateTypeDto ToDto(CertificateType t, int count) =>
        new(t.Id, t.Name, t.IsScored, t.IsActive, count);
}
