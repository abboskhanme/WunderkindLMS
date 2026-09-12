using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Chiqim kiritish — SPEC §3.7, §4.3 ("Record an expense"), §4.5. Vazifa: P1-14b.
// ===========================================================================
//
//  NEGA BU MODUL KERAK
//  -------------------
//  Faza 1 da `expenses` jadvali va `LedgerService` qurildi, lekin chiqim
//  YOZADIGAN kod yo'q edi. Natijada P&L faqat daromadni ko'rardi va "sof
//  foyda" butun aylanmaga teng bo'lib chiqardi. Eski `FinanceController`
//  chiqimni `finance_transactions` ga yozadi — u JURNALGA TUSHMAYDI, ya'ni
//  yangi hisobotlar uchun u pul umuman mavjud emas.
//
//  BITTA CHIQIM = BITTA `expenses` QATORI + IKKI JURNAL SATRI
//  ----------------------------------------------------------
//      debit  expense:<toifa>      — pul qayerga ketdi
//      credit cash | bank          — pul qayerdan chiqdi (§8.1 Q13 mantig'i:
//                                    naqd bo'lsa kassa, qolgani bank)
//  Ikkalasi BITTA tranzaksiyada: `LedgerService.PostAsync` o'z `SaveChanges`
//  ini chaqiradi, ya'ni tranzaksiyasiz bu ikki alohida commit bo'lardi va
//  orada jarayon o'lsa bazada JURNALSIZ CHIQIM qolardi.
//
//  IKKI QAVATLI NAZORAT (SPEC §4.5)
//  --------------------------------
//  `BillingSettings.ExpenseApprovalThreshold` (sukut 5 000 000 so'm) dan
//  KATTA chiqim darhol jurnalga tushmaydi: `expenses` qatori yoziladi,
//  jurnal esa IKKINCHI shaxs (`ApproveAsync`) tasdiqlaganda qo'yiladi.
//  "Yaratgan odam o'zi tasdiqlay olmaydi" qoidasi ilovada ham, bazada ham
//  bor (`ck_expenses_approver_differs`) — ilova chetlab o'tilsa ham ishlaydi.
//
//  STORNO — NEGA IKKINCHI `expenses` QATORI EMAS
//  ----------------------------------------------
//  Xato chiqim O'CHIRILMAYDI. Tuzatish `LedgerService.ReverseAsync` bilan
//  bo'ladi: original ikki satr tegilmaydi, ustiga ko'zgu satrlar qo'shiladi
//  (`ref_type = 'reversal'`, `ref_id` = O'SHA chiqim id'si, `memo` = sabab,
//  `created_by` = tasdiqlovchi). `expenses` ga yangi qator YOZILMAYDI —
//  uch sabab:
//    1) `Expense` entity'sida `reversal_of` ustuni yo'q (P1-04 da muzlatilgan),
//       ya'ni ikkinchi qatorni originalga faqat izoh MATNI orqali bog'lash
//       mumkin bo'lardi — bu bog'lanish emas, taxmin;
//    2) `amount > 0` check constraint manfiy qatorga yo'l bermaydi, ya'ni
//       storno qatori `expenses` ni summalagan har qanday hisobotda chiqimni
//       IKKI BARAVAR ko'rsatardi — jimgina va noto'g'ri tomonga;
//    3) `ledger_entries` bazada o'zgarmas (`app_rw` da UPDATE/DELETE yo'q),
//       `expenses` esa emas. Storno dalili kuchliroq qavatda turishi kerak.
//  Natija: chiqimning HOLATI `expenses` ustunida emas, jurnaldan hisoblanadi
//  (<see cref="ExpenseStatus"/>) — pul haqidagi yagona haqiqat manbai bitta
//  joyda qoladi (SPEC §3.7).
// ===========================================================================

/// <summary>
/// Chiqimning holati. Ustun sifatida SAQLANMAYDI — har safar jurnaldan
/// hisoblanadi, chunki jurnal o'zgarmas va u yagona haqiqat manbai.
/// </summary>
public static class ExpenseStatus
{
    /// <summary>Chegaradan yuqori; jurnalga hali tushmagan, tasdiq kutmoqda (SPEC §4.5).</summary>
    public const string Pending = "pending";

    /// <summary>Jurnalga tushgan — P&amp;L va pul aylanmasida ko'rinadi.</summary>
    public const string Posted = "posted";

    /// <summary>Storno qilingan: ko'zgu satrlar qo'yilgan, original joyida.</summary>
    public const string Reversed = "reversed";

    public static readonly IReadOnlyList<string> All = [Pending, Posted, Reversed];
}

/// <summary>
/// Chiqim — o'qish uchun. Pul bilan bog'liq hamma qiymat SERVERDA hisoblanadi
/// (JavaScript'da <c>number</c> — float64, tiyin xatosining manbai).
/// </summary>
/// <param name="Category">Toifa (<see cref="Accounts.ExpenseCategories"/>).</param>
/// <param name="Account">Jurnaldagi chiqim hisobi: <c>expense:&lt;toifa&gt;</c>.</param>
/// <param name="Status"><see cref="ExpenseStatus"/> — jurnaldan hisoblangan.</param>
/// <param name="SettlementAccount">Pul qayerdan chiqdi: <c>cash</c> yoki
/// <c>bank</c>. Tasdiq kutayotgan chiqimda <c>null</c> — u hali to'lanmagan.</param>
/// <param name="PostedOn">Jurnalga qaysi buxgalteriya sanasi bilan tushgani.</param>
public record ExpenseDto(
    Guid Id,
    DateOnly OnDate,
    string Category,
    string Account,
    decimal Amount,
    string? Note,
    string Status,
    string? SettlementAccount,
    DateOnly? PostedOn,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string? ApprovedBy,
    string? ApprovedByName,
    DateOnly? ReversedOn,
    string? ReversedBy,
    string? ReversedByName,
    string? ReversalReason,
    string? TeacherId = null,
    string? TeacherName = null);

/// <summary>
/// Yangi chiqim. <c>created_by</c> bu yerda YO'Q va bo'lmaydi — uni server
/// JWT'dan oladi (SPEC §4.4).
/// </summary>
/// <param name="OnDate">Buxgalteriya sanasi. Kelajak sana qabul qilinmaydi.</param>
/// <param name="Category">Toifa — <see cref="Accounts.ExpenseCategories"/> dan.</param>
/// <param name="Method">
/// To'lov usuli (<see cref="PaymentMethod"/>): naqd bo'lsa pul kassadan
/// (<c>cash</c>), qolganida bankdan (<c>bank</c>) chiqadi.
///
/// <para>
/// <b>Chegaradan YUQORI summada bu qiymat faqat tekshiriladi va SAQLANMAYDI</b>
/// — to'lov usulini tasdiqlovchi <c>ApproveAsync</c> da ko'rsatadi. Sabab:
/// <c>expenses</c> jadvalida usul ustuni yo'q va uni qo'shish P1-04 da
/// muzlatilgan entity'ni o'zgartirardi; usul esa jurnalning kredit satrida
/// saqlanadi, jurnal satri bo'lsa tasdiqdan keyin paydo bo'ladi. Ya'ni
/// "pulni qaysi hisobdan chiqarish" haqidagi qaror AYNAN pulni chiqarishga
/// ruxsat bergan odamda qoladi.
/// </para>
/// </param>
/// <param name="TeacherId">
/// Maosh kimga berilyapti (<c>teachers.id</c>). FAQAT <c>salary</c> toifasida
/// to'ldiriladi — boshqa toifada berilsa <b>400</b> (P1-21). Usiz maosh
/// hisoboti "falonchi qancha oldi" degan savolga javob bera olmaydi;
/// batafsil: <see cref="SalaryPaymentQuery"/>.
/// </param>
public record CreateExpenseRequest(
    DateOnly OnDate, string Category, decimal Amount, string Method, string? Note,
    string? TeacherId = null);

/// <summary>
/// Chegaradan yuqori chiqimni tasdiqlash (SPEC §4.5). <c>approved_by</c> tanada
/// YO'Q — u JWT'dan olinadi.
/// </summary>
/// <param name="Method">Pul qaysi usulda chiqdi — jurnalning kredit satri shundan.</param>
public record ApproveExpenseRequest(string Method);

/// <summary>Storno so'rovi. Sabab majburiy: u jurnal satrining <c>memo</c> siga tushadi.</summary>
public record ReverseExpenseRequest(string Reason);

/// <summary>Chiqimlar ro'yxati uchun filtr (davr + toifa + holat).</summary>
/// <param name="From">Shu sanadan boshlab (<c>on_date</c> bo'yicha).</param>
/// <param name="To">Shu sanagacha, shu kun ham kiradi.</param>
/// <param name="Category">Bitta toifa; <c>null</c> — hammasi.</param>
/// <param name="Status"><see cref="ExpenseStatus"/> qiymatlaridan biri; <c>null</c> — hammasi.</param>
public record ExpenseQuery(
    DateOnly? From = null, DateOnly? To = null, string? Category = null, string? Status = null,
    string? TeacherId = null);

/// <summary>
/// Chiqim kiritish, ro'yxat, tasdiqlash va storno. Batafsil: fayl boshidagi izoh.
///
/// <para>
/// Bu interfeysda <c>Update</c> ham, <c>Delete</c> ham YO'Q — va bo'lmaydi.
/// Xato chiqim <see cref="ReverseAsync"/> bilan tuzatiladi (SPEC §4.1).
/// </para>
/// <para>
/// Har bir yozish metodi shaxsni ALOHIDA parametr sifatida oladi
/// (<c>actorId</c>, <c>approverId</c>) — so'rov DTO'sida emas, SPEC §4.4.
/// </para>
/// </summary>
public interface IExpenseService
{
    /// <summary>
    /// Chiqimni qayd etadi. Summa chegaradan katta bo'lmasa — <c>expenses</c>
    /// qatori va ikki jurnal satri BITTA tranzaksiyada. Katta bo'lsa — faqat
    /// <c>expenses</c> qatori, holati <see cref="ExpenseStatus.Pending"/>
    /// (SPEC §4.5).
    /// </summary>
    Task<ExpenseDto> CreateAsync(
        CreateExpenseRequest request, string actorId, CancellationToken ct = default);

    /// <summary>Davr, toifa va holat bo'yicha ro'yxat (yangisidan eskisiga).</summary>
    Task<IReadOnlyList<ExpenseDto>> ListAsync(ExpenseQuery query, CancellationToken ct = default);

    /// <summary>Bitta chiqim; topilmasa <c>null</c>.</summary>
    Task<ExpenseDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Tasdiqlaydi va AYNAN shu lahzada jurnalga qo'yadi. Tasdiqlovchi
    /// yaratuvchidan boshqa shaxs bo'lishi SHART (SPEC §4.5).
    /// </summary>
    Task<ExpenseDto> ApproveAsync(
        Guid id, string method, string approverId, CancellationToken ct = default);

    /// <summary>
    /// Storno: jurnalga ko'zgu satrlar qo'yiladi, original TEGILMAYDI va
    /// <c>expenses</c> ga yangi qator yozilmaydi (sabab fayl boshida).
    /// Jurnal satrini qo'ygan odam uni o'zi storno qila olmaydi.
    /// </summary>
    Task<ExpenseDto> ReverseAsync(
        Guid id, string reason, string approverId, CancellationToken ct = default);
}

/// <inheritdoc cref="IExpenseService"/>
public sealed class ExpenseService(IAppDbContext db, ILedgerService ledger) : IExpenseService
{
    /// <summary>Baza ustuni <c>numeric(14,2)</c> — arifmetika ham shu aniqlikda.</summary>
    private const int MoneyScale = 2;

    /// <summary>
    /// Ro'yxatning yuqori chegarasi. Chiqim kuniga bir necha dona, ya'ni 2000
    /// qator ~ ikki yillik tarix; filtrsiz so'rov ham serverni yiqitmaydi.
    /// Jamlanma kerak bo'lsa — P&amp;L hisoboti (<c>/api/admin/finance/pnl</c>),
    /// u yig'indini bazada hisoblaydi.
    /// </summary>
    private const int MaxRows = 2000;

    /// <summary>
    /// Chiqim sanasining pastki chegarasi — "maydon to'ldirilmagan" holatini
    /// tutish uchun (<c>default(DateOnly)</c> = 0001-01-01). Maktab bu sanadan
    /// oldingi chiqimni yangi tizimga kiritmaydi: SPEC §8.1 Q9 — noldan
    /// boshlanadi, ochilish qoldiqlari ko'chirilmaydi.
    /// </summary>
    private static readonly DateOnly EarliestDate = new(2000, 1, 1);

    /// <summary>
    /// Audit qatoridagi <c>actor_name</c> uchun — keshlangan (P1-14).
    /// Aktyor JWT'dan emas, PARAMETR sifatida keladi (SPEC §4.4), shuning uchun
    /// <c>AuditService.Record</c> emas, <c>AuditService.Entry</c> ishlatiladi.
    /// </summary>
    private readonly ActorNames actors = new(db);

    // -----------------------------------------------------------------
    //  Yaratish
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ExpenseDto> CreateAsync(
        CreateExpenseRequest request, string actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId, nameof(actorId));

        // ---- 1. So'rovning o'zi (bazaga tegmasdan) ----
        var amount = Money(request.Amount);
        if (amount <= 0m)
            throw BillingRuleException.Invalid("invalid_amount", "Chiqim summasi musbat bo'lishi shart.");

        var category = RequireCategory(request.Category);
        var method = RequireMethod(request.Method);
        var teacherId = Trim(request.TeacherId);

        // Kelajak sanasi bilan chiqim — hali bo'lmagan pul harakati. U hali
        // yopilmagan davrga tushib, keyingi oyning P&L'ini jimgina o'zgartirardi.
        if (request.OnDate > AppClock.Today)
            throw BillingRuleException.Invalid("future_date",
                $"Chiqim sanasi kelajakda bo'lmaydi: {request.OnDate:yyyy-MM-dd}. Bugun — {AppClock.Today:yyyy-MM-dd}.");

        // `DateOnly` — qiymat turi: so'rovda `onDate` UMUMAN bo'lmasa u
        // 0001-01-01 bo'lib bog'lanadi va yuqoridagi tekshiruvdan bemalol
        // o'tib ketardi. Natijasi — jurnalda hech qanday hisobotga tushmaydigan
        // sanadagi pul harakati. Pastki chegara shuni to'xtatadi.
        if (request.OnDate < EarliestDate)
            throw BillingRuleException.Invalid("invalid_date",
                $"Chiqim sanasi ko'rsatilmagan yoki noto'g'ri: {request.OnDate:yyyy-MM-dd}.");

        // ---- 2. Ikki qavatli nazorat chegarasi (SPEC §4.5) ----
        var threshold = await ThresholdAsync(ct);
        var needsApproval = amount > threshold;

        // Maosh chiqimi kimgaligi bilan yoziladi (P1-21). Boshqa toifada
        // o'qituvchi ko'rsatilsa — bu so'rovdagi xato, uni JIM qabul qilish
        // maosh hisobotiga hech qachon ko'rinmaydigan pul qo'shardi. Bazada
        // ham shu qoida bor (`ck_expenses_teacher_only_salary`).
        if (teacherId is not null)
        {
            if (category != SalaryPaymentQuery.SalaryCategory)
                throw BillingRuleException.Invalid("teacher_not_allowed",
                    $"O'qituvchi faqat '{SalaryPaymentQuery.SalaryCategory}' toifasida ko'rsatiladi.");

            if (!await db.Teachers.AsNoTracking().AnyAsync(t => t.Id == teacherId, ct))
                throw BillingRuleException.NotFound("teacher_not_found", "O'qituvchi topilmadi.");
        }

        var expense = new Expense
        {
            OnDate = request.OnDate,
            Category = category,
            Amount = amount,
            Note = Trim(request.Note),
            TeacherId = teacherId,
            CreatedBy = actorId,
            ApprovedBy = null,
            CreatedAt = AppClock.NowInstant,
        };

        if (needsApproval)
        {
            // Jurnalga TUSHMAYDI: chegaradan yuqori pul ikkinchi imzosiz
            // hisobotga kirmasligi kerak. Bitta INSERT + audit qatori — ikkalasi
            // bitta `SaveChanges` da, ya'ni aniq tranzaksiya shart emas.
            db.Expenses.Add(expense);
            db.AuditLogs.Add(AuditService.Entry(
                AuditService.EntityExpense, expense.Id.ToString("D"), "create",
                $"Chiqim kiritildi, TASDIQ KUTMOQDA: {category} — {AuditService.Money(amount)} so'm "
                + $"({expense.OnDate:yyyy-MM-dd}). Chegara: {AuditService.Money(threshold)} so'm (SPEC §4.5).",
                actorId: actorId,
                actorName: await actors.OfAsync(actorId, ct),
                after: Snapshot(expense)));
            await db.SaveChangesAsync(ct);
            return (await ToDtosAsync([expense], ct))[0];
        }

        await PostAsync(expense, method, actorId, isNew: true, before: null, ct);
        return (await ToDtosAsync([expense], ct))[0];
    }

    // -----------------------------------------------------------------
    //  Tasdiqlash (SPEC §4.5)
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ExpenseDto> ApproveAsync(
        Guid id, string method, string approverId, CancellationToken ct = default)
    {
        RequireActor(approverId, nameof(approverId));
        var settlement = RequireMethod(method);

        // DIQQAT — QATOR QULFI YO'Q. Ikki tasdiqlovchi AYNI LAHZADA bir xil
        // chiqimni tasdiqlasa, ikkalasi ham tekshiruvlardan o'tib, jurnalga
        // ikkita partiya tushishi mumkin (chiqim ikki baravar ko'rinardi).
        // `SELECT ... FOR UPDATE` uchun `app_rw` da kerakli huquq yo'q va
        // Application qatlamida xom SQL yozilmaydi (P1-11 da ham shu tanlov).
        // Amalda tasdiqlovchi bitta — direktor, va u bir tugmani ikki marta
        // bosmaydi; tushib qolsa storno bilan tuzatiladi.
        //
        // Kuzatiladigan holda: `approved_by` shu obyektda yangilanadi.
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw BillingRuleException.NotFound("expense_not_found", "Chiqim topilmadi.");

        if (expense.ApprovedBy is not null)
            throw BillingRuleException.Conflict("already_approved",
                "Bu chiqim allaqachon tasdiqlangan.");

        if (await IsPostedAsync(id, ct))
            throw BillingRuleException.Conflict("already_posted",
                "Bu chiqim allaqachon jurnalga tushgan — tasdiqlash talab qilinmagan. "
                + "Xato bo'lsa storno qiling.");

        // SPEC §4.5 — o'z-o'zini tasdiqlash. Bazada ham shunday
        // (`ck_expenses_approver_differs`); bu yerda tekshirish SABABNI aniq
        // aytish uchun, u yerda umumiy constraint xatosi chiqardi.
        if (string.Equals(expense.CreatedBy, approverId, StringComparison.Ordinal))
            throw BillingRuleException.Forbidden("self_approval",
                "O'zingiz kiritgan chiqimni o'zingiz tasdiqlay olmaysiz (SPEC §4.5) — "
                + "ikkinchi shaxs tasdig'i kerak.");

        // Audit uchun "oldingi holat" — `ApprovedBy` hali null bo'lgan payt.
        var before = Snapshot(expense);
        expense.ApprovedBy = approverId;

        // Jurnal satrining muallifi — TASDIQLOVCHI: pulni haqiqatan chiqarishga
        // ruxsat bergan odam o'sha. Buning ikkinchi ta'siri ham foydali:
        // `LedgerService.ReverseAsync` partiya muallifiga storno'ni taqiqlaydi,
        // ya'ni bu chiqimni keyin UCHINCHI shaxs (yoki yaratuvchi) storno qiladi.
        await PostAsync(expense, settlement, approverId, isNew: false, before, ct);

        return (await ToDtosAsync([expense], ct))[0];
    }

    // -----------------------------------------------------------------
    //  Storno — xato chiqimni tuzatishning YAGONA yo'li
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ExpenseDto> ReverseAsync(
        Guid id, string reason, string approverId, CancellationToken ct = default)
    {
        RequireActor(approverId, nameof(approverId));

        var cleanReason = Trim(reason)
            ?? throw BillingRuleException.Invalid("reason_required",
                "Storno sababi majburiy (SPEC §4.3) — u jurnal yozuvida qoladi.");

        var expense = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw BillingRuleException.NotFound("expense_not_found", "Chiqim topilmadi.");

        // Partiyaning "langar" satri: LedgerService shundan butun partiyani
        // topadi va BUTUNLIGICHA teskari qiladi. Muallif ham shu yerda olinadi —
        // ikki qavatli nazorat tekshiruvi uchun ikkinchi so'rov kerak emas.
        var anchor = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Expense && e.RefId == id && e.ReversalOf == null)
            .OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.CreatedBy })
            .FirstOrDefaultAsync(ct)
            ?? throw BillingRuleException.Conflict("not_posted",
                "Bu chiqim hali jurnalga tushmagan (tasdiq kutmoqda) — storno qiladigan "
                + "pul harakati yo'q.");

        if (await IsReversedAsync(id, ct))
            throw BillingRuleException.Conflict("already_reversed",
                "Bu chiqim allaqachon storno qilingan.");

        // SPEC §4.5 — "yozdim, keyin o'zim bekor qildim" bitta odamning qo'lida
        // qolmasligi kerak. LedgerService ham shu qoidani MUSTAQIL tekshiradi;
        // bu yerda tekshirish sababni aniq aytish uchun.
        if (string.Equals(anchor.CreatedBy, approverId, StringComparison.Ordinal))
            throw BillingRuleException.Forbidden("self_reversal",
                "Jurnalga o'zingiz qo'ygan chiqimni o'zingiz storno qila olmaysiz (SPEC §4.5) — "
                + "ikkinchi shaxs tasdig'i kerak.");

        // ANIQ TRANZAKSIYA (P1-14 da qo'shildi). Ilgari bu yerda faqat
        // `ledger.ReverseAsync` ning bitta `SaveChanges` i bo'lgani uchun
        // tranzaksiya kerak emas edi. Endi ustiga audit qatori ham yoziladi
        // (SPEC §4.6), ya'ni ikkita yozuv bor — ular BIRGA tushishi yoki
        // birga tushmasligi kerak. Audit qatori ATAYLAB muvaffaqiyatli
        // storno'dan KEYIN qo'shiladi: aks holda `ReverseAsync` rad etganda
        // kuzatuvda saqlanmagan qator osilib qolardi.
        await using var tx = await db.BeginTransactionAsync(ct);

        try
        {
            await ledger.ReverseAsync(anchor.Id, cleanReason, approverId, ct);
        }
        // DIQQAT: `BillingRuleException` ning O'ZI `InvalidOperationException`
        // dan meros oladi, shuning uchun u avval tutilib, o'zgarishsiz
        // o'tkaziladi — aks holda aniq kod va statusi bor xato umumiy
        // "ledger_reversal_refused/409" ga aylanib qolardi.
        catch (BillingRuleException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // Jurnal darajasidagi qoidalar yuqorida tekshirilgan; bu yerga
            // tushish ma'lumot nomutanosibligini bildiradi. 500 o'rniga
            // sababni ko'rsatgan 409 foydaliroq.
            throw BillingRuleException.Conflict("ledger_reversal_refused", ex.Message);
        }

        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityExpense, expense.Id.ToString("D"), "reverse",
            $"Chiqim STORNO qilindi: {expense.Category} — "
            + $"{AuditService.Money(expense.Amount)} so'm. Sabab: {cleanReason}",
            actorId: approverId,
            actorName: await actors.OfAsync(approverId, ct),
            // `expenses` qatori TEGILMAYDI (sabab fayl boshida), shuning uchun
            // `before` va `after` bir xil — storno dalili jurnalda.
            before: Snapshot(expense),
            after: Snapshot(expense)));
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return (await ToDtosAsync([expense], ct))[0];
    }

    // -----------------------------------------------------------------
    //  O'qish
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ExpenseDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var expense = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return expense is null ? null : (await ToDtosAsync([expense], ct))[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpenseDto>> ListAsync(
        ExpenseQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = db.Expenses.AsNoTracking();

        if (query.From is { } from) q = q.Where(e => e.OnDate >= from);
        if (query.To is { } to) q = q.Where(e => e.OnDate <= to);
        if (!string.IsNullOrWhiteSpace(query.TeacherId))
        {
            var teacherId = query.TeacherId.Trim();
            q = q.Where(e => e.TeacherId == teacherId);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = RequireCategory(query.Category);
            q = q.Where(e => e.Category == category);
        }

        // Holat ustun emas — jurnaldan kelib chiqadi, shuning uchun filtri ham
        // jurnal bo'yicha EXISTS (`ix_ledger_entries_ref_type_ref_id`). Uni
        // xotirada qo'llash NOTO'G'RI bo'lardi: filtr `Take(MaxRows)` dan KEYIN
        // ishlab, "tasdiq kutayotganlar" ro'yxatidan eski qatorlarni jimgina
        // tashlab ketardi.
        if (!string.IsNullOrWhiteSpace(query.Status))
            q = RequireStatus(query.Status) switch
            {
                ExpenseStatus.Pending => q.Where(e =>
                    !db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Expense && l.RefId == e.Id)),
                ExpenseStatus.Posted => q.Where(e =>
                    db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Expense && l.RefId == e.Id)
                    && !db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Reversal && l.RefId == e.Id)),
                _ => q.Where(e =>
                    db.LedgerEntries.Any(l => l.RefType == LedgerRefType.Reversal && l.RefId == e.Id)),
            };

        var rows = await q
            .OrderByDescending(e => e.OnDate)
            .ThenByDescending(e => e.CreatedAt)
            .Take(MaxRows)
            .ToListAsync(ct);

        return await ToDtosAsync(rows, ct);
    }

    // -----------------------------------------------------------------
    //  Ichki yordamchilar
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>expenses</c> qatorini (kerak bo'lsa) va jurnal juftini BITTA
    /// tranzaksiyada yozadi.
    ///
    /// <para>
    /// Tranzaksiya MAJBURIY: <see cref="ILedgerService.PostAsync"/> o'z
    /// <c>SaveChanges</c> ini chaqiradi, ya'ni usiz bu ikki alohida commit
    /// bo'lardi va orada jarayon o'lsa bazada JURNALSIZ CHIQIM (yoki
    /// tasdiqlangan, lekin jurnalga tushmagan chiqim) qolardi.
    /// </para>
    /// </summary>
    /// <param name="before">Audit uchun oldingi holat. <c>null</c> = yangi qator
    /// (<paramref name="isNew"/>), ya'ni "oldingi holat" degan narsa yo'q.</param>
    private async Task PostAsync(
        Expense expense, string method, string actorId, bool isNew, object? before,
        CancellationToken ct)
    {
        var memo = expense.Note is null
            ? $"Chiqim: {expense.Category}"
            : $"Chiqim: {expense.Category} — {expense.Note}";

        await using var tx = await db.BeginTransactionAsync(ct);

        if (isNew) db.Expenses.Add(expense);

        // Audit (SPEC §4.6) — AYNAN shu tranzaksiya ichida. Orqaga qaytgan
        // chiqim olinmagan pul haqida audit izi qoldirmasin.
        db.AuditLogs.Add(AuditService.Entry(
            AuditService.EntityExpense, expense.Id.ToString("D"), isNew ? "create" : "approve",
            (isNew ? "Chiqim kiritildi va jurnalga tushdi: " : "Chiqim TASDIQLANDI va jurnalga tushdi: ")
            + $"{expense.Category} — {AuditService.Money(expense.Amount)} so'm "
            + $"({Accounts.SettlementFor(method)}, {expense.OnDate:yyyy-MM-dd})",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            before: before,
            after: Snapshot(expense)));

        await db.SaveChangesAsync(ct);

        await ledger.PostAsync(
        [
            // Debet: pul QAYERGA ketdi (toifa bo'yicha chiqim hisobi).
            new LedgerPosting(
                Accounts.ExpenseFor(expense.Category), LedgerDirection.Debit, expense.Amount,
                LedgerRefType.Expense, expense.Id, expense.OnDate, memo),
            // Kredit: pul QAYERDAN chiqdi. Naqd — kassadan, qolgani — bankdan
            // (SPEC §8.1 Q13 dagi to'lov mantig'ining ko'zgusi).
            new LedgerPosting(
                Accounts.SettlementFor(method), LedgerDirection.Credit, expense.Amount,
                LedgerRefType.Expense, expense.Id, expense.OnDate, memo),
        ], actorId, ct);

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Chiqimlarni DTO'ga o'giradi. Qator soni qanday bo'lsin — HAR DOIM ikkita
    /// so'rov (jurnal satrlari + foydalanuvchi ismlari). Moliya entity'larida
    /// navigatsiya xossalari yo'q (BillingModel.cs FK'larni navigatsiyasiz
    /// e'lon qiladi), shuning uchun "Include" o'rniga partiyalab o'qish —
    /// N+1 ning oldini olish yo'li shu.
    /// </summary>
    private async Task<List<ExpenseDto>> ToDtosAsync(
        IReadOnlyList<Expense> expenses, CancellationToken ct)
    {
        if (expenses.Count == 0) return [];

        var ids = expenses.Select(e => e.Id).ToList();

        // Chiqimning jurnaldagi izi: asl partiya (`expense`) va uning ko'zgusi
        // (`reversal`, `ref_id` o'zgarmaydi — P1-07 partiya qoidasi).
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

        var userIds = expenses.Select(e => e.CreatedBy)
            .Concat(expenses.Select(e => e.ApprovedBy).Where(x => x is not null).Select(x => x!))
            .Concat(entries.Select(l => l.CreatedBy))
            .Distinct()
            .ToList();

        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // Maosh qatorlaridagi o'qituvchi ismlari — bitta so'rov, sikl ichida emas.
        var teacherIds = expenses
            .Select(e => e.TeacherId).Where(x => x is not null).Select(x => x!)
            .Distinct().ToList();
        var teacherNames = teacherIds.Count == 0
            ? []
            : await db.Teachers.AsNoTracking()
                .Where(t => teacherIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FullName })
                .ToDictionaryAsync(t => t.Id, t => t.FullName, ct);

        return [.. expenses.Select(e =>
        {
            var mine = entries.Where(l => l.RefId == e.Id).ToList();

            // Kredit satri pul QAYERDAN chiqqanini aytadi (cash | bank),
            // debet satri esa QAYERGA (expense:<toifa>).
            var settlement = mine.FirstOrDefault(
                l => l.RefType == LedgerRefType.Expense && l.Direction == LedgerDirection.Credit);
            var posting = mine.FirstOrDefault(
                l => l.RefType == LedgerRefType.Expense && l.Direction == LedgerDirection.Debit);
            var reversal = mine.FirstOrDefault(l => l.RefType == LedgerRefType.Reversal);

            var status = reversal is not null ? ExpenseStatus.Reversed
                : settlement is not null ? ExpenseStatus.Posted
                : ExpenseStatus.Pending;

            // Jurnalga tushgan chiqim uchun hisobni JURNALDAN olamiz, toifadan
            // qayta hisoblamaymiz: agar toifalar jadvali kelajakda o'zgarsa ham,
            // hisobot qatori pul haqiqatan qaysi hisobga yozilganini ko'rsatadi.
            // Tushmaganida — toifadan; noma'lum toifa (qo'lda kiritilgan eski
            // qator) bu yerda YIQILMAYDI, "boshqa" bo'lib ko'rinadi.
            var account = posting?.Account
                ?? (Accounts.IsExpenseCategory(e.Category)
                    ? Accounts.ExpenseFor(e.Category)
                    : Accounts.ExpenseOther);

            return new ExpenseDto(
                e.Id, e.OnDate, e.Category, account, e.Amount, e.Note,
                status,
                settlement?.Account,
                settlement?.EntryDate,
                e.CreatedBy, Name(e.CreatedBy), e.CreatedAt,
                e.ApprovedBy, e.ApprovedBy is null ? null : Name(e.ApprovedBy),
                reversal?.EntryDate,
                reversal?.CreatedBy,
                reversal is null ? null : Name(reversal.CreatedBy),
                reversal?.Memo,
                e.TeacherId,
                e.TeacherId is null ? null : teacherNames.GetValueOrDefault(e.TeacherId, "—"));
        })];

        string Name(string userId) => names.GetValueOrDefault(userId, "—");
    }

    /// <summary>Audit uchun snapshot (<c>before</c>/<c>after</c>) — SPEC §4.6.</summary>
    private static object Snapshot(Expense e) => new
    {
        e.Id,
        e.OnDate,
        e.Category,
        e.Amount,
        e.Note,
        e.CreatedBy,
        e.ApprovedBy,
    };

    /// <summary>Shu chiqimning jurnal partiyasi bormi (ya'ni pul hisobotga tushganmi)?</summary>
    private Task<bool> IsPostedAsync(Guid id, CancellationToken ct) =>
        db.LedgerEntries.AsNoTracking()
            .AnyAsync(l => l.RefType == LedgerRefType.Expense && l.RefId == id, ct);

    private Task<bool> IsReversedAsync(Guid id, CancellationToken ct) =>
        db.LedgerEntries.AsNoTracking()
            .AnyAsync(l => l.RefType == LedgerRefType.Reversal && l.RefId == id, ct);

    /// <summary>
    /// Ikki qavatli nazorat chegarasi. Sozlama qatori yo'q bo'lsa —
    /// <see cref="BillingSettings"/> ning sukut qiymati (5 000 000), ya'ni
    /// himoya "sozlama topilmadi" holatida ham YOQILGAN qoladi.
    /// </summary>
    private async Task<decimal> ThresholdAsync(CancellationToken ct) =>
        (await db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(ct)
         ?? new BillingSettings()).ExpenseApprovalThreshold;

    private static string RequireCategory(string? category)
    {
        var clean = category?.Trim().ToLowerInvariant();
        return Accounts.IsExpenseCategory(clean)
            ? clean!
            : throw BillingRuleException.Invalid("invalid_category",
                $"Noma'lum chiqim toifasi: '{category}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", Accounts.ExpenseCategories)}.");
    }

    private static string RequireMethod(string? method) =>
        method is not null && PaymentMethod.All.Contains(method, StringComparer.Ordinal)
            ? method
            : throw BillingRuleException.Invalid("invalid_method",
                $"Noma'lum to'lov usuli: '{method}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", PaymentMethod.All)}.");

    private static string RequireStatus(string? status) =>
        status is not null && ExpenseStatus.All.Contains(status, StringComparer.Ordinal)
            ? status
            : throw BillingRuleException.Invalid("invalid_status",
                $"Noma'lum holat: '{status}'. "
                + $"Ruxsat etilganlar: {string.Join(", ", ExpenseStatus.All)}.");

    /// <summary>
    /// Pul qiymati baza aniqligiga (2 kasr) MOS bo'lishi shart. Yaxlitlab
    /// yubormaymiz: 1000.005 kabi qiymat "qabul qilindi" deb ko'rinib, bazada
    /// boshqa raqam bo'lib qolardi — va farqni keyin hech kim topa olmasdi.
    /// </summary>
    private static decimal Money(decimal value) =>
        decimal.Round(value, MoneyScale) == value
            ? value
            : throw BillingRuleException.Invalid("invalid_amount",
                $"Chiqim summasi tiyin aniqligida (2 kasr) bo'lishi shart: {value}.");

    private static void RequireActor(string? actorId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException(
                "Shaxs noma'lum. U JWT claim'idan olinadi — SPEC §4.4.", parameterName);
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
