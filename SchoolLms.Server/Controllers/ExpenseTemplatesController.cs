using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Rejalashtirilgan chiqim shabloni — F6.01
//  (docs/modules/finance-parity.md §2.6.3, §2.14: `/api/admin/finance/expense-templates`).
// ===========================================================================
//
//  RUXSAT
//  ------
//  Klass darajasi: `[Authorize(Roles = Roles.FinanceStaff)]` — admin va
//  direktor. Kassir bu yerga UMUMAN kirmaydi: SPEC §4.3 jadvalida unga
//  "Record an expense" ham berilmagan, rejalashtirishga esa aloqasi yo'q.
//  Metod darajasida `[FinanceRole(FinanceAction.ManageExpenseTemplates)]` —
//  HAR bir endpoint'da, `PayrollAdjustmentsController` naqshi (GET'lar ham
//  kiradi — task topshirig'i: "every endpoint gets an authorisation test").
//
//  NEGA XIZMAT DI'DAN EMAS
//  ------------------------
//  `Program.cs` bu vazifadan boshqa ikkita agent bilan bo'lishiladigan fayl
//  (task topshirig'i — uni tahrirlamaslik). Bog'liqliklar (`IAppDbContext`,
//  `AuditService`) allaqachon konteynerda, shuning uchun xizmatni shu yerda
//  qo'lda yig'ish hech narsani yashirmaydi — `PayrollAdjustmentsController` /
//  `BillingCatalogController.Settings` bilan bir xil naqsh
//  (docs/PENDING_WIRING.md §3a).
// ===========================================================================

/// <summary>Shablonlar: ro'yxat, yaratish, qisman tahrir, o'chirish. Batafsil: fayl boshi.</summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/finance/expense-templates")]
[BillingFault]
[Produces("application/json")]
public sealed class ExpenseTemplatesController(IAppDbContext db, AuditService audit) : ControllerBase
{
    private readonly IExpenseTemplateService templates = new ExpenseTemplateService(db, audit);

    /// <summary>Hammasi, nom bo'yicha.</summary>
    [HttpGet]
    [FinanceRole(FinanceAction.ManageExpenseTemplates)]
    public async Task<ActionResult<IEnumerable<ExpenseTemplateDto>>> List(CancellationToken ct) =>
        Ok(await templates.ListAsync(ct));

    /// <summary>Yangi shablon.</summary>
    [HttpPost]
    [FinanceRole(FinanceAction.ManageExpenseTemplates)]
    public async Task<ActionResult<ExpenseTemplateDto>> Create(
        [FromBody] CreateExpenseTemplateRequest request, CancellationToken ct)
    {
        var created = await templates.CreateAsync(request, FinanceActor.RequireUserId(User), ct);
        return Ok(created);
    }

    /// <summary>Qisman tahrir — faqat berilgan maydonlar o'zgaradi.</summary>
    [HttpPut("{id:guid}")]
    [FinanceRole(FinanceAction.ManageExpenseTemplates)]
    public async Task<ActionResult<ExpenseTemplateDto>> Update(
        Guid id, [FromBody] UpdateExpenseTemplateRequest request, CancellationToken ct)
    {
        var updated = await templates.UpdateAsync(id, request, FinanceActor.RequireUserId(User), ct);
        return Ok(updated);
    }

    /// <summary>O'chiradi (haqiqiy DELETE — soft-delete emas, fayl boshidagi izoh).</summary>
    [HttpDelete("{id:guid}")]
    [FinanceRole(FinanceAction.ManageExpenseTemplates)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await templates.DeleteAsync(id, FinanceActor.RequireUserId(User), ct);
        return NoContent();
    }
}
