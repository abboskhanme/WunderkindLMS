using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  KASSALAR (cash boxes) — "smena" o'rnini bosadi. Mijoz javobi, 2026-09:
//  "bizni tizimda smena degan tushuncha umuman bo'lmasin butunlay olib
//  tashla, shunchaki kassa degan narsa bo'lsin xolos".
// ===========================================================================
//
//  MARSHRUTLAR — frontend agent shu shaklga tayanib yozadi, O'ZGARTIRILMAYDI:
//    GET    /api/admin/cash-boxes
//    POST   /api/admin/cash-boxes
//    PUT    /api/admin/cash-boxes/{id}
//    POST   /api/admin/cash-boxes/{id}/in
//    POST   /api/admin/cash-boxes/{id}/out
//    POST   /api/admin/cash-boxes/{id}/transfer
//    POST   /api/admin/cash-boxes/{id}/exchange
//    GET    /api/admin/cash-boxes/transactions?from=&to=&boxId=&q=
//    POST   /api/admin/cash-boxes/transactions/{id}/cancel
//
//  NEGA XIZMAT DI'DAN EMAS, SHU YERDA QURILADI
//  --------------------------------------------
//  `CashHandoversController` dagi bilan AYNAN bir xil sabab: registratsiya
//  Program.cs da bo'lardi, u esa BU VAZIFADA tegilmaydigan fayl ("Do not
//  edit: Program.cs" — hisobotga chiqarilgan). `CashBoxService` faqat
//  `IAppDbContext` talab qiladi (allaqachon DI'da), shuning uchun uni shu
//  yerda yig'ish HECH NARSANI yashirmaydi va endpoint birinchi kundanoq
//  ishlaydi.
// ===========================================================================

/// <summary>
/// Kassalar: kataloq (ochish/o'zgartirish) va to'rtta pul amali (kirim,
/// chiqim, ko'chirish, ayirboshlash) + harakatlar jurnali. Batafsil: fayl
/// boshidagi izoh.
/// </summary>
//  DIQQAT — RUXSAT IKKI QAVATLI EMAS, BITTA QAVATLI:
//  `ExpensesController` dagi naqshni ATAYLAB TAKRORLAMAYMIZ (u yerda klass
//  darajasida `[Authorize(Roles = Roles.FinanceStaff)]` bor — Admin+Direktor,
//  KASSIR UMUMAN YO'Q, chunki chiqim kassirga yopiq amal). Bu yerda esa
//  KASSIR ham kirishi kerak (`OperateCashBox`/`ViewCashBoxes` — CashDesk),
//  shuning uchun klass darajasida FAQAT `[Authorize]` (autentifikatsiya —
//  "token bormi"), aniq ruxsat esa har metodda `[FinanceRole(...)]` orqali —
//  xuddi `PaymentsController` dagi kabi.
[ApiController]
[Authorize]
[Route("api/admin/cash-boxes")]
[BillingFault]
[Produces("application/json")]
public sealed class CashBoxesController(IAppDbContext db) : ControllerBase
{
    /// <summary>
    /// <see cref="CashHandoversController"/> dagi bilan bir xil naqsh — fayl
    /// boshidagi izohga qarang.
    /// </summary>
    private readonly ICashBoxService boxes = new CashBoxService(db);

    /// <summary>
    /// SPEC §4.4 — SERVER ANIQLAYDIGAN SHAXS. Bu nomlar so'rov tanasida
    /// uchrasa, so'rov <b>rad etiladi</b>; jimgina e'tiborsiz qoldirilmaydi.
    /// </summary>
    private static readonly string[] ServerDerivedFields =
    [
        "createdBy", "createdByName", "createdAt", "actorId",
        "status", "reversalOf", "who", "no",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------
    //  Kataloq
    // -----------------------------------------------------------------

    /// <summary>Kassalar ro'yxati — har birining balansi va usul kesimi HAR SAFAR hisoblanadi.</summary>
    [HttpGet]
    [FinanceRole(FinanceAction.ViewCashBoxes)]
    public async Task<ActionResult<IEnumerable<CashBoxDto>>> List(CancellationToken ct) =>
        Ok(await boxes.ListAsync(ct));

    /// <summary>
    /// Yangi kassa ochadi. Birinchi kassa avtomatik SUKUT (default) bo'ladi —
    /// bazada "aynan bittasi sukut" qisman unikal indeks bilan kafolatlanadi.
    /// </summary>
    [HttpPost]
    [FinanceRole(FinanceAction.ManageCashBoxes)]
    public async Task<ActionResult<CashBoxDto>> Create(
        [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<CreateCashBoxRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await boxes.CreateAsync(request, FinanceActor.RequireUserId(User), ct));
    }

    /// <summary>
    /// Kassani yangilaydi: nomi, mas'uli, sukut belgisi, faolligi. Sukut
    /// kassani o'chirib (<c>isActive=false</c>) bo'lmaydi — avval boshqa
    /// kassani sukut qiling.
    /// </summary>
    [HttpPut("{id:guid}")]
    [FinanceRole(FinanceAction.ManageCashBoxes)]
    public async Task<ActionResult<CashBoxDto>> Update(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<UpdateCashBoxRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await boxes.UpdateAsync(id, request, FinanceActor.RequireUserId(User), ct));
    }

    // -----------------------------------------------------------------
    //  Pul amallari — EduSchool ekranidagi to'rtta tugma
    // -----------------------------------------------------------------

    /// <summary>Kirim.</summary>
    [HttpPost("{id:guid}/in")]
    [FinanceRole(FinanceAction.OperateCashBox)]
    public async Task<ActionResult<CashBoxTransactionRowDto>> PayIn(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<CashBoxPayInRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await boxes.PayInAsync(id, request, FinanceActor.RequireUserId(User), ct));
    }

    /// <summary>Chiqim.</summary>
    [HttpPost("{id:guid}/out")]
    [FinanceRole(FinanceAction.OperateCashBox)]
    public async Task<ActionResult<CashBoxTransactionRowDto>> PayOut(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<CashBoxPayOutRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await boxes.PayOutAsync(id, request, FinanceActor.RequireUserId(User), ct));
    }

    /// <summary>
    /// Ko'chirish — bitta kassadan ikkinchisiga. Atomar: pul YO ikkala
    /// kassada ham o'zgaradi, YO hech birida (ikkala kassaning qulfi qat'iy
    /// tartibda olinadi — <see cref="CashBoxService.TransferAsync"/> izohi).
    /// </summary>
    [HttpPost("{id:guid}/transfer")]
    [FinanceRole(FinanceAction.OperateCashBox)]
    public async Task<ActionResult<CashBoxTransactionRowDto>> Transfer(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<CashBoxTransferRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await boxes.TransferAsync(id, request, FinanceActor.RequireUserId(User), ct));
    }

    /// <summary>Ayirboshlash — bitta kassa ichida usuldan usulga (jami o'zgarmaydi).</summary>
    [HttpPost("{id:guid}/exchange")]
    [FinanceRole(FinanceAction.OperateCashBox)]
    public async Task<ActionResult<CashBoxTransactionRowDto>> Exchange(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<CashBoxExchangeRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await boxes.ExchangeAsync(id, request, FinanceActor.RequireUserId(User), ct));
    }

    // -----------------------------------------------------------------
    //  Harakatlar jurnali
    // -----------------------------------------------------------------

    /// <summary>Kassa harakatlari — davr, kassa va erkin qidiruv bo'yicha filtrlanadi.</summary>
    [HttpGet("transactions")]
    [FinanceRole(FinanceAction.ViewCashBoxes)]
    public async Task<ActionResult<CashBoxTransactionsPageDto>> Transactions(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? boxId, [FromQuery] string? q,
        CancellationToken ct)
    {
        var page = await boxes.TransactionsAsync(new CashBoxTransactionsQuery(from, to, boxId, q), ct);
        return Ok(page);
    }

    /// <summary>
    /// Amalni bekor qiladi: qarshi qator qo'shiladi (original TEGILMAYDI,
    /// SPEC §4.1). Sabab majburiy.
    /// </summary>
    [HttpPost("transactions/{id:guid}/cancel")]
    [FinanceRole(FinanceAction.CancelCashBoxTransaction)]
    public async Task<ActionResult<CashBoxTransactionRowDto>> Cancel(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<CancelCashBoxTransactionRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        return Ok(await boxes.CancelTransactionAsync(
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
