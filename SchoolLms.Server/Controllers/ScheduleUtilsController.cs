using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Jadval yordamchi endpointlari — template yaratishda o'qituvchi ziddiyatini tekshirish va h.k.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule")]
[Route("api/admin/schedule")]
public class ScheduleUtilsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Dars jadvali / davomat / jurnal tanlagichlari uchun egalar ro'yxati — BITTA qoida
    /// (docs/modules/track-groups-as-classes.md): yo'nalish guruhini boqmaydigan sinflar
    /// daraja/nom bo'yicha, keyin faol yo'nalish guruhlari nom bo'yicha, keyin (faqat
    /// <c>group_lessons_enabled</c> yoqilganda) oddiy guruhlar. 9–11-sinflar (9-A ...)
    /// bu yerda YO'Q — ular hujjat, moliya va hisobotlar uchun "Sinflar" bo'limida qoladi.
    /// </summary>
    [HttpGet("owners")]
    public async Task<ActionResult<IEnumerable<LessonOwnerItem>>> Owners(CancellationToken ct = default)
        => await LessonRoster.PickerOwnersAsync(db, ct: ct);

    /// <summary>
    /// Barcha template'lardagi o'qituvchi-band-soatlar xaritasi.
    /// Jadval yaratishda: tanlangan o'qituvchi boshqa sinfda shu soatda dars bersa — ogohlantirish.
    ///
    /// <para><c>excludeTemplateId</c> — hozir tahrirlayotgan template o'zi bilan ziddiyat
    /// ko'rsatmasligi uchun o'tkazib yuboriladi.</para>
    ///
    /// <para>
    /// G-11: xarita endi SINF va O'QUV GURUHI shablonlarining ikkalasini ham
    /// ko'radi — aks holda guruhda dars beradigan o'qituvchi "bo'sh" bo'lib
    /// ko'rinar va ikki joyga birdan yozilardi. Guruh darslari o'chirgichi
    /// o'chiq bo'lsa tirik egalar ro'yxatida guruhlar yo'q, ya'ni javob
    /// bugungining aynan o'zi.
    /// </para>
    ///
    /// Javob: teacherId → [{ Day, Period, ClassName, TemplateName, OwnerKind }].
    /// </summary>
    [HttpGet("occupied-slots")]
    public async Task<ActionResult<Dictionary<string, List<OccupiedSlotDto>>>> OccupiedSlots(
        [FromQuery] string? excludeTemplateId)
    {
        var templates = await db.ScheduleTemplates
            .Include(t => t.Lessons)
            .Where(t => excludeTemplateId == null || t.Id != excludeTemplateId)
            .ToListAsync();

        var owners = await LessonRoster.LiveOwnersAsync(db);

        var result = new Dictionary<string, List<OccupiedSlotDto>>(StringComparer.Ordinal);

        foreach (var tpl in templates)
        {
            // Egasi mavjud bo'lmagan (eski o'quv yilidan/o'chirilgan sinf yoki
            // arxivlangan guruh) "yetim" shablon — ziddiyat tekshiruviga qo'shilmaydi.
            if (!owners.TryGetValue(tpl.ClassId, out var owner) || owner.Kind != tpl.OwnerKind) continue;
            var className = owner.Name;

            // (teacherId, day, period) bo'yicha guruhlash — bir soatda ikkala guruh bo'lsa bitta yozuv.
            var slots = tpl.Lessons
                .Where(l => !string.IsNullOrEmpty(l.TeacherId))
                .GroupBy(l => new { l.TeacherId, l.Day, l.Period })
                .Select(g => (TeacherId: g.Key.TeacherId!, g.Key.Day, g.Key.Period));

            foreach (var (teacherId, day, period) in slots)
            {
                if (!result.TryGetValue(teacherId, out var list))
                    result[teacherId] = list = new();

                list.Add(new OccupiedSlotDto(day, period, className, tpl.Name, owner.Kind));
            }
        }

        return result;
    }

    /// <summary>
    /// Shu eganing O'QUVCHILARI boshqa egada (odatda o'quv guruhida) qatnashadigan
    /// darslar — jadval taxtasida FAQAT KO'RSATISH uchun (G-11).
    ///
    /// <para>
    /// Sinf jadvalini tuzayotgan odam bolalarning guruh darslarini ko'rmasa,
    /// har safar 409 ga urilardi. Bu ro'yxat o'sha "band" soatlarni oldindan
    /// ko'rsatadi; tahrirlash mumkin emas, rad etish qoidasi esa baribir
    /// serverda (<c>ScheduleConflicts</c>).
    /// </para>
    /// <para>
    /// Manba — har eganing ASOSIY (eng ko'p darsli) shabloni: maosh va
    /// turniket ham aynan shuni ko'radi, ya'ni ekran bilan raqamlar bir xil
    /// narsaga tayanadi. Guruh darslari o'chirgichi o'chiq bo'lsa ro'yxat
    /// BO'SH — bugungi ekran o'zgarmaydi.
    /// </para>
    /// </summary>
    [HttpGet("pupil-overlay/{ownerId}")]
    public async Task<ActionResult<IEnumerable<PupilOverlaySlotDto>>> PupilOverlay(
        string ownerId, CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, ownerId, ct);
        if (owner is null) return NotFound();
        // Guruh darsi umuman tirik bo'lmasa (o'chirgich o'chiq va yo'nalish guruhi yo'q) — bo'sh.
        if (!(await LessonRoster.GroupScopeAsync(db, ct)).AnyGroup) return new List<PupilOverlaySlotDto>();

        var mine = (await LessonRoster.ForLessonAsync(db, owner, ct: ct))
            .Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (mine.Count == 0) return new List<PupilOverlaySlotDto>();

        var result = new List<PupilOverlaySlotDto>();
        foreach (var (other, tpl) in await TeacherLessons.MainTemplatesAsync(db, ct))
        {
            if (other.Id == owner.Id) continue;
            var shared = (await LessonRoster.ForLessonAsync(db, other, ct: ct))
                .Count(s => mine.Contains(s.Id));
            if (shared == 0) continue;

            foreach (var slot in tpl.Lessons
                .Where(l => l.Day is >= 0 and < 6 && l.Period > 0)
                .GroupBy(l => new { l.Day, l.Period }))
            {
                result.Add(new PupilOverlaySlotDto(
                    slot.Key.Day, slot.Key.Period, other.Id, other.Name, other.Kind,
                    slot.First().SubjectId, shared));
            }
        }
        return result.OrderBy(r => r.Day).ThenBy(r => r.Period).ToList();
    }
}
