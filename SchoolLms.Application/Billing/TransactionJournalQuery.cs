using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  TRANZAKSIYALAR JURNALI — FAQAT O'QISH.
//  (docs/modules/finance-parity.md §2.9, gap F9.01.)
// ===========================================================================
//
//  NEGA BU EKRAN BOR
//  -----------------
//  Bugungacha pul harakati UCHTA alohida ro'yxatda yashardi: to'lovlar
//  (`GET /api/billing/payments`, ekrani yo'q), chiqimlar (`/admin/expenses`)
//  va bitta kunning harakatlari (Kassa kuni). "Shu oyda kassaga nima kirdi va
//  nima chiqdi" degan savolga javob berish uchun uchala ro'yxatni qo'lda
//  qo'shish kerak edi. Bu ekran ularni BITTA jadvalga yig'adi.
//
//  BU YERDA YANGI RAQAM YO'Q
//  -------------------------
//  Hammasi `payments`, `expenses` va `ledger_entries` da allaqachon yotibdi.
//  Jurnal ularni faqat bitta tartibda ko'rsatadi va yakunini chiqaradi.
//  Hech narsa yozilmaydi: `AsNoTracking`, `SaveChanges` yo'q.
//
//  SANA O'QI — "BIZNES KUNI"
//  -------------------------
//  To'lov uchun kun = `received_at` maktab mintaqasida qaysi kunga tushsa
//  (`AppClock.LocalDateOf`); chiqim uchun kun = `on_date` (pul qaysi kuni
//  sarflangani, `ExpenseService.ListAsync` ham shu ustun bo'yicha filtrlaydi).
//  Filtr ham, tartib ham AYNAN shu o'q bo'yicha — aks holda "1-oktabrgacha"
//  filtri jadvalning o'zi ko'rsatgan sanadan boshqa narsani kesardi.
//
//  STORNO — IKKITA QATOR, CHIQIM — BITTA
//  -------------------------------------
//  To'lovni storno qilish `payments` ga YANGI qator qo'shadi (o'z chek raqami
//  bilan, `reversal_of` to'ldirilgan) — demak jurnalda ikkala qator ham
//  ko'rinadi: original "+", storno "−", sof 0. Chiqimni storno qilish esa
//  `expenses` ga qator QO'SHMAYDI (`ExpenseService.ReverseAsync` izohi):
//  ko'zgu satrlar faqat jurnalda. Shuning uchun storno qilingan chiqim bitta
//  qator bo'lib qolaveradi, holati "storno qilingan" bo'ladi va
//  <see cref="TransactionRowDto.SettledAmount"/> i NOLGA tushadi.
//
//  SHU SABABLI QATORDA IKKITA SUMMA BOR
//  ------------------------------------
//  · <c>Amount</c> — hujjatning O'Z summasi, ishorasi bilan. Jadvalda shu
//    ko'rinadi (storno qilingan chiqim ham o'z summasi bilan turadi, aks holda
//    "500 000 so'mlik chiqim bekor qilindi" degan qator "0" bo'lib ko'rinardi).
//  · <c>SettledAmount</c> — PUL HARAKATI: tasdiq kutayotgan va storno qilingan
//    chiqimda 0, qolganida <c>Amount</c> bilan bir xil. Yakunlar (§2.9 dagi
//    "Σ kirim, Σ chiqim, sof") AYNAN shundan yig'iladi, ya'ni jadvalning
//    pastidagi raqam haqiqatan qo'ldan chiqqan pulni ko'rsatadi.
//
//  YAKUN — SAHIFA BO'YICHA EMAS, FILTR BO'YICHA
//  --------------------------------------------
//  §2.9: "Σ kirim, Σ chiqim, sof for the whole filter, not the page".
//  Shuning uchun yakun sahifadagi qatorlardan EMAS, alohida agregat
//  so'rovlardan olinadi (`SumAsync`) — ya'ni 3-sahifada turgan foydalanuvchi
//  ham butun filtrning yakunini ko'radi.
//
//  SAHIFALASH IKKI MANBA USTIDA
//  ----------------------------
//  Ikkita jadvalni SQL'da UNION qilish EF Core'da (turlari har xil) ishonchsiz
//  chiqadi, shuning uchun klassik "merge" usuli ishlatiladi: har manbadan
//  AYNAN bir xil tartibda `skip + take` ta qator olinadi, xotirada qo'shilib
//  saralanadi, keyin kesiladi. Natija to'liq TO'G'RI (chegaralangan xotira
//  bilan): N-sahifadagi qator qaysi manbadan kelishidan qat'i nazar, undan
//  oldin ikkala manbada ham ko'pi bilan `skip + take` ta qator bo'lishi mumkin.

/// <summary>Jurnal qatorining turi (<see cref="TransactionRowDto.Kind"/>).</summary>
public static class TransactionKind
{
    /// <summary>Kassaga tushgan to'lov.</summary>
    public const string Payment = "payment";

    /// <summary>To'lovning stornosi — `payments` dagi `reversal_of` li qator.</summary>
    public const string Reversal = "reversal";

    /// <summary>Chiqim.</summary>
    public const string Expense = "expense";

    public static readonly IReadOnlyList<string> All = [Payment, Reversal, Expense];
}

/// <summary>Jurnal qatorining holati (<see cref="TransactionRowDto.Status"/>).</summary>
public static class TransactionStatus
{
    /// <summary>Kuchda: pul harakat qilgan va bekor qilinmagan.</summary>
    public const string Active = "active";

    /// <summary>Storno qilingan (original qator; storno qatorining o'zi `active`).</summary>
    public const string Reversed = "reversed";

    /// <summary>Tasdiq kutmoqda — faqat chiqimda bo'ladi (SPEC §4.5).</summary>
    public const string Pending = "pending";

    public static readonly IReadOnlyList<string> All = [Active, Reversed, Pending];
}

/// <summary>Pul yo'nalishi (<see cref="TransactionRowDto.Direction"/>).</summary>
public static class TransactionDirection
{
    public const string In = "in";
    public const string Out = "out";

    public static readonly IReadOnlyList<string> All = [In, Out];
}

/// <summary>Jurnalni saralash ustuni (<see cref="TransactionJournalFilter.Sort"/>).</summary>
public static class TransactionSort
{
    public const string Date = "date";
    public const string Amount = "amount";

    public static readonly IReadOnlyList<string> All = [Date, Amount];
}

/// <summary>
/// Jurnal filtri (§2.9). Hamma maydon ixtiyoriy; sukut — joriy oy,
/// yangisidan eskisiga.
/// </summary>
/// <param name="From">Biznes kuni shu sanadan boshlab (shu kun ham kiradi).</param>
/// <param name="To">Biznes kuni shu sanagacha (shu kun ham kiradi).</param>
/// <param name="Direction">kirim (<c>in</c>) yoki chiqim (<c>out</c>).</param>
/// <param name="Kind"><see cref="TransactionKind"/> qiymatlaridan biri.</param>
/// <param name="Method">To'lov usuli. Chiqimga <c>cash</c> → kassadan,
/// qolganlari → bankdan chiqqanlar mos keladi.</param>
/// <param name="ActorId">Kassir (to'lovda) yoki chiqimni yozgan/tasdiqlagan odam.</param>
/// <param name="StudentId">Bitta o'quvchi. Berilsa chiqimlar ro'yxatga tushmaydi.</param>
/// <param name="ClassName">Sinf. Berilsa chiqimlar ro'yxatga tushmaydi.</param>
/// <param name="Category">
/// Toifa kodi. To'lov uchun — taqsimotidagi hisob-faktura toifasi
/// (<c>tuition</c>, <c>bus</c>, …), chiqim uchun — chiqim toifasi
/// (<c>salary</c>, <c>rent</c>, …). Ikkala ro'yxatda ham <c>other</c> bor,
/// ya'ni u ikkala tomondan ham qator olib keladi — bu ATAYLAB: ekranda ham
/// yorliq bitta ("Boshqa").
/// </param>
/// <param name="Status"><see cref="TransactionStatus"/> qiymatlaridan biri.</param>
/// <param name="ReceiptNo">Chek raqami bo'yicha qidiruv. Berilsa chiqimlar
/// ro'yxatga tushmaydi — ularda chek raqami yo'q.</param>
/// <param name="OnlyFirstPayment">
/// F9.05 — faqat o'quvchining BIRINCHI (storno qilinmagan) to'lovi. Berilsa
/// chiqimlar ham, storno qatorlari ham ro'yxatga tushmaydi.
/// </param>
public record TransactionJournalFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    string? Direction = null,
    string? Kind = null,
    string? Method = null,
    string? ActorId = null,
    string? StudentId = null,
    string? ClassName = null,
    string? Category = null,
    string? Status = null,
    long? ReceiptNo = null,
    bool OnlyFirstPayment = false,
    int Page = 1,
    int PageSize = TransactionJournalQuery.DefaultPageSize,
    string Sort = TransactionSort.Date,
    bool Desc = true);

/// <summary>
/// Jurnalning bitta qatori — to'lov, storno yoki chiqim.
/// </summary>
/// <param name="Id">Manba yozuv id'si (<c>payments.id</c> yoki <c>expenses.id</c>).
/// Storno tugmasi shu id bilan chaqiriladi.</param>
/// <param name="Kind"><see cref="TransactionKind"/>.</param>
/// <param name="Direction"><see cref="TransactionDirection"/>.</param>
/// <param name="OccurredOn">Biznes kuni (fayl boshidagi "sana o'qi" izohi).</param>
/// <param name="OccurredAt">Aniq lahza — bir kun ichidagi tartib uchun.</param>
/// <param name="ReceiptNo">Chek raqami (faqat to'lov va storno).</param>
/// <param name="Amount">Hujjat summasi, ishorasi bilan (kirim +, chiqim −).</param>
/// <param name="SettledAmount">Pul harakati (fayl boshidagi "ikkita summa" izohi).</param>
/// <param name="PersonKind">student | teacher | null.</param>
/// <param name="Category">Toifa kodi (to'lovda bir nechta bo'lsa — birinchisi).</param>
/// <param name="CategoryLabel">Ekranda ko'rinadigan toifa matni.</param>
/// <param name="Account">Jurnal hisobi (<c>expense:rent</c>, <c>revenue:*</c> …).</param>
/// <param name="Method">To'lov usuli yoki chiqimning pul hisobi (<c>cash</c>/<c>bank</c>).</param>
/// <param name="Status"><see cref="TransactionStatus"/>.</param>
/// <param name="ReversalOf">Storno qatorida — bekor qilingan to'lov id'si.</param>
/// <param name="ReversedBy">Storno qilingan to'lovda — storno qatori id'si.</param>
public record TransactionRowDto(
    Guid Id,
    string Kind,
    string Direction,
    DateOnly OccurredOn,
    DateTimeOffset OccurredAt,
    long? ReceiptNo,
    decimal Amount,
    decimal SettledAmount,
    string? PersonId,
    string? PersonName,
    string? PersonKind,
    string? ClassName,
    string? Category,
    string? CategoryLabel,
    string? Account,
    string? Method,
    string? ActorId,
    string? ActorName,
    string? Note,
    string Status,
    Guid? ReversalOf,
    Guid? ReversedBy);

/// <summary>Filtr bo'yicha (sahifa bo'yicha EMAS) yakun.</summary>
/// <param name="TotalIn">Σ kirim — storno qilinmagan to'lovlar.</param>
/// <param name="TotalOut">Σ chiqim — to'lov stornolari + jurnalga tushgan chiqimlar.</param>
/// <param name="Net"><see cref="TotalIn"/> − <see cref="TotalOut"/>.</param>
/// <param name="PendingOut">Tasdiq kutayotgan chiqimlar — yakunga KIRMAYDI,
/// lekin ekranda alohida ko'rsatiladi (pul hali chiqmagan).</param>
public record TransactionTotalsDto(
    decimal TotalIn, decimal TotalOut, decimal Net, decimal PendingOut);

/// <summary>Jurnalning bitta sahifasi.</summary>
public record TransactionJournalPageDto(
    IReadOnlyList<TransactionRowDto> Rows,
    int Page,
    int PageSize,
    int Total,
    TransactionTotalsDto Totals);

/// <inheritdoc cref="TransactionJournalPageDto"/>
public sealed class TransactionJournalQuery(IAppDbContext db)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>
    /// Eksport chegarasi. Filtrsiz eksport butun bazani xotiraga tortmasin;
    /// undan kattasi kerak bo'lsa davrni toraytirish kerak.
    /// </summary>
    public const int MaxExportRows = 5000;

    /// <summary>
    /// Maktab mintaqasi ofseti — <c>received_at</c> (<c>timestamptz</c>) ni
    /// KALENDAR kuni bo'yicha kesish uchun.
    ///
    /// <para>
    /// AYNI ta'rif <c>PaymentService.SchoolOffset</c> da ham bor (u yerda
    /// <c>private</c>). Ikkovi bir xil ekanini
    /// <c>TransactionJournalTests.Kun_chegarasi_PaymentService_bilan_bir_xil</c>
    /// tekshiradi: jurnalning kun chegarasi to'lovlar ro'yxatinikidan
    /// farq qilsa, bitta to'lov ikki ekranda ikki xil kunga tushib qolardi.
    /// </para>
    /// </summary>
    private static readonly TimeSpan SchoolOffset =
        AppClock.ToLocal(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch.UtcDateTime;

    /// <summary>Maktab kunining boshlanish lahzasi.</summary>
    public static DateTimeOffset StartOfSchoolDay(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), SchoolOffset).ToUniversalTime();

    // =================================================================
    //  Ommaviy yuza
    // =================================================================

    /// <summary>Bitta sahifa + butun filtr bo'yicha yakun.</summary>
    public async Task<TransactionJournalPageDto> PageAsync(
        TransactionJournalFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var skip = (page - 1) * size;

        var total = await CountAsync(filter, ct);
        var totals = await TotalsAsync(filter, ct);

        // Merge-sahifalash: har manbadan bir xil tartibda `skip + take` ta.
        var window = skip + size;
        var rows = await MergedAsync(filter, window, ct);

        return new TransactionJournalPageDto(
            [.. rows.Skip(skip).Take(size)], page, size, total, totals);
    }

    /// <summary>
    /// Butun filtr bo'yicha qatorlar (xlsx eksporti, F9.03). Ko'pi bilan
    /// <see cref="MaxExportRows"/> ta.
    /// </summary>
    public async Task<IReadOnlyList<TransactionRowDto>> ExportRowsAsync(
        TransactionJournalFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var rows = await MergedAsync(filter, MaxExportRows, ct);
        return [.. rows.Take(MaxExportRows)];
    }

    // =================================================================
    //  Manbalar
    // =================================================================

    /// <summary>
    /// Filtrga mos to'lovlar (storno qatorlari ham shu jadvalda).
    /// </summary>
    private IQueryable<Payment> PaymentsFor(TransactionJournalFilter f)
    {
        var q = db.Payments.AsNoTracking();

        if (f.Kind == TransactionKind.Expense) return q.Where(_ => false);

        if (f.From is { } from) { var lo = StartOfSchoolDay(from); q = q.Where(p => p.ReceivedAt >= lo); }
        if (f.To is { } to) { var hi = StartOfSchoolDay(to.AddDays(1)); q = q.Where(p => p.ReceivedAt < hi); }

        q = f.Kind switch
        {
            TransactionKind.Payment => q.Where(p => p.ReversalOf == null),
            TransactionKind.Reversal => q.Where(p => p.ReversalOf != null),
            _ => q,
        };

        // Kirim = to'lov, chiqim = storno (pul kassadan qaytib chiqadi).
        q = f.Direction switch
        {
            TransactionDirection.In => q.Where(p => p.ReversalOf == null),
            TransactionDirection.Out => q.Where(p => p.ReversalOf != null),
            _ => q,
        };

        q = f.Status switch
        {
            // Storno qatorining O'ZI kuchda: u haqiqiy pul harakati.
            TransactionStatus.Active => q.Where(p =>
                p.ReversalOf != null || !db.Payments.Any(r => r.ReversalOf == p.Id)),
            TransactionStatus.Reversed => q.Where(p =>
                p.ReversalOf == null && db.Payments.Any(r => r.ReversalOf == p.Id)),
            // "Tasdiq kutmoqda" faqat chiqimda bo'ladi.
            TransactionStatus.Pending => q.Where(_ => false),
            _ => q,
        };

        if (!string.IsNullOrWhiteSpace(f.Method)) q = q.Where(p => p.Method == f.Method);
        if (!string.IsNullOrWhiteSpace(f.ActorId)) q = q.Where(p => p.CashierId == f.ActorId);
        if (!string.IsNullOrWhiteSpace(f.StudentId)) q = q.Where(p => p.StudentId == f.StudentId);
        if (f.ReceiptNo is { } receiptNo) q = q.Where(p => p.ReceiptNo == receiptNo);

        if (!string.IsNullOrWhiteSpace(f.ClassName))
            q = q.Where(p => db.Students.Any(s => s.Id == p.StudentId && s.ClassName == f.ClassName));

        if (!string.IsNullOrWhiteSpace(f.Category))
            q = q.Where(p => db.PaymentAllocations.Any(a => a.PaymentId == p.Id
                && db.Invoices.Any(i => i.Id == a.InvoiceId
                    && db.FeeCategories.Any(c => c.Id == i.CategoryId && c.Code == f.Category))));

        // F9.05 — o'quvchining birinchi to'lovi. "Birinchi" = eng erta
        // KUCHDAGI to'lov: storno qilingani hisobga olinmaydi, aks holda
        // xato kiritilib darhol bekor qilingan to'lov "birinchi" bo'lib
        // qolardi va o'quvchi ro'yxatdan butunlay yo'qolardi.
        if (f.OnlyFirstPayment)
            q = q.Where(p => p.ReversalOf == null
                && !db.Payments.Any(r => r.ReversalOf == p.Id)
                && !db.Payments.Any(o => o.StudentId == p.StudentId
                    && o.ReversalOf == null
                    && !db.Payments.Any(r2 => r2.ReversalOf == o.Id)
                    && o.ReceivedAt < p.ReceivedAt));

        return q;
    }

    /// <summary>Filtrga mos chiqimlar.</summary>
    private IQueryable<Expense> ExpensesFor(TransactionJournalFilter f)
    {
        var q = db.Expenses.AsNoTracking();

        // Chiqimda o'quvchi ham, chek raqami ham yo'q — bu filtrlarning
        // biri qo'yilgan bo'lsa chiqim javobga umuman TUSHMAYDI (bo'sh
        // ustunli qator ko'rsatish o'rniga).
        if (f.Kind is TransactionKind.Payment or TransactionKind.Reversal) return q.Where(_ => false);
        if (f.Direction == TransactionDirection.In) return q.Where(_ => false);
        if (!string.IsNullOrWhiteSpace(f.StudentId)) return q.Where(_ => false);
        if (!string.IsNullOrWhiteSpace(f.ClassName)) return q.Where(_ => false);
        if (f.ReceiptNo is not null) return q.Where(_ => false);
        if (f.OnlyFirstPayment) return q.Where(_ => false);

        if (f.From is { } from) q = q.Where(e => e.OnDate >= from);
        if (f.To is { } to) q = q.Where(e => e.OnDate <= to);

        if (!string.IsNullOrWhiteSpace(f.Category))
        {
            var category = f.Category.Trim().ToLowerInvariant();
            q = q.Where(e => e.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(f.ActorId))
            q = q.Where(e => e.CreatedBy == f.ActorId || e.ApprovedBy == f.ActorId);

        // Usul chiqimda ustun emas — u jurnalning KREDIT satrida yashaydi
        // (`ExpenseService` izohi: "usul ustuni yo'q"). Naqd usul `cash`
        // hisobiga, qolgan uchtasi `bank` ga tushadi.
        if (!string.IsNullOrWhiteSpace(f.Method))
        {
            var settlement = Accounts.SettlementFor(f.Method);
            q = q.Where(e => db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Expense
                && l.RefId == e.Id
                && l.Direction == LedgerDirection.Credit
                && l.Account == settlement));
        }

        // Holat ustun emas — jurnaldan kelib chiqadi (`ExpenseService.ListAsync`
        // dagi AYNAN o'sha uchta predikat).
        q = f.Status switch
        {
            TransactionStatus.Pending => q.Where(e =>
                !db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Expense && l.RefId == e.Id)),
            TransactionStatus.Active => q.Where(e =>
                db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Expense && l.RefId == e.Id)
                && !db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Reversal && l.RefId == e.Id)),
            TransactionStatus.Reversed => q.Where(e =>
                db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Reversal && l.RefId == e.Id)),
            _ => q,
        };

        return q;
    }

    // =================================================================
    //  Yakun va soni — FILTR bo'yicha, sahifadan mustaqil
    // =================================================================

    private async Task<int> CountAsync(TransactionJournalFilter f, CancellationToken ct) =>
        await PaymentsFor(f).CountAsync(ct) + await ExpensesFor(f).CountAsync(ct);

    private async Task<TransactionTotalsDto> TotalsAsync(
        TransactionJournalFilter f, CancellationToken ct)
    {
        var payments = PaymentsFor(f);

        var totalIn = await payments.Where(p => p.ReversalOf == null)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var stornoOut = await payments.Where(p => p.ReversalOf != null)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var expenses = ExpensesFor(f);

        // Jurnalga tushgan va storno qilinmagan chiqimlargina pulni kamaytiradi.
        var expenseOut = await expenses
            .Where(e => db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Expense && l.RefId == e.Id)
                        && !db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Reversal && l.RefId == e.Id))
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var pendingOut = await expenses
            .Where(e => !db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Expense && l.RefId == e.Id))
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var outTotal = stornoOut + expenseOut;
        return new TransactionTotalsDto(totalIn, outTotal, totalIn - outTotal, pendingOut);
    }

    // =================================================================
    //  Qatorlarni yig'ish
    // =================================================================

    /// <summary>
    /// Ikkala manbadan <paramref name="window"/> ta qator olib, bitta tartibga
    /// qo'shadi. Tartib ikkala tomonda ham AYNAN bir xil bo'lishi shart,
    /// aks holda merge noto'g'ri qator tanlardi.
    /// </summary>
    private async Task<List<TransactionRowDto>> MergedAsync(
        TransactionJournalFilter f, int window, CancellationToken ct)
    {
        var byAmount = string.Equals(f.Sort, TransactionSort.Amount, StringComparison.Ordinal);

        var paymentRows = await TakePaymentsAsync(f, window, byAmount, ct);
        var expenseRows = await TakeExpensesAsync(f, window, byAmount, ct);

        var all = new List<TransactionRowDto>(paymentRows.Count + expenseRows.Count);
        all.AddRange(paymentRows);
        all.AddRange(expenseRows);

        var ordered = byAmount
            ? (f.Desc
                ? all.OrderByDescending(r => r.Amount).ThenByDescending(r => r.OccurredAt)
                     .ThenByDescending(r => r.Id)
                : all.OrderBy(r => r.Amount).ThenBy(r => r.OccurredAt).ThenBy(r => r.Id))
            : (f.Desc
                ? all.OrderByDescending(r => r.OccurredOn).ThenByDescending(r => r.OccurredAt)
                     .ThenByDescending(r => r.Id)
                : all.OrderBy(r => r.OccurredOn).ThenBy(r => r.OccurredAt).ThenBy(r => r.Id));

        return [.. ordered];
    }

    private async Task<List<TransactionRowDto>> TakePaymentsAsync(
        TransactionJournalFilter f, int window, bool byAmount, CancellationToken ct)
    {
        // SARALASH PROYEKSIYADAN OLDIN. `Select` dan keyin tartiblasak, EF
        // Core `OrderBy(new PaymentRaw(...).ReceivedAt)` ni SQL ga tarjima
        // qila olmaydi va butun so'rov yiqiladi (amalda uchradi).
        var q = PaymentsFor(f);

        // Ishorali summa: storno pulni chiqaradi.
        q = byAmount
            ? (f.Desc
                ? q.OrderByDescending(p => p.ReversalOf == null ? p.Amount : -p.Amount)
                    .ThenByDescending(p => p.ReceivedAt).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.ReversalOf == null ? p.Amount : -p.Amount)
                    .ThenBy(p => p.ReceivedAt).ThenBy(p => p.Id))
            : (f.Desc
                ? q.OrderByDescending(p => p.ReceivedAt).ThenByDescending(p => p.Id)
                : q.OrderBy(p => p.ReceivedAt).ThenBy(p => p.Id));

        var raw = await q
            .Take(window)
            .Select(p => new PaymentRaw(
                p.Id, p.ReceiptNo, p.StudentId, p.Amount, p.Method,
                p.CashierId, p.Note, p.ReceivedAt, p.ReversalOf,
                db.Payments.Where(r => r.ReversalOf == p.Id).Select(r => (Guid?)r.Id).FirstOrDefault()))
            .ToListAsync(ct);
        if (raw.Count == 0) return [];

        return await EnrichPaymentsAsync(raw, ct);
    }

    private async Task<List<TransactionRowDto>> TakeExpensesAsync(
        TransactionJournalFilter f, int window, bool byAmount, CancellationToken ct)
    {
        var q = ExpensesFor(f);

        // Chiqimning ishorali summasi MANFIY (−amount), shuning uchun "katta
        // summadan kichigiga" tartibi `Amount` bo'yicha O'SISHI bilan bir xil.
        q = byAmount
            ? (f.Desc
                ? q.OrderBy(e => e.Amount).ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                : q.OrderByDescending(e => e.Amount).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id))
            : (f.Desc
                ? q.OrderByDescending(e => e.OnDate).ThenByDescending(e => e.CreatedAt)
                    .ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.OnDate).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id));

        var raw = await q.Take(window).ToListAsync(ct);
        if (raw.Count == 0) return [];

        return await EnrichExpensesAsync(raw, ct);
    }

    /// <summary>To'lov qatori — o'quvchi, sinf, toifalar va kassir ismi bitta so'rovda.</summary>
    private async Task<List<TransactionRowDto>> EnrichPaymentsAsync(
        List<PaymentRaw> raw, CancellationToken ct)
    {
        var ids = raw.Select(p => p.Id).ToList();
        var studentIds = raw.Select(p => p.StudentId).Distinct().ToList();
        var actorIds = raw.Select(p => p.CashierId).Distinct().ToList();

        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName })
            .ToListAsync(ct);
        var studentById = students.ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);

        var actors = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, StringComparer.Ordinal, ct);

        var categories = await (from a in db.PaymentAllocations.AsNoTracking()
                                join i in db.Invoices.AsNoTracking() on a.InvoiceId equals i.Id
                                join c in db.FeeCategories.AsNoTracking() on i.CategoryId equals c.Id
                                where ids.Contains(a.PaymentId)
                                select new { a.PaymentId, c.Code, c.Name })
                               .Distinct()
                               .ToListAsync(ct);

        return [.. raw.Select(p =>
        {
            var isReversal = p.ReversalOf is not null;
            var signed = isReversal ? -p.Amount : p.Amount;
            var mine = categories.Where(c => c.PaymentId == p.Id)
                .OrderBy(c => c.Code, StringComparer.Ordinal).ToList();
            var student = studentById.GetValueOrDefault(p.StudentId);

            return new TransactionRowDto(
                p.Id,
                isReversal ? TransactionKind.Reversal : TransactionKind.Payment,
                isReversal ? TransactionDirection.Out : TransactionDirection.In,
                AppClock.LocalDateOf(p.ReceivedAt),
                p.ReceivedAt,
                p.ReceiptNo,
                signed,
                // To'lov va storno — ikkalasi ham HAQIQIY pul harakati.
                signed,
                p.StudentId,
                student?.FullName ?? "—",
                "student",
                student?.ClassName,
                mine.Count > 0 ? mine[0].Code : null,
                mine.Count > 0 ? string.Join(", ", mine.Select(c => c.Name)) : "Avans",
                null,
                p.Method,
                p.CashierId,
                actors.GetValueOrDefault(p.CashierId, "—"),
                p.Note,
                // Storno qatorining O'ZI kuchda; storno qilingan original — `reversed`.
                !isReversal && p.ReversedBy is not null
                    ? TransactionStatus.Reversed
                    : TransactionStatus.Active,
                p.ReversalOf,
                p.ReversedBy);
        })];
    }

    /// <summary>
    /// Chiqim qatori. Holat, pul hisobi va storno ma'lumoti JURNALDAN olinadi —
    /// <c>ExpenseService.ToDtosAsync</c> dagi AYNAN o'sha qoida bo'yicha
    /// (kredit satri pul qayerdan chiqqanini, debet satri qaysi hisobga
    /// yozilganini aytadi).
    /// </summary>
    private async Task<List<TransactionRowDto>> EnrichExpensesAsync(
        List<Expense> raw, CancellationToken ct)
    {
        var ids = raw.Select(e => e.Id).ToList();

        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(l => l.RefId != null
                        && ids.Contains(l.RefId.Value)
                        && (l.RefType == LedgerRefType.Expense || l.RefType == LedgerRefType.Reversal))
            .Select(l => new
            {
                RefId = l.RefId!.Value,
                l.RefType,
                l.Account,
                l.Direction,
                l.EntryDate,
                l.CreatedBy,
                l.Memo,
            })
            .ToListAsync(ct);

        var actorIds = raw.Select(e => e.CreatedBy)
            .Concat(raw.Select(e => e.ApprovedBy).Where(x => x is not null).Select(x => x!))
            .Concat(entries.Select(l => l.CreatedBy))
            .Distinct()
            .ToList();

        var actors = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, StringComparer.Ordinal, ct);

        var teacherIds = raw.Select(e => e.TeacherId).Where(x => x is not null).Select(x => x!)
            .Distinct().ToList();
        var teachers = teacherIds.Count == 0
            ? []
            : await db.Teachers.AsNoTracking()
                .Where(t => teacherIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FullName })
                .ToDictionaryAsync(t => t.Id, t => t.FullName, StringComparer.Ordinal, ct);

        return [.. raw.Select(e =>
        {
            var mine = entries.Where(l => l.RefId == e.Id).ToList();
            var settlement = mine.FirstOrDefault(
                l => l.RefType == LedgerRefType.Expense && l.Direction == LedgerDirection.Credit);
            var posting = mine.FirstOrDefault(
                l => l.RefType == LedgerRefType.Expense && l.Direction == LedgerDirection.Debit);
            var reversal = mine.FirstOrDefault(l => l.RefType == LedgerRefType.Reversal);

            var status = reversal is not null ? TransactionStatus.Reversed
                : settlement is not null ? TransactionStatus.Active
                : TransactionStatus.Pending;

            // Yozuvni jurnalga kim qo'ygan bo'lsa, javobgar o'sha (tasdiqlovchi);
            // hali tushmagan bo'lsa — yozgan odam.
            var actorId = settlement?.CreatedBy ?? e.CreatedBy;

            var note = reversal?.Memo is { } reason
                ? string.IsNullOrWhiteSpace(e.Note) ? $"Storno: {reason}" : $"{e.Note} · Storno: {reason}"
                : e.Note;

            return new TransactionRowDto(
                e.Id,
                TransactionKind.Expense,
                TransactionDirection.Out,
                e.OnDate,
                e.CreatedAt,
                null,
                -e.Amount,
                // Tasdiq kutayotgan va storno qilingan chiqimda pul harakat
                // qilmagan (fayl boshidagi "ikkita summa" izohi).
                status == TransactionStatus.Active ? -e.Amount : 0m,
                e.TeacherId,
                e.TeacherId is null ? null : teachers.GetValueOrDefault(e.TeacherId, "—"),
                e.TeacherId is null ? null : "teacher",
                null,
                e.Category,
                null,
                posting?.Account
                    ?? (Accounts.IsExpenseCategory(e.Category)
                        ? Accounts.ExpenseFor(e.Category)
                        : Accounts.ExpenseOther),
                settlement?.Account,
                actorId,
                actors.GetValueOrDefault(actorId, "—"),
                note,
                status,
                null,
                null);
        })];
    }

    /// <summary>Bazadan o'qilgan xom to'lov qatori (proyeksiya).</summary>
    private sealed record PaymentRaw(
        Guid Id, long ReceiptNo, string StudentId, decimal Amount, string Method,
        string CashierId, string? Note, DateTimeOffset ReceivedAt,
        Guid? ReversalOf, Guid? ReversedBy);
}
