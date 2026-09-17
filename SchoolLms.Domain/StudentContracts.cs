namespace SchoolLms.Domain;

// ===========================================================================
//  O'quvchi shartnomalari reyestri — docs/modules/students-parity.md §2.10 (K-1).
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  Bizda `contract_templates` (Word shablon) va `Contract` (kimga yuborildi)
//  bor, lekin O'QUVCHIGA bog'langan shartnoma YO'Q: raqam, imzo sanasi,
//  tugash sanasi, skanerlangan nusxa — hech qayerda saqlanmaydi. §2.10
//  buni "shartnoma" so'zining uchinchi ma'nosi deb ajratadi va aynan shuni
//  so'raydi.
//
//  MOLIYAVIY EMAS — ATAYLAB
//  ------------------------
//  Jadvalda SUMMA yo'q. To'lov shartlari `student_subscriptions` da
//  (SPEC §3.7) va faqat u yerda qoladi: ikkinchi joyda saqlangan narx bir kun
//  birinchisi bilan ziddiyatga tushardi. Shu sababli bu jadval
//  `deploy/init-roles.sql` §5 dagi REVOKE ro'yxatiga ham kirmaydi.
//
//  RAQAM: unikal, LEKIN faqat null bo'lmaganda (qisman unikal indeks) —
//  raqamsiz (hali rasmiylashtirilmagan) yozuv bir nechta bo'lishi mumkin.
//  Nomerlash qoidasi (avtomatik/qo'lda) — K-6, P2 (Batch C).
// ===========================================================================

/// <summary><see cref="StudentContract.Source"/> qiymatlari (K-1).</summary>
public static class StudentContractSource
{
    /// <summary>Shablondan tizimning o'zi hosil qilgan (.docx) — K-2.</summary>
    public const string Generated = "generated";

    /// <summary>Imzolangan nusxa qo'lda yuklangan — K-3.</summary>
    public const string Uploaded = "uploaded";

    /// <summary>Baza check constraint'i bilan BIR XIL ro'yxat.</summary>
    public static readonly string[] All = [Generated, Uploaded];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>O'quvchining bitta shartnomasi (K-1).</summary>
public class StudentContract
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>students.id — `text`.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>
    /// Qaysi shablondan hosil qilingan (contract_templates.id — `text`).
    /// null = qo'lda yuklangan yoki shablon keyin o'chirilgan.
    /// </summary>
    public string? TemplateId { get; set; }

    /// <summary>Shartnoma raqami. null bo'lmasa — unikal.</summary>
    public string? Number { get; set; }

    public DateOnly? SignedOn { get; set; }

    public DateOnly? EndsOn { get; set; }

    /// <summary>Hujjat fayli (`/uploads/...`), UploadsController orqali.</summary>
    public string? FileUrl { get; set; }

    /// <summary><see cref="StudentContractSource"/>.</summary>
    public string Source { get; set; } = StudentContractSource.Generated;

    public string? Comment { get; set; }

    /// <summary>Kim kiritgan (users.id) — JWT'dan (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
