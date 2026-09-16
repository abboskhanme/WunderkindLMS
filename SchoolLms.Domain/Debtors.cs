namespace SchoolLms.Domain;

// ===========================================================================
//  Qarzdorlar bilan ISHLASH — docs/modules/existing-module-gaps.md §3.5.
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  Bugungacha qarzdorlar bo'limi RO'YXAT edi: kim qancha qarz. "Bu qarz
//  bo'yicha nima qilindi?" degan savolga javob hech qayerda saqlanmasdi —
//  u administratorning daftarida yoki xotirasida qolardi. §3.5 shuni
//  "yig'ish CRM'i" deb ataydi va uchta narsani talab qiladi: rangli HOLAT,
//  izohli AMALLAR TARIXI va ota-ona bilan kelishilgan YANGI TO'LOV SANASI.
//
//  IKKI JADVAL, ISH OQIMI DVIGATELI EMAS (§3.5 "Build a thin version")
//  ------------------------------------------------------------------
//  `debtor_statuses` — maktab o'zi tuzadigan ma'lumotnoma (rang + tavsif).
//  `debtor_actions`  — har bir amal bitta qator. O'quvchining JORIY holati =
//  eng oxirgi amalning holati; alohida "current_status" ustuni YO'Q, chunki
//  saqlangan joriy holat tarix bilan bir kunda ziddiyatga tushadi (aynan
//  `students.balance` ni P1-21 da nega olib tashlaganimiz — SPEC §3.7).
//
//  BU MOLIYAVIY JADVAL EMAS (§3.5 ning o'z izohi)
//  ----------------------------------------------
//  Ichida summa ham, jurnal (ledger) yozuvi ham yo'q — faqat izoh va sana.
//  Shuning uchun u `deploy/init-roles.sql` §5 dagi REVOKE ro'yxatiga
//  QO'SHILMAYDI va `app_rw` da to'liq CRUD qoladi: xato yozilgan izohni
//  tuzatish oddiy `UPDATE`. Himoya boshqa joyda: nima VA'DA QILINGANI
//  yo'qolmasligi kerak, shuning uchun qator o'chirilmaydi —
//  <see cref="DebtorAction.DeletedAt"/> qo'yiladi.
//
//  TIPLAR: yangi jadval, ya'ni haqiqiy tiplar (Billing.cs dagi qoida) —
//  id `uuid`, sana `date`, vaqt belgisi `timestamptz`. O'quvchiga va
//  foydalanuvchiga havolalar `text` bo'lib qoladi, chunki `students.id` va
//  `users.id` shu bazada `text`: FK ustuni turi ota-jadval PK turiga MOS
//  bo'lishi shart.
// ===========================================================================

/// <summary>
/// Qarzdor bilan ishlash holati — maktab o'zi tuzadigan rangli ma'lumotnoma
/// (§3.5: <c>debtors.status_name</c>, <c>status_color</c>, <c>status_hint</c>).
/// Masalan "Bog'lanildi", "Va'da berdi", "Javob bermayapti".
/// </summary>
public class DebtorStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ko'rsatiladigan nom. Unikal — ikkita bir xil holat chalkashtiradi.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ro'yxatdagi nishon rangi (<c>#RRGGBB</c>). Bo'sh bo'lsa UI o'zining
    /// neytral rangini ishlatadi — ya'ni rang MAJBURIY emas.
    /// </summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// Qisqa tushuntirish: bu holat AYNAN qachon qo'yiladi. Administratorlar
    /// almashganda holatlar ma'nosi shu yerda qoladi, odamlarning xotirasida emas.
    /// </summary>
    public string? Hint { get; set; }

    /// <summary>Ro'yxatdagi tartib (kichikdan kattaga).</summary>
    public int Position { get; set; }

    /// <summary>
    /// false = yangi amalda tanlab bo'lmaydi, lekin ESKI amallarda ko'rinib
    /// turaveradi. Ma'lumotnoma qatori o'chirilmaydi — o'chirilsa, u ishlatilgan
    /// tarix ham ma'nosini yo'qotardi.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Qarzdor bo'yicha bitta amal: kim, qachon, nima qildi va ota-ona qaysi sanani
/// va'da qildi (§3.5).
///
/// <para>
/// <b>Xatti-harakatda qo'shimcha-only, grantlarda emas.</b> Jadval o'chirishga
/// ochiq qoladi (moliyaviy emas), lekin ilova qatorni o'chirmaydi —
/// <see cref="DeletedAt"/> qo'yadi. Sabab §3.5 da: nima va'da qilingani
/// tarixi yo'qolmasligi kerak.
/// </para>
/// <para>
/// <b><see cref="PromisedOn"/> — kelishilgan yangi to'lov sanasi.</b> O'tib
/// ketgan va'da, qarz esa hali ochiq bo'lsa — bu "buzilgan va'da" va §3.5 uni
/// beshinchi <c>AnomalyKind</c> sifatida direktor paneliga chiqarishni
/// so'raydi. Shu sabab sana alohida ustun: izoh matnidan uni qidirib
/// bo'lmaydi.
/// </para>
/// </summary>
public class DebtorAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Qaysi o'quvchining qarzi haqida (students.id — `text`).</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>
    /// Shu amaldan keyingi holat. null = holat o'zgarmadi (shunchaki izoh
    /// yozildi yoki va'da sanasi kelishildi).
    /// </summary>
    public Guid? StatusId { get; set; }

    /// <summary>
    /// Nima qilingani — MAJBURIY va bo'sh bo'la olmaydi (baza tekshiradi).
    /// Izohsiz amal "kimdir nimadir qildi" degani, ya'ni foydasiz.
    /// </summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Ota-ona va'da qilgan yangi to'lov sanasi. null = va'da yo'q.</summary>
    public DateOnly? PromisedOn { get; set; }

    /// <summary>Kim yozgan (users.id) — JWT'dan olinadi, so'rov tanasidan EMAS (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Yumshoq o'chirish. null = tirik qator. Ro'yxat va "joriy holat"
    /// hisobi faqat <c>deleted_at is null</c> qatorlarni ko'radi, lekin
    /// yozuvning O'ZI bazada qoladi.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
