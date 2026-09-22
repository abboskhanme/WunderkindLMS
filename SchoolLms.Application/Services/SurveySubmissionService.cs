using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Ommaviy ariza formasining QOIDALARI — docs/modules/sales-marketing.md
//  §2.4 (maydonlar), §2.5 (spam), §2.6 (topshiriq → lid). Vazifa: SM-2.
//
//  BU — MAHSULOTDAGI BIRINCHI ANONIM YOZUV ENDPOINT'I
//  --------------------------------------------------
//  Shu paytgacha anonim so'rovlar yo faqat o'qigan, yo token bilan kelgan
//  (`GpsIngestController`). Bu yerda esa internetdan kelgan har qanday
//  odam BIZNES YOZUVINI — lidni — yaratadi. Shuning uchun xizmat uchta
//  narsani bir vaqtda ushlaydi:
//
//    1. Bot filtri (honeypot + vaqt tuzog'i) — JIMGINA 200, hech nima
//       yozilmaydi. Botga hech qachon aytilmaydi.
//    2. Tekshiruv — o'zbekcha, maydonma-maydon xato.
//    3. Takror — bitta oila bitta tugmani ikki marta bossa, doskada ikkita
//       bir xil kartochka paydo bo'lmasin.
//
//  YOZADIGAN JADVALLAR: `survey_submissions`, `leads` va — FAQAT Rule S
//  ning 3-qadamida, doska butunlay bo'sh bo'lganda — bitta `lead_stages`
//  qatori. Boshqa hech nima.
//
//  DOSKA MUZLATILGAN (CLAUDE.md)
//  -----------------------------
//  Lid `pages/admin/leads/*` ning bugungi maydonlariga tushadi: izoh BIR
//  SATR (Rule N — kartochka uni bitta <p> da chizadi), jins ikki qiymatli,
//  sinf esa 0..11 butun son. Bu yerdagi har bir "clean/collapse" chaqiruvi
//  aynan shuning uchun: ommaviy formadan kelgan qator uzilishi yoki 500
//  belgilik ism o'sha muzlatilgan kartochkani buzardi, tuzatish esa
//  taqiqlangan.
//
//  TRANZAKSIYA: BITTA `SaveChanges`
//  --------------------------------
//  Lid, topshiriq qatori va (kerak bo'lsa) yangi ustun + audit qatori —
//  hammasi BITTA `SaveChangesAsync` da ketadi, ya'ni EF ning o'z
//  tranzaksiyasida. `BeginTransactionAsync` KERAK EMAS: bu yerda ikkinchi
//  commit yo'q (moliyadagi `PaymentService` dan farqi shunda).
// ===========================================================================

/// <summary>Topshiriqning natijasi — kontroller shunga qarab status tanlaydi.</summary>
public enum SurveySubmitKind
{
    /// <summary>
    /// 200. Haqiqiy topshiriq, TAKROR, honeypot va vaqt tuzog'i — TO'RTOVI
    /// ham shu qiymatni beradi (§5.1). Kontroller ularni ajrata olmasligi —
    /// xatolik emas, himoyaning o'zi.
    /// </summary>
    Accepted,

    /// <summary>400 <c>validation</c> — <see cref="SurveySubmitOutcome.Errors"/> to'ldirilgan.</summary>
    Invalid,

    /// <summary>400 <c>survey_fields_required</c> — Rule T buzilgan ariza (§2.4).</summary>
    FieldsRequired,
}

/// <summary>
/// <see cref="SurveySubmissionService.SubmitAsync"/> natijasi. Tanlangan
/// shakl ataylab "yupqa": kontroller faqat statusni va tayyor tanani
/// yig'adi, birorta qoidani qayta talqin qilmaydi.
/// </summary>
public sealed record SurveySubmitOutcome(
    SurveySubmitKind Kind,
    string? ThankYou,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyList<string> Fields)
{
    private static readonly IReadOnlyDictionary<string, string> NoErrors =
        new Dictionary<string, string>(0);

    private static readonly IReadOnlyList<string> NoFields = [];

    public static SurveySubmitOutcome Accepted(string? thankYou) =>
        new(SurveySubmitKind.Accepted, thankYou, NoErrors, NoFields);

    public static SurveySubmitOutcome Invalid(IReadOnlyDictionary<string, string> errors) =>
        new(SurveySubmitKind.Invalid, null, errors, NoFields);

    public static SurveySubmitOutcome FieldsRequired(IReadOnlyList<string> fields) =>
        new(SurveySubmitKind.FieldsRequired, null, NoErrors, fields);
}

/// <summary>
/// Ommaviy ariza formasi: arizani o'qish va topshiriqni qabul qilish.
/// Kontroller (<c>PublicSurveyController</c>) faqat HTTP ni biladi, qoidalar
/// shu yerda — <see cref="StudyGroupService"/> bilan bir xil bo'linish.
/// </summary>
public sealed class SurveySubmissionService(
    IAppDbContext db, ILogger<SurveySubmissionService> logger)
{
    // ----- §5.6 dagi xato kodlari. Matn AYNAN shu — klient shu bo'yicha tarmoqlanadi. -----

    /// <summary>404 — noma'lum slug ham, yopilgan ariza ham (§5.1).</summary>
    public const string CodeNotFound = "survey_not_found";

    /// <summary>400 — maydon xatolari (<c>errors</c>).</summary>
    public const string CodeValidation = "validation";

    /// <summary>400 — Rule T buzilgan ariza (<c>fields</c>).</summary>
    public const string CodeFieldsRequired = "survey_fields_required";

    /// <summary>Noma'lum slug va yopilgan ariza uchun BIR XIL matn (§5.1).</summary>
    public const string NotFoundMessage = "Bu ariza topilmadi yoki yopilgan";

    /// <summary>400 <c>validation</c> ning umumiy sarlavhasi.</summary>
    public const string ValidationMessage = "Ma'lumotlarni tekshiring";

    /// <summary>
    /// Doska bo'sh bo'lganda yaratiladigan yagona ustun nomi (Rule S, 3-qadam).
    /// </summary>
    public const string DefaultStageTitle = "Yangi arizalar";

    /// <summary>Rule S 3-qadamida yaratilgan ustunning audit yorlig'i.</summary>
    /// <remarks>
    /// <c>AuditService</c> — bu to'lqinda BOSHQA agentning fayli (§7.1,
    /// SM-12), shuning uchun konstanta shu yerda turadi —
    /// <c>CertificateService.AuditEntityType</c> bilan bir xil naqsh.
    /// Qiymat o'zgarmasligi shart: audit ekranidagi filtr shu matn bo'yicha
    /// qidiradi.
    /// </remarks>
    public const string AuditEntityLeadStage = "LeadStage";

    /// <summary>Audit qatoridagi aktyor nomi: bu yozuvni odam emas, forma yozgan.</summary>
    public const string AuditActorName = "Ariza formasi";

    // ----- §2.5 vaqt tuzog'i. BOT FILTRI, xavfsizlik chegarasi EMAS. -----
    private static readonly TimeSpan MinFormAge = TimeSpan.FromSeconds(2);

    /// <summary>Takrorni qidirish oynasi (§2.6, 4-qadam).</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(24);

    /// <summary>Slug'ning bazadagi eng uzun uzunligi (<c>ck_surveys_slug</c>).</summary>
    private const int MaxSlugLength = 60;

    /// <summary>Ism/familiya uchun chegara — muzlatilgan kartochkani himoya qiladi.</summary>
    private const int MaxNameLength = 50;

    /// <summary>Telefon satri uchun chegara ("+998 90 123 45 67" — 17 belgi).</summary>
    private const int MaxPhoneLength = 32;

    /// <summary>Solishtirish uchun kerakli eng kam raqam (<c>PhoneUtil.Key</c> — 9).</summary>
    private const int MinPhoneDigits = 9;

    /// <summary>
    /// Brauzer satri (<c>user_agent</c>) uchun chegara: qiymat anonim
    /// so'rovning sarlavhasidan keladi va ustun cheksiz <c>text</c>.
    /// </summary>
    private const int MaxUserAgentLength = 512;

    // ----- Xato matnlari. Ommaviy sahifadagi matnlar bilan AYNI (SurveyPage.tsx). -----
    private const string ParentFirstNameRequired = "Ismni kiriting";
    private const string ParentFirstNameTooShort = "Ism kamida 2 ta harfdan iborat bo'lsin";
    private const string StudentFirstNameRequired = "O'quvchining ismini kiriting";
    private const string GenderRequired = "Jinsini tanlang";
    private const string GradeRequired = "Sinfni tanlang";
    private const string GradeOutOfRange = "Sinfni 0 dan 11 gacha tanlang";
    private const string PhoneIncomplete = "Telefon raqamini to'liq kiriting";
    private const string PhoneInvalid = "Telefon raqamini to'g'ri kiriting";

    /// <summary>Matn chegaradan olinadi — ikkovi bir-biridan uzoqlashib ketmasin.</summary>
    private static readonly string TooLong = $"Juda uzun — {MaxNameLength} belgidan oshmasin";

    /* -------------------------------------------------------------------
     *  1. Arizani o'qish
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Slug bo'yicha FAOL arizani topadi; topilmasa yoki yopiq bo'lsa —
    /// <c>null</c>. Chaqiruvchi ikkala holatda ham BIR XIL 404 qaytaradi:
    /// ommaviy endpoint havola qachondir mavjud bo'lganini tasdiqlamasligi
    /// kerak (§5.1).
    /// </summary>
    public async Task<Survey?> FindActiveAsync(string? slug, CancellationToken ct = default)
    {
        var value = (slug ?? "").Trim();
        // Bazaga bormasdan kesish: slug bazada 60 belgi bilan cheklangan
        // (`ck_surveys_slug`), ya'ni undan uzuni hech qachon topilmaydi.
        if (value.Length is 0 or > MaxSlugLength) return null;

        // `ux_surveys_slug` — `lower(slug)` ustidagi indeks, shuning uchun
        // solishtirish ham `lower(...)` bo'lishi kerak (aks holda indeks
        // ishlamaydi va katta harf bilan kelgan havola "topilmadi" berardi).
        var lowered = value.ToLowerInvariant();
        return await db.Surveys.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsActive && s.Slug.ToLower() == lowered, ct);
    }

    /// <summary>
    /// <c>GET /api/public/surveys/{slug}</c> javobi (§5.1). Sahifa shundan
    /// boshqa hech nima so'ramaydi.
    /// </summary>
    public static PublicSurveyDto ToPublicDto(Survey survey) => new(
        survey.Slug,
        survey.Name,
        survey.Subtitle,
        survey.ImageUrl,
        survey.OfferUrl,
        survey.ThankYouText,
        new PublicSurveyFieldsDto(
            survey.ShowStudentFirstNameInput,
            survey.ShowStudentLastNameInput,
            survey.ShowStudentPhoneNumberInput,
            survey.ShowStudentGradeInput,
            survey.ShowStudentGenderInput),
        ServedAtNow());

    /// <summary>
    /// Sahifaga beriladigan server soati (§2.5).
    ///
    /// <para>
    /// Ofset — Toshkentniki (+05:00), nol emas: bu qiymat BAZAGA yozilmaydi
    /// (<c>timestamptz</c> ning "faqat UTC" qoidasi bunga tegishli emas), zato
    /// uni brauzer konsolida va log'da odam o'qiydi — u yerda "14:03" soatga
    /// mos tushishi kerak. Qaytib kelganda solishtirish baribir ofsetni
    /// hisobga oladi.
    /// </para>
    /// </summary>
    private static DateTimeOffset ServedAtNow()
    {
        var instant = AppClock.NowInstant;            // UTC lahza
        var local = AppClock.ToLocal(instant);        // Toshkentdagi devor soati
        var offset = local - instant.UtcDateTime;     // +05:00
        return new DateTimeOffset(local, offset);
    }

    /* -------------------------------------------------------------------
     *  2. Topshiriq → lid (§2.6)
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Topshiriqni qabul qiladi: bot filtri → tekshiruv → takror → lid.
    /// Qadamlar tartibi §2.6 dagi ro'yxatning AYNAN o'zi.
    /// </summary>
    /// <param name="survey"><see cref="FindActiveAsync"/> topgan faol ariza.</param>
    /// <param name="ip">So'rovning IP'si — faqat suiste'molni tekshirish uchun (§4.2).</param>
    /// <param name="userAgent">Brauzer satri — o'sha maqsadda.</param>
    public async Task<SurveySubmitOutcome> SubmitAsync(
        Survey survey,
        PublicSurveySubmitRequest req,
        string? ip,
        string? userAgent,
        CancellationToken ct = default)
    {
        // Rule T (§2.4). Bazada `ck_surveys_required_toggles` buni allaqachon
        // ushlab turadi, ya'ni bu yerga tushish uchun ariza CHECK'dan oldin
        // yaratilgan bo'lishi kerak. Shunda ham 500 emas, tushunarli 400
        // qaytadi — ota-ona uchun emas, buni ko'radigan admin uchun.
        var missingToggles = RequiredTogglesOff(survey);
        if (missingToggles.Count > 0) return SurveySubmitOutcome.FieldsRequired(missingToggles);

        // §2.5 — honeypot va vaqt tuzog'i. Javob odatdagi 200, yozuv YO'Q.
        if (IsHoneypotHit(req))
        {
            logger.LogInformation("Ariza honeypot: survey={Slug}", survey.Slug);
            return SurveySubmitOutcome.Accepted(survey.ThankYouText);
        }

        if (IsTimeTrapHit(req.ServedAt, out var reason))
        {
            logger.LogInformation(
                "Ariza vaqt tuzog'i: survey={Slug}, sabab={Reason}", survey.Slug, reason);
            return SurveySubmitOutcome.Accepted(survey.ThankYouText);
        }

        var values = SubmittedValues.From(survey, req);

        var errors = Validate(survey, values);
        if (errors.Count > 0) return SurveySubmitOutcome.Invalid(errors);

        var now = AppClock.NowInstant;
        var submission = NewSubmission(survey, values, ip, userAgent, now);

        // §2.6, 4-qadam — takror. Qator BARIBIR saqlanadi (ota-ona nima
        // yozganining isboti), lekin ikkinchi lid yaratilmaydi.
        var duplicateOf = await FindRecentDuplicateAsync(survey, values, now, ct);
        if (duplicateOf is not null)
        {
            submission.Status = SurveySubmissionStatus.Duplicate;
            submission.LeadId = duplicateOf.LeadId;
            db.SurveySubmissions.Add(submission);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Ariza takrori: survey={Slug}, lead={LeadId}", survey.Slug, duplicateOf.LeadId);
            return SurveySubmitOutcome.Accepted(survey.ThankYouText);
        }

        var lead = NewLead(survey, values, await ResolveStageIdAsync(survey, ct));
        db.Leads.Add(lead);

        submission.LeadId = lead.Id;
        db.SurveySubmissions.Add(submission);

        // Bitta commit: lid, topshiriq va (Rule S 3-qadami bo'lsa) yangi
        // ustun + audit qatori — birgalikda yoki umuman yo'q.
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Yangi ariza: survey={Slug}, lead={LeadId}", survey.Slug, lead.Id);
        return SurveySubmitOutcome.Accepted(survey.ThankYouText);
    }

    /* -------------------------------------------------------------------
     *  3. Spam filtri (§2.5)
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Honeypot: odam ko'rmaydigan <c>website</c> maydoni to'ldirilganmi.
    /// </summary>
    private static bool IsHoneypotHit(PublicSurveySubmitRequest req) =>
        !string.IsNullOrWhiteSpace(req.Website);

    /// <summary>
    /// Vaqt tuzog'i: forma juda tez (&lt; 2 s) yuborildimi — odam ikki
    /// soniyada to'ldira olmaydi.
    ///
    /// <para>
    /// <b>Bu bot filtri, xavfsizlik chegarasi emas.</b> <c>servedAt</c>
    /// imzolanmagan va uni istagan odam o'zgartira oladi. Uni HMAC bilan
    /// "mustahkamlash" — endpoint xavfsiz bo'lib qoldi degan YOLG'ON
    /// taassurot beradi; haqiqiy himoya — chastota chegarasi
    /// (<c>survey</c> siyosati, §2.5).
    /// </para>
    /// <para>
    /// <b>Faqat pastki chegara tuzoq.</b> Qiymat kelmasa, o'qib bo'lmasa yoki
    /// forma 2 soatdan keyin yuborilsa — ariza QABUL qilinadi (2026-09-21,
    /// docs/ASSUMPTIONS.md). Tuzoqqa tushgan yuborish "qabul qilindi" javobini
    /// oladi va hech narsa yozilmaydi: kechqurun ochib ertalab to'ldirgan
    /// ota-ona arizasi jimgina yo'qolardi. Himoya qiymati esa nol — bot
    /// <c>servedAt</c> ni istagancha soxtalashtiradi yoki qayta <c>GET</c>
    /// qiladi (60/daqiqa ruxsat).
    /// </para>
    /// </summary>
    private static bool IsTimeTrapHit(string? servedAt, out string reason)
    {
        // Sahifa qiymatni AYNAN biz bergan ko'rinishda (ofseti bilan)
        // qaytaradi, shuning uchun qo'shimcha `Assume...` bayrog'i kerak
        // emas — ofseti bor satr o'zi bir ma'noli.
        if (!DateTimeOffset.TryParse(
                servedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var served))
        {
            reason = "";
            return false;
        }

        var age = AppClock.NowInstant - served;
        if (age < MinFormAge)
        {
            // Manfiy ham shu yerga tushadi: kelajakdagi `servedAt` — soxta.
            reason = "juda tez";
            return true;
        }

        reason = "";
        return false;
    }

    /* -------------------------------------------------------------------
     *  4. Tekshiruv (§2.4)
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Rule T: o'chirilgan bo'lmasligi SHART bo'lgan tugmalar. Bo'sh ro'yxat —
    /// hammasi joyida.
    /// </summary>
    private static List<string> RequiredTogglesOff(Survey survey)
    {
        var off = new List<string>(2);
        if (!survey.ShowStudentGenderInput) off.Add("showStudentGenderInput");
        if (!survey.ShowStudentGradeInput) off.Add("showStudentGradeInput");
        return off;
    }

    /// <summary>
    /// Rule T buzilgan arizaning odam o'qiydigan xabari (§2.4 dagi shakl).
    /// </summary>
    public static string FieldsRequiredMessage(IReadOnlyList<string> fields)
    {
        var names = fields.Select(f => f switch
        {
            "showStudentGenderInput" => "jins",
            "showStudentGradeInput" => "sinf",
            _ => f,
        });
        return $"Bu maydonlarni o'chirib bo'lmaydi: {string.Join(", ", names)}";
    }

    /// <summary>
    /// Maydonma-maydon tekshiruv. Kalitlar — ommaviy sahifadagi maydon
    /// nomlari (§5.1 <c>errors</c>), matnlar esa o'sha sahifaning o'z
    /// matnlari bilan bir xil: server va brauzer bitta gapni ikki xil
    /// aytmasligi kerak.
    /// </summary>
    private static Dictionary<string, string> Validate(Survey survey, SubmittedValues v)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        // --- Ota-ona: uchta maydon DOIM chiziladi va ikkitasi majburiy (§2.4) ---
        if (v.ParentFirstName.Length == 0) errors["parentFirstName"] = ParentFirstNameRequired;
        else if (v.ParentFirstName.Length < 2) errors["parentFirstName"] = ParentFirstNameTooShort;
        else if (v.ParentFirstName.Length > MaxNameLength) errors["parentFirstName"] = TooLong;

        if (v.ParentLastName.Length > MaxNameLength) errors["parentLastName"] = TooLong;

        if (v.ParentPhone.Length > MaxPhoneLength) errors["parentPhone"] = PhoneInvalid;
        else if (PhoneUtil.DigitsOnly(v.ParentPhone).Length < MinPhoneDigits)
            errors["parentPhone"] = PhoneIncomplete;

        // --- O'quvchi: faqat YOQILGAN tugmalar tekshiriladi ---
        if (survey.ShowStudentFirstNameInput)
        {
            if (v.StudentFirstName.Length == 0) errors["studentFirstName"] = StudentFirstNameRequired;
            else if (v.StudentFirstName.Length > MaxNameLength) errors["studentFirstName"] = TooLong;
        }

        if (survey.ShowStudentLastNameInput && v.StudentLastName.Length > MaxNameLength)
            errors["studentLastName"] = TooLong;

        if (survey.ShowStudentGenderInput && !SurveyGender.All.Contains(v.StudentGender))
            errors["studentGender"] = GenderRequired;

        if (survey.ShowStudentGradeInput)
        {
            if (v.StudentGrade is null) errors["studentGrade"] = GradeRequired;
            else if (v.StudentGrade is < 0 or > 11) errors["studentGrade"] = GradeOutOfRange;
        }

        // O'quvchi telefoni — IXTIYORIY: bo'sh bo'lsa xato yo'q, yozilgan
        // bo'lsa to'liq bo'lsin (sahifadagi qoidaning aynan o'zi).
        if (survey.ShowStudentPhoneNumberInput && v.StudentPhone.Length > 0)
        {
            if (v.StudentPhone.Length > MaxPhoneLength) errors["studentPhone"] = PhoneInvalid;
            else if (PhoneUtil.DigitsOnly(v.StudentPhone).Length < MinPhoneDigits)
                errors["studentPhone"] = PhoneIncomplete;
        }

        return errors;
    }

    /* -------------------------------------------------------------------
     *  5. Takror (§2.6, 4-qadam)
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Oxirgi 24 soatda SHU ariza bo'yicha, SHU telefon kaliti bilan va SHU
    /// o'quvchi nomi bilan topshiriq bo'lganmi.
    ///
    /// <para>
    /// Bir xil telefon + BOSHQA bola nomi — bu ikkinchi farzand, ya'ni
    /// ikkinchi lid (§2.6). Shuning uchun solishtirish nom bo'yicha ham
    /// ketadi.
    /// </para>
    /// <para>
    /// Nom normalizatsiyasi (kichik harf, qirqilgan, bo'shliqlar
    /// siqilgan) SQL da emas, xotirada: <c>ix_survey_submissions_dedupe</c>
    /// indeksi ariza + telefon kaliti + 24 soat bo'yicha qatorlar sonini
    /// bir nechtagacha tushiradi, qolgani esa bitta <c>foreach</c>.
    /// </para>
    /// </summary>
    private async Task<DuplicateCandidate?> FindRecentDuplicateAsync(
        Survey survey, SubmittedValues v, DateTimeOffset now, CancellationToken ct)
    {
        var since = now - DuplicateWindow;

        // Faqat lidi doskada HALI TURGAN topshiriq takror sanaladi. Xodim lidni
        // o'chirgan bo'lsa-yu, oila 24 soat ichida qayta yuborsa — bu yangi murojaat:
        // aks holda ikkinchi urinish hech qayerga yetmay, "takror" bo'lib qolardi.
        var recent = await db.SurveySubmissions.AsNoTracking()
            .Where(s => s.SurveyId == survey.Id
                        && s.ParentPhoneKey == v.ParentPhoneKey
                        && s.CreatedAt > since
                        && s.LeadId != null
                        && db.Leads.Any(l => l.Id == s.LeadId))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new DuplicateCandidate(
                s.LeadId, s.StudentFirstName, s.StudentLastName, s.StudentGrade, s.StudentGender))
            .ToListAsync(ct);

        // Kalit: ism + SINF + JINS. Ism so'ralmaydigan arizada ism kaliti bo'sh, va
        // faqat telefon bo'yicha solishtirilsa ikkinchi farzand birinchisiga "takror"
        // bo'lib qo'shilib ketardi. Sinf va jins Rule T bo'yicha HAR DOIM so'raladi.
        var name = NormalizeForCompare(v.StudentFirstName, v.StudentLastName);
        var gender = NullIfEmpty(v.StudentGender);
        return recent.FirstOrDefault(
            c => NormalizeForCompare(c.StudentFirstName, c.StudentLastName) == name
                 && c.StudentGrade == v.StudentGrade
                 && c.StudentGender == gender);
    }

    /// <summary>Takrorni qidirishda kerak bo'ladigan ustunlar.</summary>
    private sealed record DuplicateCandidate(
        string? LeadId, string? StudentFirstName, string? StudentLastName,
        short? StudentGrade, string? StudentGender);

    /// <summary>Ism + familiya → solishtirish kaliti: kichik harf, bo'shliqlar siqilgan.</summary>
    private static string NormalizeForCompare(string? first, string? last) =>
        Collapse($"{first} {last}").ToLowerInvariant();

    /* -------------------------------------------------------------------
     *  6. Bosqich (Rule S) va lid (§2.6, 5–6-qadam)
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Rule S — lid qaysi ustunga tushadi:
    /// 1) arizaning o'z bosqichi, agar u hali ham mavjud bo'lsa;
    /// 2) eng kichik <c>Order</c> li ustun (<c>Order == 0</c> EMAS: tartib
    ///    <c>LeadStagesController.Reorder</c> qo'lida va nol qolishiga
    ///    kafolat yo'q);
    /// 3) doska BUTUNLAY bo'sh bo'lsa — bitta ustun yaratiladi va bu audit
    ///    qilinadi.
    ///
    /// <para>
    /// 3-qadam nega bor: muqobillarning hammasi yomonroq. 500 — telefon
    /// raqamini yozgan haqiqiy oilani yo'qotadi; bo'sh <c>Stage</c> — doska
    /// chiza olmaydigan lid (uni faqat voronkadagi "orphan" hisoblagichi
    /// ko'radi). Yangi USTUN qo'shish esa doskaning dizayniga tegmaydi —
    /// CLAUDE.md muzlatgan narsa ko'rinish, ma'lumot emas.
    /// </para>
    /// </summary>
    private async Task<string> ResolveStageIdAsync(Survey survey, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(survey.StageId))
        {
            var stillThere = await db.LeadStages.AsNoTracking()
                .AnyAsync(s => s.Id == survey.StageId, ct);
            if (stillThere) return survey.StageId!;
        }

        // `ThenBy(Id)` — bir xil `Order` li ikki ustun bo'lsa tanlov barqaror
        // bo'lsin: har topshiriq boshqa ustunga tushishi tushuntirib
        // bo'lmaydigan xatti-harakat bo'lardi.
        var lowest = await db.LeadStages.AsNoTracking()
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);
        if (lowest is not null) return lowest;

        var stage = new LeadStage { Title = DefaultStageTitle, Color = "blue", Order = 0 };
        db.LeadStages.Add(stage);

        // Anonim so'rov `surveys`/`survey_submissions`/`leads` dan tashqariga
        // YOZADIGAN yagona holat — shuning uchun u izsiz qolmaydi. Aktyor
        // `null`: buni odam emas, forma qildi.
        db.AuditLogs.Add(AuditService.Entry(
            AuditEntityLeadStage, stage.Id, "create",
            $"Lidlar doskasi bo'sh edi — «{DefaultStageTitle}» ustuni ariza formasi tomonidan yaratildi",
            actorId: null, actorName: AuditActorName));

        logger.LogInformation(
            "Lidlar doskasi bo'sh edi — «{Title}» ustuni yaratildi (survey={Slug})",
            DefaultStageTitle, survey.Slug);

        return stage.Id;
    }

    /// <summary>
    /// §2.4 jadvalidagi moslash. Doskaning ustunlari o'zgarmaydi — faqat
    /// to'ldiriladi.
    /// </summary>
    private static Lead NewLead(Survey survey, SubmittedValues v, string stageId) => new()
    {
        // O'quvchi nomi bo'lmasa — HAQIQATNI yozadigan zaxira nom (§2.4).
        // `leads.full_name` not null, doska esa uni kartochkaning sarlavhasi
        // qilib chizadi.
        FullName = v.StudentFullName.Length > 0
            ? v.StudentFullName
            : $"{v.ParentFullName} — farzandi",
        Gender = v.StudentGender,
        // Tug'ilgan sana ommaviy formada SO'RALMAYDI (§2.4) — ustun esa
        // not null satr, ya'ni bo'sh satr = "yo'q".
        BirthDate = string.Empty,
        ParentFullName = v.ParentFullName,
        // Telefon AYNAN yozilganicha: ota-ona keyin o'zi yozgan raqamni
        // ko'rsin. Solishtirish uchun `parent_phone_key` bor.
        ParentPhone = v.ParentPhone,
        TargetGrade = v.StudentGrade ?? 0,
        Note = BuildNote(survey, v),
        Stage = stageId,
        Source = LeadSource.Survey,
        SurveyId = survey.Id,
        // `CreatedAt` — entity initsializatoridan (`AppClock.Now`), §4.4.
    };

    /// <summary>
    /// Rule N — izoh. BIR SATR: muzlatilgan <c>LeadCard.tsx</c> uni bitta
    /// <c>&lt;p&gt;</c> ichida chizadi, ya'ni qator uzilishi shunchaki
    /// yo'qoladi. Id, slug va uuid yo'q — kartochkani ODAM o'qiydi; mashina
    /// uchun bog'lanish <c>leads.survey_id</c> da.
    /// </summary>
    private static string BuildNote(Survey survey, SubmittedValues v)
    {
        // Sana — maktab mintaqasida (`AppClock.Now`), chunki uni xodim
        // o'qiydi. Ariza nomi ham siqiladi: admin uni ko'p qatorli qilib
        // kiritgan bo'lsa ham izoh bir satr bo'lib qolishi kerak.
        var stamp = AppClock.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        var note = $"Ariza: {Collapse(survey.Name)} · {stamp}";

        // O'quvchi telefonining `leads` da USTUNI yo'q (§2.4) — kelgan
        // bo'lsa, shu yerda saqlanadi.
        return v.StudentPhone.Length > 0
            ? $"{note} · O'quvchi tel: {v.StudentPhone}"
            : note;
    }

    /// <summary>
    /// Registr qatori (§4.2): ota-ona AYNAN nima yozgan. Lid keyin
    /// tahrirlansa yoki o'chirilsa ham bu qator o'zgarmaydi.
    /// </summary>
    private static SurveySubmission NewSubmission(
        Survey survey, SubmittedValues v, string? ip, string? userAgent, DateTimeOffset now) => new()
    {
        SurveyId = survey.Id,
        Status = SurveySubmissionStatus.Lead,
        ParentFirstName = v.ParentFirstName,
        ParentLastName = NullIfEmpty(v.ParentLastName),
        ParentPhone = v.ParentPhone,
        ParentPhoneKey = v.ParentPhoneKey,
        StudentFirstName = NullIfEmpty(v.StudentFirstName),
        StudentLastName = NullIfEmpty(v.StudentLastName),
        StudentPhone = NullIfEmpty(v.StudentPhone),
        StudentGrade = v.StudentGrade is null ? null : (short)v.StudentGrade.Value,
        StudentGender = NullIfEmpty(v.StudentGender),
        Ip = ip,
        // Sarlavha anonim so'rovdan keladi va ustun cheksiz `text` — kesamiz.
        UserAgent = Truncate(userAgent, MaxUserAgentLength),
        // Vaqt ATAYLAB shu yerda qo'yiladi (bazadagi `default now()` ga
        // tashlab qo'yilmaydi): takror oynasi ham, registrdagi sana ham
        // BITTA soatdan kelishi kerak.
        CreatedAt = now,
    };

    /* -------------------------------------------------------------------
     *  7. Kiruvchi qiymatlarni tozalash
     * ---------------------------------------------------------------- */

    /// <summary>
    /// Tozalangan kiruvchi qiymatlar. O'chirilgan tugmaning qiymati bu yerga
    /// UMUMAN tushmaydi: forma so'ramagan narsani hech kim (jumladan qo'lda
    /// yasalgan so'rov ham) yozib keta olmaydi.
    /// </summary>
    private sealed record SubmittedValues(
        string ParentFirstName,
        string ParentLastName,
        string ParentPhone,
        string ParentPhoneKey,
        string StudentFirstName,
        string StudentLastName,
        string StudentPhone,
        int? StudentGrade,
        string StudentGender)
    {
        /// <summary>"{ism} {familiya}" — bo'shliqlari tartibga solingan.</summary>
        public string ParentFullName => Collapse($"{ParentFirstName} {ParentLastName}");

        /// <summary>O'quvchining to'liq nomi; ikkalasi ham bo'sh bo'lsa — bo'sh satr.</summary>
        public string StudentFullName => Collapse($"{StudentFirstName} {StudentLastName}");

        public static SubmittedValues From(Survey survey, PublicSurveySubmitRequest req)
        {
            var parentPhone = Collapse(req.ParentPhone);
            return new SubmittedValues(
                ParentFirstName: Collapse(req.ParentFirstName),
                ParentLastName: Collapse(req.ParentLastName),
                ParentPhone: parentPhone,
                ParentPhoneKey: PhoneUtil.Key(parentPhone),
                StudentFirstName: survey.ShowStudentFirstNameInput
                    ? Collapse(req.StudentFirstName) : "",
                StudentLastName: survey.ShowStudentLastNameInput
                    ? Collapse(req.StudentLastName) : "",
                StudentPhone: survey.ShowStudentPhoneNumberInput
                    ? Collapse(req.StudentPhone) : "",
                StudentGrade: survey.ShowStudentGradeInput ? req.StudentGrade : null,
                StudentGender: survey.ShowStudentGenderInput
                    ? Collapse(req.StudentGender).ToLowerInvariant() : "");
        }
    }

    /// <summary>
    /// Qirqadi va ICHKI bo'shliqlarni (jumladan qator uzilishini) bitta
    /// probelga siqadi. Har bir kiruvchi satr shu yerdan o'tadi: ommaviy
    /// formadan kelgan qator uzilishi Rule N ning bir satrli izohini ham,
    /// muzlatilgan kartochkaning ko'rinishini ham buzardi.
    /// </summary>
    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var sb = new StringBuilder(value.Length);
        var space = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space) sb.Append(' ');
            space = false;
            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>Bo'sh satr — bazada <c>null</c> (ustunlar nullable, §4.2).</summary>
    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>Uzun satrni kesadi; <c>null</c> va bo'sh — <c>null</c>.</summary>
    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
