using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  O'QUVCHIGA PUL QAYTARISH — F1.05 (finance-parity §2.1.3, §4 — slice S3).
// ===========================================================================
//
//  BU CONTROLLERDA PUT HAM, DELETE HAM YO'Q — VA BO'LMAYDI.
//  --------------------------------------------------------
//  `student_refunds` bazada FAQAT INSERT + to'rtta "qaror" ustuniga BIR
//  MARTALIK UPDATE (`app_rw`, finance-parity §3.1 A3). Xato qaytarim
//  `POST {id}/reverse` + `POST {id}/approve` bilan tuzatiladi: asl qator
//  joyida qoladi, ustiga yangi (storno) qator qo'shiladi.
//
//  XIZMAT DI'DAN EMAS, SHU YERDA QURILADI.
//  ----------------------------------------
//  `CashHandoversController` dagi bilan AYNAN bir xil sabab: registratsiya
//  `Program.cs` da bo'lardi, u esa bu to'lqinda ORKESTRATOR fayli
//  (finance-parity §4 — "shared files, one pass per wave") va S3 unga
//  tegmaydi. Xizmatning hamma bog'liqligi (`IAppDbContext`, `ILedgerService`,
//  `ICashShiftService`) allaqachon konteynerda ro'yxatdan o'tgan, ya'ni uni
//  shu yerda yig'ish hech narsani yashirmaydi va endpoint birinchi kundanoq
//  ishlaydi. Orkestrator keyin bitta qator qo'shsa (`Program.cs`):
//
//    builder.Services.AddScoped<SchoolLms.Application.Billing.IStudentRefundService,
//                               SchoolLms.Application.Billing.StudentRefundService>();
//
//  — konstruktorga `IStudentRefundService` ni kiritish bir qatorlik o'zgarish
//  (docs/PENDING_WIRING.md ga yozilmadi: `CashHandoversController` ham xuddi
//  shu holatda va u yerda ham faylga yozilmagan — inline izoh yetarli edi).
//
//  RUXSAT (SPEC §4.3, §4.5)
//  -------------------------
//  1) Klass darajasi: `[Authorize(Roles = Roles.FinanceStaff)]` — admin va
//     direktor. KASSIR BU YERGA UMUMAN KIRMAYDI: qaytarim so'rovi kassa
//     amali emas, ma'muriy qaror. `teacher`/`staff` ham shu darvozada to'xtaydi
//     — "moliya ruxsati" berilgan `staff` ham (SPEC talab qiladi: "finance-
//     permitted staff get neither" — rol darvozasi buni tekshirmasdan ta'minlaydi).
//  2) Metod darajasi: `[FinanceRole(FinanceAction.RequestRefund)]` — so'rash
//     va storno SO'RASH (admin + direktor); `[FinanceRole(FinanceAction.ApproveRefund)]`
//     — tasdiqlash va rad etish (FAQAT direktor).
// ===========================================================================

/// <summary>
/// Qaytarimlar: so'rash, tasdiqlash, rad etish, storno so'rash va ro'yxat.
/// Batafsil: fayl boshidagi izoh.
///
/// <para>
/// Xato javoblari <c>{ "code": ..., "message": ... }</c> shaklida —
/// <see cref="BillingFaultAttribute"/> orqali, moliya modulining qolgan
/// qismidagi bilan bir xil.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/finance/refunds")]
[BillingFault]
[Produces("application/json")]
public sealed class StudentRefundsController(
    IAppDbContext db, ILedgerService ledger, ICashShiftService shifts) : ControllerBase
{
    private readonly IStudentRefundService refunds = new StudentRefundService(db, ledger, shifts);

    /// <summary>
    /// SPEC §4.4 — SERVER ANIQLAYDIGAN SHAXS/HOLAT. Bu nomlar so'rov tanasida
    /// uchrasa, so'rov <b>rad etiladi</b>; jimgina e'tiborsiz qoldirilmaydi.
    /// </summary>
    private static readonly string[] ServerDerivedFields =
    [
        "requestedBy", "requestedByName", "requestedAt",
        "approvedBy", "approvedByName", "approvedAt",
        "cashShiftId", "rejectedReason", "reversalOf", "reversed", "status",
        "studentName",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------
    //  O'qish
    // -----------------------------------------------------------------

    /// <summary>Ro'yxat: o'quvchi va/yoki holat bo'yicha filtr, yangisidan eskisiga.</summary>
    [HttpGet]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<IEnumerable<StudentRefundDto>>> List(
        [FromQuery] string? studentId, [FromQuery] string? status, CancellationToken ct) =>
        Ok(await refunds.ListAsync(new StudentRefundQuery(studentId, status), ct));

    /// <summary>Bitta qaytarim.</summary>
    [HttpGet("{id:guid}")]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<StudentRefundDto>> Get(Guid id, CancellationToken ct)
    {
        var refund = await refunds.GetAsync(id, ct);
        return refund is null
            ? NotFound(new BillingErrorDto("refund_not_found", "Qaytarim topilmadi."))
            : Ok(refund);
    }

    /// <summary>
    /// O'quvchining JORIY avansi (qaytarimlar ayirilgan) — so'rov formasi
    /// "eng ko'pi shuncha" chegarasini shu yerdan ko'rsatadi.
    /// </summary>
    [HttpGet("students/{studentId}/advance")]
    [FinanceRole(FinanceAction.RequestRefund)]
    public async Task<ActionResult<decimal>> Advance(string studentId, CancellationToken ct) =>
        Ok(await refunds.AdvanceAsync(studentId, ct));

    // -----------------------------------------------------------------
    //  So'rash (admin + direktor)
    // -----------------------------------------------------------------

    /// <summary>
    /// Yangi qaytarim so'raydi. Har doim <c>pending</c> holatda tug'iladi —
    /// pul <see cref="Approve"/> chaqirilmaguncha HECH QAYERGA chiqmaydi.
    /// So'ralgan summa o'quvchining joriy avansidan katta bo'lsa — <b>400</b>
    /// (<c>insufficient_advance</c>).
    /// </summary>
    [HttpPost]
    [FinanceRole(FinanceAction.RequestRefund)]
    public async Task<ActionResult<StudentRefundDto>> RequestRefund(
        [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<RequestStudentRefundRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await refunds.RequestAsync(request, FinanceActor.RequireUserId(User), ct));
    }

    /// <summary>
    /// Allaqachon TASDIQLANGAN qaytarimni bekor qilishni so'raydi — yangi,
    /// <c>pending</c> qator. Hali tasdiqlanmagan yoki rad etilgan qaytarim —
    /// <b>409</b> (<c>not_posted</c>); allaqachon storno so'ralgan bo'lsa —
    /// <b>409</b> (<c>already_has_reversal_request</c>).
    /// </summary>
    [HttpPost("{id:guid}/reverse")]
    [FinanceRole(FinanceAction.RequestRefund)]
    public async Task<ActionResult<StudentRefundDto>> RequestReversal(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<RequestRefundReversalRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await refunds.RequestReversalAsync(
            id, request.Reason ?? string.Empty, FinanceActor.RequireUserId(User), ct));
    }

    // -----------------------------------------------------------------
    //  Qaror (FAQAT direktor, SPEC §4.5)
    // -----------------------------------------------------------------

    /// <summary>
    /// Tasdiqlaydi: oddiy qaytarim jurnalga tushadi, storno so'rovi esa asl
    /// qaytarimning partiyasini teskari qiladi. So'ragan shaxsning o'zi
    /// tasdiqlay olmaydi — <b>403</b> (<c>self_approval</c>). Naqd qaytarim
    /// ochiq smenasiz — <b>409</b> (<c>no_open_shift</c>).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [FinanceRole(FinanceAction.ApproveRefund)]
    public async Task<ActionResult<StudentRefundDto>> Approve(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;
        return Ok(await refunds.ApproveAsync(id, FinanceActor.RequireUserId(User), ct));
    }

    /// <summary>
    /// Qaytarim (yoki storno) so'rovini rad etadi — pul harakati bo'lmaydi.
    /// Sabab majburiy.
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [FinanceRole(FinanceAction.ApproveRefund)]
    public async Task<ActionResult<StudentRefundDto>> Reject(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<RejectStudentRefundRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await refunds.RejectAsync(
            id, request.Reason ?? string.Empty, FinanceActor.RequireUserId(User), ct));
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    private ActionResult? RejectServerDerivedFields(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in body.EnumerateObject())
        {
            if (!ServerDerivedFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            return BadRequest(new BillingErrorDto("identity_in_body",
                $"'{property.Name}' so'rov tanasida yuborilmaydi — uni server JWT'dan yoki "
                + "jurnaldan aniqlaydi (SPEC §4.4). Maydonni olib tashlang."));
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
