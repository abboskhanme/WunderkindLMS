namespace SchoolLms.Domain;

// ===========================================================================
//  Vasiylar (guardians) — SPEC §3.2. Faza 3.
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  Bugungacha vasiy o'quvchi qatoridagi YAGONA `students.parent_phone` ustuni
//  edi. Bir ota-onaning ikki farzandi bo'lsa, ota-ona portali (va endi Telegram
//  Mini App) faqat BIRINCHI mos kelgan farzandni ko'rsatardi — telefon bo'yicha
//  `FirstOrDefault`. Mini App'dagi "farzandni almashtirish" tugmasi shu sababli
//  bloklangan edi.
//
//  `parent_phone` OLIB TASHLANMAYDI. P1-21 endigina katta "eski moliyani
//  yopish" ishini tugatdi; ikkinchi retirement'ni shu yerda boshlash noto'g'ri
//  bo'lardi. Ustun joyida qoladi va uni o'qiydigan kod ham (import, e'lon
//  mail-merge, admin kartochkasi) o'zgarmaydi. Bog'lanish ikki manbali bo'lib
//  qolgani `docs/PENDING_WIRING.md` da yozilgan.
//
//  ID TURI: `string` (uuid matn ko'rinishida) — SPEC §3.2 `uuid` deydi, lekin
//  `students.id` va `users.id` shu bazada `text`. FK ikkalasiga ham boradi,
//  ya'ni tur MOS bo'lishi shart. Aralash sxema tuzishdan ko'ra qo'shnisiga
//  o'xshash bo'lgani afzal (docs/ASSUMPTIONS.md).
// ===========================================================================

/// <summary>
/// Vasiy — ota-ona, bobo-buvi yoki ishonchli shaxs (SPEC §3.2).
/// Bitta vasiy bir nechta o'quvchiga bog'lanishi mumkin (<see cref="StudentGuardian"/>).
/// </summary>
public class Guardian
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Vasiyning tizim akkaunti (rol = <c>parent</c>). Null = akkauntsiz vasiy
    /// (masalan faqat olib ketish uchun ishonchli shaxs). Unikal: bitta akkaunt
    /// bitta vasiyga tegishli.
    /// </summary>
    public string? UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Ko'rsatiladigan telefon raqami — foydalanuvchi kiritgan shaklda.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Faqat raqamlar (`+998 90 123 45 67` → `998901234567`). BAZADA hisoblanadi
    /// (generated stored column), ya'ni ilova uni yozmaydi va u <see cref="Phone"/>
    /// dan hech qachon uzoqlashmaydi. Unikal — bitta raqam bitta vasiy.
    /// </summary>
    public string PhoneKey { get; set; } = string.Empty;

    /// <summary>Passport nusxasi (`/uploads/...`).</summary>
    public string? PassportUrl { get; set; }

    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// O'quvchi ↔ vasiy bog'lanishi (ko'p-ko'pga, SPEC §3.2). Birlamchi kalit —
/// (StudentId, GuardianId) juftligi.
/// </summary>
public class StudentGuardian
{
    public string StudentId { get; set; } = string.Empty;
    public string GuardianId { get; set; } = string.Empty;

    /// <summary>
    /// parent | father | mother | grandparent | trustee | other —
    /// <see cref="GuardianRelation.Stored"/> (students-parity.md §2.3 S-8).
    /// </summary>
    public string Relation { get; set; } = GuardianRelation.Parent;

    /// <summary>
    /// <c>other</c> tanlanganda kim ekanini yozadigan matn ("amakisi",
    /// "opasi") — S-8. Boshqa qiymatlarda odatda null.
    /// </summary>
    public string? RelationNote { get; set; }

    /// <summary>
    /// Asosiy vasiy — chek, shartnoma va rasmiy xabar shu odamga boradi.
    /// Har o'quvchida ko'pi bilan BITTA (qisman unikal indeks).
    /// </summary>
    public bool IsPrimary { get; set; }

    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>Vasiylik turi — <see cref="StudentGuardian.Relation"/> qiymatlari (SPEC §3.2).</summary>
public static class GuardianRelation
{
    public const string Parent = "parent";
    public const string Grandparent = "grandparent";
    public const string Trustee = "trustee";

    // ----- students-parity.md §2.3 (S-8) — kengaytirilgan ro'yxat -----
    // EduSchool ota va onani ALOHIDA ajratadi (ota-onalar ro'yxati aynan shu
    // bo'yicha filtrlanadi), "boshqa" tanlovi esa yonidagi matn bilan keladi.
    public const string Father = "father";
    public const string Mother = "mother";
    public const string Other = "other";

    /// <summary>
    /// Baza <c>ck_student_guardians_relation</c> check constraint'i bilan
    /// BIR XIL ro'yxat: eski uchtasi O'RNIDA emas, YONIGA qo'shildi, ya'ni
    /// birorta mavjud qator o'zgarmaydi.
    /// </summary>
    public static readonly string[] Stored =
        [Parent, Father, Mother, Grandparent, Trustee, Other];

    /// <summary>
    /// Ilova qabul qiladigan qiymatlar — endi <see cref="Stored"/> bilan
    /// AYNAN bir xil (S-8, 3-slice).
    ///
    /// <para>
    /// Ilgari bu ro'yxat uchtagina edi: sxema migratsiyasi baza chegarasini
    /// kengaytirgan, kirish tekshiruvi esa forma bilan birga kengayishi kerak
    /// edi (§4.2). Shu qadam bajarildi — forma endi "ota" va "ona" ni alohida
    /// yozadi, ya'ni tekshiruv ularni rad etsa forma umuman saqlanmasdi.
    /// Ro'yxatda YO'Q qiymat esa hamon rad etiladi: bazadagi
    /// <c>ck_student_guardians_relation</c> ga borib 23514 bilan yiqilishdan
    /// ko'ra, tushunarli 400 qaytgani ma'qul.
    /// </para>
    /// </summary>
    public static readonly string[] All = Stored;

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>Bazaga yozsa bo'ladimi (3-slice validatsiyasi uchun).</summary>
    public static bool IsStorable(string? value) => value is not null && Stored.Contains(value);
}
