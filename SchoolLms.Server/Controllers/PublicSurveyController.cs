using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Ommaviy ariza formasi — <c>/ariza/{slug}</c> sahifasining ikkita anonim
/// endpoint'i (docs/modules/sales-marketing.md §5.1, vazifa SM-2).
///
/// <para>
/// <b>Shakli <c>GpsIngestController</c> dan:</b> <c>[AllowAnonymous]</c>
/// sinf darajasida, chunki bu yerdagi HAR IKKALA amal ham sessiyasiz.
/// Sahifani ota-ona Instagram yoki Telegram havolasidan ochadi — unda token
/// ham, foydalanuvchi ham yo'q.
/// </para>
///
/// <para>
/// <b>Bu — mahsulotdagi birinchi anonim YOZUV endpoint'i</b>, shuning uchun
/// nimaga tegishi qat'iy chegaralangan: <c>surveys</c>, <c>survey_submissions</c>,
/// <c>leads</c> va — faqat doska butunlay bo'sh bo'lganda (Rule S 3-qadami) —
/// bitta <c>lead_stages</c> qatori. Mavjud biror odam yoki yozuv haqida hech
/// qachon hech nima qaytarmaydi.
/// </para>
///
/// <para>
/// <b>Qoidalar bu yerda emas.</b> Kontroller faqat HTTP ni biladi: so'rovni
/// oladi, <see cref="SurveySubmissionService"/> ga beradi, natijaga qarab
/// status tanlaydi. Honeypot, vaqt tuzog'i, takror va Rule S — xizmatda
/// (§2.5, §2.6).
/// </para>
///
/// <para>
/// <b>Chastota chegarasi siyosatlarini SM-12 ro'yxatdan o'tkazadi</b>
/// (<c>Program.cs</c> — umumiy fayl, §7.1). Bu yerda faqat atributlar turadi:
/// <c>survey-read</c> — 60/daqiqa (sahifa ochilishi), <c>survey</c> — 5/10
/// daqiqa (topshiriq). Rad javobi global 429 (§2.5).
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/surveys")]
public class PublicSurveyController(SurveySubmissionService surveys) : ControllerBase
{
    /// <summary>
    /// Sahifa chizadigan hamma narsa (§5.1). Noma'lum slug ham, yopilgan
    /// ariza ham — AYNAN BIR XIL 404.
    /// </summary>
    [HttpGet("{slug}")]
    [EnableRateLimiting("survey-read")]
    public async Task<ActionResult<PublicSurveyDto>> Get(string slug, CancellationToken ct)
    {
        var survey = await surveys.FindActiveAsync(slug, ct);
        if (survey is null) return SurveyNotFound();

        return SurveySubmissionService.ToPublicDto(survey);
    }

    /// <summary>
    /// Topshiriqni qabul qiladi (§2.6). 200 tanasi to'rtta holat uchun bir
    /// xil: haqiqiy ariza, takror, honeypot va vaqt tuzog'i (§5.1).
    /// </summary>
    [HttpPost("{slug}")]
    [EnableRateLimiting("survey")]
    public async Task<ActionResult<PublicSurveySubmitResultDto>> Submit(
        string slug, PublicSurveySubmitRequest req, CancellationToken ct)
    {
        var survey = await surveys.FindActiveAsync(slug, ct);
        if (survey is null) return SurveyNotFound();

        var outcome = await surveys.SubmitAsync(survey, req, ClientIp(), ClientUserAgent(), ct);

        switch (outcome.Kind)
        {
            case SurveySubmitKind.Invalid:
                return BadRequest(new
                {
                    code = SurveySubmissionService.CodeValidation,
                    message = SurveySubmissionService.ValidationMessage,
                    errors = outcome.Errors,
                });

            // Rule T (§2.4): bazadagi `ck_surveys_required_toggles` buni
            // ushlab turadi, ya'ni amalda bu tarmoq ishlamaydi. U shunchaki
            // 500 o'rniga o'qiladigan javob beradi.
            case SurveySubmitKind.FieldsRequired:
                return BadRequest(new
                {
                    code = SurveySubmissionService.CodeFieldsRequired,
                    fields = outcome.Fields,
                    message = SurveySubmissionService.FieldsRequiredMessage(outcome.Fields),
                });

            default:
                return Ok(new PublicSurveySubmitResultDto(true, outcome.ThankYou));
        }
    }

    /// <summary>
    /// Ikkala endpoint uchun YAGONA 404 tanasi. Bitta joyda turishi shart:
    /// noma'lum slug va yopilgan ariza javobi bir-biridan farq qilsa,
    /// ommaviy endpoint havola qachondir mavjud bo'lganini tasdiqlagan
    /// bo'lardi (§5.1).
    /// </summary>
    private ActionResult SurveyNotFound() => NotFound(new
    {
        code = SurveySubmissionService.CodeNotFound,
        message = SurveySubmissionService.NotFoundMessage,
    });

    /// <summary>
    /// So'rovning IP'si — <c>survey_submissions.ip</c> uchun (§4.2: faqat
    /// suiste'molni tekshirish, faqat admin detal panelida).
    ///
    /// <para>
    /// <c>X-Forwarded-For</c> QO'LDA o'qilmaydi: <c>Program.cs</c> da
    /// <c>UseForwardedHeaders</c> yoqilgan, ya'ni bu qiymat proksi ortida
    /// ham haqiqiy klient manzili (§2.5). Ikkinchi marta tahlil qilish —
    /// soxtalashtirilgan sarlavhaga ishonish degani.
    /// </para>
    /// </summary>
    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Brauzer satri; kesish va bo'sh qiymatni <c>null</c> qilish — xizmatda.</summary>
    private string? ClientUserAgent() => Request.Headers.UserAgent.ToString();
}
