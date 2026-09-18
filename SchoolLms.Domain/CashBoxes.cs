namespace SchoolLms.Domain;

// ===========================================================================
//  KASSALAR (cash boxes) — "smena" tushunchasini almashtiradi.
//  Mijoz javobi (2026-09-1x): "bizni tizimda smena degan tushuncha umuman
//  bo'lmasin butunlay olib tashla, shunchaki kassa degan narsa bo'lsin xolos,
//  bizda bir nechta kassa bo'lishi mumkin, ular har bir alohida pul kirim
//  chiqim qilishi va o'zaro o'tkazma qilishi mumkin."
// ===========================================================================
//
//  NEGA `cash_shifts` O'CHIRILMADI
//  -------------------------------
//  `cash_shifts` haqiqiy tarixni saqlaydi va SPEC §4.1 pul yozuvini o'chirishni
//  taqiqlaydi. Bu migratsiya uni FAQAT "majburiy" bo'lishdan to'xtatadi — jadval,
//  ustunlari va qatorlari joyida qoladi (`CashShiftService`/`CashHandoverService`
//  hamon kompilyatsiya qilinadi, faqat endi `PaymentService`/`ExpenseService`
//  yo'lida CHAQIRILMAYDI).
//
//  IKKI JADVAL — NEGA
//  -------------------
//  `cash_boxes`             — kataloq: nom, mas'ul, sukut belgisi, faollik.
//                              ODDIY jadval (rename/deactivate — UPDATE bor).
//  `cash_box_transactions`  — pul harakati jurnali. `payments`/`ledger_entries`
//                              bilan bir xil qoidaga bo'ysunadi: FAQAT QO'SHILADI.
//                              Bekor qilingan amal — STORNO QATORI
//                              (`ReversalOf`), hech qachon UPDATE/DELETE emas.
//
//  BALANS — HECH QACHON SAQLANMAYDI
//  ---------------------------------
//  SPEC §4.1: "no stored balance". Har bir kassaning joriy qoldig'i va usul
//  kesimi (`byMethod`) HAR SAFAR `cash_box_transactions` dan hisoblanadi
//  (`CashBoxService.BalanceAsync`) — bu faylda "balance" ustuni YO'Q va
//  bo'lmaydi.
//
//  TO'RTTA AMAL — BITTA QATOR YOKI IKKITA QATORMI
//  ------------------------------------------------
//  * `pay_in` / `pay_out`  — BITTA qator: pul shu kassaning shu usuliga
//    kirdi/undan chiqdi.
//  * `transfer`            — BITTA qator: `CashBoxId` — pul QAYERDAN chiqdi
//    (manba), `TransferToBoxId` — pul QAYERGA tushdi (manzil). Ikkinchi qator
//    KERAK EMAS: manba kassaning qoldig'ini hisoblaganda bu qator AYIRILADI,
//    manzil kassaning qoldig'ini hisoblaganda esa (`TransferToBoxId` orqali
//    topilib) QO'SHILADI — bitta yozuv ikkala tomonni ham qamrab oladi, ya'ni
//    "ikkalasi ham bo'ladi yoki hech biri bo'lmaydi" avtomatik bajariladi
//    (bitta INSERT, bitta tranzaksiya).
//  * `exchange`            — BITTA qator: `Method` — qaysi usuldan chiqdi,
//    `ToMethod` — qaysi usulga kirdi. Kassaning UMUMIY qoldig'i o'zgarmaydi
//    (`Method` bo'yicha -amount, `ToMethod` bo'yicha +amount), faqat usul
//    kesimi o'zgaradi. `TransferToBoxId` MISOLI naqshi: bitta qator ikkala
//    "tomonni" ifodalaydi.
//
//  STORNO — QANDAY ISHLAYDI
//  -------------------------
//  Bekor qilish YANGI qator qo'shadi: xuddi shu `Kind`/`Amount`/`Method`
//  (va `TransferToBoxId`/`ToMethod`, bo'lsa), lekin `ReversalOf` originalga
//  ishora qiladi va `Status = Reversal`. Balans hisoblovchi so'rov bu qatorni
//  originalning TESKARISI sifatida qo'shadi (`CashBoxService` dagi izoh) —
//  ya'ni bitta amalni ikki marta, lekin qarama-qarshi ishora bilan hisoblaydi.
//  Original qator TEGILMAYDI.

/// <summary>
/// Kassa (till) — smena o'rnini bosadi. Bir nechta kassa bo'lishi mumkin,
/// har birining o'z qoldig'i (hisoblanadi, saqlanmaydi), mas'uli va sukut
/// (default) belgisi bor.
/// </summary>
public class CashBox
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ko'rsatiladigan nom ("Asosiy kassa", "Filial-2 kassasi", ...).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Mas'ul xodim (users.id — `text`). null = mas'ul tayinlanmagan.</summary>
    public string? ResponsibleUserId { get; set; }

    /// <summary>
    /// Sukut (default) kassa — <c>cashBoxId</c> ko'rsatilmagan to'lov/chiqim
    /// shu yerga tushadi. AYNAN BITTASI true bo'lishi SHART — bazada qisman
    /// unikal indeks (<c>ux_cash_boxes_one_default</c>) kafolatlaydi.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>false = yangi amal qabul qilmaydi (tarix qoladi, o'chirilmaydi).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Kassa harakati — jurnal yozuvi. <b>FAQAT QO'SHILADI</b> (SPEC §4.1):
/// <c>app_rw</c> da bu jadvalga UPDATE/DELETE yo'q
/// (<c>Migrations/Sql/cash_boxes_guards.sql</c>). Xato amal
/// <see cref="ReversalOf"/> bilan QARSHI QATOR qo'shib tuzatiladi.
/// </summary>
public class CashBoxTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Qaysi kassa. <c>transfer</c> da — pul QAYERDAN chiqqan (manba);
    /// manzil <see cref="TransferToBoxId"/> da.
    /// </summary>
    public Guid CashBoxId { get; set; }

    /// <summary>pay_in | pay_out | transfer | exchange — <see cref="CashBoxTransactionKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Summa (har doim &gt; 0; storno ham musbat — uni <see cref="ReversalOf"/> belgilaydi).</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// cash | card | transfer | online (<see cref="PaymentMethod"/> bilan bir xil
    /// ro'yxat). <c>exchange</c> da — pul QAYERDAN chiqqan usul.
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// FAQAT <c>exchange</c> da to'ladi: pul QAYSI usulga kirgan.
    /// Boshqa turlarda <c>null</c> (bazada <c>ck_cash_box_transactions_exchange_shape</c>
    /// shuni talab qiladi).
    /// </summary>
    public string? ToMethod { get; set; }

    /// <summary>
    /// FAQAT <c>pay_in</c> da ixtiyoriy: pul kimdan kelgani (students.id — `text`).
    /// Boshqa turlarda odatda <c>null</c>.
    /// </summary>
    public string? StudentId { get; set; }

    /// <summary>Izoh yoki kontragent (erkin matn). Storno qatorida — bekor qilish sababi.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// posted | reversal — <see cref="CashBoxTransactionStatus"/>. INSERT paytida
    /// bir marta yoziladi va KEYIN o'zgarmaydi (jadval o'zgarmas). "Bekor
    /// qilindi" degan HOLAT bu ustunda emas — u <see cref="ReversalOf"/>
    /// orqali boshqa qatordan qidiriladi va DTO darajasida hisoblanadi
    /// (<c>CashBoxService.DisplayStatus</c>), xuddi <c>ExpenseStatus</c> kabi.
    /// </summary>
    public string Status { get; set; } = CashBoxTransactionStatus.Posted;

    /// <summary>Kim yozgan (users.id) — JWT'dan, so'rov tanasidan EMAS (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// FAQAT <c>transfer</c> da to'ladi: pul QAYSI kassaga tushgan (manzil).
    /// Boshqa turlarda <c>null</c>.
    /// </summary>
    public Guid? TransferToBoxId { get; set; }

    /// <summary>Storno: qaysi amalni bekor qilmoqda. null = oddiy amal.</summary>
    public Guid? ReversalOf { get; set; }
}

/// <summary>Kassa amali turi (<see cref="CashBoxTransaction.Kind"/>).</summary>
public static class CashBoxTransactionKind
{
    /// <summary>Kirim — pul kassaga kirdi.</summary>
    public const string PayIn = "pay_in";

    /// <summary>Chiqim — pul kassadan chiqdi.</summary>
    public const string PayOut = "pay_out";

    /// <summary>Ko'chirish — bitta kassadan ikkinchisiga.</summary>
    public const string Transfer = "transfer";

    /// <summary>Ayirboshlash — bitta kassa ichida usuldan usulga (jami o'zgarmaydi).</summary>
    public const string Exchange = "exchange";

    public static readonly IReadOnlyList<string> All = [PayIn, PayOut, Transfer, Exchange];
}

/// <summary>
/// Qator holati — bazadagi qiymat, INSERT'da qotadi
/// (<see cref="CashBoxTransaction.Status"/>). Ko'rsatiladigan holat
/// (<c>posted</c> | <c>cancelled</c> | <c>reversal</c>) bundan farqli —
/// u <c>ReversalOf</c> havolasi mavjudligidan hisoblanadi.
/// </summary>
public static class CashBoxTransactionStatus
{
    /// <summary>Oddiy amal.</summary>
    public const string Posted = "posted";

    /// <summary>Bu qatorning O'ZI — boshqa birining stornosi.</summary>
    public const string Reversal = "reversal";

    public static readonly IReadOnlyList<string> All = [Posted, Reversal];
}
