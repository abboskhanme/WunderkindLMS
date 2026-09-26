using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  O'QITUVCHI BO'LMAGAN XODIM MAOSHI — docs/modules/employees-unified.md
// ===========================================================================
//
//  `TeachersController` dagi uchta maosh endpoint'ining ko'zgusi: so'rov va
//  javob shakli, chiqim yo'li (`IExpenseService` — kassa, direktor tasdig'i
//  chegarasi, jurnal juftligi) va RUXSAT aynan bir xil. Farqi — pul
//  `expenses.employee_user_id` ga (xodim akkaunti, role="staff") yoziladi va
//  kerakli summa belgilangan oylikdan olinadi (`StaffSalaryCalc`), dars
//  jadvali yoki davomatdan emas.
//
//  NEGA `StaffController` ICHIDA EMAS
//  ---------------------------------
//  `StaffController` `[AdminPerm("staff")]` ostida — u xodim kartasini
//  boshqaradi. Maosh esa pul: F3.05 dagi xato (bo'lim kaliti bilan maosh
//  yozish) takrorlanmasligi uchun bu yerda faqat moliya darvozasi turadi —
//  `[Authorize(Roles = Roles.FinanceStaff)]` va amal darajasida
//  `[FinanceRole(...)]` (qoida `FinanceMatrix` da). "finance:view" xodimi
//  o'qiydi, yozishini `ViewOnlyWriteGuard` rad etadi.
// ===========================================================================

/// <summary>Xodim (role="staff") maoshi: berish, tarix, daftar. Batafsil: fayl boshidagi izoh.</summary>
[ApiController]
[Authorize(Roles = Roles.FinanceStaff)]
[Route("api/admin/staff")]
public class StaffSalaryController(AppDbContext db, AuditService audit, IExpenseService expenses)
    : ControllerBase
{
    /// <summary>
    /// Xodimga maosh berish — <c>POST api/admin/teachers/{id}/salary-payments</c> bilan bir xil
    /// tana va qoidalar (chegaradan katta summa direktor tasdig'ini kutadi, xato yozuv storno
    /// qilinadi). Chiqim <c>employee_user_id = id</c> bilan yoziladi.
    /// </summary>
    [HttpPost("{id}/salary-payments")]
    [Authorize(Roles = Roles.FinanceStaff)]
    [FinanceRole(FinanceAction.RecordExpense)]
    [BillingFault]
    public async Task<ActionResult<ExpenseDto>> PaySalary(
        string id, SalaryPaymentRequest req, CancellationToken ct)
    {
        var staff = await FindStaffAsync(id, ct);
        if (staff is null) return NotFound();

        if (req.Amount <= 0)
            return BadRequest(new { message = "Maosh summasi musbat bo'lishi kerak" });

        var note = string.IsNullOrWhiteSpace(req.Note)
            ? $"Oylik maosh — {staff.FullName}"
            : req.Note.Trim();

        // Kim berayotgani JWT'dan olinadi (SPEC §4.4), so'rov tanasidan emas.
        var expense = await expenses.CreateAsync(
            new CreateExpenseRequest(
                OnDate: AppClock.Today,
                Category: SalaryPaymentQuery.SalaryCategory,
                Amount: req.Amount,
                Method: string.IsNullOrWhiteSpace(req.Method) ? PaymentMethod.Transfer : req.Method,
                Note: note,
                EmployeeUserId: staff.Id),
            FinanceActor.RequireUserId(User), ct);

        audit.Record(AuditService.EntityStaffSalary, expense.Id.ToString(), "create",
            $"Maosh berildi: {staff.FullName} — {AuditService.Money(req.Amount)} so'm"
                + (expense.Status == ExpenseStatus.Pending ? " — direktor tasdig'ini kutmoqda" : ""),
            after: new { expense.OnDate, expense.Category, expense.Amount, expense.Note, expense.Status });
        await db.SaveChangesAsync(ct);

        return Ok(expense);
    }

    /// <summary>Xodimga berilgan maoshlar tarixi (jurnalga tushgan, storno qilinmagan).</summary>
    [HttpGet("{id}/salary-history")]
    [Authorize(Roles = Roles.FinanceStaff)]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<SalaryHistoryDto>> SalaryHistory(string id, CancellationToken ct)
    {
        var staff = await FindStaffAsync(id, ct);
        if (staff is null) return NotFound();

        var payments = (await new SalaryPaymentQuery(db).ForEmployeeAsync(id, ct: ct))
            .Select(t => new PaymentDto(t.OnDate.ToString("yyyy-MM-dd"), t.Amount, t.Note, t.Month))
            .ToList();

        return new SalaryHistoryDto(staff.Id, staff.FullName, staff.Salary,
            payments.Sum(p => p.Amount), payments);
    }

    /// <summary>
    /// Xodim maoshi bo'yicha davr hisobi (from..to): har oy kerakli (oylik, birinchi oy qisman),
    /// berilgan va qoldiq. Javob shakli o'qituvchinikining aynan o'zi.
    /// </summary>
    [HttpGet("{id}/salary-ledger")]
    [Authorize(Roles = Roles.FinanceStaff)]
    [FinanceRole(FinanceAction.ViewBillingReports)]
    public async Task<ActionResult<SalaryLedgerDto>> SalaryLedger(
        string id, [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var staff = await FindStaffAsync(id, ct);
        if (staff is null) return NotFound();
        return await SchoolLms.Application.Services.SalaryLedger.BuildForStaffAsync(db, staff, from, to);
    }

    /// <summary>Faqat role="staff" akkaunt — o'qituvchi, admin yoki kassir id'si 404.</summary>
    private Task<AppUser?> FindStaffAsync(string id, CancellationToken ct) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.Role == Roles.Staff, ct);
}
