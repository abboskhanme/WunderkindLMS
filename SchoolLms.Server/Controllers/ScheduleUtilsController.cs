using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Jadval yordamchi endpointlari — template yaratishda o'qituvchi ziddiyatini tekshirish va h.k.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/schedule")]
public class ScheduleUtilsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Barcha template'lardagi o'qituvchi-band-soatlar xaritasi.
    /// Jadval yaratishda: tanlangan o'qituvchi boshqa sinfda shu soatda dars bersa — ogohlantirish.
    ///
    /// <para><c>excludeTemplateId</c> — hozir tahrirlayotgan template o'zi bilan ziddiyat
    /// ko'rsatmasligi uchun o'tkazib yuboriladi.</para>
    ///
    /// Javob: teacherId → [{ Day, Period, ClassName, TemplateName }].
    /// </summary>
    [HttpGet("occupied-slots")]
    public async Task<ActionResult<Dictionary<string, List<OccupiedSlotDto>>>> OccupiedSlots(
        [FromQuery] string? excludeTemplateId)
    {
        var templates = await db.ScheduleTemplates
            .Include(t => t.Lessons)
            .Where(t => excludeTemplateId == null || t.Id != excludeTemplateId)
            .ToListAsync();

        var classNames = await db.Classes
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var result = new Dictionary<string, List<OccupiedSlotDto>>(StringComparer.Ordinal);

        foreach (var tpl in templates)
        {
            var className = classNames.GetValueOrDefault(tpl.ClassId, "");

            // (teacherId, day, period) bo'yicha guruhlash — bir soatda ikkala guruh bo'lsa bitta yozuv.
            var slots = tpl.Lessons
                .Where(l => !string.IsNullOrEmpty(l.TeacherId))
                .GroupBy(l => new { l.TeacherId, l.Day, l.Period })
                .Select(g => (TeacherId: g.Key.TeacherId!, g.Key.Day, g.Key.Period));

            foreach (var (teacherId, day, period) in slots)
            {
                if (!result.TryGetValue(teacherId, out var list))
                    result[teacherId] = list = new();

                list.Add(new OccupiedSlotDto(day, period, className, tpl.Name));
            }
        }

        return result;
    }
}
