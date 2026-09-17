namespace SchoolLms.Application.Dtos;

/* =========================================================================
 *  O'quvchi kartochkasi (profil) — docs/modules/students-parity.md §2.3
 *  (S-10, S-12, L-1).
 *
 *  Bu yerdagi tiplar FAQAT kartochka ekrani uchun. Ro'yxat qatori
 *  (`StudentListRowDto`) va shaxsiy daftar (`StudentNotebookDto`) tegilmadi:
 *  ular boshqa slice'larga tegishli va ularning shakli o'zgarmaydi.
 *
 *  PUL BU YERDA YO'Q. Kartochkadagi balans va hisob-kitob moliya rolining
 *  orqasida (`/api/student/billing`, `{id}/ledger`) qoladi — shuning uchun
 *  <see cref="StudentCardDto.Student"/> ichidagi `Balance` HAR DOIM null
 *  bo'lib qaytadi (davomat ro'yxatidagi bilan bir xil qoida).
 * ========================================================================= */

/// <summary>
/// Kartochkaning chap paneli: kim, qayerda, qanday holatda (§2.3.1).
/// </summary>
/// <param name="Student">
/// Ro'yxatdagi bilan AYNAN bir xil shakl — kartochkadagi "Tahrirlash" va
/// "Arxivlash" oynalari o'quvchi ro'yxatidagi oynalarning o'zi, ya'ni ikkinchi
/// forma yozilmaydi. `Balance` bu yerda har doim null.
/// </param>
/// <param name="ActiveDays">
/// Maktabda necha kun — qabul sanasidan bugungacha, arxivlangan bo'lsa arxiv
/// sanasigacha. Sinf va guruh kesimidagi to'liq tarix "Sinf va guruhlar"
/// tab'ida (G-10).
/// </param>
public record StudentCardDto(
    StudentDto Student,
    string? Phone,
    string? Language,
    string? DocumentUrl,
    Guid? StatusId,
    string? StatusName,
    string? StatusColor,
    string? Login,
    string HomeroomTeacher,
    double? Latitude,
    double? Longitude,
    string? LocationAddress,
    string? LocationUpdatedAt,
    int ActiveDays);

/// <summary>
/// Kartochkaning "Jadval" tab'i — bitta haftaning darslari.
/// Manba <c>PupilTimetable</c>: sinf darslari + FAOL GURUH darslari.
/// </summary>
/// <param name="WeekCount">Chorakdagi haftalar soni — oldinga/orqaga yurish uchun.</param>
public record StudentTimetableDto(
    int Quarter,
    int Week,
    int WeekCount,
    string? WeekStart,
    string? WeekEnd,
    IReadOnlyList<StudentLessonDto> Lessons);

/// <summary>Davomat oralig'i hisobotining bitta fan qatori.</summary>
/// <param name="Planned">O'tilgan darslar (jurnalda "o'tildi" belgisi bor).</param>
/// <param name="Late">Kech kelgan darslar — yo'qlik sifatida sanalmaydi.</param>
public record StudentAttendanceSubjectDto(
    string SubjectId,
    string SubjectName,
    int Planned,
    int Attended,
    int Absent,
    int Late,
    int Pct);

/// <summary>
/// Bitta kun — oy kalendari uchun. <paramref name="Planned"/> o'sha kuni
/// o'tilgan darslar soni; nol bo'lsa kalendarda kun "bo'sh" ko'rinadi.
/// </summary>
public record StudentAttendanceDayDto(
    string Date,
    int Planned,
    int Absent,
    int Late);

/// <summary>
/// "Davomat" tab'idagi oraliq hisobot (§2.3.1 — attendance range report).
///
/// <para>
/// <b>"Sababli / sababsiz" ustuni YO'Q.</b> <c>absence_reasons</c> jadvalida
/// bunday ustun yo'q (faqat <c>is_late</c> va intizomiy ball), shuning uchun
/// yo'qliklar SABAB kesimida beriladi — bu bor ma'lumotning o'zi, to'qilgani
/// emas.
/// </para>
/// </summary>
public record StudentAttendanceRangeDto(
    string From,
    string To,
    int Planned,
    int Attended,
    int Absent,
    int Late,
    int Pct,
    IReadOnlyList<StudentAttendanceSubjectDto> Subjects,
    IReadOnlyList<AttendanceReasonCountDto> Reasons,
    IReadOnlyList<StudentAttendanceDayDto> Days);

/// <summary>
/// O'quvchining uy joylashuvini xodim qo'lda qo'yadi (L-1).
/// Uchala maydon ham null bo'lsa — joylashuv tozalanadi.
/// </summary>
public record SaveStudentLocationRequest(
    double? Latitude,
    double? Longitude,
    string? Address);
