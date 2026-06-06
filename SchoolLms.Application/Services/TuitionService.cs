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
    public static string CurrentMonth() => AppClock.Now.ToString("yyyy-MM");

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

    /// <summary>
    /// O'quv yili boshlanish oyi ("yyyy-MM") — choraklardagi ENG ERTA StartDate'ning oyi
    /// (1-chorak qaysi oydan boshlangan bo'lsa). Choraklar belgilanmagan bo'lsa — joriy yil yanvari.
    /// Maosh hisobi yanvardan emas, shu oydan boshlanadi.
    /// </summary>
    public static async Task<string> AcademicYearStartMonthAsync(IAppDbContext db)
    {
        var starts = await db.Quarters
            .Where(q => q.StartDate.Length >= 7)
            .Select(q => q.StartDate).ToListAsync();
        return starts.Count == 0 ? $"{AppClock.Now.Year:D4}-01" : starts.Min()![..7];
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

    /// <summary>
    /// Hisoblanishi kerak bo'lgan BARCHA oylarni (eng erta o'quvchi kelgan oydan / o'quv yili
    /// boshidan — qaysi biri ertaroq — joriy oygacha) to'ldiradi. Har oy uchun
    /// <see cref="AccrueMonth"/> chaqiriladi: u idempotent (allaqachon hisoblangan o'quvchini
    /// o'tkazib yuboradi) va har o'quvchini faqat o'z EnrollmentDate'idan boshlab hisoblaydi.
    /// Shu sabab: import/seed orqali qo'shilgan, hali hisoblanmagan o'quvchilar ham tutiladi,
    /// va oraliqdagi "tushib qolgan" oylar to'ldiriladi (avvalgi xulq faqat oxirgi oydan
    /// keyingi oylarni qo'shardi — yangi/eski o'quvchilar 0 bo'lib qolardi).
    /// </summary>
    public static async Task<List<string>> AccrueDue(IAppDbContext db)
    {
        var cur = CurrentMonth();
        var start = await AcademicYearStartMonthAsync(db);

        // Faol o'quvchilarning eng erta kelgan oyi (o'quv yili boshidan oldin kelgan bo'lsa,
        // o'sha oydan boshlab). AccrueMonth har o'quvchini o'z enrollment'idan tekshiradi.
        var enrolls = await db.Students
            .Where(s => !s.IsArchived && s.EnrollmentDate != null && s.EnrollmentDate.Length >= 7)
            .Select(s => s.EnrollmentDate).ToListAsync();
        if (enrolls.Count > 0)
        {
            var minEnroll = enrolls.Min()![..7];
            if (string.CompareOrdinal(minEnroll, start) < 0) start = minEnroll;
        }

        if (string.CompareOrdinal(start, cur) > 0) start = cur; // o'quv yili hali boshlanmagan bo'lsa

        var accrued = new List<string>();
        foreach (var month in MonthRange(start, cur))
        {
            var (count, _) = await AccrueMonth(db, month);
            if (count > 0) accrued.Add(month);
        }
        return accrued;
    }
}
