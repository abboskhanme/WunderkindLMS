namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Telegram Mini App DTO'lari — SPEC §6 Faza 3.
// ===========================================================================
//
//  Ikkita frontend agenti (`schoollms.client/src/pages/miniapp/`) shu
//  shakllarga tayanib PARALLEL yozadi. To'liq shartnoma jadvali —
//  docs/PENDING_WIRING.md, "Telegram Mini App" bo'limi.
//
//  QOIDA: MAVJUD DTO QAYTA ISHLATILADI. Davomat (`StudentAttendanceFullDto`),
//  baholar (`StudentReportDto`), jadval (`StudentLessonDto`), oshxona
//  (`DayMenuDto`), pickup (`PickupRequestDto`), e'lon (`BroadcastDto`),
//  maosh (`SalaryLedgerDto`) — hammasi Dtos.cs dagi mavjud shakllar.
//  Bu yerda faqat Mini App'ga XOS bo'lgan yangi shakllar bor: kirish,
//  bog'lanish, farzand almashtirgich va ikkita jamlama ekran.
// ===========================================================================

/* ---------- Kirish va bog'lanish ---------- */

/// <summary>`POST /api/tg/auth` — Telegram sahifaga bergan XOM `initData` satri.</summary>
public record TgAuthRequest(string InitData);

/// <summary>
/// `POST /api/tg/link` — maktab bergan bir martalik kod.
///
/// <para>
/// <c>InitData</c> IXTIYORIY. Mini App uni yubormasa, server kimligini
/// <c>/api/tg/auth</c> qoldirgan qisqa muddatli "bog'lash chiptasi"dan
/// (HttpOnly cookie) oladi — <c>TelegramAuthController</c> izohiga qarang.
/// Ikkalasi ham bo'lmasa 401: kodning o'zi kimligini isbotlamaydi.
/// </para>
/// </summary>
public record TgLinkRequest(string Code, string? InitData = null);

/// <summary>
/// `POST /api/tg/link-login` — birinchi marta login va parol bilan kirish (mijoz, 2026-09-25). Telegram akkaunti
/// shu foydalanuvchiga bog'lanadi, keyingi ochilishlarda parol so'ralmaydi.
/// </summary>
public record TgLoginLinkRequest(string Login, string Password, string? InitData = null);

/// <summary>Imzosi tekshirilgan Telegram foydalanuvchisi (bog'lash ekranida ko'rsatiladi).</summary>
public record TgTelegramUserDto(string Id, string DisplayName, string? Username);

/// <summary>
/// Mini App kirishining MUVAFFAQIYATLI javobi (HTTP 200).
///
/// <para>
/// <c>Token</c> va <c>User</c> — <c>POST /api/auth/login</c> beradigan shaklning
/// AYNAN o'zi, shuning uchun Mini App tokeni bilan mavjud barcha endpointlar
/// hech qanday o'zgarishsiz ishlaydi. <c>Status</c> doim <c>"ok"</c>: boshqa
/// holatlar HTTP kodi bilan ajratiladi (401 — imzo yaroqsiz, 409 — bog'lanmagan).
/// </para>
/// </summary>
public record TgAuthResponse(
    string Status,
    string? Token,
    UserDto? User,
    TgTelegramUserDto Telegram,
    string? Message);

/// <summary>
/// `POST /api/tg/auth` — imzo TO'G'RI, lekin bu Telegram akkaunti hech kimga
/// bog'lanmagan (HTTP <b>409</b>).
///
/// <para>
/// 401 EMAS: 401 "kimligingni bilmadim" degani, bu yerda esa kim ekani aniq —
/// Telegram uni imzolab tasdiqladi, faqat maktab uni hali tanimaydi. Ikki holat
/// mijozda butunlay boshqa ekran: "qaytadan urinib ko'ring" va "maktabdan kod
/// oling". <c>Code</c> doim <c>"not_linked"</c>.
/// </para>
/// </summary>
public record TgNotLinkedDto(string Code, string Message, TgTelegramUserDto Telegram);

/// <summary>Admin panel: bir martalik kod chiqarish so'rovi.</summary>
public record IssueLinkCodeRequest(string UserId);

/// <summary>Chiqarilgan kod — ochiq matni FAQAT shu javobda, bir marta.</summary>
public record LinkCodeDto(string Code, string UserId, string UserFullName, string Role, string ExpiresAt);

/// <summary>
/// Admin panel: Telegram id ALLAQACHON ma'lum bo'lganda to'g'ridan-to'g'ri bog'lash
/// (bot `request_contact` orqali chat id'ni bilib olgan holat va demo urug'i).
/// </summary>
public record LinkTelegramRequest(string UserId, long TelegramUserId, string? DisplayName, string? Username);

/// <summary>Admin panel: mavjud Telegram bog'lanishi.</summary>
public record TelegramLinkDto(
    string TelegramUserId, string DisplayName, string? Username,
    string UserId, string UserFullName, string Role,
    string LinkedAt, string LastSeenAt);

/* ---------- Admin: vasiylar (SPEC §3.2) ---------- */

/// <summary>Admin ro'yxatidagi vasiyning bitta farzandi.</summary>
public record GuardianChildDto(
    string StudentId, string FullName, string ClassName, string Relation, bool IsPrimary);

/// <summary>
/// Admin "Vasiylar" ro'yxatidagi bitta qator.
/// </summary>
/// <param name="Login">Tizim akkaunti logini (akkaunt yo'q bo'lsa null).</param>
/// <param name="TelegramLinked">Telegram Mini App'ga bog'langanmi.</param>
public record GuardianDto(
    string Id, string FullName, string Phone, string? PassportUrl,
    string? UserId, string? Login, bool TelegramLinked,
    List<GuardianChildDto> Children);

/// <summary>Vasiy yaratish/tahrirlash.</summary>
public record SaveGuardianRequest(string FullName, string Phone, string? PassportUrl);

/// <summary>Vasiyga farzand biriktirish.</summary>
/// <param name="Relation">parent | grandparent | trustee.</param>
/// <param name="IsPrimary">Asosiy vasiy — o'quvchida bittadan ortiq bo'la olmaydi.</param>
public record AttachChildRequest(string StudentId, string? Relation, bool IsPrimary);

/// <summary>Vasiyga tizim akkaunti (rol = <c>parent</c>) ochish. Parol berilmasa avtomatik yaratiladi.</summary>
public record CreateGuardianAccountRequest(string? NewPassword);

/* ---------- Umumiy: men kimman ---------- */

/// <summary>Farzand almashtirgichdagi bitta karta.</summary>
/// <param name="Debt">Qarz (musbat son). 0 = qarzsiz. Avans bu yerda ko'rsatilmaydi.</param>
public record TgChildDto(
    string StudentId, string FullName, string ClassName, string? PhotoUrl,
    string Relation, bool IsPrimary, decimal Debt);

/// <summary>
/// Mini App qobig'ining birinchi chaqiruvi: men kimman, nimani ko'raman.
/// <c>Children</c> faqat <c>parent</c> uchun, <c>Teacher</c> faqat <c>teacher</c> uchun to'ladi.
/// </summary>
/// <param name="ShowLearningProgress">
/// Shu foydalanuvchi o'zlashtirishni (baholarni) ko'radimi — §5.5
/// <c>show_learning_progress_in_parent_dashboard</c>. Faqat <c>parent</c> uchun
/// <c>false</c> bo'lishi mumkin; o'quvchi va o'qituvchi uchun har doim <c>true</c>.
/// Qobiq shunga qarab "Baholar" bo'limini KO'RSATMAYDI — server tarafdagi qulf esa
/// <c>StudentPortalController</c> / <c>TelegramParentController</c> da, chunki faqat
/// ekranda yashirish yolg'on bo'lardi.
/// </param>
public record TgProfileDto(
    string UserId, string FullName, string Role, string SchoolName,
    TgTelegramUserDto Telegram,
    List<TgChildDto> Children,
    TeacherProfileDto? Teacher,
    bool ShowLearningProgress = true);

/* ---------- Ota-ona ekranlari ---------- */

/// <summary>
/// Farzand bosh sahifasi — BITTA chaqiruvda: bugungi darslar, bugungi baholar,
/// joriy chorak davomati, qarz va kutilayotgan pickup.
///
/// <para>
/// <b>Qatnashish FOIZI ataylab yo'q.</b> Uni hisoblash qoidasi
/// (<c>lesson_notes.conducted</c> × guruh × sabab turi) allaqachon ikki joyda
/// yozilgan; uchinchi nusxa bu ekranda boshqacha raqam ko'rsatish xavfini
/// tug'dirardi. To'liq davomat —
/// <c>GET /api/tg/parent/children/{id}/attendance</c>.
/// </para>
/// </summary>
/// <param name="Debt">Qarz (musbat son), 0 = qarzsiz.</param>
/// <param name="Credit">Taqsimlanmagan avans.</param>
/// <param name="RecentAnnouncements">Oxirgi 14 kundagi e'lonlar soni (o'qilgan/o'qilmagan emas).</param>
public record TgChildOverviewDto(
    TgChildDto Child, PortalMetaDto Meta,
    List<StudentLessonDto> TodayLessons,
    List<HomeworkItemDto> TodayGrades,
    int QuarterMissedDays, int QuarterMissedLessons, int QuarterLateCount,
    decimal Debt, decimal Credit,
    PickupRequestDto? Pickup,
    int RecentAnnouncements);

/* ---------- O'qituvchi ekranlari ---------- */

/// <summary>O'qituvchining bugungi bosh sahifasi.</summary>
public record TgTeacherTodayDto(
    string Date, int Quarter, int Week,
    List<TeacherLessonDto> Lessons,
    List<PickupRequestDto> Pickups,
    int UnreadMessages);

/// <summary>
/// Bir tegishlik yo'qlama uchun ro'yxatdagi bitta o'quvchi: joriy holati bilan
/// (sabab qo'yilgan bo'lsa — o'sha, baho qo'yilgan bo'lsa — o'sha).
/// </summary>
public record TgRosterStudentDto(
    string StudentId, string FullName, int SubGroup,
    string? ReasonId, string? ReasonName, bool IsLate, int? Grade);

/// <summary>
/// Bir dars uchun to'liq yo'qlama ekrani: ega (sinf yoki o'quv guruhi), fan,
/// sana, dars raqami, ruxsat etilgan sabablar va o'quvchilar.
/// </summary>
/// <param name="OwnerKind">
/// <c>class</c> | <c>group</c> (<c>LessonOwnerKind</c>; students-parity.md
/// §2.1.4). Guruh darsida <paramref name="ClassId"/> guruh id'sini,
/// <paramref name="ClassName"/> esa guruh nomini saqlaydi. Oxirida turibdi va
/// sukuti <c>class</c> — eski mijoz kodi buzilmaydi.
/// </param>
public record TgRosterDto(
    string ClassId, string ClassName, string SubjectId, string SubjectName,
    string Date, int Period, int Quarter, bool Conducted, string? Topic, string? Homework,
    List<AbsenceReasonDto> Reasons,
    List<TgRosterStudentDto> Students,
    string OwnerKind = SchoolLms.Domain.LessonOwnerKind.Class);

/// <summary>O'qituvchi yaqinda yozgan bitta jurnal yozuvi.</summary>
public record TgJournalRecentDto(
    string Date, int Period, string ClassId, string ClassName,
    string SubjectId, string SubjectName,
    string StudentId, string StudentName,
    int? Grade, string? ReasonName, bool IsLate);

/// <summary>Bitta chat kanalidagi o'qilmagan xabarlar.</summary>
/// <param name="Channel">Sinf nomi yoki <c>__xodimlar__</c>.</param>
public record TgChatUnreadDto(string Channel, int Unread, string? LastMessageAt, string? LastSender);
