namespace SchoolLms.Domain;

// ===========================================================================
//  O'quvchi haqidagi izohlar — docs/modules/students-parity.md §2.3 (S-11).
// ===========================================================================
//
//  NEGA ALOHIDA JADVAL, `discipline_points` EMAS
//  ---------------------------------------------
//  Intizomiy ball BALLGA tegadi (100 dan ayriladi, hisobotga tushadi,
//  ota-onaga xabar ketishi mumkin). Izoh esa BALLSIZ kuzatuv: "onasi bilan
//  gaplashildi", "olimpiadaga tayyorlanmoqda". Ikkalasini bitta jadvalga
//  qo'shish har ikkalasining ma'nosini buzardi.
//
//  `kind` faqat ikkita qiymat — `positive` | `negative` (§2.3.1 dagi chip).
//  Uchinchi "neytral" qiymat EduSchool'da yo'q, bizda ham yo'q.
//
//  FAYL VA RASM: mavjud `UploadsController` + `UploadGuard` orqali yuklanadi,
//  bu yerda faqat manzil (`/uploads/...`) saqlanadi.
// ===========================================================================

/// <summary><see cref="StudentComment.Kind"/> qiymatlari (S-11).</summary>
public static class StudentCommentKind
{
    public const string Positive = "positive";
    public const string Negative = "negative";

    /// <summary>Baza check constraint'i bilan BIR XIL ro'yxat.</summary>
    public static readonly string[] All = [Positive, Negative];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>O'quvchi haqida xodim yozgan bitta izoh (S-11).</summary>
public class StudentComment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>students.id — `text`.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary><see cref="StudentCommentKind"/>.</summary>
    public string Kind { get; set; } = StudentCommentKind.Positive;

    /// <summary>Izoh matni — bo'sh bo'la olmaydi (baza tekshiradi).</summary>
    public string Body { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public string? FileUrl { get; set; }

    /// <summary>Kim yozgan (users.id) — JWT'dan (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Tahrirlangan vaqt. null = tahrirlanmagan.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
