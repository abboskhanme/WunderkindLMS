using System.Text.RegularExpressions;
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
/// O'quvchi holati taglari — katalog (CRUD) va o'quvchiga holat qo'yish
/// (docs/modules/students-parity.md §2.3, S-5).
///
/// <para>
/// <b>Nega <c>AdminPerm("students")</c>, <c>settings</c> emas.</b> Holat —
/// O'quv bo'limining ish vositasi ("VIP", "Sinov muddatida"), uni ro'yxatdan
/// turib almashtiradigan odam bilan katalogni to'ldiradigan odam bir xil.
/// Menyuda ham u O'quv bo'limi ostida turadi — sertifikat turlari bilan bir
/// xil sabab (navigation.ts dagi izoh): SOZLAMALAR marshruti <c>settings</c>
/// ruxsatiga bog'langan, API esa <c>students</c> ga — ikkovi zid bo'lardi.
/// </para>
/// <para>
/// <b>O'CHIRISH — faqat ishlatilmagan qator uchun.</b> Ishlatilgani
/// o'chirilsa baza <c>on delete set null</c> bilan o'quvchilarning tagini
/// jimgina bo'shatardi. Ro'yxatdan chiqarish yo'li bitta:
/// <c>isActive = false</c> — qo'yilgan holatlar joyida qoladi, yangi
/// tanlovda esa ko'rinmaydi.
/// </para>
/// <para>
/// <b><c>isDefault</c> qatorlar</b> tahrirlanmaydi va o'chirilmaydi (§2.3.1).
/// Migratsiya bunday qator YOZMAGAN; mijoz sukut ro'yxatini bersa, ular shu
/// bayroq bilan keladi va bu tekshiruv ularni himoya qiladi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/student-statuses")]
public partial class StudentStatusesController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>
    /// Audit yozuvidagi entity turi. <c>AuditService.cs</c> shu to'lqinda
    /// muzlatilgan fayl (bir nechta slice unga tegadi), shuning uchun
    /// konstanta shu yerda; wiring bosqichida u <c>AuditService</c> ga
    /// ko'chiriladi — <c>EntityDebtorStatus</c> bilan yonma-yon.
    /// </summary>
    public const string AuditEntity = AuditService.EntityStudentStatus;

    public const string ColorMessage = "Rang #RRGGBB ko'rinishida bo'lsin (masalan #34C759)";
    public const string NameRequiredMessage = "Holat nomini yozing";
    public const string NameTakenMessage = "Bunday nomli holat allaqachon bor";
    public const string DefaultLockedMessage =
        "Tizim holatini tahrirlab ham, o'chirib ham bo'lmaydi";

    /// <summary>Baza CHECK constraint'i bilan AYNAN bir xil shakl (`ck_student_statuses_color`).</summary>
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    /// <summary>
    /// Katalog. Sukut bo'yicha faqat FAOL qatorlar — ro'yxatdagi tanlovga
    /// aynan shular kerak. Katalog ekrani <c>includeInactive=true</c> bilan
    /// hammasini so'raydi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentStatusTagDto>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var rows = await db.StudentStatuses.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Position).ThenBy(s => s.Name)
            .ToListAsync(ct);

        var used = await db.Students.AsNoTracking()
            .Where(s => s.StatusId != null)
            .GroupBy(s => s.StatusId!.Value)
            .Select(g => new { StatusId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StatusId, x => x.Count, ct);

        return rows.Select(r => ToDto(r, used.GetValueOrDefault(r.Id))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<StudentStatusTagDto>> Create(
        SaveStudentStatusRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = NameRequiredMessage });
        if (await db.StudentStatuses.AnyAsync(s => s.Name == name, ct))
            return BadRequest(new { message = NameTakenMessage });

        var color = NormalizeColor(req.Color);
        if (color is null && !string.IsNullOrWhiteSpace(req.Color))
            return BadRequest(new { message = ColorMessage });

        var position = req.Position ?? 0;
        if (position < 0) return BadRequest(new { message = "Tartib raqami manfiy bo'lmasin" });

        var row = new StudentStatus
        {
            Name = name,
            Color = color,
            Position = position,
            IsActive = req.IsActive ?? true,
        };
        db.StudentStatuses.Add(row);
        await db.SaveChangesAsync(ct);
        return ToDto(row, 0);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StudentStatusTagDto>> Update(
        Guid id, SaveStudentStatusRequest req, CancellationToken ct = default)
    {
        var row = await db.StudentStatuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null) return NotFound();
        if (row.IsDefault) return BadRequest(new { message = DefaultLockedMessage });

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = NameRequiredMessage });
        if (await db.StudentStatuses.AnyAsync(s => s.Name == name && s.Id != id, ct))
            return BadRequest(new { message = NameTakenMessage });

        // Rang: berilmasa tegilmaydi; bo'sh satr berilsa TOZALANADI (neytral).
        if (req.Color is not null)
        {
            if (req.Color.Trim().Length == 0) row.Color = null;
            else
            {
                var color = NormalizeColor(req.Color);
                if (color is null) return BadRequest(new { message = ColorMessage });
                row.Color = color;
            }
        }

        row.Name = name;
        if (req.Position is { } position)
        {
            if (position < 0) return BadRequest(new { message = "Tartib raqami manfiy bo'lmasin" });
            row.Position = position;
        }
        if (req.IsActive is { } active) row.IsActive = active;

        await db.SaveChangesAsync(ct);
        var used = await db.Students.CountAsync(s => s.StatusId == id, ct);
        return ToDto(row, used);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var row = await db.StudentStatuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null) return NoContent();
        if (row.IsDefault) return BadRequest(new { message = DefaultLockedMessage });

        var used = await db.Students.CountAsync(s => s.StatusId == id, ct);
        if (used > 0)
            return BadRequest(new
            {
                message = $"Bu holat {used} ta o'quvchida qo'yilgan — o'chirib bo'lmaydi. "
                    + "Ro'yxatdan olib tashlash uchun uni faolsizlantiring.",
            });

        db.StudentStatuses.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// O'quvchiga holat qo'yish yoki olib tashlash (<c>statusId: null</c>) —
    /// EduSchool ro'yxatdagi rangli tanlovni aynan shunday ishlatadi
    /// (§2.3.1: <c>PUT student {_id, statusId}</c>).
    ///
    /// <para>
    /// Marshrut <c>~/</c> bilan yozilgan: u o'quvchiga tegishli, lekin
    /// <c>StudentsController</c> boshqa slice'ga tegishli fayl — yangi metodni
    /// unga qo'shish birlashtirish (merge) konfliktini yaratardi.
    /// </para>
    /// <para>
    /// Faolsizlantirilgan holatni QO'YIB BO'LMAYDI (mavjudlari qoladi) —
    /// katalogdan chiqarilgan qator qaytadan tarqalib ketmasligi uchun.
    /// </para>
    /// </summary>
    [HttpPut("~/api/admin/students/{studentId}/status")]
    public async Task<IActionResult> SetStudentStatus(
        string studentId, SetStudentStatusRequest req, CancellationToken ct = default)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound();

        StudentStatus? status = null;
        if (req.StatusId is { } statusId)
        {
            status = await db.StudentStatuses.FirstOrDefaultAsync(s => s.Id == statusId, ct);
            if (status is null) return BadRequest(new { message = "Holat topilmadi" });
            if (!status.IsActive)
                return BadRequest(new { message = "Bu holat katalogdan chiqarilgan — tanlab bo'lmaydi" });
        }

        if (student.StatusId == req.StatusId) return NoContent();

        var previous = student.StatusId is { } old
            ? (await db.StudentStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == old, ct))?.Name
            : null;

        student.StatusId = req.StatusId;
        audit.Record(AuditEntity, student.Id, "update",
            $"O'quvchi holati: {previous ?? "yo'q"} → {status?.Name ?? "yo'q"} ({student.FullName})",
            before: new { Status = previous },
            after: new { Status = status?.Name },
            studentId: student.Id);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static StudentStatusTagDto ToDto(StudentStatus s, int usedBy) =>
        new(s.Id, s.Name, s.Color, s.Position, s.IsDefault, s.IsActive, usedBy);

    /// <summary>
    /// <c>#RRGGBB</c> ga keltiradi (katta harfda). Bo'sh — null (rang yo'q);
    /// shakli noto'g'ri — null va chaqiruvchi 400 qaytaradi.
    /// </summary>
    private static string? NormalizeColor(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return null;
        if (!v.StartsWith('#')) v = "#" + v;
        return ColorPattern().IsMatch(v) ? "#" + v[1..].ToUpperInvariant() : null;
    }
}
