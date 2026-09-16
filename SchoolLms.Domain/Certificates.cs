namespace SchoolLms.Domain;

// ===========================================================================
//  Sertifikatlar — docs/modules/existing-module-gaps.md §2.3.
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  Tashqi imtihon topshiradigan maktabda (IELTS, SAT, olimpiada) bolaning
//  sertifikatlari hozir hech qayerda saqlanmaydi. §2.3 buni "genuinely
//  missing" deb belgilaydi va ikkita kichik jadval taklif qiladi.
//
//  IKKI JADVAL, BIRI MA'LUMOTNOMA
//  ------------------------------
//  `certificate_types` — sertifikat TURI (admin Sozlamalardan boshqaradi).
//  `certificates`      — bitta hujjat: kimga, qaysi turda, qachon berilgan.
//
//  `is_scored` — ENG MUHIM USTUN (§2.3 dagi `certificate.is_sat_hint` shundan
//  ochiladi). Turni "standart test" deb belgilash mumkin, ya'ni unda BALL
//  bo'ladi. Shu bitta bayroq tufayli "Natijalar" tab'i (o'quvchi × ball)
//  keyinchalik EKRAN bo'ladi, migratsiya emas — mijoz savoli Q7 ning javobi
//  aynan shu: "register first, scores second".
//
//  TIPLAR: yangi jadval → haqiqiy tiplar (Billing.cs qoidasi): id `uuid`,
//  sana `date`, vaqt belgisi `timestamptz`, ball `numeric(6,2)`. O'quvchi,
//  fan, o'qituvchi va foydalanuvchiga havolalar `text` — ota-jadvallarning
//  PK turi shu.
//
//  FAYL: mavjud `UploadsController` + `UploadGuard` orqali yuklanadi, bu yerda
//  faqat manzil (`/uploads/...`) saqlanadi — `Student.BirthCertificateUrl`
//  bilan bir xil yondashuv.
// ===========================================================================

/// <summary>
/// Sertifikat turi — "IELTS", "Matematika olimpiadasi", "Ichki imtihon" (§2.3).
/// </summary>
public class CertificateType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ko'rsatiladigan nom. Unikal.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// true = standart test, ya'ni sertifikatda BALL bo'ladi (IELTS 7.5, SAT 1340).
    /// false = shunchaki hujjat (ishtirok, g'olib, malaka).
    /// "Natijalar" tab'i faqat shunday turlarni ko'rsatadi.
    /// </summary>
    public bool IsScored { get; set; }

    /// <summary>false = yangi sertifikatda tanlanmaydi; eski hujjatlar joyida qoladi.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// O'quvchiga berilgan bitta sertifikat (§2.3).
///
/// <para>
/// <b><see cref="Score"/> — faqat <see cref="CertificateType.IsScored"/> turlarda.</b>
/// Bazada bu bog'liqlik TEKSHIRILMAYDI (u ikkita jadvalga tegishli bo'lgani
/// uchun CHECK bilan ifodalanmaydi); baza faqat "ball manfiy emas" ni
/// kafolatlaydi, qolganini ilova qatlami tekshiradi.
/// </para>
/// </summary>
public class Certificate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Kimga berilgan (students.id — `text`).</summary>
    public string StudentId { get; set; } = string.Empty;

    public Guid TypeId { get; set; }

    /// <summary>Qaysi fan bo'yicha (subjects.id). null = fanga bog'liq emas.</summary>
    public string? SubjectId { get; set; }

    /// <summary>Tayyorlagan/imzolagan o'qituvchi (teachers.id). null = ko'rsatilmagan.</summary>
    public string? TeacherId { get; set; }

    /// <summary>Hujjat raqami (blank raqami, sertifikat kodi).</summary>
    public string? Number { get; set; }

    /// <summary>Ball — standart testlarda (IELTS 7.5). Manfiy bo'la olmaydi.</summary>
    public decimal? Score { get; set; }

    /// <summary>Berilgan sana — MAJBURIY (ro'yxat shu bo'yicha saralanadi).</summary>
    public DateOnly IssuedOn { get; set; }

    /// <summary>
    /// Amal qilish muddati. null = muddatsiz. IELTS ikki yil amal qiladi —
    /// "muddati tugayapti" filtri shu ustundan ishlaydi.
    /// </summary>
    public DateOnly? ExpiresOn { get; set; }

    /// <summary>Skanerlangan hujjat (`/uploads/...`), UploadsController orqali.</summary>
    public string? FileUrl { get; set; }

    public string? Comment { get; set; }

    /// <summary>Kim kiritgan (users.id) — JWT'dan (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
