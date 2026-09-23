namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  O'quvchilar ro'yxati, holat taglari, izohlar va ikki bosqichli import —
//  docs/modules/students-parity.md §2.3 (S-1..S-7, S-11, K-4) va §2.4 (A-1..A-3).
//
//  HAMMASI QO'SHIMCHA. Mavjud `StudentDto` va `/api/admin/students` javobi
//  TEGILMAGAN: eski ekranlar (ota-ona portali, hisobotlar) o'shandan o'qiydi.
//  Bu yerdagi qatorlar FAQAT ro'yxat ekranining ustunlari uchun — shuning
//  uchun ularda sinf darajasi, holat nomi/rangi va shartnoma raqami bor,
//  lekin ichki maydonlar (UserId, koordinatalar) yo'q.
// ===========================================================================

/// <summary>
/// Ro'yxat so'rovining filtrlari — query string'dan bog'lanadi
/// (<c>[FromQuery]</c>). BARCHASI ixtiyoriy: birortasi berilmasa natija
/// bugungi `GET /api/admin/students` bilan AYNAN bir xil bo'ladi (§4.2 qoidasi).
/// </summary>
public sealed class StudentListFilter
{
    /// <summary><c>active</c> (sukut) | <c>archived</c> | <c>all</c>.</summary>
    public string? State { get; set; }

    /// <summary>F.I.SH yoki ota-ona F.I.SH bo'yicha qidiruv (registrga bog'liq emas).</summary>
    public string? Search { get; set; }

    /// <summary>Sinf nomi (aniq moslik).</summary>
    public string? ClassName { get; set; }

    /// <summary>Sinf darajalari — vergul bilan ("1,2,9"). Orasidagi bog'lovchi — YOKI.</summary>
    public string? Grades { get; set; }

    /// <summary><c>male</c> | <c>female</c>.</summary>
    public string? Gender { get; set; }

    /// <summary>O'qish tili: uz | ru | en | kaa.</summary>
    public string? Language { get; set; }

    /// <summary>Holat tagi (<c>student_statuses.id</c>).</summary>
    public Guid? StatusId { get; set; }

    /// <summary>true = holati qo'yilmaganlar, false = qo'yilganlar.</summary>
    public bool? HasStatus { get; set; }

    /// <summary>Guruh a'zoligi (faol a'zolik — <c>left_on is null</c>).</summary>
    public Guid? GroupId { get; set; }

    /// <summary>K-4 — shartnomasi bor / yo'q.</summary>
    public bool? HasContract { get; set; }

    /// <summary>Faol obunasi bor / yo'q.</summary>
    public bool? HasSubscription { get; set; }

    /// <summary>Obuna toifasi (<c>fee_categories.id</c>) — faqat faol obuna bo'yicha.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Tasdiqlangan, bugun kuchda bo'lgan chegirmasi bor / yo'q.
    /// EduSchool'dagi <c>discountId</c> (chegirma KATALOGI) bizda yo'q — bizda
    /// chegirma har doim BITTA o'quvchiga beriladi (SPEC §8.1 Q5), shuning
    /// uchun bu yerda "bormi" savolidan boshqa savol yo'q.
    /// </summary>
    public bool? HasDiscount { get; set; }

    /// <summary>Sertifikat turlari — vergul bilan ajratilgan id'lar (mavjud filtr).</summary>
    public string? CertificateTypeIds { get; set; }

    /// <summary>Sertifikatni bergan o'qituvchi (mavjud filtr).</summary>
    public string? CertificateTeacherId { get; set; }

    /// <summary>Qabul sanasi oralig'i (ISO "YYYY-MM-DD", ikkalasi ham kiradi).</summary>
    public string? EnrolledFrom { get; set; }
    public string? EnrolledTo { get; set; }

    /// <summary>A-1 — arxivlangan sana oralig'i (ISO).</summary>
    public string? ArchivedFrom { get; set; }
    public string? ArchivedTo { get; set; }

    /// <summary>A-1 — arxivlash sababi (katalog qatori).</summary>
    public Guid? ArchiveReasonId { get; set; }

    /// <summary>Yosh oralig'i (to'liq yil, ikkalasi ham kiradi).</summary>
    public int? AgeFrom { get; set; }
    public int? AgeTo { get; set; }

    /// <summary>
    /// <c>debt</c> = qarzdorlar (qoldiq &lt; 0), <c>paid</c> = qarzsizlar
    /// (qoldiq &gt;= 0), <c>credit</c> = haqdorlar (qoldiq &gt; 0). Bugungi ekrandagi "Balans" tanlovi bilan bir xil.
    /// </summary>
    public string? BalanceState { get; set; }

    /// <summary>
    /// Bosh sahifa kartalari bilan AYNAN bir xil to'plamlar (karta bosilganda shu ro'yxat ochiladi):
    /// <c>inClass</c> = mavjud sinfda o'qiyotgan ("Aktiv o'quvchilar"),
    /// <c>unassigned</c> = sinfi yo'q yoki sinfi o'chirilgan ("Sinfga qo'shilmagan"),
    /// <c>waiting</c> = sinfsiz, lekin mo'ljaldagi darajasi bor ("Kutayotgan"),
    /// <c>leftFromClass</c> = sinfsiz va yopilgan sinf a'zoligi bor ("Sinfdan chiqarilgan").
    /// </summary>
    public string? Placement { get; set; }

    /// <summary>
    /// <c>ever</c> = kamida bitta (bekor qilinmagan) to'lov qilganlar,
    /// <c>thisMonth</c> = birinchi to'lovi shu oyda bo'lganlar ("Birinchi to'lov qilganlar").
    /// </summary>
    public string? FirstPayment { get; set; }

    /// <summary>Eng kam qarz (musbat son): faqat shu summadan ko'p qarzi borlar.</summary>
    public decimal? MinDebt { get; set; }

    /// <summary>Qoldiq oralig'i (ishorali: manfiy = qarz).</summary>
    public decimal? BalanceFrom { get; set; }
    public decimal? BalanceTo { get; set; }

    /// <summary>
    /// <c>fullName</c> (sukut) | <c>className</c> | <c>balance</c> |
    /// <c>birthDate</c> | <c>enrollmentDate</c> | <c>status</c> |
    /// <c>archivedAt</c> | <c>parentFullName</c>.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary><c>asc</c> (sukut) | <c>desc</c>.</summary>
    public string? SortOrder { get; set; }

    /// <summary>1-asosli sahifa raqami.</summary>
    public int? Page { get; set; }

    /// <summary>Sahifadagi qatorlar soni (1..1000, sukut 200).</summary>
    public int? PageSize { get; set; }
}

/// <summary>Ro'yxatning bitta qatori — §2.3.1 dagi ustunlar.</summary>
/// <param name="TargetGrade">
/// §3.3 (S-9) — <paramref name="ClassName"/> bo'sh bo'lganda mo'ljaldagi sinf
/// darajasi (0-11); sinfi bor o'quvchida har doim null.
/// </param>
public record StudentListRowDto(
    string Id,
    string FullName,
    string ClassName,
    int Grade,
    string Gender,
    string BirthDate,
    int? Age,
    string Address,
    string? Phone,
    string ParentFullName,
    string ParentPhone,
    string? Language,
    string EnrollmentDate,
    decimal Balance,
    Guid? StatusId,
    string? StatusName,
    string? StatusColor,
    bool HasContract,
    string? ContractNumber,
    bool IsArchived,
    string? ArchivedAt,
    string? ArchiveReason,
    Guid? ArchiveReasonId,
    string? PhotoUrl,
    short? TargetGrade = null);

/// <summary>
/// Bitta sahifa. <paramref name="TotalDebt"/> va <paramref name="TotalCredit"/> —
/// S-4: FILTRLANGAN to'plamning umumiy qarzi va avansi (ikkalasi ham musbat son),
/// ya'ni ro'yxat ostidagi yakun.
/// </summary>
public record StudentListPageDto(
    IReadOnlyList<StudentListRowDto> Items,
    int Total,
    int Page,
    int PageSize,
    decimal TotalDebt,
    decimal TotalCredit);

/* =========================================================================
 *  S-5 — holat taglari
 * ========================================================================= */

/// <summary>
/// Holat tagi katalogidagi bitta qator.
///
/// <para>
/// <b>Nomi nega <c>StudentStatusTagDto</c>.</b> <c>StudentStatusDto</c> nomi
/// allaqachon band (Dtos.cs — davomat ekranidagi "bugun keldi/kelmadi"),
/// va ikkalasi butunlay boshqa narsa.
/// </para>
/// </summary>
/// <param name="UsedBy">Nechta o'quvchida qo'yilgan — o'chirish mumkinligini shu hal qiladi.</param>
public record StudentStatusTagDto(
    Guid Id,
    string Name,
    string? Color,
    int Position,
    bool IsDefault,
    bool IsActive,
    int UsedBy);

public record SaveStudentStatusRequest(string? Name, string? Color, int? Position, bool? IsActive);

/// <summary>O'quvchiga holat qo'yish. <c>null</c> = holatni olib tashlash.</summary>
public record SetStudentStatusRequest(Guid? StatusId);

/* =========================================================================
 *  S-7 — ommaviy butunlay o'chirish
 * ========================================================================= */

/// <summary>O'chirilmagan bitta o'quvchi va sababi.</summary>
public record StudentDeleteBlockedDto(string StudentId, string FullName, string Reason);

/// <summary>
/// Ommaviy o'chirish natijasi. Arxivlash bilan bir xil qoida:
/// <b>hammasi yoki hech nima</b> — bitta o'quvchi o'chirilmasa, hech biri
/// o'chirilmaydi va ro'yxat qaytadi.
/// </summary>
public record StudentDeleteResultDto(
    int Deleted,
    IReadOnlyList<StudentDeleteBlockedDto> Blocked,
    string? Message = null);

public record BulkDeleteStudentsRequest(List<string>? StudentIds);

/* =========================================================================
 *  S-11 — o'quvchi izohlari
 * ========================================================================= */

/// <param name="CanEdit">Joriy foydalanuvchi shu izohni tahrirlay/o'chira oladimi.</param>
public record StudentCommentDto(
    Guid Id,
    string StudentId,
    string Kind,
    string Body,
    string? ImageUrl,
    string? FileUrl,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanEdit);

public record SaveStudentCommentRequest(string? Kind, string? Body, string? ImageUrl, string? FileUrl);

/* =========================================================================
 *  S-3 — ikki bosqichli import
 * ========================================================================= */

/// <summary>Bitta xato: Excel qator raqami, ustun nomi va sababi.</summary>
public record ImportCellErrorDto(int Row, string Column, string Message);

/// <summary>
/// Tekshiruv natijasi. <paramref name="Ok"/> = <c>false</c> bo'lsa
/// <c>commit</c> HECH NARSA yozmaydi — fayl butunligicha rad etiladi.
/// </summary>
/// <param name="Created">Yangi yaratiladigan qatorlar soni.</param>
/// <param name="Updated">Mavjud o'quvchi ustiga yoziladigan qatorlar soni.</param>
/// <param name="Skipped">Bo'sh (e'tiborsiz) qatorlar soni.</param>
/// <param name="Errors">Har bir xato — qator raqami bilan.</param>
/// <param name="Preview">Birinchi qatorlarning ko'rinishi (eng ko'pi 20 ta).</param>
public record StudentImportPreviewDto(
    bool Ok,
    int Total,
    int Created,
    int Updated,
    int Skipped,
    IReadOnlyList<ImportCellErrorDto> Errors,
    IReadOnlyList<StudentImportPreviewRowDto> Preview,
    string? Message = null);

/// <summary>Ko'rib chiqish jadvalining bitta qatori.</summary>
/// <param name="Groups">G-19 — qatorda tanilgan guruhlar, vergul bilan ("" — yo'q).</param>
public record StudentImportPreviewRowDto(
    int Row,
    string FullName,
    string ClassName,
    string Action,
    string Groups = "");

/// <summary>Yozib bo'lingandan keyingi yakun.</summary>
public record StudentImportCommitDto(int Created, int Updated, int Skipped);
