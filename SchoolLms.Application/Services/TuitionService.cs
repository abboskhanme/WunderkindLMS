using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Oy va chegirma arifmetikasi — MAOSH hisobotlari va o'quv yili oralig'i uchun.
///
/// <para>
/// <b>P1-21: bu klass endi hech narsa YOZMAYDI.</b> Oylik hisoblash
/// (<c>AccrueMonth</c> / <c>AccrueDue</c>) va u bilan birga
/// <c>TuitionAccrualService</c> fon xizmati olib tashlandi — o'rniga toifalar
/// kesimidagi <c>IInvoiceService.AccrueDueAsync</c> +
/// <c>BillingAccrualService</c> (P1-09). Bu yerda faqat SOF funksiyalar qoldi:
/// ular <c>TeacherSalaryCalc</c>, <c>SalaryLedger</c> va maosh hisobotida
/// ishlatiladi.
/// </para>
/// <para>
/// <c>ChargeFor</c> / <c>DiscountFor</c> — chegirma arifmetikasining ESKI
/// nusxasi. Yangi kod <c>DiscountMath</c> (<c>InvoiceService.cs</c>) ni
/// chaqiradi; ikkovi bir xil natija berishini <c>BillingCatalogTests</c> va
/// <c>InvoiceServiceTests</c> har yurishda tekshiradi, ya'ni port
/// isbotlangan holda xulq-atvorni saqlaydi.
/// </para>
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
}
