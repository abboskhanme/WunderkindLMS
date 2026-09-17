using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  P&L: YIL × OY MATRITSASI VA KATAKCHA ORTIDAGI JURNAL SATRLARI
//  (docs/modules/finance-parity.md §2.5 — F5.01, F5.02, F5.03.)
// ===========================================================================
//
//  YANGI ARIFMETIKA YO'Q
//  ---------------------
//  Matritsaning har bir katagi — <see cref="FinanceReportQueries.Lines"/>
//  ning O'SHA o'zi, faqat davr sifatida bitta oy berilgan. Ya'ni
//  "2026-mart, o'qish to'lovi" katagi AYNAN `ProfitLossAsync(1-mart,
//  31-mart)` dagi o'sha satrga teng — ikkinchi ta'rif paydo bo'lmaydi.
//  Buni test ham tekshiradi (katak = o'sha oyning P&L satri).
//
//  Qatorning yakuni 12 katakning yig'indisi, ustunning yakuni esa
//  qatorlarning yig'indisi — ikkovi ham SHU YERDA qo'shiladi, brauzerda
//  emas (SPEC: pulni frontend hisoblamaydi).
//
//  QOLDIQ QATORLARI (F5.02)
//  ------------------------
//  "Oy boshidagi" va "oy oxiridagi" qoldiq <see cref="FinanceReportQueries
//  .CashFlowAsync"/> dan OLINADI, qayta hisoblanmaydi: pul qoldig'ining
//  ta'rifi (davrdan oldingi hamma yozuvdan) o'sha yerda yozilgan va
//  "Pul oqimi" ekrani ham aynan shundan chizadi.
//
//  DRILL-DOWN (F5.03)
//  ------------------
//  Katakcha bosilganda ko'rinadigan satrlar — o'sha katakni HOSIL QILGAN
//  jurnal satrlarining o'zi. Shuning uchun <see cref="FinanceReportQueries
//  .LedgerLinesAsync"/> ham xuddi shu belgi qoidasini ishlatadi va
//  qaytaradigan `Total` maydoni katakning qiymatiga TENG bo'lishi shart —
//  buni test o'lchaydi.

/// <summary>P&amp;L matritsasining bitta qatori: hisob va uning 12 oyi.</summary>
/// <param name="Account">Hisob kodi (<c>revenue:*</c> yoki <c>expense:*</c>).</param>
/// <param name="Months">Yanvardan dekabrgacha — HAR DOIM 12 ta katak.</param>
/// <param name="Total">Qator yakuni = 12 katakning yig'indisi.</param>
public sealed record ProfitLossMatrixRowDto(string Account, List<decimal> Months, decimal Total);

/// <summary>
/// Yil × oy P&amp;L matritsasi (§2.5): daromad qatorlari, chiqim qatorlari,
/// sof natija va pul qoldig'i.
///
/// <para>
/// Belgilar <see cref="ProfitLossDto"/> bilan bir xil: daromad ham, chiqim
/// ham MUSBAT ko'rsatiladi (chiqim minus bilan yozilmaydi), sof natija esa
/// ularning ayirmasi.
/// </para>
/// </summary>
/// <param name="Year">Yil.</param>
/// <param name="Months">Ustunlar: "YYYY-MM", 12 ta.</param>
/// <param name="Revenue">Daromad qatorlari (yil yakuni bo'yicha kamayish tartibida).</param>
/// <param name="RevenueMonths">Har oyning daromad yakuni.</param>
/// <param name="RevenueTotal">Yilning daromad yakuni.</param>
/// <param name="Expense">Chiqim qatorlari.</param>
/// <param name="ExpenseMonths">Har oyning chiqim yakuni.</param>
/// <param name="ExpenseTotal">Yilning chiqim yakuni.</param>
/// <param name="NetMonths">Har oy uchun daromad − chiqim.</param>
/// <param name="NetTotal">Yil uchun daromad − chiqim.</param>
/// <param name="StartBalance">Oy boshidagi pul qoldig'i (<c>cash</c> + <c>bank</c>).</param>
/// <param name="EndBalance">Oy oxiridagi pul qoldig'i.</param>
/// <param name="OpeningBalance">1-yanvardagi qoldiq.</param>
/// <param name="ClosingBalance">31-dekabrdagi qoldiq.</param>
public sealed record ProfitLossMatrixDto(
    int Year,
    List<string> Months,
    List<ProfitLossMatrixRowDto> Revenue, List<decimal> RevenueMonths, decimal RevenueTotal,
    List<ProfitLossMatrixRowDto> Expense, List<decimal> ExpenseMonths, decimal ExpenseTotal,
    List<decimal> NetMonths, decimal NetTotal,
    List<decimal> StartBalance, List<decimal> EndBalance,
    decimal OpeningBalance, decimal ClosingBalance);

/// <summary>
/// Katakcha ortidagi BITTA jurnal satri (drill-down).
/// </summary>
/// <param name="EntryId">Jurnal satri id'si — ro'yxatdagi barqaror kalit.</param>
/// <param name="EntryDate">Buxgalteriya sanasi (katakning oyi shundan).</param>
/// <param name="Account">Hisob kodi.</param>
/// <param name="Direction">debit | credit.</param>
/// <param name="Amount">Jurnaldagi summa (har doim musbat).</param>
/// <param name="Signed">Katakka QANDAY kirgani: tabiiy tomonda +, qarshi tomonda −.</param>
/// <param name="Kind">payment | invoice | expense | salary | reversal.</param>
/// <param name="KindLabel">O'zbekcha nom — SERVERDAN (UI o'z lug'atini saqlamaydi).</param>
/// <param name="IsReversal">true = storno satri.</param>
/// <param name="RefId">Manba yozuv id'si (storno'da — ASL yozuvniki).</param>
/// <param name="Title">Bir qatorlik tavsif: o'quvchi ismi yoki chiqim toifasi.</param>
/// <param name="Person">O'quvchi yoki xodim — "kim" ustuni.</param>
/// <param name="ActorName">Yozuvni kiritgan xodim (kassir / chiqim muallifi).</param>
/// <param name="ReceiptNo">Chek raqami (faqat to'lov va uning storno'si).</param>
/// <param name="Method">To'lov usuli: cash | card | transfer | online.</param>
/// <param name="Memo">Jurnaldagi izoh. Storno'da — SABAB (majburiy).</param>
/// <param name="CreatedAt">Yozuv jurnalga tushgan lahza.</param>
public sealed record LedgerLineDto(
    long EntryId,
    DateOnly EntryDate,
    string Account,
    string Direction,
    decimal Amount,
    decimal Signed,
    string Kind,
    string KindLabel,
    bool IsReversal,
    Guid? RefId,
    string Title,
    string? Person,
    string? ActorName,
    long? ReceiptNo,
    string? Method,
    string? Memo,
    DateTimeOffset CreatedAt);

/// <summary>
/// Drill-down javobi: katakni hosil qilgan satrlar va ularning yig'indisi.
///
/// <para>
/// <b><see cref="Total"/> — katakning O'ZI.</b> U ro'yxatdan emas, BUTUN
/// to'plamdan hisoblanadi, shuning uchun ro'yxat kesilgan bo'lsa ham
/// (<see cref="Truncated"/>) yig'indi to'g'ri qoladi.
/// </para>
/// </summary>
/// <param name="Scope">So'ralgan hisob yoki guruh (<c>revenue:*</c>).</param>
/// <param name="From">Davr boshi.</param>
/// <param name="To">Davr oxiri.</param>
/// <param name="Total">Yig'indi = katakning qiymati.</param>
/// <param name="Count">Davrdagi satrlarning HAQIQIY soni.</param>
/// <param name="Truncated">true = ro'yxat kesilgan.</param>
/// <param name="Lines">Satrlar, yangisidan eskisiga.</param>
public sealed record LedgerLinesDto(
    string Scope, DateOnly From, DateOnly To,
    decimal Total, int Count, bool Truncated,
    List<LedgerLineDto> Lines);

public sealed partial class FinanceReportQueries
{
    /// <summary>Matritsa qamrab oladigan oylar — kalendar yili.</summary>
    public const int MonthsInYear = 12;

    /// <summary>Hisobot ochilishi mumkin bo'lgan eng erta yil (baza 2000-yildan oldingi pulni bilmaydi).</summary>
    public const int MinReportYear = 2000;

    /// <summary>Eng kech yil — terishdagi xatoni ("20226") to'xtatadi.</summary>
    public const int MaxReportYear = 2100;

    /// <summary>Drill-down ro'yxatining eng ko'p uzunligi (yig'indi BARIBIR to'liq).</summary>
    public const int MaxLedgerLines = 500;

    /// <summary>Drill-down so'rovi qamray oladigan eng uzun davr — bir yil.</summary>
    public const int MaxLedgerLineMonths = 12;

    /// <summary>Guruh so'rovining belgisi: <c>revenue:*</c> — prefiksdagi hamma hisob.</summary>
    public const string GroupSuffix = ":*";

    // =====================================================================
    //  1) Yil × oy matritsasi
    // =====================================================================

    /// <summary>
    /// Bir yilning P&amp;L matritsasi: har hisob uchun bitta qator, har oy
    /// uchun bitta katak, pastida sof natija va pul qoldig'i qatorlari.
    ///
    /// <para>
    /// Uchta so'rov: jurnal (oy × hisob × yo'nalish) va pul oqimining ikki
    /// so'rovi. Sikl ichida <c>await</c> YO'Q.
    /// </para>
    /// </summary>
    /// <param name="year">Kalendar yili.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    /// <exception cref="ArgumentOutOfRangeException">Yil ishonchli oraliqdan tashqarida.</exception>
    public async Task<ProfitLossMatrixDto> ProfitLossMatrixAsync(int year, CancellationToken ct = default)
    {
        if (year < MinReportYear || year > MaxReportYear)
            throw new ArgumentOutOfRangeException(
                nameof(year), year,
                $"Yil {MinReportYear}–{MaxReportYear} oralig'ida bo'lishi kerak.");

        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);

        // ---- So'rov 1: oy × hisob × yo'nalish ----
        // Oy BAZADA ajratiladi; `new DateOnly(y, m, 1)` ni EF tarjima qila
        // olmaydi (CashFlowAsync dagi bilan bir xil sabab).
        var grouped = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate >= from && e.EntryDate <= to)
            .GroupBy(e => new { e.EntryDate.Month, e.Account, e.Direction })
            .Select(g => new
            {
                g.Key.Month,
                g.Key.Account,
                g.Key.Direction,
                Total = g.Sum(x => x.Amount),
            })
            .ToListAsync(ct);

        var yearTotals = grouped
            .Select(r => new AccountTotal(r.Account, r.Direction, r.Total))
            .ToList();

        var monthTotals = Enumerable.Range(1, MonthsInYear)
            .Select(month => grouped
                .Where(r => r.Month == month)
                .Select(r => new AccountTotal(r.Account, r.Direction, r.Total))
                .ToList())
            .ToList();

        var revenue = MatrixRows(yearTotals, monthTotals, RevenuePrefix, creditPositive: true);
        var expense = MatrixRows(yearTotals, monthTotals, ExpensePrefix, creditPositive: false);

        var revenueMonths = ColumnTotals(revenue);
        var expenseMonths = ColumnTotals(expense);
        var netMonths = revenueMonths.Zip(expenseMonths, (r, e) => r - e).ToList();

        // ---- So'rov 2-3: pul qoldig'i (ta'rif CashFlowAsync da) ----
        var cash = await CashFlowAsync(from, to, ct);

        var startBalance = new List<decimal>(MonthsInYear);
        var endBalance = new List<decimal>(MonthsInYear);
        for (var i = 0; i < MonthsInYear; i++)
        {
            startBalance.Add(cash.Accounts.Sum(a => a.Months[i].Opening));
            endBalance.Add(cash.Accounts.Sum(a => a.Months[i].Closing));
        }

        return new ProfitLossMatrixDto(
            Year: year,
            Months: [.. Enumerable.Range(1, MonthsInYear).Select(m => MonthKey(new DateOnly(year, m, 1)))],
            Revenue: revenue,
            RevenueMonths: revenueMonths,
            RevenueTotal: revenue.Sum(r => r.Total),
            Expense: expense,
            ExpenseMonths: expenseMonths,
            ExpenseTotal: expense.Sum(r => r.Total),
            NetMonths: netMonths,
            NetTotal: netMonths.Sum(),
            StartBalance: startBalance,
            EndBalance: endBalance,
            OpeningBalance: cash.Opening,
            ClosingBalance: cash.Closing);
    }

    /// <summary>
    /// Prefiksga mos qatorlar. Qator to'plami va TARTIBI yil yakunidan
    /// olinadi, katak esa o'sha oyning <see cref="Lines"/> natijasidan —
    /// ya'ni har katak bitta oylik P&amp;L satrining o'zi.
    /// </summary>
    private static List<ProfitLossMatrixRowDto> MatrixRows(
        IReadOnlyList<AccountTotal> yearTotals,
        IReadOnlyList<List<AccountTotal>> monthTotals,
        string prefix,
        bool creditPositive)
    {
        var yearLines = Lines(yearTotals, prefix, creditPositive);

        var byMonth = monthTotals
            .Select(totals => Lines(totals, prefix, creditPositive)
                .ToDictionary(l => l.Account, l => l.Amount, StringComparer.Ordinal))
            .ToList();

        return [.. yearLines.Select(line =>
        {
            var months = byMonth
                .Select(m => m.GetValueOrDefault(line.Account))
                .ToList();

            // Qator yakuni — kataklarning yig'indisi. Yil bo'yicha to'g'ridan
            // to'g'ri hisoblangan qiymat bilan bir xil chiqadi (ayirma
            // chiziqli), lekin ekranda QO'SHILADIGAN raqam turishi kerak.
            return new ProfitLossMatrixRowDto(line.Account, months, months.Sum());
        })];
    }

    /// <summary>Ustun yakunlari: har oy uchun qatorlar yig'indisi.</summary>
    private static List<decimal> ColumnTotals(IReadOnlyList<ProfitLossMatrixRowDto> rows) =>
        [.. Enumerable.Range(0, MonthsInYear).Select(i => rows.Sum(r => r.Months[i]))];

    // =====================================================================
    //  2) Drill-down — katakni hosil qilgan jurnal satrlari
    // =====================================================================

    /// <summary>
    /// Bitta hisob (yoki <c>revenue:*</c> / <c>expense:*</c> guruhi) bo'yicha
    /// davrdagi jurnal satrlari va ularning yig'indisi.
    ///
    /// <para>
    /// Belgi qoidasi P&amp;L dagi bilan BIR XIL: daromad hisobida kredit +,
    /// debet −; chiqim hisobida teskari; pul hisoblarida (<c>cash</c>,
    /// <c>bank</c>, <c>receivable</c>) debet +. Shuning uchun
    /// <see cref="LedgerLinesDto.Total"/> matritsadagi katakning AYNAN o'zi.
    /// </para>
    /// </summary>
    /// <param name="account">Hisob kodi yoki guruh (<c>revenue:*</c>).</param>
    /// <param name="from">Davr boshi.</param>
    /// <param name="to">Davr oxiri.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    public async Task<LedgerLinesDto> LedgerLinesAsync(
        string account, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        RequireRange(from, to);

        var months = MonthsBetween(from, to);
        if (months > MaxLedgerLineMonths)
            throw new ArgumentOutOfRangeException(
                nameof(to), months,
                $"Davr juda uzun: {months} oy. Ruxsat etilgani — {MaxLedgerLineMonths} oy.");

        var scope = account.Trim();
        var isGroup = scope.EndsWith(GroupSuffix, StringComparison.Ordinal);
        var prefix = isGroup ? scope[..^1] : scope;

        var query = db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate >= from && e.EntryDate <= to);

        query = isGroup
            ? query.Where(e => e.Account.StartsWith(prefix))
            : query.Where(e => e.Account == scope);

        // Tabiiy tomon: daromad — kredit, qolgani — debet. Bu AYNAN
        // `Lines(..., creditPositive)` dagi qoida.
        var creditPositive = prefix.StartsWith(RevenuePrefix, StringComparison.Ordinal);

        // ---- So'rov 1: yig'indi va son — BUTUN to'plamdan ----
        var totals = await query
            .GroupBy(e => e.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct);

        var total = Net(
            totals.Select(t => new AccountTotal(scope, t.Direction, t.Total)),
            debitPositive: !creditPositive);
        var count = totals.Sum(t => t.Count);

        // ---- So'rov 2: ko'rsatiladigan satrlar ----
        var rows = await query
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.Id)
            .Take(MaxLedgerLines)
            .Select(e => new LedgerRowSource(
                e.Id, e.EntryDate, e.Account, e.Direction, e.Amount,
                e.RefType, e.RefId, e.Memo, e.CreatedAt))
            .ToListAsync(ct);

        var lines = await DecorateAsync(
            rows,
            r => Sign(r.Direction, creditPositive) * r.Amount,
            ct);

        return new LedgerLinesDto(
            Scope: scope,
            From: from,
            To: to,
            Total: total,
            Count: count,
            Truncated: count > rows.Count,
            Lines: lines);
    }

    /// <summary>Tabiiy tomonda +1, qarshi tomonda −1.</summary>
    private static decimal Sign(string direction, bool creditPositive) =>
        (direction == LedgerDirection.Credit) == creditPositive ? 1m : -1m;
}
