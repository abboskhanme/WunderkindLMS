using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  "KASSA KUNI" — kunlik kassa paneli. FAQAT O'QISH.
//  (docs/modules/existing-module-gaps.md §3.2 — EduSchool'dagi `fin-map/*`.)
// ===========================================================================
//
//  NEGA BU EKRAN BOR
//  -----------------
//  Kassir va direktor har kuni bitta savol bilan keladi: "hozir kassada
//  qancha pul bor va bugun nima bo'ldi". Bugungacha ular buni FOYDA-ZARAR
//  hisobotidan o'qishga majbur edi — davrni bir kunga qisqartirib, revenue
//  qatorlaridan taxmin qilib. P&L esa bu savolga JAVOB BERMAYDI: u
//  daromadni tan olingan kuni ko'rsatadi, pul esa boshqa kuni keladi.
//
//  Bu klass hech qanday YANGI raqam o'ylab topmaydi. Hammasi allaqachon
//  `ledger_entries`, `payments`, `payment_allocations` va `cash_shifts` da
//  yotibdi; bu yerda ular KUN shaklida yig'iladi, xolos.
//
//  YAGONA MANBA — JURNAL, KUN ESA `entry_date`
//  -------------------------------------------
//  "Kun" deganda `ledger_entries.entry_date` tushuniladi — u `date` tipida
//  va maktab mintaqasida (`AppClock.LocalDateOf`) yoziladi. `payments.
//  received_at` (timestamptz) bo'yicha guruhlash MUMKIN EMAS edi: UTC+5 da
//  kun 19:00 da almashadi, ya'ni kechki to'lovlar qo'shni kunga tushib
//  ketardi. `CashShiftService.ListAsync` xuddi shu sababdan ikki bosqichli
//  filtr ishlatadi; bu yerda esa muammo umuman yo'q, chunki jurnal sanani
//  ALLAQACHON mahalliy kun sifatida saqlaydi.
//
//  ARIFMETIKA TAKRORLANMAYDI
//  -------------------------
//  Ochilish → kirim → chiqim → yopilish ta'rifi AYNAN
//  `FinanceReportQueries.CashFlowAsync` dagi ta'rif (debet − kredit, davrdan
//  OLDINGI hamma yozuvdan hisoblangan qoldiq), faqat oy o'rniga kun. Ochiq
//  smenaning raqamlari `ICashShiftService.ListAsync` dan OLINADI — bu yerda
//  qayta hisoblanmaydi (quyida `OpenShiftsAsync` izohiga qarang).
//
//  STORNO — KUN EKRANIDAGI ENG NOZIK JOY
//  -------------------------------------
//  `LedgerService.ReverseAsync` ko'zgu satrni DOIM BUGUNGI sana bilan
//  yozadi, originalning sanasi bilan emas (SPEC §4.1: yopilgan davr orqaga
//  qarab o'zgarmaydi). Demak bu ekranda storno — o'tgan kunni "tuzatadigan"
//  narsa emas, BUGUNGI kunning chiqimi. Shuning uchun:
//    · kunning kirim/chiqimi hech qachon orqaga qarab o'zgarmaydi;
//    · toifalar kesimi ham SHU qoidaga bo'ysunadi — storno qilingan to'lovning
//      toifasi storno KUNIDA minus bilan ko'rinadi, to'lov kunida esa plus
//      bo'lib qolaveradi (batafsil: `ByCategoryAsync`).
//  Ya'ni bu yerda `FinanceReportQueries.EffectiveAllocations` (qarzdorlar va
//  yig'ilish darajasi uchun to'g'ri bo'lgan "storno = umuman bo'lmagan"
//  qoidasi) ATAYLAB ishlatilmaydi: u qarz uchun to'g'ri, kun uchun esa kecha
//  chop etilgan qog'ozni bugun o'zgartirib yuborardi.

/// <summary>Kunning bitta pul hisobi (<c>cash</c> yoki <c>bank</c>) kesimi.</summary>
/// <param name="Account">Hisob kodi: <c>cash</c> yoki <c>bank</c>.</param>
/// <param name="Opening">Kun boshidagi qoldiq (shu kungacha bo'lgan hamma yozuvdan).</param>
/// <param name="Inflow">Kun ichidagi kirim (debet).</param>
/// <param name="Outflow">Kun ichidagi chiqim (kredit) — storno ham shu yerda.</param>
/// <param name="Net">Kirim − chiqim.</param>
/// <param name="Closing">Kun oxiridagi qoldiq = Opening + Net.</param>
public sealed record CashDayAccountDto(
    string Account, decimal Opening, decimal Inflow, decimal Outflow, decimal Net, decimal Closing);

/// <summary>
/// Kunning bitta pul harakati = jurnalning <c>cash</c>/<c>bank</c> hisobiga
/// tushgan BITTA satri.
///
/// <para>
/// Nega aynan jurnal satri, "to'lov" yoki "chiqim" emas: har bir to'lov
/// jurnalda pul hisobiga ANIQ bitta satr yozadi (<c>PaymentService</c>),
/// har bir chiqim ham (<c>ExpenseService</c>), storno ham. Ya'ni "kassadan
/// o'tgan harakatlar ro'yxati" degan savolning javobi jurnalda bir ma'noli
/// turibdi va uni jadvallardan qayta yig'ish shart emas.
/// </para>
/// </summary>
/// <param name="EntryId">Jurnal satri id'si — ro'yxatdagi barqaror kalit.</param>
/// <param name="Account">Pul qayerdan/qayerga: <c>cash</c> | <c>bank</c>.</param>
/// <param name="Direction">debit = kirim, credit = chiqim.</param>
/// <param name="Amount">Summa (har doim musbat — jurnal qoidasi).</param>
/// <param name="Signed">Kirim uchun +Amount, chiqim uchun −Amount.</param>
/// <param name="Kind">payment | expense | reversal | invoice | salary (<see cref="LedgerRefType"/>).</param>
/// <param name="IsReversal">true = storno satri. UI buni ALOHIDA ko'rsatadi, yashirmaydi.</param>
/// <param name="RefId">Manba yozuv id'si (to'lov / chiqim). Storno'da — ASL to'lovning id'si.</param>
/// <param name="Title">Bir qatorlik tavsif: o'quvchi ismi yoki chiqim toifasi.</param>
/// <param name="Memo">Jurnaldagi izoh. Storno'da — SABAB (u majburiy, SPEC §4.3).</param>
/// <param name="ReceiptNo">Chek raqami (faqat to'lov va uning storno'si).</param>
/// <param name="Method">To'lov usuli: cash | card | transfer | online.</param>
/// <param name="ActorName">Kassir (to'lov) yoki yozuvni kiritgan xodim (chiqim/storno).</param>
/// <param name="Category">Chiqim toifasi (<c>salary</c>, <c>utilities</c> …) — faqat chiqimda.</param>
/// <param name="CreatedAt">Yozuv jurnalga tushgan lahza.</param>
public sealed record CashDayMovementDto(
    long EntryId,
    string Account,
    string Direction,
    decimal Amount,
    decimal Signed,
    string Kind,
    bool IsReversal,
    Guid? RefId,
    string Title,
    string? Memo,
    long? ReceiptNo,
    string? Method,
    string? ActorName,
    string? Category,
    DateTimeOffset CreatedAt);

/// <summary>
/// Kunning turlar kesimi. "Tur" — bu pul harakatining IKKINCHI oyog'i
/// (hisoblar rejasidagi qarshi hisob), ya'ni yangi taksonomiya emas:
/// to'lov uchun <c>receivable</c>, chiqim uchun <c>expense:*</c>.
/// </summary>
/// <param name="Key">Barqaror kalit: <c>{qarshi-hisob}</c> yoki <c>{qarshi-hisob}:reversal</c>.</param>
/// <param name="Account">Qarshi hisob kodi. Topilmasa — jurnaldagi <c>ref_type</c>.</param>
/// <param name="Label">O'zbekcha nom — SERVERDAN keladi, UI o'z lug'atini saqlamaydi.</param>
/// <param name="IsReversal">true = storno qatori (alohida satr, netlanmaydi).</param>
/// <param name="Count">Nechta harakat.</param>
/// <param name="Amount">Belgi bilan: kirim +, chiqim −.</param>
public sealed record CashDayTypeRowDto(
    string Key, string Account, string Label, bool IsReversal, int Count, decimal Amount);

/// <summary>Kunning to'lov toifalari kesimi (nimaga to'landi).</summary>
/// <param name="CategoryId">Toifa id'si.</param>
/// <param name="CategoryCode">tuition | bus | dormitory | meals | other …</param>
/// <param name="CategoryName">O'zbekcha nom (<c>fee_categories.name</c>).</param>
/// <param name="Amount">Belgi bilan: to'lov +, storno −.</param>
public sealed record CashDayCategoryRowDto(
    Guid CategoryId, string CategoryCode, string CategoryName, decimal Amount);

/// <summary>
/// HOZIR ochiq turgan smena: kim kassada, qachondan beri, shu daqiqagacha
/// kassada qancha naqd bo'lishi kerak.
/// </summary>
/// <param name="ShiftId">Smena id'si — Z-hisobotga o'tish uchun.</param>
/// <param name="CashierName">Kassir ismi.</param>
/// <param name="OpenedAt">Smena ochilgan lahza.</param>
/// <param name="OpeningFloat">Smena boshidagi kassa qoldig'i.</param>
/// <param name="CashSoFar">Shu smenadagi naqd harakat (storno minus bilan).</param>
/// <param name="ExpectedCashSoFar">OpeningFloat + CashSoFar — "hozir javonda shuncha bo'lishi kerak".</param>
/// <param name="NonCashSoFar">Karta/o'tkazma/onlayn — bankka tushadi, kassada sanalmaydi.</param>
/// <param name="PaymentsCount">Smenadagi to'lovlar soni (storno ham kiradi).</param>
public sealed record CashDayShiftDto(
    Guid ShiftId,
    string CashierName,
    DateTimeOffset OpenedAt,
    decimal OpeningFloat,
    decimal CashSoFar,
    decimal ExpectedCashSoFar,
    decimal NonCashSoFar,
    int PaymentsCount);

/// <summary>
/// "Kassa kuni" paneli — bitta kunning to'liq manzarasi.
///
/// <para>
/// <b>Nega bitta DTO, EduSchool'dagi kabi to'qqizta endpoint emas.</b> Panel
/// bir kun uchun ochiladi va o'sha zahoti hamma bo'lagini ko'rsatadi. To'qqiz
/// so'rov = to'qqiz spinner va to'qqizta yarim yuklangan holat; javob esa bir
/// kunlik, ya'ni kichik. Kalendar alohida (u kun almashganda o'zgarmaydi).
/// </para>
/// </summary>
/// <param name="Date">Qaysi kun.</param>
/// <param name="Total">Naqd + bank birgalikda.</param>
/// <param name="Accounts">Har bir pul hisobi alohida (<c>cash</c>, <c>bank</c>) — shu tartibda.</param>
/// <param name="Movements">Kunning harakatlari, yangisidan eskisiga.</param>
/// <param name="MovementsTruncated">true = ro'yxat kesilgan (jami <paramref name="MovementsTotal"/> ta).</param>
/// <param name="MovementsTotal">Kundagi harakatlarning HAQIQIY soni.</param>
/// <param name="TopFive">Kunning eng yirik beshta harakati (modul bo'yicha).</param>
/// <param name="ByType">Turlar kesimi (qarshi hisob bo'yicha).</param>
/// <param name="ByCategory">To'lov toifalari kesimi.</param>
/// <param name="AllocatedTotal">Toifalarga taqsimlangan summa (= ByCategory yig'indisi).</param>
/// <param name="UnallocatedTotal">Taqsimlanmagan qism (avans): to'lovlar − taqsimlanganlar.</param>
/// <param name="OpenShifts">Hozir ochiq smenalar. Bo'sh = kassa yopiq.</param>
public sealed record CashDayDto(
    DateOnly Date,
    CashDayAccountDto Total,
    List<CashDayAccountDto> Accounts,
    List<CashDayMovementDto> Movements,
    bool MovementsTruncated,
    int MovementsTotal,
    List<CashDayMovementDto> TopFive,
    List<CashDayTypeRowDto> ByType,
    List<CashDayCategoryRowDto> ByCategory,
    decimal AllocatedTotal,
    decimal UnallocatedTotal,
    List<CashDayShiftDto> OpenShifts);

/// <summary>Kalendar katakchasi — oyning bitta kuni.</summary>
/// <param name="Date">Kun.</param>
/// <param name="Inflow">Kirim (naqd + bank).</param>
/// <param name="Outflow">Chiqim (naqd + bank).</param>
/// <param name="Net">Kirim − chiqim. Katakchada AYNAN shu ko'rsatiladi.</param>
/// <param name="Closing">Kun oxiridagi qoldiq.</param>
/// <param name="HasMovement">false = o'sha kuni jurnalda bitta ham yozuv yo'q.</param>
public sealed record CashMonthDayDto(
    DateOnly Date, decimal Inflow, decimal Outflow, decimal Net, decimal Closing, bool HasMovement);

/// <summary>
/// Oylik kalendar: har kun uchun bitta katak. Harakatsiz kun ham qatorga
/// TUSHADI (qoldiq o'zgarmasdan ko'chadi) — aks holda grid'da teshik paydo
/// bo'lib, "o'sha kuni nima bo'ldi?" degan javobsiz savol qolardi.
/// </summary>
/// <param name="Month">Oyning birinchi kuni.</param>
/// <param name="Opening">Oy boshidagi qoldiq.</param>
/// <param name="Inflow">Oy kirimi.</param>
/// <param name="Outflow">Oy chiqimi.</param>
/// <param name="Net">Oy sof natijasi.</param>
/// <param name="Closing">Oy oxiridagi qoldiq.</param>
/// <param name="Days">Oyning har bir kuni, 1-sanadan oxirgisigacha.</param>
public sealed record CashMonthDto(
    DateOnly Month,
    decimal Opening, decimal Inflow, decimal Outflow, decimal Net, decimal Closing,
    List<CashMonthDayDto> Days);

/// <summary>
/// "Kassa kuni" paneli uchun so'rovlar — <b>hech narsa yozmaydi</b>: bu yerda
/// <c>SaveChanges</c> ham, <c>Add</c> ham yo'q, hamma so'rov <c>AsNoTracking</c>.
/// Nom shuni aytib turadi (<see cref="FinanceReportQueries"/> bilan bir xil
/// uslub).
///
/// <para>
/// <b>Unumdorlik.</b> Kunlik panel 6 ta so'rov yuboradi, kalendar — 2 ta.
/// Sikl ichida <c>await</c> YO'Q, ya'ni N+1 yo'q va tasodifan paydo bo'lishi
/// ham qiyin. Tayanadigan indeks — <c>ledger_entries (entry_date, account)</c>
/// (P1-05 migratsiyasida MAVJUD).
/// </para>
/// </summary>
public sealed class CashDayQueries(IAppDbContext db, ICashShiftService shifts)
{
    /// <summary>
    /// Haqiqiy pul hisoblari — SPEC §3.7 dagi ikkitasi. Tartib ham shu (UI
    /// aynan shu ketma-ketlikda chizadi). <c>string[]</c> ataylab: EF
    /// <c>Contains</c> ni massiv ustida <c>= ANY</c> ga tarjima qiladi.
    /// </summary>
    public static readonly string[] MoneyAccounts = [Accounts.Cash, Accounts.Bank];

    /// <summary>Pul <c>numeric(14,2)</c> — yig'indi ham tiyingacha yaxlitlanadi.</summary>
    private const int MoneyScale = 2;

    /// <summary>"Kunning eng yiriklari" nechta qator (§3.2: <c>top-five</c>).</summary>
    public const int TopCount = 5;

    /// <summary>
    /// Ro'yxatda ko'rsatiladigan eng ko'p harakat. Kunlik hajm bundan ancha
    /// kichik; chegara faqat "bir kun 10 000 qator chiqib qolsa" degan
    /// holatda sahifani o'ldirmaslik uchun. Yig'indilar BARIBIR to'liq
    /// ma'lumotdan hisoblanadi — kesilgani faqat ro'yxat.
    /// </summary>
    public const int MaxMovements = 500;

    // =====================================================================
    //  1) Kun
    // =====================================================================

    /// <summary>
    /// Bir kunning to'liq manzarasi: ochilish → kirim → chiqim → yopilish
    /// (naqd va bank alohida), harakatlar ro'yxati, eng yiriklari, turlar va
    /// toifalar kesimi, hozir ochiq smenalar.
    /// </summary>
    public async Task<CashDayDto> DayAsync(DateOnly date, CancellationToken ct = default)
    {
        // ---- So'rov 1: kun BOSHIGACHA bo'lgan qoldiq ----
        // `FinanceReportQueries.CashFlowAsync` dagi ta'rifning aynan o'zi,
        // faqat oy chegarasi o'rniga kun chegarasi.
        var openingRows = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate < date && MoneyAccounts.Contains(e.Account))
            .GroupBy(e => new { e.Account, e.Direction })
            .Select(g => new AccountTotal(g.Key.Account, g.Key.Direction, g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        // ---- So'rov 2: kunning BUTUN jurnali ----
        // Faqat `cash`/`bank` emas, HAMMA hisob olinadi — chunki harakatning
        // "turi" uning IKKINCHI oyog'idan o'qiladi (to'lov → `receivable`,
        // chiqim → `expense:*`). Bitta kunning partiyalari ikki satrdan
        // iborat, ya'ni bu yerda pul satrlaridan ikki barobar ko'p qator
        // keladi — bir kun uchun bu arzon va ikkinchi so'rovni yo'qotadi.
        var dayRows = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate == date)
            .OrderBy(e => e.Id)
            .Select(e => new LedgerRow(
                e.Id, e.Account, e.Direction, e.Amount, e.RefType, e.RefId, e.Memo,
                e.CreatedAt, e.RefType == LedgerRefType.Reversal))
            .ToListAsync(ct);

        var moneyRows = dayRows
            .Where(r => MoneyAccounts.Contains(r.Account, StringComparer.Ordinal))
            .ToList();

        // Partiyaning qarshi oyog'i: bir xil (RefType, RefId) dagi pul
        // BO'LMAGAN satr. Partiya har doim ikki satr (LedgerService qoidasi),
        // shuning uchun bu bir ma'noli.
        var counterByBatch = dayRows
            .Where(r => !MoneyAccounts.Contains(r.Account, StringComparer.Ordinal))
            .GroupBy(r => (r.RefType, r.RefId))
            .ToDictionary(g => g.Key, g => g.First().Account, BatchKeyComparer.Instance);

        // ---- So'rov 3-4: to'lov va chiqim tafsilotlari ----
        // Storno satrining `ref_id` si ASL yozuvni ko'rsatadi
        // (`LedgerService.ReverseAsync` ko'zguga originalning `ref_id` sini
        // beradi), shuning uchun storno ham xuddi shu ikki lug'atdan topiladi.
        var refIds = moneyRows.Where(r => r.RefId is not null).Select(r => r.RefId!.Value).Distinct().ToList();

        var payments = await PaymentsAsync(refIds, ct);
        var expenses = await ExpensesAsync(refIds, ct);

        var movements = moneyRows
            .Select(r => ToMovement(r, payments, expenses))
            .ToList();

        // ---- So'rov 5: toifalar kesimi ----
        var (byCategory, allocated) = await ByCategoryAsync(moneyRows, ct);

        // ---- So'rov 6 (× 3 ichki): hozir ochiq smenalar ----
        var openShifts = await OpenShiftsAsync(ct);

        var accounts = MoneyAccounts
            .Select(account => Summarise(account, openingRows, moneyRows))
            .ToList();

        var total = new CashDayAccountDto(
            Account: "total",
            Opening: accounts.Sum(a => a.Opening),
            Inflow: accounts.Sum(a => a.Inflow),
            Outflow: accounts.Sum(a => a.Outflow),
            Net: accounts.Sum(a => a.Net),
            Closing: accounts.Sum(a => a.Closing));

        // To'lovlar keltirgan pul (storno minus bilan). Taqsimlanmagan qism =
        // avans: pul keldi, lekin hali hech qaysi hisob-fakturaga biriktirilmadi.
        var paymentInflow = movements
            .Where(m => m.Kind == LedgerRefType.Payment || IsPaymentReversal(m, payments))
            .Sum(m => m.Signed);

        return new CashDayDto(
            Date: date,
            Total: total,
            Accounts: accounts,
            Movements: [.. movements.OrderByDescending(m => m.EntryId).Take(MaxMovements)],
            MovementsTruncated: movements.Count > MaxMovements,
            MovementsTotal: movements.Count,
            TopFive: [.. movements
                .OrderByDescending(m => m.Amount)
                .ThenByDescending(m => m.EntryId)
                .Take(TopCount)],
            ByType: TypeRows(movements, counterByBatch),
            ByCategory: byCategory,
            AllocatedTotal: allocated,
            UnallocatedTotal: decimal.Round(paymentInflow - allocated, MoneyScale),
            OpenShifts: openShifts);
    }

    // =====================================================================
    //  2) Oylik kalendar
    // =====================================================================

    /// <summary>
    /// Oyning har bir kuni uchun sof harakat va kun oxiridagi qoldiq.
    /// Katakcha bosilganda panel o'sha kunga qayta yuklanadi — shuning uchun
    /// bu yerda faqat YIG'INDI bor, tafsilot yo'q.
    /// </summary>
    /// <param name="month">Oyning istalgan kuni; birinchi kunga keltiriladi.</param>
    public async Task<CashMonthDto> MonthAsync(DateOnly month, CancellationToken ct = default)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        // ---- So'rov 1: oy boshigacha bo'lgan qoldiq ----
        var openingRows = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate < first && MoneyAccounts.Contains(e.Account))
            .GroupBy(e => e.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var opening = Net(
            openingRows.Sum(r => r.Direction == LedgerDirection.Debit ? r.Total : 0m),
            openingRows.Sum(r => r.Direction == LedgerDirection.Credit ? r.Total : 0m));

        // ---- So'rov 2: oy ichidagi harakat, kun × yo'nalish ----
        var movementRows = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate >= first && e.EntryDate <= last && MoneyAccounts.Contains(e.Account))
            .GroupBy(e => new { e.EntryDate, e.Direction })
            .Select(g => new { g.Key.EntryDate, g.Key.Direction, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var days = new List<CashMonthDayDto>(last.Day);
        var running = opening;

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var rows = movementRows.Where(r => r.EntryDate == day).ToList();
            var inflow = rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Total);
            var outflow = rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Total);
            var net = inflow - outflow;
            running = decimal.Round(running + net, MoneyScale);

            days.Add(new CashMonthDayDto(
                day,
                decimal.Round(inflow, MoneyScale),
                decimal.Round(outflow, MoneyScale),
                decimal.Round(net, MoneyScale),
                running,
                rows.Count > 0));
        }

        var monthIn = days.Sum(d => d.Inflow);
        var monthOut = days.Sum(d => d.Outflow);

        return new CashMonthDto(
            Month: first,
            Opening: opening,
            Inflow: monthIn,
            Outflow: monthOut,
            Net: decimal.Round(monthIn - monthOut, MoneyScale),
            Closing: running,
            Days: days);
    }

    // =====================================================================
    //  Ichki qism
    // =====================================================================

    /// <summary>
    /// Bitta pul hisobi bo'yicha ochilish → kirim → chiqim → yopilish.
    /// Debet = kirim, kredit = chiqim: <c>cash</c> va <c>bank</c> aktiv
    /// hisoblar, ya'ni ularning tabiiy tomoni DEBET.
    /// </summary>
    private static CashDayAccountDto Summarise(
        string account,
        IReadOnlyList<AccountTotal> openingRows,
        IReadOnlyList<LedgerRow> dayRows)
    {
        decimal openDebit = 0m, openCredit = 0m;
        foreach (var row in openingRows)
        {
            if (!string.Equals(row.Account, account, StringComparison.Ordinal)) continue;
            if (row.Direction == LedgerDirection.Debit) openDebit += row.Total;
            else openCredit += row.Total;
        }

        var opening = Net(openDebit, openCredit);

        var mine = dayRows.Where(r => string.Equals(r.Account, account, StringComparison.Ordinal)).ToList();
        var inflow = decimal.Round(mine.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount), MoneyScale);
        var outflow = decimal.Round(mine.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount), MoneyScale);
        var net = inflow - outflow;

        return new CashDayAccountDto(account, opening, inflow, outflow, net, decimal.Round(opening + net, MoneyScale));
    }

    private static decimal Net(decimal debit, decimal credit) => decimal.Round(debit - credit, MoneyScale);

    /// <summary>
    /// To'lov tafsilotlari — o'quvchi ismi, chek raqami, usul, kassir.
    /// Ismlar BITTA so'rovda olinadi (ro'yxat uzunligiga qaramay), chunki
    /// moliya entity'larida navigatsiya xossalari yo'q.
    /// </summary>
    private async Task<Dictionary<Guid, PaymentInfo>> PaymentsAsync(
        List<Guid> refIds, CancellationToken ct)
    {
        if (refIds.Count == 0) return [];

        var rows = await (
            from p in db.Payments.AsNoTracking()
            where refIds.Contains(p.Id)
            join s in db.Students.AsNoTracking() on p.StudentId equals s.Id into students
            from s in students.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on p.CashierId equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new PaymentInfo(
                p.Id, p.ReceiptNo, p.Method,
                s == null ? null : s.FullName,
                u == null ? null : u.FullName))
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.PaymentId);
    }

    /// <summary>Chiqim tafsilotlari — toifa, izoh, kiritgan xodim.</summary>
    private async Task<Dictionary<Guid, ExpenseInfo>> ExpensesAsync(
        List<Guid> refIds, CancellationToken ct)
    {
        if (refIds.Count == 0) return [];

        var rows = await (
            from e in db.Expenses.AsNoTracking()
            where refIds.Contains(e.Id)
            join u in db.Users.AsNoTracking() on e.CreatedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new ExpenseInfo(e.Id, e.Category, e.Note, u == null ? null : u.FullName))
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.ExpenseId);
    }

    /// <summary>
    /// Jurnal satrini ekrandagi qatorga aylantiradi. Tavsif (<c>Title</c>)
    /// SERVERDA yig'iladi — UI o'quvchi ismini yoki toifa nomini o'zi
    /// qidirib yurmasin.
    /// </summary>
    private static CashDayMovementDto ToMovement(
        LedgerRow row,
        IReadOnlyDictionary<Guid, PaymentInfo> payments,
        IReadOnlyDictionary<Guid, ExpenseInfo> expenses)
    {
        var isInflow = row.Direction == LedgerDirection.Debit;
        var signed = isInflow ? row.Amount : -row.Amount;

        PaymentInfo? payment = null;
        ExpenseInfo? expense = null;
        if (row.RefId is { } refId)
        {
            payments.TryGetValue(refId, out payment);
            if (payment is null) expenses.TryGetValue(refId, out expense);
        }

        // Chiqim toifasi yopiq ro'yxatdan (`Accounts.ExpenseCategories`), lekin
        // `ExpenseFor` noma'lum qiymatda ATAYLAB xato beradi. Ekran esa
        // yiqilmasligi kerak: toifa nomi topilmasa kodning o'zi ko'rsatiladi.
        var title = payment is not null
            ? payment.StudentName ?? "Noma'lum o'quvchi"
            : expense is not null
                ? Accounts.IsExpenseCategory(expense.Category)
                    ? MoneyFlowQueries.LabelFor(Accounts.ExpenseFor(expense.Category))
                    : expense.Category
                : KindLabel(row.RefType);

        if (row.IsReversal) title = $"STORNO — {title}";

        return new CashDayMovementDto(
            EntryId: row.Id,
            Account: row.Account,
            Direction: row.Direction,
            Amount: row.Amount,
            Signed: signed,
            Kind: row.RefType,
            IsReversal: row.IsReversal,
            RefId: row.RefId,
            Title: title,
            Memo: row.Memo,
            ReceiptNo: payment?.ReceiptNo,
            Method: payment?.Method,
            ActorName: payment?.CashierName ?? expense?.CreatedByName,
            Category: expense?.Category,
            CreatedAt: row.CreatedAt);
    }

    private static bool IsPaymentReversal(
        CashDayMovementDto movement, IReadOnlyDictionary<Guid, PaymentInfo> payments) =>
        movement.IsReversal && movement.RefId is { } id && payments.ContainsKey(id);

    /// <summary>
    /// Turlar kesimi: harakat QARSHI hisobi bo'yicha guruhlanadi
    /// (to'lov → <c>receivable</c>, chiqim → <c>expense:*</c>). Yangi
    /// taksonomiya o'ylab topilmaydi — hisoblar rejasi allaqachon yopiq
    /// ro'yxat (<see cref="Accounts"/>) va u yetarli.
    ///
    /// <para>
    /// Storno ALOHIDA qator bo'ladi, asl turga netlanmaydi: direktor
    /// "bugun 12 mln tushdi" va "shundan 2 mln qaytarildi" ni bitta 10 mln
    /// ichida ko'rmasligi kerak — aynan shu ikkinchi raqam uchun ekran
    /// ochiladi.
    /// </para>
    /// </summary>
    private static List<CashDayTypeRowDto> TypeRows(
        IReadOnlyList<CashDayMovementDto> movements,
        IReadOnlyDictionary<(string RefType, Guid? RefId), string> counterByBatch)
    {
        return [.. movements
            .Select(m =>
            {
                var account = counterByBatch.GetValueOrDefault((m.Kind, m.RefId)) ?? m.Kind;
                return new { Movement = m, Account = account };
            })
            .GroupBy(x => (x.Account, x.Movement.IsReversal))
            .Select(g => new CashDayTypeRowDto(
                Key: g.Key.IsReversal ? $"{g.Key.Account}:reversal" : g.Key.Account,
                Account: g.Key.Account,
                Label: TypeLabel(g.Key.Account, g.Key.IsReversal),
                IsReversal: g.Key.IsReversal,
                Count: g.Count(),
                Amount: decimal.Round(g.Sum(x => x.Movement.Signed), MoneyScale)))
            .OrderByDescending(r => Math.Abs(r.Amount))
            .ThenBy(r => r.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Qarshi hisobning o'zbekcha nomi. <c>revenue:*</c> va <c>expense:*</c>
    /// uchun <see cref="MoneyFlowQueries.LabelFor"/> ishlatiladi — nomlar
    /// ikki ekranda har xil bo'lib qolmasin.
    /// </summary>
    private static string TypeLabel(string account, bool isReversal)
    {
        var name = account switch
        {
            Accounts.Receivable => "O'quvchi to'lovlari",
            Accounts.Cash => "Kassa (naqd)",
            Accounts.Bank => "Bank hisobi",
            _ when account.StartsWith("revenue:", StringComparison.Ordinal)
                   || account.StartsWith("expense:", StringComparison.Ordinal)
                => MoneyFlowQueries.LabelFor(account),
            _ => KindLabel(account),
        };

        return isReversal ? $"{name} — storno" : name;
    }

    /// <summary>Jurnal manba turi (<see cref="LedgerRefType"/>) → o'zbekcha nom.</summary>
    private static string KindLabel(string refType) => refType switch
    {
        LedgerRefType.Payment => "To'lov",
        LedgerRefType.Invoice => "Hisob-faktura",
        LedgerRefType.Expense => "Chiqim",
        LedgerRefType.Salary => "Maosh",
        LedgerRefType.Reversal => "Storno",
        _ => "Boshqa harakat",
    };

    /// <summary>
    /// Kunning toifalar kesimi — "nimaga to'landi".
    ///
    /// <para>
    /// <b>Belgi KUN bo'yicha qo'yiladi, to'lovning hozirgi holati bo'yicha
    /// emas.</b> Shu kuni qabul qilingan to'lovning taqsimotlari PLUS bilan,
    /// shu kuni storno qilingan to'lovniki MINUS bilan kiradi. Sabab:
    /// <c>LedgerService.ReverseAsync</c> ko'zgu satrni bugungi sana bilan
    /// yozadi, ya'ni pul aynan BUGUN chiqib ketgan. Aks holda (agar
    /// <c>FinanceReportQueries.EffectiveAllocations</c> qoidasi qo'llansa)
    /// kecha chop etilgan kunlik hisobot bugun o'z-o'zidan o'zgarib qolardi —
    /// bu esa aynan SPEC §4.1 taqiqlaydigan "orqaga qarab tuzatish" ning
    /// ekrandagi ko'rinishi bo'lardi.
    /// </para>
    /// <para>
    /// <c>payment_allocations.amount &gt; 0</c> (baza check'i), shuning uchun
    /// storno o'z taqsimotini YOZMAYDI — minus shu yerda, ASL to'lovning
    /// taqsimotidan olinadi.
    /// </para>
    /// </summary>
    private async Task<(List<CashDayCategoryRowDto> Rows, decimal Total)> ByCategoryAsync(
        IReadOnlyList<LedgerRow> moneyRows, CancellationToken ct)
    {
        // Pul satri ikki xil bo'lishi mumkin: to'lovning o'zi (`payment`) yoki
        // uning ko'zgusi (`reversal`, `ref_id` — ASL to'lov). Ikkovi ham shu
        // yerda kerak, faqat belgisi qarama-qarshi.
        var sign = new Dictionary<Guid, decimal>();
        foreach (var row in moneyRows)
        {
            if (row.RefId is not { } refId) continue;
            if (row.RefType != LedgerRefType.Payment && row.RefType != LedgerRefType.Reversal) continue;

            sign[refId] = sign.GetValueOrDefault(refId) + (row.Direction == LedgerDirection.Debit ? 1m : -1m);
        }

        if (sign.Count == 0) return ([], 0m);

        var ids = sign.Keys.ToList();
        var rows = await (
            from a in db.PaymentAllocations.AsNoTracking()
            where ids.Contains(a.PaymentId)
            join inv in db.Invoices.AsNoTracking() on a.InvoiceId equals inv.Id
            join c in db.FeeCategories.AsNoTracking() on inv.CategoryId equals c.Id
            group a by new { a.PaymentId, CategoryId = c.Id, c.Code, c.Name } into g
            select new
            {
                g.Key.PaymentId,
                g.Key.CategoryId,
                g.Key.Code,
                g.Key.Name,
                Amount = g.Sum(x => x.Amount),
            })
            .ToListAsync(ct);

        var byCategory = rows
            .GroupBy(r => new { r.CategoryId, r.Code, r.Name })
            .Select(g => new CashDayCategoryRowDto(
                g.Key.CategoryId, g.Key.Code, g.Key.Name,
                decimal.Round(g.Sum(x => x.Amount * sign.GetValueOrDefault(x.PaymentId)), MoneyScale)))
            .Where(r => r.Amount != 0m)
            .OrderByDescending(r => r.Amount)
            .ThenBy(r => r.CategoryCode, StringComparer.Ordinal)
            .ToList();

        return (byCategory, byCategory.Sum(r => r.Amount));
    }

    /// <summary>
    /// Hozir ochiq smenalar.
    ///
    /// <para>
    /// <b>Raqamlar QAYTA HISOBLANMAYDI.</b> <c>CashShiftService</c> allaqachon
    /// "shu smenada qancha naqd bor" degan savolga javob beradi
    /// (<c>CashShiftDto.CashTotal</c>, to'lov usullari kesimidan, storno
    /// minus bilan) va uning yopilish paytidagi og'ir varianti
    /// (<c>ExpectedCashAsync</c>, jurnal bo'yicha) o'sha to'plamga MOS
    /// bo'lishi shart — bu o'sha faylda ochiq yozilgan qoida. Shuning uchun
    /// bu yerda <c>opening_float + cash_total</c> dan boshqa hech narsa
    /// qilinmaydi: ikkinchi ta'rif yaratilsa, panel bilan Z-hisobot bir kun
    /// kelib bir-biriga zid raqam ko'rsatardi.
    /// </para>
    /// <para>
    /// Smena YOPILGANDA esa haqiqiy <c>expected_cash</c> bazaga yoziladi va
    /// <c>variance</c> ni baza hisoblaydi (generated column) — bu panel u
    /// yerga umuman tegmaydi.
    /// </para>
    /// </summary>
    private async Task<List<CashDayShiftDto>> OpenShiftsAsync(CancellationToken ct)
    {
        var open = await shifts.ListAsync(new CashShiftQuery(Status: CashShiftStatus.Open), ct);

        return [.. open
            .OrderBy(s => s.OpenedAt)
            .Select(s => new CashDayShiftDto(
                ShiftId: s.Id,
                CashierName: s.CashierName,
                OpenedAt: s.OpenedAt,
                OpeningFloat: s.OpeningFloat,
                CashSoFar: s.CashTotal,
                ExpectedCashSoFar: decimal.Round(s.OpeningFloat + s.CashTotal, MoneyScale),
                NonCashSoFar: s.NonCashTotal,
                PaymentsCount: s.PaymentsCount))];
    }

    /// <summary>Jurnal satrining ekranga kerak bo'lgan qismi (bazadan shu ko'rinishda keladi).</summary>
    private sealed record LedgerRow(
        long Id, string Account, string Direction, decimal Amount, string RefType, Guid? RefId,
        string? Memo, DateTimeOffset CreatedAt, bool IsReversal);

    /// <summary>Hisob × yo'nalish kesimidagi yig'indi — bazadan guruhlangan holda keladi.</summary>
    private sealed record AccountTotal(string Account, string Direction, decimal Total);

    private sealed record PaymentInfo(
        Guid PaymentId, long ReceiptNo, string Method, string? StudentName, string? CashierName);

    private sealed record ExpenseInfo(Guid ExpenseId, string Category, string? Note, string? CreatedByName);

    /// <summary>
    /// Partiya kaliti — <c>(ref_type, ref_id)</c>. <c>Guid?</c> ustidagi
    /// sukut taqqoslash yetarli, lekin lug'at kaliti ANIQ nomlangan
    /// bo'lgani yaxshi: bu yerda "bir xil partiya" tushunchasi jurnalning
    /// o'zidan keladi (<c>LedgerService</c> partiya qoidasi).
    /// </summary>
    private sealed class BatchKeyComparer : IEqualityComparer<(string RefType, Guid? RefId)>
    {
        public static readonly BatchKeyComparer Instance = new();

        public bool Equals((string RefType, Guid? RefId) x, (string RefType, Guid? RefId) y) =>
            string.Equals(x.RefType, y.RefType, StringComparison.Ordinal) && x.RefId == y.RefId;

        public int GetHashCode((string RefType, Guid? RefId) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.RefType), obj.RefId);
    }
}
