using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Chorak haftalariga jadval biriktirish — SINF yoki O'QUV GURUHI uchun.
///
/// <para>
/// <b>Bu — cut-over darvozasi (§2.1.4, §4.3).</b> Guruh shabloni yaratilishi
/// mumkin, lekin u HAFTAGA BIRIKTIRILMAGUNCHA hech bir dars mavjud emas.
/// Shuning uchun <c>school_meta.group_lessons_enabled</c> o'chiq ekan aynan
/// SHU endpoint guruh biriktirishini rad etadi — maktab o'chirgichni yoqmasa,
/// jurnal, davomat, hisobot, maosh va turniket raqamlari bugungidek qoladi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule")]
[Route("api/admin/classes/{classId}/week-assignments")]
public class WeekAssignmentsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Eganing shu chorakdagi hafta biriktirishlari.
    ///
    /// <para>
    /// Ega topilmasa (o'chirilgan sinfning "yetim" id'si) filtr-siz ro'yxat
    /// qaytadi — bugungi xatti-harakat 404 emas edi va uni o'zgartirish
    /// brauzerdagi jadval sahifasini yiqitardi.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WeekAssignmentDto>>> Get(
        string classId, [FromQuery] int quarter, CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, classId, ct);
        var query = db.WeekAssignments.Where(a => a.ClassId == classId && a.Quarter == quarter);
        if (owner is not null) query = query.Where(a => a.OwnerKind == owner.Kind);

        return await query
            .OrderBy(a => a.Week)
            .Select(a => new WeekAssignmentDto(a.Week, a.TemplateId))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Chorakning barcha haftalari uchun biriktirishlarni almashtiradi.
    ///
    /// <para>Ikki to'siq bor:</para>
    /// <list type="number">
    ///   <item><b>Cut-over o'chirgichi</b> — guruh biriktirishi o'chirgich
    ///     yoqilmaguncha 409 bilan rad etiladi.</item>
    ///   <item><b>O'quvchi ziddiyati (G-11)</b> — bola bir (kun, dars) da
    ///     ikki xil egaga tegishli darsda bo'lib qolsa 409 va TO'QNASHGAN
    ///     BOLALARNING ISMLARI qaytadi.</item>
    /// </list>
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Save(
        string classId, SaveWeekAssignmentsRequest req, CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, classId, ct);
        if (owner is null) return NotFound();

        // Yo'nalish guruhi o'chirgichga qaramaydi — u sinf kabi haftaga biriktiriladi.
        if (!await LessonRoster.LessonsLiveAsync(db, owner, ct))
            return Conflict(new
            {
                message = "Guruh darslari hali yoqilmagan. Guruh jadvalini haftaga biriktirish "
                          + "uchun avval Sozlamalardan \"Guruh darslari\" o'chirgichini yoqing "
                          + "(tizim egasi huquqi).",
            });

        var wanted = req.Assignments ?? [];
        var clash = await ScheduleConflicts.ForAssignmentsAsync(
            db, owner, req.Quarter, [.. wanted.Select(a => (a.Week, a.TemplateId))], ct);
        if (clash.Count > 0) return Conflict(new { message = ScheduleConflicts.Message(clash) });

        var existing = db.WeekAssignments.Where(
            a => a.ClassId == classId && a.Quarter == req.Quarter && a.OwnerKind == owner.Kind);
        db.WeekAssignments.RemoveRange(existing);
        db.WeekAssignments.AddRange(wanted.Select(a => new WeekAssignment
        {
            ClassId = classId,
            Quarter = req.Quarter,
            Week = a.Week,
            TemplateId = a.TemplateId,
            OwnerKind = owner.Kind,
        }));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
