namespace SchoolLms.Domain;

// ===========================================================================
//  Kassa stoli — moliya pariteti, A to'plami.
//  Manba: docs/modules/finance-parity.md §3.1 (A2, A3, A4) va §3.4.
// ===========================================================================
//
//  NEGA ALOHIDA FAYL
//  -----------------
//  `Billing.cs` dagi sabab bilan bir xil: `Entities.cs` repozitoriyadagi eng
//  ko'p konflikt beradigan fayl, `Billing.cs` esa shu to'lqinda faqat IKKI
//  qator qo'shimcha oladi (A1 va A5). Uchta yangi jadval o'z uyida yashaydi,
//  ya'ni keyingi slice'lar (S2 — kassa, S3 — qaytarim) bir-birining diff'ini
//  bosmaydi.
//
//  TIPLAR
//  ------
//  `Billing.cs` bilan aynan bir xil: id — `uuid`, pul — `numeric(14,2)`,
//  vaqt belgisi — `timestamptz` (`DateTimeOffset`, hech qachon `DateTime`),
//  `students.id` va `app_users.id` ga havolalar esa `text` bo'lib qoladi.
//
//  O'ZGARMASLIK (SPEC §4.1)
//  ------------------------
//  Uchala jadval ham pul yo'liga tegadi, shuning uchun `app_rw` da ularda
//  UPDATE/DELETE yo'q — `Migrations/Sql/finance_parity_guards.sql`:
//
//    * `cash_handovers`    — FAQAT qo'shiladi. Xato topshiriq `reversal_of`
//                            bilan qarshi yozuv qo'shib tuzatiladi, xuddi
//                            `payments` dagidek.
//    * `student_refunds`   — FAQAT qo'shiladi, BITTA istisno bilan: qaror
//                            (tasdiq yoki rad) to'rtta ustunga yoziladi va
//                            ular ustun darajasidagi GRANT bilan ochiq
//                            (`anomaly_guards.sql` naqshi). Qaror bir marta
//                            qo'yilgach, uni BAZADAGI TRIGGER qulflaydi.
//    * `expense_attachments` — chiqimning DALILI (chek surati, shartnoma
//                            nusxasi). Uni o'chirish yoki almashtirish —
//                            SPEC §4 tasvirlagan firibgarlikning aynan
//                            o'zi, shuning uchun SELECT va INSERT, boshqa
//                            hech narsa.

/// <summary>
/// Kassadan pul chiqishi (F1.04): bankka topshirish yoki direktorning
/// seyfiga berish. Smenaning kutilgan naqdini KAMAYTIRADI — usiz Kassa
/// kunining yopilish qoldig'i har kuni yuqoriga suriladi va bir oydan keyin
/// "nomuvofiqlik" hisoboti ma'nosini yo'qotadi.
///
/// <para>
/// <b>FAQAT QO'SHILADI</b> (SPEC §4.1): <c>app_rw</c> da UPDATE va DELETE
/// yo'q. Xato topshiriq <see cref="ReversalOf"/> bilan qarshi qator qo'shib
/// tuzatiladi.
/// </para>
/// </summary>
public class CashHandover
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Qaysi smenadan chiqdi. Smena o'chirilmaydi (RESTRICT).</summary>
    public Guid CashShiftId { get; set; }

    /// <summary>Summa (har doim &gt; 0; storno ham musbat — uni <see cref="ReversalOf"/> belgilaydi).</summary>
    public decimal Amount { get; set; }

    /// <summary>bank | safe — <see cref="CashHandoverDestination"/>.</summary>
    public string Destination { get; set; } = CashHandoverDestination.Bank;

    public string? Note { get; set; }

    /// <summary>Kim topshirdi (users.id) — JWT'dan, so'rov tanasidan EMAS (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Storno: qaysi topshiriqni bekor qilmoqda. null = oddiy topshiriq.</summary>
    public Guid? ReversalOf { get; set; }
}

/// <summary>
/// Pul qayerga ketdi (<see cref="CashHandover.Destination"/>) — finance-parity
/// §5 Q1 ning ikkita javobi.
/// </summary>
public static class CashHandoverDestination
{
    /// <summary>Bankka topshirildi: pul <c>cash</c> dan <c>bank</c> ga ko'chadi.</summary>
    public const string Bank = "bank";

    /// <summary>
    /// Direktorning seyfiga berildi: smenaning kutilgan naqdi kamayadi,
    /// lekin pul MAKTABDA qoladi — ya'ni bu hisobot uchun boshqa hodisa.
    /// </summary>
    public const string Safe = "safe";

    public static readonly IReadOnlyList<string> All = [Bank, Safe];
}

/// <summary>
/// O'quvchiga pul qaytarish (F1.05) — MAKTABDAN PUL CHIQADI, shuning uchun
/// <c>payments</c> bilan bir sinf himoya: ikki qavatli nazorat (SPEC §4.5),
/// faqat INSERT, va qaror qo'yilgach qulflanadigan qator.
///
/// <para>
/// <b>Ikki qavatli nazorat.</b> So'rovchi o'zi tasdiqlay olmaydi — bu ilova
/// tekshiruvi EMAS, baza constraint'i
/// (<c>ck_student_refunds_approver_differs</c>). Naqd qaytarim esa ochiq
/// smenasiz tasdiqlanmaydi (<c>ck_student_refunds_cash_shift</c>): pul
/// kassadan chiqadi, ya'ni qaysidir smenaning kutilgan naqdini kamaytirishi
/// shart.
/// </para>
///
/// <para>
/// <b>Qaror QAYTARILMAYDI.</b> <c>app_rw</c> da jadval darajasida UPDATE
/// yo'q; pastdagi to'rtta "qaror" ustuni ustun darajasidagi GRANT bilan
/// ochiq (<c>anomaly_guards.sql</c> naqshi). Va o'sha to'rttasi ham FAQAT
/// BIR MARTA yoziladi: <c>approved_at</c> yoki <c>rejected_reason</c>
/// to'lgach, <c>student_refunds_locked</c> trigger'i har qanday keyingi
/// UPDATE ni rad etadi. Ya'ni "tasdiqladim, keyin smenani almashtirdim"
/// degan yo'l yopiq. Xato qaytarim <see cref="ReversalOf"/> bilan
/// tuzatiladi.
/// </para>
/// </summary>
public class StudentRefund
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Kimga qaytariladi (students.id — `text`). RESTRICT: pul tarixi o'quvchi bilan ketmaydi.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Summa (har doim &gt; 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>cash | card | transfer | online — <see cref="PaymentMethod"/> bilan bir xil ro'yxat.</summary>
    public string Method { get; set; } = PaymentMethod.Cash;

    /// <summary>Sabab — MAJBURIY va bo'sh bo'la olmaydi (baza CHECK bilan bloklaydi).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Kim so'radi (users.id) — JWT'dan (SPEC §4.4).</summary>
    public string RequestedBy { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Kim tasdiqladi (users.id). null = hali qaror yo'q.</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>Tasdiqlangan lahza. To'lgach qator QULFLANADI (trigger).</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>
    /// Naqd qaytarim qaysi smenadan chiqdi. Naqd bo'lmasa (karta, o'tkazma)
    /// null bo'lishi mumkin; naqd tasdiqlanganda esa SHART.
    /// </summary>
    public Guid? CashShiftId { get; set; }

    /// <summary>Rad etish sababi. To'lgach qator QULFLANADI (trigger).</summary>
    public string? RejectedReason { get; set; }

    /// <summary>Storno: qaysi qaytarimni bekor qilmoqda. null = oddiy qaytarim.</summary>
    public Guid? ReversalOf { get; set; }
}

/// <summary>
/// Chiqimga biriktirilgan hujjat (F1.08): chek surati, shartnoma nusxasi,
/// hisob-faktura skani. Pul yozuvining DALILI.
///
/// <para>
/// <b>FAQAT SELECT va INSERT.</b> Dalilni almashtirish yoki o'chirish —
/// summani o'zgartirish bilan bir xil og'irlikdagi harakat: chiqim qatori
/// joyida qoladi, lekin uni tekshirib bo'lmaydigan bo'lib qoladi. Noto'g'ri
/// fayl yuklansa, to'g'risi YANGI qator bo'lib qo'shiladi va ilova oxirgisini
/// ko'rsatadi.
/// </para>
/// </summary>
public class ExpenseAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Qaysi chiqimga. RESTRICT: dalili bor chiqim o'chirilmaydi.</summary>
    public Guid ExpenseId { get; set; }

    /// <summary>Saqlangan faylga yo'l (yuklash xizmati beradi — S2).</summary>
    public string FileUrl { get; set; } = string.Empty;

    /// <summary>Foydalanuvchi ko'radigan asl nom.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME turi (image/jpeg, application/pdf, ...).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Hajm baytlarda (&gt; 0). Nol baytli "dalil" dalil emas.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Kim yukladi (users.id) — JWT'dan (SPEC §4.4).</summary>
    public string UploadedBy { get; set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; set; }
}
