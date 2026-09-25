namespace SchoolLms.Domain;

// ===========================================================================
//  Moliya (billing) modeli — SPEC §3.7 va §4.
//  Vazifa: P1-04.
// ===========================================================================
//
//  NEGA ALOHIDA FAYL (Entities.cs ga qo'shilmagan)
//  ----------------------------------------------
//  `Entities.cs` 1077 qator va repozitoriyadagi eng ko'p konflikt beradigan fayl.
//  Faza 1.C da beshta agent bir vaqtda moliya kodini yozadi. Shuning uchun yangi
//  entity'lar shu yerda yashaydi va `Entities.cs` ga UMUMAN tegilmaydi
//  (qabul mezoni: `git diff --stat SchoolLms.Domain/Entities.cs` bo'sh).
//
//  TIPLAR — NEGA ESKI JADVALLARDAN FARQ QILADI
//  -------------------------------------------
//  Faza 0 eski sxemani Postgres'ga "borligicha" ko'chirgan: id'lar `text`, sanalar
//  `text`. Yangi moliya jadvallari esa HAQIQIY tiplarda:
//      id            -> uuid            (Guid)
//      sana          -> date            (DateOnly)
//      vaqt belgisi  -> timestamptz     (DateTimeOffset)
//      pul           -> numeric(14,2)   (decimal + HasPrecision(14,2))
//  53 ta eski entity'ni ko'chirish Faza 1 ishi emas (docs/ASSUMPTIONS.md), shuning
//  uchun o'quvchi/foydalanuvchiga havolalar `text` bo'lib qoladi — bu aralashma
//  ataylab va arzon: FK ustuni turi ota-jadval PK turiga mos, ya'ni HAQIQIY FK.
//
//  DIQQAT — VAQT TIPI
//  ------------------
//  `AppDbContext.OnModelCreating` oxirida BARCHA `DateTime` ustunlari
//  `timestamp without time zone` ga majburlanadi (eski kod Toshkent "devor soati"ni
//  saqlaydi). Moliyada bu YARAMAYDI: smena chegarasi va `received_at` mintaqasiz
//  bo'lsa, hisobotni qayta hisoblab bo'lmaydi. Shuning uchun moliya vaqtlari
//  `DateTimeOffset` — Npgsql uni `timestamptz` ga moslaydi va yuqoridagi tsikl
//  (u faqat `DateTime` ni qidiradi) ularga TEGMAYDI.
//
//  O'ZGARMASLIK (immutability)
//  ---------------------------
//  `payments`, `payment_allocations`, `ledger_entries` — faqat INSERT. `app_rw`
//  rolida UPDATE/DELETE yo'q (SPEC §4.1, migratsiyada REVOKE). Xato to'lov
//  `reversal_of` bilan YANGI qator qo'shib tuzatiladi, tahrirlash bilan emas.
//  Shu sabab bu uch entity'da "o'zgartirish" uchun metod ham, setter mantiqi ham
//  yo'q — ular yozilgach o'lik.

/// <summary>
/// To'lov toifasi — SPEC §3.7: <c>tuition</c> | <c>bus</c> | <c>dormitory</c> |
/// <c>meals</c> | <c>other</c>. Beshtasi migratsiyada seed qilinadi (id'lari
/// barqaror), keyin admin yangisini qo'sha oladi.
/// </summary>
/// <summary>
/// Toifa kodlari — mashina uchun barqaror kalitlar. Ular migratsiyada seed
/// qilinadi va O'ZGARMAYDI (nom o'zgarishi mumkin, kod emas).
/// </summary>
public static class FeeCategoryCode
{
    /// <summary>
    /// O'qish to'lovi. Oy ichida taqsimlashda BIRINCHI o'rinda turadi (mijoz, 2026-09-24) —
    /// tartib: <see cref="SchoolLms.Application.Billing.PaymentService.CategoryRank"/>.
    /// </summary>
    public const string Tuition = "tuition";
    public const string Bus = "bus";
    public const string Dormitory = "dormitory";
    public const string Meals = "meals";
    public const string Other = "other";
}

public class FeeCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Mashina uchun kalit (unikal, o'zgarmaydi): tuition, bus, ...</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Ko'rsatiladigan nom (o'zbekcha).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>false = yangi obuna/hisob-faktura ochib bo'lmaydi (tarix qoladi).</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Abonementning o'zgarmas oylik narxi (so'm). Belgilangan bo'lsa, yangi obuna
    /// AYNAN shu summa bilan ochiladi — o'quvchi kartochkasida qo'lda yozilmaydi.
    /// null = narx belgilanmagan (masalan, <c>tuition</c> — narxi sinfdan olinadi).
    /// </summary>
    public decimal? MonthlyAmount { get; set; }
}

/// <summary>
/// O'quvchi qaysi toifaga va qancha summaga yozilgan. Oylik hisoblash (accrual)
/// AYNAN shu jadvaldan narx oladi — <c>SchoolClass.MonthlyFee</c> endi faqat
/// birinchi obuna yaratilganda taklif qilinadigan standart qiymat (P1-08).
/// </summary>
public class StudentSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>O'quvchi (students.id — `text`).</summary>
    public string StudentId { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    /// <summary>Oylik summa (so'm). 0 bo'lishi mumkin (bepul o'qish).</summary>
    public decimal MonthlyAmount { get; set; }
    /// <summary>Tafsilot: avtobus yo'nalishi, yotoqxona xonasi va h.k.</summary>
    public string? Detail { get; set; }
    public DateOnly StartsOn { get; set; }
    /// <summary>null = hali tugamagan (faol obuna).</summary>
    public DateOnly? EndsOn { get; set; }
    /// <summary>Kim yaratgan (users.id) — JWT'dan olinadi, so'rov tanasidan EMAS (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Chegirma. <b>Mijoz javobi (SPEC §8.1, Q5): har qanday chegirma direktor
/// tasdig'ini talab qiladi — chegara YO'Q.</b> Shuning uchun chegirma
/// <see cref="DiscountStatus.Pending"/> holatda tug'iladi va
/// <see cref="ApprovedBy"/> qo'yilmaguncha hisob-kitobga UMUMAN ta'sir qilmaydi:
/// accrual faqat <see cref="DiscountStatus.Approved"/> larni ko'radi.
///
/// <para>
/// Ikki qavatli himoya (SPEC §4.5): ilova darajasida "o'zi yaratgan chegirmani
/// o'zi tasdiqlay olmaydi" tekshiruvi, va baza darajasida
/// <c>check (approved_by is null or approved_by &lt;&gt; created_by)</c>.
/// </para>
/// </summary>
public class Discount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>null = BARCHA toifalarga tegishli.</summary>
    public Guid? CategoryId { get; set; }
    /// <summary>Foiz (0..100). Avval foiz, keyin <see cref="Amount"/> ayriladi.</summary>
    public decimal Percent { get; set; }
    /// <summary>Aniq summa (so'm), foizdan keyin ayriladi.</summary>
    public decimal Amount { get; set; }
    /// <summary>Sabab — majburiy, hisobotda va audit'da ko'rinadi.</summary>
    public string Reason { get; set; } = string.Empty;
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    /// <summary>pending | approved | rejected — <see cref="DiscountStatus"/>.</summary>
    public string Status { get; set; } = DiscountStatus.Pending;
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>Tasdiqlagan direktor (users.id). null = hali tasdiqlanmagan.</summary>
    public string? ApprovedBy { get; set; }
    /// <summary>Tasdiqlangan/rad etilgan vaqt. null = hali qaror qabul qilinmagan.</summary>
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Chegirma holatlari (<see cref="Discount.Status"/>).</summary>
public static class DiscountStatus
{
    /// <summary>Yaratilgan, direktor tasdig'ini kutmoqda. Hisob-kitobga TA'SIR QILMAYDI.</summary>
    public const string Pending = "pending";
    /// <summary>Tasdiqlangan — accrual shu chegirmani qo'llaydi.</summary>
    public const string Approved = "approved";
    /// <summary>Rad etilgan. Tarix uchun qoladi, hisob-kitobga kirmaydi.</summary>
    public const string Rejected = "rejected";

    public static readonly IReadOnlyList<string> All = [Pending, Approved, Rejected];
}

/// <summary>
/// Oylik hisob-faktura: bitta o'quvchi × bitta toifa × bitta oy. Qarz AYNAN shu
/// jadvaldan hisoblanadi (o'quvchi qatoridagi saqlangan qoldiq P1-21 da o'chdi):
/// qarz = Σ(amount − discount) − Σ(payment_allocations.amount).
/// </summary>
public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StudentId { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    /// <summary>Oyning BIRINCHI kuni (2026-09-01 = 2026-yil sentyabr).</summary>
    public DateOnly PeriodMonth { get; set; }
    /// <summary>To'liq summa, chegirmasiz (obunadagi narx).</summary>
    public decimal Amount { get; set; }
    /// <summary>Qo'llangan chegirma summasi. To'lash kerak = Amount − Discount.</summary>
    public decimal Discount { get; set; }
    /// <summary>To'lov muddati — <see cref="BillingSettings.PaymentDueDay"/> dan hisoblanadi.</summary>
    public DateOnly DueOn { get; set; }
    /// <summary>open | partial | paid | void — <see cref="InvoiceStatus"/>.</summary>
    public string Status { get; set; } = InvoiceStatus.Open;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Hisob-faktura holatlari (<see cref="Invoice.Status"/>).</summary>
public static class InvoiceStatus
{
    /// <summary>Hech narsa to'lanmagan.</summary>
    public const string Open = "open";
    /// <summary>Qisman to'langan.</summary>
    public const string Partial = "partial";
    /// <summary>To'liq to'langan.</summary>
    public const string Paid = "paid";
    /// <summary>Bekor qilingan (xato hisoblangan). Summasi qarzga kirmaydi.</summary>
    public const string Void = "void";

    public static readonly IReadOnlyList<string> All = [Open, Partial, Paid, Void];
}

/// <summary>
/// Kassaga tushgan to'lov. <b>O'ZGARMAS</b> (SPEC §4.1): <c>app_rw</c> roli bu
/// jadvalda UPDATE va DELETE qila olmaydi. Xato to'lov <see cref="ReversalOf"/>
/// bilan qarshi yozuv qo'shib tuzatiladi.
///
/// <para>
/// <b>Bitta to'lov — bitta o'quvchi</b> (mijoz javobi, SPEC §8.1 Q14). Ikki
/// farzandga to'layotgan ota-ona ikkita to'lov (ikkita chek) oladi.
/// </para>
/// </summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Chek raqami — smena ichida uzluksiz (SPEC §4.2). P1-10 beradi.</summary>
    public long ReceiptNo { get; set; }
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Summa (har doim &gt; 0; storno ham musbat, uni <see cref="ReversalOf"/> belgilaydi).</summary>
    public decimal Amount { get; set; }
    /// <summary>cash | card | transfer | online — <see cref="PaymentMethod"/>.</summary>
    public string Method { get; set; } = PaymentMethod.Cash;
    /// <summary>
    /// Eski smena havolasi. <b>"Smena" tizimdan olib tashlangan (kassalar
    /// modeli, 2026-09) — endi <c>null</c> bo'la oladi.</b> Yangi to'lovlar
    /// buni hech qachon to'ldirmaydi (<see cref="CashBoxId"/> o'rnini bosadi);
    /// eski qatorlarda esa tarix sifatida qoladi (SPEC §4.1 — pul yozuvi
    /// o'chirilmaydi ham, "taxmin" ham qilinmaydi).
    /// </summary>
    public Guid? CashShiftId { get; set; }
    /// <summary>
    /// Qaysi kassaga tushdi (<c>cash_boxes</c>, "smena" o'rnini bosuvchi
    /// model, 2026-09). <c>null</c> = eski qator (kassa modelidan oldin
    /// yozilgan) — TAXMIN QILINMAYDI. Yangi to'lov har doim bitta kassaga
    /// (ko'rsatilmasa — sukut kassaga) tushadi.
    /// </summary>
    public Guid? CashBoxId { get; set; }
    /// <summary>Kassir (users.id) — JWT'dan, so'rov tanasidan EMAS (SPEC §4.4).</summary>
    public string CashierId { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    /// <summary>Storno: qaysi to'lovni bekor qilmoqda. null = oddiy to'lov.</summary>
    public Guid? ReversalOf { get; set; }

    /// <summary>
    /// Chekdagi QR kodning O'ZGARMAS kaliti (mijoz, 2026-09-24): QR skanerlanganda
    /// <c>/r/{token}</c> sahifasi shu to'lov haqida ma'lumot beradi. 128 bitli tasodifiy qiymat —
    /// chek raqami yoki id'dan taxmin qilib bo'lmaydi, shuning uchun boshqa cheklar ochilmaydi.
    /// Yaratilganda bir marta beriladi va hech qachon o'zgarmaydi (payments jadvaliga UPDATE yo'q).
    /// </summary>
    public string ReceiptToken { get; set; } = Guid.NewGuid().ToString("N");
}

/// <summary>
/// To'lov usuli. <b>Mijoz javobi (SPEC §8.1, Q13): bu FAQAT YORLIQ.</b> Hech
/// qanday to'lov provayderi (Payme/Click/Uzum/terminal) bilan integratsiya YO'Q —
/// na hozir, na keyingi fazalarda. Kassir to'lovchi nima bilan to'laganini
/// belgilaydi, tizim hech kimga so'rov yubormaydi.
///
/// <para>
/// Smena yopilishida <b>faqat <see cref="Cash"/></b> <c>expected_cash</c> ga
/// kiradi — qolgan uchtasi bankka tushadi, ularni sanash har smenada soxta
/// nomuvofiqlik (variance) berardi.
/// </para>
/// </summary>
public static class PaymentMethod
{
    /// <summary>Naqd pul — kassada sanaladi.</summary>
    public const string Cash = "cash";
    /// <summary>Karta terminali.</summary>
    public const string Card = "card";
    /// <summary>Bank o'tkazmasi (pul ko'chirish).</summary>
    public const string Transfer = "transfer";
    /// <summary>Onlayn to'lov (Payme/Click/Uzum) — kassir qo'lda belgilaydi.</summary>
    public const string Online = "online";

    public static readonly IReadOnlyList<string> All = [Cash, Card, Transfer, Online];

    /// <summary>Smena yopilishida sanaladigan (naqd) usulmi?</summary>
    public static bool CountsAsCash(string method) => method == Cash;
}

/// <summary>
/// ASOSIY talab: bitta to'lovni bir necha toifaga (o'qish + avtobus + ovqat)
/// taqsimlash. <b>O'ZGARMAS</b> — faqat INSERT.
///
/// <para>
/// Invariant "taqsimot yig'indisi to'lov summasidan oshmasin" ILOVA KODIDA EMAS,
/// bazadagi trigger'da (<c>check_allocation_total()</c>, SPEC §3.7) — ya'ni
/// xatolik yoki chetlab o'tish urinishi ham uni buza olmaydi.
/// </para>
/// </summary>
public class PaymentAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public Guid InvoiceId { get; set; }
    /// <summary>Shu hisob-fakturaga yo'naltirilgan qism (&gt; 0).</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// Kassir smenasi (SPEC §4.2). To'lov qabul qilish uchun ochiq smena SHART.
/// Bir kassirda bir vaqtda faqat bitta ochiq smena bo'ladi — buni bazadagi
/// shartli unikal indeks kafolatlaydi (<c>where status = 'open'</c>).
/// </summary>
public class CashShift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CashierId { get; set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>Smena boshidagi kassadagi pul (sukut 0 — docs/ASSUMPTIONS.md).</summary>
    public decimal OpeningFloat { get; set; }
    /// <summary>Ledger'dan hisoblangan kutilgan naqd. Yopilmaguncha null.</summary>
    public decimal? ExpectedCash { get; set; }
    /// <summary>Kassir QO'LDA sanab kiritgan naqd. Yopilmaguncha null.</summary>
    public decimal? CountedCash { get; set; }
    /// <summary>
    /// Nomuvofiqlik = CountedCash − ExpectedCash. <b>Bazada generated column</b>
    /// (<c>generated always as ... stored</c>) — ilova uni YOZA OLMAYDI, ya'ni
    /// yopilgandan keyin "tuzatib" qo'yib bo'lmaydi (SPEC §4.2).
    /// </summary>
    public decimal? Variance { get; private set; }
    /// <summary>open | closed — <see cref="CashShiftStatus"/>.</summary>
    public string Status { get; set; } = CashShiftStatus.Open;
    /// <summary>Yopgan foydalanuvchi (users.id). Odatda kassirning o'zi.</summary>
    public string? ClosedBy { get; set; }
}

/// <summary>Smena holatlari (<see cref="CashShift.Status"/>).</summary>
public static class CashShiftStatus
{
    public const string Open = "open";
    public const string Closed = "closed";

    public static readonly IReadOnlyList<string> All = [Open, Closed];
}

/// <summary>
/// Chiqim (maosh, kommunal, ta'mir, ...). Ikki qavatli nazorat: yaratgan odam
/// o'zi tasdiqlay olmaydi — baza <c>check (approved_by is null or approved_by
/// &lt;&gt; created_by)</c> bilan bloklaydi (SPEC §4.5).
/// </summary>
public class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly OnDate { get; set; }
    /// <summary>Erkin toifa: salary | utilities | supplies | rent | other ...</summary>
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    /// <summary>
    /// Maosh kimga berilgani (<c>teachers.id</c>). <c>salary</c> dan boshqa
    /// toifalarda <c>null</c> (P1-21).
    ///
    /// <para>
    /// Nega ustun kerak: maosh hisoboti, o'qituvchi kartochkasi va maosh
    /// jadvali bitta savolga javob beradi — "falonchi falon oyda qancha oldi".
    /// Eski <c>finance_transactions.teacher_id</c> shu bog'lanishni berardi;
    /// usiz javob faqat izoh matnini o'qish bilan topilardi, bu esa
    /// bog'lanish emas, taxmin. Batafsil: <c>SalaryPaymentQuery</c>.
    /// </para>
    /// </summary>
    public string? TeacherId { get; set; }

    /// <summary>
    /// Naqd chiqim qaysi kassa smenasidan to'landi (F1.03, finance-parity
    /// §3.1 A1). <c>null</c> = smenaga bog'lanmagan chiqim.
    ///
    /// <para>
    /// <b>Nega bu ustun kerak.</b> Smenaning kutilgan naqdi jurnal (ledger)
    /// dan hisoblanadi, chiqim esa smenaga UMUMAN bog'lanmagan edi — ya'ni
    /// kassadan naqd chiqib ketardi, kutilgan naqd esa o'zgarmasdi va smena
    /// AYNAN o'sha summaga KAM pul bilan yopilardi. Hisobotda bu "kassir
    /// yetishmovchiligi" bo'lib ko'rinardi. Bu defekt F1.03.
    /// </para>
    ///
    /// <para>
    /// <b>Nega null bo'la oladi.</b> `expenses` jonli jadval: bugungacha
    /// yozilgan har bir chiqimda smena YO'Q va ular yaroqli bo'lib qolishi
    /// kerak. Eski qatorlarga smena TAXMIN QILINMAYDI — "qaysidir smena
    /// bo'lgandir" degan taxmin kutilgan naqdni orqaga qarab buzardi.
    /// Yangi naqd chiqimda smenani XIZMAT talab qiladi (S2), baza emas.
    /// </para>
    /// </summary>
    public Guid? CashShiftId { get; set; }

    /// <summary>
    /// Naqd chiqim qaysi KASSADAN to'landi ("smena" o'rnini bosuvchi model,
    /// 2026-09). <c>null</c> = bankdan chiqqan yoki eski (kassa modelidan
    /// oldingi) qator — TAXMIN QILINMAYDI.
    /// </summary>
    public Guid? CashBoxId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Ikki yoqlama yozuv jurnali — moliyaviy haqiqatning YAGONA manbai.
/// <b>Faqat qo'shiladi</b> (append-only): <c>app_rw</c> da UPDATE/DELETE yo'q.
///
/// <para>
/// Bu jadvalga faqat <c>LedgerService</c> yozadi (P1-07) — umumiy repozitoriy
/// orqali emas (SPEC §2.2). Har yozuv juft bo'ladi: debet yig'indisi kredit
/// yig'indisiga TENG bo'lmasa, xizmat <c>SaveChanges</c> gacha xato beradi.
/// </para>
/// </summary>
public class LedgerEntry
{
    /// <summary>bigint identity — tartib raqami vaqt bo'yicha o'sib boradi.</summary>
    public long Id { get; set; }
    /// <summary>Buxgalteriya sanasi (to'lov qabul qilingan kun).</summary>
    public DateOnly EntryDate { get; set; }
    /// <summary>Hisob kodi — yopiq ro'yxat, <c>Accounts.cs</c> (P1-07).</summary>
    public string Account { get; set; } = string.Empty;
    /// <summary>debit | credit — <see cref="LedgerDirection"/>.</summary>
    public string Direction { get; set; } = string.Empty;
    /// <summary>Summa (har doim &gt; 0; yo'nalishni <see cref="Direction"/> beradi).</summary>
    public decimal Amount { get; set; }
    /// <summary>payment | invoice | expense | salary | reversal — <see cref="LedgerRefType"/>.</summary>
    public string RefType { get; set; } = string.Empty;
    /// <summary>Manba yozuv id'si (to'lov/hisob-faktura/chiqim). null = qo'lda tuzatish.</summary>
    public Guid? RefId { get; set; }
    public string? Memo { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Storno: qaysi yozuvni teskari qilmoqda. Original TEGILMAYDI.</summary>
    public long? ReversalOf { get; set; }
}

/// <summary>Ledger yozuvi yo'nalishi (<see cref="LedgerEntry.Direction"/>).</summary>
public static class LedgerDirection
{
    public const string Debit = "debit";
    public const string Credit = "credit";

    public static readonly IReadOnlyList<string> All = [Debit, Credit];
}

/// <summary>
/// Ledger yozuvi manbai (<see cref="LedgerEntry.RefType"/>).
///
/// <para>
/// <b>Bazada CHECK YO'Q — ataylab.</b> <c>ledger_entries.ref_type</c> ustuni
/// hech qanday constraint bilan cheklanmagan (<c>BillingModel.ConfigureLedger</c>
/// da faqat <c>amount</c>, <c>direction</c> va <c>reversal_not_self</c> bor).
/// Ya'ni bu ro'yxat KOD kelishuvi: jadvalga faqat <c>LedgerService</c> yozadi
/// (SPEC §2.2), va yangi manba turi qo'shish migratsiya talab qilmaydi.
/// Shu sabab finance-parity §3.1 A5 "code only, no DDL" deb belgilangan.
/// </para>
/// </summary>
public static class LedgerRefType
{
    public const string Payment = "payment";
    public const string Invoice = "invoice";
    public const string Expense = "expense";
    public const string Salary = "salary";
    public const string Reversal = "reversal";

    /// <summary>
    /// Kassadan pul chiqishi — <see cref="CashHandover"/> (F1.04).
    /// Hisobotlarda hozircha "Boshqa harakat" yorlig'i ostida ko'rinadi;
    /// nomlarni S4 (<c>CashDayQueries</c>) qo'shadi — finance-parity §4.
    /// </summary>
    public const string CashHandover = "cash_handover";

    /// <summary>O'quvchiga pul qaytarish — <see cref="StudentRefund"/> (F1.05).</summary>
    public const string Refund = "refund";

    public static readonly IReadOnlyList<string> All =
        [Payment, Invoice, Expense, Salary, Reversal, CashHandover, Refund];
}

/// <summary>
/// Moliya sozlamalari — BITTA qator (<see cref="SingletonId"/>).
///
/// <para>
/// <b>Nega alohida jadval, <c>SchoolMeta</c> ga ustun qo'shilmadi?</b> Ikki sabab:
/// (1) <c>SchoolMeta</c> <c>Entities.cs</c> ichida, unga tegish P1-04 qabul
/// mezonini buzardi ("Entities.cs diff'i bo'sh"); (2) <c>SchoolMeta</c> allaqachon
/// 40+ ustunli "hamma narsa" jadvali — Telegram tokeni, turniket paroli, GPS
/// sozlamalari. Moliya sozlamasini o'sha uyumga qo'shish P1-22 dagi "moliya
/// jadvallariga kim yozadi" savolini chalkashtirardi.
/// </para>
///
/// <para>
/// <b>Mijoz javobi (SPEC §8.1, Q6): to'lov muddati qat'iy raqam emas, sozlanadigan.</b>
/// Ikkalasini ham admin UI'dan o'zgartiradi — bu <c>UPDATE</c>, hech qachon
/// migratsiya emas.
/// </para>
/// </summary>
public class BillingSettings
{
    /// <summary>Yagona qator id'si — barqaror (migratsiyada seed qilinadi).</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-0000000000b1");

    public Guid Id { get; set; } = SingletonId;

    /// <summary>
    /// Hisob-faktura to'lov muddati — oyning shu kuni (1..28). Sukut: 10.
    /// Accrual job <c>due_on</c> ni shundan hisoblaydi.
    /// </summary>
    public int PaymentDueDay { get; set; } = 10;

    /// <summary>
    /// Shu kundan KEYIN to'lanmagan hisob-faktura "muddati o'tgan" (overdue)
    /// hisoblanadi. Sukut: 15. <see cref="PaymentDueDay"/> dan kichik bo'lmasligi
    /// kerak — buni baza check constraint bilan kafolatlaydi.
    /// </summary>
    public int OverdueAfterDay { get; set; } = 15;

    /// <summary>
    /// Ikki qavatli nazorat chegarasi (SPEC §4.5): <b>shu summadan KATTA</b>
    /// chiqim ikkinchi, boshqa shaxsning tasdig'isiz jurnalga tushmaydi.
    /// Sukut: 5 000 000 so'm (mijoz javobi, docs/TASKS.md §8 Q16).
    ///
    /// <para>
    /// Chegara sozlama, konstanta emas: maktabning "katta pul" tushunchasi
    /// yiliga bir marta o'zgaradi va bu <c>UPDATE</c> bo'lishi kerak,
    /// migratsiya emas — xuddi <see cref="PaymentDueDay"/> kabi.
    /// </para>
    /// <para>
    /// <c>approved_by &lt;&gt; created_by</c> qoidasi esa sozlama EMAS: u
    /// <c>expenses</c> jadvalidagi check constraint
    /// (<c>ck_expenses_approver_differs</c>), ya'ni chegarani nolga tushirib
    /// ham o'z-o'zini tasdiqlab bo'lmaydi.
    /// </para>
    /// </summary>
    public decimal ExpenseApprovalThreshold { get; set; } = 5_000_000m;

    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Oxirgi marta kim o'zgartirgan (users.id). Seed'dan keyin null.</summary>
    public string? UpdatedBy { get; set; }
}

// ===========================================================================
//  Tungi tekshiruv bayroqlari — SPEC §4.6. Vazifa: P1-14.
// ===========================================================================
//
//  NEGA ENTITY SHU YERDA, `Application/Billing/Anomaly.cs` DA EMAS
//  ---------------------------------------------------------------
//  Repozitoriyada BARCHA EF entity'lari Domain qatlamida yashaydi va holat
//  konstantalari (`InvoiceStatus`, `PaymentMethod`, `CashShiftStatus`) o'z
//  entity'sining YONIDA turadi. Bayroq ham xuddi shunday: jadval, uning
//  holatlari va turlari bitta joyda. `Anomaly.cs` esa DTO'lar, sozlamalar va
//  xizmat shartnomasini saqlaydi — u yerda EF'ga bog'liq narsa yo'q.
//
//  O'CHIRIB BO'LMAYDI
//  ------------------
//  Bayroqni faqat SABAB YOZIB yopish mumkin (SPEC §4.6: "cannot be dismissed,
//  only resolved with a written reason"). Shuning uchun:
//    * DELETE endpoint YO'Q va bo'lmaydi;
//    * `app_rw` rolida DELETE/TRUNCATE yo'q, UPDATE esa FAQAT uchta ustunga
//      (`resolved_at`, `resolved_by`, `resolved_reason`) — ustun darajasidagi
//      GRANT, `Migrations/Sql/anomaly_guards.sql`;
//    * bo'sh sabab bazadagi check constraint bilan ham bloklanadi.

/// <summary>
/// Tungi tekshiruv topgan shubhali hodisa (SPEC §4.6). Bir hodisa = bir qator;
/// takroriy tekshiruv dublikat YARATMAYDI — buni <c>(kind, ref_id)</c> unikal
/// indeksi kafolatlaydi.
/// </summary>
public class FinanceAnomalyFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>To'rt shartdan qaysi biri — <see cref="AnomalyKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Manba jadval turi — <see cref="AnomalyRefType"/> (o'qishda qulaylik uchun).</summary>
    public string RefType { get; set; } = string.Empty;

    /// <summary>
    /// Manba yozuv id'si: smena, to'lov yoki hisob-faktura. FK QO'YILMAGAN —
    /// bitta ustun uchta jadvalga ishora qiladi; buning o'rniga
    /// <see cref="RefType"/> + <c>(kind, ref_id)</c> unikal indeksi ishlaydi.
    /// </summary>
    public Guid RefId { get; set; }

    /// <summary>Hodisaning O'ZI qachon bo'lgan (smena yopilgan / to'lov qabul qilingan lahza).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Tekshiruv uni qachon topgan.</summary>
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>
    /// Hodisaning pul o'lchami: nomuvofiqlik summasi, storno summasi va h.k.
    /// Direktor panelidagi "yopilmagan nomuvofiqlik" hisoblagichi shundan yig'iladi.
    /// null = pul o'lchami yo'q (masalan taqsimotsiz "to'langan" hisob-faktura).
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>O'zbekcha, o'qiladigan izoh — panelda shu ko'rinadi.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Raqamlar va id'lar (jsonb). Tekshiruvni qayta hisoblash uchun.</summary>
    public string? Details { get; set; }

    /// <summary>Yopilgan lahza. null = hali ochiq (panel hisoblagichida turadi).</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Kim yopgan (users.id) — JWT'dan, so'rov tanasidan EMAS (SPEC §4.4).</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>
    /// YOZMA SABAB — majburiy (SPEC §4.6). Bo'sh yoki faqat probel bo'lishi
    /// mumkin emas: ilova 400 qaytaradi, baza esa check constraint bilan
    /// bloklaydi.
    /// </summary>
    public string? ResolvedReason { get; set; }
}

/// <summary>SPEC §4.6 dagi to'rtta shart (<see cref="FinanceAnomalyFlag.Kind"/>).</summary>
public static class AnomalyKind
{
    /// <summary>Smena nolga teng bo'lmagan nomuvofiqlik bilan yopilgan.</summary>
    public const string ShiftVariance = "shift_variance";

    /// <summary>Storno original to'lovdan keyin 24 soat ichida qilingan.</summary>
    public const string FastReversal = "fast_reversal";

    /// <summary>To'lov kassirning odatdagi ish soatlaridan tashqarida qabul qilingan.</summary>
    public const string OffHoursPayment = "off_hours_payment";

    /// <summary>Hisob-faktura "to'langan", lekin unga birorta taqsimot yo'q.</summary>
    public const string PaidWithoutAllocation = "paid_without_allocation";

    /// <summary>
    /// BESHINCHI shart (docs/modules/existing-module-gaps.md §3.5): ota-ona
    /// va'da qilgan to'lov sanasi o'tib ketdi, qarz esa hali ochiq.
    ///
    /// <para>
    /// <b>Bu bayroq JADVALGA YOZILMAYDI</b> — u har so'rovda hisoblanadi
    /// (<c>BrokenPromiseScan</c>). Ikki sabab: (1) buzilgan va'da HODISA emas,
    /// HOLAT — ota-ona ertaga to'lasa, shart o'z-o'zidan yo'qoladi va qator
    /// esa qolib, haqiqatdan ajralib ketardi (`students.balance`, P1-21);
    /// (2) <c>ck_finance_anomaly_flags_kind</c> check constraint'i faqat
    /// yuqoridagi to'rttasiga ruxsat beradi, ya'ni INSERT 23514 bilan
    /// yiqilardi. Shu kod bilan qator yozmoqchi bo'lsangiz — avval migratsiya
    /// kerak (constraint + <see cref="AnomalyRefType"/> ro'yxati).
    /// </para>
    /// </summary>
    public const string BrokenPromise = "broken_promise";

    public static readonly IReadOnlyList<string> All =
        [ShiftVariance, FastReversal, OffHoursPayment, PaidWithoutAllocation, BrokenPromise];
}

/// <summary>Bayroq qaysi jadvalga ishora qilyapti (<see cref="FinanceAnomalyFlag.RefType"/>).</summary>
public static class AnomalyRefType
{
    public const string CashShift = "cash_shift";
    public const string Payment = "payment";
    public const string Invoice = "invoice";

    /// <summary>
    /// <c>debtor_actions</c> qatori — FAQAT hisoblanadigan
    /// <see cref="AnomalyKind.BrokenPromise"/> bayrog'ida ishlatiladi.
    /// <c>ck_finance_anomaly_flags_ref_type</c> bu qiymatni QABUL QILMAYDI:
    /// bunday bayroq bazaga yozilmaydi va yozilmasligi kerak.
    /// </summary>
    public const string DebtorAction = "debtor_action";

    public static readonly IReadOnlyList<string> All = [CashShift, Payment, Invoice, DebtorAction];
}
