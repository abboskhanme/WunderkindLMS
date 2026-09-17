using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  KASSADAN PUL TOPSHIRISH — F1.04 (finance-parity §2.1.3, §5 Q1).
// ===========================================================================
//
//  BU CONTROLLERDA PUT HAM, DELETE HAM YO'Q — VA BO'LMAYDI.
//  --------------------------------------------------------
//  `cash_handovers` bazada FAQAT QO'SHILADI (`app_rw` da UPDATE/DELETE yo'q,
//  finance-parity §3.1 A2). Xato topshiriq `POST {id}/reverse` bilan
//  tuzatiladi: original qator joyida qoladi, ustiga qarshi qator qo'shiladi.
//
//  RUXSAT (SPEC §4.3)
//  ------------------
//  Topshirish — KASSIRNING o'z smenasidagi amali, ya'ni rollar to'plami
//  `ManageOwnShift` bilan AYNAN bir xil (kassir + admin + direktor). Yangi
//  `FinanceAction` a'zosi ATAYLAB qo'shilmadi: `FinanceRoleAttribute.cs` bu
//  to'lqinda umumiy fayl va unga qo'shimcha bir nechta slice'ning diff'ini
//  to'qnashtiradi; mavjud a'zoning ma'nosi esa AYNAN mos tushadi — "o'z
//  smenangdagi amal". Xuddi shu yo'l `ExpensesController` da storno uchun
//  ham tanlangan (u `ApproveExpense` ni qayta ishlatadi), ya'ni bu yerda
//  yangi naqsh kiritilmayapti.
//
//  Ko'rish esa ROLGA QARAB TORAYADI: admin va direktor hamma kassirni,
//  kassir faqat O'ZINIKINI ko'radi. 403 berilmaydi, filtr jimgina toraytiriladi
//  — `CashShiftsController.List` dagi bilan bir xil sabab: kassir boshqa
//  filtr yuborayotganini bilmasligi ham mumkin.
// ===========================================================================

/// <summary>
/// Kassadan bankka yoki direktorning seyfiga topshirilgan naqd (F1.04).
/// Batafsil: fayl boshidagi izoh.
///
/// <para>
/// Xato javoblari <c>{ "code": ..., "message": ... }</c> shaklida —
/// <see cref="BillingFaultAttribute"/> orqali, moliya modulining qolgan
/// qismidagi bilan bir xil.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/cash/handovers")]
[BillingFault]
[Produces("application/json")]
public sealed class CashHandoversController(
    IAppDbContext db, ILedgerService ledger, ICashShiftService shifts) : ControllerBase
{
    /// <summary>
    /// <b>Nega xizmat DI'dan emas, shu yerda quriladi.</b> Registratsiya
    /// <c>Program.cs</c> da bo'lardi, u esa bu to'lqinda ORKESTRATOR fayli
    /// (finance-parity §4 — "shared files, one pass per wave") va S2 unga
    /// tegmaydi. Xizmatning hamma bog'liqligi allaqachon konteynerda, ya'ni
    /// uni shu yerda yig'ish hech narsani yashirmaydi va endpoint birinchi
    /// kundanoq ishlaydi. Keyinchalik orkestrator bitta qator qo'shsa,
    /// konstruktorga <c>ICashHandoverService</c> ni kiritish — bir qatorlik
    /// o'zgarish.
    /// </summary>
    private readonly ICashHandoverService handovers = new CashHandoverService(db, ledger, shifts);

    /// <summary>
    /// SPEC §4.4 — SERVER ANIQLAYDIGAN SHAXS. Bu nomlar so'rov tanasida
    /// uchrasa, so'rov <b>rad etiladi</b>; jimgina e'tiborsiz qoldirilmaydi
    /// (<c>System.Text.Json</c> notanish maydonni tashlab ketardi va yozuvchi
    /// "boshqa odam nomiga yozdim" deb o'ylab qolardi).
    /// </summary>
    private static readonly string[] ServerDerivedFields =
    [
        "cashShiftId", "cashierId", "cashierName",
        "createdBy", "createdByName", "createdAt",
        "reversalOf", "reversed",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------
    //  Yozish
    // -----------------------------------------------------------------

    /// <summary>
    /// Topshiriqni yozadi. Smena — JORIY foydalanuvchining ochiq smenasi
    /// (so'rovdan olinmaydi); ochiq smena bo'lmasa <b>409</b>
    /// (<c>no_open_shift</c>).
    ///
    /// <para>
    /// <c>destination = bank</c> jurnalga <c>debit bank / credit cash</c>
    /// qo'yadi; <c>safe</c> jurnalga yozmaydi, lekin ikkalasi ham smenaning
    /// kutilgan naqdini kamaytiradi (pul javondan chiqdi).
    /// </para>
    /// </summary>
    [HttpPost]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    public async Task<ActionResult<CashHandoverDto>> Record(
        [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<RecordCashHandoverRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await handovers.RecordAsync(request, FinanceActor.RequireUserId(User), ct));
    }

    /// <summary>
    /// Topshiriqni storno qiladi: qarshi qator qo'shiladi va pul storno
    /// qiluvchining OCHIQ smenasiga qaytadi. Sabab majburiy.
    ///
    /// <para>
    /// Kassir o'zganing smenasidan chiqqan topshiriqni qaytara olmaydi —
    /// <b>403</b> (<c>not_your_shift</c>); admin va direktor qaytara oladi.
    /// Ikki marta storno — <b>409</b>.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/reverse")]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    public async Task<ActionResult<CashHandoverDto>> Reverse(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<ReverseCashHandoverRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await handovers.ReverseAsync(
            id, request.Reason ?? string.Empty, FinanceActor.RequireUserId(User), ct));
    }

    // -----------------------------------------------------------------
    //  O'qish
    // -----------------------------------------------------------------

    /// <summary>
    /// Topshiriqlar registri. Admin va direktor hamma kassirni ko'radi;
    /// kassir esa faqat O'Z smenalaridan chiqqan pulni, so'rovda boshqa
    /// <c>cashierId</c> yozgan bo'lsa ham.
    /// </summary>
    [HttpGet]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    public async Task<ActionResult<IEnumerable<CashHandoverDto>>> List(
        [FromQuery] Guid? shiftId,
        [FromQuery] string? cashierId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? destination,
        CancellationToken ct)
    {
        var scoped = IsSupervisor ? cashierId : FinanceActor.RequireUserId(User);
        var query = new CashHandoverQuery(shiftId, scoped, from, to, destination);
        return Ok(await handovers.ListAsync(query, ct));
    }

    /// <summary>
    /// Bitta topshiriq. Kassir o'zganing smenasidagi qatorni ko'ra olmaydi —
    /// <b>403</b>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [FinanceRole(FinanceAction.ManageOwnShift)]
    public async Task<ActionResult<CashHandoverDto>> Get(Guid id, CancellationToken ct)
    {
        var row = await handovers.GetAsync(id, ct);
        if (row is null)
            return NotFound(new BillingErrorDto("handover_not_found", "Topshiriq topilmadi."));

        // Egalik tekshiruvi qator o'qilgandan KEYIN: smenaning egasini bilish
        // uchun baribir o'sha qator kerak edi. Javob berilmaydi.
        if (!IsSupervisor && !string.Equals(
                row.CashierId, FinanceActor.RequireUserId(User), StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new BillingErrorDto(
                "not_your_shift",
                "Bu topshiriq boshqa kassirning smenasidan — uni admin yoki direktor ko'radi."));

        return Ok(row);
    }

    // -----------------------------------------------------------------
    //  Yordamchilar
    // -----------------------------------------------------------------

    /// <summary>
    /// Nazoratchi (admin/direktor) mi? Rollar ro'yxati YAGONA joyda —
    /// <see cref="CashShiftService.SupervisorRoles"/> da, chunki AYNAN o'sha
    /// ro'yxat xizmat ichida ham ("o'zganing smenasi") ishlatiladi.
    /// </summary>
    private bool IsSupervisor => CashShiftService.SupervisorRoles.Any(User.IsInRole);

    private ActionResult? RejectServerDerivedFields(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in body.EnumerateObject())
        {
            if (!ServerDerivedFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            return BadRequest(new BillingErrorDto("identity_in_body",
                $"'{property.Name}' so'rov tanasida yuborilmaydi — uni server JWT'dan "
                + "yoki ochiq smenadan aniqlaydi (SPEC §4.4). Maydonni olib tashlang."));
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
