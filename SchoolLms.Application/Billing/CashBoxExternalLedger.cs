using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  KASSA JAVONIDAGI PUL — `cash_box_transactions` DAN TASHQARIDAGI QATORLAR.
//  FAQAT O'QISH (hech narsa yozilmaydi: `AsNoTracking`, `SaveChanges` yo'q).
// ===========================================================================
//
//  NEGA BU FAYL BOR (ishlab chiqarishdagi nosozlik, 2026-09-22)
//  -----------------------------------------------------------
//  Kassir "Kirim" bosib, o'quvchini tanlab, to'lovni saqlaydi. Pul
//  o'quvchining balansiga tushadi (to'g'ri), lekin Kassa ekrani "0 so'm" va
//  "Tanlangan davrda tranzaksiya yo'q" deb turaveradi. Sabab: kassaning
//  balansi ham, jurnali ham FAQAT `cash_box_transactions` dan o'qilardi,
//  o'quvchi to'lovi esa u yerga umuman yozilmaydi — u `payments` qatori
//  bo'lib, kassaga `payments.cash_box_id` orqali bog'lanadi (chek raqami ham
//  kassa bo'yicha uzluksiz: `ix_payments_cash_box_id_receipt_no`). Xuddi shu
//  holat `expenses.cash_box_id` (naqd chiqim) va `student_refunds.cash_box_id`
//  (naqd qaytarim) da ham bor.
//
//  Ya'ni YOZISH yo'li to'g'ri ishlagan, O'QISH yo'li esa uchta manbaning
//  bittasini ko'rgan. Bu fayl qolgan ikkitasini (uchtasini) qo'shadi —
//  yozish yo'liga BIR QATOR HAM tegmasdan.
//
//  `cash_box_id` NULL BO'LGAN QATOR HECH QAYSI KASSAGA TEGISHLI EMAS
//  ----------------------------------------------------------------
//  Bu ATAYLAB qo'yilgan qoida (`CashBoxModel.cs`): smena davridagi eski
//  qatorlarda kassa YO'Q va ular "qaysidir kassadir" deb TAXMIN QILINMAYDI —
//  aks holda bugungi balans orqaga qarab buzilardi. Shuning uchun har uchala
//  so'rov ham `cash_box_id is not null` bilan boshlanadi.
//
//  ISHORA QOIDASI — BITTA JOYDA
//  ----------------------------
//  · to'lov (`payments`)          → kassaga pul KIRADI  (+)
//  · to'lov STORNOSI              → o'sha qatorning o'zi manfiy (−), ya'ni
//                                   storno qilingan juftlik NOLGA tushadi.
//                                   Storno BOSHQA kassaga yozilgan bo'lishi
//                                   mumkin (`PaymentService.ReverseAsync` —
//                                   ko'rsatilmasa SUKUT kassa): pul haqiqatan
//                                   O'SHA kassadan qaytariladi, shuning uchun
//                                   minus ham o'sha kassaga tushadi.
//  · chiqim (`expenses`)          → pul CHIQADI (−). Storno qilingani
//                                   (jurnalda `ref_type = 'reversal'`) NOLGA
//                                   tushadi: `expenses` da storno QATORI yo'q
//                                   (`ExpenseService` izohi), shuning uchun
//                                   ayirish o'rniga qator umuman sanalmaydi.
//  · qaytarim (`student_refunds`) → pul CHIQADI (−), lekin FAQAT TASDIQLANGANI
//                                   (`approved_at` to'la, `rejected_reason`
//                                   bo'sh): so'ralgan yoki rad etilgan
//                                   qaytarimda javondan bir tiyin ham
//                                   chiqmagan. Tasdiqlangan STORNO qatori
//                                   pulni QAYTARADI (+).
//
//  USUL (method) KESIMI
//  --------------------
//  `payments` va `student_refunds` da `method` ustuni bor. `expenses` da
//  YO'Q — va kerak ham emas: kassa chiqimga FAQAT naqdda biriktiriladi
//  (`ExpenseService.AttachCashBoxAsync`: naqd bo'lmasa `cash_box_id = null`,
//  chunki pul bank hisobidan chiqadi). Ya'ni kassasi bor chiqim — har doim
//  `cash`.
//
//  BALANS UCHUN — AGREGAT, RO'YXAT UCHUN — QATORLAR
//  ------------------------------------------------
//  <see cref="TotalsAsync"/> yig'indini BAZADA hisoblaydi (`GROUP BY`):
//  kassalar ro'yxati har ochilganda 50 000 ta to'lov qatorini xotiraga
//  tortib olish — bir yildan keyin shu ekranni o'ldiradigan yo'l.
//  <see cref="RowsAsync"/> esa jurnal uchun qatorlarning O'ZINI oladi,
//  lekin HAR MANBADAN ko'pi bilan <c>maxRows</c> ta (birlashtirilgandan keyin
//  chaqiruvchi yana kesadi — `TransactionJournalQuery` dagi "merge" naqshi).

/// <summary>
/// Kassa jurnalidagi qator TURI (<see cref="CashBoxTransactionRowDto.Kind"/>).
///
/// <para>
/// Birinchi to'rttasi bazadagi ustunning o'zi
/// (<see cref="CashBoxTransactionKind"/>), bu yerdagi uchtasi esa
/// HISOBLANADI: ular `cash_box_transactions` da emas, `payments`,
/// `expenses` va `student_refunds` da yotadi. Frontend yorliqlari —
/// <c>schoollms.client/src/pages/cashier/format.ts</c> (<c>kindLabel</c>).
/// </para>
/// </summary>
public static class CashBoxLedgerKind
{
    /// <summary>O'quvchining to'lovi — `payments`, `cash_box_id` shu kassa.</summary>
    public const string StudentPayment = "student_payment";

    /// <summary>Naqd chiqim — `expenses`, `cash_box_id` shu kassa.</summary>
    public const string Expense = "expense";

    /// <summary>O'quvchiga qaytarim — `student_refunds`, `cash_box_id` shu kassa.</summary>
    public const string Refund = "refund";

    public static readonly IReadOnlyList<string> All = [StudentPayment, Expense, Refund];
}

/// <summary>
/// Jurnal qatorining KO'RSATILADIGAN holati
/// (<see cref="CashBoxTransactionRowDto.Status"/>) — bazadagi
/// <see cref="CashBoxTransactionStatus"/> dan FARQLI: u faqat qatorning O'ZI
/// storno ekanini bildiradi, "bekor qilindi" esa boshqa qatordan (storno
/// havolasidan) hisoblanadi. Qoida <c>CashBoxService.DisplayStatus</c> da.
/// </summary>
public static class CashBoxRowStatus
{
    /// <summary>Kuchda — bekor qilinmagan.</summary>
    public const string Posted = "posted";

    /// <summary>Bekor qilingan (ASL qator; uni bekor qilgan storno bor).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Bu qatorning O'ZI — boshqasining stornosi.</summary>
    public const string Reversal = "reversal";

    public static readonly IReadOnlyList<string> All = [Posted, Cancelled, Reversal];
}

/// <summary>
/// Kassaga tegishli, lekin `cash_box_transactions` dan TASHQARIDAGI bitta pul
/// qatori (to'lov, chiqim yoki qaytarim) — balans va jurnal uchun YAGONA shakl.
/// </summary>
/// <param name="Amount">Hujjatdagi summa — HAR DOIM musbat (bazadagi <c>amount &gt; 0</c>).</param>
/// <param name="Signed">Shu kassaning javoniga TA'SIRI (+ kirdi, − chiqdi, 0 — bekor qilingan).</param>
/// <param name="At">Pul HARAKAT QILGAN lahza: to'lovda `received_at`, chiqimda
/// `created_at`, qaytarimda `approved_at`.</param>
/// <param name="Who">Jadvaldagi "KIM" ustuni: to'lov va qaytarimda — O'QUVCHI,
/// chiqimda — chiqimni yozgan xodim.</param>
/// <param name="TypeName">"Tranzaksiya turi" ustuni — chiqimda toifa nomi, qolganida <c>null</c>.</param>
/// <param name="ReceiptNo">Chek raqami (faqat to'lov; kassa bo'yicha uzluksiz).</param>
internal sealed record CashBoxExternalRow(
    Guid Id,
    Guid BoxId,
    string Kind,
    decimal Amount,
    decimal Signed,
    string Method,
    DateTimeOffset At,
    string Status,
    string Who,
    string? ContractNo,
    string? TypeName,
    string? Note,
    string? CancelReason,
    long? ReceiptNo);

/// <summary>Bitta kassaning bitta usul bo'yicha tashqi yig'indisi (ishorasi bilan).</summary>
internal sealed record CashBoxExternalTotal(Guid BoxId, string Method, decimal Amount);

/// <summary>
/// `payments` / `expenses` / `student_refunds` ni KASSA ko'zi bilan o'qiydi.
/// Batafsil — fayl boshidagi izoh.
/// </summary>
internal sealed class CashBoxExternalLedger(IAppDbContext db)
{
    // =====================================================================
    //  Balans — bazada yig'iladi
    // =====================================================================

    /// <summary>
    /// <paramref name="boxIds"/> kassalarining tashqi manbalardagi yig'indisi,
    /// usul kesimida va ISHORASI bilan. Qator soni qanday bo'lsin — BESHTA
    /// agregat so'rov (to'lov ×2, chiqim, qaytarim ×2).
    /// </summary>
    public async Task<List<CashBoxExternalTotal>> TotalsAsync(
        IReadOnlyList<Guid> boxIds, CancellationToken ct)
    {
        if (boxIds.Count == 0) return [];

        var ids = boxIds.Distinct().ToList();
        var totals = new List<CashBoxExternalTotal>();

        // ---- To'lovlar: oddiy qator (+), storno qatori (−) ----
        var payments = db.Payments.AsNoTracking()
            .Where(p => p.CashBoxId != null && ids.Contains(p.CashBoxId.Value));
        totals.AddRange(await SumAsync(payments.Where(p => p.ReversalOf == null), 1m, ct));
        totals.AddRange(await SumAsync(payments.Where(p => p.ReversalOf != null), -1m, ct));

        // ---- Chiqimlar: (−), storno qilingani UMUMAN sanalmaydi ----
        // `expenses` da storno qatori yo'q (`ExpenseService.ReverseAsync`
        // izohi), u faqat jurnalda — shuning uchun "ayirib qo'yish" emas,
        // qatorni chiqarib tashlash.
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.CashBoxId != null && ids.Contains(e.CashBoxId.Value))
            .Where(e => !db.LedgerEntries.Any(
                l => l.RefType == LedgerRefType.Reversal && l.RefId == e.Id))
            .GroupBy(e => e.CashBoxId!.Value)
            .Select(g => new { BoxId = g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync(ct);
        // Kassasi bor chiqim — har doim naqd (fayl boshidagi "usul kesimi" izohi).
        totals.AddRange(expenses.Select(
            e => new CashBoxExternalTotal(e.BoxId, PaymentMethod.Cash, -e.Total)));

        // ---- Qaytarimlar: FAQAT tasdiqlangani; storno qatori pulni qaytaradi ----
        var refunds = db.StudentRefunds.AsNoTracking()
            .Where(r => r.CashBoxId != null && ids.Contains(r.CashBoxId.Value))
            .Where(r => r.ApprovedAt != null && r.RejectedReason == null);
        totals.AddRange(await SumAsync(refunds.Where(r => r.ReversalOf == null), -1m, ct));
        totals.AddRange(await SumAsync(refunds.Where(r => r.ReversalOf != null), 1m, ct));

        return totals;
    }

    private static async Task<List<CashBoxExternalTotal>> SumAsync(
        IQueryable<Payment> query, decimal sign, CancellationToken ct)
    {
        var sums = await query
            .GroupBy(p => new { BoxId = p.CashBoxId!.Value, p.Method })
            .Select(g => new { g.Key.BoxId, g.Key.Method, Total = g.Sum(p => p.Amount) })
            .ToListAsync(ct);

        return [.. sums.Select(s => new CashBoxExternalTotal(s.BoxId, s.Method, sign * s.Total))];
    }

    private static async Task<List<CashBoxExternalTotal>> SumAsync(
        IQueryable<StudentRefund> query, decimal sign, CancellationToken ct)
    {
        var sums = await query
            .GroupBy(r => new { BoxId = r.CashBoxId!.Value, r.Method })
            .Select(g => new { g.Key.BoxId, g.Key.Method, Total = g.Sum(r => r.Amount) })
            .ToListAsync(ct);

        return [.. sums.Select(s => new CashBoxExternalTotal(s.BoxId, s.Method, sign * s.Total))];
    }

    // =====================================================================
    //  Jurnal — qatorlarning o'zi
    // =====================================================================

    /// <summary>
    /// Jurnalga qo'shiladigan tashqi qatorlar: <paramref name="boxId"/> (yoki
    /// <c>null</c> — hamma kassa) va davr bo'yicha, har manbadan ko'pi bilan
    /// <paramref name="maxRows"/> ta.
    /// </summary>
    public async Task<List<CashBoxExternalRow>> RowsAsync(
        Guid? boxId, DateOnly? from, DateOnly? to, int maxRows, CancellationToken ct)
    {
        var rows = new List<CashBoxExternalRow>();
        rows.AddRange(await PaymentRowsAsync(boxId, from, to, maxRows, ct));
        rows.AddRange(await ExpenseRowsAsync(boxId, from, to, maxRows, ct));
        rows.AddRange(await RefundRowsAsync(boxId, from, to, maxRows, ct));
        return rows;
    }

    private async Task<List<CashBoxExternalRow>> PaymentRowsAsync(
        Guid? boxId, DateOnly? from, DateOnly? to, int maxRows, CancellationToken ct)
    {
        var q = db.Payments.AsNoTracking().Where(p => p.CashBoxId != null);
        if (boxId is { } id) q = q.Where(p => p.CashBoxId == id);
        if (CashBoxDateWindow.Lower(from) is { } lower) q = q.Where(p => p.ReceivedAt >= lower);
        if (CashBoxDateWindow.Upper(to) is { } upper) q = q.Where(p => p.ReceivedAt <= upper);

        var raw = await q
            .OrderByDescending(p => p.ReceivedAt)
            .Take(maxRows)
            .Select(p => new
            {
                p.Id,
                BoxId = p.CashBoxId!.Value,
                p.ReceiptNo,
                p.StudentId,
                p.Amount,
                p.Method,
                p.Note,
                p.ReceivedAt,
                p.ReversalOf,
            })
            .ToListAsync(ct);

        raw = [.. raw.Where(p => CashBoxDateWindow.Covers(p.ReceivedAt, from, to))];
        if (raw.Count == 0) return [];

        var studentIds = raw.Select(p => p.StudentId).Distinct().ToList();
        var names = await StudentNamesAsync(studentIds, ct);
        var contractNos = await CashBoxService.LatestContractNumbersAsync(db, studentIds, ct);

        // Qaysi to'lov BEKOR QILINGAN va nega: stornoning IZOHI aynan sabab
        // (`PaymentService.ReverseAsync` uni shu yerga yozadi) — bu
        // `CashBoxService.ToRowDtosAsync` dagi bilan bir xil qoida.
        var ids = raw.Select(p => p.Id).ToList();
        var stornos = await db.Payments.AsNoTracking()
            .Where(p => p.ReversalOf != null && ids.Contains(p.ReversalOf.Value))
            .Select(p => new { Original = p.ReversalOf!.Value, p.Note })
            .ToListAsync(ct);
        var cancelReasons = stornos
            .GroupBy(x => x.Original)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Note).FirstOrDefault(n => n is not null));

        return [.. raw.Select(p =>
        {
            var isReversal = p.ReversalOf is not null;
            var cancelled = cancelReasons.TryGetValue(p.Id, out var reason);

            return new CashBoxExternalRow(
                p.Id, p.BoxId, CashBoxLedgerKind.StudentPayment,
                p.Amount,
                isReversal ? -p.Amount : p.Amount,
                p.Method,
                p.ReceivedAt,
                isReversal ? CashBoxRowStatus.Reversal
                    : cancelled ? CashBoxRowStatus.Cancelled
                    : CashBoxRowStatus.Posted,
                names.GetValueOrDefault(p.StudentId, "—"),
                contractNos.GetValueOrDefault(p.StudentId),
                TypeName: null,
                // Stornoning o'z izohi AYNAN sabab — ikkala ustunda bir xil
                // matn turmasin (`CashBoxService.ToRowDtosAsync` naqshi).
                Note: isReversal ? null : p.Note,
                CancelReason: isReversal ? p.Note : reason,
                ReceiptNo: p.ReceiptNo);
        })];
    }

    private async Task<List<CashBoxExternalRow>> ExpenseRowsAsync(
        Guid? boxId, DateOnly? from, DateOnly? to, int maxRows, CancellationToken ct)
    {
        var q = db.Expenses.AsNoTracking().Where(e => e.CashBoxId != null);
        if (boxId is { } id) q = q.Where(e => e.CashBoxId == id);
        // Sana o'qi — `created_at` (pul javondan AYNAN o'shanda chiqdi), `on_date`
        // EMAS: `on_date` buxgalteriya sanasi va orqadagi kun bilan yozilgan
        // chiqimda undan farq qiladi. Jadvaldagi sana bilan kunlik filtr BIR
        // manbadan bo'lishi shart (`CashBoxTransactionRowDto.CreatedAt` izohi).
        if (CashBoxDateWindow.Lower(from) is { } lower) q = q.Where(e => e.CreatedAt >= lower);
        if (CashBoxDateWindow.Upper(to) is { } upper) q = q.Where(e => e.CreatedAt <= upper);

        var raw = await q
            .OrderByDescending(e => e.CreatedAt)
            .Take(maxRows)
            .Select(e => new
            {
                e.Id,
                BoxId = e.CashBoxId!.Value,
                e.Amount,
                e.Category,
                e.Note,
                e.CreatedAt,
                e.CreatedBy,
            })
            .ToListAsync(ct);

        raw = [.. raw.Where(e => CashBoxDateWindow.Covers(e.CreatedAt, from, to))];
        if (raw.Count == 0) return [];

        var ids = raw.Select(e => e.Id).ToList();

        // Storno — `expenses` da qator QO'SHMAYDI, u faqat jurnalda
        // (`ref_type = 'reversal'`, `ref_id` = chiqim id'si, `memo` = sabab).
        var reversals = await db.LedgerEntries.AsNoTracking()
            .Where(l => l.RefType == LedgerRefType.Reversal
                        && l.RefId != null && ids.Contains(l.RefId.Value))
            .Select(l => new { Id = l.RefId!.Value, l.Memo })
            .ToListAsync(ct);
        var cancelReasons = reversals
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Memo).FirstOrDefault(m => m is not null));

        var userIds = raw.Select(e => e.CreatedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return [.. raw.Select(e =>
        {
            var reversed = cancelReasons.TryGetValue(e.Id, out var reason);

            return new CashBoxExternalRow(
                e.Id, e.BoxId, CashBoxLedgerKind.Expense,
                e.Amount,
                // Storno qilingan chiqim javondagi pulga TA'SIR QILMAYDI,
                // lekin jadvalda O'Z summasi bilan turadi (aks holda
                // "500 000 so'mlik chiqim bekor qilindi" qatori 0 bo'lib
                // ko'rinardi — `TransactionJournalQuery` dagi bilan bir xil qoida).
                reversed ? 0m : -e.Amount,
                PaymentMethod.Cash,
                e.CreatedAt,
                reversed ? CashBoxRowStatus.Cancelled : CashBoxRowStatus.Posted,
                names.GetValueOrDefault(e.CreatedBy, "—"),
                ContractNo: null,
                TypeName: CategoryLabel(e.Category),
                Note: e.Note,
                CancelReason: reason,
                ReceiptNo: null);
        })];
    }

    private async Task<List<CashBoxExternalRow>> RefundRowsAsync(
        Guid? boxId, DateOnly? from, DateOnly? to, int maxRows, CancellationToken ct)
    {
        var q = db.StudentRefunds.AsNoTracking()
            .Where(r => r.CashBoxId != null)
            // So'ralgan yoki rad etilgan qaytarimda javondan pul CHIQMAGAN —
            // u kassa jurnalining qatori emas (fayl boshidagi ishora qoidasi).
            .Where(r => r.ApprovedAt != null && r.RejectedReason == null);
        if (boxId is { } id) q = q.Where(r => r.CashBoxId == id);
        if (CashBoxDateWindow.Lower(from) is { } lower) q = q.Where(r => r.ApprovedAt >= lower);
        if (CashBoxDateWindow.Upper(to) is { } upper) q = q.Where(r => r.ApprovedAt <= upper);

        var raw = await q
            .OrderByDescending(r => r.ApprovedAt)
            .Take(maxRows)
            .Select(r => new
            {
                r.Id,
                BoxId = r.CashBoxId!.Value,
                r.StudentId,
                r.Amount,
                r.Method,
                r.Reason,
                ApprovedAt = r.ApprovedAt!.Value,
                r.ReversalOf,
            })
            .ToListAsync(ct);

        raw = [.. raw.Where(r => CashBoxDateWindow.Covers(r.ApprovedAt, from, to))];
        if (raw.Count == 0) return [];

        var studentIds = raw.Select(r => r.StudentId).Distinct().ToList();
        var names = await StudentNamesAsync(studentIds, ct);
        var contractNos = await CashBoxService.LatestContractNumbersAsync(db, studentIds, ct);

        // Bekor qilingan qaytarim — uni bekor qilgan TASDIQLANGAN storno
        // qatoridan bilinadi (`StudentRefundService`: storno ham tasdiqdan
        // o'tadi, tasdiqlanmagani pulga tegmaydi).
        var ids = raw.Select(r => r.Id).ToList();
        var stornos = await db.StudentRefunds.AsNoTracking()
            .Where(r => r.ReversalOf != null && ids.Contains(r.ReversalOf.Value)
                        && r.ApprovedAt != null && r.RejectedReason == null)
            .Select(r => new { Original = r.ReversalOf!.Value, r.Reason })
            .ToListAsync(ct);
        var cancelReasons = stornos
            .GroupBy(x => x.Original)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Reason).FirstOrDefault());

        return [.. raw.Select(r =>
        {
            var isReversal = r.ReversalOf is not null;
            var cancelled = cancelReasons.TryGetValue(r.Id, out var reason);

            return new CashBoxExternalRow(
                r.Id, r.BoxId, CashBoxLedgerKind.Refund,
                r.Amount,
                isReversal ? r.Amount : -r.Amount,
                r.Method,
                r.ApprovedAt,
                isReversal ? CashBoxRowStatus.Reversal
                    : cancelled ? CashBoxRowStatus.Cancelled
                    : CashBoxRowStatus.Posted,
                names.GetValueOrDefault(r.StudentId, "—"),
                contractNos.GetValueOrDefault(r.StudentId),
                TypeName: null,
                // Qaytarimda "sabab" — hujjatning O'Z maydoni; storno
                // qatorida u bekor qilish sababi (to'lovdagi bilan bir xil qoida).
                Note: isReversal ? null : r.Reason,
                CancelReason: isReversal ? r.Reason : reason,
                ReceiptNo: null);
        })];
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<Dictionary<string, string>> StudentNamesAsync(
        List<string> studentIds, CancellationToken ct) =>
        studentIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.Students.AsNoTracking()
                .Where(s => studentIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

    /// <summary>
    /// Chiqim toifasining o'zbekcha nomi — jadvaldagi "Tranzaksiya turi"
    /// ustuniga. Noma'lum toifa (qo'lda kiritilgan eski qator) bu yerda
    /// YIQILMAYDI, kodning o'zi ko'rinadi.
    /// </summary>
    private static string CategoryLabel(string category) =>
        Accounts.IsExpenseCategory(category)
            ? MoneyFlowQueries.LabelFor(Accounts.ExpenseFor(category))
            : category;
}

/// <summary>
/// Kunlik filtrni lahza (<c>timestamptz</c>) ustuniga qo'llash — kassa
/// jurnalining HAR TO'RT manbasi uchun bitta qoida.
///
/// <para>
/// Ikki qadam, ATAYLAB: bazada chegara BIR KUN KENG olinadi (ustun UTC
/// lahzasi, filtr esa MAKTAB kuni — UTC+5), keyin xotirada
/// <see cref="AppClock.LocalDateOf"/> bo'yicha AYNAN kesiladi. Faqat
/// birinchisi qolsa chegaradagi kun noto'g'ri kirar/chiqar edi; faqat
/// ikkinchisi qolsa butun jadval o'qilardi.
/// </para>
/// </summary>
internal static class CashBoxDateWindow
{
    /// <summary>Bazaga beriladigan PASTKI chegara (bir kun keng). <c>null</c> = chegara yo'q.</summary>
    public static DateTimeOffset? Lower(DateOnly? from) =>
        from is { } f
            ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1)
            : null;

    /// <summary>Bazaga beriladigan YUQORI chegara (bir kun keng). <c>null</c> = chegara yo'q.</summary>
    public static DateTimeOffset? Upper(DateOnly? to) =>
        to is { } t
            ? new DateTimeOffset(t.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddDays(1)
            : null;

    /// <summary>Lahza maktab mintaqasida so'ralgan davrga tushadimi (AYNAN kesim).</summary>
    public static bool Covers(DateTimeOffset at, DateOnly? from, DateOnly? to)
    {
        var day = AppClock.LocalDateOf(at);
        if (from is { } f && day < f) return false;
        return !(to is { } t && day > t);
    }
}
