using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;
using SchoolLms.Server.Models;
using SchoolLms.Server.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/finance")]
public class FinanceController(AppDbContext db, AuditService audit) : ControllerBase
{
    private async Task<Dictionary<string, string>> StudentNames() =>
        await db.Students.ToDictionaryAsync(s => s.Id, s => s.FullName);

    private async Task<Dictionary<string, string>> TeacherNames() =>
        await db.Teachers.ToDictionaryAsync(t => t.Id, t => t.FullName);

    private static FinanceTransactionDto ToDto(
        FinanceTransaction t,
        IReadOnlyDictionary<string, string> students,
        IReadOnlyDictionary<string, string> teachers) =>
        new(t.Id, t.Date, t.Direction, t.Category, t.Amount, t.Note,
            t.StudentId, t.StudentId is not null && students.TryGetValue(t.StudentId, out var s) ? s : null,
            t.TeacherId, t.TeacherId is not null && teachers.TryGetValue(t.TeacherId, out var te) ? te : null,
            t.Month);

    [HttpGet("transactions")]
    public async Task<ActionResult<IEnumerable<FinanceTransactionDto>>> GetTransactions(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? direction, [FromQuery] string? category)
    {
        var query = db.FinanceTransactions.AsQueryable();
        if (!string.IsNullOrEmpty(from)) query = query.Where(t => string.Compare(t.Date, from) >= 0);
        if (!string.IsNullOrEmpty(to)) query = query.Where(t => string.Compare(t.Date, to) <= 0);
        if (!string.IsNullOrEmpty(direction)) query = query.Where(t => t.Direction == direction);
        if (!string.IsNullOrEmpty(category)) query = query.Where(t => t.Category == category);

        var list = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync();
        var students = await StudentNames();
        var teachers = await TeacherNames();
        return list.Select(t => ToDto(t, students, teachers)).ToList();
    }

    [HttpPost("transactions")]
    public async Task<ActionResult<FinanceTransactionDto>> Create(FinanceTransactionPayload p)
    {
        var tx = new FinanceTransaction
        {
            Date = p.Date,
            Direction = p.Direction,
            Category = p.Category,
            Amount = p.Amount,
            Note = p.Note,
            StudentId = p.StudentId,
            TeacherId = p.TeacherId,
        };
        db.FinanceTransactions.Add(tx);

        var dir = tx.Direction == "income" ? "Kirim" : "Chiqim";
        var summary = tx is { Category: "salary", TeacherId: not null }
            ? $"Maosh berildi: {AuditService.Money(tx.Amount)} so'm"
            : $"{dir} qo'shildi: {tx.Category} — {AuditService.Money(tx.Amount)} so'm";
        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "create",
            summary, after: AuditService.Snapshot(tx), studentId: tx.StudentId, teacherId: tx.TeacherId);

        await db.SaveChangesAsync();
        return ToDto(tx, await StudentNames(), await TeacherNames());
    }

    [HttpPut("transactions/{id}")]
    public async Task<ActionResult<FinanceTransactionDto>> Update(string id, FinanceTransactionPayload p)
    {
        var tx = await db.FinanceTransactions.FindAsync(id);
        if (tx is null) return NotFound();

        var before = AuditService.Snapshot(tx);
        var changes = new List<string>();
        if (tx.Amount != p.Amount)
            changes.Add($"summa {AuditService.Money(tx.Amount)} → {AuditService.Money(p.Amount)} so'm");
        if (tx.Date != p.Date) changes.Add($"sana {tx.Date} → {p.Date}");
        if (tx.Direction != p.Direction) changes.Add($"yo'nalish {tx.Direction} → {p.Direction}");
        if (tx.Category != p.Category) changes.Add($"toifa {tx.Category} → {p.Category}");
        if (tx.Note != p.Note) changes.Add("izoh o'zgartirildi");

        tx.Date = p.Date;
        tx.Direction = p.Direction;
        tx.Category = p.Category;
        tx.Amount = p.Amount;
        tx.Note = p.Note;
        tx.StudentId = p.StudentId;
        tx.TeacherId = p.TeacherId;

        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "update",
            changes.Count > 0 ? "Tahrirlandi: " + string.Join(", ", changes) : "Tahrirlandi",
            before: before, after: AuditService.Snapshot(tx),
            studentId: tx.StudentId, teacherId: tx.TeacherId);

        await db.SaveChangesAsync();
        return ToDto(tx, await StudentNames(), await TeacherNames());
    }

    /// <summary>O'qituvchilarga berilgan maoshlar hisoboti (davr bo'yicha): oylik, kerakli
    /// (oylik × davr oylari), berilgan va qoldiq.</summary>
    [HttpGet("salary-report")]
    public async Task<ActionResult<IEnumerable<SalaryReportRowDto>>> SalaryReport(
        [FromQuery] string? from, [FromQuery] string? to)
    {
        var fromMonth = string.IsNullOrEmpty(from) ? $"{DateTime.Now.Year:D4}-01" : from[..7];
        var toMonth = string.IsNullOrEmpty(to) ? TuitionService.CurrentMonth() : to[..7];

        var paid = await db.FinanceTransactions
            .Where(t => t.Direction == "expense" && t.Category == "salary" && t.TeacherId != null)
            .Where(t => string.Compare(t.Date, $"{fromMonth}-01") >= 0
                        && string.Compare(t.Date, $"{toMonth}-31") <= 0)
            .ToListAsync();

        var teachers = await db.Teachers.OrderBy(t => t.FullName).ToListAsync();
        return teachers.Select(te =>
        {
            // Oylik faqat o'qituvchi boshlagan oydan hisoblanadi (avvalgi oylar uchun qarz yozilmaydi).
            var startMonth = !string.IsNullOrEmpty(te.SalaryStartMonth)
                             && string.CompareOrdinal(te.SalaryStartMonth, fromMonth) > 0
                ? te.SalaryStartMonth
                : fromMonth;
            var months = string.CompareOrdinal(startMonth, toMonth) > 0
                ? 0
                : TuitionService.MonthRange(startMonth, toMonth).Count();

            var rows = paid.Where(t => t.TeacherId == te.Id
                                       && string.Compare(t.Date, $"{startMonth}-01") >= 0).ToList();
            var totalPaid = rows.Sum(r => r.Amount);
            var expected = te.Salary * months;
            return new SalaryReportRowDto(
                te.Id, te.FullName, te.Salary, totalPaid, rows.Count,
                months, expected, expected - totalPaid);
        }).ToList();
    }

    /// <summary>O'quvchilar bo'yicha to'lov hisoboti (joriy holat):
    /// Hisoblangan (to'liq sinf narxi yig'indi), Chegirma (berilgan), To'langan (HAQIQIY naqd to'lov),
    /// Qarz va Avans. Eng katta qarzdorlar yuqorida.</summary>
    [HttpGet("student-report")]
    public async Task<ActionResult<IEnumerable<StudentFinanceRowDto>>> StudentReport()
    {
        var students = await db.Students.ToListAsync();
        var charges = await db.MonthlyCharges.ToListAsync();
        var chargedByStudent = charges.GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var discountByStudent = charges.GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Discount));
        // Haqiqiy naqd to'lov — chegirma TA'SIR QILMAYDI, har doim turg'un.
        var paidByStudent = (await db.FinanceTransactions
                .Where(t => t.Direction == "income" && t.Category == "tuition" && t.StudentId != null)
                .ToListAsync())
            .GroupBy(t => t.StudentId!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        return students.Select(s =>
        {
            var charged = chargedByStudent.GetValueOrDefault(s.Id, 0m);
            var discount = discountByStudent.GetValueOrDefault(s.Id, 0m);
            var paid = paidByStudent.GetValueOrDefault(s.Id, 0m);
            var debt = s.Balance < 0 ? -s.Balance : 0m;
            var advance = s.Balance > 0 ? s.Balance : 0m;
            return new StudentFinanceRowDto(
                s.Id, s.FullName, s.ClassName, charged, discount, paid, debt, advance,
                s.DiscountPct, s.DiscountAmount);
        }).OrderByDescending(r => r.Debt).ThenBy(r => r.FullName).ToList();
    }

    [HttpDelete("transactions/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var tx = await db.FinanceTransactions.FindAsync(id);
        if (tx is null) return NotFound();

        var dir = tx.Direction == "income" ? "Kirim" : "Chiqim";
        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "delete",
            $"O'chirildi: {dir} {tx.Category} — {AuditService.Money(tx.Amount)} so'm",
            before: AuditService.Snapshot(tx), studentId: tx.StudentId, teacherId: tx.TeacherId);

        db.FinanceTransactions.Remove(tx);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Tanlangan davr bo'yicha umumiy moliyaviy xulosa.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<FinanceSummaryDto>> Summary([FromQuery] string? from, [FromQuery] string? to)
    {
        var query = db.FinanceTransactions.AsQueryable();
        if (!string.IsNullOrEmpty(from)) query = query.Where(t => string.Compare(t.Date, from) >= 0);
        if (!string.IsNullOrEmpty(to)) query = query.Where(t => string.Compare(t.Date, to) <= 0);
        var txs = await query.ToListAsync();

        var income = txs.Where(t => t.Direction == "income").ToList();
        var expense = txs.Where(t => t.Direction == "expense").ToList();

        var totalIncome = income.Sum(t => t.Amount);
        var totalExpense = expense.Sum(t => t.Amount);
        var tuition = income.Where(t => t.Category == "tuition").Sum(t => t.Amount);

        var incomeByCat = income.GroupBy(t => t.Category)
            .Select(g => new CategoryAmountDto(g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(c => c.Amount).ToList();
        var expenseByCat = expense.GroupBy(t => t.Category)
            .Select(g => new CategoryAmountDto(g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(c => c.Amount).ToList();

        var balances = await db.Students.Select(s => s.Balance).ToListAsync();
        var studentDebt = balances.Where(b => b < 0).Sum(b => -b);
        var studentAdvance = balances.Where(b => b > 0).Sum();

        return new FinanceSummaryDto(
            totalIncome, totalExpense, totalIncome - totalExpense,
            tuition, totalIncome - tuition,
            incomeByCat, expenseByCat,
            studentDebt, studentAdvance, txs.Count);
    }

    /// <summary>Oylik to'lovni qo'lda hisoblash. month berilmasa — hisoblanmagan barcha oylar.</summary>
    [HttpPost("accrue")]
    public async Task<ActionResult<AccrueResultDto>> Accrue([FromQuery] string? month)
    {
        if (!string.IsNullOrEmpty(month))
        {
            var (count, total) = await TuitionService.AccrueMonth(db, month);
            return new AccrueResultDto([month], count, total);
        }

        var accrued = await TuitionService.AccrueDue(db);
        var sum = accrued.Count == 0
            ? 0m
            : await db.MonthlyCharges.Where(c => accrued.Contains(c.Month)).SumAsync(c => c.Amount);
        var cnt = accrued.Count == 0
            ? 0
            : await db.MonthlyCharges.CountAsync(c => accrued.Contains(c.Month));
        return new AccrueResultDto(accrued, cnt, sum);
    }

    /// <summary>Bir yil bo'yicha oylik kirim/chiqim (grafik uchun, 12 oy).</summary>
    [HttpGet("monthly")]
    public async Task<ActionResult<IEnumerable<FinanceMonthlyDto>>> Monthly([FromQuery] int? year)
    {
        var y = year ?? DateTime.Now.Year;
        var prefix = y.ToString("D4") + "-";
        var txs = await db.FinanceTransactions
            .Where(t => t.Date.StartsWith(prefix))
            .ToListAsync();

        var result = new List<FinanceMonthlyDto>();
        for (var m = 1; m <= 12; m++)
        {
            var month = $"{y:D4}-{m:D2}";
            var monthTxs = txs.Where(t => t.Date.StartsWith(month)).ToList();
            result.Add(new FinanceMonthlyDto(
                month,
                monthTxs.Where(t => t.Direction == "income").Sum(t => t.Amount),
                monthTxs.Where(t => t.Direction == "expense").Sum(t => t.Amount)));
        }
        return result;
    }
}
