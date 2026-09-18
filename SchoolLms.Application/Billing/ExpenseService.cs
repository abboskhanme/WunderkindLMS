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
//
//  NAQD CHIQIM ENDI KASSAGA TEGISHLI, SMENAGA EMAS (kassalar modeli, 2026-09)
//  ----------------------------------------------------------------------
//  Ilgari (F1.03) naqd chiqim yozuvchining OCHIQ SMENASIGA biriktirilardi,
//  smenasiz esa 409 `no_open_shift` bilan rad etilardi. Mijoz javobi
//  ("smena degan tushuncha umuman bo'lmasin") bilan bu qoida OLIB
//  TASHLANDI: `ExpenseService` endi `ICashShiftService` ga UMUMAN
//  bog'lanmaydi. Qoida endi:
//    * darhol jurnalga tushadigan chiqim  → SO'ROVDA ko'rsatilgan (yoki
//                                           SUKUT) kassa;
//    * tasdiq kutgan chiqim               → TASDIQLOVCHI ko'rsatgan (yoki
//                                           SUKUT) kassa (usulni ham
//                                           o'sha tanlaydi);
//    * storno                             → HECH QANDAY kassa/smena talab
//                                           qilinmaydi — `expenses` ga bu
//                                           qadamda hech qanday ustun
//                                           yozilmaydi (pastdagi izoh),
//                                           ya'ni biriktiriladigan maydon
//                                           ham yo'q.
//  Kassa mavjud va FAOL ekanligi tekshiriladi (`cash_box_not_found` /
//  `cash_box_inactive`), lekin bu — SMENA emas. Naqd bo'lmagan chiqim
//  (karta, o'tkazma, onlayn) bank hisobidan chiqadi va kassaga umuman
//  tegmaydi.
//
//  Kassani biriktirish — TEKSHIR-VA-YOZ, shuning uchun u
//  `CashBoxService.BoxLockKey` qulfi ostida bajariladi (xuddi ilgari
//  `CashShiftService.ShiftLockKey` ishlatilgani kabi, endi kassa bo'yicha).
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
/// <param name="CashShiftId">
/// Naqd chiqim qaysi kassa smenasidan to'landi (F1.03). <c>null</c> = pul
/// bankdan chiqqan yoki chiqim hali jurnalga tushmagan.
/// </param>
/// <param name="AttachmentCount">Biriktirilgan hujjatlar soni (F1.08).</param>
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
    string? TeacherName = null,
    Guid? CashShiftId = null,
    int AttachmentCount = 0,
    // Qaysi KASSADAN to'landi (kassalar modeli, 2026-09). `null` = bankdan
    // yoki eski (kassa modelidan oldingi) qator.
    Guid? CashBoxId = null,
    string? CashBoxName = null);

/// <summary>
/// Chiqimga biriktirilgan hujjat (F1.08) — o'qish uchun.
///
/// <para>
/// Faylning O'ZI <c>POST /api/admin/uploads</c> orqali yuklanadi
/// (<c>UploadsController</c> + <c>UploadGuard</c>), bu yerga esa FAQAT
/// natijaviy yo'l keladi. Ikkinchi yuklash yo'li ATAYLAB qurilmadi: fayl
/// turi va hajmi tekshiruvining ikkita nusxasi bir kun albatta bir-biridan
/// uzoqlashardi, va ulardan biri yumshoqroq bo'lardi.
/// </para>
/// </summary>
public record ExpenseAttachmentDto(
    Guid Id,
    Guid ExpenseId,
    string FileUrl,
    string FileName,
    string ContentType,
    long SizeBytes,
    string UploadedBy,
    string UploadedByName,
    DateTimeOffset UploadedAt);

/// <summary>
/// Hujjatni chiqimga biriktirish (F1.08). <c>uploaded_by</c> tanada YO'Q —
/// u JWT'dan olinadi (SPEC §4.4).
/// </summary>
/// <param name="FileUrl">
/// <c>POST /api/admin/uploads</c> qaytargan yo'l. <c>/uploads/</c> bilan
/// boshlanishi SHART: tashqi URL "dalil" emas — uni istalgan payt
/// almashtirib qo'yish mumkin, biz esa uni nazorat qilmaymiz.
/// </param>
public record AttachExpenseFileRequest(
    string FileUrl, string FileName, string ContentType, long SizeBytes);

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
/// <param name="CashBoxId">
/// Naqd chiqim QAYSI kassadan to'lanadi (kassalar modeli, 2026-09 — "smena"
/// o'rnini bosadi). <c>null</c> = SUKUT kassa. Naqd bo'lmagan usulda e'tiborsiz.
/// </param>
public record CreateExpenseRequest(
    DateOnly OnDate, string Category, decimal Amount, string Method, string? Note,
    string? TeacherId = null, Guid? CashBoxId = null);

/// <summary>
/// Chegaradan yuqori chiqimni tasdiqlash (SPEC §4.5). <c>approved_by</c> tanada
/// YO'Q — u JWT'dan olinadi.
/// </summary>
/// <param name="Method">Pul qaysi usulda chiqdi — jurnalning kredit satri shundan.</param>
/// <param name="CashBoxId">Naqd tasdiqda QAYSI kassadan chiqadi. <c>null</c> = SUKUT kassa.</param>
public record ApproveExpenseRequest(string Method, Guid? CashBoxId = null);

/// <summary>Storno so'rovi. Sabab majburiy: u jurnal satrining <c>memo</c> siga tushadi.</summary>
public record ReverseExpenseRequest(string Reason);

/// <summary>
/// Ikki qavatli nazorat chegarasi (SPEC §4.5) — interfeys uchun.
///
/// <para>
/// Nega endpoint kerak: chegara <c>billing_settings.expense_approval_threshold</c>
/// da yashaydi va o'zgaradi. Klient uni O'ZIDA takrorlasa (ilgari
/// <c>expenses.ts</c> da 5 000 000 yozilgan konstanta bor edi), sozlama
/// o'zgargan kuni ekran "tasdiq kutmoqda" degan chiqimni "yozib olingan" deb
/// ko'rsatishda davom etardi — ya'ni tasdiq navbati jimgina bo'shab qolardi.
/// </para>
/// </summary>
/// <param name="Threshold">Shu summadan KATTA chiqim ikkinchi tasdiqni talab qiladi.</param>
public record ExpenseApprovalPolicyDto(decimal Threshold);

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
    /// Ikki qavatli nazorat chegarasi — sozlamadan (SPEC §4.5). Interfeys shu
    /// raqamni SERVERDAN oladi, o'zida saqlamaydi.
    /// </summary>
    Task<ExpenseApprovalPolicyDto> ApprovalPolicyAsync(CancellationToken ct = default);

    /// <summary>
    /// Tasdiqlaydi va AYNAN shu lahzada jurnalga qo'yadi. Tasdiqlovchi
    /// yaratuvchidan boshqa shaxs bo'lishi SHART (SPEC §4.5). Bitta chiqim
    /// bo'yicha tasdiqlar KETMA-KET bajariladi (advisory lock), ya'ni ikki
    /// marta bosilgan tugma jurnalga ikkinchi partiya qo'ymaydi.
    /// </summary>
    Task<ExpenseDto> ApproveAsync(
        Guid id, string method, string approverId, Guid? cashBoxId = null, CancellationToken ct = default);

    /// <summary>
    /// Storno: jurnalga ko'zgu satrlar qo'yiladi, original TEGILMAYDI va
    /// <c>expenses</c> ga yangi qator yozilmaydi (sabab fayl boshida).
    /// Jurnal satrini qo'ygan odam uni o'zi storno qila olmaydi.
    /// </summary>
    Task<ExpenseDto> ReverseAsync(
        Guid id, string reason, string approverId, CancellationToken ct = default);

    /// <summary>
    /// Chiqimga hujjat biriktiradi (F1.08). Fayl allaqachon
    /// <c>POST /api/admin/uploads</c> orqali yuklangan bo'lishi kerak —
    /// bu metod faqat yo'lni yozadi.
    ///
    /// <para>
    /// <c>Detach</c> yoki <c>ReplaceAttachment</c> YO'Q va bo'lmaydi:
    /// <c>expense_attachments</c> da <c>app_rw</c> ga faqat SELECT va INSERT
    /// berilgan (finance-parity §3.1 A4). Noto'g'ri fayl yuklansa, to'g'risi
    /// YANGI qator bo'lib qo'shiladi.
    /// </para>
    /// </summary>
    Task<ExpenseAttachmentDto> AttachAsync(
        Guid expenseId, AttachExpenseFileRequest request, string actorId,
        CancellationToken ct = default);

    /// <summary>Chiqimning hujjatlari (yangisidan eskisiga).</summary>
    Task<IReadOnlyList<ExpenseAttachmentDto>> AttachmentsAsync(
        Guid expenseId, CancellationToken ct = default);
}

/// <inheritdoc cref="IExpenseService"/>
///
/// <para>
/// <b>"Smena" endi YO'Q (kassalar modeli, 2026-09).</b> Bu klass
/// <c>ICashShiftService</c> ga UMUMAN bog'lanmaydi — naqd chiqim endi ochiq
/// smena emas, bitta KASSAGA (<c>cash_box_id</c>) biriktiriladi: so'rovda
/// ko'rsatilgan, aks holda SUKUT (default) kassa. Batafsil:
/// <see cref="AttachCashBoxAsync"/>.
/// </para>
public sealed class ExpenseService(
    IAppDbContext db, ILedgerService ledger) : IExpenseService
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
    /// <b>CHIQIM BO'YICHA TASDIQ QULFI (advisory lock).</b> Kalit satr bilan
    /// nomlangan (<c>expense_approval:{id}</c>), chunki advisory lock'ning
    /// 64-bitli fazosi butun bazada YAGONA: xom <c>id</c> hash'i
    /// <c>billing_guards.sql</c> dagi taqsimot qulfi yoki
    /// <c>CashShiftService</c> ning chek qulfi bilan tasodifan to'qnashib,
    /// bir-biriga aloqasi yo'q ikki amalni navbatga qo'yardi.
    /// Batafsil: <see cref="ApproveAsync"/>.
    /// </summary>
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))";

    private static string LockKey(Guid expenseId) => $"expense_approval:{expenseId:D}";

    /// <summary>
    /// Audit qatoridagi <c>actor_name</c> uchun — keshlangan (P1-14).
    /// Aktyor JWT'dan emas, PARAMETR sifatida keladi (SPEC §4.4), shuning uchun
    /// <c>AuditService.Record</c> emas, <c>AuditService.Entry</c> ishlatiladi.
    /// </summary>
    private readonly ActorNames actors = new(db);

    /// <summary>
    /// Xom SQL uchun kontekstning o'zi. <see cref="IAppDbContext"/> da
    /// <c>Database</c> yo'q (u ataylab tor interfeys), advisory lock esa EF
    /// LINQ bilan ifodalab bo'lmaydigan yagona narsa —
    /// <see cref="CashShiftService"/> dagi bilan bir xil yechim.
    /// </summary>
    private readonly DbContext ef = db as DbContext ?? throw new ArgumentException(
        $"{nameof(ExpenseService)} EF kontekstini talab qiladi: tasdiq qulfi xom SQL orqali "
        + "qo'yiladi. Berilgan implementatsiya DbContext emas.", nameof(db));

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

        await PostAsync(expense, method, actorId, isNew: true, before: null, request.CashBoxId, ct);
        return (await ToDtosAsync([expense], ct))[0];
    }

    // -----------------------------------------------------------------
    //  Tasdiqlash (SPEC §4.5)
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ExpenseDto> ApproveAsync(
        Guid id, string method, string approverId, Guid? cashBoxId = null, CancellationToken ct = default)
    {
        RequireActor(approverId, nameof(approverId));
        var settlement = RequireMethod(method);

        // ---- TASDIQ KETMA-KET BAJARILADI (advisory lock) ----
        //
        // Ilgari bu yerda hech qanday qulf yo'q edi va izohda "direktor bitta
        // tugmani ikki marta bosmaydi" deb yozilgandi. Bosadi: sekin javobda
        // ikkinchi bosish YANGI so'rov bo'lib ketadi. Qulfsiz ikkala so'rov ham
        // `approved_by is null` tekshiruvidan O'TARDI (READ COMMITTED birinchi
        // so'rovning commit qilinmagan qatorini ko'rmaydi), so'ng ikkalasi ham
        // jurnalga partiya qo'yardi — chiqim P&L da IKKI BARAVAR ko'rinardi.
        //
        // `SELECT ... FOR UPDATE` bu yerda ham yaramaydi (`app_rw` da
        // `expenses` ga UPDATE bor, lekin qator qulfi butun jadval huquqiga
        // tayanadigan usul emas) — `CashShiftService.NextReceiptNoAsync` dagi
        // sabab bilan bir xil: advisory lock hech qanday jadval huquqini talab
        // qilmaydi va TRANZAKSIYA oxirida o'zi bo'shaydi. Shuning uchun qulf
        // ochiq tranzaksiya ichida olinadi va hamma tekshiruv qulf OSTIDA,
        // qulfdan keyin o'qilgan qator ustida bajariladi.
        await using var tx = await db.BeginTransactionAsync(ct);

        await ef.Database.ExecuteSqlRawAsync(LockSql, [LockKey(id)], ct);

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

        // F1.03 — naqd tasdiq TASDIQLOVCHI ko'rsatgan (yoki SUKUT) kassadan
        // chiqadi: usulni ham, pulni ham aynan u beradi. Kassa qulfi shu
        // tranzaksiyada olinadi (chiqim qulfi allaqachon olingan — ikkalasi
        // har xil nomlangan kalitlar, ya'ni to'qnashmaydi).
        await AttachCashBoxAsync(expense, settlement, cashBoxId, ct);

        // Jurnal satrining muallifi — TASDIQLOVCHI: pulni haqiqatan chiqarishga
        // ruxsat bergan odam o'sha. Buning ikkinchi ta'siri ham foydali:
        // `LedgerService.ReverseAsync` partiya muallifiga storno'ni taqiqlaydi,
        // ya'ni bu chiqimni keyin UCHINCHI shaxs (yoki yaratuvchi) storno qiladi.
        await WriteAsync(expense, settlement, approverId, isNew: false, before, ct);

        await tx.CommitAsync(ct);

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

        // "SMENA" ENDI YO'Q (kassalar modeli, 2026-09). Ilgari bu yerda naqd
        // chiqimning stornosi storno qiluvchining OCHIQ SMENASINI talab
        // qilardi — endi bunday talab YO'Q: `expenses` ga bu qadamda hech
        // qanday ustun yozilmaydi (fayl boshidagi izoh — storno faqat
        // jurnalga ko'zgu satr qo'shadi), ya'ni biriktiriladigan maydon ham
        // yo'q. Naqd pul ledger orqali qaytadi, kassaga "qaysi" degan savol
        // esa bu yerda ma'nosiz — u faqat YANGI amal (pay_in/pay_out/…)
        // yaratilganda tegishli.

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
    //  Hujjatlar (F1.08) — chiqimning DALILI
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ExpenseAttachmentDto> AttachAsync(
        Guid expenseId, AttachExpenseFileRequest request, string actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(actorId, nameof(actorId));

        if (!await db.Expenses.AsNoTracking().AnyAsync(e => e.Id == expenseId, ct))
            throw BillingRuleException.NotFound("expense_not_found", "Chiqim topilmadi.");

        var fileUrl = Trim(request.FileUrl)
            ?? throw BillingRuleException.Invalid("invalid_file_url", "Fayl manzili bo'sh.");

        // Faqat BIZ saqlagan fayl. Tashqi URL "dalil" emas: uni yuklagan odam
        // istalgan payt almashtirib qo'yishi mumkin va chiqim tekshirib
        // bo'lmaydigan bo'lib qolardi — jadval INSERT-only bo'lgani esa bunga
        // hech qanday to'siq emas, chunki o'zgaradigan narsa fayl, qator emas.
        if (!fileUrl.StartsWith(UploadsPrefix, StringComparison.Ordinal))
            throw BillingRuleException.Invalid("invalid_file_url",
                $"Fayl avval yuklanishi kerak (POST /api/admin/uploads) — manzil "
                + $"'{UploadsPrefix}' bilan boshlanadi.");

        var fileName = Trim(request.FileName)
            ?? throw BillingRuleException.Invalid("invalid_file_name", "Fayl nomi bo'sh.");

        // Kengaytmalar ro'yxati — YUKLASH bilan bir xil manbadan
        // (`UploadGuard`). Ikkinchi ro'yxat bir kun yumshoqroq bo'lardi.
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !UploadGuard.AllowedExtensions.Contains(extension))
            throw BillingRuleException.Invalid("invalid_file_type",
                $"Ruxsat etilmagan fayl turi: '{extension}'.");

        var contentType = Trim(request.ContentType)
            ?? throw BillingRuleException.Invalid("invalid_content_type", "Fayl turi ko'rsatilmagan.");

        // Nol baytli "dalil" dalil emas (bazada ham `ck_expense_attachments_size`).
        if (request.SizeBytes <= 0 || request.SizeBytes > UploadGuard.MaxBytes)
            throw BillingRuleException.Invalid("invalid_file_size",
                $"Fayl hajmi 1 bayt bilan {UploadGuard.MaxBytes} bayt orasida bo'lishi kerak.");

        var attachment = new ExpenseAttachment
        {
            ExpenseId = expenseId,
            FileUrl = fileUrl,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = request.SizeBytes,
            UploadedBy = actorId,
            UploadedAt = AppClock.NowInstant,
        };

        db.ExpenseAttachments.Add(attachment);
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityExpenseAttachment, attachment.Id.ToString("D"), "create",
            $"Chiqimga hujjat biriktirildi: {fileName} ({request.SizeBytes} bayt).",
            actorId: actorId,
            actorName: await actors.OfAsync(actorId, ct),
            after: new { attachment.ExpenseId, attachment.FileUrl, attachment.FileName }));

        await db.SaveChangesAsync(ct);

        return (await AttachmentDtosAsync([attachment], ct))[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpenseAttachmentDto>> AttachmentsAsync(
        Guid expenseId, CancellationToken ct = default)
    {
        var rows = await db.ExpenseAttachments.AsNoTracking()
            .Where(a => a.ExpenseId == expenseId)
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync(ct);

        return await AttachmentDtosAsync(rows, ct);
    }

    private async Task<List<ExpenseAttachmentDto>> AttachmentDtosAsync(
        IReadOnlyList<ExpenseAttachment> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var userIds = rows.Select(a => a.UploadedBy).Distinct(StringComparer.Ordinal).ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return [.. rows.Select(a => new ExpenseAttachmentDto(
            a.Id, a.ExpenseId, a.FileUrl, a.FileName, a.ContentType, a.SizeBytes,
            a.UploadedBy, names.GetValueOrDefault(a.UploadedBy, "—"), a.UploadedAt))];
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

    /// <inheritdoc />
    public async Task<ExpenseApprovalPolicyDto> ApprovalPolicyAsync(CancellationToken ct = default) =>
        new(await ThresholdAsync(ct));

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
    /// <param name="cashBoxId">
    /// Naqd chiqim QAYSI kassadan to'lanadi (kassalar modeli, 2026-09).
    /// <c>null</c> = SUKUT kassa. Naqd bo'lmagan usulda e'tiborsiz.
    /// </param>
    private async Task PostAsync(
        Expense expense, string method, string actorId, bool isNew, object? before,
        Guid? cashBoxId, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        // Kassa qulfi ham, chiqim qatori ham, jurnal ham AYNAN shu
        // tranzaksiyada (F1.03 — fayl boshidagi izoh).
        await AttachCashBoxAsync(expense, method, cashBoxId, ct);
        await WriteAsync(expense, method, actorId, isNew, before, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Naqd chiqimni bitta KASSAGA biriktiradi (F1.03, endi "smena" o'rniga —
    /// kassalar modeli, 2026-09). Naqd bo'lmagan chiqimda hech narsa qilmaydi —
    /// pul bank hisobidan chiqadi va birorta kassaning javoniga tegmaydi.
    ///
    /// <para>
    /// <b>Chaqiruvchi ochiq tranzaksiya ichida bo'lishi SHART.</b> Advisory
    /// lock tranzaksiya oxirida bo'shaydi, ya'ni tranzaksiyasiz u darhol
    /// qo'yib yuborilardi va qulfning ma'nosi qolmasdi.
    /// </para>
    /// <para>
    /// Kassa mavjud/faolligi QULF OSTIDA tekshiriladi: oldingi o'qish qulfdan
    /// OLDIN bo'lgan bo'lardi, ya'ni oradagi lahzada kassa deaktivatsiya
    /// qilingan bo'lishi mumkin edi.
    /// </para>
    /// </summary>
    private async Task AttachCashBoxAsync(
        Expense expense, string method, Guid? cashBoxId, CancellationToken ct)
    {
        if (!PaymentMethod.CountsAsCash(method))
        {
            expense.CashBoxId = null;
            return;
        }

        var boxId = cashBoxId ?? await CashBoxService.DefaultBoxIdAsync(db, ct);

        await ef.Database.ExecuteSqlRawAsync(LockSql, [CashBoxService.BoxLockKey(boxId)], ct);

        var box = await db.CashBoxes.AsNoTracking()
            .Where(b => b.Id == boxId)
            .Select(b => new { b.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw BillingRuleException.NotFound("cash_box_not_found", "Kassa topilmadi.");

        if (!box.IsActive)
            throw BillingRuleException.Conflict("cash_box_inactive", "Bu kassa faol emas.");

        expense.CashBoxId = boxId;
    }

    /// <summary>
    /// <see cref="PostAsync"/> ning ichki qismi — tranzaksiyaSIZ. Tasdiqlash
    /// yo'li tranzaksiyani O'ZI ochadi (qulf tranzaksiya oxirida bo'shaydi,
    /// ya'ni yozuv ham AYNAN o'sha tranzaksiyada bo'lishi kerak), shuning uchun
    /// bu ikkisi ajratilgan: ichma-ich tranzaksiya EF'da xato.
    /// </summary>
    private async Task WriteAsync(
        Expense expense, string method, string actorId, bool isNew, object? before,
        CancellationToken ct)
    {
        var memo = expense.Note is null
            ? $"Chiqim: {expense.Category}"
            : $"Chiqim: {expense.Category} — {expense.Note}";

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

        // Hujjatlar SONI (F1.08) — bitta guruhlangan so'rov, sikl ichida emas.
        // Ro'yxatda faqat "dalil bormi?" degan savol beriladi; fayllarning
        // o'zi alohida endpoint orqali o'qiladi.
        var attachmentCounts = await db.ExpenseAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.ExpenseId))
            .GroupBy(a => a.ExpenseId)
            .Select(g => new { ExpenseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ExpenseId, g => g.Count, ct);

        var boxIds = expenses.Select(e => e.CashBoxId).Where(x => x is not null).Select(x => x!.Value)
            .Distinct().ToList();
        var boxNames = boxIds.Count == 0
            ? []
            : await db.CashBoxes.AsNoTracking()
                .Where(b => boxIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Name })
                .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

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
                e.TeacherId is null ? null : teacherNames.GetValueOrDefault(e.TeacherId, "—"),
                e.CashShiftId,
                attachmentCounts.GetValueOrDefault(e.Id, 0),
                e.CashBoxId,
                e.CashBoxId is null ? null : boxNames.GetValueOrDefault(e.CashBoxId.Value, "—"));
        })];

        string Name(string userId) => names.GetValueOrDefault(userId, "—");
    }

    /// <summary>
    /// <c>audit_log.entity_type</c> qiymati — chiqim hujjatlari shu bo'yicha
    /// topiladi.
    ///
    /// <para>
    /// Konstanta SHU YERDA, <c>AuditService</c> da emas: u fayl bir vaqtda
    /// ishlayotgan bir nechta slice uchun umumiy va har qo'shimcha unda
    /// konflikt beradi. Xuddi shu naqsh <c>CashShiftService.AuditEntityCashShift</c>
    /// da ham ishlatilgan.
    /// </para>
    /// </summary>
    public const string AuditEntityExpenseAttachment = AuditService.EntityExpenseAttachment;

    /// <summary>
    /// <c>UploadsController</c> qaytaradigan yo'lning boshlanishi. Biriktirish
    /// faqat shu prefiksni qabul qiladi (<see cref="AttachAsync"/>).
    /// </summary>
    public const string UploadsPrefix = "/uploads/";

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
        // F1.03 — naqd chiqim qaysi smenadan chiqqani auditda ham qolsin:
        // "kutilgan naqd nega bunday chiqdi" degan savol aynan shu yerda
        // tekshiriladi.
        e.CashShiftId,
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
