namespace SchoolLms.Domain;

// ===========================================================================
//  O'quv guruhlari va sinf a'zoligi — docs/modules/students-parity.md §2.1.4, §3.1.
// ===========================================================================
//
//  NEGA KERAK BO'LDI
//  -----------------
//  Mijoz 2026-09-17 da tasdiqladi: maktabda BIR NECHTA SINFDAN yig'iladigan
//  guruhlar bor (masalan 5-A va 5-B dan kuchli ingliz tili guruhi). Bizda
//  bunday narsa yo'q edi — faqat `Student.SubGroup` (0/1/2), u esa BITTA sinf
//  ichidagi bo'linish. "5-A ning 3-guruhi" bilan "5-B ning 3-guruhi" bog'liq
//  bo'lmagan ikki narsa bo'lib, har hisobot ularni jimgina qo'shib yuborardi.
//
//  BESH JADVAL
//  -----------
//  `study_groups`          — guruhning o'zi: nomi, BITTA fani, jinsi (ixtiyoriy).
//  `study_group_classes`   — guruhni "boqadigan" sinflar (ko'p-ko'pga).
//  `study_group_teachers`  — guruh o'qituvchilari (ko'p-ko'pga).
//  `study_group_members`   — o'quvchining guruhdagi a'zoligi, SANALI (tarix).
//  `class_memberships`     — o'quvchining SINFDAGI a'zoligi, SANALI (tarix).
//
//  `students.class_name` QOLADI
//  ----------------------------
//  Bugun ~14 ta server so'rovi va ikkita brauzer filtri o'quvchini
//  `ClassName == Name` bilan topadi. Bu migratsiya ulardan BIRORTASINI
//  o'zgartirmaydi: `class_memberships` shu ustundan BIR MARTA to'ldiriladi
//  (backfill) va keyin a'zolik xizmati (1-slice) ikkalasini birga yuritadi.
//  O'sha xizmat chiqmaguncha HAQIQAT MANBAI — `students.class_name`;
//  `class_memberships` migratsiya lahzasidagi nusxa.
//
//  GURUH DARSLARI HALI YO'Q
//  ------------------------
//  Beshta dars jadvaliga (`schedule_templates`, `week_assignments`,
//  `journal_entries`, `lesson_notes`, `quarter_grades`) `owner_kind` qo'shiladi
//  (<see cref="LessonOwnerKind"/>). Mavjud har bir qator `'class'` oladi, va
//  `school_meta.group_lessons_enabled` sukut bo'yicha O'CHIQ — maktab uni
//  cut-over kuni (§4.3) o'zi yoqadi.
//
//  TIPLAR: yangi jadval → haqiqiy tiplar (Billing.cs qoidasi): id `uuid`, sana
//  `date`, vaqt belgisi `timestamptz`. O'quvchi, sinf, fan, o'qituvchi va
//  foydalanuvchiga havolalar `text` — ota-jadvallarning PK turi shu.
// ===========================================================================

/// <summary>
/// Dars qatorining EGASI kim — sinfmi yoki guruhmi (§2.1.4).
///
/// <para>
/// Guruh darsi mavjud <c>class_id</c> ustunida GURUH id'sini saqlaydi va
/// <c>owner_kind = 'group'</c> bilan belgilanadi. Alohida nullable
/// <c>group_id</c> ATAYLAB olinmadi: ~60 joyda o'qiladigan <c>string ClassId</c>
/// null bilan ishlashga majbur bo'lardi. <c>owner_kind</c> esa o'sha
/// o'qishlarni o'zgarishsiz qoldiradi va egasining turini ANIQ qiladi.
/// </para>
/// </summary>
public static class LessonOwnerKind
{
    public const string Class = "class";
    public const string Group = "group";

    /// <summary>Baza check constraint'i bilan BIR XIL ro'yxat.</summary>
    public static readonly string[] All = [Class, Group];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>
/// O'quv guruhi — bir yoki bir nechta sinfdan yig'ilgan, BITTA fan bo'yicha
/// o'qiydigan o'quvchilar (§2.1.1, §3.1).
///
/// <para>
/// <b>Fan o'zgarmas EMAS, lekin EF uchun kalit qismi.</b>
/// <c>(id, subject_id)</c> juftligi <c>study_group_members</c> dagi kompozit
/// FK'ning nishoni (baza "bitta fandan bitta guruh" qoidasini shu orqali
/// ushlaydi). EF Core kalit ustunini kuzatuv (change tracking) orqali
/// o'zgartirishga ruxsat bermaydi — fanni almashtirish
/// <c>ExecuteUpdate</c> bilan qilinadi, a'zolardagi nusxani esa baza
/// <c>ON UPDATE CASCADE</c> bilan o'zi yangilaydi.
/// </para>
/// </summary>
public class StudyGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Guruh nomi. Bitta fan ichida arxivlanmagan guruhlar orasida
    /// unikal (katta-kichik harf farqlanmaydi).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Guruh fani (subjects.id). Fan "guruhlarga bo'linadi"
    /// (<see cref="Subject.IsGroupable"/>) bo'lishi kerak — buni xizmat
    /// tekshiradi, baza emas.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary><c>male</c> | <c>female</c> | null (aralash). Ro'yxatga
    /// qo'shishda filtr bo'ladi.</summary>
    public string? Gender { get; set; }

    /// <summary>Arxivlangan guruh o'quv yili tugagan yoki yopilgan guruh —
    /// tarix saqlanadi, yangi a'zo qo'shilmaydi.</summary>
    public bool IsArchived { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Kim yaratgan (users.id) — JWT'dan (SPEC §4.4).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Guruhni "boqadigan" sinf (§3.1). Sinflar darajasi (grade) alohida
/// saqlanmaydi — u sinfning o'zidan olinadi.
/// </summary>
public class StudyGroupClass
{
    public Guid GroupId { get; set; }

    /// <summary>classes.id — `text`.</summary>
    public string ClassId { get; set; } = string.Empty;
}

/// <summary>Guruh o'qituvchisi (§3.1). Bitta guruhda bir nechta bo'lishi mumkin.</summary>
public class StudyGroupTeacher
{
    public Guid GroupId { get; set; }

    /// <summary>teachers.id — `text`.</summary>
    public string TeacherId { get; set; } = string.Empty;
}

/// <summary>
/// O'quvchining guruhdagi a'zoligi — SANALI yozuv, o'chirilmaydi (§2.1.4).
///
/// <para>
/// <b><see cref="SubjectId"/> — guruh fanining NUSXASI, ataylab.</b>
/// "Bitta o'quvchi — bitta fandan ko'pi bilan bitta faol guruh" qoidasi
/// (mijoz savoli Q1 ning "javob bo'lmasa" qarori) BAZADA qisman unikal
/// indeks bilan ushlanadi, indeks esa bitta jadval ustunlarini ko'radi.
/// Nusxa guruh bilan kompozit FK <c>(group_id, subject_id)</c> orqali
/// bog'langan, ya'ni u guruh fanidan uzoqlasha OLMAYDI.
/// </para>
/// </summary>
public class StudyGroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GroupId { get; set; }

    /// <summary>Guruh fani (study_groups.subject_id nusxasi, kompozit FK).</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>students.id — `text`.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Guruhga qo'shilgan sana.</summary>
    public DateOnly JoinedOn { get; set; }

    /// <summary>Guruhdan chiqqan sana. null = hozir ham guruhda (faol a'zolik).</summary>
    public DateOnly? LeftOn { get; set; }

    /// <summary>Chiqish sababi (erkin matn).</summary>
    public string? LeaveReason { get; set; }

    /// <summary>Kim qo'shgan (users.id).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// O'quvchining sinfdagi (homeroom) a'zoligi — SANALI yozuv (§2.2, §3.1).
///
/// <para>
/// Bir o'quvchida ko'pi bilan BITTA faol (<c>left_on is null</c>) yozuv —
/// baza kafolati. <c>students.class_name</c> shu yozuvning ko'zgusi bo'lib
/// qoladi va ikkalasini a'zolik xizmati birga yuritadi (1-slice).
/// </para>
/// </summary>
public class ClassMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>students.id — `text`.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>classes.id — `text`.</summary>
    public string ClassId { get; set; } = string.Empty;

    public DateOnly JoinedOn { get; set; }

    /// <summary>Sinfdan chiqqan sana. null = faol a'zolik.</summary>
    public DateOnly? LeftOn { get; set; }

    public string? LeaveReason { get; set; }

    /// <summary>Kim yozgan (users.id). null = migratsiya backfill'i yoki
    /// o'chirilgan foydalanuvchi.</summary>
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
