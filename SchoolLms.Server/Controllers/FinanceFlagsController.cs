using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Tungi tekshiruv bayroqlari — direktor panelidagi "yopilmagan nomuvofiqlik"
/// hisoblagichi (SPEC §4.6). Vazifa: P1-14.
///
/// <para>
/// <b>BU CONTROLLER'DA DELETE YO'Q VA BO'LMAYDI.</b> SPEC §4.6: hisoblagichni
/// "bekor qilib bo'lmaydi, faqat yozma sabab bilan yopiladi". Bayroqni
/// o'chirish imkoni — aynan shu himoyani ma'nosiz qiladigan yagona narsa.
/// Uch qavat: (1) bu yerda endpoint yo'q; (2) <see cref="IAnomalyService"/> da
/// o'chirish metodi yo'q; (3) <c>app_rw</c> rolida <c>finance_anomaly_flags</c>
/// ga DELETE/TRUNCATE huquqi yo'q va UPDATE faqat uchta "yopish" ustuniga
/// berilgan (<c>Migrations/Sql/anomaly_guards.sql</c>). Uchinchisi hal
/// qiluvchi: birinchi ikkitasini kod yozib chetlab o'tish mumkin, uchinchisini
/// yo'q.
/// </para>
///
/// <para>
/// <b>RUXSAT (SPEC §4.3).</b> Klass darajasida
/// <c>[Authorize(Roles = Roles.FinanceStaff)]</c> — <c>admin</c> va
/// <c>superadmin</c>. Kassir bu yerga UMUMAN kira olmaydi (403): bayroqlarning
/// aksariyati aynan kassirning ishi haqida, va tekshirilayotgan odam
/// tekshiruvni yopa olmasligi kerak. Amal darajasidagi ikkinchi qavat —
/// <see cref="FinanceRoleAttribute"/>, SPEC §4.3 matritsasidagi
/// <see cref="FinanceAction.ViewVarianceReport"/> qatori.
/// </para>
///
/// <para>
/// Yo'l qo'shnisi <see cref="FinanceReportsController"/> bilan bir xil
/// (<c>api/admin/finance</c>), lekin segmentlar boshqa (<c>flags*</c>) —
/// marshrut to'qnashuvi yo'q. Alohida fayl: hisobotlar va tekshiruv ikki xil
/// vazifa (P1-13 va P1-14), ularni bitta faylga qo'shish parallel
/// agentlarga konflikt maydonini berardi.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/finance")]
[Produces("application/json")]
[BillingFault]
public sealed class FinanceFlagsController(IAnomalyService anomalies) : ControllerBase
{
    /// <summary>
    /// Bayroqlar va hisoblagichlar. Direktor paneli
    /// <c>?unresolved=true</c> bilan chaqiradi va
    /// <see cref="AnomalyFlagsDto.Unresolved"/> ni ko'rsatadi.
    ///
    /// <para>
    /// Hisoblagichlar HAR DOIM to'liq: ular ro'yxatdan emas, alohida
    /// guruhlangan so'rovdan keladi. Ya'ni <c>limit</c> yoki <c>kind</c>
    /// filtri hisoblagichni kamaytirmaydi — SPEC §4.6 dagi "bekor qilib
    /// bo'lmaydigan hisoblagich" aynan shuni talab qiladi.
    /// </para>
    /// </summary>
    /// <param name="unresolved">true = ro'yxatda faqat yopilmaganlar.</param>
    /// <param name="kind">Tur bo'yicha filtr: <c>shift_variance</c>,
    /// <c>fast_reversal</c>, <c>off_hours_payment</c>,
    /// <c>paid_without_allocation</c>. Noma'lum qiymat — 400.</param>
    /// <param name="limit">Ro'yxat uzunligi (1..500, sukut 200).</param>
    [HttpGet("flags")]
    [FinanceRole(FinanceAction.ViewVarianceReport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnomalyFlagsDto>> Flags(
        [FromQuery] bool unresolved = false,
        [FromQuery] string? kind = null,
        [FromQuery] int limit = 200,
        CancellationToken ct = default) =>
        Ok(await anomalies.ListAsync(unresolved, kind, limit, ct));

    /// <summary>
    /// Bayroqni YOZMA SABAB bilan yopadi (SPEC §4.6).
    ///
    /// <para>
    /// Bo'sh yoki faqat probeldan iborat sabab — <b>400</b>; allaqachon
    /// yopilgan bayroq — <b>409</b>. Yopgan shaxs va vaqt SERVERDA aniqlanadi
    /// (SPEC §4.4), so'rov tanasida ular uchun maydon yo'q.
    /// </para>
    /// </summary>
    [HttpPost("flags/{id:guid}/resolve")]
    [FinanceRole(FinanceAction.ViewVarianceReport)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AnomalyFlagDto>> Resolve(
        Guid id, [FromBody] ResolveAnomalyRequest request, CancellationToken ct) =>
        // `request` null bo'lishi mumkin (bo'sh tana) — xizmat baribir bo'sh
        // sababni rad etadi, shuning uchun bu yerda alohida tekshiruv yo'q.
        Ok(await anomalies.ResolveAsync(
            id, request?.Reason ?? string.Empty, FinanceActor.RequireUserId(User), ct));

    /// <summary>
    /// Tekshiruvni QO'LDA yurgizish. Odatda kerak emas — fon xizmati har tunda
    /// 03:00 da o'zi yuradi — lekin direktor "hozir tekshir" deganda kutish
    /// noto'g'ri bo'lardi.
    ///
    /// <para>
    /// Amal <b>idempotent</b>: takroriy chaqiruv dublikat bayroq yaratmaydi
    /// (<c>ux_finance_anomaly_flags_kind_ref</c>). Shuning uchun tugmani ikki
    /// marta bosish xavfsiz.
    /// </para>
    /// </summary>
    [HttpPost("flags/scan")]
    [FinanceRole(FinanceAction.ViewVarianceReport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnomalyScanResult>> Scan(CancellationToken ct) =>
        // AYNAN fon xizmati chaqiradigan metod — tekshiruvning ikkita
        // implementatsiyasi bo'lmasligi uchun. `AnomalyScanService` ning o'zi
        // bu yerda kerak emas: u faqat scope ochib, natijani jurnalga yozadi,
        // ikkalasi ham so'rov yo'lida allaqachon bor.
        Ok(await anomalies.ScanAsync(ct: ct));
}
