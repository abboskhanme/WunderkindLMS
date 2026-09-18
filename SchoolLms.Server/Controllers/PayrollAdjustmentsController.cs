using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Hr;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  BONUS / JARIMA endpoint'lari — F11.01, F11.02
//  (docs/modules/finance-parity.md §2.11: `/api/admin/hr/adjustments`,
//  `/api/admin/hr/adjustment-reasons`).
// ===========================================================================
//
//  BU CONTROLLERDA TAHRIRLASH VA O'CHIRISH YO'Q — VA BO'LMAYDI (registr uchun)
//  ------------------------------------------------------------------------
//  `payroll_adjustments` bazada FAQAT QO'SHILADI (`app_rw` da UPDATE/DELETE
//  yo'q, finance-parity §3.2 B3). Xato yozuv `POST {id}/reverse` bilan
//  tuzatiladi. Sabab katalogi (`adjustment_reasons`) esa moliyaviy EMAS —
//  PUT bilan tahrirlanadi.
//
//  RUXSAT (hr.md §5.4)
//  --------------------
//  Klass darajasi: `[Authorize(Roles = Roles.FinanceStaff)]` — admin va
//  direktor. KASSIR VA ODDIY XODIM BU YERGA UMUMAN KIRMAYDI: bu xodimning
//  pulini o'zgartiradigan amal, `AdminPerm("teachers")` EMAS — F3.05
//  xatosining aynan o'zi (task topshirig'i). Metod darajasida
//  `[FinanceRole(FinanceAction.ManagePayrollAdjustments)]`.
// ===========================================================================

/// <summary>
/// Bonus/jarima: yozish, ro'yxat, storno va sabab katalogi. Batafsil: fayl
/// boshidagi izoh.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/hr")]
[BillingFault]
[Produces("application/json")]
public sealed class PayrollAdjustmentsController(IAppDbContext db) : ControllerBase
{
    /// <summary>
    /// Nega xizmat DI'dan emas, shu yerda quriladi: `Program.cs` bu
    /// vazifaning orqasidan boruvchi bir nechta agent bilan bo'lishiladigan
    /// fayl (task topshirig'i — uni tahrirlamaslik). Xizmatning bog'liqligi
    /// (`IAppDbContext`) allaqachon konteynerda, shuning uchun uni shu yerda
    /// yig'ish hech narsani yashirmaydi (`CashHandoversController` dagi bilan
    /// bir xil naqsh).
    /// </summary>
    private readonly IPayrollAdjustmentService adjustments = new PayrollAdjustmentService(db);

    /// <summary>
    /// SPEC §4.4 — SERVER ANIQLAYDIGAN SHAXS. Bu nomlar so'rov tanasida
    /// uchrasa, so'rov rad etiladi.
    /// </summary>
    private static readonly string[] ServerDerivedFields =
    [
        "createdBy", "createdByName", "createdAt", "reversalOf", "reversed",
        "employeeName", "reasonName",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------
    //  Bonus/jarima registri (F11.01)
    // -----------------------------------------------------------------

    /// <summary>Registr (yangisidan eskisiga). <c>kind=bonus|penalty</c> ikkita ekranni ajratadi.</summary>
    [HttpGet("adjustments")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<IEnumerable<PayrollAdjustmentDto>>> List(
        [FromQuery] string? kind,
        [FromQuery] string? employeeKind,
        [FromQuery] string? employeeId,
        [FromQuery] short? periodYear,
        [FromQuery] short? periodMonth,
        CancellationToken ct)
    {
        var query = new PayrollAdjustmentQuery(kind, employeeKind, employeeId, periodYear, periodMonth);
        return Ok(await adjustments.ListAsync(query, ct));
    }

    /// <summary>Bitta yozuv.</summary>
    [HttpGet("adjustments/{id:guid}")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<PayrollAdjustmentDto>> Get(Guid id, CancellationToken ct)
    {
        var row = await adjustments.GetAsync(id, ct);
        return row is null
            ? NotFound(new BillingErrorDto("adjustment_not_found", "Yozuv topilmadi."))
            : Ok(row);
    }

    /// <summary>
    /// Xodimga bonus yoki jarima yozadi. <c>createdBy</c> JWT'dan olinadi;
    /// tanada kelsa — 400 (SPEC §4.4). Davr joriy oydan oldingi bo'lsa — 400
    /// (SPEC §4, "no back-dating").
    /// </summary>
    [HttpPost("adjustments")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<PayrollAdjustmentDto>> Create(
        [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<CreatePayrollAdjustmentRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        var created = await adjustments.CreateAsync(request, FinanceActor.RequireUserId(User), ct);
        return Ok(created);
    }

    /// <summary>
    /// Yozuvni storno qiladi: qarshi qator qo'shiladi, original TEGILMAYDI.
    /// Sabab majburiy. Allaqachon storno bo'lgan yoki o'zi storno bo'lgan
    /// yozuvni yana storno qilish — 409.
    /// </summary>
    [HttpPost("adjustments/{id:guid}/reverse")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<PayrollAdjustmentDto>> Reverse(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<ReverseAdjustmentRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        var reversed = await adjustments.ReverseAsync(
            id, request.Reason ?? string.Empty, FinanceActor.RequireUserId(User), ct);
        return Ok(reversed);
    }

    // -----------------------------------------------------------------
    //  Sabab katalogi (F11.02) — "sozlamalar" bo'limi, xuddi shu xizmatda
    // -----------------------------------------------------------------

    /// <summary>Sabablar ro'yxati. <c>kind</c> berilmasa — ikkalasi ham.</summary>
    [HttpGet("adjustment-reasons")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<IEnumerable<AdjustmentReasonDto>>> ListReasons(
        [FromQuery] string? kind, CancellationToken ct) =>
        Ok(await adjustments.ListReasonsAsync(kind, ct));

    /// <summary>Yangi sabab. Bitta <c>kind</c> ichida nom takrorlanmaydi — 409.</summary>
    [HttpPost("adjustment-reasons")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<AdjustmentReasonDto>> CreateReason(
        [FromBody] JsonElement body, CancellationToken ct)
    {
        var request = Read<CreateAdjustmentReasonRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await adjustments.CreateReasonAsync(request, ct));
    }

    /// <summary>
    /// Sababni tahrirlaydi: nom, tartib, faollik. <c>kind</c> O'ZGARMAYDI —
    /// tanada kelsa e'tiborsiz qoldiriladi (u DTO'da umuman yo'q).
    /// </summary>
    [HttpPut("adjustment-reasons/{id:guid}")]
    [FinanceRole(FinanceAction.ManagePayrollAdjustments)]
    public async Task<ActionResult<AdjustmentReasonDto>> UpdateReason(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var request = Read<UpdateAdjustmentReasonRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await adjustments.UpdateReasonAsync(id, request, ct));
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    private sealed record ReverseAdjustmentRequest(string? Reason);

    private ActionResult? RejectServerDerivedFields(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in body.EnumerateObject())
        {
            if (!ServerDerivedFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            return BadRequest(new BillingErrorDto("identity_in_body",
                $"'{property.Name}' so'rov tanasida yuborilmaydi — uni server JWT'dan "
                + "yoki bazadan aniqlaydi (SPEC §4.4). Maydonni olib tashlang."));
        }

        return null;
    }

    private static T? Read<T>(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return default;
        try { return body.Deserialize<T>(Json); }
        catch (JsonException) { return default; }
    }
}
