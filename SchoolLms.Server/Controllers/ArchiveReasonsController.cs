using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Arxivlash sabablari katalogi (§2.2) — "Sozlamalar → Arxivlash sabablari".
///
/// <para>
/// <b>Nega <c>AdminPerm("settings")</c>.</b> Bu ma'lumotnoma, ya'ni sozlama; uni
/// tahrirlash huquqi Sozlamalar bo'limiga tegishli. O'QISH esa keng kerak —
/// o'quvchini arxivlash oynasi shu ro'yxatni ko'rsatadi va u "O'quv bo'limi"da
/// yashaydi. <see cref="AdminPermAttribute"/> aynan shunga mo'ljallangan: xodim
/// uchun GET har doim ochiq, yozish esa kalitga bog'liq.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("settings")]
[Route("api/admin/archive-reasons")]
public class ArchiveReasonsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Katalog. Sukut bo'yicha faqat FAOL qatorlar — arxivlash oynasiga aynan shu
    /// ro'yxat kerak. Sozlamalar ekrani <c>includeInactive=true</c> bilan hammasini
    /// so'raydi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentArchiveReasonDto>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var rows = await db.StudentArchiveReasons.AsNoTracking()
            .Where(r => includeInactive || r.IsActive)
            .OrderBy(r => r.Position).ThenBy(r => r.Name)
            .ToListAsync(ct);

        // "Nechta o'quvchida ishlatilgan" — o'chirish tugmasini ko'rsatish/yashirish uchun
        // va §2.2 ning butun maqsadi (guruhlab ko'rish) shu sondan boshlanadi.
        var used = await UsedByAsync(null, ct);

        return rows
            .Select(r => new StudentArchiveReasonDto(
                r.Id, r.Name, r.IsActive, r.Position, used.GetValueOrDefault(r.Id)))
            .ToList();
    }

    [HttpPost]
    public async Task<ActionResult<StudentArchiveReasonDto>> Create(
        SaveArchiveReasonRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = "Sabab nomini yozing" });
        if (await db.StudentArchiveReasons.AnyAsync(r => r.Name == name, ct))
            return BadRequest(new { message = "Bunday nomli sabab allaqachon bor" });

        var position = req.Position ?? 0;
        if (position < 0) return BadRequest(new { message = "Tartib raqami manfiy bo'lmasin" });

        var row = new StudentArchiveReason
        {
            Name = name,
            IsActive = req.IsActive ?? true,
            Position = position,
        };
        db.StudentArchiveReasons.Add(row);
        await db.SaveChangesAsync(ct);
        return new StudentArchiveReasonDto(row.Id, row.Name, row.IsActive, row.Position, 0);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StudentArchiveReasonDto>> Update(
        Guid id, SaveArchiveReasonRequest req, CancellationToken ct = default)
    {
        var row = await db.StudentArchiveReasons.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = "Sabab nomini yozing" });
        if (await db.StudentArchiveReasons.AnyAsync(r => r.Name == name && r.Id != id, ct))
            return BadRequest(new { message = "Bunday nomli sabab allaqachon bor" });

        row.Name = name;
        if (req.IsActive is { } active) row.IsActive = active;
        if (req.Position is { } position)
        {
            if (position < 0) return BadRequest(new { message = "Tartib raqami manfiy bo'lmasin" });
            row.Position = position;
        }
        await db.SaveChangesAsync(ct);

        var used = (await UsedByAsync(id, ct)).GetValueOrDefault(id);
        return new StudentArchiveReasonDto(row.Id, row.Name, row.IsActive, row.Position, used);
    }

    /// <summary>
    /// O'chirish — FAQAT ishlatilmagan qator uchun. Ishlatilgani o'chirilsa arxivdagi
    /// o'quvchi "nega ketgani"ni yo'qotardi (FK <c>RESTRICT</c>, ParityModel.cs), va
    /// §2.2 ning butun maqsadi shu bilan yo'qolardi. Ro'yxatdan chiqarish yo'li bitta —
    /// <c>is_active = false</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var row = await db.StudentArchiveReasons.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NoContent();

        var used = (await UsedByAsync(id, ct)).GetValueOrDefault(id);
        if (used > 0)
            return BadRequest(new
            {
                message = $"Bu sabab {used} ta o'quvchida ishlatilgan — o'chirib bo'lmaydi. "
                    + "Ro'yxatdan olib tashlash uchun uni faolsizlantiring.",
            });

        db.StudentArchiveReasons.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Pupils per reason: archived with it now, or who left with it earlier (student_events —
    /// docs/modules/student-flow.md; a restored pupil's departure still names the reason, and its
    /// FK is RESTRICT). Each pupil once per reason. <paramref name="only"/> — one reason; null — all.
    /// </summary>
    private async Task<Dictionary<Guid, int>> UsedByAsync(Guid? only, CancellationToken ct)
    {
        var students = await db.Students.AsNoTracking()
            .Where(s => s.ArchiveReasonId != null && (only == null || s.ArchiveReasonId == only))
            .Select(s => new { ReasonId = s.ArchiveReasonId!.Value, StudentId = s.Id })
            .ToListAsync(ct);
        var departures = await db.StudentEvents.AsNoTracking()
            .Where(e => e.ReasonId != null && (only == null || e.ReasonId == only))
            .Select(e => new { ReasonId = e.ReasonId!.Value, e.StudentId })
            .Distinct()
            .ToListAsync(ct);
        return students.Concat(departures)
            .Distinct()
            .GroupBy(x => x.ReasonId)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
