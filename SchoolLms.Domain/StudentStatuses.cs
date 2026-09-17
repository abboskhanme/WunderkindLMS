namespace SchoolLms.Domain;

// ===========================================================================
//  O'quvchi holati (status tag) — docs/modules/students-parity.md §2.3 (S-5).
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  EduSchool o'quvchilar ro'yxatida rangli HOLAT ustuni bor va u to'g'ridan
//  to'g'ri satrdan almashtiriladi ("VIP", "Sinov muddatida", "Ko'chib ketmoqchi").
//  Bizda bunday narsa yo'q: o'quvchi yo faol, yo arxivda. Holat — arxivlashdan
//  OLDINGI kuzatuv vositasi, ya'ni boshqa o'q.
//
//  KATALOG SEED QILINMAYDI — ATAYLAB
//  ---------------------------------
//  §2.3 EduSchool'ning sukut holatlari borligini yozadi, LEKIN ularning
//  nomlari bundle'da yo'q (tarjimalar alohida). Nom to'qib chiqarish —
//  maktabning ish jarayonini bizning taxminimiz bilan almashtirish bo'lardi
//  (`certificate_types` da ham aynan shu sabab bilan seed yo'q). Birinchi
//  holatni maktab Sozlamalarda o'zi kiritadi.
//
//  `IsDefault` — o'chirib bo'lmaydigan tizim qatori uchun. Bugun bunday qator
//  yo'q; mijoz sukut holatlar ro'yxatini bersa, ular shu bayroq bilan keladi
//  va ekran ularni tahrirlashni bloklaydi (§2.3.1).
// ===========================================================================

/// <summary>
/// O'quvchi holati — maktab o'zi boshqaradigan rangli katalog (S-5).
/// </summary>
public class StudentStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ko'rsatiladigan nom. Unikal.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Nishon rangi <c>#RRGGBB</c> ko'rinishida. null = neytral rang.</summary>
    public string? Color { get; set; }

    /// <summary>Ro'yxatdagi tartib (kichikdan kattaga).</summary>
    public int Position { get; set; }

    /// <summary>
    /// Tizim qatori — tahrirlab va o'chirib bo'lmaydi (EduSchool'dagi
    /// <c>isDefault</c>). Migratsiya birorta shunday qator YOZMAYDI.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>false = yangi tanlovda ko'rinmaydi; qo'yilgan holatlar qoladi.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
