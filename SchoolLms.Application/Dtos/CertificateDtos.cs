namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Sertifikatlar registri — docs/modules/existing-module-gaps.md §2.3.
// ===========================================================================
//
//  Alohida faylda (Dtos.cs ni shishirmaslik uchun). JSON standart sozlama bilan
//  camelCase'ga aylanadi; frontend tiplari `src/api/services/certificates.ts`
//  da AYNAN shu tartibda ko'chirilgan.
//
//  SANALAR — MATN ("yyyy-MM-dd"), `DateOnly` emas. Sabab: butun loyihada
//  sana simi shunday (Student.BirthDate, AttendanceDisciplineReport, ...) va
//  <input type="date"> ham aynan shu formatni beradi/kutadi. Bazada esa
//  ustun haqiqiy `date` — matn faqat SIM ustida.
// ===========================================================================

/// <summary>
/// Sertifikat turi — ro'yxat qatori.
/// </summary>
/// <param name="IsScored">Standart test (IELTS/SAT) — sertifikatda BALL bo'ladi.</param>
/// <param name="IsActive">false = yangi sertifikatda tanlanmaydi (eskilar joyida qoladi).</param>
/// <param name="CertificateCount">Shu turda nechta hujjat bor. SERVERDA sanaladi —
/// "o'chirib bo'lmaydi" tugmasini brauzer o'zi o'ylab topmasin.</param>
public record CertificateTypeDto(
    Guid Id, string Name, bool IsScored, bool IsActive, int CertificateCount);

/// <summary>Tur formasi.</summary>
public record CertificateTypePayload(string Name, bool IsScored, bool IsActive);

/// <summary>
/// Bitta sertifikat — ro'yxat va kartochka uchun.
///
/// <para>
/// O'quvchi/fan/o'qituvchi NOMI ham qo'shiladi: ro'yxat bitta so'rovda to'liq
/// ko'rinishi kerak, brauzer keyin yana uchta lug'at yuklab o'tirmasin.
/// </para>
/// </summary>
/// <param name="TypeIsScored">Turning bayrog'i — forma "Ball" maydonini shu bo'yicha ochadi.</param>
/// <param name="IsExpired">Muddati bugunga nisbatan o'tganmi. SERVERDA hisoblanadi
/// (maktab vaqti bilan, AppClock) — brauzerning soati boshqa mintaqada bo'lishi mumkin.</param>
public record CertificateDto(
    Guid Id,
    string StudentId, string StudentName, string ClassName,
    Guid TypeId, string TypeName, bool TypeIsScored,
    string? SubjectId, string? SubjectName,
    string? TeacherId, string? TeacherName,
    string? Number, decimal? Score,
    string IssuedOn, string? ExpiresOn, bool IsExpired,
    string? FileUrl, string? Comment);

/// <summary>
/// Sertifikat formasi. <see cref="Score"/> FAQAT turi <c>IsScored</c> bo'lganda
/// qabul qilinadi — bazada bu bog'liqlik tekshirilmaydi (ikki jadvalga tegishli),
/// uni <c>CertificateService</c> tekshiradi va o'qiladigan xato qaytaradi.
/// </summary>
public record CertificatePayload(
    string StudentId, Guid TypeId, string? SubjectId, string? TeacherId,
    string? Number, decimal? Score, string IssuedOn, string? ExpiresOn,
    string? FileUrl, string? Comment);

/// <summary>
/// "Natijalar" tab'ining bitta qatori — bitta o'quvchi, bitta ballik tur.
/// </summary>
/// <param name="BestScore">Eng yuqori ball (IELTS'ni ikki marta topshirgan bola bor).</param>
/// <param name="LatestScore">Eng oxirgi berilgan sertifikatdagi ball.</param>
/// <param name="LatestIssuedOn">O'sha oxirgi sertifikat sanasi.</param>
/// <param name="Count">Shu turdagi hujjatlar soni.</param>
public record CertificateResultRowDto(
    string StudentId, string StudentName, string ClassName,
    decimal? BestScore, decimal? LatestScore, string LatestIssuedOn, int Count);

/// <summary>
/// "Natijalar" tab'i — bitta ballik tur bo'yicha o'quvchilar jadvali (§2.3).
/// <c>is_scored</c> ustuni AYNAN shuning uchun bor.
///
/// <para>
/// Yig'ma raqamlar (o'rtacha/eng yuqori/eng past) SERVERDA hisoblanadi: sahifalash
/// qo'shilganda brauzerdagi hisob yolg'on gapira boshlardi.
/// </para>
/// </summary>
public record CertificateResultsDto(
    Guid TypeId, string TypeName, int StudentCount, int CertificateCount,
    decimal? AverageScore, decimal? MaxScore, decimal? MinScore,
    List<CertificateResultRowDto> Rows);
