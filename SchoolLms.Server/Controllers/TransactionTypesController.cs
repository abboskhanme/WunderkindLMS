using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Tranzaksiya turi katalogi (Kirim/Chiqim) — mijoz yuborgan EduSchool kassa
//  kirim shakli va moliya sozlamalari ekrani, 2026-09-18.
//  `/api/admin/finance/transaction-types`.
// ===========================================================================
//
//  RUXSAT
//  ------
//  Klass darajasi: `[Authorize(Roles = Roles.FinanceStaff)]` — admin va
//  direktor. Kassir bu yerga UMUMAN kirmaydi: kataloqni boshqarish emas,
//  ISHLATISH kassirning ishi (`CashBoxesController.PayIn` — `OperateCashBox`,
//  CashDesk — allaqachon o'sha rolga ochiq). Metod darajasida
//  `[FinanceRole(FinanceAction.ManageTransactionTypes)]` — HAR bir endpoint'da
//  (GET'lar ham kiradi), `ExpenseTemplatesController` naqshi.
//
//  NEGA XIZMAT DI'DAN EMAS
//  ------------------------
//  `Program.cs` bu vazifada tegilmaydigan fayl (hisobotga chiqarilgan).
//  Bog'liqliklar (`IAppDbContext`, `AuditService`) allaqachon konteynerda,
//  shuning uchun xizmatni shu yerda qo'lda yig'ish hech narsani yashirmaydi —
//  `ExpenseTemplatesController` bilan bir xil naqsh.
// ===========================================================================

/// <summary>Turlar: ro'yxat (kind bo'yicha filtrlanadi), yaratish, qisman tahrir, o'chirish. Batafsil: fayl boshi.</summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/finance/transaction-types")]
[BillingFault]
[Produces("application/json")]
public sealed class TransactionTypesController(IAppDbContext db, AuditService audit) : ControllerBase
{
    private readonly ITransactionTypeService types = new TransactionTypeService(db, audit);

    /// <summary>Hammasi, yoki <paramref name="kind"/> berilsa faqat shu turkum (<c>in</c> | <c>out</c>).</summary>
    [HttpGet]
    [FinanceRole(FinanceAction.ManageTransactionTypes)]
    public async Task<ActionResult<IEnumerable<TransactionTypeDto>>> List(
        [FromQuery] string? kind, CancellationToken ct) =>
        Ok(await types.ListAsync(kind, ct));

    /// <summary>Yangi tur.</summary>
    [HttpPost]
    [FinanceRole(FinanceAction.ManageTransactionTypes)]
    public async Task<ActionResult<TransactionTypeDto>> Create(
        [FromBody] CreateTransactionTypeRequest request, CancellationToken ct)
    {
        var created = await types.CreateAsync(request, FinanceActor.RequireUserId(User), ct);
        return Ok(created);
    }

    /// <summary>Qisman tahrir — faqat berilgan maydonlar o'zgaradi. <c>Kind</c> o'zgarmaydi.</summary>
    [HttpPut("{id:guid}")]
    [FinanceRole(FinanceAction.ManageTransactionTypes)]
    public async Task<ActionResult<TransactionTypeDto>> Update(
        Guid id, [FromBody] UpdateTransactionTypeRequest request, CancellationToken ct)
    {
        var updated = await types.UpdateAsync(id, request, FinanceActor.RequireUserId(User), ct);
        return Ok(updated);
    }

    /// <summary>
    /// O'chiradi (haqiqiy DELETE). Rad etiladi — seed qilingan tur, yoki
    /// allaqachon ishlatilgan tur (fayl boshidagi izoh).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [FinanceRole(FinanceAction.ManageTransactionTypes)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await types.DeleteAsync(id, FinanceActor.RequireUserId(User), ct);
        return NoContent();
    }
}
