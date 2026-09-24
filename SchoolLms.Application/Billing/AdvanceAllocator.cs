using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// AVANS O'Z-O'ZIDAN YOPADI (mijoz, 2026-09-24): o'quvchi qarzidan ko'p to'lagan bo'lsa, ortgan pul yangi
/// hisob-faktura yozilganda uni avtomatik yopadi — xuddi to'lovdagi ustuvorlik bilan
/// (<see cref="PaymentService.CategoryRank"/>: eng eski oy, oy ichida O'qish → Yotoqxona → Avtobus → Ovqat → Boshqa).
///
/// <para>
/// <b>Yangi pul yozuvi YO'Q.</b> Avans allaqachon to'lov sifatida jurnalda turibdi (debet kassa/bank, kredit
/// receivable); bu yerda faqat <c>payment_allocations</c> qatorlari qo'shiladi, ya'ni jurnal ham, kassa qoldig'i
/// ham, o'quvchining sof balansi ham O'ZGARMAYDI — faqat "qarz" va "avans" ustunlari to'g'ri joyiga tushadi.
/// </para>
/// <para>
/// <b>Qaytarilgan pul hisobga olinadi.</b> Mavjud avans = taqsimlanmagan to'lovlar − tasdiqlangan qaytarimlar
/// (<see cref="StudentBalanceQuery.AdvanceForAsync"/> bilan bir xil qoida), shuning uchun ota-onaga qaytarilgan
/// pul qarzni "yopib" qo'ymaydi. Eski to'lovlardan boshlab ishlatiladi.
/// </para>
/// <para>Chaqiruvchi OCHIQ tranzaksiya ichida chaqiradi; bu metod <c>SaveChanges</c> qiladi, commit qilmaydi.</para>
/// </summary>
public static class AdvanceAllocator
{
    private const int MoneyScale = 2;

    /// <returns>Yozilgan taqsimot qatorlari soni.</returns>
    public static async Task<int> ApplyAsync(IAppDbContext db, string studentId, CancellationToken ct = default)
    {
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.StudentId == studentId && p.ReversalOf == null)
            .Where(p => !db.Payments.Any(r => r.ReversalOf == p.Id))
            .OrderBy(p => p.ReceivedAt).ThenBy(p => p.ReceiptNo)
            .Select(p => new
            {
                p.Id,
                Unallocated = p.Amount
                    - (db.PaymentAllocations.Where(a => a.PaymentId == p.Id).Sum(a => (decimal?)a.Amount) ?? 0m),
            })
            .ToListAsync(ct);

        var refunded = await db.StudentRefunds.AsNoTracking()
            .Where(r => r.StudentId == studentId && r.ReversalOf == null && r.ApprovedBy != null && r.RejectedReason == null)
            .Where(r => !db.StudentRefunds.Any(rev => rev.ReversalOf == r.Id && rev.ApprovedBy != null))
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

        var available = decimal.Round(payments.Sum(p => p.Unallocated) - refunded, MoneyScale);
        if (available <= 0m) return 0;

        var invoices = await db.Invoices
            .Where(i => i.StudentId == studentId && i.Status != InvoiceStatus.Void)
            .ToListAsync(ct);
        if (invoices.Count == 0) return 0;

        var ids = invoices.Select(i => i.Id).ToList();
        // Kuchdagi taqsimotlar: storno qilingan to'lov ham, storno qatori ham hisoblanmaydi.
        var paid = await db.PaymentAllocations.AsNoTracking()
            .Where(a => ids.Contains(a.InvoiceId))
            .Where(a => !db.Payments.Any(p => p.Id == a.PaymentId && p.ReversalOf != null))
            .Where(a => !db.Payments.Any(r => r.ReversalOf == a.PaymentId))
            .GroupBy(a => a.InvoiceId)
            .Select(g => new { g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var codes = await db.FeeCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);

        var plan = PaymentService.PlanAllocations(
            invoices.Select(i => (i.Id, i.PeriodMonth, codes.GetValueOrDefault(i.CategoryId, FeeCategoryCode.Other),
                i.Amount - i.Discount - paid.GetValueOrDefault(i.Id))),
            available);
        if (plan.Count == 0) return 0;

        // Rejadagi har bir qatorni eng eski to'lovlarning taqsimlanmagan qismidan yig'amiz.
        var sources = new Queue<(Guid Id, decimal Left)>(payments.Where(p => p.Unallocated > 0m).Select(p => (p.Id, p.Unallocated)));
        var written = 0;
        foreach (var line in plan)
        {
            var need = line.Amount;
            while (need > 0m && sources.Count > 0)
            {
                var (paymentId, left) = sources.Dequeue();
                var take = Math.Min(left, need);
                db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = paymentId, InvoiceId = line.InvoiceId, Amount = take });
                written++;
                need -= take;
                if (left - take > 0m) sources = new Queue<(Guid, decimal)>(new[] { (paymentId, left - take) }.Concat(sources));
            }
            paid[line.InvoiceId] = paid.GetValueOrDefault(line.InvoiceId) + line.Amount - need;
        }

        foreach (var invoice in invoices.Where(i => plan.Any(l => l.InvoiceId == i.Id)))
        {
            var total = paid.GetValueOrDefault(invoice.Id);
            invoice.Status = total <= 0m ? InvoiceStatus.Open
                : total >= invoice.Amount - invoice.Discount ? InvoiceStatus.Paid : InvoiceStatus.Partial;
        }

        await db.SaveChangesAsync(ct);
        return written;
    }
}
