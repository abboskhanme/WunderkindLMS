using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// "Abonement oyini butunlay o'chirish" (client, 2026-10-07) — even a paid month; its payments stay in
/// the archive as storno and leave the accounts (MonthRemovalService). Admin and director only, like
/// voiding an invoice or reversing a payment.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[BillingFault]
[Route("api/admin/billing/subscriptions/{id:guid}/remove-months")]
public sealed class SubscriptionMonthRemovalController(AppDbContext db, AuditService audit) : ControllerBase
{
    private MonthRemovalService Service => new(db, audit);

    /// <summary>The months that can be removed and the receipts that paid them — nothing is written.</summary>
    [HttpGet]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<MonthRemovalPreviewDto>> Preview(Guid id) =>
        await Service.PreviewAsync(id, FinanceActor.RequireUserId(User), HttpContext.RequestAborted);

    [HttpPost]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<MonthRemovalResultDto>> Remove(Guid id, MonthRemovalRequest request) =>
        await Service.RemoveAsync(id, request, FinanceActor.RequireUserId(User), HttpContext.RequestAborted);
}
