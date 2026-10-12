using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
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
//    POST   /api/admin/cash-boxes/transactions/export        (2026-10-12, .xlsx)
//    GET    /api/admin/cash-boxes/transfers/incoming         (2026-10-12)
//    POST   /api/admin/cash-boxes/transfers/{id}/accept      (2026-10-12)
//    POST   /api/admin/cash-boxes/transfers/{id}/return      (2026-10-12)
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
public sealed class CashBoxesController(
    IAppDbContext db, TelegramService telegram, ILogger<CashBoxTransferNotifier> notifierLogger) : ControllerBase
{
    private CashBoxTransferNotifier Notifier => new(db, telegram, notifierLogger);

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

        var row = await boxes.TransferAsync(id, request, FinanceActor.RequireUserId(User), ct);
        if (row.Status == CashBoxRowStatus.Pending)
            await Notifier.NotifyPendingAsync(row.Id, CancellationToken.None);
        return Ok(row);
    }

    // -----------------------------------------------------------------
    //  Transfer acceptance (client, 2026-10-12) — the receiving box's responsible person
    //  accepts what was sent, or returns it. The service checks who may answer.
    // -----------------------------------------------------------------

    /// <summary>Transfers waiting for the current user's answer (into boxes they are responsible for).</summary>
    [HttpGet("transfers/incoming")]
    [FinanceRole(FinanceAction.ViewCashBoxes)]
    public async Task<ActionResult<IEnumerable<IncomingCashBoxTransferDto>>> IncomingTransfers(CancellationToken ct) =>
        Ok(await boxes.IncomingTransfersAsync(FinanceActor.RequireUserId(User), ct));

    /// <summary>Accept: the money leaves the sending box and lands in the receiving one.</summary>
    [HttpPost("transfers/{id:guid}/accept")]
    [FinanceRole(FinanceAction.OperateCashBox)]
    public async Task<ActionResult<CashBoxTransactionRowDto>> AcceptTransfer(Guid id, CancellationToken ct)
    {
        var row = await boxes.AcceptTransferAsync(id, FinanceActor.RequireUserId(User), ct);
        await Notifier.NotifyDecidedAsync(id, CancellationToken.None);
        return Ok(row);
    }

    /// <summary>Return: the money stays in the sending box. A reason is required.</summary>
    [HttpPost("transfers/{id:guid}/return")]
    [FinanceRole(FinanceAction.OperateCashBox)]
    public async Task<ActionResult<CashBoxTransactionRowDto>> ReturnTransfer(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (RejectServerDerivedFields(body) is { } rejected) return rejected;

        var request = Read<ReturnCashBoxTransferRequest>(body);
        if (request is null)
            return BadRequest(new BillingErrorDto("invalid_body", "So'rov tanasi o'qilmadi."));

        var row = await boxes.ReturnTransferAsync(id, request.Reason ?? string.Empty, FinanceActor.RequireUserId(User), ct);
        await Notifier.NotifyDecidedAsync(id, CancellationToken.None);
        return Ok(row);
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

    /// <summary>
    /// Who a salary Chiqim can be paid to: teachers and staff, by name (2026-10-09). Its own endpoint,
    /// because the cashier may not have the Teachers or Staff sections open, and the form needs only
    /// an id and a name.
    /// </summary>
    [HttpGet("salary-recipients")]
    [FinanceRole(FinanceAction.OperateCashBox)]
    public async Task<ActionResult<IEnumerable<SalaryRecipientDto>>> SalaryRecipients(CancellationToken ct)
    {
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => !t.IsArchived)
            .Select(t => new SalaryRecipientDto("teacher", t.Id, t.FullName))
            .ToListAsync(ct);
        var staff = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Staff)
            .Select(u => new SalaryRecipientDto("employee", u.Id, u.FullName))
            .ToListAsync(ct);
        return Ok(teachers.Concat(staff).OrderBy(r => r.Name, StringComparer.Ordinal));
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
    /// The journal as .xlsx — the rows on screen, in screen order (<see cref="CashBoxTransactionsExport"/>).
    /// A POST because the ids of a full day do not fit in a query string; it writes nothing.
    /// </summary>
    [HttpPost("transactions/export")]
    [FinanceRole(FinanceAction.ViewCashBoxes, ReadOnlyPost = true)]
    public async Task<IActionResult> ExportTransactions(
        [FromBody] CashBoxTransactionsExportRequest request, CancellationToken ct)
    {
        var page = await boxes.TransactionsAsync(
            new CashBoxTransactionsQuery(request.From, request.To, request.BoxId, request.Q), ct);

        IReadOnlyList<CashBoxTransactionRowDto> rows = page.Rows;
        if (request.Ids is { Count: > 0 } ids)
        {
            var byId = page.Rows.ToDictionary(r => r.Id);
            rows = [.. ids.Distinct().Where(byId.ContainsKey).Select(id => byId[id])];
        }

        var boxName = request.BoxId is { } boxId
            ? await db.CashBoxes.AsNoTracking().Where(b => b.Id == boxId).Select(b => b.Name).FirstOrDefaultAsync(ct)
            : null;
        var bytes = CashBoxTransactionsExport.Build(rows, request.From, request.To, boxName);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            CashBoxTransactionsExport.FileName(request.From, request.To));
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

/// <summary>A person a salary Chiqim can name.</summary>
/// <param name="Kind"><c>teacher</c> (teachers.id) or <c>employee</c> (users.id).</param>
public sealed record SalaryRecipientDto(string Kind, string Id, string Name);

/// <summary>What to export: the journal filter, and the ids of the rows on screen (null or empty = all of them).</summary>
public sealed record CashBoxTransactionsExportRequest(
    DateOnly? From = null, DateOnly? To = null, Guid? BoxId = null, string? Q = null,
    IReadOnlyList<Guid>? Ids = null);
