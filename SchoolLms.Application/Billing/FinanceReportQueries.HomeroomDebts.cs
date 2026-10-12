using Microsoft.EntityFrameworkCore;

namespace SchoolLms.Application.Billing;

public sealed partial class FinanceReportQueries
{
    /// <summary>
    /// Each pupil's unpaid invoice months and what is still owed for each — the same billable invoices
    /// and effective allocations as <see cref="DebtorsAsync"/>, so the months add up to that report's
    /// debt for a pupil without advances. For the homeroom teacher's list (2026-10-12).
    /// </summary>
    public async Task<Dictionary<string, List<(DateOnly Month, decimal Amount)>>> UnpaidMonthsAsync(
        IReadOnlyCollection<string> studentIds, CancellationToken ct = default)
    {
        if (studentIds.Count == 0) return new(StringComparer.Ordinal);

        var effective = EffectiveAllocations();
        var rows = await (
            from inv in BillableInvoices()
            where studentIds.Contains(inv.StudentId)
            let paid = effective.Where(a => a.InvoiceId == inv.Id).Sum(a => a.Amount)
            where inv.Amount - inv.Discount > paid
            group new { Owed = inv.Amount - inv.Discount - paid } by new { inv.StudentId, inv.PeriodMonth }
            into g
            select new { g.Key.StudentId, g.Key.PeriodMonth, Amount = g.Sum(x => x.Owed) })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.StudentId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.PeriodMonth).Select(r => (r.PeriodMonth, r.Amount)).ToList(),
                StringComparer.Ordinal);
    }
}
