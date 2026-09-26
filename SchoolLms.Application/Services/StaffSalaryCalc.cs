using System.Globalization;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'qituvchi BO'LMAGAN xodim (role="staff") maoshi — docs/modules/employees-unified.md.
/// Dars jadvali ham, davomat ham yo'q: har oy belgilangan oylik. Boshlanish qoidasi
/// o'qituvchinikining aynan o'zi (<see cref="TeacherSalaryCalc.StartDateOf"/>): boshlangan
/// oydan oldingi oylar — 0, birinchi oy — boshlangan KUNidan (bu yerda kalendar kunlari
/// bo'yicha), boshlanish sanasi bo'sh — davr boshidan to'liq oy.
/// </summary>
public static class StaffSalaryCalc
{
    private const string IsoDate = "yyyy-MM-dd";

    /// <summary>Xodimning maosh boshlanish sanasi (<c>yyyy-MM-dd</c>) yoki belgilanmagan bo'lsa null.</summary>
    public static string? StartDateOf(AppUser u) => StartDateOf(u.SalaryStartDate);

    /// <inheritdoc cref="StartDateOf(AppUser)"/>
    public static string? StartDateOf(string? salaryStartDate) =>
        string.IsNullOrWhiteSpace(salaryStartDate) ? null : salaryStartDate;

    /// <summary>Qiymat aniq <c>yyyy-MM-dd</c> ko'rinishidagi haqiqiy sanami.</summary>
    public static bool IsIsoDate(string value) =>
        DateOnly.TryParseExact(value, IsoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>
    /// Davrdagi birinchi hisoblanadigan oy: boshlangan oy va davr boshi (<paramref name="fromMonth"/>)
    /// ning kattasi — maosh hisoboti va daftarda o'qituvchi bilan bir xil.
    /// </summary>
    public static string FirstMonth(string? startDate, string fromMonth)
    {
        var startMonth = startDate is { Length: >= 7 } ? startDate[..7] : fromMonth;
        return string.CompareOrdinal(startMonth, fromMonth) > 0 ? startMonth : fromMonth;
    }

    /// <summary>
    /// Bitta oy (<c>yyyy-MM</c>) uchun kerakli maosh: boshlanishdan oldin 0; boshlangan oyda
    /// <c>oylik × ishlagan kunlar / oydagi kunlar</c> (2 kasrgacha, yarmi yuqoriga); keyin to'liq oylik.
    /// </summary>
    public static decimal MonthlyForMonth(decimal salary, string month, string? startDate)
    {
        if (salary <= 0 || month.Length < 7) return 0m;
        if (startDate is not { Length: >= 7 }) return salary;

        var order = string.CompareOrdinal(month, startDate[..7]);
        if (order < 0) return 0m;
        if (order > 0) return salary;

        if (!DateOnly.TryParseExact(startDate, IsoDate, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var start))
            return salary;

        var days = DateTime.DaysInMonth(start.Year, start.Month);
        var worked = days - start.Day + 1;
        return Math.Round(salary * worked / days, 2, MidpointRounding.AwayFromZero);
    }
}
