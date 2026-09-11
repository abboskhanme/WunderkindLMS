using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Moliya hisobotlari — FAQAT O'QISH. Vazifa: P1-13 (SPEC §3.7, §4.3, §7).
// ===========================================================================
//
//  ENG MUHIM QOIDA — O'QUVCHI QATORIDAGI SAQLANGAN QOLDIQQA HAVOLA YO'Q
//  --------------------------------------------------------------------
//  Eski tizimda qarz o'quvchi qatoridagi saqlangan ustundan o'qilardi va u
//  OLTI joyda qo'lda o'zgartirilardi (docs/TASKS.md §1.2 va §1.4 — "What is
//  replaced"). Bitta yangilanish o'tkazib yuborilsa, raqam JIMGINA buziladi:
//  xato ham chiqmaydi, hisobot ham "to'g'ri" ko'rinadi. Shuning uchun bu
//  yerdagi har bir raqam HAR SAFAR manba jadvallardan hisoblanadi:
//
//      qarz = Σ(invoices.amount − invoices.discount) − Σ(payment_allocations.amount)
//
//  Buni mashina tekshiradi: shu faylda o'sha ustun nomi UMUMAN uchramaydi
//  (P1-13 qabul mezoni). P1-21 da ustunning o'zi o'ladi; bu fayl o'sha kunni
//  o'zgarishsiz kutadi.
//
//  NEGA SERVICE EMAS, "QUERIES"
//  ----------------------------
//  Bu klass hech narsa YOZMAYDI — `SaveChanges` ham, `Add` ham yo'q, hamma
//  so'rov `AsNoTracking`. Nom shuni aytib turadi. `IBillingServices.cs`
//  (muzlatilgan) da bunga interfeys yo'q, shuning uchun bu yerda ham
//  yaratilmadi: bitta implementatsiyaga bitta interfeys — ortiqcha qavat.
//
//  UNUMDORLIK (SPEC §7: 500 ms)
//  ----------------------------
//  Har bir metod AYNIQSA sanab bo'ladigan miqdordagi so'rov yuboradi
//  (1 yoki 2 ta), o'quvchilar/oylar soniga BOG'LIQ EMAS. Ya'ni N+1 yo'q va
//  paydo bo'lishi ham qiyin: sikl ichida `await` bor joy bu faylda yo'q.
//  Tayanadigan indekslar (hammasi P1-05 migratsiyasida MAVJUD):
//    invoices (student_id, category_id, period_month) · (status, due_on) · (period_month)
//    payment_allocations (invoice_id) · (payment_id)
//    payments (reversal_of) where reversal_of is not null
//    ledger_entries (entry_date, account)
//
//  STORNO (reversal) — MOLIYADAGI ENG OSON YASHIRIN XATO
//  ----------------------------------------------------
//  To'lov o'chirilmaydi, storno bilan tuzatiladi (SPEC §4.1): asl qator
//  joyida qoladi, ustiga `reversal_of` bilan qarshi qator qo'shiladi.
//  Taqsimotlar (`payment_allocations`) esa O'CHMAYDI va summasi musbat
//  bo'lishi shart. Demak "to'langan" ni shunchaki Σ allocations deb olsak,
//  BEKOR QILINGAN to'lov ham to'langan bo'lib qolardi — qarz kam ko'rinardi.
//  Shuning uchun <see cref="FinanceReportQueries.EffectiveAllocations"/>
//  ikkala tomonni ham chiqarib tashlaydi: storno qatorining o'zini ham,
//  storno qilingan asl to'lovni ham.

/// <summary>
/// Qarzdorlar hisoboti filtri. Hammasi ixtiyoriy — sukut bo'yicha "qarzi
/// bor barcha o'quvchilar" qaytadi.
/// </summary>
/// <param name="ClassName">Sinf (aniq moslik). null = barcha sinflar.</param>
/// <param name="MinDebt">Shu summadan kam qarz ko'rsatilmaydi. Sukut 0.01 —
/// ya'ni qarzi yo'qlar va avansi borlar ro'yxatga TUSHMAYDI.</param>
/// <param name="OnlyOverdue">true = faqat muddati o'tganlar
/// (<c>overdue_after_day</c> sozlamasi bo'yicha).</param>
/// <param name="IncludeArchived">Sukut true: maktabdan ketgan o'quvchining
/// qarzi ham qarz. UI kerak bo'lsa false bilan yashiradi.</param>
public record DebtorReportQuery(
    string? ClassName = null,
    decimal MinDebt = 0.01m,
    bool OnlyOverdue = false,
    bool IncludeArchived = true);

/// <summary>P&amp;L ning bitta satri: hisob kodi va davr bo'yicha sof summasi.</summary>
/// <param name="Account">Hisob kodi (<see cref="Accounts"/> yopiq ro'yxatidan).</param>
/// <param name="Amount">Daromad uchun kredit−debet, chiqim uchun debet−kredit.
/// Storno qarshi tomonga yozilgani uchun bu ayirma AVTOMATIK sof qiymat beradi.</param>
public record ProfitLossLineDto(string Account, decimal Amount);

/// <summary>
/// Foyda va zarar (P&amp;L) — <c>revenue:*</c> va <c>expense:*</c> hisoblari
/// kesimida. <see cref="Net"/> = <see cref="RevenueTotal"/> −
/// <see cref="ExpenseTotal"/>.
/// </summary>
public record ProfitLossDto(
    DateOnly From, DateOnly To,
    List<ProfitLossLineDto> Revenue, decimal RevenueTotal,
    List<ProfitLossLineDto> Expense, decimal ExpenseTotal,
    decimal Net);

/// <summary>Pul harakatining bitta oyi (bitta hisob bo'yicha).</summary>
/// <param name="Month">Oyning birinchi kuni.</param>
/// <param name="Opening">Oy boshidagi qoldiq.</param>
/// <param name="Inflow">Kirim (debet).</param>
/// <param name="Outflow">Chiqim (kredit).</param>
/// <param name="Net">Kirim − chiqim.</param>
/// <param name="Closing">Oy oxiridagi qoldiq = Opening + Net.</param>
public record CashFlowMonthDto(
    DateOnly Month, decimal Opening, decimal Inflow, decimal Outflow, decimal Net, decimal Closing);

/// <summary>Bitta hisob (<c>cash</c> yoki <c>bank</c>) bo'yicha butun davr va oylar kesimi.</summary>
public record CashFlowAccountDto(
    string Account, decimal Opening, decimal Inflow, decimal Outflow, decimal Net, decimal Closing,
    List<CashFlowMonthDto> Months);

/// <summary>
/// Pul oqimi (Cash Flow): <c>cash</c> va <c>bank</c> hisoblarining harakati,
/// oylar kesimida. Davr boshidagi qoldiq (<see cref="Opening"/>) davrdan
/// OLDINGI hamma yozuvdan hisoblanadi — aks holda "qoldiq" ma'nosini
/// yo'qotardi.
/// </summary>
public record CashFlowDto(
    DateOnly From, DateOnly To,
    decimal Opening, decimal Inflow, decimal Outflow, decimal Net, decimal Closing,
    List<CashFlowAccountDto> Accounts);

/// <summary>
/// Moliya hisobotlari (P1-13): qarzdorlar, P&amp;L, pul oqimi, yig'ilish darajasi.
///
/// <para>
/// Faylning boshidagi izoh — qoidalar. Qisqasi: o'quvchi qatoridagi
/// saqlangan qoldiq ustuniga murojaat YO'Q, yozish YO'Q, sikl ichida
/// so'rov YO'Q.
/// </para>
/// </summary>
public sealed class FinanceReportQueries(IAppDbContext db)
{
    /// <summary>Daromad hisoblari shu prefiks bilan boshlanadi (SPEC §3.7).</summary>
    public const string RevenuePrefix = "revenue:";

    /// <summary>Chiqim hisoblari shu prefiks bilan boshlanadi (SPEC §3.7).</summary>
    public const string ExpensePrefix = "expense:";

    /// <summary>Pul oqimi ko'rsatiladigan hisoblar — SPEC §3.7 dagi ikkita "haqiqiy pul" hisobi.</summary>
    private static readonly string[] CashAccounts = [Accounts.Cash, Accounts.Bank];

    /// <summary>Foiz ikki kasr bilan — pul ustuni <c>numeric(14,2)</c> bo'lgani kabi.</summary>
    private const int RateScale = 2;

    /// <summary>Pul oqimi so'rovida ruxsat etilgan eng uzun davr (10 yil).</summary>
    public const int MaxCashFlowMonths = 120;

    // =====================================================================
    //  1) Qarzdorlar — har o'quvchi bitta qator, toifalar kesimi bilan
    // =====================================================================

    /// <summary>
    /// Qarzdorlar hisoboti. Har o'quvchi uchun BITTA qator va uning ichida
    /// toifalar bo'yicha yoyilma (o'qish / avtobus / yotoqxona / ovqat / boshqa).
    ///
    /// <para>
    /// Qarz manbai FAQAT ikki jadval: <c>invoices</c> va <c>payment_allocations</c>.
    /// Bekor qilingan (<c>void</c>) hisob-faktura hisobga KIRMAYDI — u "xato
    /// hisoblangan oy" degani.
    /// </para>
    /// <para>
    /// <b>Invariant:</b> qator ichidagi <c>ByCategory</c> yig'indisi
    /// <c>Debt</c> ga TENG. Shuning uchun manfiy toifa (avans) ham
    /// yoyilmada qoladi: uni yashirsak, ekranda qo'shilmaydigan ikki raqam
    /// paydo bo'lardi.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<DebtorRowDto>> DebtorsAsync(
        DebtorReportQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var students = db.Students.AsNoTracking();
        if (!query.IncludeArchived) students = students.Where(s => !s.IsArchived);
        if (!string.IsNullOrWhiteSpace(query.ClassName))
        {
            var className = query.ClassName.Trim();
            students = students.Where(s => s.ClassName == className);
        }

        // ---- So'rov 1: o'quvchi × toifa kesimida HISOBLANGAN summa ----
        // Guruhlash BAZADA bo'ladi: 500 o'quvchi × 10 oy × 5 toifa = 25 000
        // qator kirib, ~2 500 qator chiqadi. Xotiraga xom hisob-fakturalarni
        // tortish shart emas.
        var accrued = await (
            from inv in BillableInvoices()
            join s in students on inv.StudentId equals s.Id
            join c in db.FeeCategories.AsNoTracking() on inv.CategoryId equals c.Id
            group inv by new
            {
                inv.StudentId,
                s.FullName,
                s.ClassName,
                s.ParentPhone,
                inv.CategoryId,
                CategoryCode = c.Code,
                CategoryName = c.Name,
            }
            into g
            select new
            {
                g.Key.StudentId,
                g.Key.FullName,
                g.Key.ClassName,
                g.Key.ParentPhone,
                g.Key.CategoryId,
                g.Key.CategoryCode,
                g.Key.CategoryName,
                Payable = g.Sum(x => x.Amount - x.Discount),
            }).ToListAsync(ct);

        // ---- So'rov 2: O'SHA kesimda haqiqatan to'langan summa ----
        // Taqsimotlar tomonidan guruhlanadi (hisob-faktura tomonidan emas):
        // shunda bir hisob-fakturaga tushgan bir necha to'lov ham to'g'ri
        // qo'shiladi va hech narsa ikki marta sanalmaydi.
        var paid = await (
            from a in EffectiveAllocations()
            join inv in BillableInvoices() on a.InvoiceId equals inv.Id
            join s in students on inv.StudentId equals s.Id
            group a by new { inv.StudentId, inv.CategoryId }
            into g
            select new { g.Key.StudentId, g.Key.CategoryId, Paid = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var paidByKey = paid.ToDictionary(x => (x.StudentId, x.CategoryId), x => x.Paid);

        // ---- So'rov 3: eng eski to'lanmagan oy va eng erta muddat ----
        // Bu yerda guruhlash o'quvchi kesimida va FAQAT qoldig'i bor
        // hisob-fakturalar bo'yicha — 1-so'rovga sig'maydi.
        var effective = EffectiveAllocations();
        var unpaid = await (
            from inv in BillableInvoices()
            join s in students on inv.StudentId equals s.Id
            where inv.Amount - inv.Discount > effective.Where(a => a.InvoiceId == inv.Id).Sum(a => a.Amount)
            group inv by inv.StudentId
            into g
            select new
            {
                StudentId = g.Key,
                OldestMonth = g.Min(x => x.PeriodMonth),
                EarliestDue = g.Min(x => x.DueOn),
            }).ToListAsync(ct);

        var unpaidByStudent = unpaid.ToDictionary(x => x.StudentId);

        // Muddat sozlamalari (SPEC §8.1 Q6) — qat'iy raqam emas, bitta qatordan o'qiladi.
        var settings = await db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var graceDays = GraceDays(settings);
        var today = AppClock.Today;

        // Ikki kesimni xotirada birlashtiramiz: ~2 500 qator, bitta lug'at
        // qidiruvi. Bazada LEFT JOIN bilan qilsa ham bo'lardi, lekin EF
        // guruhlangan quyi so'rovga LEFT JOIN ni tarjima qila olmaydi.
        var byCategory = accrued
            .Select(r => new
            {
                r.StudentId,
                r.FullName,
                r.ClassName,
                r.ParentPhone,
                r.CategoryCode,
                r.CategoryName,
                Debt = r.Payable - paidByKey.GetValueOrDefault((r.StudentId, r.CategoryId)),
            })
            .ToList();

        var rows = byCategory
            .GroupBy(r => r.StudentId)
            .Select(g =>
            {
                var first = g.First();
                var debt = g.Sum(x => x.Debt);
                unpaidByStudent.TryGetValue(g.Key, out var oldest);

                return new DebtorRowDto(
                    StudentId: g.Key,
                    FullName: first.FullName,
                    ClassName: first.ClassName,
                    ParentPhone: first.ParentPhone,
                    Debt: debt,
                    OldestUnpaidMonth: oldest?.OldestMonth,
                    DaysOverdue: oldest is null ? 0 : DaysOverdue(oldest.EarliestDue, graceDays, today),
                    ByCategory: [.. g
                        .Where(x => x.Debt != 0m)
                        .OrderByDescending(x => x.Debt)
                        .ThenBy(x => x.CategoryCode, StringComparer.Ordinal)
                        .Select(x => new DebtorCategoryRowDto(x.CategoryCode, x.CategoryName, x.Debt))]);
            })
            .Where(r => r.Debt >= query.MinDebt)
            .Where(r => !query.OnlyOverdue || r.DaysOverdue > 0)
            .OrderByDescending(r => r.Debt)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

        return rows;
    }

    // =====================================================================
    //  2) P&L — daromad va chiqim, hisob prefiksi bo'yicha
    // =====================================================================

    /// <summary>
    /// Foyda va zarar: <c>ledger_entries</c> ni akkaunt prefiksi bo'yicha
    /// yig'adi (<c>revenue:*</c> va <c>expense:*</c>).
    ///
    /// <para>
    /// <b>Nega debet/kredit ayirmasi, shunchaki yig'indi emas.</b> Daromad
    /// hisobi kredit tabiatli: hisob-faktura yozilganda <c>credit revenue:*</c>
    /// bo'ladi. Storno esa AYNAN qarshi tomonga yoziladi (<c>debit revenue:*</c>).
    /// Ayirma olinsa bekor qilingan daromad o'z-o'zidan chiqib ketadi;
    /// shunchaki <c>sum(amount)</c> olinsa — ikki baravar bo'lib ko'rinardi.
    /// </para>
    /// </summary>
    public async Task<ProfitLossDto> ProfitLossAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        RequireRange(from, to);

        // Bitta so'rov. Hisoblar soni yopiq ro'yxat bilan chegaralangan (10 ta),
        // ya'ni bu yerdan ko'pi bilan 20 qator qaytadi — qolgani xotirada.
        var grouped = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate >= from && e.EntryDate <= to)
            .GroupBy(e => new { e.Account, e.Direction })
            .Select(g => new { g.Key.Account, g.Key.Direction, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var totals = grouped.Select(r => new AccountTotal(r.Account, r.Direction, r.Total)).ToList();

        var revenue = Lines(totals, RevenuePrefix, creditPositive: true);
        var expense = Lines(totals, ExpensePrefix, creditPositive: false);

        var revenueTotal = revenue.Sum(l => l.Amount);
        var expenseTotal = expense.Sum(l => l.Amount);

        return new ProfitLossDto(
            from, to,
            revenue, revenueTotal,
            expense, expenseTotal,
            revenueTotal - expenseTotal);
    }

    // =====================================================================
    //  3) Cash Flow — `cash` va `bank` harakati, oylar kesimida
    // =====================================================================

    /// <summary>
    /// Pul oqimi: <c>cash</c> va <c>bank</c> hisoblarining oylar kesimidagi
    /// harakati va qoldig'i.
    ///
    /// <para>
    /// Harakatsiz oy ham qatorga TUSHADI (qoldiq o'zgarmasdan ko'chadi) —
    /// grafikda uzilish bo'lmasligi va "o'sha oyda nima bo'ldi?" degan savol
    /// tug'ilmasligi uchun.
    /// </para>
    /// </summary>
    public async Task<CashFlowDto> CashFlowAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        RequireRange(from, to);

        var firstMonth = FirstDayOfMonth(from);
        var monthCount = MonthsBetween(from, to);
        if (monthCount > MaxCashFlowMonths)
            throw new ArgumentOutOfRangeException(
                nameof(to), monthCount,
                $"Davr juda uzun: {monthCount} oy. Ruxsat etilgani — {MaxCashFlowMonths} oy.");

        // ---- So'rov 1: davr BOSHIGACHA bo'lgan qoldiq ----
        var openingGrouped = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate < from && CashAccounts.Contains(e.Account))
            .GroupBy(e => new { e.Account, e.Direction })
            .Select(g => new { g.Key.Account, g.Key.Direction, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var openingRows = openingGrouped
            .Select(r => new AccountTotal(r.Account, r.Direction, r.Total))
            .ToList();

        // ---- So'rov 2: davr ichidagi harakat, oy × hisob × yo'nalish ----
        // Oyni BAZADA ajratamiz (`date_part`), keyin xotirada DateOnly ga
        // yig'amiz: `new DateOnly(y, m, 1)` ni EF SQL ga tarjima qila olmaydi.
        var movementRows = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.EntryDate >= from && e.EntryDate <= to && CashAccounts.Contains(e.Account))
            .GroupBy(e => new { e.EntryDate.Year, e.EntryDate.Month, e.Account, e.Direction })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Account,
                g.Key.Direction,
                Total = g.Sum(x => x.Amount),
            })
            .ToListAsync(ct);

        var accounts = new List<CashFlowAccountDto>(CashAccounts.Length);
        foreach (var account in CashAccounts)
        {
            var opening = Net(openingRows.Where(r => r.Account == account), debitPositive: true);

            var months = new List<CashFlowMonthDto>(monthCount);
            var running = opening;
            var month = firstMonth;

            for (var i = 0; i < monthCount; i++)
            {
                var rows = movementRows
                    .Where(r => r.Account == account && r.Year == month.Year && r.Month == month.Month)
                    .ToList();

                var inflow = rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Total);
                var outflow = rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Total);
                var net = inflow - outflow;
                var closing = running + net;

                months.Add(new CashFlowMonthDto(month, running, inflow, outflow, net, closing));

                running = closing;
                month = month.AddMonths(1);
            }

            var totalIn = months.Sum(m => m.Inflow);
            var totalOut = months.Sum(m => m.Outflow);
            accounts.Add(new CashFlowAccountDto(
                account, opening, totalIn, totalOut, totalIn - totalOut, running, months));
        }

        return new CashFlowDto(
            From: from,
            To: to,
            Opening: accounts.Sum(a => a.Opening),
            Inflow: accounts.Sum(a => a.Inflow),
            Outflow: accounts.Sum(a => a.Outflow),
            Net: accounts.Sum(a => a.Net),
            Closing: accounts.Sum(a => a.Closing),
            Accounts: accounts);
    }

    // =====================================================================
    //  4) Yig'ilish darajasi — hisoblangan va yig'ilgan, oylar kesimida
    // =====================================================================

    /// <summary>
    /// Oylar kesimida hisoblangan (<c>Accrued</c>) va yig'ilgan
    /// (<c>Collected</c>) summa hamda ularning nisbati.
    ///
    /// <para>
    /// <b>Oy — HISOB-FAKTURA oyi</b> (<c>invoices.period_month</c>), pul
    /// kelgan kun emas. Ya'ni "sentyabr: 100 mln hisoblandi, 82 mln yig'ildi,
    /// 82%" degani sentyabr uchun to'langan pul — oktyabrda to'lansa ham
    /// sentyabr qatoriga tushadi. Ikki sabab: (1) shunda qatorning qoldig'i
    /// AYNAN qarzdorlar hisobotidagi o'sha oyning qarzi bo'ladi, ikki ekran
    /// bir-biriga mos tushadi; (2) <c>period_month</c> — <c>date</c>, ya'ni
    /// mintaqa muammosi yo'q, <c>payments.received_at</c> esa
    /// <c>timestamptz</c> va oy chegarasida Toshkent/UTC farqi bitta to'lovni
    /// qo'shni oyga tashlab yuborardi.
    /// </para>
    /// <para>
    /// "Qaysi oyda qancha PUL tushdi" degan savolga
    /// <see cref="CashFlowAsync"/> javob beradi (<c>cash</c> + <c>bank</c>
    /// kirimi) — shuning uchun bu yerda takrorlanmaydi.
    /// </para>
    /// </summary>
    /// <param name="from">Boshlang'ich oy (kuni ahamiyatsiz). null = eng erta oydan.</param>
    /// <param name="to">Oxirgi oy (kuni ahamiyatsiz). null = eng oxirgi oygacha.</param>
    public async Task<IReadOnlyList<BillingMonthlyDto>> CollectionRateAsync(
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        if (from is { } f0 && to is { } t0) RequireRange(f0, t0);

        var invoices = BillableInvoices();
        if (from is { } f) invoices = invoices.Where(i => i.PeriodMonth >= FirstDayOfMonth(f));
        if (to is { } t) invoices = invoices.Where(i => i.PeriodMonth <= FirstDayOfMonth(t));

        // ---- So'rov 1: oyga hisoblangan (chegirmadan keyin) ----
        var accrued = await invoices
            .GroupBy(i => i.PeriodMonth)
            .Select(g => new { PeriodMonth = g.Key, Accrued = g.Sum(x => x.Amount - x.Discount) })
            .ToListAsync(ct);

        // ---- So'rov 2: o'sha oyning hisob-fakturalariga tushgan pul ----
        var collected = await (
            from a in EffectiveAllocations()
            join inv in invoices on a.InvoiceId equals inv.Id
            group a by inv.PeriodMonth
            into g
            select new { PeriodMonth = g.Key, Collected = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var collectedByMonth = collected.ToDictionary(x => x.PeriodMonth, x => x.Collected);

        return [.. accrued
            .OrderBy(r => r.PeriodMonth)
            .Select(r =>
            {
                var paid = collectedByMonth.GetValueOrDefault(r.PeriodMonth);
                return new BillingMonthlyDto(
                    r.PeriodMonth,
                    r.Accrued,
                    paid,
                    r.Accrued == 0m ? null : decimal.Round(paid / r.Accrued * 100m, RateScale));
            })];
    }

    // =====================================================================
    //  Umumiy qism
    // =====================================================================

    /// <summary>
    /// Qarzga KIRADIGAN hisob-fakturalar. <c>void</c> — "xato hisoblangan oy",
    /// u na qarzga, na hisoblangan summaga kiradi.
    ///
    /// <para>
    /// Qarz ikki bosqichda hisoblanadi: shu yerdan hisoblangan summa, ayrim
    /// so'rovdan esa <see cref="EffectiveAllocations"/> bo'yicha to'langan
    /// summa. Ikkovi bir kesimda (o'quvchi × toifa yoki oy) guruhlanib,
    /// xotirada ayriladi. Bitta so'rovda LEFT JOIN bilan qilish mumkin
    /// emas: EF Core guruhlangan quyi so'rovga <c>GroupJoin …
    /// DefaultIfEmpty</c> ni SQL ga tarjima qila olmaydi.
    /// </para>
    /// </summary>
    private IQueryable<Invoice> BillableInvoices() =>
        db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Void);

    /// <summary>
    /// HAQIQATAN kuchda bo'lgan taqsimotlar: storno qatorining taqsimoti ham,
    /// storno qilingan asl to'lovning taqsimoti ham chiqarib tashlanadi.
    ///
    /// <para>
    /// Fayl boshidagi izohga qarang — bu filtr bo'lmasa bekor qilingan to'lov
    /// hisobotda "to'langan" bo'lib qolaveradi. <c>payments.reversal_of</c>
    /// ustida shartli unikal indeks bor (P1-05), shuning uchun ikkinchi
    /// <c>NOT EXISTS</c> arzon.
    /// </para>
    /// </summary>
    private IQueryable<PaymentAllocation> EffectiveAllocations() =>
        db.PaymentAllocations.AsNoTracking()
            // (1) qatorning o'zi storno to'loviga tegishli;
            .Where(a => !db.Payments.Any(p => p.Id == a.PaymentId && p.ReversalOf != null))
            // (2) asl to'lov keyinchalik storno qilingan.
            .Where(a => !db.Payments.Any(r => r.ReversalOf == a.PaymentId));

    /// <summary>
    /// Prefiksga mos hisoblar bo'yicha satrlar. Yopiq ro'yxatdagi hamma hisob
    /// qatnashadi (harakat bo'lmasa 0 bilan) — hisobot ustunlari oydan oyga
    /// o'zgarib ketmasin. Ro'yxatda YO'Q, lekin bazada uchragan hisob ham
    /// qo'shiladi: pulni jimgina yo'qotgandan ko'ra, kutilmagan qator
    /// ko'rsatilgani yaxshi.
    /// </summary>
    private static List<ProfitLossLineDto> Lines(
        IReadOnlyList<AccountTotal> totals, string prefix, bool creditPositive)
    {
        var rows = totals.Where(r => r.Account.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        var accounts = Accounts.All
            .Where(a => a.StartsWith(prefix, StringComparison.Ordinal))
            .Concat(rows.Select(r => r.Account))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return [.. accounts
            .Select(account =>
            {
                var net = Net(rows.Where(r => r.Account == account), debitPositive: !creditPositive);
                return new ProfitLossLineDto(account, net);
            })
            .OrderByDescending(l => l.Amount)
            .ThenBy(l => l.Account, StringComparer.Ordinal)];
    }

    /// <summary>Debet va kredit ayirmasi — tabiiy tomoni <paramref name="debitPositive"/> bilan beriladi.</summary>
    private static decimal Net(IEnumerable<AccountTotal> rows, bool debitPositive)
    {
        decimal debit = 0m, credit = 0m;
        foreach (var row in rows)
        {
            if (row.Direction == LedgerDirection.Debit) debit += row.Total;
            else credit += row.Total;
        }
        return debitPositive ? debit - credit : credit - debit;
    }

    /// <summary>
    /// Muddat o'tishigacha beriladigan kunlar: <c>overdue_after_day −
    /// payment_due_day</c>. Sozlama qatori bo'lmasa — SPEC §8.1 Q6 dagi
    /// sukut qiymatlar (10 va 15).
    ///
    /// <para>
    /// Nega <c>invoices.due_on</c> + shu kunlar, "oyning 15-kuni" emas:
    /// <c>due_on</c> hisob-faktura yozilgan paytdagi sozlama bilan qotib
    /// qolgan. Sozlama keyin o'zgarsa, eski hisob-fakturaning muddati
    /// orqaga qarab siljib ketmasligi kerak.
    /// </para>
    /// </summary>
    private static int GraceDays(BillingSettings? settings)
    {
        var dueDay = settings?.PaymentDueDay ?? 10;
        var overdueDay = settings?.OverdueAfterDay ?? 15;
        return Math.Max(0, overdueDay - dueDay);
    }

    private static int DaysOverdue(DateOnly earliestDue, int graceDays, DateOnly today)
    {
        var overdueOn = earliestDue.AddDays(graceDays);
        var days = today.DayNumber - overdueOn.DayNumber;
        return days > 0 ? days : 0;
    }

    private static void RequireRange(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new ArgumentOutOfRangeException(
                nameof(to), to, $"Davr oxiri boshidan oldin bo'lishi mumkin emas ({from} … {to}).");
    }

    private static DateOnly FirstDayOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>
    /// Davr necha OYNI qamraydi (ikkala chekka ham kiradi). Controller ham
    /// shu hisobni ishlatadi — <see cref="MaxCashFlowMonths"/> ni tekshirib
    /// 400 qaytarish uchun; arifmetika ikki joyda takrorlanmasin.
    /// </summary>
    public static int MonthsBetween(DateOnly from, DateOnly to)
    {
        var first = FirstDayOfMonth(from);
        var last = FirstDayOfMonth(to);
        return ((last.Year - first.Year) * 12) + last.Month - first.Month + 1;
    }

    /// <summary>
    /// Bitta hisob × yo'nalish kesimidagi yig'indi — bazadan guruhlangan
    /// holda keladi (P&amp;L da ham, pul oqimining boshlang'ich qoldig'ida ham).
    /// </summary>
    private sealed record AccountTotal(string Account, string Direction, decimal Total);
}
