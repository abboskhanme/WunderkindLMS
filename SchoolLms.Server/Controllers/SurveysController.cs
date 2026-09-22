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
/// Arizalar (ommaviy qabul formalari) — ADMIN CRUD.
/// <c>docs/modules/sales-marketing.md</c> §5.2. Vazifa: SM-3.
///
/// <para>
/// <b>Ruxsat — <c>marketing</c>.</b> <see cref="AdminPermAttribute"/>:
/// admin/superadmin — cheklovsiz; xodim (staff) GET/HEAD/OPTIONS ni HAR DOIM
/// o'qiy oladi, YOZISH uchun esa <c>marketing</c> claim'i kerak; o'qituvchi,
/// o'quvchi, ota-ona va kassir — umuman rad etiladi. §8.1 shu jadvalning
/// oltita qatorini aynan shu xulq bo'yicha tekshiradi.
/// </para>
///
/// <para>
/// <b>Ommaviy sahifa bu yerda EMAS.</b> <c>/api/public/surveys/{slug}</c>
/// (§5.1) — SM-2 ning <c>PublicSurveyController</c> i, <c>[AllowAnonymous]</c>
/// va tezlik chegarasi bilan. Bu controller'da anonim yo'l YO'Q.
/// </para>
///
/// <para>
/// <b>Uchta qoida, uchtasi ham bazada yozilgan va uchtasi ham shu yerda
/// TUSHUNTIRILADI</b> (§5.6 kodlari, xom SQLSTATE emas):
/// slug shakli — 400 <c>validation</c>; slug bandligi
/// (<c>ux_surveys_slug</c>, <c>lower(slug)</c>) — 409 <c>slug_taken</c>;
/// topshiriqli arizani o'chirish (<c>on delete restrict</c>) — 409
/// <c>survey_in_use</c> (D7). Qoidalarning o'zi <see cref="SurveyService"/> da.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("marketing")]
[Route("api/admin/surveys")]
public class SurveysController(AppDbContext db, AuditService audit, IConfiguration config)
    : ControllerBase
{
    private string Uid =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";

    /// <summary>
    /// Arizalar ro'yxati, yangisi tepada. Har bir qatorda topshiriqlar soni va
    /// oxirgi ariza sanasi bor (§2.7).
    /// </summary>
    /// <param name="includeInactive">true = yopilganlar ham
    /// ("Yopilganlarni ko'rsatish" tugmasi). Sukut bo'yicha faqat faollar.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SurveyDto>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var q = db.Surveys.AsNoTracking();
        if (!includeInactive) q = q.Where(s => s.IsActive);

        var rows = await q.OrderByDescending(s => s.CreatedAt).ToListAsync(ct);

        // Hisoblagichlar — BITTA guruhlangan so'rov, ariza soniga bog'liq emas
        // (N+1 yo'q; sabab SurveyService.CountersAsync izohida).
        var counters = await SurveyService.CountersAsync(db, ct);

        return rows
            .Select(s => SurveyService.ToDto(
                s,
                counters.GetValueOrDefault(s.Id, SurveyService.Counters.Empty),
                PublicUrlFor(s.Slug)))
            .ToList();
    }

    /// <summary>Bitta ariza — tahrir formasi uchun. Yopilgani ham qaytadi.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SurveyDto>> GetOne(Guid id, CancellationToken ct = default)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound(new { code = "survey_not_found", message = NotFoundMessage });

        var counters = await SurveyService.CountersAsync(db, id, ct);
        return SurveyService.ToDto(survey, counters, PublicUrlFor(survey.Slug));
    }

    /// <summary>
    /// Slug bandmi — forma yozayotganda so'raydi (§5.2).
    ///
    /// <para>
    /// Shakli buzuq slug ham <c>available: false</c> beradi: uni baribir
    /// saqlab bo'lmaydi, "bo'sh" deb ko'rsatish esa xodimni aldardi. Javob
    /// MASLAHAT, hakam emas — haqiqiy hakam <c>ux_surveys_slug</c>, va
    /// poygada saqlash 409 <c>slug_taken</c> qaytaradi.
    /// </para>
    /// </summary>
    /// <param name="excludeId">Tahrirlanayotgan arizaning o'zi — o'z slug'i band ko'rinmasin.</param>
    [HttpGet("slug-available")]
    public async Task<ActionResult<SlugAvailabilityDto>> SlugAvailable(
        [FromQuery] string? slug, [FromQuery] Guid? excludeId = null,
        CancellationToken ct = default)
    {
        var normalized = SurveyService.NormalizeSlug(slug);
        if (SurveyService.SlugProblem(normalized) is not null) return new SlugAvailabilityDto(false);

        var taken = await SurveyService.SlugTakenAsync(db, normalized, excludeId, ct);
        return new SlugAvailabilityDto(!taken);
    }

    /// <summary>Yangi ariza. Javob — 200 va to'liq <see cref="SurveyDto"/>.</summary>
    [HttpPost]
    public async Task<ActionResult<SurveyDto>> Create(
        SurveySaveRequest p, CancellationToken ct = default)
    {
        var check = await SurveyService.ValidateAsync(db, p, exceptId: null, ct);
        if (!check.Ok) return Invalid(check);
        if (await SurveyService.SlugTakenAsync(db, check.Slug, exceptId: null, ct)) return SlugTaken();

        var survey = new Survey { CreatedBy = Uid };
        Apply(survey, p, check.Slug);
        db.Surveys.Add(survey);

        audit.Record(SurveyService.AuditEntity, survey.Id.ToString(), "create",
            $"Ariza yaratildi: «{survey.Name}»", after: Snapshot(survey));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SurveyService.IsSlugTakenViolation(ex))
        {
            return SlugTaken();
        }

        // Yangi arizada topshiriq bo'lishi mumkin emas — bazaga ortiqcha so'rov yubormaymiz.
        return SurveyService.ToDto(survey, SurveyService.Counters.Empty, PublicUrlFor(survey.Slug));
    }

    /// <summary>
    /// Arizani tahrirlash. <c>isActive</c> BU YERDA o'zgarmaydi — buning uchun
    /// alohida <see cref="SetActive"/> bor (§5.2): formani saqlash yopiq
    /// arizani bexosdan ochib yubormasin.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SurveyDto>> Update(
        Guid id, SurveySaveRequest p, CancellationToken ct = default)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound(new { code = "survey_not_found", message = NotFoundMessage });

        var check = await SurveyService.ValidateAsync(db, p, exceptId: id, ct);
        if (!check.Ok) return Invalid(check);
        if (await SurveyService.SlugTakenAsync(db, check.Slug, exceptId: id, ct)) return SlugTaken();

        var before = Snapshot(survey);
        Apply(survey, p, check.Slug);
        survey.UpdatedAt = AppClock.NowInstant;

        audit.Record(SurveyService.AuditEntity, survey.Id.ToString(), "update",
            $"Ariza tahrirlandi: «{survey.Name}»", before: before, after: Snapshot(survey));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SurveyService.IsSlugTakenViolation(ex))
        {
            return SlugTaken();
        }

        var counters = await SurveyService.CountersAsync(db, id, ct);
        return SurveyService.ToDto(survey, counters, PublicUrlFor(survey.Slug));
    }

    /// <summary>
    /// Faol/yopiq tugmasi. Yopiq arizaning ommaviy sahifasi 404 beradi (D8), va
    /// bu — topshiriqli arizani "o'chirish" ning YAGONA to'g'ri yo'li (D7).
    /// </summary>
    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetActive(
        Guid id, SurveyActiveRequest req, CancellationToken ct = default)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound(new { code = "survey_not_found", message = NotFoundMessage });

        // Holat o'zgarmasa — na yozuv, na audit qatori: jurnal hech narsa
        // bo'lmagan joyda "o'zgardi" deb yozmasligi kerak.
        if (survey.IsActive != req.IsActive)
        {
            survey.IsActive = req.IsActive;
            survey.UpdatedAt = AppClock.NowInstant;

            audit.Record(SurveyService.AuditEntity, survey.Id.ToString(),
                req.IsActive ? "activate" : "deactivate",
                (req.IsActive ? "Ariza qayta ochildi: " : "Ariza yopildi: ") + $"«{survey.Name}»",
                before: new { IsActive = !req.IsActive }, after: new { req.IsActive });

            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>
    /// Arizani o'chirish — FAQAT birorta topshiriq tushmagan bo'lsa (D7).
    /// Aks holda 409 <c>survey_in_use</c> va nima qilish kerakligi: uni
    /// o'chirish emas, "faol emas" qilib qo'yish.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (survey is null) return NotFound(new { code = "survey_not_found", message = NotFoundMessage });

        if (await SurveyService.SubmissionCountAsync(db, id, ct) > 0) return InUse();

        db.Surveys.Remove(survey);

        audit.Record(SurveyService.AuditEntity, survey.Id.ToString(), "delete",
            $"Ariza o'chirildi: «{survey.Name}»", before: Snapshot(survey));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SurveyService.IsInUseViolation(ex))
        {
            // Poyga: tekshiruv bilan DELETE orasida ariza topshiriq (yoki lid)
            // oldi. FK `restrict` ushlab qoldi — xodimga 23503 emas, o'sha gap.
            return InUse();
        }

        return NoContent();
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private const string NotFoundMessage = "Ariza topilmadi";

    /// <summary>
    /// Ota-ona ochadigan havola — APEKS domendan (<c>Tenancy:RootDomain</c>),
    /// so'rov hostidan emas: admin <c>test.</c> subdomenida o'tiradi (§5.2).
    /// </summary>
    private string PublicUrlFor(string slug) => SurveyService.PublicUrl(
        config["Tenancy:RootDomain"], Request.Scheme, Request.Host.Value, slug);

    /// <summary>
    /// Ikki xil 400 — §5.6 da ular ikki xil kod. Rule T BIRINCHI tekshiriladi:
    /// klient aynan <c>survey_fields_required</c> ni ushlab qulflangan
    /// tugmalarni qaytarib yoqadi (SurveyFormModal.tsx), <c>validation</c> esa
    /// unga maydon nomlarini beradi.
    /// </summary>
    private BadRequestObjectResult Invalid(SurveyService.Result check)
    {
        if (check.RuleTBroken)
            return BadRequest(new
            {
                code = "survey_fields_required",
                fields = check.PinnedFields,
                message = SurveyService.RequiredTogglesMessage(check.PinnedFields),
            });

        return BadRequest(new
        {
            code = "validation",
            message = SurveyService.ValidationMessage,
            errors = check.Errors,
        });
    }

    private ConflictObjectResult SlugTaken() =>
        Conflict(new { code = "slug_taken", message = SurveyService.SlugTakenMessage });

    private ConflictObjectResult InUse() =>
        Conflict(new { code = "survey_in_use", message = SurveyService.InUseMessage });

    /// <summary>
    /// Formadan kelgan qiymatlarni entity'ga ko'chiradi. <c>IsActive</c> va
    /// <c>CreatedBy</c> BU YERDA yo'q — ular so'rov tanasidan kelmaydi.
    /// </summary>
    private static void Apply(Survey s, SurveySaveRequest p, string slug)
    {
        s.Name = (p.Name ?? "").Trim();
        s.Slug = slug;
        s.Subtitle = Clean(p.Subtitle);
        s.ImageUrl = Clean(p.ImageUrl);
        s.OfferUrl = Clean(p.OfferUrl);
        s.ThankYouText = Clean(p.ThankYouText);
        s.StageId = Clean(p.StageId);
        s.ShowStudentFirstNameInput = p.ShowStudentFirstNameInput;
        s.ShowStudentLastNameInput = p.ShowStudentLastNameInput;
        s.ShowStudentPhoneNumberInput = p.ShowStudentPhoneNumberInput;
        s.ShowStudentGradeInput = p.ShowStudentGradeInput;
        s.ShowStudentGenderInput = p.ShowStudentGenderInput;
    }

    /// <summary>Bo'sh (yoki faqat bo'shliqdan iborat) matn — <c>null</c>, "" emas.</summary>
    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Audit jurnalidagi before/after — ariza qatorining o'qiladigan kesimi.</summary>
    private static object Snapshot(Survey s) => new
    {
        s.Name,
        s.Slug,
        s.Subtitle,
        s.ImageUrl,
        s.OfferUrl,
        s.ThankYouText,
        s.StageId,
        s.ShowStudentFirstNameInput,
        s.ShowStudentLastNameInput,
        s.ShowStudentPhoneNumberInput,
        s.ShowStudentGradeInput,
        s.ShowStudentGenderInput,
        s.IsActive,
    };
}
