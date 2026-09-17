namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  O'quv guruhlari va sinf a'zoligi — docs/modules/students-parity.md
//  §2.1 (Group), §2.2 (Sinf), G-6 / G-10 / C-1.
//
//  SANA TIPLARI. Guruh va sinf a'zoligi jadvallarida sana `date`, ya'ni
//  <see cref="DateOnly"/>. JSON'ga u "2026-09-17" bo'lib chiqadi — brauzer
//  tomonda oddiy satr, vaqt mintaqasi muammosi yo'q (o'quvchi qaysi kuni
//  guruhga qo'shilgani — kun, lahza emas).
//
//  NEGA HAMMA YERDA `ClassName` VA `ClassId` BIRGA. Bugungi tizimda
//  o'quvchini sinfga bog'laydigan narsa — `students.class_name` (NOM). Yangi
//  `class_memberships` esa id bilan ishlaydi. Ekran ikkalasini ham ko'radi:
//  nomi — odam uchun, id'si — keyingi so'rov uchun.
// ===========================================================================

/* ---------------------------------------------------------------------------
 *  1. Guruh — ro'yxat, kartochka, saqlash
 * ------------------------------------------------------------------------ */

/// <summary>Guruhni boqadigan sinfning qisqa ko'rinishi.</summary>
public record StudyGroupClassRefDto(string Id, string Name, int Grade);

/// <summary>Guruh o'qituvchisining qisqa ko'rinishi.</summary>
public record StudyGroupTeacherRefDto(string Id, string FullName);

/// <summary>
/// Guruhlar ro'yxatidagi bitta qator (§2.1.1 "List"): fan, boqadigan sinflar,
/// nom, o'qituvchilar va FAOL a'zolar soni.
/// </summary>
public record StudyGroupListItemDto(
    Guid Id,
    string Name,
    string SubjectId,
    string SubjectName,
    string? Gender,
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    IReadOnlyList<StudyGroupClassRefDto> Classes,
    IReadOnlyList<StudyGroupTeacherRefDto> Teachers,
    int MemberCount);

/// <summary>
/// Guruhning to'liq kartochkasi — forma uchun. <paramref name="Members"/> —
/// FAOL a'zolar (o'ng panel); tarix alohida so'raladi.
/// </summary>
public record StudyGroupDetailDto(
    Guid Id,
    string Name,
    string SubjectId,
    string SubjectName,
    string? Gender,
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    IReadOnlyList<StudyGroupClassRefDto> Classes,
    IReadOnlyList<StudyGroupTeacherRefDto> Teachers,
    IReadOnlyList<StudyGroupMemberDto> Members);

/// <summary>
/// Guruhdagi bitta a'zolik yozuvi. <paramref name="LeftOn"/> null bo'lsa —
/// hozir ham guruhda.
/// </summary>
/// <param name="ClassName">
/// O'quvchining BUGUNGI sinfi (<c>students.class_name</c>), a'zolik ochilgan
/// paytdagisi emas: ro'yxatni ochgan odam bolani bugun qayerdan topishini
/// bilishi kerak.
/// </param>
public record StudyGroupMemberDto(
    Guid Id,
    string StudentId,
    string FullName,
    string ClassName,
    string Gender,
    DateOnly JoinedOn,
    DateOnly? LeftOn,
    string? LeaveReason);

/// <summary>
/// Guruhni yaratish/tahrirlash (§2.1.1 "Form").
///
/// <para>
/// <paramref name="StudentIds"/> — <b>ixtiyoriy</b>. Berilsa, u FAOL ro'yxatning
/// to'liq holati deb qabul qilinadi: ro'yxatda yo'q a'zolar yopiladi, yangilari
/// qo'shiladi. Berilmasa (null) ro'yxatga UMUMAN tegilmaydi — guruh nomini
/// o'zgartirish bolalarni guruhdan chiqarib yubormasligi kerak.
/// </para>
/// </summary>
public record SaveStudyGroupRequest(
    string Name,
    string SubjectId,
    IReadOnlyList<string> ClassIds,
    IReadOnlyList<string> TeacherIds,
    string? Gender = null,
    IReadOnlyList<string>? StudentIds = null);

/// <summary>
/// Guruhni nusxalash (§2.1.1 — "Duplicate" qatori). Fan, sinflar va jins
/// nusxada QULFLANGAN (EduSchool ham shunday), shuning uchun bu yerda faqat
/// yangi nom, o'qituvchilar va ro'yxatni ko'chirish bayrog'i bor.
/// </summary>
public record DuplicateStudyGroupRequest(
    string Name,
    IReadOnlyList<string>? TeacherIds = null,
    bool CopyMembers = false);

/// <summary>
/// Ro'yxat tanlash oynasining CHAP paneli (§2.1.1 "Roster picker").
///
/// <para>
/// <paramref name="CurrentGroupId"/> to'ldirilgan bo'lsa — o'quvchi SHU FAN
/// bo'yicha allaqachon boshqa guruhda va qatorni tanlab bo'lmaydi (kulrang).
/// Qoidani baza ham ushlaydi (<c>ux_group_members_one_group_per_subject</c>),
/// bu yerda esa u ko'rinadigan bo'ladi.
/// </para>
/// </summary>
public record GroupCandidateDto(
    string StudentId,
    string FullName,
    string ClassId,
    string ClassName,
    string Gender,
    Guid? CurrentGroupId,
    string? CurrentGroupName);

/// <summary>Guruhga bir yoki bir nechta o'quvchi qo'shish.</summary>
public record AddGroupMembersRequest(IReadOnlyList<string> StudentIds);

/// <summary>
/// O'quvchini guruhdan chiqarish. Sabab — erkin matn, ixtiyoriy (§2.1.1:
/// sabab faqat SINFdan chiqarishda majburiy bo'lishi mumkin).
/// </summary>
public record RemoveGroupMemberRequest(string? Reason = null);

/// <summary>
/// O'quvchini boshqa guruhga o'tkazish. Nishon guruh AYNAN SHU FANDAN bo'lishi
/// shart (§2.1.1 "Transfer") — aks holda bola ikki fanning guruhida chalkashib
/// ketardi.
/// </summary>
public record TransferGroupMemberRequest(Guid ToGroupId, string? Reason = null);

/* ---------------------------------------------------------------------------
 *  2. Sinf ro'yxati (C-1)
 * ------------------------------------------------------------------------ */

/// <summary>Sinf ro'yxatidagi bitta o'quvchi.</summary>
/// <param name="Balance">
/// HISOBLANGAN qoldiq (<c>StudentBalanceQuery</c> kelishuvi: manfiy = qarz).
/// Ekran uni faqat <c>finance</c> ni ko'ra oladigan foydalanuvchiga chizadi.
/// </param>
public record ClassRosterRowDto(
    Guid MembershipId,
    string StudentId,
    string FullName,
    string Gender,
    decimal Balance,
    DateOnly JoinedOn);

/// <summary>Sinf ro'yxati sahifasining butun ma'lumoti.</summary>
public record ClassRosterDto(
    string ClassId,
    string ClassName,
    int Grade,
    IReadOnlyList<ClassRosterRowDto> Students);

/// <summary>Sinfsiz o'quvchi — "sinfga qo'shish" tanlovi uchun.</summary>
public record ClassCandidateDto(string StudentId, string FullName, string Gender);

/// <summary>Sinfga o'quvchi qo'shish.</summary>
public record AddClassMemberRequest(string StudentId);

/// <summary>
/// O'quvchini sinfdan chiqarish. Sabab MAJBURIY: sinf a'zoligining tarixi —
/// "nega ketdi" degan savolga javob beradigan yagona joy (§2.2.1).
/// </summary>
public record RemoveClassMemberRequest(string Reason);

/// <summary>
/// O'quvchini boshqa sinfga o'tkazish (§2.2.1 "Transfer").
/// </summary>
/// <param name="KeepGroups">
/// "Guruhlarda qolsin" — sukut bo'yicha <b>true</b> (EduSchool'dagi kabi).
/// false bo'lsa, yangi sinf boqmaydigan guruhlardagi faol a'zoliklar yopiladi.
/// </param>
public record TransferClassMemberRequest(
    string ToClassId,
    bool KeepGroups = true,
    string? Reason = null);

/// <summary>
/// Qo'shish/o'tkazishdan keyingi sig'im ogohlantirishi (C-4, students-parity.md §2.2.3).
/// FAQAT ogohlantirish — amal baribir bajarilgan bo'ladi. Ogohlantirish yo'q bo'lsa
/// controller <c>204 No Content</c> qaytaradi (eski xatti-harakat, sinfda <c>Capacity</c>
/// belgilanmagan bo'lsa — bugungi barcha sinflar shunday).
/// </summary>
public record ClassCapacityWarningDto(string Warning);

/* ---------------------------------------------------------------------------
 *  3. O'quvchi kartochkasi — "Sinf va guruhlar" (G-10)
 * ------------------------------------------------------------------------ */

/// <summary>Kartochkadagi sinf a'zoligi qatori (tarix bilan).</summary>
/// <param name="Days">Shu sinfda o'tgan kunlar soni (yopilmagan bo'lsa — bugungacha).</param>
public record StudentClassMembershipDto(
    Guid Id,
    string ClassId,
    string ClassName,
    int Grade,
    DateOnly JoinedOn,
    DateOnly? LeftOn,
    string? LeaveReason,
    int Days);

/// <summary>Kartochkadagi guruh a'zoligi qatori (tarix bilan).</summary>
public record StudentGroupMembershipDto(
    Guid Id,
    Guid GroupId,
    string GroupName,
    string SubjectId,
    string SubjectName,
    bool GroupIsArchived,
    DateOnly JoinedOn,
    DateOnly? LeftOn,
    string? LeaveReason,
    int Days);

/// <summary>
/// "Sinf va guruhlar" tab'ining butun ma'lumoti.
/// </summary>
/// <param name="ClassName">
/// <c>students.class_name</c> — BUGUNGI haqiqat manbai. A'zolik yozuvlari
/// uning ko'zgusi; ikkalasi bir joyda ko'rsatiladi, chunki eski (backfill
/// qilinmagan) o'quvchida yozuv bo'lmasligi mumkin.
/// </param>
public record StudentMembershipsDto(
    string StudentId,
    string ClassName,
    IReadOnlyList<StudentClassMembershipDto> Classes,
    IReadOnlyList<StudentGroupMembershipDto> Groups);
