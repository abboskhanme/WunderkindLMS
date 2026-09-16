using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'quvchining oylar bo'yicha to'lov tarixi: hisoblangan / chegirma / to'langan /
/// qoldiq va to'lovlar ro'yxati. Admin kartochkasi ham, o'quvchi (oila) ilovasi ham
/// shu yagona mantiqdan foydalanadi.
///
/// <para>
/// <b>P1-21 da qayta yozildi.</b> Ilgari manba <c>monthly_charges</c> +
/// <c>finance_transactions</c> edi va to'lovni oylarga ILOVA taqsimlardi (FIFO
/// taxmini). Endi taqsimot bazadagi haqiqiy qator: <c>payment_allocations</c>.
/// Ya'ni bu yerda endi taxmin yo'q — kassir qaysi oyga qancha yo'naltirgan
/// bo'lsa, ekranda ham o'sha ko'rinadi (SPEC §3.7).
/// </para>
///
/// <para>
/// <b>Toifalar bu ko'rinishda yig'ib beriladi.</b> Bitta oy = o'sha oyning
/// o'qish + avtobus + yotoqxona + ovqat hisob-fakturalari yig'indisi. Toifa
/// kesimidagi batafsil ko'rinish alohida ekranda —
/// <c>GET /api/student/billing</c> (<c>InvoiceService.ForStudentAsync</c>,
/// P1-19). Bu yerda uni takrorlash ikkita "qarz" raqamini keltirib chiqarardi.
/// </para>
///
/// <para>
/// <b>Unumdorlik:</b> to'rtta so'rov, o'quvchining oylari soniga bog'liq emas.
/// Sikl ichida <c>await</c> yo'q.
/// </para>
/// </summary>
public static class StudentLedger
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — hisob-kitob ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    public static async Task<StudentLedgerDto> BuildAsync(IAppDbContext db, Student student)
    {
        ArgumentNullException.ThrowIfNull(student);

        // ---- 1. Hisob-fakturalar (bekor qilinganlar KIRMAYDI) ----
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == student.Id && i.Status != InvoiceStatus.Void)
            .Select(i => new { i.Id, i.PeriodMonth, i.Amount, i.Discount })
            .ToListAsync();

        var invoiceIds = invoices.Select(i => i.Id).ToList();

        // ---- 2. Kuchdagi taqsimotlar, hisob-faktura kesimida ----
        // "Kuchda" = storno qatorining o'zi ham, storno qilingan asl to'lov ham
        // chiqarib tashlangan (FinanceReportQueries dagi bilan AYNAN bir xil
        // qoida). Aks holda bekor qilingan pul "to'langan" bo'lib qolardi.
        var paidByInvoice = invoiceIds.Count == 0
            ? []
            : (await db.PaymentAllocations.AsNoTracking()
                .Where(a => invoiceIds.Contains(a.InvoiceId))
                .Where(a => !db.Payments.Any(p => p.Id == a.PaymentId && p.ReversalOf != null))
                .Where(a => !db.Payments.Any(r => r.ReversalOf == a.PaymentId))
                .GroupBy(a => a.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, Paid = g.Sum(x => x.Amount) })
                .ToListAsync())
            .ToDictionary(x => x.InvoiceId, x => x.Paid);

        // ---- 3. To'lovlar ro'yxati (kassa cheklari) ----
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.StudentId == student.Id)
            .OrderByDescending(p => p.ReceivedAt)
            .Select(p => new
            {
                p.Id,
                p.Amount,
                p.Note,
                p.ReceivedAt,
                p.ReversalOf,
                Reversed = db.Payments.Any(r => r.ReversalOf == p.Id),
            })
            .ToListAsync();

        // Har to'lov qaysi oy(lar)ga tushgani — chek qatorida ko'rsatiladi.
        var paymentIds = payments.Select(p => p.Id).ToList();
        var monthsByPayment = paymentIds.Count == 0
            ? []
            : (await (
                    from a in db.PaymentAllocations.AsNoTracking()
                    join inv in db.Invoices.AsNoTracking() on a.InvoiceId equals inv.Id
                    where paymentIds.Contains(a.PaymentId)
                    select new { a.PaymentId, inv.PeriodMonth })
                .Distinct()
                .ToListAsync())
            .GroupBy(x => x.PaymentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PeriodMonth).OrderBy(m => m).ToList());

        // ---- 4. Oylar kesimi ----
        var months = invoices
            .GroupBy(i => i.PeriodMonth)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var charged = g.Sum(i => i.Amount);
                var discount = g.Sum(i => i.Discount);
                // Har hisob-faktura alohida: ortiqcha to'langan toifa qo'shni
                // toifaning qarzini yopib ko'rsatmasin (ota-ona portali ham
                // aynan shunday hisoblaydi).
                var payable = g.Sum(i => i.Amount - i.Discount);
                var paid = g.Sum(i => Math.Min(
                    i.Amount - i.Discount, paidByInvoice.GetValueOrDefault(i.Id)));
                var remaining = payable - paid;
                if (remaining < 0m) remaining = 0m;

                var status = payable == 0m || remaining == 0m ? "paid"
                    : paid > 0m ? "partial"
                    : "unpaid";

                return new MonthLedgerDto(
                    g.Key.ToString("yyyy-MM"),
                    decimal.Round(charged, MoneyScale),
                    decimal.Round(discount, MoneyScale),
                    decimal.Round(paid, MoneyScale),
                    decimal.Round(remaining, MoneyScale),
                    status);
            })
            .ToList();

        // ---- 5. Jamlar ----
        var totalCharged = months.Sum(m => m.Charged);
        var totalDiscount = months.Sum(m => m.Discount);
        // Jami to'langan — HAQIQATAN kassaga tushgan pul (storno chiqarilgan),
        // taqsimlanmagan qoldiq ham shu yerda: ota-ona to'lagan summani ko'radi.
        var totalPaid = payments
            .Where(p => p.ReversalOf is null && !p.Reversed)
            .Sum(p => p.Amount);

        // Joriy effektiv oylik = eng oxirgi hisoblangan oyning to'lash kerak
        // bo'lgan summasi. Obunadan qayta hisoblamaymiz: chegirma arifmetikasi
        // ikkinchi nusxaga ega bo'lib qolardi (`DiscountMath` — yagona nusxa).
        var monthlyFee = invoices.Count == 0
            ? 0m
            : invoices.GroupBy(i => i.PeriodMonth).OrderByDescending(g => g.Key)
                .First().Sum(i => i.Amount - i.Discount);

        var balance = await new StudentBalanceQuery(db).ForAsync(student.Id);

        // Storno ikki tomonlama ko'rinadi: bekor QILUVCHI qator ham, bekor
        // QILINGAN asl to'lov ham. Ikkovi ham ro'yxatda QOLADI (SPEC §4.1: to'lov
        // o'chirilmaydi), lekin ikkovi ham `totalPaid` ga kirmaydi — quyidagi
        // yig'indi shuni ta'minlaydi. Ekran ularni bayroq bo'yicha chizadi.
        var paymentDtos = payments.Select(p =>
        {
            var applied = monthsByPayment.GetValueOrDefault(p.Id) ?? [];
            return new PaymentDto(
                AppClock.LocalDateOf(p.ReceivedAt).ToString("yyyy-MM-dd"),
                p.Amount,
                p.Note,
                // Bir necha oyga bo'lingan to'lovda oy ustuni bo'sh qoladi —
                // bitta katakka ikki oyni yozish yolg'on bo'lardi.
                applied.Count == 1 ? applied[0].ToString("yyyy-MM") : null,
                IsReversal: p.ReversalOf is not null,
                Reversed: p.Reversed);
        }).ToList();

        return new StudentLedgerDto(
            Map(student, balance),
            decimal.Round(balance, MoneyScale),
            decimal.Round(monthlyFee, MoneyScale),
            decimal.Round(totalCharged, MoneyScale),
            decimal.Round(totalDiscount, MoneyScale),
            decimal.Round(totalPaid, MoneyScale),
            months, paymentDtos);
    }

    private static StudentDto Map(Student s, decimal balance) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, balance);
}
