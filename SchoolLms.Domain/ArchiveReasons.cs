namespace SchoolLms.Domain;

// ===========================================================================
//  Arxivlash sabablari — docs/modules/existing-module-gaps.md §2.2.
// ===========================================================================
//
//  NEGA UCHINCHI "SABAB" JADVALI
//  -----------------------------
//  §2.2 umumiy `reasons` jadvalini ATAYLAB rad etadi: bizda allaqachon ikkita
//  MAQSADLI sabab jadvali bor va ularning har birida o'ziga xos ustunlar
//  yashaydi — `AbsenceReason` da `Short`, `IsLate`, `Points`;
//  `DisciplineReason` da `Points`. Umumiy jadval ularni null ustunlar yoki
//  JSON sifatida ushlab turishga majbur bo'lardi. Uchinchi kichik jadval
//  halolroq.
//
//  NIMANI HAL QILADI
//  -----------------
//  Hozir `students.archive_reason` — ERKIN MATN. Ya'ni 400 ta arxivlangan
//  o'quvchi 400 ta har xil jumla beradi va "nega ketishyapti" degan savolga
//  guruhlab javob berib bo'lmaydi. Katalog shuni o'zgartiradi.
//
//  ESKI USTUN QOLADI
//  -----------------
//  `students.archive_reason` (matn) O'CHIRILMAYDI: yangi
//  `archive_reason_id` uning yoniga qo'shiladi. Sabab ikkita — (1) ishning
//  qoidasi qo'shimcha (additive), (2) §2.2 "Boshqa" tanlovini va uning
//  yonidagi erkin matnni ATAYLAB saqlab qoladi. Ya'ni ikkala ustun ham
//  kerak: katalog qatori "nima uchun" ni guruhlaydi, matn esa tafsilotni
//  yozadi.
// ===========================================================================

/// <summary>
/// O'quvchini arxivga ko'chirish sababi — maktab o'zi boshqaradigan katalog
/// (§2.2): "Boshqa maktabga ketdi", "Bitirdi", "Boshqa shaharga ko'chdi".
/// </summary>
public class StudentArchiveReason
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ko'rsatiladigan nom. Unikal.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>false = yangi arxivlashda tanlanmaydi; eski yozuvlar joyida qoladi.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Ro'yxatdagi tartib (kichikdan kattaga).</summary>
    public int Position { get; set; }
}
