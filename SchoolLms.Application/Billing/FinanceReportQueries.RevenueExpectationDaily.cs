using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  F6.04 — KUNLIK DINAMIKA
//  (docs/modules/finance-parity.md §2.6.3 — "dynamics" tabi, EduSchool'da
//  `GET reports/pnl/expectation/daily`. FAQAT O'QISH.)
// ===========================================================================
//
//  BITTA MANBA — `ledger_entries`, <see cref="FinanceReportQueries.ProfitLossAsync"/>
//  BILAN AYNAN BIR XIL HISOB PREFIKSLARI
//  ----------------------------------------------------------------------------
//  Kunlik daromad/chiqim — <c>revenue:*</c>/<c>expense:*</c> hisoblarining
//  KUN kesimidagi debet/kredit ayirmasi, xuddi <c>ProfitLossAsync</c> BUTUN
//  DAVR uchun qiladiganidek (shu klassdagi <c>Net(...)</c> yordamchisi AYNAN
//  o'sha, faqat kesim — kun, oy emas). Shuning uchun bir oyning barcha
//  kunlari yig'indisi <c>ProfitLossAsync(oy)</c> bilan AYNAN teng bo'lishi
//  SHART — pastdagi test buni tekshiradi (vazifa: "figures that also appear
//  ... must reconcile").
//
//  "BASHORAT" YO'Q — EduSchool'da bor, bizda YO'Q
//  ------------------------------------------------
//  §2.6.1: "daily chart with forecast and 'today' marker". Bashorat chizig'i
//  qurish uchun kamida bitta modellashtirish qarori kerak (masalan, o'tgan
//  oylar o'rtachasi) — bu HALI HECH QAYERDA yozilmagan yangi qoida bo'lardi.
//  Shuning uchun bu yerda faqat FAKT (kundalik) va "bugun" belgisi bor;
//  bashorat frontendda ham chizilmaydi (soxta panel yo'q qoidasi).

/// <summary>Bitta kunning daromad/chiqim/sof natijasi.</summary>
/// <param name="Date">Kun.</param>
/// <param name="Revenue">Shu kunning daromadi (<c>revenue:*</c>, kredit−debet).</param>
/// <param name="Expense">Shu kunning chiqimi (<c>expense:*</c>, debet−kredit).</param>
/// <param name="Net"><see cref="Revenue"/> − <see cref="Expense"/>.</param>
/// <param name="CumulativeNet">Oy boshidan shu kungacha yig'ilgan sof natija.</param>
public record DailyDynamicsDayDto(
    DateOnly Date, decimal Revenue, decimal Expense, decimal Net, decimal CumulativeNet);

/// <summary>Bir oy uchun kunlik dinamika.</summary>
/// <param name="Month">Oyning birinchi kuni.</param>
/// <param name="Days">Oyning HAR bir kuni — harakatsiz kun ham 0 bilan turadi (uzilishsiz grafik).</param>
/// <param name="Today">Bugungi kun, AGAR so'ralgan oy ichida bo'lsa; aks holda <c>null</c>
/// (frontend "bugun" chizig'ini shunga qarab chizadi yoki chizmaydi).</param>
public record DailyDynamicsDto(DateOnly Month, IReadOnlyList<DailyDynamicsDayDto> Days, DateOnly? Today);

public sealed partial class FinanceReportQueries
{
    /// <summary>Kunlik dinamika (§2.6, F6.04) — bitta oy, har kuni bitta qator.</summary>
    public async Task<DailyDynamicsDto> RevenueExpectationDailyAsync(
        DateOnly month, CancellationToken ct = default)
    {
        var periodMonth = FirstDayOfMonth(month);
        var monthEnd = periodMonth.AddMonths(1).AddDays(-1);
        var dayCount = monthEnd.DayNumber - periodMonth.DayNumber + 1;

        // Bitta so'rov: kun × hisob × yo'nalish kesimida yig'indi — bazada
        // guruhlangan holda keladi (oy ichida ko'pi bilan 31 × 10 × 2 = 620 qator).
        var grouped = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate >= periodMonth && e.EntryDate <= monthEnd)
            .GroupBy(e => new { e.EntryDate, e.Account, e.Direction })
            .Select(g => new { g.Key.EntryDate, g.Key.Account, g.Key.Direction, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var byDay = grouped.ToLookup(r => r.EntryDate);
        var today = AppClock.Today;

        var days = new List<DailyDynamicsDayDto>(dayCount);
        var cumulative = 0m;
        var date = periodMonth;
        for (var i = 0; i < dayCount; i++)
        {
            var dayRows = byDay[date]
                .Select(r => new AccountTotal(r.Account, r.Direction, r.Total))
                .ToList();

            // AYNAN `ProfitLossAsync`/`Lines` ning ishorasi: daromadda
            // kredit−debet (debitPositive: false), chiqimda debet−kredit
            // (debitPositive: true) — ikkinchi ta'rif emas, o'sha yordamchi.
            var revenue = Net(dayRows.Where(r => r.Account.StartsWith(RevenuePrefix, StringComparison.Ordinal)), debitPositive: false);
            var expense = Net(dayRows.Where(r => r.Account.StartsWith(ExpensePrefix, StringComparison.Ordinal)), debitPositive: true);
            var net = revenue - expense;
            cumulative += net;

            days.Add(new DailyDynamicsDayDto(date, revenue, expense, net, cumulative));
            date = date.AddDays(1);
        }

        var todayMarker = today >= periodMonth && today <= monthEnd ? today : (DateOnly?)null;
        return new DailyDynamicsDto(periodMonth, days, todayMarker);
    }
}
