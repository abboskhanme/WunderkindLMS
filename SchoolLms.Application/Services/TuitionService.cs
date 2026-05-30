using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Oylik to'lovlarni hisoblash (accrual). Har oy har bir o'quvchiga sinf oylik
/// to'lovi miqdorida qarz yoziladi: balans kamayadi va MonthlyCharge yozuvi yaratiladi.
/// To'lanmagan oylar balansda jamlanib boradi.
/// </summary>
public static class TuitionService
{
    public static string CurrentMonth() => DateTime.Now.ToString("yyyy-MM");

    /// <summary>
    /// Sinf oylik to'lovidan o'quvchining chegirmasini ayirib, hisoblanishi kerak bo'lgan
    /// summa. Avval foiz olib tashlanadi (<paramref name="discountPct"/>, 0..100), keyin aniq
    /// summa (<paramref name="discountAmount"/>) ayriladi. Manfiy chiqsa — 0 qaytadi.
    /// </summary>
    public static decimal ChargeFor(decimal fee, int discountPct, decimal discountAmount)
    {
        if (fee <= 0) return 0m;
        var pct = Math.Clamp(discountPct, 0, 100);
        var amount = Math.Max(0m, discountAmount);
        var afterPct = fee * (100 - pct) / 100m;
        var charge = afterPct - amount;
        if (charge < 0m) charge = 0m;
        return decimal.Round(charge, 2);
    }

    /// <summary>O'quvchining shu paytdagi haqiqiy oylik to'lovi (sinf narxi minus chegirma).
    /// Sinf topilmasa yoki narx 0 bo'lsa — 0 qaytadi.</summary>
    public static decimal ChargeFor(Student s, IDictionary<string, decimal> feeByClassName) =>
        feeByClassName.TryGetValue(s.ClassName, out var fee)
            ? ChargeFor(fee, s.DiscountPct, s.DiscountAmount)
            : 0m;

    /// <summary>Berilgan oylik to'lovga qo'yiladigan chegirma summasi (fee − effective).
    /// Chegirma fee dan oshmaydi.</summary>
    public static decimal DiscountFor(decimal fee, int discountPct, decimal discountAmount)
    {
        if (fee <= 0) return 0m;
        var effective = ChargeFor(fee, discountPct, discountAmount);
        return decimal.Round(fee - effective, 2);
    }

    /// <summary>"yyyy-MM" -> keyingi oy "yyyy-MM".</summary>
    public static string NextMonth(string month)
    {
        var year = int.Parse(month[..4]);
        var m = int.Parse(month[5..]);
        if (m == 12) { year++; m = 1; } else { m++; }
        return $"{year:D4}-{m:D2}";
    }

    /// <summary>fromMonth..toMonth (inklyuziv) oralig'idagi oylar ("yyyy-MM"). from > to bo'lsa — bo'sh.</summary>
    public static IEnumerable<string> MonthRange(string fromMonth, string toMonth)
    {
        if (string.IsNullOrEmpty(fromMonth) || string.IsNullOrEmpty(toMonth)) yield break;
        var m = fromMonth;
        while (string.CompareOrdinal(m, toMonth) <= 0)
        {
            yield return m;
            m = NextMonth(m);
        }
    }

    /// <summary>Bitta oy uchun hisoblash. Allaqachon hisoblangan o'quvchilar o'tkazib yuboriladi.</summary>
    public static async Task<(int Count, decimal Total)> AccrueMonth(IAppDbContext db, string month)
    {
        var fees = await db.Classes.ToDictionaryAsync(c => c.Name, c => c.MonthlyFee);
        var already = (await db.MonthlyCharges.Where(c => c.Month == month)
            .Select(c => c.StudentId).ToListAsync()).ToHashSet();
        // Arxivlangan o'quvchilarga oylik hisoblanmaydi.
        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync();

        var count = 0;
        decimal total = 0;
        foreach (var s in students)
        {
            if (already.Contains(s.Id)) continue;
            // O'quvchi shu oydan oldin kelmagan bo'lsa, hisoblamaymiz.
            if (!string.IsNullOrEmpty(s.EnrollmentDate) && string.CompareOrdinal(s.EnrollmentDate[..7], month) > 0) continue;
            if (!fees.TryGetValue(s.ClassName, out var fee) || fee <= 0) continue;
            var discount = DiscountFor(fee, s.DiscountPct, s.DiscountAmount);
            var effective = fee - discount;
            // Amount = to'liq sinf narxi; Discount = chegirma; balans faqat effective uchun kamayadi.
            // Effective = 0 (100% chegirma) holatida ham yozuv qoldiriladi — hisobotda ko'rinsin.
            db.MonthlyCharges.Add(new MonthlyCharge
            {
                StudentId = s.Id,
                Month = month,
                Amount = fee,
                Discount = discount,
                Date = $"{month}-01",
            });
            s.Balance -= effective;
            count++;
            total += effective;
        }

        if (count > 0) await db.SaveChangesAsync();
        return (count, total);
    }

    /// <summary>Oxirgi hisoblangan oydan joriy oygacha bo'lgan barcha oylarni hisoblaydi.</summary>
    public static async Task<List<string>> AccrueDue(IAppDbContext db)
    {
        var cur = CurrentMonth();
        var months = await db.MonthlyCharges.Select(c => c.Month).Distinct().ToListAsync();
        var accrued = new List<string>();

        if (months.Count == 0)
        {
            await AccrueMonth(db, cur);
            accrued.Add(cur);
            return accrued;
        }

        var next = NextMonth(months.Max()!);
        while (string.CompareOrdinal(next, cur) <= 0)
        {
            await AccrueMonth(db, next);
            accrued.Add(next);
            next = NextMonth(next);
        }
        return accrued;
    }
}
