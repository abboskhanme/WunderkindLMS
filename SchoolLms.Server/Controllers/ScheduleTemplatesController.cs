using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Jadval variantlari (shablonlar) — SINF yoki O'QUV GURUHI uchun.
///
/// <para>
/// <b>Nega yo'l hali ham <c>classes/{classId}</c>.</b> §2.1.4: guruh darsi
/// mavjud <c>class_id</c> ustunida GURUH id'sini saqlaydi va
/// <c>owner_kind='group'</c> bilan belgilanadi. Yo'l segmenti ham xuddi
/// shunday ishlaydi — u yerga guruh id'si berilsa, controller egani o'zi
/// aniqlaydi. Shu tufayli brauzerdagi jadval sahifasi, API xizmati va
/// ziddiyat tekshiruvi IKKI marta yozilmaydi (EduSchool ham `classId` da
/// guruh id'sini uzatadi).
/// </para>
/// <para>
/// <b>Guruh shabloni o'chirgich o'chiq paytda ham tahrirlanadi</b> — u
/// QORALAMA: birorta haftaga biriktirilmaguncha jurnal, davomat, maosh va
/// turniket uni umuman ko'rmaydi (<see cref="LessonRoster.LiveOwnersAsync"/>
/// o'chirgich o'chiq bo'lsa guruhlarni qaytarmaydi). Biriktirishning o'zini
/// esa <see cref="WeekAssignmentsController"/> rad etadi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule")]
[Route("api/admin/classes/{classId}/schedule-templates")]
public class ScheduleTemplatesController(AppDbContext db) : ControllerBase
{
    private static ScheduleTemplateDto ToDto(ScheduleTemplate t) => new(
        t.Id, t.ClassId, t.Name,
        t.Lessons.OrderBy(l => l.Day).ThenBy(l => l.Period).ThenBy(l => l.SubGroup)
            .Select(l => new ScheduleLessonDto(l.Day, l.Period, l.SubjectId, l.TeacherId, l.SubGroup)).ToList(),
        t.OwnerKind);

    /// <summary>Yo'ldagi id'ni egaga (sinf yoki guruh) aylantiradi.</summary>
    private Task<LessonOwner?> OwnerAsync(string ownerId, CancellationToken ct = default) =>
        LessonRoster.OwnerAsync(db, ownerId, ct);

    /// <summary>
    /// Eganing jadval variantlari.
    ///
    /// <para>
    /// Ega topilmasa (o'chirilgan sinfning "yetim" id'si) — BUGUNGIDEK bo'sh
    /// emas, balki filtr-siz ro'yxat qaytadi: eski xatti-harakat 404 emas edi
    /// va uni o'zgartirish brauzerdagi jadval sahifasini yiqitardi.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ScheduleTemplateDto>>> GetAll(
        string classId, CancellationToken ct = default)
    {
        var owner = await OwnerAsync(classId, ct);
        var query = db.ScheduleTemplates.Include(t => t.Lessons).Where(t => t.ClassId == classId);
        if (owner is not null) query = query.Where(t => t.OwnerKind == owner.Kind);

        var templates = await query.ToListAsync(ct);
        return templates.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<ScheduleTemplateDto>> Create(
        string classId, CreateTemplateRequest req, CancellationToken ct = default)
    {
        var owner = await OwnerAsync(classId, ct);
        if (owner is null) return NotFound();

        var tpl = new ScheduleTemplate { ClassId = classId, Name = req.Name, OwnerKind = owner.Kind };
        db.ScheduleTemplates.Add(tpl);
        await db.SaveChangesAsync(ct);
        return ToDto(tpl);
    }

    [HttpPatch("{templateId}")]
    public async Task<IActionResult> Rename(
        string classId, string templateId, RenameTemplateRequest req, CancellationToken ct = default)
    {
        var tpl = await FindAsync(classId, templateId, ct);
        if (tpl is null) return NotFound();
        tpl.Name = req.Name;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{templateId}")]
    public async Task<IActionResult> Delete(
        string classId, string templateId, CancellationToken ct = default)
    {
        var tpl = await FindAsync(classId, templateId, ct);
        if (tpl is null) return NotFound();
        db.ScheduleTemplates.Remove(tpl);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Jadval katagini butun sinf uchun belgilash (eski oqim — orqaga moslik).
    /// Avval (Day,Period) dagi BARCHA yozuvlar (jumladan guruh bo'linishlari ham) o'chiriladi va
    /// bitta SubGroup=0 lesson qo'shiladi. Yangi UI <c>cell</c> endpoint'idan foydalanadi.
    /// </summary>
    [HttpPut("{templateId}/{day:int}/{period:int}")]
    public async Task<IActionResult> SetSlot(
        string classId, string templateId, int day, int period, ScheduleLessonDto lesson,
        CancellationToken ct = default)
    {
        var owner = await OwnerAsync(classId, ct);
        if (owner is null) return NotFound();
        var tpl = await FindAsync(classId, templateId, ct);
        if (tpl is null) return NotFound();

        var clash = await ScheduleConflicts.ForTemplateAsync(
            db, owner, tpl.Id, [(day, period, lesson.SubGroup)], ct);
        if (clash.Count > 0) return Conflict(new { message = ScheduleConflicts.Message(clash) });

        var existing = tpl.Lessons.Where(l => l.Day == day && l.Period == period).ToList();
        db.RemoveRange(existing);
        tpl.Lessons.Add(new ScheduleLesson
        {
            TemplateId = tpl.Id,
            Day = day,
            Period = period,
            SubjectId = lesson.SubjectId,
            TeacherId = lesson.TeacherId,
            SubGroup = lesson.SubGroup,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Jadval katagini tozalash (har qanday guruhdagi).</summary>
    [HttpDelete("{templateId}/{day:int}/{period:int}")]
    public async Task<IActionResult> ClearSlot(
        string classId, string templateId, int day, int period, CancellationToken ct = default)
    {
        var tpl = await FindAsync(classId, templateId, ct);
        if (tpl is null) return NotFound();

        var existing = tpl.Lessons.Where(l => l.Day == day && l.Period == period).ToList();
        db.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Bir katak (Day,Period) ning to'liq holatini almashtirish. Lessons: bo'sh = tozalash,
    /// 1 ta (SubGroup=0) = butun sinf, 2 ta (SubGroup=1 va SubGroup=2) = bo'lingan.
    /// Validatsiya: SubGroup 0/1/2 dan tashqari va dublikat bo'lsa 400. Day/Period mos kelmasa 400.
    ///
    /// <para>
    /// G-11: saqlashdan OLDIN o'quvchi ziddiyati tekshiriladi — bola bir vaqtda
    /// ham sinf, ham guruh darsida qolib ketmasin. Ziddiyat bo'lsa 409 va
    /// TO'QNASHGAN BOLALARNING ISMLARI qaytadi. Guruh darslari o'chiq ekan
    /// bunday holat mumkin emas va tekshiruv darhol bo'sh qaytadi.
    /// </para>
    /// </summary>
    [HttpPut("{templateId}/cell")]
    public async Task<IActionResult> SetCell(
        string classId, string templateId, SetCellRequest req, CancellationToken ct = default)
    {
        var owner = await OwnerAsync(classId, ct);
        if (owner is null) return NotFound();
        var tpl = await FindAsync(classId, templateId, ct);
        if (tpl is null) return NotFound();

        var lessons = req.Lessons ?? new();
        if (lessons.Any(l => l.Day != req.Day || l.Period != req.Period))
            return BadRequest(new { message = "Day/Period darslarda mos kelmaydi" });
        if (lessons.Any(l => l.SubGroup is < 0 or > 2))
            return BadRequest(new { message = "SubGroup 0, 1 yoki 2 bo'lishi kerak" });
        if (lessons.Count > 1 && lessons.Any(l => l.SubGroup == 0))
            return BadRequest(new { message = "SubGroup=0 (butun sinf) bilan boshqa guruh birga bo'lmaydi" });
        if (lessons.GroupBy(l => l.SubGroup).Any(g => g.Count() > 1))
            return BadRequest(new { message = "Bitta guruhda bir nechta dars bo'lishi mumkin emas" });
        // Guruh darsida sinf ichidagi bo'linish yo'q — guruhning o'zi tanlangan bolalar to'plami.
        if (owner.IsGroup && lessons.Any(l => l.SubGroup != 0))
            return BadRequest(new
            {
                message = "Guruh darsida sinf ichidagi 1/2-guruhga bo'linish bo'lmaydi — "
                          + "guruhning o'zi allaqachon tanlangan o'quvchilar ro'yxati.",
            });

        if (lessons.Count > 0)
        {
            var clash = await ScheduleConflicts.ForTemplateAsync(
                db, owner, tpl.Id, [.. lessons.Select(l => (l.Day, l.Period, l.SubGroup))], ct);
            if (clash.Count > 0) return Conflict(new { message = ScheduleConflicts.Message(clash) });
        }

        var existing = tpl.Lessons.Where(l => l.Day == req.Day && l.Period == req.Period).ToList();
        db.RemoveRange(existing);
        foreach (var l in lessons)
        {
            tpl.Lessons.Add(new ScheduleLesson
            {
                TemplateId = tpl.Id,
                Day = l.Day,
                Period = l.Period,
                SubjectId = l.SubjectId,
                TeacherId = l.TeacherId,
                SubGroup = l.SubGroup,
            });
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<ScheduleTemplate?> FindAsync(string classId, string templateId, CancellationToken ct) =>
        db.ScheduleTemplates.Include(t => t.Lessons)
            .FirstOrDefaultAsync(t => t.Id == templateId && t.ClassId == classId, ct);
}
