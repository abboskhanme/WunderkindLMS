using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// "Chiqib ketdi — abonementni bekor qilish" (<see cref="StudentWithdrawalService"/>). Kim ochib-yopa
/// olsa abonementni, o'sha bekor qila oladi: moliya rollari va "abonementlarni boshqarish" huquqi.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[BillingFault]
[Route("api/admin/billing/students/{studentId}/withdrawals")]
public sealed class StudentWithdrawalController(AppDbContext db, AuditService audit) : ControllerBase
{
    private StudentWithdrawalService Service => new(db, audit);

    /// <summary>O'quvchining "chiqib ketdi" amallari, yangisi birinchi.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WithdrawalEntryDto>>> List(string studentId) =>
        Ok(await Service.ListAsync(studentId, HttpContext.RequestAborted));

    /// <summary>Nima bo'lishini ko'rsatadi — hech narsa yozilmaydi.</summary>
    [HttpPost("preview")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<WithdrawalPreviewDto>> Preview(string studentId, WithdrawalRequest request) =>
        await Service.PreviewAsync(studentId, request, HttpContext.RequestAborted);

    [HttpPost]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<WithdrawalPreviewDto>> Apply(string studentId, WithdrawalRequest request) =>
        await Service.ApplyAsync(studentId, request, FinanceActor.RequireUserId(User), HttpContext.RequestAborted);

    [HttpPost("{withdrawalId:guid}/cancel")]
    [FinanceRole(FinanceAction.ManageSubscriptions)]
    public async Task<ActionResult<WithdrawalEntryDto>> Cancel(string studentId, Guid withdrawalId) =>
        await Service.CancelAsync(studentId, withdrawalId, FinanceActor.RequireUserId(User), HttpContext.RequestAborted);
}
