namespace SchoolLms.Application.Dtos.Billing;

// ===========================================================================
//  Moliya (billing) DTO'lari — MUZLATILGAN SHARTNOMA. Vazifa: P1-06.
// ===========================================================================
//
//  BU FAYL P1-06 DA BIR MARTA YOZILADI VA MUZLAYDI.
//  Faza 1.C (P1-08…P1-12) va Faza 1.F (P1-16…P1-19) shu shakllarga tayanib
//  PARALLEL yoziladi. Imzoni o'zgartirish — beshta agentning ishini buzish.
//  Agar o'zgartirish CHINDAN kerak bo'lsa: docs/ASSUMPTIONS.md ga yozing va
//  har bir 1.C egasiga xabar bering. Yangi MAYDON qo'shish (ixtiyoriy, sukut
//  qiymatli) — buzmaydigan o'zgarish, bemalol.
//
//  NEGA ALOHIDA NAMESPACE (`...Dtos.Billing`), Dtos.cs EMAS
//  -------------------------------------------------------
//  `Dtos.cs` da allaqachon `PaymentDto` (maosh to'lovi tarixi, 591-qator) va
//  `PaymentRequest` (eski o'quvchi to'lovi, 37-qator) bor. Moliya modulida
//  ham aynan shu ikki nom kerak. Ularni `BillingPaymentDto` deb nomlash
//  butun modulni chirkin qilardi; alohida namespace esa C# da aynan shu
//  muammo uchun mavjud. Kerak bo'lsa `using Billing = ...Dtos.Billing;`.
//
//  UCHTA QAT'IY QOIDA
//  ------------------
//  1. SPEC §4.4 — SERVER ANIQLAYDIGAN SHAXS. `cashier_id`, `created_by`,
//     `approved_by`, `closed_by`, `updated_by` HECH QACHON so'rov tanasida
//     bo'lmaydi. Ular faqat JWT claim'idan olinadi (`FinanceActor.RequireUserId`).
//     Shuning uchun moliyada so'rov tiplari DOIM `Request` bilan tugaydi
//     (`Payload` emas) — qoidani bitta buyruq bilan tekshirsa bo'ladi:
//
//         python3 -c "import re;src=open('SchoolLms.Application/Dtos/BillingDtos.cs').read();\
//         c=re.sub(r'//[^\n]*','',src);\
//         print([n for n,b in re.findall(r'public record ([A-Za-z]+Request)\((.*?)\);',c,re.S)\
//         if any(x in b for x in ('CashierId','CreatedBy','ApprovedBy','ClosedBy','UpdatedBy'))])"
//
//     Natija BO'SH ro'yxat bo'lishi SHART. (P1-06 da tekshirilgan: 13 ta
//     `Request` tipi, shaxsni bildiruvchi maydon 0 ta.)
//
//     FILTR (`*Query`) tiplari BUNDAN MUSTASNO va ularda `CashierId` bo'lishi
//     MUMKIN: "shu kassirning to'lovlarini ko'rsat" — bu GET filtri, "men shu
//     kassirman" degan da'vo emas. Farqi shuki, filtr hech qachon YOZILADIGAN
//     qatorga tushmaydi.
//  2. PULNI FRONTEND HISOBLAMAYDI. Javob DTO'lari hisoblangan qiymatlarni
//     (`Payable`, `Paid`, `Remaining`, `Variance`) TAYYOR holda beradi.
//     JavaScript'da pul `number` (float64) — u yerda ayirish/qo'shish
//     tiyin xatosini keltirib chiqaradi. Serverda `decimal`.
//  3. Sana — `DateOnly` (JSON: "2026-09-01"), vaqt — `DateTimeOffset`
//     (JSON: ofset bilan). Moliyada mintaqasiz vaqt yo'q.

/* ---------- Ma'lumotnoma: to'lov toifalari ---------- */

/// <summary>To'lov toifasi (SPEC §3.7). Beshtasi migratsiyada seed qilingan.</summary>
public record FeeCategoryDto(Guid Id, string Code, string Name, bool IsActive);

/// <summary>Toifa yaratish/tahrirlash. `Code` yaratilgandan keyin o'zgarmaydi.</summary>
public record FeeCategoryRequest(string Code, string Name, bool IsActive);

/* ---------- Obunalar (o'quvchi nimaga yozilgan va qancha) ---------- */

/// <summary>
/// O'quvchining bitta toifadagi obunasi. Oylik hisoblash narxni AYNAN shu
/// yerdan oladi — `SchoolClass.MonthlyFee` endi faqat standart taklif.
/// </summary>
public record StudentSubscriptionDto(
    Guid Id, string StudentId, string StudentName,
    Guid CategoryId, string CategoryCode, string CategoryName,
    decimal MonthlyAmount, string? Detail,
    DateOnly StartsOn, DateOnly? EndsOn,
    bool IsActive,
    string CreatedByName, DateTimeOffset CreatedAt);

/// <summary>Obuna ochish. `created_by` JWT'dan (§4.4).</summary>
public record CreateSubscriptionRequest(
    string StudentId, Guid CategoryId, decimal MonthlyAmount,
    string? Detail, DateOnly StartsOn, DateOnly? EndsOn);

/// <summary>Narx yoki tafsilotni o'zgartirish. Toifa va o'quvchi o'zgarmaydi.</summary>
public record UpdateSubscriptionRequest(decimal MonthlyAmount, string? Detail, DateOnly? EndsOn);

/// <summary>Obunani yopish (o'quvchi avtobusdan chiqdi va h.k.).</summary>
public record EndSubscriptionRequest(DateOnly EndsOn);

/* ---------- Chegirmalar (mijoz javobi: SPEC §8.1 Q5) ---------- */

/// <summary>
/// Chegirma. <b>Chegara YO'Q — har qanday chegirma direktor tasdig'ini
/// talab qiladi.</b> `Status` = pending bo'lsa hisob-kitobga TA'SIR QILMAYDI.
/// </summary>
public record DiscountDto(
    Guid Id, string StudentId, string StudentName,
    Guid? CategoryId, string? CategoryCode, string? CategoryName,
    decimal Percent, decimal Amount, string Reason,
    DateOnly StartsOn, DateOnly? EndsOn,
    // pending | approved | rejected.
    string Status,
    string CreatedByName, string? ApprovedByName,
    DateTimeOffset? DecidedAt, DateTimeOffset CreatedAt);

/// <summary>
/// Chegirma so'rash. HAR DOIM `pending` holatda yaratiladi — bu yerda
/// "darhol qo'llash" varianti YO'Q va bo'lmaydi.
/// </summary>
public record CreateDiscountRequest(
    string StudentId, Guid? CategoryId, decimal Percent, decimal Amount,
    string Reason, DateOnly StartsOn, DateOnly? EndsOn);

/// <summary>Rad etish sababi — majburiy (kim, nega rad etgani ko'rinib tursin).</summary>
public record RejectDiscountRequest(string Reason);

/* ---------- Hisob-fakturalar ---------- */

/// <summary>
/// Bitta o'quvchi × toifa × oy. `Payable`/`Paid`/`Remaining` SERVERDA
/// hisoblanadi — frontend pul arifmetikasi qilmaydi.
/// </summary>
public record InvoiceDto(
    Guid Id, string StudentId, string StudentName,
    Guid CategoryId, string CategoryCode, string CategoryName,
    // Oyning birinchi kuni.
    DateOnly PeriodMonth,
    // To'liq summa, chegirmasiz.
    decimal Amount,
    // Qo'llangan (TASDIQLANGAN) chegirma.
    decimal Discount,
    // To'lash kerak = Amount − Discount.
    decimal Payable,
    // Taqsimotlar yig'indisi.
    decimal Paid,
    // Qoldiq = Payable − Paid.
    decimal Remaining,
    DateOnly DueOn,
    // open | partial | paid | void.
    string Status,
    // Muddati o'tganmi (`overdue_after_day` sozlamasi bo'yicha).
    bool IsOverdue,
    DateTimeOffset CreatedAt);

/// <summary>Hisob-fakturalar ro'yxati uchun filtr (query string).</summary>
public record InvoiceQuery(
    string? StudentId = null, Guid? CategoryId = null,
    DateOnly? FromMonth = null, DateOnly? ToMonth = null,
    string? Status = null, bool OnlyOverdue = false,
    string? ClassName = null);

/// <summary>Bitta oyni hisoblash natijasi (accrual job va qo'lda ishga tushirish).</summary>
public record AccrualResultDto(
    DateOnly PeriodMonth,
    // Yangi yaratilgan hisob-fakturalar soni.
    int Created,
    // Allaqachon mavjud bo'lgani uchun o'tkazib yuborilganlar.
    int Skipped,
    // Yaratilganlarning jami to'lanadigan summasi.
    decimal Total);

/// <summary>O'quvchining moliyaviy kartochkasi — qarz, obunalar, oylar, to'lovlar.</summary>
public record StudentBillingDto(
    string StudentId, string StudentName, string ClassName,
    // Jami qarz (musbat son) = Σ Remaining. 0 = qarzsiz.
    decimal Debt,
    // Taqsimlanmagan avans (oldindan to'langan, hali oy ochilmagan).
    decimal Credit,
    List<StudentSubscriptionDto> Subscriptions,
    List<InvoiceDto> Invoices,
    List<PaymentDto> Payments);

/* ---------- Kassa smenasi (SPEC §4.2) ---------- */

/// <summary>
/// Kassir smenasi. `Variance` BAZADA hisoblanadi (generated column) —
/// yopilgandan keyin uni hech kim tuzata olmaydi.
/// </summary>
public record CashShiftDto(
    Guid Id, string CashierId, string CashierName,
    DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt,
    decimal OpeningFloat,
    // Ledger'dan hisoblangan. Yopilmaguncha null.
    decimal? ExpectedCash,
    // Kassir qo'lda sanagan. Yopilmaguncha null.
    decimal? CountedCash,
    // CountedCash − ExpectedCash. Manfiy = kam chiqdi.
    decimal? Variance,
    // open | closed.
    string Status,
    string? ClosedByName,
    // Shu smenadagi to'lovlar soni (storno ham kiradi).
    int PaymentsCount,
    // Faqat `cash` usuli — smenada sanaladigan qism.
    decimal CashTotal,
    // card + transfer + online — bankka tushadi, sanalmaydi.
    decimal NonCashTotal);

/// <summary>Smena ochish. `cashier_id` JWT'dan (§4.4).</summary>
public record OpenShiftRequest(decimal OpeningFloat);

/// <summary>Smena yopish. Sanalgan naqd MAJBURIY (§4.2). `closed_by` JWT'dan.</summary>
public record CloseShiftRequest(decimal CountedCash, string? Note);

/// <summary>Smenalar ro'yxati uchun filtr.</summary>
public record CashShiftQuery(
    string? CashierId = null, DateOnly? From = null, DateOnly? To = null,
    string? Status = null,
    // true = faqat nomuvofiqligi nol bo'lmagan smenalar (direktor paneli).
    bool OnlyWithVariance = false);

/// <summary>Z-hisobot qatori: to'lov usuli bo'yicha yig'indi.</summary>
public record ZReportMethodRowDto(string Method, int Count, decimal Amount);

/// <summary>Z-hisobot qatori: toifa bo'yicha yig'indi (nimaga to'landi).</summary>
public record ZReportCategoryRowDto(Guid CategoryId, string CategoryCode, string CategoryName, decimal Amount);

/// <summary>
/// Smena yakuni (SPEC §4.6): boshlang'ich qoldiq, usullar kesimi, toifalar
/// kesimi, kutilgan/sanalgan va nomuvofiqlik, chek raqamlari oralig'i.
/// </summary>
public record ZReportDto(
    CashShiftDto Shift,
    List<ZReportMethodRowDto> ByMethod,
    List<ZReportCategoryRowDto> ByCategory,
    // Birinchi chek raqami (smena bo'sh bo'lsa null).
    long? ReceiptFrom,
    // Oxirgi chek raqami.
    long? ReceiptTo,
    // Shu smenadagi storno'lar soni — alohida ko'rsatiladi.
    int ReversalsCount,
    // ---- F1.03 / F1.04: javondan CHIQQAN naqd ----
    // Bu ikki qator `expected_cash` bilan naqd tushum orasidagi farqni
    // tushuntiradi. Ular AYNAN `CashShiftService.CashOutflowAsync` dan
    // keladi, ya'ni kutilgan naqd bilan bir manbadan (o'sha faylning
    // invarianti).
    //
    // Sukut qiymatlari ATAYLAB: DTO pozitsion record va uning imzosi P1-06
    // da muzlatilgan — yangi maydonlar oxiriga qo'shiladi, mavjud
    // chaqiruvlar o'zgarishsiz qoladi.

    // Shu smenadan naqd chiqim sifatida chiqqan pul (storno ayirilgan).
    decimal CashExpensesTotal = 0m,
    // Necha dona naqd chiqim satri (storno satrlari ham sanaladi).
    int CashExpensesCount = 0,
    // Bankka yoki seyfga topshirilgan naqd (storno ayirilgan).
    decimal CashHandoversTotal = 0m,
    int CashHandoversCount = 0,
    // F1.05 — shu smenadan o'quvchilarga QAYTARILGAN naqd (storno qilingan
    // qaytarimlar ayirilgan). AYNAN `CashShiftService.CashOutflowAsync` dan —
    // yuqoridagi ikkitasi bilan bir manbadan (fayl boshidagi invariant).
    decimal CashRefundsTotal = 0m,
    int CashRefundsCount = 0);

/* ---------- To'lovlar ---------- */

/// <summary>Bitta to'lovning bitta hisob-fakturaga taqsimlangan qismi.</summary>
public record PaymentAllocationDto(
    Guid Id, Guid InvoiceId,
    Guid CategoryId, string CategoryCode, string CategoryName,
    DateOnly PeriodMonth, decimal Amount);

/// <summary>
/// Kassaga tushgan to'lov. O'ZGARMAS: tahrirlash/o'chirish endpoint'i YO'Q
/// va bo'lmaydi — tuzatish faqat storno (<see cref="ReversePaymentRequest"/>) orqali.
/// </summary>
public record PaymentDto(
    Guid Id, long ReceiptNo,
    string StudentId, string StudentName,
    decimal Amount,
    // cash | card | transfer | online — FAQAT YORLIQ (§8.1 Q13).
    string Method,
    Guid CashShiftId, string CashierId, string CashierName,
    string? Note, DateTimeOffset ReceivedAt,
    // Bu qator storno bo'lsa — qaysi to'lovni bekor qilgani.
    Guid? ReversalOf,
    // Bu to'lov keyinchalik storno qilingan bo'lsa — storno qatori id'si.
    Guid? ReversedBy,
    // Taqsimlanmagan qoldiq = Amount − Σ Allocations (avans).
    decimal Unallocated,
    List<PaymentAllocationDto> Allocations);

/// <summary>To'lovning bitta hisob-fakturaga yo'naltiriladigan qismi (so'rov).</summary>
public record AllocationRequest(Guid InvoiceId, decimal Amount);

/// <summary>
/// To'lov qabul qilish. `cashier_id` va `cash_shift_id` SERVERDA aniqlanadi
/// (§4.4): kassir JWT'dan, smena esa uning ochiq smenasidan. Ochiq smena
/// bo'lmasa — 409.
///
/// <para>
/// <see cref="Allocations"/> bo'sh bo'lishi mumkin: u holda pul avans
/// sifatida taqsimlanmagan qoladi (docs/ASSUMPTIONS.md, Q15).
/// Yig'indi <see cref="Amount"/> dan oshsa — baza trigger'i rad etadi.
/// </para>
/// </summary>
public record AcceptPaymentRequest(
    string StudentId, decimal Amount, string Method, string? Note,
    List<AllocationRequest> Allocations);

/// <summary>
/// Storno. Sabab MAJBURIY (SPEC §4.3). Tasdiqlovchi JWT'dan; kassir bu
/// amalni umuman chaqira olmaydi.
/// </summary>
public record ReversePaymentRequest(string Reason);

/// <summary>To'lovlar ro'yxati uchun filtr.</summary>
public record PaymentQuery(
    string? StudentId = null, string? CashierId = null, Guid? CashShiftId = null,
    DateOnly? From = null, DateOnly? To = null, string? Method = null,
    // true = faqat storno qatorlari.
    bool OnlyReversals = false);

/// <summary>
/// Kassir ekranidagi TAKLIF: pulni qaysi hisob-fakturalarga taqsimlash
/// mumkin (eski `StudentLedger` dagi FIFO algoritmi). Bu faqat taklif —
/// haqiqiy taqsimotni kassir tasdiqlaydi va u `payment_allocations` da
/// yoziladi.
/// </summary>
public record AllocationSuggestionDto(
    Guid InvoiceId, Guid CategoryId, string CategoryCode, string CategoryName,
    DateOnly PeriodMonth, decimal Remaining, decimal Suggested);

/* ---------- Chiqimlar ---------- */

// CHIQIM DTO'LARI BU YERDA EMAS — `SchoolLms.Application/Billing/ExpenseService.cs` da.
//
// Bu yerda P1-06 da yozilgan ikkita qoralama bor edi (`ExpenseDto`,
// `CreateExpenseRequest`) va ular P1-14b da xizmat yozilganda YANGILANMADI:
// `Method`, `Status`, `SettlementAccount`, `TeacherId` va storno maydonlari
// faqat `Billing` namespace'idagi nusxada bor. Ikkita bir xil nomli, har xil
// shaklli shartnoma — chiqimni JO'NATADIGAN kod qaysi biriga qaraganini
// tasodifga qoldirardi (`AnomalyScanTests` allaqachon to'liq nom bilan
// yozishga majbur bo'lgan). Ikkalasini ham hech kim ishlatmagani tekshirilib,
// qoralamalar olib tashlandi; yagona shartnoma — `Billing` dagi.

/* ---------- Ledger (ikki yoqlama jurnal) ---------- */

/// <summary>Jurnal yozuvi — faqat o'qish uchun (yozish faqat `LedgerService` orqali).</summary>
public record LedgerEntryDto(
    long Id, DateOnly EntryDate, string Account,
    // debit | credit.
    string Direction,
    decimal Amount,
    // payment | invoice | expense | salary | reversal.
    string RefType,
    Guid? RefId, string? Memo,
    string CreatedByName, DateTimeOffset CreatedAt, long? ReversalOf);

/// <summary>Hisob bo'yicha qoldiq (debet − kredit). P&amp;L va Cash Flow shundan quriladi.</summary>
public record AccountBalanceDto(string Account, decimal Debit, decimal Credit, decimal Balance);

/* ---------- Hisobotlar ---------- */

/// <summary>Qarzdorlar hisoboti qatori.</summary>
public record DebtorRowDto(
    string StudentId, string FullName, string ClassName, string ParentPhone,
    decimal Debt,
    // Eng eski to'lanmagan oy.
    DateOnly? OldestUnpaidMonth,
    // Necha kun kechikkan (`overdue_after_day` dan hisoblanadi). 0 = kechikmagan.
    int DaysOverdue,
    List<DebtorCategoryRowDto> ByCategory);

/// <summary>Qarzdorning toifalar bo'yicha yoyilmasi.</summary>
public record DebtorCategoryRowDto(string CategoryCode, string CategoryName, decimal Debt);

/// <summary>Oylik daromad dinamikasi (toifalar kesimida).</summary>
public record BillingMonthlyDto(
    DateOnly PeriodMonth,
    // Shu oyga hisoblangan (chegirmadan keyin).
    decimal Accrued,
    // Shu oyda HAQIQATAN tushgan pul.
    decimal Collected,
    // Yig'ilish darajasi, % (Collected / Accrued × 100). Accrued = 0 bo'lsa null.
    decimal? CollectionRate);

/* ---------- Oyma-oy qarzdorlik (arrears pivot) ---------- */

/// <summary>
/// Jadvalning bitta katagi: bitta o'quvchining bitta oyi.
/// <c>ToBePaid</c> = <c>max(0, Amount − Paid)</c> — server hisoblaydi,
/// frontend pul arifmetikasini qaytarmaydi (bu fayldagi 2-qoida).
/// </summary>
public record ArrearsCellDto(decimal Amount, decimal Paid, decimal ToBePaid);

/// <summary>
/// Jadvalning bitta qatori — bitta o'quvchi.
///
/// <para>
/// <see cref="Cells"/> kaliti — "YYYY-MM". Oyda hisob-faktura bo'lmasa kalit
/// UMUMAN yo'q: "maktabda bo'lmagan oy" va "to'lab bo'lingan oy" ekranda ham,
/// bu yerda ham bir xil ko'rinmasligi kerak.
/// </para>
/// <para>
/// <b>Invariant:</b> <see cref="Total"/> — kataklar yig'indisi.
/// </para>
/// </summary>
public record ArrearsRowDto(
    string StudentId, string FullName, string ClassName,
    // Arxivdagi (maktabdan ketgan) o'quvchi — qarzi qoladi, ekranda belgilanadi.
    bool IsArchived,
    IReadOnlyDictionary<string, ArrearsCellDto> Cells,
    ArrearsCellDto Total);

/// <summary>
/// Oyma-oy qarzdorlik jadvali: o'quvchi × oy.
/// <see cref="Footer"/> — ustun yakunlari (kalit "YYYY-MM"),
/// <see cref="Total"/> — butun jadval yakuni. Ikkovi ham FAQAT filtrdan
/// o'tgan qatorlardan yig'iladi.
/// </summary>
public record ArrearsPivotDto(
    // Ustunlar tartibi — "YYYY-MM", oyma-oy, bo'sh oy ham ro'yxatda qoladi.
    IReadOnlyList<string> Months,
    IReadOnlyList<ArrearsRowDto> Rows,
    IReadOnlyDictionary<string, ArrearsCellDto> Footer,
    ArrearsCellDto Total);

/* ---------- Moliya sozlamalari (mijoz javobi: SPEC §8.1 Q6) ---------- */

/// <summary>To'lov muddati sozlamalari — qat'iy raqam emas, tahrirlanadi.</summary>
public record BillingSettingsDto(
    // Hisob-faktura to'lov muddati — oyning shu kuni (1..28).
    int PaymentDueDay,
    // Shu kundan keyin qarz "muddati o'tgan" hisoblanadi (1..28).
    int OverdueAfterDay,
    DateTimeOffset UpdatedAt, string? UpdatedByName);

/// <summary>Sozlamalarni saqlash. `updated_by` JWT'dan (§4.4).</summary>
public record UpdateBillingSettingsRequest(int PaymentDueDay, int OverdueAfterDay);
