namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  O'quvchi shartnomalari reyestri — docs/modules/students-parity.md §2.10
//  (K-1, K-2, K-3).
//
//  UCHTA "SHARTNOMA" NI ARALASHTIRMANG (§2.10.1):
//    1. ANDOZA        — `contract_templates` (Word fayl). TEGILMAGAN.
//    2. YUBORILGANI   — `contracts` (kimga, qachon, qaysi raqam bilan
//                       Telegram orqali ketdi). TEGILMAGAN.
//    3. O'QUVCHINIKI  — `student_contracts`: shu bola bilan tuzilgan
//                       shartnomaning O'ZI. Shu fayl ana shu uchinchisi haqida.
//
//  PUL YO'Q. Summa, to'lov turi, to'lov kuni — hech biri bu yerda emas:
//  ular `student_subscriptions` va `invoices` da (SPEC §3.7) va faqat
//  o'sha yerda. Shartnoma yozuvi — HUJJAT: raqam, sana, fayl, holat.
// ===========================================================================

/// <summary>
/// Reyestr filtri — query string'dan bog'lanadi (<c>[FromQuery]</c>).
/// Barchasi ixtiyoriy; bo'sh filtr butun reyestrni beradi.
/// </summary>
public sealed class StudentContractFilter
{
    /// <summary>O'quvchi F.I.SH yoki shartnoma raqami bo'yicha (registrga bog'liq emas).</summary>
    public string? Search { get; set; }

    /// <summary>Bitta o'quvchining tarixi (kartochkadagi tab).</summary>
    public string? StudentId { get; set; }

    /// <summary>Sinf nomi (aniq moslik).</summary>
    public string? ClassName { get; set; }

    /// <summary><c>generated</c> | <c>uploaded</c>.</summary>
    public string? Source { get; set; }

    /// <summary>true = fayli bor, false = fayli yo'q.</summary>
    public bool? HasFile { get; set; }

    /// <summary><c>draft</c> | <c>active</c> | <c>expired</c> — <see cref="StudentContractDto.Status"/> ga qarang.</summary>
    public string? Status { get; set; }

    /// <summary>Imzo sanasi shu kundan boshlab (ISO "yyyy-MM-dd").</summary>
    public string? From { get; set; }

    /// <summary>Imzo sanasi shu kungacha (ISO "yyyy-MM-dd").</summary>
    public string? To { get; set; }

    /// <summary>1 dan boshlanadi (sukut 1).</summary>
    public int? Page { get; set; }

    /// <summary>Sahifadagi qatorlar soni (1..500, sukut 50).</summary>
    public int? PageSize { get; set; }
}

/// <summary>Reyestrdagi bitta qator.</summary>
/// <param name="Status">
/// HISOBLANADI, saqlanmaydi (jadvalda bunday ustun YO'Q):
/// <list type="bullet">
///   <item><c>draft</c> — raqami yo'q, ya'ni hali rasmiylashtirilmagan;</item>
///   <item><c>expired</c> — tugash sanasi o'tgan;</item>
///   <item><c>active</c> — qolgan hammasi.</item>
/// </list>
/// </param>
public record StudentContractDto(
    Guid Id,
    string StudentId,
    string StudentName,
    string ClassName,
    string? TemplateId,
    string? TemplateName,
    string? Number,
    string? SignedOn,
    string? EndsOn,
    string? FileUrl,
    string Source,
    string Status,
    string? Comment,
    string CreatedBy,
    string? CreatedByName,
    string CreatedAt);

/// <summary>Reyestrning bitta sahifasi.</summary>
public record StudentContractPageDto(
    IReadOnlyList<StudentContractDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Qo'lda yozuv qo'shish/tahrirlash (K-1) va imzolangan nusxani biriktirish (K-3).
/// Fayl AVVAL <c>POST /api/admin/uploads</c> orqali yuklanadi va bu yerga faqat
/// uning manzili (<paramref name="FileUrl"/>) keladi — ikkinchi yuklash yo'li yo'q.
/// </summary>
public record SaveStudentContractRequest(
    string? StudentId,
    string? TemplateId,
    string? Number,
    string? SignedOn,
    string? EndsOn,
    string? FileUrl,
    string? Source,
    string? Comment);

/// <summary>
/// Bitta o'quvchi uchun Word andozadan shartnoma hosil qilish (K-2).
/// Raqam berilmasa — avtomatik (keyingi bo'sh raqam).
/// </summary>
public record GenerateStudentContractRequest(
    string? StudentId,
    string? TemplateId,
    string? Number,
    string? SignedOn,
    string? EndsOn,
    string? Comment);

/// <summary>
/// Hosil qilishdan OLDIN ko'rsatiladigan ma'lumot (K-2 "review-and-edit"):
/// andozaga qanday qiymatlar tushishi va keyingi bo'sh raqam.
/// </summary>
public record StudentContractPreviewDto(
    string StudentId,
    string StudentName,
    string ClassName,
    string NextNumber,
    string Today,
    IReadOnlyList<ContractTokenDto> Tokens);

/// <summary>Andozadagi bitta <c>@</c>-o'rinbosar va uning qiymati.</summary>
public record ContractTokenDto(string Token, string Value);
