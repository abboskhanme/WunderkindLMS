namespace SchoolLms.Domain;

// ===========================================================================
//  O'quvchi joylashuvlari — docs/modules/students-parity.md §2.8 (L-2), §3.3.
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  Bugun o'quvchining joylashuvi `students` qatorining O'ZIDA yotadi
//  (`latitude`, `longitude`, `location_address`, `location_updated_at`) va u
//  BITTA nuqta. §2.8 esa uchtasini so'raydi: uy, maktab va olib ketish
//  nuqtasi (avtobus), har biri o'z manzili bilan, olib ketish nuqtasida
//  esa vaqt oralig'i ham.
//
//  ESKI TO'RTTA USTUNGA TEGILMAYDI — ATAYLAB
//  -----------------------------------------
//  Ularni o'qiydigan kod bor (xarita ekrani, ota-ona kabineti). Ko'chirish
//  (migratsiya bilan qatorlarni bu yerga o'tkazish va ustunlarni tashlash)
//  ikki joyda haqiqat yaratardi va `Up()` ga `DROP` olib kelardi. Bu jadval
//  ularning YONIDA turadi; qaysi biri "haqiqat" ekani — L-2 slice'ining
//  qarori, sxemaniki emas.
//
//  BITTA TURDAN BITTA NUQTA
//  ------------------------
//  `unique (student_id, kind)` — §3.3 talabi. "Uchtagacha" degani aynan shu:
//  ro'yxat uchta qiymatdan iborat, har biridan bittadan.
//
//  KOORDINATA MAJBURIY, MANZIL YO'Q
//  --------------------------------
//  Qator — xaritadagi NUQTA (§2.8: xodim xaritani bosadi). Koordinatasiz
//  qator ekranda ko'rinmaydi, ya'ni ma'nosiz. Manzil matni esa ixtiyoriy:
//  Q6 (geokoder yo'q) bo'yicha uni xodim qo'lda yozadi va yozmasligi ham
//  mumkin.
//
//  OLIB KETISH VAQTI FAQAT `pickup` DA EMAS
//  ----------------------------------------
//  Vaqt oralig'i `kind` bilan CHEKLANMAYDI: maktab avtobusi bolani UYIDAN
//  soat 7:30 da oladi, ya'ni `home` qatorida ham vaqt bo'lishi mumkin.
//  Bunday cheklov bugun hech kimga kerak emas, ertaga esa to'sqinlik
//  qilardi.
// ===========================================================================

/// <summary><see cref="StudentLocation.Kind"/> qiymatlari (§3.3, L-2).</summary>
public static class StudentLocationKind
{
    /// <summary>Uy manzili.</summary>
    public const string Home = "home";

    /// <summary>Maktab (yoki bolaning ikkinchi o'quv joyi).</summary>
    public const string School = "school";

    /// <summary>Olib ketish/olib kelish nuqtasi (avtobus bekati).</summary>
    public const string Pickup = "pickup";

    /// <summary>Baza CHECK cheklovi bilan BIR XIL ro'yxat.</summary>
    public static readonly string[] All = [Home, School, Pickup];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>O'quvchining bitta turdagi joylashuvi (L-2).</summary>
public class StudentLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>students.id — `text`.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary><see cref="StudentLocationKind"/>. Bitta o'quvchida har turdan bitta.</summary>
    public string Kind { get; set; } = StudentLocationKind.Home;

    /// <summary>
    /// Manzil matni ("Chilonzor 9-kvartal, 3-uy"). null = xodim yozmagan;
    /// geokoder YO'Q (§5 Q6), ya'ni bu maydon hech qachon avtomatik
    /// to'ldirilmaydi.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Kenglik (latitude), `numeric(9,6)` — ~11 sm aniqlik.</summary>
    public decimal Lat { get; set; }

    /// <summary>Uzunlik (longitude), `numeric(9,6)`.</summary>
    public decimal Lng { get; set; }

    /// <summary>Olib ketish oynasining boshi ("07:30"). null = ko'rsatilmagan.</summary>
    public TimeOnly? PickupFrom { get; set; }

    /// <summary>Olib ketish oynasining oxiri. null = ko'rsatilmagan.</summary>
    public TimeOnly? PickupTo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
