namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Arizalar (surveys) — ADMIN CRUD sim shakllari.
//  docs/modules/sales-marketing.md §5.2. Vazifa: SM-3.
// ===========================================================================
//
//  NEGA `SurveyDtos.cs` EMAS
//  -------------------------
//  `SurveyDtos.cs` — SM-2 ning (ommaviy `/api/public/surveys/{slug}`) fayli.
//  Ikki agent bir vaqtda bitta faylga yozsa, birinchi konflikt shu yerda
//  chiqardi. Ikkala fayl ham BITTA namespace'da: tiplar nomi to'qnashmaydi —
//  §5.1 dagi ommaviy tiplar boshqacha nomlanadi (`PublicSurvey...`).
//
//  KLIENT SHARTNOMASI
//  ------------------
//  `schoollms.client/src/api/services/surveys.ts` dagi `SurveySaveRequest` va
//  `Survey` interfeyslari AYNAN shu ikki record'ning nusxasi (maydon nomi ham,
//  null bo'la olishi ham). Bu yerda maydon nomini o'zgartirish — o'sha faylni
//  ham o'zgartirish demakdir.
//
//  NEGA SAQLASH SO'ROVIDA HAMMA MATN `string?`
//  -------------------------------------------
//  `[ApiController]` null bo'lmaydigan `string` xossaga YASHIRIN `[Required]`
//  qo'yadi va uni o'zi, biz ko'rmasdan, `ProblemDetails` 400 bilan rad etadi —
//  ya'ni §5.6 dagi `code: "validation"` va `errors: { maydon: xabar }` klientga
//  YETIB BORMASDI. Shuning uchun tekshiruv to'liq `SurveyService` da, sim
//  shakli esa hamma joyda null qabul qiladi.
// ===========================================================================

/// <summary>
/// Ariza formasi (yaratish ham, tahrirlash ham) — §5.2 <c>SurveySaveRequest</c>.
/// </summary>
/// <param name="Name">Ariza nomi. Bo'sh bo'lmasligi shart (<c>ck_surveys_name</c>).</param>
/// <param name="Slug">Havolaning o'qiladigan qismi. Server uni KICHIK HARFGA
/// keltirib tekshiradi (<c>ux_surveys_slug</c> <c>lower(slug)</c> ustida).</param>
/// <param name="StageId">Lid tushadigan kanban ustuni. <c>null</c> — Rule S
/// 2-qadami: eng kichik <c>Order</c> li ustun (§2.6).</param>
/// <param name="ShowStudentGradeInput">Rule T (§2.4): <c>false</c> bo'lsa
/// 400 <c>survey_fields_required</c>. Bazada — <c>ck_surveys_required_toggles</c>.</param>
/// <param name="ShowStudentGenderInput">Rule T (§2.4): yuqoridagi bilan bir xil.</param>
public record SurveySaveRequest(
    string? Name,
    string? Slug,
    string? Subtitle,
    string? ImageUrl,
    string? OfferUrl,
    string? ThankYouText,
    string? StageId,
    bool ShowStudentFirstNameInput,
    bool ShowStudentLastNameInput,
    bool ShowStudentPhoneNumberInput,
    bool ShowStudentGradeInput,
    bool ShowStudentGenderInput);

/// <summary>
/// Ariza — ro'yxat qatori va forma uchun (§5.2 <c>SurveyDto</c>):
/// so'rov maydonlari + SERVERDA hisoblangan to'rttasi.
/// </summary>
/// <param name="PublicUrl">To'liq ommaviy havola. SERVERDA yig'iladi
/// (<c>Tenancy:RootDomain</c> — apeks domen): admin <c>test.</c> subdomenida
/// o'tiradi, ota-ona esa maktab reklama qiladigan domenni ochadi. Klient uni
/// hech qachon o'zi yasamaydi.</param>
/// <param name="SubmissionCount">Shu arizaga topshirilgan HAMMA so'rov soni
/// (takrorlari bilan). O'chirish tugmasi shu songa qarab so'nadi (D7).</param>
/// <param name="LeadCount">Shulardan nechtasi lid yaratgan
/// (<c>status = 'lead'</c>). Qolgani — takror (§2.6 4-qadam).</param>
/// <param name="LastSubmissionAt">Oxirgi ariza vaqti; <c>null</c> — hali
/// birorta topshiriq yo'q.</param>
public record SurveyDto(
    Guid Id,
    string Name,
    string Slug,
    string? Subtitle,
    string? ImageUrl,
    string? OfferUrl,
    string? ThankYouText,
    string? StageId,
    bool ShowStudentFirstNameInput,
    bool ShowStudentLastNameInput,
    bool ShowStudentPhoneNumberInput,
    bool ShowStudentGradeInput,
    bool ShowStudentGenderInput,
    bool IsActive,
    string PublicUrl,
    int SubmissionCount,
    int LeadCount,
    DateTimeOffset? LastSubmissionAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// <c>PATCH /api/admin/surveys/{id}/active</c> tanasi. Yopiq arizaning ommaviy
/// sahifasi 404 beradi (D8) — o'chirilgan forma emas.
/// </summary>
public record SurveyActiveRequest(bool IsActive);

/// <summary>
/// <c>GET /api/admin/surveys/slug-available</c> javobi. Yozayotganda
/// chaqiriladi; HAQIQIY hakam baribir <c>ux_surveys_slug</c>, poygada
/// saqlash 409 <c>slug_taken</c> qaytaradi.
/// </summary>
public record SlugAvailabilityDto(bool Available);
