namespace SchoolLms.Application.Billing;

// ===========================================================================
//  F6.05 — YILLIK REJA/FAKT JADVALI, ENG YAXSHI/YOMON OY BILAN
//  (docs/modules/finance-parity.md §2.6.3 — "yearly" tabi, EduSchool'da
//  `GET reports/pnl/expectation/yearly`. FAQAT O'QISH.)
// ===========================================================================
//
//  IKKINCHI ARIFMETIKA YO'Q — HAR OY AYNAN `RevenueExpectationAsync`
//  --------------------------------------------------------------------
//  Vazifaning o'zi buni talab qiladi: "take them from the existing query
//  rather than deriving a second time". Shuning uchun bu yerda yangi
//  hisob-kitob YO'Q — o'n ikki oyning har biri uchun
//  <see cref="FinanceReportQueries.RevenueExpectationAsync"/> AYNAN o'zi
//  chaqiriladi va natija bitta jadvalga yig'iladi. Demak yillik jadvaldagi
//  har bir katak bitta oy tab bilan (yoki eski P&amp;L bilan, u orqali)
//  HECH QACHON kelishmay qolmaydi — ular bitta funksiyani chaqiradi.
//
//  NARX — O'N IKKITA SO'ROV KETMA-KETLIGI, ATAYLAB
//  ---------------------------------------------------
//  Bitta yillik hisobot chaqiruvi ≈ 12 × (RevenueExpectationAsync ichidagi
//  4-5 so'rov) = ko'pi bilan ~60 ketma-ket so'rov. Bu boshqa hisobotlardagi
//  "sikl ichida await yo'q" qoidasidan chetga chiqadi — ATAYLAB: yagona
//  muqobili har oy uchun arifmetikani QAYTA yozish bo'lardi (o'quvchi
//  harakati, chegirma, yig'ilgan pul — hammasi ikkinchi marta), bu esa aynan
//  fayl boshidagi "ikkinchi ta'rif" xavfi. Direktor panelida yillik jadval
//  kamdan-kam ochiladi (oylik "expectation" tabidan farqli), shuning uchun
//  to'g'rilik tezlikdan ustun qo'yildi.

/// <summary>Bitta oyning yillik jadvaldagi qatori — <c>RevenueExpectationAsync</c> dan.</summary>
/// <param name="Month">Oyning birinchi kuni.</param>
/// <param name="GrossPlan">Reja — chegirmasiz yalpi (<c>GrossExpected</c>).</param>
/// <param name="DiscountAmount">Reja — chegirma summasi.</param>
/// <param name="NetPlan">Reja — sof kutilgan daromad (<c>NetExpected</c>).</param>
/// <param name="RevenueActual">Fakt — jurnaldagi daromad (eski P&amp;L bilan AYNAN bir xil).</param>
/// <param name="ExpenseActual">Fakt — jurnaldagi chiqim.</param>
/// <param name="ProfitActual">Fakt — sof natija.</param>
/// <param name="Margin">Foyda darajasi, %. Daromad 0 bo'lsa — <c>null</c>.</param>
/// <param name="DiscountRate">Chegirma stavkasi, %. Yalpi 0 bo'lsa — <c>null</c>.</param>
public record YearlyExpectationMonthDto(
    DateOnly Month,
    decimal GrossPlan, decimal DiscountAmount, decimal NetPlan,
    decimal RevenueActual, decimal ExpenseActual, decimal ProfitActual,
    decimal? Margin, decimal? DiscountRate);

/// <summary>Yillik reja/fakt jadvali — o'n ikki oy, yakunlar va eng yaxshi/yomon oy.</summary>
/// <param name="Year">Kalendar yil.</param>
/// <param name="Months">Yanvardan dekabrgacha — o'n ikkita qator, har doim to'liq.</param>
/// <param name="NetPlanTotal">Yil bo'yicha reja yig'indisi.</param>
/// <param name="RevenueActualTotal">Yil bo'yicha fakt daromad yig'indisi.</param>
/// <param name="ExpenseActualTotal">Yil bo'yicha fakt chiqim yig'indisi.</param>
/// <param name="ProfitActualTotal">Yil bo'yicha sof natija yig'indisi.</param>
/// <param name="BestMonth">Eng katta <see cref="YearlyExpectationMonthDto.ProfitActual"/> bo'lgan oy.
/// Yil bo'yicha bitta ham harakat bo'lmasa — <c>null</c>.</param>
/// <param name="WorstMonth">Eng kichik sof natijali oy. <see cref="BestMonth"/> bilan bir xil
/// oy bo'lishi mumkin (faqat bitta oyda harakat bo'lsa).</param>
public record YearlyExpectationDto(
    int Year, IReadOnlyList<YearlyExpectationMonthDto> Months,
    decimal NetPlanTotal, decimal RevenueActualTotal, decimal ExpenseActualTotal, decimal ProfitActualTotal,
    DateOnly? BestMonth, DateOnly? WorstMonth);

public sealed partial class FinanceReportQueries
{
    /// <summary>Yillik reja/fakt (§2.6, F6.05). Har oy — <see cref="RevenueExpectationAsync"/>
    /// ning o'zi (fayl boshidagi izohga qarang — ikkinchi arifmetika yo'q).</summary>
    public async Task<YearlyExpectationDto> RevenueExpectationYearlyAsync(
        int year, CancellationToken ct = default)
    {
        var months = new List<YearlyExpectationMonthDto>(12);
        var hasActivity = false;

        for (var m = 1; m <= 12; m++)
        {
            var e = await RevenueExpectationAsync(new DateOnly(year, m, 1), ct);
            if (e.StudentsExpected != 0 || e.RevenueActual != 0m || e.ExpenseActual != 0m) hasActivity = true;

            months.Add(new YearlyExpectationMonthDto(
                e.Month, e.GrossExpected, e.DiscountAmount, e.NetExpected,
                e.RevenueActual, e.ExpenseActual, e.ProfitActual,
                e.Margin, e.DiscountRate));
        }

        DateOnly? bestMonth = null;
        DateOnly? worstMonth = null;
        if (hasActivity)
        {
            bestMonth = months.OrderByDescending(m => m.ProfitActual).ThenBy(m => m.Month).First().Month;
            worstMonth = months.OrderBy(m => m.ProfitActual).ThenBy(m => m.Month).First().Month;
        }

        return new YearlyExpectationDto(
            year, months,
            NetPlanTotal: months.Sum(m => m.NetPlan),
            RevenueActualTotal: months.Sum(m => m.RevenueActual),
            ExpenseActualTotal: months.Sum(m => m.ExpenseActual),
            ProfitActualTotal: months.Sum(m => m.ProfitActual),
            BestMonth: bestMonth,
            WorstMonth: worstMonth);
    }
}
