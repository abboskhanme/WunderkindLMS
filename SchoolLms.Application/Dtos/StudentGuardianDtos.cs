namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  O'quvchi formasi va uning VASIYLARI —
//  docs/modules/students-parity.md §2.3 (S-8) va §2.9 (P-1, P-2).
// ===========================================================================
//
//  HAMMASI QO'SHIMCHA. Mavjud `StudentPayload` maydonlari, `StudentDto`
//  javobi va `GET /api/admin/parents` qatorlari TEGILMAGAN — bu yerdagi
//  shakllar ularning YONIDA turadi.
//
//  ESKI IKKI USTUN QOLADI. `students.parent_full_name` va
//  `students.parent_phone` o'nlab joydan o'qiladi (ota-ona portali telefon
//  bo'yicha topadi, shartnoma matni, eksport, import). Vasiy jadvali ularni
//  ALMASHTIRMAYDI: forma ham, quyidagi endpointlar ham ASOSIY vasiyni shu
//  ikki ustun bilan bir qadamda ushlab turadi (`GuardianSync` izohiga
//  qarang).
// ===========================================================================

/// <summary>
/// Formadan keladigan BITTA vasiy (S-8). O'quvchida ko'pi bilan ikkitasi
/// bo'ladi — EduSchool ham shuncha (§2.3.1 "parents[] 1–2 entries").
/// </summary>
/// <param name="FullName">Vasiy F.I.SH. Bo'sh bo'lsa o'quvchining nomidan yasaladi.</param>
/// <param name="Phone">Telefon — vasiyni TOPISH kaliti (oxirgi 9 raqam). Bo'sh = qator yaratilmaydi.</param>
/// <param name="Relation">
/// <c>parent | father | mother | grandparent | trustee | other</c>
/// (<see cref="SchoolLms.Domain.GuardianRelation"/>). Bo'sh = <c>parent</c>.
/// </param>
/// <param name="RelationNote">"Boshqa" tanlanganda kim ekani ("amakisi").</param>
/// <param name="IsPrimary">Asosiy vasiy — o'quvchida bittadan ortiq bo'la olmaydi.</param>
/// <param name="PassportUrl">Passport/hujjat nusxasi (`/uploads/...`).</param>
public record StudentGuardianInput(
    string? FullName,
    string? Phone,
    string? Relation = null,
    string? RelationNote = null,
    bool IsPrimary = false,
    string? PassportUrl = null);

/// <summary>O'quvchi kartochkasidagi bitta vasiy qatori (forma va ro'yxat uchun).</summary>
/// <param name="HasAccount">Tizim akkaunti bormi (rol = <c>parent</c>).</param>
/// <param name="TelegramLinked">Telegram Mini App'ga bog'langanmi.</param>
/// <param name="ChildrenCount">Shu vasiyga biriktirilgan farzandlar soni (uzishdan oldin ko'rinadi).</param>
public record StudentGuardianDto(
    string GuardianId,
    string FullName,
    string Phone,
    string Relation,
    string? RelationNote,
    bool IsPrimary,
    string? PassportUrl,
    bool HasAccount,
    bool TelegramLinked,
    int ChildrenCount);

/// <summary>
/// Forma TAHRIRDA yuklaydigan qo'shimcha ma'lumot: ro'yxat qatorida
/// bo'lmagan maydonlar va vasiylar ro'yxati.
///
/// <para>
/// Nega alohida endpoint: ro'yxat qatori (<c>StudentListRowDto</c>) ekran
/// ustunlari uchun yasalgan va unda hujjat nusxasi ham, vasiylar ham yo'q.
/// Forma ularsiz ochilsa, saqlashda ular JIMGINA tozalanib ketardi.
/// </para>
/// </summary>
public record StudentFormCardDto(
    string StudentId,
    string? Phone,
    string? Language,
    string? DocumentUrl,
    List<StudentGuardianDto> Guardians);

/* ---------- §2.9 Ota-onalar ekrani (P-1, P-2) ---------- */

/// <summary>Ota-onalar ro'yxatidagi bitta farzand.</summary>
public record GuardianChildRowDto(
    string StudentId,
    string FullName,
    string ClassName,
    string Relation,
    string? RelationNote,
    bool IsPrimary,
    string? Phone,
    bool IsArchived);

/// <summary>
/// Ota-onalar ro'yxatining bitta qatori — endi <c>guardians</c> +
/// <c>student_guardians</c> dan (P-1), telefon bo'yicha guruhlashdan emas.
/// </summary>
/// <param name="TelegramLinked">EduSchool'dagi "ilova o'rnatilgan" ning o'rnini bosadi.</param>
public record GuardianRowDto(
    string GuardianId,
    string FullName,
    string Phone,
    string? Login,
    bool HasAccount,
    bool TelegramLinked,
    string? LastSeenAt,
    int ChildrenCount,
    List<GuardianChildRowDto> Children);

/// <summary>
/// Ota-onalar ro'yxatining filtrlari (§2.9.1). BARCHASI ixtiyoriy —
/// birortasi berilmasa barcha vasiy qaytadi.
/// </summary>
public sealed class GuardianListFilter
{
    /// <summary>Vasiy F.I.SH, telefon yoki farzand F.I.SH bo'yicha.</summary>
    public string? Search { get; set; }

    /// <summary>Farzandning sinfi (aniq nom).</summary>
    public string? ClassName { get; set; }

    /// <summary>Farzandning FAOL guruh a'zoligi.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Bitta o'quvchi bo'yicha (kartochkadan kelgan havola).</summary>
    public string? StudentId { get; set; }

    /// <summary>Vasiylik turi — <c>father</c>, <c>mother</c>, ...</summary>
    public string? Relation { get; set; }

    /// <summary>true = Telegram bog'langanlar, false = bog'lanmaganlar.</summary>
    public bool? Connected { get; set; }

    /// <summary>Farzand holati: <c>active</c> (sukut) | <c>archived</c> | <c>all</c>.</summary>
    public string? State { get; set; }
}
