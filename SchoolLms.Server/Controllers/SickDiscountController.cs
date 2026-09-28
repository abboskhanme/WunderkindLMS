using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// "Kasallik chegirmasi" from the cashier's payment form — docs/modules/billing-plans.md §9.
/// Put by whoever takes payments (<see cref="FinanceAction.AcceptPayment"/>), no approval
/// (client, 2026-09-28); every deduction is an append-only invoice adjustment with its ledger batch.
/// </summary>
[ApiController]
[Authorize]
[BillingFault]
[FinanceRole(FinanceAction.AcceptPayment)]
public sealed class SickDiscountController(AppDbContext db, AuditService audit) : ControllerBase
{
    private SickDiscountService Service => new(db, audit);

    /// <summary>What would be taken off — nothing is written.</summary>
    [HttpGet("/api/cash/sick-discount/preview")]
    [HttpGet("/api/cashier/sick-discount/preview")]
    public async Task<ActionResult<SickDiscountDto>> Preview(
        [FromQuery] string studentId, [FromQuery] string month, [FromQuery] int days) =>
        await Service.PreviewAsync(studentId, month, days, HttpContext.RequestAborted);

    [HttpPost("/api/cash/sick-discount")]
    [HttpPost("/api/cashier/sick-discount")]
    public async Task<ActionResult<SickDiscountDto>> Apply(SickDiscountRequest request) =>
        await Service.ApplyAsync(request, FinanceActor.RequireUserId(User), HttpContext.RequestAborted);

    /// <summary>The student's sickness discounts that still stand — each can be cancelled (§9.1).</summary>
    [HttpGet("/api/cash/sick-discount")]
    [HttpGet("/api/cashier/sick-discount")]
    public async Task<ActionResult<IReadOnlyList<SickDiscountEntryDto>>> List([FromQuery] string studentId) =>
        Ok(await Service.ListAsync(studentId, HttpContext.RequestAborted));

    /// <summary>Cancels one application whole — the debt it took off comes back.</summary>
    [HttpPost("/api/cash/sick-discount/{groupId:guid}/cancel")]
    [HttpPost("/api/cashier/sick-discount/{groupId:guid}/cancel")]
    public async Task<ActionResult<SickDiscountCancelDto>> Cancel(Guid groupId) =>
        await Service.CancelAsync(groupId, FinanceActor.RequireUserId(User), HttpContext.RequestAborted);
}
