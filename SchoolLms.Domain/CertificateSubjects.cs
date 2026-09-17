namespace SchoolLms.Domain;

// ===========================================================================
//  Sertifikat fanlari — docs/modules/students-parity.md §2.7 (Z-3), §3.3.
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  `certificates.subject_id` BITTA fanni ko'rsatadi. Amalda esa bitta hujjat
//  bir nechta fanni qamrab oladi: "Matematika + Fizika olimpiadasi",
//  "Ingliz tili va adabiyoti". §2.7 buni Z-3 sifatida qayd etadi.
//
//  `certificates.subject_id` JOYIDA QOLADI — ATAYLAB
//  ------------------------------------------------
//  Uni o'chirish ikki narsani buzardi: (1) uni o'qiydigan mavjud kod
//  (sertifikat ro'yxatidagi "Fan" ustuni) va (2) "asosiy fan" ma'nosi —
//  hujjatda bittasi bosh fan bo'lib qoladi. Shuning uchun bu jadval eski
//  ustunning O'RNIGA emas, YONIGA qo'shiladi va migratsiya uni eski
//  ustundan BIR MARTA to'ldiradi (backfill). Ikkalasini birga yuritish —
//  sertifikat xizmatining (Z-slice) ishi; sxema faqat joy tayyorlaydi.
//
//  ALOHIDA `id` YO'Q
//  -----------------
//  Kalit — (certificate_id, subject_id) juftligi, xuddi
//  `study_group_classes` va `study_group_teachers` dagidek: bu bog'lanish
//  jadvali, unda saqlanadigan boshqa ma'lumot yo'q va bitta juftlik ikki
//  marta yozilmasligi kerak.
// ===========================================================================

/// <summary>
/// Sertifikat ↔ fan bog'lanishi (Z-3). Kalit — ikkala ustun birgalikda.
/// </summary>
public class CertificateSubject
{
    /// <summary>Qaysi hujjat (certificates.id — `uuid`).</summary>
    public Guid CertificateId { get; set; }

    /// <summary>Qaysi fan (subjects.id — `text`).</summary>
    public string SubjectId { get; set; } = string.Empty;
}
