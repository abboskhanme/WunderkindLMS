using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Qarzdorlar bilan ISHLASH (docs/modules/existing-module-gaps.md §3.5):
/// rangli holat ma'lumotnomasi, amallar tarixi va buzilgan va'dalar.
///
/// <para>
/// <b>RUXSAT (SPEC §4.3).</b> Klass darajasida ikki qavat:
/// <c>[Authorize(Roles = Roles.FinanceStaff)]</c> va
/// <c>[FinanceRole(FinanceAction.ViewBillingReports)]</c> — ya'ni
/// <c>admin</c> va <c>superadmin</c>. <b>Kassir 403 oladi</b>, ham o'qishda,
/// ham yozishda. Sabab qat'iy: qarzdorlar ro'yxati butun maktabning kim
/// qancha qarzdorligini ochadi, va "kim to'lamayapti" ma'lumoti kassaning
/// kundalik ishiga kerak emas. Yozish esa o'qishdan ham torroq bo'lishi
/// kerak edi — lekin §4.3 matritsasida bundan tor qator yo'q (faqat
/// direktorga tegishli amallar boshqa ma'noda), shuning uchun ikkovi ham
/// <c>ViewBillingReports</c> ostida: <b>kassir hech qaysi endpoint'ga
/// kira olmaydi</b>.
/// </para>
/// <para>
/// <b>Nega alohida controller.</b> <see cref="FinanceReportsController"/>
/// (qarzdorlar RO'YXATI, P&amp;L, pivot) boshqa vazifaga tegishli va boshqa
/// agent qo'lida. Yo'l bir xil (<c>api/admin/finance</c>), segmentlar boshqa
/// (<c>debtor-statuses</c>, <c>debtors/workflow</c>, <c>debtor-actions</c>) —
/// marshrut to'qnashuvi yo'q. Xuddi shu naqsh
/// <see cref="FinanceFlagsController"/> da ham ishlatilgan.
/// </para>
/// <para>
/// <b>Nega xizmat DI'dan olinmaydi.</b> <c>Program.cs</c> ga tegilmaydi
/// (parallel vazifalar konflikt maydoni); xizmat holatsiz va faqat
/// <c>AppDbContext</c> ga bog'liq, shuning uchun so'rov doirasida shu yerda
/// yaratiladi — <see cref="FinanceReportsController"/> bilan bir xil.
/// </para>
/// <para>
/// <b>HAQIQIY DELETE YO'Q.</b> <c>DELETE</c> fe'li ikki joyda bor va ikkovi
/// ham qatorni O'CHIRMAYDI: amal uchun <c>deleted_at</c> qo'yiladi, holat
/// uchun <c>is_active = false</c>. §3.5: yo'qolmasligi kerak bo'lgan narsa —
/// nima va'da qilingani.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[FinanceRole(FinanceAction.ViewBillingReports)]
[Route("api/admin/finance")]
[Produces("application/json")]
[BillingFault]
public sealed class DebtorWorkflowController(AppDbContext db) : ControllerBase
{
    private readonly DebtorWorkflowService _workflow = new(db);
    private readonly BrokenPromiseScan _promises = new(db);

    // =====================================================================
    //  Holat ma'lumotnomasi
    // =====================================================================

    /// <summary>
    /// Holatlar katalogi, <c>position</c> bo'yicha.
    /// </summary>
    /// <param name="includeInactive">true = katalogdan chiqarilganlar ham
    /// (sozlamalar ekrani). Sukut false — "Amal qo'shish" oynasi faqat
    /// faollarni ko'rsatadi.</param>
    [HttpGet("debtor-statuses")]
    public async Task<ActionResult<IEnumerable<DebtorStatusDto>>> Statuses(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await _workflow.StatusesAsync(includeInactive, ct));

    /// <summary>Yangi holat qo'shadi. Nom takrorlansa — 409.</summary>
    [HttpPost("debtor-statuses")]
    public async Task<ActionResult<DebtorStatusDto>> CreateStatus(
        [FromBody] SaveDebtorStatusRequest request, CancellationToken ct = default) =>
        Ok(await _workflow.CreateStatusAsync(request, Actor, ct));

    /// <summary>Holatni tahrirlaydi (nomi, rangi, izohi, tartibi, faolligi).</summary>
    [HttpPut("debtor-statuses/{id:guid}")]
    public async Task<ActionResult<DebtorStatusDto>> UpdateStatus(
        Guid id, [FromBody] SaveDebtorStatusRequest request, CancellationToken ct = default) =>
        Ok(await _workflow.UpdateStatusAsync(id, request, Actor, ct));

    /// <summary>
    /// Holatni katalogdan CHIQARADI — <c>is_active = false</c>. Qator
    /// o'chirilmaydi va eski amallarda ko'rinib turaveradi.
    /// </summary>
    [HttpDelete("debtor-statuses/{id:guid}")]
    public async Task<ActionResult<DebtorStatusDto>> RetireStatus(
        Guid id, CancellationToken ct = default) =>
        Ok(await _workflow.RetireStatusAsync(id, Actor, ct));

    // =====================================================================
    //  Amallar
    // =====================================================================

    /// <summary>
    /// Qarzdorlar ro'yxatining yangi ustunlari: joriy holat, oxirgi amal,
    /// va'da sanasi va "va'da buzildimi".
    ///
    /// <para>
    /// Qarz summasi bu javobda YO'Q — u <c>GET /admin/finance/debtors</c> da
    /// va ekranda <c>studentId</c> bo'yicha birlashtiriladi. Faqat kamida
    /// bitta tirik amali bor o'quvchilar qaytadi.
    /// </para>
    /// </summary>
    /// <param name="className">Sinf bo'yicha filtr (aniq moslik).</param>
    [HttpGet("debtors/workflow")]
    public async Task<ActionResult<IEnumerable<DebtorWorkflowRowDto>>> Workflow(
        [FromQuery] string? className, CancellationToken ct = default) =>
        Ok(await _workflow.RowsAsync(className, ct));

    /// <summary>
    /// Buzilgan va'dalar: sana o'tib ketgan, qarz hali ochiq. Direktor
    /// panelida beshinchi anomaliya sifatida ham ko'rinadi
    /// (<c>GET /admin/finance/flags</c>), bu yerda esa qarzdor kesimida
    /// batafsil.
    /// </summary>
    /// <param name="limit">Qatorlar chegarasi (1..500, sukut 200).</param>
    [HttpGet("debtors/broken-promises")]
    public async Task<ActionResult<IEnumerable<BrokenPromiseDto>>> BrokenPromises(
        [FromQuery] int limit = 200, CancellationToken ct = default) =>
        Ok(await _promises.FindAsync(limit, ct));

    /// <summary>Bitta o'quvchining amallari tarixi (o'chirilganlarsiz), eng yangisidan.</summary>
    [HttpGet("debtors/{studentId}/actions")]
    public async Task<ActionResult<IEnumerable<DebtorActionDto>>> Actions(
        string studentId, CancellationToken ct = default) =>
        Ok(await _workflow.ActionsAsync(studentId, ct));

    /// <summary>
    /// "Amal qo'shish" — izoh (majburiy), holat va va'da sanasi (ixtiyoriy).
    ///
    /// <para>
    /// Kim yozayotgani so'rov tanasidan EMAS, JWT'dan olinadi (SPEC §4.4).
    /// Bo'sh izoh — 400, noma'lum o'quvchi yoki holat — 404, katalogdan
    /// chiqarilgan holat — 400.
    /// </para>
    /// </summary>
    [HttpPost("debtors/{studentId}/actions")]
    public async Task<ActionResult<DebtorActionDto>> AddAction(
        string studentId, [FromBody] CreateDebtorActionRequest request,
        CancellationToken ct = default) =>
        Ok(await _workflow.AddActionAsync(studentId, request, Actor, ct));

    /// <summary>
    /// Amalni YUMSHOQ o'chiradi (<c>deleted_at</c>). Qator bazada qoladi —
    /// va'da tarixi yo'qolmasligi kerak (§3.5). Allaqachon o'chirilgan
    /// bo'lsa — 409.
    /// </summary>
    [HttpDelete("debtor-actions/{id:guid}")]
    public async Task<ActionResult<DebtorActionDto>> DeleteAction(
        Guid id, CancellationToken ct = default) =>
        Ok(await _workflow.DeleteActionAsync(id, Actor, ct));

    /// <summary>Joriy foydalanuvchi id'si — SPEC §4.4 (topilmasa so'rov yiqiladi).</summary>
    private string Actor => FinanceActor.RequireUserId(User);
}
