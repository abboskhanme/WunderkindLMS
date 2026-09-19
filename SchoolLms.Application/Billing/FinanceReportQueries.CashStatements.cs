using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  PUL OQIMI TOIFALAR KESIMIDA + MOLIYA HISOBOTLARI PANELI
//  (docs/modules/finance-parity.md §2.7 F7.01/F7.03 va §2.4 F4.01/F4.02.)
// ===========================================================================
//
//  SAVOL: "shu oyda kassaga va bankka QAYERDAN pul kirdi va QAYERGA ketdi".
//  `CashFlowAsync` bunga bitta raqam bilan javob beradi (kirim, chiqim,
//  qoldiq); bu yerda O'SHA raqam toifalarga BO'LINADI. Yangi summa
//  yaratilmaydi — mavjudi bo'laklanadi, xolos.
//
//  BO'LISH QOIDASI — IKKITA, IKKOVI HAM ALLAQACHON MAVJUD
//  ------------------------------------------------------
//  1. TO'LOV satri (`ref_type = payment`, storno'da esa `ref_id` ASL
//     to'lovni ko'rsatadi): pul TAQSIMOTLAR bo'yicha bo'linadi —
//     `payment_allocations` → `invoices.category_id` → `Accounts.RevenueFor`.
//     Taqsimlanmagan qoldiq — AVANS (`advance`). Bu aynan
//     `CashDayQueries.ByCategoryAsync` ning qoidasi, faqat kun o'rniga oy.
//  2. QOLGAN har qanday satr: partiyaning IKKINCHI oyog'i (qarshi hisob).
//     Chiqim uchun bu `expense:*`, ichki o'tkazma uchun esa boshqa pul
//     hisobi (`transfer`). Qarshi oyoq topilmasa — `other`.
//
//  STRUKTURAVIY KAFOLAT: bitta jurnal satrining bo'laklari yig'indisi
//  HAR DOIM satrning summasiga teng (qoldig'i `advance` yoki `other` ga
//  tushadi, ortiqchasi esa kesiladi). Demak "toifalar yig'indisi = pul
//  oqimining kirim/chiqimi" tengligi tasodif emas, tuzilish xossasi.
//
//  STORNO — KUN EKRANIDAGI QOIDA, `EffectiveAllocations` EMAS
//  ----------------------------------------------------------
//  Storno o'z KUNIDA (ya'ni o'z oyida) harakat bo'lib turadi: ko'zgu satr
//  bugungi sana bilan yoziladi (`LedgerService.ReverseAsync`). Shuning
//  uchun bu yerda "storno = umuman bo'lmagan" qoidasi (qarzdorlar uchun
//  to'g'ri bo'lgan `EffectiveAllocations`) ATAYLAB ishlatilmaydi — u kecha
//  chop etilgan pul oqimini bugun o'zgartirib yuborardi
//  (`CashDayQueries.cs` sarlavhasi shuni batafsil yozadi). Storno o'z
//  toifasining CHIQIMI bo'lib ko'rinadi: qator ichida kirim va chiqim
//  alohida ustunda turadi, "sof" esa ularning ayirmasi.
//
//  UNUMDORLIK
//  ----------
//  Davr 12 oygacha (`MaxStatementMonths`), satrlar soni esa
//  `MaxStatementLines` bilan chegaralangan: bo'lish qoidasi satr darajasida
//  ishlaydi, ya'ni bir yillik pul satrlari xotiraga olinadi (500 o'quvchili
//  maktabda ~7 000 satr). To'rtta so'rov + pul oqimining ikkitasi; sikl
//  ichida `await` YO'Q.

/// <summary>Toifa kataklarining kaliti — hisob kodi bo'lmagan uchta holat.</summary>
public static class CashFlowKeys
{
    /// <summary>Taqsimlanmagan to'lov — pul keldi, hali hisob-fakturaga biriktirilmadi.</summary>
    public const string Advance = "advance";

    /// <summary>Kassa ↔ bank ichki o'tkazmasi: ikkala oyog'i ham pul hisobi.</summary>
    public const string Transfer = "transfer";

    /// <summary>Qarshi oyog'i topilmagan qoldiq. Yashirilmaydi — yashirish balansni buzardi.</summary>
    public const string Other = "other";
}

/// <summary>Toifa bo'limlari — ekrandagi uchta blok.</summary>
public static class CashFlowSectionKind
{
    public const string Income = "income";
    public const string Expense = "expense";
    public const string Other = "other";
}

/// <summary>
/// Bitta katak: kirim, chiqim va ularning ayirmasi.
/// <see cref="Amount"/> HAR DOIM <c>Inflow − Outflow</c> — ya'ni chiqim
/// toifasining kataklari MANFIY. Belgi ekranda ham shundayligicha
/// ko'rsatiladi: "chiqimni musbat qilib ko'rsatish" pul oqimini ikki xil
/// o'qishga yo'l ochardi.
/// </summary>
public sealed record CashFlowCellDto(decimal Inflow, decimal Outflow, decimal Amount);

/// <summary>Toifaning bitta qatori: oylar bo'yicha kataklar va yakun.</summary>
/// <param name="Key">Hisob kodi (<c>revenue:*</c>, <c>expense:*</c>) yoki
/// <see cref="CashFlowKeys"/> dagi maxsus kalit.</param>
/// <param name="Kind">income | expense | other — qaysi bo'limga tushishi.</param>
/// <param name="Label">O'zbekcha nom — SERVERDAN.</param>
/// <param name="Months">Oylar bo'yicha kataklar (ustunlar tartibida).</param>
/// <param name="Total">Qator yakuni = kataklarning yig'indisi.</param>
public sealed record CashFlowCategoryRowDto(
    string Key, string Kind, string Label, List<CashFlowCellDto> Months, CashFlowCellDto Total);

/// <summary>Bo'lim: tushumlar, chiqimlar yoki boshqa harakatlar.</summary>
public sealed record CashFlowSectionDto(
    string Kind, string Label, List<CashFlowCategoryRowDto> Rows,
    List<CashFlowCellDto> Months, CashFlowCellDto Total);

/// <summary>
/// Pul oqimi TOIFALAR kesimida (§2.7). Ustunlar — oylar; qatorlar —
/// toifalar; pastda oy boshidagi va oxiridagi qoldiq.
/// </summary>
/// <param name="From">Davr boshi.</param>
/// <param name="To">Davr oxiri.</param>
/// <param name="Account">Filtr: <c>cash</c>, <c>bank</c> yoki null (ikkovi).</param>
/// <param name="Months">Ustunlar: "YYYY-MM".</param>
/// <param name="Sections">Bo'limlar — income, expense, other (bo'shi tushmaydi).</param>
/// <param name="MonthTotals">Har oyning kirim / chiqim / sof yakuni.</param>
/// <param name="Total">Davrning yakuni — <c>CashFlowAsync</c> dagi raqamning o'zi.</param>
/// <param name="Opening">Oy boshidagi qoldiq.</param>
/// <param name="Closing">Oy oxiridagi qoldiq.</param>
/// <param name="OpeningBalance">Davr boshidagi qoldiq.</param>
/// <param name="ClosingBalance">Davr oxiridagi qoldiq.</param>
public sealed record CashFlowStatementDto(
    DateOnly From, DateOnly To, string? Account,
    List<string> Months,
    List<CashFlowSectionDto> Sections,
    List<CashFlowCellDto> MonthTotals,
    CashFlowCellDto Total,
    List<decimal> Opening, List<decimal> Closing,
    decimal OpeningBalance, decimal ClosingBalance);

/// <summary>
/// Toifa katagining drill-down javobi.
///
/// <para>
/// <b>Muhim:</b> bu yerdagi <see cref="LedgerLineDto.Amount"/> —
/// satrning katakka TUSHGAN qismi, jurnaldagi to'liq summasi emas. Bitta
/// to'lov bir necha toifaga taqsimlangan bo'lsa, u har toifada o'z
/// bo'lagi bilan ko'rinadi. Shuning uchun satrlar yig'indisi katakka
/// AYNAN teng bo'ladi.
/// </para>
/// </summary>
public sealed record CashFlowLinesDto(
    string Scope, DateOnly From, DateOnly To,
    CashFlowCellDto Total, int Count, bool Truncated,
    List<LedgerLineDto> Lines);

/// <summary>KPI: joriy davr, oldingi davr va o'zgarish foizi.</summary>
/// <param name="Current">Joriy davr qiymati.</param>
/// <param name="Previous">Shu uzunlikdagi OLDINGI davr qiymati.</param>
/// <param name="ChangePercent">O'zgarish foizi. Oldingi davr nol bo'lsa —
/// null: nolga bo'lish ham, "+100%" ham yolg'on bo'lardi.</param>
public sealed record FinanceKpiDto(decimal Current, decimal Previous, decimal? ChangePercent);

/// <summary>Kunlik grafikning bitta ustuni.</summary>
public sealed record FinanceDayDto(DateOnly Date, decimal Inflow, decimal Outflow, decimal Net);

/// <summary>
/// To'lov usuli kesimi. FAQAT to'lovlar: chiqimda usul saqlanmaydi
/// (§2.8 F8.02), shuning uchun bu yerda chiqim ustuni yo'q va uni o'ylab
/// topish ham mumkin emas.
/// </summary>
/// <param name="Method">cash | card | transfer | online.</param>
/// <param name="Label">O'zbekcha nom — SERVERDAN.</param>
/// <param name="Inflow">Qabul qilingan pul.</param>
/// <param name="Outflow">Storno qilingan qismi (o'z kunida).</param>
/// <param name="Amount">Inflow − Outflow.</param>
/// <param name="Count">Jurnal satrlari soni (storno ham sanaladi).</param>
public sealed record FinanceMethodRowDto(
    string Method, string Label, decimal Inflow, decimal Outflow, decimal Amount, int Count);

/// <summary>
/// "Moliya hisobotlari" paneli (§2.4): davrning uchta KPI'si, kunlik
/// grafik, toifalar va to'lov usullari kesimi.
/// </summary>
/// <param name="From">Davr boshi.</param>
/// <param name="To">Davr oxiri.</param>
/// <param name="PreviousFrom">Taqqoslanayotgan oldingi davr boshi.</param>
/// <param name="PreviousTo">Oldingi davr oxiri.</param>
/// <param name="Inflow">Kirim KPI'si.</param>
/// <param name="Outflow">Chiqim KPI'si.</param>
/// <param name="Net">Qoldiq (kirim − chiqim) KPI'si.</param>
/// <param name="OpeningBalance">Davr boshidagi pul qoldig'i.</param>
/// <param name="ClosingBalance">Davr oxiridagi pul qoldig'i.</param>
/// <param name="Days">Har kun uchun bitta ustun (harakatsiz kun ham).</param>
/// <param name="Sections">Toifalar kesimi — pul oqimi hisobotining o'sha bo'limlari.</param>
/// <param name="Methods">To'lov usullari kesimi.</param>
/// <param name="MethodsTotal">Usullar yakuni — qatorlar yig'indisi.</param>
/// <summary>Chegirma tahlilining bitta qatori — toifa kesimida.</summary>
/// <param name="Share">Umumiy chegirmadagi ulushi, foizda (ko'rsatish uchun).</param>
public sealed record DiscountBreakdownRowDto(
    string CategoryCode, string CategoryName, int InvoiceCount, decimal Total, decimal Share);

/// <summary>
/// Chegirmalar tahlili (EduSchool "Moliya hisobotlari" ekranidagi
/// "Chegirmalar tahlili" bloki — 2026-09-18 da o'qildi).
///
/// <para>
/// KESIM TOIFA BO'YICHA, CHEGIRMA NOMI BO'YICHA EMAS. Ularda har bir
/// chegirma qoidasining ulushi ko'rsatiladi; bizda hisob-faktura QAYSI
/// chegirma qoidasidan kelganini saqlaydigan ustun yo'q
/// (<c>invoices.discount</c> — faqat summa). Mavjud ma'lumotdan qoidaga
/// qaytib bo'lmaydi: bitta o'quvchida ikkita ustma-ust chegirma bo'lsa,
/// summani ular orasida bo'lish TAXMIN bo'lardi — pul hisobotida taxmin
/// qilmaymiz. Toifa kesimi esa ANIQ: u hisob-fakturaning o'z ustunidan
/// keladi. Qoida bo'yicha kesim kerak bo'lsa —
/// <c>invoices.discount_id</c> ustuni qo'shilishi kerak (docs/REMAINING-PARITY.md §3.4).
/// </para>
/// </summary>
/// <param name="AppliedCount">Chegirma qo'llangan hisob-fakturalar soni.</param>
/// <param name="Average">O'rtacha chegirma (jami ÷ qo'llanishlar soni).</param>
public sealed record DiscountAnalysisDto(
    decimal Total, int AppliedCount, decimal Average, List<DiscountBreakdownRowDto> Rows);

public sealed record FinanceDashboardDto(
    DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo,
    FinanceKpiDto Inflow, FinanceKpiDto Outflow, FinanceKpiDto Net,
    decimal OpeningBalance, decimal ClosingBalance,
    List<FinanceDayDto> Days,
    List<CashFlowSectionDto> Sections,
    List<FinanceMethodRowDto> Methods,
    FinanceMethodRowDto MethodsTotal,
    DiscountAnalysisDto Discounts);

public sealed partial class FinanceReportQueries
{
    /// <summary>Toifalar kesimi qamray oladigan eng uzun davr — bir yil.</summary>
    public const int MaxStatementMonths = 12;

    /// <summary>
    /// Bo'lish qoidasi uchun xotiraga olinadigan eng ko'p pul satri.
    /// Undan oshsa hisobot RAD ETADI — yarim to'g'ri raqam ko'rsatgandan
    /// ko'ra davrni qisqartirishni so'ragan yaxshi.
    /// </summary>
    public const int MaxStatementLines = 50_000;

    // =====================================================================
    //  1) Toifalar kesimidagi pul oqimi
    // =====================================================================

    /// <summary>
    /// Pul oqimi toifalar kesimida: kirim daromad hisobi bo'yicha (to'lov
    /// taqsimotlaridan), chiqim esa chiqim hisobi bo'yicha.
    /// </summary>
    /// <param name="from">Davr boshi.</param>
    /// <param name="to">Davr oxiri.</param>
    /// <param name="account">Faqat <c>cash</c> yoki faqat <c>bank</c>; null = ikkovi.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    public async Task<CashFlowStatementDto> CashFlowStatementAsync(
        DateOnly from, DateOnly to, string? account = null, CancellationToken ct = default)
    {
        var set = await LoadMoneyAsync(from, to, account, ct);
        var cash = await CashFlowAsync(from, to, ct);

        return ComposeStatement(set, from, to, account, cash);
    }

    /// <summary>
    /// Toifa katagining ortidagi satrlar. Yig'indi BUTUN to'plamdan
    /// hisoblanadi, ro'yxat esa eng yangi <see cref="MaxLedgerLines"/> tasi.
    /// </summary>
    /// <param name="key">Toifa kaliti. null = hamma toifa (usul bo'yicha drill-down).</param>
    /// <param name="method">To'lov usuli bo'yicha filtr (faqat to'lov satrlari). null = hammasi.</param>
    /// <param name="from">Davr boshi.</param>
    /// <param name="to">Davr oxiri.</param>
    /// <param name="account">Faqat <c>cash</c> yoki faqat <c>bank</c>; null = ikkovi.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    public async Task<CashFlowLinesDto> CashFlowLinesAsync(
        string? key, string? method, DateOnly from, DateOnly to, string? account = null,
        CancellationToken ct = default)
    {
        var set = await LoadMoneyAsync(from, to, account, ct);

        var portions = set.Portions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(key))
        {
            var wanted = key.Trim();
            portions = portions.Where(p => string.Equals(p.Key, wanted, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(method))
        {
            var wantedMethod = method.Trim();
            portions = portions.Where(p =>
                p.Line.RefId is { } id
                && set.Names.Payments.TryGetValue(id, out var payment)
                && string.Equals(payment.Method, wantedMethod, StringComparison.Ordinal));
        }

        // Bitta satr bir toifaga bir necha bo'lak bilan tushishi mumkin
        // (masalan ikki hisob-faktura, bitta daromad hisobi) — ular bitta
        // qatorga yig'iladi.
        var perLine = portions
            .GroupBy(p => p.Line.Id)
            .Select(g => new { Line = g.First().Line, Amount = g.Sum(p => p.Amount) })
            .Where(x => x.Amount != 0m)
            .OrderByDescending(x => x.Line.EntryDate)
            .ThenByDescending(x => x.Line.Id)
            .ToList();

        var inflow = perLine.Where(x => x.Line.Direction == LedgerDirection.Debit).Sum(x => x.Amount);
        var outflow = perLine.Where(x => x.Line.Direction == LedgerDirection.Credit).Sum(x => x.Amount);

        var shown = perLine.Take(MaxLedgerLines).ToList();
        var amountById = shown.ToDictionary(x => x.Line.Id, x => x.Amount);

        var decorated = await DecorateAsync(
            [.. shown.Select(x => x.Line.Source)],
            row => (row.Direction == LedgerDirection.Debit ? 1m : -1m) * amountById[row.Id],
            ct);

        // Satrning KATAKKA TUSHGAN qismi ko'rsatiladi (DTO izohiga qarang).
        var lines = decorated
            .Select(line => line with { Amount = Math.Abs(line.Signed) })
            .ToList();

        return new CashFlowLinesDto(
            Scope: string.IsNullOrWhiteSpace(key) ? "*" : key.Trim(),
            From: from,
            To: to,
            Total: Cell(inflow, outflow),
            Count: perLine.Count,
            Truncated: perLine.Count > shown.Count,
            Lines: lines);
    }

    // =====================================================================
    //  2) "Moliya hisobotlari" paneli
    // =====================================================================

    /// <summary>
    /// Davrning paneli: kirim / chiqim / qoldiq (oldingi davr bilan
    /// taqqoslab), kunlik grafik, toifalar va to'lov usullari kesimi.
    ///
    /// <para>
    /// <b>To'lov usuli bo'yicha UMUMIY filtr ataylab yo'q.</b> Usul faqat
    /// to'lovda saqlanadi; chiqimda u umuman yo'q (§2.8 F8.02). Shunday
    /// filtr qo'yilsa, "karta" tanlangan panel hamma chiqimni jimgina
    /// tashlab yuborardi va chiqim nolga aylanardi. Shuning uchun usul
    /// alohida KESIM sifatida ko'rsatiladi.
    /// </para>
    /// </summary>
    public async Task<FinanceDashboardDto> DashboardAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var set = await LoadMoneyAsync(from, to, account: null, ct);
        var cash = await CashFlowAsync(from, to, ct);
        var statement = ComposeStatement(set, from, to, null, cash);

        // ---- Oldingi davr: shu uzunlikdagi, tugagan kuni davr boshidan bir kun oldin ----
        var days = to.DayNumber - from.DayNumber + 1;
        var previousTo = from.AddDays(-1);
        var previousFrom = previousTo.AddDays(-(days - 1));

        var previous = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate >= previousFrom && e.EntryDate <= previousTo
                        && CashAccounts.Contains(e.Account))
            .GroupBy(e => e.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var previousIn = previous.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Total);
        var previousOut = previous.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Total);

        // ---- Kunlik grafik: harakatsiz kun ham ustun bo'lib qoladi ----
        var byDay = set.Lines
            .GroupBy(l => l.EntryDate)
            .ToDictionary(
                g => g.Key,
                g => (
                    In: g.Where(l => l.Direction == LedgerDirection.Debit).Sum(l => l.Amount),
                    Out: g.Where(l => l.Direction == LedgerDirection.Credit).Sum(l => l.Amount)));

        var daily = new List<FinanceDayDto>(days);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var (dayIn, dayOut) = byDay.GetValueOrDefault(day);
            daily.Add(new FinanceDayDto(day, dayIn, dayOut, dayIn - dayOut));
        }

        // ---- To'lov usullari kesimi ----
        var methods = set.Lines
            .Select(l => new
            {
                Line = l,
                Payment = l.RefId is { } id && set.Names.Payments.TryGetValue(id, out var p) ? p : null,
            })
            .Where(x => x.Payment is not null)
            .GroupBy(x => x.Payment!.Method, StringComparer.Ordinal)
            .Select(g =>
            {
                var methodIn = g.Where(x => x.Line.Direction == LedgerDirection.Debit).Sum(x => x.Line.Amount);
                var methodOut = g.Where(x => x.Line.Direction == LedgerDirection.Credit).Sum(x => x.Line.Amount);
                return new FinanceMethodRowDto(
                    g.Key, MethodLabel(g.Key), methodIn, methodOut, methodIn - methodOut, g.Count());
            })
            .OrderByDescending(r => r.Amount)
            .ThenBy(r => r.Method, StringComparer.Ordinal)
            .ToList();

        var methodsTotal = new FinanceMethodRowDto(
            "total", "Jami",
            methods.Sum(r => r.Inflow), methods.Sum(r => r.Outflow), methods.Sum(r => r.Amount),
            methods.Sum(r => r.Count));

        var discounts = await DiscountAnalysisAsync(from, to, ct);

        return new FinanceDashboardDto(
            From: from,
            To: to,
            PreviousFrom: previousFrom,
            PreviousTo: previousTo,
            Inflow: Kpi(cash.Inflow, previousIn),
            Outflow: Kpi(cash.Outflow, previousOut),
            Net: Kpi(cash.Net, previousIn - previousOut),
            OpeningBalance: cash.Opening,
            ClosingBalance: cash.Closing,
            Days: daily,
            Sections: statement.Sections,
            Methods: methods,
            MethodsTotal: methodsTotal,
            Discounts: discounts);
    }

    /// <summary>
    /// Chegirmalar tahlili — davrga tegishli hisob-fakturalardagi chegirma.
    ///
    /// <para>
    /// DAVR — hisob-fakturaning OYI bo'yicha (<c>period_month</c>), pul
    /// harakati sanasi bo'yicha emas: chegirma pul emas, HISOBLANGAN summani
    /// kamaytiradi, ya'ni u qaysi OYGA tegishli ekani muhim. Shu sababli
    /// ekrandagi davr "sentabr" bo'lsa, sentabr hisob-fakturalaridagi
    /// chegirma ko'rinadi — to'lov qachon kelganidan qat'i nazar.
    /// </para>
    /// <para>
    /// BEKOR QILINGAN (<c>void</c>) hisob-faktura hisobga OLINMAYDI: uning
    /// summasi ham, chegirmasi ham amalda yo'q.
    /// </para>
    /// </summary>
    private async Task<DiscountAnalysisDto> DiscountAnalysisAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Oy chegaralari: davrga TEGIB o'tgan oylarning hammasi.
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);

        var rows = await (from i in db.Invoices.AsNoTracking()
                          join c in db.FeeCategories.AsNoTracking() on i.CategoryId equals c.Id
                          where i.Discount > 0m
                                && i.Status != InvoiceStatus.Void
                                && i.PeriodMonth >= firstMonth && i.PeriodMonth <= lastMonth
                          group new { i.Discount } by new { c.Code, c.Name } into g
                          select new
                          {
                              g.Key.Code,
                              g.Key.Name,
                              Count = g.Count(),
                              Total = g.Sum(x => x.Discount),
                          })
                         .ToListAsync(ct);

        var total = rows.Sum(r => r.Total);
        var count = rows.Sum(r => r.Count);

        var breakdown = rows
            .OrderByDescending(r => r.Total)
            .Select(r => new DiscountBreakdownRowDto(
                r.Code, r.Name, r.Count, r.Total,
                total == 0m ? 0m : decimal.Round(r.Total * 100m / total, 1)))
            .ToList();

        return new DiscountAnalysisDto(
            total, count,
            count == 0 ? 0m : decimal.Round(total / count, 2),
            breakdown);
    }

    // =====================================================================
    //  Bo'lish qoidasi
    // =====================================================================

    /// <summary>
    /// Davrning pul satrlarini va ularning toifalarga bo'lingan bo'laklarini
    /// yig'adi. To'rtta so'rov, satr soniga qarab o'smaydi.
    /// </summary>
    private async Task<MoneySet> LoadMoneyAsync(
        DateOnly from, DateOnly to, string? account, CancellationToken ct)
    {
        RequireRange(from, to);

        var months = MonthsBetween(from, to);
        if (months > MaxStatementMonths)
            throw new ArgumentOutOfRangeException(
                nameof(to), months,
                $"Davr juda uzun: {months} oy. Ruxsat etilgani — {MaxStatementMonths} oy.");

        if (account is not null && !CashAccounts.Contains(account, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(
                nameof(account), account,
                $"Pul hisobi kutilgan edi: {string.Join(", ", CashAccounts)}.");

        string[] accounts = account is null ? CashAccounts : [account];

        var query = db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate >= from && e.EntryDate <= to && accounts.Contains(e.Account));

        var count = await query.CountAsync(ct);
        if (count > MaxStatementLines)
            throw new ArgumentOutOfRangeException(
                nameof(to), count,
                $"Davrda {count} ta pul yozuvi bor, chegara — {MaxStatementLines}. "
                + "Davrni qisqartiring yoki hisobni tanlang.");

        // ---- So'rov 1: davrning pul satrlari ----
        var lines = await query
            .OrderBy(e => e.Id)
            .Select(e => new LedgerRowSource(
                e.Id, e.EntryDate, e.Account, e.Direction, e.Amount,
                e.RefType, e.RefId, e.Memo, e.CreatedAt))
            .ToListAsync(ct);

        var rows = lines.Select(l => new MoneyLine(l)).ToList();
        var refIds = rows.Where(r => r.RefId is not null).Select(r => r.RefId!.Value).Distinct().ToList();

        // ---- So'rov 2-4: nomlar (to'lov / chiqim / hisob-faktura) ----
        var names = await NamesAsync(refIds, ct);

        var nullableRefIds = refIds.Select(id => (Guid?)id).ToList();
        var refTypes = rows.Select(r => r.RefType).Distinct(StringComparer.Ordinal).ToList();

        // ---- So'rov 5: to'lov taqsimotlari, toifa kodi bilan ----
        var paymentIds = refIds.Where(names.Payments.ContainsKey).ToList();
        List<AllocationTotal> allocations = paymentIds.Count == 0
            ? []
            : await (
                from a in db.PaymentAllocations.AsNoTracking()
                where paymentIds.Contains(a.PaymentId)
                join inv in db.Invoices.AsNoTracking() on a.InvoiceId equals inv.Id
                join c in db.FeeCategories.AsNoTracking() on inv.CategoryId equals c.Id
                group a by new { a.PaymentId, c.Code } into g
                select new AllocationTotal(g.Key.PaymentId, g.Key.Code, g.Sum(x => x.Amount)))
                .ToListAsync(ct);

        // ---- So'rov 6: partiyaning ikkinchi oyog'i ----
        List<CounterLeg> counters = refIds.Count == 0
            ? []
            : await db.LedgerEntries.AsNoTracking()
                .Where(e => nullableRefIds.Contains(e.RefId) && refTypes.Contains(e.RefType))
                .OrderBy(e => e.Id)
                .Select(e => new CounterLeg(e.Id, e.RefType, e.RefId!.Value, e.Account, e.Direction, e.Amount))
                .ToListAsync(ct);

        var allocationsByPayment = allocations
            .GroupBy(a => a.PaymentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var countersByBatch = counters
            .GroupBy(c => (c.RefType, c.RefId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var portions = new List<MoneyPortion>(rows.Count);
        foreach (var row in rows)
            portions.AddRange(Split(row, names, allocationsByPayment, countersByBatch));

        return new MoneySet(rows, portions, names);
    }

    /// <summary>
    /// Bitta pul satrini toifalarga bo'ladi.
    ///
    /// <para>
    /// <b>Yig'indi HAR DOIM satrning summasiga teng</b>: bo'laklar qoldiqdan
    /// olinadi (<c>Math.Min</c>), qolgani esa <c>advance</c> yoki
    /// <c>other</c> ga tushadi. Shu sabab toifalar yig'indisi pul oqimidan
    /// chetga chiqa olmaydi — hisobotning ikki ekrani bir-biriga zid raqam
    /// ko'rsatmaydi.
    /// </para>
    /// </summary>
    private static IEnumerable<MoneyPortion> Split(
        MoneyLine row,
        LedgerNames names,
        IReadOnlyDictionary<Guid, List<AllocationTotal>> allocationsByPayment,
        IReadOnlyDictionary<(string RefType, Guid RefId), List<CounterLeg>> countersByBatch)
    {
        var remaining = row.Amount;
        var isPayment = row.RefId is { } paymentId && names.Payments.ContainsKey(paymentId);

        if (isPayment)
        {
            // (1) TO'LOV: taqsimotlar bo'yicha daromad hisoblariga.
            foreach (var allocation in allocationsByPayment.GetValueOrDefault(row.RefId!.Value, []))
            {
                if (remaining <= 0m) break;
                var take = Math.Min(remaining, allocation.Amount);
                remaining -= take;
                yield return new MoneyPortion(row, Accounts.RevenueFor(allocation.CategoryCode), take);
            }

            if (remaining != 0m) yield return new MoneyPortion(row, CashFlowKeys.Advance, remaining);
            yield break;
        }

        // (2) QOLGANI: partiyaning qarshi oyog'i bo'yicha.
        var batch = row.RefId is { } refId
            ? countersByBatch.GetValueOrDefault((row.RefType, refId), [])
            : [];

        foreach (var leg in batch)
        {
            if (remaining <= 0m) break;
            if (leg.Id == row.Id) continue;
            if (leg.Direction == row.Direction) continue;

            var take = Math.Min(remaining, leg.Amount);
            remaining -= take;

            var key = CashAccounts.Contains(leg.Account, StringComparer.Ordinal)
                ? CashFlowKeys.Transfer
                : leg.Account;
            yield return new MoneyPortion(row, key, take);
        }

        if (remaining != 0m) yield return new MoneyPortion(row, CashFlowKeys.Other, remaining);
    }

    // =====================================================================
    //  Yig'ish
    // =====================================================================

    /// <summary>Bo'laklardan oylar × toifalar jadvalini quradi.</summary>
    private static CashFlowStatementDto ComposeStatement(
        MoneySet set, DateOnly from, DateOnly to, string? account, CashFlowDto cash)
    {
        var first = new DateOnly(from.Year, from.Month, 1);
        var monthCount = MonthsBetween(from, to);

        var byKey = new Dictionary<string, CashCell[]>(StringComparer.Ordinal);
        foreach (var portion in set.Portions)
        {
            var index = ((portion.Line.EntryDate.Year - first.Year) * 12)
                        + portion.Line.EntryDate.Month - first.Month;
            if (index < 0 || index >= monthCount) continue;

            if (!byKey.TryGetValue(portion.Key, out var cells))
            {
                cells = [.. Enumerable.Range(0, monthCount).Select(_ => new CashCell())];
                byKey[portion.Key] = cells;
            }

            cells[index].Add(portion.Line.Direction, portion.Amount);
        }

        var rows = byKey
            .Select(kv =>
            {
                var months = kv.Value.Select(c => c.ToDto()).ToList();
                return new CashFlowCategoryRowDto(
                    Key: kv.Key,
                    Kind: KeyKind(kv.Key),
                    Label: CashFlowKeyLabel(kv.Key),
                    Months: months,
                    Total: Sum(months));
            })
            .Where(r => r.Total.Inflow != 0m || r.Total.Outflow != 0m)
            .ToList();

        var sections = new[]
            {
                (Kind: CashFlowSectionKind.Income, Label: "Tushumlar"),
                (Kind: CashFlowSectionKind.Expense, Label: "Chiqimlar"),
                (Kind: CashFlowSectionKind.Other, Label: "Boshqa harakatlar"),
            }
            .Select(section =>
            {
                var sectionRows = rows
                    .Where(r => r.Kind == section.Kind)
                    .OrderByDescending(r => Math.Abs(r.Total.Amount))
                    .ThenBy(r => r.Key, StringComparer.Ordinal)
                    .ToList();

                var months = Enumerable.Range(0, monthCount)
                    .Select(i => Sum(sectionRows.Select(r => r.Months[i])))
                    .ToList();

                return new CashFlowSectionDto(section.Kind, section.Label, sectionRows, months, Sum(months));
            })
            .Where(s => s.Rows.Count > 0)
            .ToList();

        var monthTotals = Enumerable.Range(0, monthCount)
            .Select(i => Sum(sections.Select(s => s.Months[i])))
            .ToList();

        // Qoldiq qatorlari pul oqimining O'ZIDAN — bu yerda qayta hisoblanmaydi.
        List<CashFlowAccountDto> accounts = account is null
            ? cash.Accounts
            : [.. cash.Accounts.Where(a => string.Equals(a.Account, account, StringComparison.Ordinal))];

        return new CashFlowStatementDto(
            From: from,
            To: to,
            Account: account,
            Months: [.. Enumerable.Range(0, monthCount).Select(i => MonthKey(first.AddMonths(i)))],
            Sections: sections,
            MonthTotals: monthTotals,
            Total: Sum(monthTotals),
            Opening: [.. Enumerable.Range(0, monthCount).Select(i => accounts.Sum(a => a.Months[i].Opening))],
            Closing: [.. Enumerable.Range(0, monthCount).Select(i => accounts.Sum(a => a.Months[i].Closing))],
            OpeningBalance: accounts.Sum(a => a.Opening),
            ClosingBalance: accounts.Sum(a => a.Closing));
    }

    private static CashFlowCellDto Cell(decimal inflow, decimal outflow) =>
        new(inflow, outflow, inflow - outflow);

    private static CashFlowCellDto Sum(IEnumerable<CashFlowCellDto> cells)
    {
        decimal inflow = 0m, outflow = 0m;
        foreach (var cell in cells)
        {
            inflow += cell.Inflow;
            outflow += cell.Outflow;
        }

        return Cell(inflow, outflow);
    }

    private static FinanceKpiDto Kpi(decimal current, decimal previous) =>
        new(current, previous,
            previous == 0m ? null : decimal.Round((current - previous) / Math.Abs(previous) * 100m, RateScale));

    /// <summary>Toifa kaliti qaysi bo'limga tushadi.</summary>
    internal static string KeyKind(string key) =>
        key.StartsWith(RevenuePrefix, StringComparison.Ordinal) || key == CashFlowKeys.Advance
            ? CashFlowSectionKind.Income
            : key.StartsWith(ExpensePrefix, StringComparison.Ordinal)
                ? CashFlowSectionKind.Expense
                : CashFlowSectionKind.Other;

    /// <summary>
    /// Toifa kalitining o'zbekcha nomi. Hisob kodlari uchun
    /// <see cref="MoneyFlowQueries.LabelFor"/> — nomlar ikki ekranda har xil
    /// bo'lib qolmasin.
    /// </summary>
    internal static string CashFlowKeyLabel(string key) => key switch
    {
        CashFlowKeys.Advance => "Avans (taqsimlanmagan)",
        CashFlowKeys.Transfer => "Ichki o'tkazma (kassa ↔ bank)",
        CashFlowKeys.Other => "Boshqa harakat",
        Accounts.Receivable => "O'quvchi hisobi",
        Accounts.Cash => "Kassa (naqd)",
        Accounts.Bank => "Bank hisobi",
        _ => MoneyFlowQueries.LabelFor(key),
    };

    /// <summary>To'lov usulining o'zbekcha nomi — UI o'z lug'atini saqlamaydi.</summary>
    internal static string MethodLabel(string method) => method switch
    {
        PaymentMethod.Cash => "Naqd",
        PaymentMethod.Card => "Karta",
        PaymentMethod.Transfer => "O'tkazma",
        PaymentMethod.Online => "Onlayn",
        _ => method,
    };

    /// <summary>Yig'indini to'playdigan o'zgaruvchan katak (DTO o'zgarmas).</summary>
    private sealed class CashCell
    {
        private decimal inflow;
        private decimal outflow;

        public void Add(string direction, decimal amount)
        {
            if (direction == LedgerDirection.Debit) inflow += amount;
            else outflow += amount;
        }

        public CashFlowCellDto ToDto() => Cell(inflow, outflow);
    }

    /// <summary>Pul satri — bezash uchun manba qatori bilan birga.</summary>
    private sealed record MoneyLine(LedgerRowSource Source)
    {
        public long Id => Source.Id;

        public DateOnly EntryDate => Source.EntryDate;

        public string Direction => Source.Direction;

        public decimal Amount => Source.Amount;

        public string RefType => Source.RefType;

        public Guid? RefId => Source.RefId;
    }

    /// <summary>Bitta pul satrining bitta toifaga tushgan bo'lagi.</summary>
    private sealed record MoneyPortion(MoneyLine Line, string Key, decimal Amount);

    /// <summary>To'lovning bitta toifaga taqsimlangan summasi.</summary>
    private sealed record AllocationTotal(Guid PaymentId, string CategoryCode, decimal Amount);

    /// <summary>Partiyaning bitta satri — qarshi oyoqni topish uchun.</summary>
    private sealed record CounterLeg(
        long Id, string RefType, Guid RefId, string Account, string Direction, decimal Amount);

    /// <summary>Davrning pul satrlari, ularning bo'laklari va nomlar lug'ati.</summary>
    private sealed record MoneySet(
        List<MoneyLine> Lines, List<MoneyPortion> Portions, LedgerNames Names);
}
