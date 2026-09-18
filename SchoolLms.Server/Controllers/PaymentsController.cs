using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  To'lov endpoint'lari — SPEC §3.7, §4. Vazifa: P1-11.
// ===========================================================================
//
//  BU CONTROLLERDA O'ZGARTIRISH VA O'CHIRISH AMALLARI YO'Q — VA BO'LMAYDI.
//  ---------------------------------------------------------------------
//  To'lov o'zgarmas (SPEC §4.1). Xato to'lov FAQAT storno bilan tuzatiladi:
//  original qator joyida qoladi, ustiga qarshi qator qo'shiladi. Bu qoida
//  uch qavatda mustahkamlangan:
//    1) shu yerda bunday amal umuman yozilmagan (qabul mezoni: bu faylda
//       mos HTTP fe'llari NOL marta uchraydi — `PaymentsTests` buni
//       refleksiya orqali har yurishda tekshiradi);
//    2) `FinanceMatrix` da `EditOrDeletePayment` qoidasining rollar ro'yxati
//       BO'SH — kimdir yozib qo'ysa ham hech kimga ochilmaydi;
//    3) bazada `app_rw` rolida `payments` jadvaliga UPDATE/DELETE huquqi yo'q
//       (42501, `tools/verify-billing-guards.sh`).
//
//  IKKITA MANZIL, BITTA AMAL
//  -------------------------
//  `docs/TASKS.md` (P1-11) `POST /api/cash/payments` va
//  `POST /api/admin/payments/{id}/reverse` deb nomlaydi; P1-06 da MUZLATILGAN
//  klient shartnomasi (`schoollms.client/src/api/services/payments.ts`) esa
//  `/api/cashier/payments` va `/api/admin/billing/payments/{id}/reverse` ga
//  murojaat qiladi. Ikkala hujjat ham o'z o'rnida majburiy, shuning uchun har
//  amal IKKALA manzilda ham javob beradi — mantiq bitta, faqat marshrut ikkita.
//  Sabab docs/ASSUMPTIONS.md da yozilgan. Bittasini olib tashlash — buzuvchi
//  o'zgarish.
// ===========================================================================

/// <summary>
/// Moliya xatosining javob shakli. <c>code</c> — MASHINA uchun barqaror kalit
/// (<c>no_open_shift</c>, <c>already_reversed</c>, ...), <c>message</c> — ekranga
/// chiqadigan o'zbekcha matn. Klient matnga emas, kodga qarab qaror qabul qiladi.
/// </summary>
public sealed record PaymentErrorDto(string Code, string Message);

/// <summary>
/// Kassa to'lovlari: qabul qilish, taqsimlash, ko'rish va storno (P1-11).
/// Batafsil: fayl boshidagi izoh.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
public class PaymentsController(IPaymentService payments, IReceiptService receipts) : ControllerBase
{
    /// <summary>
    /// SPEC §4.4 — SERVER ANIQLAYDIGAN SHAXS. Bu nomlar so'rov tanasida
    /// uchrasa, so'rov <b>rad etiladi</b>; jimgina e'tiborsiz qoldirilmaydi.
    ///
    /// <para>
    /// Nega jim o'tkazib yubormaymiz: `System.Text.Json` notanish maydonni
    /// sukut bo'yicha TASHLAB ketadi. Ya'ni kassir `{"cashierId": "boshqa-odam"}`
    /// yuborsa, so'rov muvaffaqiyatli o'tardi va u "men boshqa odam nomidan
    /// yozdim" deb o'ylab qolardi — aslida yozuv o'z nomiga tushgan bo'lardi.
    /// Bunday chalkashlik moliyaviy tekshiruvda eng yomon holat. 400 esa
    /// muammoni AYNAN yuborgan odamga ko'rsatadi.
    /// </para>
    /// </summary>
    private static readonly string[] ServerDerivedFields =
    [
        "cashierId", "cashierName", "cashShiftId", "shiftId",
        "receiptNo", "receivedAt", "reversalOf", "reversedBy",
        "createdBy", "approvedBy", "approverId",
    ];

    /// <summary>MVC ning o'z sozlamalari bilan bir xil: camelCase, registrga befarq.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------
    //  To'lov qabul qilish
    // -----------------------------------------------------------------

    /// <summary>
    /// To'lovni qabul qiladi va chek raqamini beradi. BITTA tranzaksiyada:
    /// chek raqami, <c>payments</c>, N ta <c>payment_allocations</c>,
    /// hisob-faktura statuslari va 2 ta <c>ledger_entries</c> qatori.
    ///
    /// <para>
    /// <b>Bitta to'lov — bitta o'quvchi</b> (mijoz javobi, SPEC §8.1 Q14), lekin
    /// bir necha TOIFAGA taqsimlanadi: o'qish + avtobus + yotoqxona bitta chekda.
    /// </para>
    /// <para>
    /// Kassir (<c>cashier_id</c>) JWT'dan, smena esa uning ochiq smenasidan
    /// olinadi. So'rov tanasida shu maydonlar bo'lsa — <b>400</b> (SPEC §4.4).
    /// Ochiq smena bo'lmasa — <b>409 <c>no_open_shift</c></b> va bitta ham pul
    /// qatori yozilmaydi.
    /// </para>
    /// </summary>
    [HttpPost("/api/cash/payments")]
    [HttpPost("/api/cashier/payments")]
    [FinanceRole(FinanceAction.AcceptPayment)]
    public async Task<ActionResult<PaymentDto>> Accept(
        [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<AcceptPaymentRequest>(body);
        if (request is null)
            return BadRequest(new PaymentErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        try
        {
            var payment = await payments.AcceptAsync(
                request, FinanceActor.RequireUserId(User), ct);

            // finance-parity.md F1.07 / SPEC §4.7 — chek AVTOMATIK ota-onaning
            // Telegramiga yuboriladi, tugmani bosishni kutmaydi. To'lov
            // ALLAQACHON qabul qilingan (yuqoridagi qator commit bo'lgan);
            // `SendToGuardianAsync` HECH QACHON istisno tashlamaydi va
            // natijasini bu yerda e'tiborsiz qoldiramiz — yetkazilmaslik
            // (ota-ona botga ulanmagan, Telegram javob bermadi) to'lovni
            // bekor qilmaydi. "Qayta yuborish" tugmasi ekranda qoladi.
            _ = await receipts.SendToGuardianAsync(payment.Id, ct);

            return Ok(payment);
        }
        catch (PaymentException ex)
        {
            return Fail(ex);
        }
    }

    /// <summary>
    /// Taqsimot TAKLIFI kassir ekrani uchun: summani eng eski qarzdan boshlab
    /// bo'ladi (FIFO). Bu faqat taklif — haqiqiy taqsimotni kassir tasdiqlaydi.
    /// </summary>
    [HttpGet("/api/cash/payments/suggest-allocation")]
    [HttpGet("/api/cashier/payments/suggest-allocation")]
    [FinanceRole(FinanceAction.AcceptPayment)]
    public async Task<ActionResult<IEnumerable<AllocationSuggestionDto>>> Suggest(
        [FromQuery] string studentId, [FromQuery] decimal amount, CancellationToken ct)
    {
        var suggestion = await payments.SuggestAllocationAsync(studentId, amount, ct);
        return Ok(suggestion);
    }

    // -----------------------------------------------------------------
    //  O'qish
    // -----------------------------------------------------------------

    /// <summary>
    /// Bitta to'lov (chek uchun). Kassir FAQAT o'zi qabul qilgan to'lovni
    /// ko'radi — SPEC §4.3 da unga kassirlar kesimidagi ko'rinish berilmagan;
    /// boshqa kassirning cheki uning uchun mavjud emas (404, 403 emas: chek
    /// borligining o'zi ham ma'lumot).
    /// </summary>
    [HttpGet("/api/billing/payments/{id:guid}")]
    [FinanceRole(FinanceAction.AcceptPayment)]
    public async Task<ActionResult<PaymentDto>> Get(Guid id, CancellationToken ct)
    {
        var payment = await payments.GetAsync(id, ct);
        if (payment is null || (OnlyOwnPayments && payment.CashierId != FinanceActor.RequireUserId(User)))
            return NotFound(new PaymentErrorDto("payment_not_found", "To'lov topilmadi."));

        return Ok(payment);
    }

    /// <summary>
    /// To'lovlar ro'yxati (yangisidan eskisiga, eng ko'pi 1000 qator).
    /// Kassirning so'rovi majburan O'ZINING to'lovlari bilan cheklanadi.
    /// </summary>
    [HttpGet("/api/billing/payments")]
    [FinanceRole(FinanceAction.AcceptPayment)]
    public async Task<ActionResult<IEnumerable<PaymentDto>>> List(
        [FromQuery] string? studentId, [FromQuery] string? cashierId,
        [FromQuery] Guid? cashShiftId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? method, [FromQuery] bool onlyReversals,
        CancellationToken ct)
    {
        var query = new PaymentQuery(studentId, cashierId, cashShiftId, from, to, method, onlyReversals);
        if (OnlyOwnPayments) query = query with { CashierId = FinanceActor.RequireUserId(User) };

        var list = await payments.ListAsync(query, ct);
        return Ok(list);
    }

    // -----------------------------------------------------------------
    //  Storno — xato to'lovni tuzatishning YAGONA yo'li
    // -----------------------------------------------------------------

    /// <summary>
    /// To'lovni storno qiladi: <c>reversal_of</c> bilan YANGI <c>payments</c>
    /// qatori va ko'zgu jurnal yozuvlari. Original TEGILMAYDI.
    ///
    /// <para>
    /// Faqat <c>admin</c> yoki <c>superadmin</c> (SPEC §4.3) — kassirga
    /// <b>403</b>. Sabab majburiy: bo'sh bo'lsa <b>400</b>. Allaqachon storno
    /// qilingan to'lov — <b>409</b>. O'zi qabul qilgan to'lovni storno qilishga
    /// urinish — <b>403</b> (SPEC §4.5, ikki qavatli nazorat).
    /// </para>
    /// <para>
    /// Storno qatori tasdiqlovchining O'Z ochiq smenasiga yoziladi (pul bugun,
    /// uning kassasidan chiqadi), shuning uchun ochiq smena bo'lmasa —
    /// <b>409 <c>no_open_shift</c></b>.
    /// </para>
    /// </summary>
    [HttpPost("/api/admin/payments/{id:guid}/reverse")]
    [HttpPost("/api/admin/billing/payments/{id:guid}/reverse")]
    [FinanceRole(FinanceAction.ReversePayment)]
    public async Task<ActionResult<PaymentDto>> Reverse(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<ReversePaymentRequest>(body);
        if (request is null)
            return BadRequest(new PaymentErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        try
        {
            var storno = await payments.ReverseAsync(
                id, request.Reason ?? string.Empty, FinanceActor.RequireUserId(User), ct);
            return Ok(storno);
        }
        catch (PaymentException ex)
        {
            return Fail(ex);
        }
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    /// <summary>
    /// Chaqiruvchi FAQAT o'z to'lovlarini ko'rishi kerakmi? Kassir — ha;
    /// admin va direktor — yo'q (SPEC §4.3, "variance across cashiers").
    /// </summary>
    private bool OnlyOwnPayments =>
        User.IsInRole(Roles.Cashier)
        && !User.IsInRole(Roles.Admin)
        && !User.IsInRole(Roles.SuperAdmin);

    /// <summary>
    /// SPEC §4.4 tekshiruvi. Faqat YUQORI qavat nomlari ko'riladi: ichki
    /// obyektlar (taqsimot qatorlari) shaxsni bildiruvchi maydon saqlamaydi.
    /// </summary>
    private ActionResult? RejectServerDerivedFields(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in body.EnumerateObject())
        {
            if (!ServerDerivedFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            return BadRequest(new PaymentErrorDto("identity_in_body",
                $"'{property.Name}' so'rov tanasida yuborilmaydi — uni server JWT'dan "
                + "aniqlaydi (SPEC §4.4). Maydonni olib tashlang."));
        }

        return null;
    }

    private static T? Read<T>(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return default;
        try { return body.Deserialize<T>(Json); }
        catch (JsonException) { return default; }
    }

    /// <summary>
    /// Xizmat xatosini HTTP statusiga o'giradi. Jadval AYNAN shu yerda —
    /// status kodi ilova qatlamiga sizib kirmasligi uchun.
    /// </summary>
    private ObjectResult Fail(PaymentException ex)
    {
        var body = new PaymentErrorDto(ex.Code, ex.Message);
        return ex.Error switch
        {
            PaymentError.NotFound => StatusCode(StatusCodes.Status404NotFound, body),
            PaymentError.NoOpenShift => StatusCode(StatusCodes.Status409Conflict, body),
            PaymentError.Conflict => StatusCode(StatusCodes.Status409Conflict, body),
            PaymentError.DualControl => StatusCode(StatusCodes.Status403Forbidden, body),
            _ => StatusCode(StatusCodes.Status400BadRequest, body),
        };
    }
}
