using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Moliya xizmatlari — MUZLATILGAN IMZOLAR. Vazifa: P1-06.
// ===========================================================================
//
//  Faza 1.C (P1-07…P1-12) FAQAT implementatsiya yozadi. Bu fayldagi imzo
//  o'zgarsa — parallel ishlayotgan beshta agentning kodi kompilyatsiyadan
//  o'tmay qoladi. O'zgartirish kerak bo'lsa: docs/ASSUMPTIONS.md ga yozing
//  va har bir 1.C egasiga xabar bering.
//
//  SPEC §2.2 — MOLIYAVIY YOZUV UMUMIY REPOZITORIY ORQALI O'TMAYDI.
//  Shu sababli bu yerda "generic CRUD" yo'q: har bir metod aniq biznes
//  amalini bildiradi va kim qilayotganini (actorId) majburiy oladi.
//
//  ACTOR ID QOIDASI (SPEC §4.4): har bir yozuv metodi `actorId` / `cashierId`
//  / `approverId` ni ALOHIDA parametr sifatida oladi — DTO ichida emas.
//  Controller uni JWT claim'idan beradi (`FinanceActor.RequireUserId`),
//  so'rov tanasidan HECH QACHON emas.

/// <summary>
/// Ikki yoqlama jurnalga yozadigan YAGONA kod (P1-07). SPEC §2.2.
///
/// <para>
/// Bu interfeysda `Update` ham, `Delete` ham YO'Q — va bo'lmaydi. Xato yozuv
/// <see cref="ReverseAsync"/> bilan tuzatiladi: original tegilmaydi, uning
/// ko'zgusi qo'shiladi. Baza darajasida ham shunday (`app_rw` da UPDATE/DELETE
/// yo'q), ya'ni bu qoida ikki joyda mustahkamlangan.
/// </para>
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// Balanslashgan yozuvlar to'plamini jurnalga qo'yadi.
    ///
    /// <para>
    /// Debet yig'indisi kredit yig'indisiga TENG bo'lmasa — <c>SaveChanges</c>
    /// gacha <see cref="InvalidOperationException"/>. Noma'lum hisob kodi
    /// (<see cref="Accounts"/> ro'yxatida yo'q) ham shu yerda yiqiladi.
    /// </para>
    /// </summary>
    /// <param name="entries">Kamida ikkita satr: debet va kredit.</param>
    /// <param name="actorId">JWT'dagi foydalanuvchi id'si (SPEC §4.4).</param>
    Task<IReadOnlyList<LedgerEntry>> PostAsync(
        IReadOnlyList<LedgerPosting> entries, string actorId, CancellationToken ct = default);

    /// <summary>
    /// Yozuvni teskari qiladi: har bir satrning yo'nalishi almashtirilib,
    /// <c>reversal_of</c> bilan YANGI qatorlar qo'shiladi. Original
    /// O'ZGARTIRILMAYDI va o'chirilmaydi.
    /// </summary>
    /// <param name="entryId">Teskari qilinayotgan yozuv id'si.</param>
    /// <param name="reason">Sabab — majburiy (SPEC §4.3).</param>
    /// <param name="approverId">Tasdiqlovchi. Storno har doim ikkinchi shaxsni
    /// talab qiladi (SPEC §4.5) — kassir o'zi qila olmaydi.</param>
    Task<IReadOnlyList<LedgerEntry>> ReverseAsync(
        long entryId, string reason, string approverId, CancellationToken ct = default);

    /// <summary>Bitta hisob bo'yicha qoldiq (debet − kredit) berilgan davrda.</summary>
    Task<decimal> BalanceAsync(
        string account, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);

    /// <summary>Barcha hisoblar kesimi — P&amp;L va Cash Flow shundan quriladi (P1-13).</summary>
    Task<IReadOnlyList<AccountBalanceDto>> TrialBalanceAsync(
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
}

/// <summary>
/// Jurnalga qo'yiladigan BITTA satr (kirish tipi — entity emas).
/// <c>created_by</c> va <c>created_at</c> ni xizmatning o'zi to'ldiradi,
/// shuning uchun bu yerda ular YO'Q (SPEC §4.4).
/// </summary>
/// <param name="Account">Hisob kodi — <see cref="Accounts"/> dagi yopiq ro'yxatdan.</param>
/// <param name="Direction">debit | credit (<see cref="LedgerDirection"/>).</param>
/// <param name="Amount">Har doim musbat; belgi yo'nalishdan kelib chiqadi.</param>
/// <param name="RefType">payment | invoice | expense | salary | reversal.</param>
/// <param name="RefId">Manba yozuv id'si (bo'lsa).</param>
/// <param name="EntryDate">Buxgalteriya sanasi. Berilmasa — bugun (AppClock).</param>
/// <param name="Memo">O'zbekcha qisqa izoh — hisobotda shu ko'rinadi.</param>
public record LedgerPosting(
    string Account,
    string Direction,
    decimal Amount,
    string RefType,
    Guid? RefId = null,
    DateOnly? EntryDate = null,
    string? Memo = null);

/// <summary>Hisob-fakturalar va oylik hisoblash (P1-09).</summary>
public interface IInvoiceService
{
    /// <summary>
    /// Bitta oyni hisoblaydi: har faol obuna uchun bitta hisob-faktura.
    /// IDEMPOTENT — mavjud qator qayta yaratilmaydi (unique indeks kafolatlaydi).
    /// Chegirmalardan FAQAT <c>approved</c> lari qo'llanadi (SPEC §8.1 Q5).
    /// </summary>
    Task<AccrualResultDto> AccrueMonthAsync(
        DateOnly periodMonth, string actorId, CancellationToken ct = default);

    /// <summary>
    /// Hisoblanmagan BARCHA oylarni to'ldiradi (fon xizmati shuni chaqiradi).
    /// Eski <c>TuitionService.AccrueDue</c> ning toifalarga bo'lingan vorisi.
    /// </summary>
    Task<IReadOnlyList<AccrualResultDto>> AccrueDueAsync(
        string actorId, CancellationToken ct = default);

    Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceQuery query, CancellationToken ct = default);

    /// <summary>
    /// Registr uchun: server tomonda sahifalangan ro'yxat va BUTUN FILTR
    /// bo'yicha yakun (F10.01, docs/modules/finance-parity.md §2.10).
    ///
    /// <para>
    /// QO'SHIMCHA metod — <see cref="ListAsync"/> tegilmadi: uning imzosi
    /// P1-06 da muzlatilgan va uni o'zgartirish parallel ishlayotgan kodni
    /// buzardi (fayl boshidagi izoh).
    /// </para>
    /// </summary>
    Task<InvoicePageDto> ListPageAsync(InvoicePageQuery query, CancellationToken ct = default);

    /// <summary>O'quvchining to'liq moliyaviy kartochkasi (qarz, obunalar, oylar, to'lovlar).</summary>
    Task<StudentBillingDto?> ForStudentAsync(string studentId, CancellationToken ct = default);

    /// <summary>
    /// Kassir ekrani uchun: shu o'quvchining hali to'liq to'lanmagan
    /// hisob-fakturalari (eng eskisi birinchi).
    /// </summary>
    Task<IReadOnlyList<InvoiceDto>> OpenForStudentAsync(string studentId, CancellationToken ct = default);

    /// <summary>
    /// Xato hisoblangan oyni bekor qiladi (<c>status = 'void'</c>) — qarzga
    /// kirmay qoladi. Taqsimoti bor hisob-fakturani bekor qilib bo'lmaydi.
    /// </summary>
    Task<InvoiceDto> VoidAsync(
        Guid invoiceId, string reason, string actorId, CancellationToken ct = default);
}

/// <summary>Kassa smenasi, chek raqami, Z-hisobot (P1-10). SPEC §4.2.</summary>
public interface ICashShiftService
{
    /// <summary>
    /// Smena ochadi. Kassirda allaqachon ochiq smena bo'lsa xato — buni
    /// bazadagi qisman unikal indeks ham kafolatlaydi
    /// (<c>ux_cash_shifts_one_open_per_cashier</c>).
    /// </summary>
    Task<CashShiftDto> OpenAsync(
        string cashierId, decimal openingFloat, CancellationToken ct = default);

    /// <summary>Kassirning ochiq smenasi; yo'q bo'lsa null.</summary>
    Task<CashShiftDto?> CurrentAsync(string cashierId, CancellationToken ct = default);

    /// <summary>
    /// Smenani yopadi: <c>expected_cash</c> LEDGER'dan hisoblanadi,
    /// <c>counted_cash</c> kassirdan keladi, <c>variance</c> ni BAZA hisoblaydi.
    /// Kassir o'zganing smenasini yopa olmaydi (SPEC §4.2).
    /// </summary>
    Task<CashShiftDto> CloseAsync(
        Guid shiftId, string closedByUserId, decimal countedCash, string? note,
        CancellationToken ct = default);

    /// <summary>
    /// Smena ichidagi keyingi chek raqami — UZLUKSIZ (SPEC §4.2).
    /// Raqam berish va to'lovni yozish BITTA tranzaksiyada bo'lishi shart,
    /// aks holda bekor qilingan so'rov raqamda teshik qoldiradi.
    /// </summary>
    Task<long> NextReceiptNoAsync(Guid shiftId, CancellationToken ct = default);

    Task<ZReportDto> ZReportAsync(Guid shiftId, CancellationToken ct = default);

    Task<IReadOnlyList<CashShiftDto>> ListAsync(CashShiftQuery query, CancellationToken ct = default);
}

/// <summary>To'lov qabul qilish, taqsimlash, storno (P1-11).</summary>
public interface IPaymentService
{
    /// <summary>
    /// To'lovni qabul qiladi va (berilgan bo'lsa) taqsimlaydi. Bitta
    /// tranzaksiyada: chek raqami, <c>payments</c>, <c>payment_allocations</c>,
    /// hisob-faktura statusi va ledger yozuvlari.
    ///
    /// <para>
    /// <paramref name="cashierId"/> JWT'dan keladi; smena ham serverda
    /// aniqlanadi (kassirning ochiq smenasi). So'rovda ular YO'Q (SPEC §4.4).
    /// </para>
    /// </summary>
    Task<PaymentDto> AcceptAsync(
        AcceptPaymentRequest request, string cashierId, CancellationToken ct = default);

    /// <summary>
    /// Storno: qarshi <c>payments</c> qatori + ko'zgu ledger yozuvlari.
    /// Original TEGILMAYDI. Kassir chaqira olmaydi (SPEC §4.3), sabab majburiy.
    /// </summary>
    /// <param name="cashBoxId">
    /// Storno QAYSI kassaga qaytishi (kassalar modeli, 2026-09 — "smena"
    /// o'rnini bosadi). <c>null</c> = SUKUT (default) kassa.
    /// </param>
    Task<PaymentDto> ReverseAsync(
        Guid paymentId, string reason, string approverId, Guid? cashBoxId = null, CancellationToken ct = default);

    Task<PaymentDto?> GetAsync(Guid paymentId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentDto>> ListAsync(PaymentQuery query, CancellationToken ct = default);

    /// <summary>
    /// Kassir ekranidagi TAKLIF: summani eng eski qarzdan boshlab taqsimlash
    /// (eski <c>StudentLedger</c> dagi FIFO algoritmi). Bu faqat taklif —
    /// haqiqiy taqsimotni kassir tasdiqlaydi.
    /// </summary>
    Task<IReadOnlyList<AllocationSuggestionDto>> SuggestAllocationAsync(
        string studentId, decimal amount, CancellationToken ct = default);
}

/// <summary>
/// Chegirmalar (P1-08). <b>Mijoz javobi (SPEC §8.1 Q5): chegara YO'Q —
/// har qanday chegirma direktor tasdig'ini talab qiladi.</b>
/// </summary>
public interface IDiscountService
{
    /// <summary>HAR DOIM <c>pending</c> holatda yaratadi. `created_by` JWT'dan.</summary>
    Task<DiscountDto> CreateAsync(
        CreateDiscountRequest request, string createdByUserId, CancellationToken ct = default);

    /// <summary>
    /// Tasdiqlaydi (faqat <c>superadmin</c>). O'zi yaratgan chegirmani
    /// tasdiqlashga urinish rad etiladi — ilovada ham, bazadagi
    /// <c>ck_discounts_approver_differs</c> constraint'ida ham.
    /// </summary>
    Task<DiscountDto> ApproveAsync(
        Guid discountId, string approverId, CancellationToken ct = default);

    /// <summary>Rad etadi. Qator tarix uchun qoladi, hisob-kitobga kirmaydi.</summary>
    Task<DiscountDto> RejectAsync(
        Guid discountId, string approverId, string reason, CancellationToken ct = default);

    Task<IReadOnlyList<DiscountDto>> ListAsync(DiscountQuery query, CancellationToken ct = default);

    /// <summary>Direktor paneli uchun "tasdiq kutmoqda" navbati.</summary>
    Task<IReadOnlyList<DiscountDto>> PendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Chegirmadan KEYINGI summa. Eski <c>TuitionService.ChargeFor</c> ning
    /// AYNAN o'sha arifmetikasi: avval foiz, keyin aniq summa, quyi chegara 0.
    /// Qayta yozilmaydi — ko'chiriladi (P1-23 eski qiymatlar bilan solishtiradi).
    /// </summary>
    decimal ChargeFor(decimal grossAmount, decimal percent, decimal amount);

    /// <summary>Chegirma summasi = <paramref name="grossAmount"/> − ChargeFor(...).</summary>
    decimal DiscountFor(decimal grossAmount, decimal percent, decimal amount);
}

/// <summary>Chegirmalar ro'yxati uchun filtr.</summary>
public record DiscountQuery(
    string? StudentId = null,
    Guid? CategoryId = null,
    // pending | approved | rejected. null = hammasi.
    string? Status = null);

/// <summary>
/// Chek: PDF yasash va ota-onaga Telegram orqali yuborish (P1-12).
/// SPEC §4.7 — to'lovchi qo'lida maktab o'zgartira olmaydigan nusxa qoladi.
/// </summary>
public interface IReceiptService
{
    /// <summary>Chekni PDF sifatida chizadi (A5). Storno cheki ham shu yerda.</summary>
    Task<byte[]> RenderPdfAsync(Guid paymentId, CancellationToken ct = default);

    /// <summary>
    /// Chekni ota-onaning Telegram chatiga yuboradi (mavjud
    /// <c>TelegramService.SendDocumentAsync</c> orqali). Ota-ona ro'yxatdan
    /// o'tmagan bo'lsa false qaytadi — bu xato emas, kutilgan holat.
    /// </summary>
    Task<bool> SendToGuardianAsync(Guid paymentId, CancellationToken ct = default);
}
