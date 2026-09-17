namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Arxivlash sabablari katalogi va ommaviy arxivlash — §2.2.
//
//  IKKI USTUN, IKKI VAZIFA. `students.archive_reason` (erkin matn) QOLADI va
//  MAJBURIY: katalog qatori "nega ketishdi" ni GURUHLAYDI, matn esa TAFSILOT
//  yozadi. Shuning uchun quyidagi so'rovlarning hammasida `Reason` (matn) bor
//  va u bo'sh bo'lishi mumkin emas, `ArchiveReasonId` esa ixtiyoriy.
// ===========================================================================

/// <summary>Katalogdagi bitta arxivlash sababi.</summary>
public record StudentArchiveReasonDto(Guid Id, string Name, bool IsActive, int Position, int UsedBy);

/// <summary>Katalog qatorini yaratish/tahrirlash. Berilmagan maydon o'zgarmaydi.</summary>
public record SaveArchiveReasonRequest(string Name, bool? IsActive = null, int? Position = null);

/// <summary>
/// Bir nechta o'quvchini bitta sabab bilan arxivlash (§2.2 — bitiruv tabiatan
/// ommaviy amal).
/// </summary>
/// <param name="StudentIds">Arxivlanadigan o'quvchilar.</param>
/// <param name="Reason">Erkin matn — MAJBURIY (katalog uni almashtirmaydi).</param>
/// <param name="ArchiveReasonId">Katalog qatori (ixtiyoriy — "Boshqa" uchun null).</param>
/// <param name="Force">
/// Qarzdorlik to'sig'ini chetlab o'tish. FAQAT superadmin uchun (§9 Q4); boshqa
/// rolda bu bayroq e'tiborga olinmaydi.
/// </param>
public record BulkArchiveRequest(
    IReadOnlyList<string> StudentIds,
    string Reason,
    Guid? ArchiveReasonId = null,
    bool Force = false);

/// <summary>Qarzi borligi uchun arxivlanmagan bitta o'quvchi.</summary>
public record ArchiveBlockedStudentDto(string StudentId, string FullName, string ClassName, decimal Debt);

/// <summary>
/// Ommaviy arxivlash natijasi. Qarzdorlar SAKRAB O'TILMAYDI — butun amal rad
/// etiladi (<c>Archived = 0</c>) va ro'yxat qaytadi: "o'n besh bolaning uchtasi
/// qoldi" degan yarim natija administrator uchun eng yomon holat.
/// </summary>
public record BulkArchiveResultDto(
    int Archived,
    IReadOnlyList<ArchiveBlockedStudentDto> Blocked,
    bool CanOverride,
    string? Message = null);
