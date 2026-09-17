using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Cut-over o'chirgichi — <c>school_meta.group_lessons_enabled</c>
/// (docs/modules/students-parity.md §4.3, 3-qadam).
///
/// <para>
/// <b>Nega alohida endpoint, umumiy sozlamalar ichida emas.</b> Bu bayroq —
/// oddiy sozlama emas, BIR MARTALIK o'tish. U yoqilgan lahzada guruh
/// jadvallari haftaga biriktirila boshlaydi va o'sha zahoti jurnal, davomat,
/// hisobot, MAOSH va turniket raqamlari yangi manbani ko'radi. Shuning uchun:
/// </para>
/// <list type="bullet">
///   <item>yozish faqat TIZIM EGASIga (<c>superadmin</c>) — oddiy admin ham
///     emas: §4.3 checklistini bajarish kerak;</item>
///   <item>har o'zgarish auditga tushadi (kim, qachon, nimadan nimaga);</item>
///   <item>o'qish esa har qanday admin/xodimga ochiq — ekranlar guruh
///     jadvalini ko'rsatish yoki ko'rsatmaslikni shundan biladi.</item>
/// </list>
/// <para>
/// O'chirib qo'yish ham mumkin: shundan keyin guruh jadvallari haftaga
/// biriktirilmaydi va hamma hisob-kitob yana faqat sinflarni ko'radi. YOZIB
/// BO'LINGAN jurnal qatorlari o'chmaydi — ular joyida qoladi (§4.3).
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule")]
[Route("api/admin/group-lessons")]
public class GroupLessonsSwitchController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>O'chirgichning holati + uni yoqish/o'chirish oqibatlari haqidagi raqamlar.</summary>
    [HttpGet]
    public async Task<ActionResult<GroupLessonsSwitchDto>> Get(CancellationToken ct = default)
    {
        var enabled = await LessonRoster.GroupLessonsEnabledAsync(db, ct);
        var groups = await db.StudyGroups.CountAsync(g => !g.IsArchived, ct);
        var templates = await db.ScheduleTemplates
            .CountAsync(t => t.OwnerKind == LessonOwnerKind.Group, ct);
        var assignments = await db.WeekAssignments
            .CountAsync(a => a.OwnerKind == LessonOwnerKind.Group && a.TemplateId != null, ct);
        return new GroupLessonsSwitchDto(enabled, groups, templates, assignments);
    }

    /// <summary>
    /// O'chirgichni yoqish/o'chirish. FAQAT tizim egasi (<c>superadmin</c>).
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<ActionResult<GroupLessonsSwitchDto>> Save(
        SaveGroupLessonsSwitchRequest req, CancellationToken ct = default)
    {
        var meta = await db.SchoolMeta.FirstOrDefaultAsync(ct);
        if (meta is null)
        {
            // Qator hali yo'q (yangi o'rnatilgan maktab) — avval ALOHIDA yoziladi.
            // `SettingsController.SaveGeneral` bilan bir xil sabab: `SchoolMeta` da
            // baza DEFAULT'i `true` bo'lgan bayroqlar bor va EF yangi qatorda CLR
            // sukutiga teng qiymatni INSERT'dan tushirib qoldiradi.
            meta = new SchoolMeta();
            db.SchoolMeta.Add(meta);
            await db.SaveChangesAsync(ct);
        }

        var before = meta.GroupLessonsEnabled;
        if (before != req.Enabled)
        {
            meta.GroupLessonsEnabled = req.Enabled;
            audit.Record(AuditService.EntityStudyGroup, "group-lessons-switch", "update",
                req.Enabled
                    ? "Guruh darslari YOQILDI — guruh jadvallari haftaga biriktirilishi mumkin; "
                      + "jurnal, davomat, hisobot, maosh va turniket endi guruh darslarini ham ko'radi."
                    : "Guruh darslari O'CHIRILDI — guruh jadvallari haftaga biriktirilmaydi; "
                      + "yozib bo'lingan jurnal qatorlari joyida qoladi.",
                before: new { GroupLessonsEnabled = before },
                after: new { meta.GroupLessonsEnabled });
            await db.SaveChangesAsync(ct);
        }

        return await Get(ct);
    }
}
