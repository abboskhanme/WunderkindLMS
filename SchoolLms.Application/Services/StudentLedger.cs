using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'quvchi to'lov tarixi (ledger): oylar bo'yicha hisoblangan/chegirma/to'langan holat va to'lovlar ro'yxati.
/// Admin moliya bo'limi ham, o'quvchi (oila) ilovasi ham shu yagona mantiqdan foydalanadi.
///
/// MODEL: MonthlyCharge.Amount — to'liq oylik (sinf narxi), MonthlyCharge.Discount — chegirma.
/// Haqiqiy to'lash kerak bo'lgan summa har oy uchun: effective = Amount − Discount.
/// "Paid" — sof naqd to'lovlar (FinanceTransaction[income, tuition]), chegirma ta'sir qilmaydi.
/// </summary>
public static class StudentLedger
{
    public static async Task<StudentLedgerDto> BuildAsync(IAppDbContext db, Student student)
    {
        var rawFee = (await db.Classes.FirstOrDefaultAsync(c => c.Name == student.ClassName))?.MonthlyFee ?? 0m;
        // Joriy effektiv oylik (yangi oy uchun nima hisoblanadi) — chegirma ayirilgan.
        var fee = TuitionService.ChargeFor(rawFee, student.DiscountPct, student.DiscountAmount);

        var charges = await db.MonthlyCharges.Where(c => c.StudentId == student.Id)
            .OrderBy(c => c.Month).ToListAsync();
        var payments = await db.FinanceTransactions
            .Where(t => t.StudentId == student.Id && t.Direction == "income" && t.Category == "tuition")
            .OrderByDescending(t => t.Date).ToListAsync();

        var totalCharged = charges.Sum(c => c.Amount);          // to'liq narx
        var totalDiscount = charges.Sum(c => c.Discount);       // jami chegirma
        var totalPaidActual = payments.Sum(p => p.Amount);      // haqiqiy naqd

        // To'lovni oylarga taqsimlash (allokatsiya):
        //   1) Aniq oyga biriktirilgan to'lov o'sha oyning EFFEKTIV summasidan oshmagan holda yoziladi;
        //   2) qolgan pul (oysiz to'lovlar + ortgani) eng eski qarzdan boshlab (FIFO) taqsimlanadi.
        // Effektiv summa = Amount − Discount; status shunga qarab.
        var paidByMonth = payments
            .Where(p => !string.IsNullOrEmpty(p.Month))
            .GroupBy(p => p.Month!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var pool = payments.Where(p => string.IsNullOrEmpty(p.Month)).Sum(p => p.Amount);

        var alloc = new decimal[charges.Count];
        for (var i = 0; i < charges.Count; i++)
        {
            var effective = charges[i].Amount - charges[i].Discount;
            if (effective < 0) effective = 0;
            if (!paidByMonth.TryGetValue(charges[i].Month, out var explicitPaid)) continue;
            var applied = Math.Min(explicitPaid, effective);
            alloc[i] = applied;
            pool += explicitPaid - applied; // oy summasidan ortgani umumiy hovuzga qo'shiladi
        }
        for (var i = 0; i < charges.Count && pool > 0; i++)
        {
            var effective = charges[i].Amount - charges[i].Discount;
            if (effective < 0) effective = 0;
            var remaining = effective - alloc[i];
            if (remaining <= 0) continue;
            var extra = Math.Min(pool, remaining);
            alloc[i] += extra;
            pool -= extra;
        }

        var months = new List<MonthLedgerDto>();
        for (var i = 0; i < charges.Count; i++)
        {
            var c = charges[i];
            var effective = c.Amount - c.Discount;
            if (effective < 0) effective = 0;
            var paid = alloc[i];
            var remaining = effective - paid;
            if (remaining < 0) remaining = 0;
            string status;
            if (effective == 0) status = "paid";          // 100% chegirma — qarz yo'q
            else if (remaining <= 0) status = "paid";
            else if (paid > 0) status = "partial";
            else status = "unpaid";
            months.Add(new MonthLedgerDto(c.Month, c.Amount, c.Discount, paid, remaining, status));
        }

        var paymentDtos = payments.Select(t => new PaymentDto(t.Date, t.Amount, t.Note, t.Month)).ToList();

        return new StudentLedgerDto(
            Map(student), student.Balance, fee,
            totalCharged, totalDiscount, totalPaidActual,
            months, paymentDtos);
    }

    private static StudentDto Map(Student s) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, s.Balance,
        s.DiscountPct, s.DiscountAmount, s.DiscountNote);
}
