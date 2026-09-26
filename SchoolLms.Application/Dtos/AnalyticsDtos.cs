namespace SchoolLms.Application.Dtos;

// Analitika hisobotlari — davomat roll-up'i va fanlar bo'yicha o'zlashtirish pivoti.
// Ikkalasi ham BOR ma'lumot ustidagi hisobot: yangi jadval ham, yangi ustun ham yo'q.
// JSON camelCase'ga ASP.NET Core standart sozlamasi orqali aylantiriladi.

/* ================= Davomat analitikasi ================= */

/// <summary>
/// Bitta kesim (butun davr / sinf / dars soati / kun) bo'yicha davomat yig'indisi.
///
/// <para>
/// <b>Maxraj — O'TILGAN darslar.</b> Dars o'tilmagan bo'lsa (<c>LessonNote.Conducted = false</c>)
/// unda tekshiriladigan hech narsa yo'q, shuning uchun u hisobga umuman kirmaydi. Bu
/// <c>Analytics.BuildClass</c> va bosh sahifadagi davomat foizi bilan BIR XIL ta'rif.
/// </para>
///
/// <para>
/// <b><c>Unchecked</c> hech qachon <c>Present</c> ga qo'shilmaydi.</b> Davomati
/// belgilanmagan o'quvchini "keldi" deb hisoblash — eng oson va eng zararli xato:
/// direktor 100% ko'rib, aslida o'qituvchi jurnalni ochmaganini bilmay qolardi.
/// Shu sababli <c>Present + Absent + Unchecked = Opportunities</c>.
/// </para>
///
/// <para>
/// <b><c>Late</c> — <c>Present</c> ning ICHIDA.</b> "Kech keldi"
/// (<c>AbsenceReason.IsLate</c>) o'quvchi darsda QATNASHGANini bildiradi, shuning uchun u
/// yo'qlik emas; alohida son sifatida ko'rsatiladi, lekin yig'indini buzmaydi.
/// </para>
/// </summary>
/// <param name="Lessons">Kesimga tushgan o'tilgan dars kataklari soni.</param>
/// <param name="Opportunities">O'tilgan dars × shu darsga tegishli o'quvchi (maxraj).</param>
/// <param name="Present">Belgilangan va yo'qlik sababi qo'yilmagan (kech kelganlar ham shu yerda).</param>
/// <param name="Absent">Sababli + sababsiz yo'qliklar (kech keldi BUNGA KIRMAYDI).</param>
/// <param name="Excused">Sababli yo'qliklar.</param>
/// <param name="Unexcused">Sababsiz yo'qliklar.</param>
/// <param name="Late">Kech kelganlar (<c>Present</c> ichidan).</param>
/// <param name="Unchecked">Dars o'tilgan, lekin o'quvchi davomati umuman belgilanmagan.</param>
public record AttendanceTallyDto(
    int Lessons,
    int Opportunities,
    int Present,
    int Absent,
    int Excused,
    int Unexcused,
    int Late,
    int Unchecked,
    double? PresentPct,
    double? AbsentPct,
    double? UncheckedPct);

/// <summary>Sinf kesimidagi davomat qatori.</summary>
/// <param name="Students">Sinfdagi (arxivlanmagan) o'quvchilar soni.</param>
/// <param name="OwnerKind">
/// <c>class</c> yoki <c>group</c> — yo'nalish guruhi o'zini boqadigan sinflar (9–11) o'rnida
/// alohida qator bo'lib chiqadi (docs/modules/track-groups-as-classes.md).
/// </param>
public record AttendanceClassRowDto(
    string ClassId, string ClassName, int Grade, int Students, AttendanceTallyDto Tally,
    string OwnerKind = "class");

/// <summary>Tanlangan KUNning bitta dars soati kesimidagi davomat.</summary>
/// <param name="StartTime">Dars vaqti "HH:mm" (sozlamada belgilanmagan bo'lsa — null).</param>
public record AttendancePeriodRowDto(
    int Period, string? StartTime, string? EndTime, AttendanceTallyDto Tally);

/// <summary>Davr ichidagi bitta kun (grafik uchun).</summary>
public record AttendanceTrendPointDto(string Date, AttendanceTallyDto Tally);

/// <summary>Sabab bo'yicha taqsimot — jurnalda qo'yilgan belgilar soni.</summary>
/// <param name="Unexcused">Shu sabab sababsiz yo'qlik deb hisoblanadimi.</param>
public record AttendanceReasonRowDto(
    string ReasonId, string Name, string Short, bool IsLate, bool Unexcused, int Count);

/// <summary>
/// Davomat analitikasi — zavuch har kuni ertalab ochadigan roll-up ekrani.
/// </summary>
/// <param name="ClassId">Tanlangan sinf; bo'sh bo'lsa — butun maktab.</param>
/// <param name="Day">Dars soatlari kesimi ko'rsatilayotgan kun (davr ichida).</param>
/// <param name="StudentsTotal">Tanlangan sinf(lar)dagi arxivlanmagan o'quvchilar soni.</param>
public record AttendanceAnalyticsDto(
    string From,
    string To,
    string? ClassId,
    string Day,
    int StudentsTotal,
    AttendanceTallyDto Total,
    List<AttendanceClassRowDto> Classes,
    List<AttendancePeriodRowDto> Periods,
    List<AttendanceTrendPointDto> Trend,
    List<AttendanceReasonRowDto> Reasons);

/* ================= O'zlashtirish (fanlar bo'yicha) ================= */

/// <summary>
/// Pivotning bitta katagi: sinf × fan, tanlangan choraklar bo'yicha.
/// </summary>
/// <param name="Values">Katakka tushgan (o'quvchi × chorak) baho qiymatlari soni.</param>
/// <param name="Average">O'rtacha baho; qiymat bo'lmasa — null (nol EMAS: "baho yo'q" ≠ "0").</param>
/// <param name="QualityPct">Sifat ko'rsatkichi — 4 va 5 baholarning ulushi (%).</param>
/// <param name="ByQuarter">Chorak → o'rtacha baho (faqat qiymati bor choraklar).</param>
public record SubjectAttainmentCellDto(
    string SubjectId,
    int Values,
    double? Average,
    double QualityPct,
    Dictionary<int, double> ByQuarter);

/// <summary>
/// Pivotning bitta qatori. <c>Cells</c> hisobotdagi <c>Subjects</c> ro'yxati bilan BIR XIL
/// tartibda va BIR XIL uzunlikda — sinf o'tmaydigan fan katagi ham bor (Values = 0).
/// </summary>
/// <param name="Kind">"class" — sinf qatori; "school" — maktab o'rtachasi qatori.</param>
public record SubjectAttainmentRowDto(
    string Kind,
    string ClassId,
    string ClassName,
    int Grade,
    int Students,
    List<SubjectAttainmentCellDto> Cells,
    double? Average,
    double QualityPct);

/// <summary>
/// O'zlashtirish (fanlar bo'yicha) — sinf × fan pivoti, chorak kesimi bilan.
/// Mavjud "Baholar hisoboti" oilasining davomi: baho manbai va rasmiy chorak bahosining
/// ustunligi <c>grades-report/class</c> bilan bir xil.
/// </summary>
public record SubjectAttainmentReportDto(
    List<int> Quarters,
    List<SubjectDto> Subjects,
    List<SubjectAttainmentRowDto> Rows,
    SubjectAttainmentRowDto School);
