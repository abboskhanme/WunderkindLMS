using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>
/// Finance — every tool requires <c>finance</c> or <c>finance:view</c> (admin/superadmin always).
/// All of them reuse the read-only billing queries the Moliya screens use.
/// </summary>
[McpServerToolType]
public sealed class FinanceTools(McpToolContext t)
{
    [McpServerTool(Name = "finance_debtors", Title = "Qarzdorlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Qarzdor o'quvchilar: qarz summasi, eng eski to'lanmagan oy, necha kun kechikkan, toifalar kesimi, ota-ona telefoni. "
        + "Eng katta qarz birinchi. Debtors list with amounts and days overdue. (Moliya ruxsati kerak.)")]
    public async Task<string> Debtors(
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        [Description("Kamida shuncha qarz, so'm")] decimal? minDebt = null,
        [Description("Faqat muddati o'tganlar")] bool onlyOverdue = false,
        [Description("Arxivdagi (ketgan) o'quvchilar ham (sukut true)")] bool includeArchived = true,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Debtors);
        var rows = await new FinanceReportQueries(t.Db).DebtorsAsync(
            new DebtorReportQuery(className, Math.Max(0.01m, minDebt ?? 0.01m), onlyOverdue, includeArchived), ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = rows.OrderByDescending(r => r.Debt).Skip((p - 1) * size).Take(size).Select(r => new
        {
            r.StudentId, r.FullName, r.ClassName, r.ParentPhone, r.Debt, r.OldestUnpaidMonth, r.DaysOverdue,
            byCategory = r.ByCategory.Select(c => new { c.CategoryName, c.Debt }),
        }).ToList();
        return t.Json(new { total = rows.Count, totalDebt = rows.Sum(r => r.Debt), page = p, pageSize = size, items }, items.Count);
    }

    [McpServerTool(Name = "finance_arrears_pivot", Title = "Oylar kesimida qarzdorlik",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Oylar kesimidagi hisoblangan / to'langan / to'lanishi kerak summa har bir o'quvchi uchun (ko'pi bilan 12 oy). "
        + "Arrears pivot by month (billed, paid, outstanding). (Moliya ruxsati kerak.)")]
    public async Task<string> ArrearsPivot(
        [Description("Boshlanish oyi YYYY-MM")] string fromMonth,
        [Description("Tugash oyi YYYY-MM")] string toMonth,
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        [Description("Faqat qarzdorlar")] bool debtorsOnly = true,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Arrears);
        var f = Month(fromMonth, "fromMonth");
        var to = Month(toMonth, "toMonth");
        if (f > to || (to.Year - f.Year) * 12 + to.Month - f.Month >= 12)
            throw new McpException("Oylar oralig'i to'g'ri va ko'pi bilan 12 oy bo'lsin.");
        ArrearsPivotDto pivot;
        try
        {
            pivot = await new FinanceReportQueries(t.Db).ArrearsPivotAsync(
                new ArrearsPivotQuery(f, to, className, DebtorsOnly: debtorsOnly), ct);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new McpException("So'rov juda katta: " + ex.Message.Split('\n')[0]);
        }
        var rows = pivot.Rows.Take(McpToolContext.MaxPageSize).Select(r => new
        {
            r.FullName, r.ClassName, r.IsArchived, total = r.Total,
            months = r.Cells.ToDictionary(c => c.Key, c => c.Value),
        }).ToList();
        return t.Json(new
        {
            months = pivot.Months, total = pivot.Total, footer = pivot.Footer,
            pupils = pivot.Rows.Count, truncated = pivot.Rows.Count > rows.Count, rows,
        }, rows.Count);
    }

    [McpServerTool(Name = "finance_transactions", Title = "Tranzaksiyalar jurnali",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Pul harakatlari jurnali: to'lovlar, chiqimlar, qaytarimlar, kassa amallari — sana, summa, kim, usul, holat. "
        + "Filtrlar: davr (ko'pi bilan 366 kun), yo'nalish (in/out), turi, sinf, holat. Yakun: kirim, chiqim, sof. "
        + "Transactions journal (payments, expenses, refunds). (Moliya ruxsati kerak.)")]
    public async Task<string> Transactions(
        [Description("Davr boshi YYYY-MM-DD (sukut — 30 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("in | out (ixtiyoriy)")] string? direction = null,
        [Description("Turi: payment, expense, refund, ... (ixtiyoriy)")] string? kind = null,
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        [Description("Holat (ixtiyoriy)")] string? status = null,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Transactions);
        var (f, tt) = McpToolContext.Range(from, to, 30);
        TransactionJournalPageDto res;
        try
        {
            res = await new TransactionJournalQuery(t.Db).PageAsync(new TransactionJournalFilter(
                From: f, To: tt, Direction: direction, Kind: kind, ClassName: className, Status: status,
                Page: McpToolContext.Page(page), PageSize: McpToolContext.PageSize(pageSize)), ct);
        }
        catch (ArgumentException ex)
        {
            throw new McpException("Filtr noto'g'ri: " + ex.Message.Split('\n')[0]);
        }
        var rows = res.Rows.Select(r => new
        {
            date = r.OccurredOn, r.Kind, r.Direction, r.Amount, r.ReceiptNo, person = r.PersonName, r.ClassName,
            category = r.CategoryLabel ?? r.Category, r.Method, by = r.ActorName, r.Status, r.Note, cashBox = r.CashBoxName,
        }).ToList();
        return t.Json(new { from = f, to = tt, total = res.Total, page = res.Page, pageSize = res.PageSize, totals = res.Totals, rows }, rows.Count);
    }

    [McpServerTool(Name = "finance_invoices", Title = "Hisob-fakturalar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'quvchilarga yozilgan oylik hisob-fakturalar: oy, toifa, summa, chegirma, to'langan, qoldiq, holat. "
        + "Filtrlar: oylar, holat, sinf, faqat muddati o'tganlar/qarzdorlar. Invoices. (Moliya ruxsati kerak.)")]
    public async Task<string> Invoices(
        [Description("Boshlanish oyi YYYY-MM (ixtiyoriy)")] string? fromMonth = null,
        [Description("Tugash oyi YYYY-MM (ixtiyoriy)")] string? toMonth = null,
        [Description("Holat (ixtiyoriy)")] string? status = null,
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        [Description("O'quvchi id (ixtiyoriy)")] string? studentId = null,
        [Description("Faqat muddati o'tganlar")] bool onlyOverdue = false,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Invoices);
        var svc = new InvoiceService(t.Db, new LedgerService(t.Db)); // ledger: write-side dependency, unused by reads
        var res = await svc.ListPageAsync(new InvoicePageQuery(
            StudentId: studentId,
            FromMonth: fromMonth is null ? null : Month(fromMonth, "fromMonth"),
            ToMonth: toMonth is null ? null : Month(toMonth, "toMonth"),
            Status: status, OnlyOverdue: onlyOverdue, ClassName: className,
            Page: McpToolContext.Page(page), PageSize: McpToolContext.PageSize(pageSize)), ct);
        var rows = res.Rows.Select(r => new
        {
            r.StudentName, className = res.ClassNames.GetValueOrDefault(r.StudentId), month = r.PeriodMonth,
            category = r.CategoryName, r.Amount, r.Discount, r.Payable, r.Paid, remaining = r.Payable - r.Paid, r.Status,
        }).ToList();
        return t.Json(new { total = res.Total, page = res.Page, pageSize = res.PageSize, totals = res.Totals, rows }, rows.Count);
    }

    [McpServerTool(Name = "finance_summary", Title = "Moliyaviy natija (P&L va pul oqimi)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Davr bo'yicha foyda-zarar (daromad va xarajat moddalari, sof natija) va pul oqimi (hisoblar bo'yicha "
        + "boshlang'ich qoldiq, kirim, chiqim, yakuniy qoldiq). Ko'pi bilan 366 kun. P&L and cash-flow summary. (Moliya ruxsati kerak.)")]
    public async Task<string> Summary(
        [Description("Davr boshi YYYY-MM-DD (sukut — joriy oy boshi)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.FinanceReports);
        var today = McpToolContext.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var (f, tt) = McpToolContext.Range(from ?? monthStart.ToString("yyyy-MM-dd"), to, 30);
        var q = new FinanceReportQueries(t.Db);
        try
        {
            var pl = await q.ProfitLossAsync(f, tt, ct);
            var cf = await q.CashFlowAsync(f, tt, ct);
            return t.Json(new
            {
                from = f, to = tt,
                profitLoss = new { revenue = pl.Revenue, pl.RevenueTotal, expense = pl.Expense, pl.ExpenseTotal, pl.Net },
                cashFlow = new
                {
                    cf.Opening, cf.Inflow, cf.Outflow, cf.Net, cf.Closing,
                    accounts = cf.Accounts.Select(a => new { a.Account, a.Opening, a.Inflow, a.Outflow, a.Net, a.Closing }),
                },
            }, pl.Revenue.Count + pl.Expense.Count);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new McpException("Davr noto'g'ri: " + ex.Message.Split('\n')[0]);
        }
    }

    [McpServerTool(Name = "finance_salary_payments", Title = "Maosh to'lovlari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'qituvchi va xodimlarga to'langan maoshlar davr bo'yicha (ko'pi bilan 366 kun): kimga, qaysi oy uchun, summa, sana; "
        + "har bir xodim bo'yicha jami. Salary payments. (Moliya ruxsati kerak.)")]
    public async Task<string> SalaryPayments(
        [Description("Davr boshi YYYY-MM-DD (sukut — 90 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Salary);
        var (f, tt) = McpToolContext.Range(from, to, 90);
        var q = new SalaryPaymentQuery(t.Db);
        var teacherRows = await q.ForAllAsync(f, tt, ct);
        var staffRows = await q.ForAllEmployeesAsync(f, tt, ct);
        // Project BEFORE materializing: app_ro may only read non-secret columns of `users`.
        var teacherNames = await t.Db.Teachers.Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var staffNames = await t.Db.Users.Where(u => u.Role == SchoolLms.Domain.Roles.Staff)
            .Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var all = teacherRows.Select(r => new { name = teacherNames.GetValueOrDefault(r.TeacherId, "—"), kind = "teacher", r.OnDate, r.Month, r.Amount })
            .Concat(staffRows.Select(r => new { name = staffNames.GetValueOrDefault(r.TeacherId, "—"), kind = "staff", r.OnDate, r.Month, r.Amount }))
            .OrderByDescending(r => r.OnDate).ToList();
        var byPerson = all.GroupBy(r => new { r.name, r.kind })
            .Select(g => new { g.Key.name, g.Key.kind, total = g.Sum(x => x.Amount), payments = g.Count() })
            .OrderByDescending(x => x.total).ToList();
        return t.Json(new
        {
            from = f, to = tt, total = all.Sum(r => r.Amount), byPerson,
            payments = all.Take(McpToolContext.MaxPageSize), truncated = all.Count > McpToolContext.MaxPageSize,
        }, all.Count);
    }

    [McpServerTool(Name = "finance_discounts", Title = "Chegirmalar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'quvchi chegirmalari: foiz yoki summa, toifa, sabab, amal qilish muddati, holat (pending/approved/rejected), kim so'ragan/tasdiqlagan. "
        + "Discounts. (Moliya ruxsati kerak.)")]
    public async Task<string> Discounts(
        [Description("pending | approved | rejected (ixtiyoriy)")] string? status = null,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Discounts);
        var svc = new DiscountService(t.Db, new AuditService(t.Db, new HttpContextAccessor())); // audit: write-side only
        IReadOnlyList<DiscountDto> list;
        try { list = await svc.ListAsync(new DiscountQuery(Status: status), ct); }
        catch (Exception ex) when (ex is ArgumentException or BillingRuleException)
        { throw new McpException("status: pending, approved yoki rejected."); }
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = list.Skip((p - 1) * size).Take(size).Select(d => new
        {
            d.StudentName, category = d.CategoryName ?? "barcha toifalar", d.Percent, d.Amount, d.Reason,
            d.StartsOn, d.EndsOn, d.Status, requestedBy = d.CreatedByName, approvedBy = d.ApprovedByName,
        }).ToList();
        return t.Json(new { total = list.Count, page = p, pageSize = size, rows }, rows.Count);
    }

    [McpServerTool(Name = "finance_expenses", Title = "Chiqimlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Maktab chiqimlari davr bo'yicha (ko'pi bilan 366 kun): sana, toifa, summa, izoh, holat, kim yozgan/tasdiqlagan, kassa; "
        + "toifalar bo'yicha jami. Expenses. (Moliya ruxsati kerak.)")]
    public async Task<string> Expenses(
        [Description("Davr boshi YYYY-MM-DD (sukut — 30 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("Toifa (ixtiyoriy)")] string? category = null,
        [Description("Holat (ixtiyoriy)")] string? status = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Expenses);
        var (f, tt) = McpToolContext.Range(from, to, 30);
        var svc = new ExpenseService(t.Db, new LedgerService(t.Db)); // ledger: write-side dependency, unused by reads
        IReadOnlyList<ExpenseDto> list;
        try { list = await svc.ListAsync(new ExpenseQuery(f, tt, category, status), ct); }
        catch (Exception ex) when (ex is ArgumentException or BillingRuleException)
        { throw new McpException("Filtr noto'g'ri: " + ex.Message.Split('\n')[0]); }
        var byCategory = list.GroupBy(e => e.Category).Select(g => new { category = g.Key, total = g.Sum(e => e.Amount), count = g.Count() })
            .OrderByDescending(x => x.total).ToList();
        var rows = list.Take(McpToolContext.MaxPageSize).Select(e => new
        {
            date = e.OnDate, e.Category, e.Amount, e.Note, e.Status, createdBy = e.CreatedByName, approvedBy = e.ApprovedByName,
            person = e.TeacherName ?? e.EmployeeName, cashBox = e.CashBoxName,
        }).ToList();
        return t.Json(new { from = f, to = tt, total = list.Count, sum = list.Sum(e => e.Amount), byCategory, rows, truncated = list.Count > rows.Count }, rows.Count);
    }

    private static DateOnly Month(string value, string name) =>
        DateOnly.TryParseExact((value ?? "").Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new McpException($"«{name}» YYYY-MM ko'rinishida bo'lsin (masalan 2026-09).");
}
