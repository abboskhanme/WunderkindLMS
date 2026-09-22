namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Ommaviy ariza formasi (`/ariza/{slug}`) — docs/modules/sales-marketing.md
//  §5.1 ning SHARTNOMASI. Vazifa: SM-2.
//
//  BU FAYLDA FAQAT OMMAVIY (ANONIM) SHARTNOMA TURADI
//  -------------------------------------------------
//  Admin ekranlarining DTO'lari — `SurveyDto`, `SurveySaveRequest`,
//  `SubmissionDto`, `NewsAdminDto` (§5.2–§5.5) — BOSHQA fayllarda: ularni
//  shu to'lqinda parallel ishlayotgan agentlar yozadi (§7.2). Shuning uchun
//  bu yerdagi HAR BIR tur `PublicSurvey` prefiksi bilan nomlangan: bitta
//  `SchoolLms.Application.Dtos` nomlar fazosida ikki agent bir xil nom
//  qo'ysa, loyiha kompilyatsiya bo'lmaydi.
//
//  NOMLAR AYNAN KLIENTNIKI
//  -----------------------
//  `schoollms.client/src/api/services/publicSurvey.ts` shu shakllarni
//  TypeScript'da takrorlaydi. Maydon nomini o'zgartirish — ommaviy sahifani
//  JIMGINA buzish demak: sahifa sessiyasiz ishlaydi va xatoni faqat ota-ona
//  ko'radi, hech bir admin ekrani emas. JSON camelCase bo'lib chiqadi —
//  ASP.NET ning standart web sozlamasi (`Program.cs` da JSON siyosati
//  o'zgartirilmagan).
//
//  XATO TANALARI BU YERDA YO'Q — ATAYLAB
//  --------------------------------------
//  `{ code, message }` javoblari kontrollerda anonim obyekt bilan yoziladi:
//  `TelegramAuthController` va `AdminTelegramController` shu naqshda
//  (`new { code = "...", message = "..." }`). Kodlar esa qattiq matn emas —
//  `SurveySubmissionService` dagi konstantalar (§5.6).
// ===========================================================================

/// <summary>
/// Ommaviy sahifa QAYSI o'quvchi maydonlarini chizishi (§5.1 `fields`, §2.4).
/// Ota-onaning uchta maydoni (ism, familiya, telefon) bu yerda YO'Q: ular
/// doim chiziladi va doim majburiy, ya'ni ularni sozlab bo'lmaydi.
/// </summary>
/// <param name="StudentGrade">Rule T bo'yicha DOIM <c>true</c> — <c>leads.target_grade</c> not null.</param>
/// <param name="StudentGender">Rule T bo'yicha DOIM <c>true</c> — <c>leads.gender</c> not null va uni muzlatilgan doska chizadi.</param>
public record PublicSurveyFieldsDto(
    bool StudentFirstName,
    bool StudentLastName,
    bool StudentPhone,
    bool StudentGrade,
    bool StudentGender);

/// <summary>
/// <c>GET /api/public/surveys/{slug}</c> javobi — sahifa chizadigan HAMMA
/// narsa shu yerda. Sahifa boshqa birorta so'rov yubormaydi (§6.1).
///
/// <para>
/// Bu yerda tizimdagi MAVJUD birorta yozuv (lid, o'quvchi, xodim) haqida
/// ma'lumot yo'q va id ham yo'q — anonim endpoint faqat shu arizaning o'z
/// matnini qaytaradi (§5.1).
/// </para>
/// </summary>
/// <param name="ThankYouText"><c>null</c> — sahifa o'zining standart matnini ko'rsatadi.</param>
/// <param name="ServedAt">
/// Server soati. Sahifa uni topshiriqda QAYTA yuboradi (§2.5 vaqt tuzog'i).
/// Bu — bot filtri, xavfsizlik chegarasi EMAS: qiymat imzolanmagan va uni
/// soxtalashtirish oson. Haqiqiy himoya — chastota chegarasi.
/// </param>
public record PublicSurveyDto(
    string Slug,
    string Name,
    string? Subtitle,
    string? ImageUrl,
    string? OfferUrl,
    string? ThankYouText,
    PublicSurveyFieldsDto Fields,
    DateTimeOffset ServedAt);

/// <summary>
/// <c>POST /api/public/surveys/{slug}</c> so'rovi. HAR BIR maydon nullable:
/// tanasi anonim internetdan keladi va "maydon kelmadi" — bu 400 emas,
/// oddiy holat (o'chirilgan tugma, bo'sh ixtiyoriy maydon). Tekshiruv
/// <c>SurveySubmissionService.Validate</c> da, model bog'lovchida emas —
/// shunda xato matni o'zbekcha va maydonma-maydon chiqadi (§5.1 `errors`).
/// </summary>
/// <param name="StudentGrade">0..11 — <c>0</c> HAQIQIY sinf (nol sinf), "noma'lum" emas (§2.1).</param>
/// <param name="ServedAt">
/// <c>GET</c> qaytargan <c>servedAt</c> ning AYNI o'zi. Tip — <c>string</c>,
/// <c>DateTimeOffset</c> emas: yaroqsiz sana kelganda model bog'lovchi o'zining
/// inglizcha 400 ini qaytarib yuborardi, biz esa uni §2.5 bo'yicha JIMGINA
/// (200 bilan) yutishimiz kerak.
/// </param>
/// <param name="Website">
/// Honeypot (§2.5): odam ko'rmaydigan maydon. To'ldirilgan bo'lsa — javob
/// odatdagi 200, lekin hech nima yozilmaydi.
/// </param>
public record PublicSurveySubmitRequest(
    string? ParentFirstName,
    string? ParentLastName,
    string? ParentPhone,
    string? StudentFirstName,
    string? StudentLastName,
    string? StudentPhone,
    int? StudentGrade,
    string? StudentGender,
    string? ServedAt,
    string? Website);

/// <summary>
/// <c>POST /api/public/surveys/{slug}</c> ning 200 tanasi.
///
/// <para>
/// <b>To'rtta HAR XIL holat uchun AYNAN BIR XIL javob</b> (§5.1): haqiqiy
/// topshiriq, takror (§2.6 4-qadam), honeypot va vaqt tuzog'i. Chaqiruvchi
/// ularni bir-biridan ajrata olmasligi — xatolik emas, maqsad.
/// </para>
/// </summary>
/// <param name="ThankYou">Arizaning o'z matni; <c>null</c> bo'lsa sahifa standart matnni chizadi.</param>
public record PublicSurveySubmitResultDto(bool Ok, string? ThankYou);
