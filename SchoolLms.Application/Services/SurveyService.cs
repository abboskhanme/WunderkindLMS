using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Arizalar (surveys) admin CRUD'ining QOIDALARI.
//  docs/modules/sales-marketing.md §5.2. Vazifa: SM-3.
//
//  NEGA STATIK VA NEGA DI'DA YO'Q
//  ------------------------------
//  `Program.cs` bu to'lqinda boshqa agent qo'lida (§7.1: DI ro'yxati SM-12
//  ning ishi), ya'ni yangi xizmatni ro'yxatdan o'tkazib bo'lmaydi. Loyihada
//  bunday hollar uchun tayyor naqsh bor — `CertificateService`,
//  `StudentProfileBuilder`: statik klass, `IAppDbContext` parametr sifatida.
//  Controller `AppDbContext` ni DI'dan oladi va shu yerga uzatadi.
//  §7.1 SM-12 ning ro'yxatida `SurveySubmissionService` va
//  `NewsTelegramNotifier` bor — bu xizmat u yerda ATAYLAB yo'q.
//
//  BAZA QOIDANI USHLAYDI, XIZMAT ESA TUSHUNTIRADI
//  ----------------------------------------------
//  Uchala qoida ham bazada yozilgan: `ck_surveys_slug` (shakl),
//  `ux_surveys_slug` (`lower(slug)` bo'yicha unikal) va
//  `survey_submissions.survey_id` ning `on delete restrict` i. Baza ularni
//  buzilishga qo'ymaydi, lekin javobi 23514 / 23505 / 23503 raqami bo'ladi —
//  ekranning narigi tomonidagi odam uchun bu 500 bilan bir xil. Shuning
//  uchun xizmat AVVAL o'zi tekshiradi va §5.6 kodini beradi; baza esa poyga
//  holatida (ikki admin bir vaqtda) oxirgi to'siq bo'lib qoladi — chaqiruvchi
//  `IsSlugTakenViolation` / `IsInUseViolation` bilan uni ham o'sha kodga
//  o'giradi.
// ===========================================================================

/// <summary>
/// Ariza (survey) bilan bog'liq barcha qoidalar: slug, Rule T, o'chirish
/// qulfi, hisoblagichlar va ommaviy havola. Hech biri <c>HttpContext</c> ga
/// tegmaydi — shuning uchun ularni testdan ham to'g'ridan-to'g'ri chaqirsa
/// bo'ladi.
/// </summary>
public static partial class SurveyService
{
    /// <summary>
    /// Audit jurnalidagi yorliq (§5.2) — <see cref="AuditService.EntitySurvey"/>.
    /// Qiymatning O'ZI o'zgarmasligi shart: audit qatorlari shu matn bo'yicha
    /// topiladi.
    /// </summary>
    public const string AuditEntity = AuditService.EntitySurvey;

    /// <summary>Ommaviy sahifaning yo'l prefiksi (D1): <c>/ariza/qabul-2027</c>.</summary>
    public const string PublicPath = "/ariza/";

    /// <summary><c>ux_surveys_slug</c> — migratsiyadagi xom SQL indeks nomi.</summary>
    public const string SlugIndex = "ux_surveys_slug";

    /// <summary>§4.1 <c>ck_surveys_slug</c> dagi chegaralar — bir joyda.</summary>
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 60;

    // ----- Xato matnlari (§5.6). Klientdagi zaxira matnlar bilan bir xil. -----

    public const string NameRequiredMessage = "Ariza nomini yozing";
    public const string SlugRequiredMessage = "Havola manzilini kiriting";
    public const string SlugShapeMessage =
        "Faqat kichik lotin harflari, raqam va '-' ishlatiladi (masalan: qabul-2027)";
    public const string StageNotFoundMessage =
        "Tanlangan bosqich topilmadi — ro'yxatni yangilang va qaytadan tanlang";
    public const string ValidationMessage = "Ma'lumotlarni tekshiring";
    public const string SlugTakenMessage = "Bu havola manzili band — boshqasini tanlang";
    public const string InUseMessage =
        "Bu arizada topshirilgan so'rovlar bor — uni o'chirib bo'lmaydi, "
        + "faol emas qilib qo'ying";

    /// <summary>
    /// §4.1 <c>ck_surveys_slug</c> ning AYNAN o'zi. Uzunlik ALOHIDA
    /// tekshiriladi (quyida, regex'dan OLDIN): 60 belgidan uzun satrni bu
    /// naqshga bermaslik — `(-[a-z0-9]+)*` ustida orqaga qaytishning (backtrack)
    /// oldini oladi.
    /// </summary>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    // =====================================================================
    //  Slug
    // =====================================================================

    /// <summary>
    /// Slug'ni SOLISHTIRISH VA SAQLASH shakliga keltiradi: chetdagi bo'shliq
    /// olinadi, harflar kichiklashadi.
    ///
    /// <para>
    /// <b>Nega kichiklashtiramiz, katta harfni rad etmaymiz.</b> Unikal indeks
    /// <c>lower(slug)</c> ustida (§4.1), ya'ni <c>Qabul-2027</c> va
    /// <c>qabul-2027</c> — baza uchun BITTA havola. Agar server katta harfni
    /// 400 "shakli noto'g'ri" deb rad etsa, xodim "band emas ekan" deb o'ylab
    /// qolardi; kichiklashtirilgandan keyin esa javob haqiqatni aytadi —
    /// 409 <c>slug_taken</c>. Klient ham aynan shunday qiladi
    /// (<c>surveys.ts</c>: <c>tail.trim().toLowerCase()</c>).
    /// </para>
    /// <para>
    /// <c>ToLowerInvariant</c> — madaniyatga bog'liq bo'lmagan holda
    /// (turkcha <c>I</c> muammosi). Naqsh baribir faqat ASCII ga ruxsat
    /// beradi, ya'ni PostgreSQL <c>lower()</c> i bilan farq qilmaydi.
    /// </para>
    /// </summary>
    public static string NormalizeSlug(string? raw) => (raw ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Slug shakli buzuqmi. Buzuq bo'lsa — o'zbekcha sabab, aks holda
    /// <c>null</c>. Kirish qiymati <see cref="NormalizeSlug"/> dan o'tgan
    /// bo'lishi kutiladi.
    /// </summary>
    public static string? SlugProblem(string slug)
    {
        if (slug.Length == 0) return SlugRequiredMessage;
        if (slug.Length < SlugMinLength) return $"Havola manzili kamida {SlugMinLength} ta belgi bo'lsin";
        if (slug.Length > SlugMaxLength) return $"Havola manzili ko'pi bilan {SlugMaxLength} ta belgi";
        return SlugPattern().IsMatch(slug) ? null : SlugShapeMessage;
    }

    /// <summary>
    /// Shu slug boshqa arizada bandmi. Taqqoslash <c>lower(slug)</c> bo'yicha —
    /// aynan <c>ux_surveys_slug</c> qaysi ifoda ustida qurilgan bo'lsa,
    /// so'rov ham o'sha ifodani ishlatadi (indeks ishlaydi).
    /// </summary>
    /// <param name="exceptId">Tahrirlanayotgan arizaning o'zi — o'z slug'i
    /// bilan to'qnashmasin.</param>
    public static Task<bool> SlugTakenAsync(
        IAppDbContext db, string slug, Guid? exceptId, CancellationToken ct = default)
    {
        var q = db.Surveys.AsNoTracking().Where(s => s.Slug.ToLower() == slug);
        if (exceptId is { } id) q = q.Where(s => s.Id != id);
        return q.AnyAsync(ct);
    }

    // =====================================================================
    //  Tekshiruv (§5.2, Rule T)
    // =====================================================================

    /// <summary>
    /// Saqlash so'rovini tekshiradi. Javobi ikki xil xatoni ALOHIDA saqlaydi,
    /// chunki ular §5.6 da ikki xil kod: oddiy maydon xatosi — 400
    /// <c>validation</c>, qulflangan tugma — 400 <c>survey_fields_required</c>
    /// (klient uni ushlab tugmalarni qaytarib yoqadi).
    /// </summary>
    public sealed class Result
    {
        /// <summary>Maydon → o'zbekcha xabar. Kalitlar klient formasidagi nomlar.</summary>
        public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);

        /// <summary>Rule T buzilgan tugmalar (<c>showStudent...Input</c>).</summary>
        public List<string> PinnedFields { get; } = [];

        /// <summary>Tekshiruvdan o'tgan, kichiklashtirilgan slug.</summary>
        public string Slug { get; init; } = "";

        public bool RuleTBroken => PinnedFields.Count > 0;
        public bool Ok => Errors.Count == 0 && PinnedFields.Count == 0;
    }

    /// <summary>Rule T qulflagan ikkita tugma — kalit va xodimga ko'rinadigan nomi.</summary>
    private static readonly (string Key, string Label)[] PinnedToggles =
    [
        ("showStudentGenderInput", "jins"),
        ("showStudentGradeInput", "sinf"),
    ];

    /// <summary>
    /// Saqlashdan oldingi barcha tekshiruv. Bazaga BITTA qo'shimcha so'rov
    /// qiladi (bosqich bormi) va faqat bosqich tanlangan bo'lsa.
    /// </summary>
    /// <param name="exceptId">Tahrirlashda — arizaning o'z id'si.</param>
    /// <summary>Xavfsiz havola xatosi matni.</summary>
    public const string UnsafeUrlMessage = "Havola yuklangan fayl yoki https:// bilan boshlanishi kerak";

    /// <summary>
    /// Bo'sh, yuklangan fayl (<c>/uploads/</c>) yoki <c>https://</c> — boshqa hech narsa.
    /// <see cref="NewsService"/> ham shuni ishlatadi.
    /// </summary>
    public static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        var u = url.Trim();
        return u.StartsWith("/uploads/", StringComparison.Ordinal)
            || (Uri.TryCreate(u, UriKind.Absolute, out var abs) && abs.Scheme == Uri.UriSchemeHttps);
    }

    public static async Task<Result> ValidateAsync(
        IAppDbContext db, SurveySaveRequest p, Guid? exceptId, CancellationToken ct = default)
    {
        var slug = NormalizeSlug(p.Slug);
        var result = new Result { Slug = slug };

        if (string.IsNullOrWhiteSpace(p.Name)) result.Errors["name"] = NameRequiredMessage;
        if (SlugProblem(slug) is { } slugProblem) result.Errors["slug"] = slugProblem;

        // Bo'sh satr = "bosqich tanlanmagan" (Rule S 2-qadami), NULL bilan bir xil:
        // HTML <select> tanlanmagan holatda aynan bo'sh satr yuboradi.
        var stageId = string.IsNullOrWhiteSpace(p.StageId) ? null : p.StageId.Trim();
        if (stageId is not null && !await db.LeadStages.AsNoTracking().AnyAsync(s => s.Id == stageId, ct))
            result.Errors["stageId"] = StageNotFoundMessage;

        // Rule T (§2.4) — dizayni MUZLATILGAN doska "noma'lum" jinsni ham,
        // "noma'lum" sinfni ham chiza olmaydi. Bazada `ck_surveys_required_toggles`,
        // bu yerda esa o'sha qoidaning O'QILADIGAN javobi.
        // Rasm va taklif hujjati — faqat yuklangan fayl (`/uploads/...`) yoki `https://`.
        // `OfferUrl` ochiq sahifada xom `<a href>` bo'ladi: `javascript:` qiymati
        // admin bosganda JWT turgan domenda ishga tushardi.
        if (!IsSafeUrl(p.ImageUrl)) result.Errors["imageUrl"] = UnsafeUrlMessage;
        if (!IsSafeUrl(p.OfferUrl)) result.Errors["offerUrl"] = UnsafeUrlMessage;

        if (!p.ShowStudentGenderInput) result.PinnedFields.Add(PinnedToggles[0].Key);
        if (!p.ShowStudentGradeInput) result.PinnedFields.Add(PinnedToggles[1].Key);

        return result;
    }

    /// <summary>
    /// Rule T xabari — FAQAT haqiqatan o'chirilgan tugmalarni sanaydi
    /// ("... : jins" yoki "... : jins, sinf"), §2.4 dagi namunadagidek ikkovini
    /// har doim emas: xato xabari o'zi aytgan faktda ham to'g'ri bo'lishi kerak.
    /// </summary>
    public static string RequiredTogglesMessage(IReadOnlyCollection<string> fields)
    {
        var labels = PinnedToggles.Where(t => fields.Contains(t.Key)).Select(t => t.Label);
        return "Bu maydonlarni o'chirib bo'lmaydi: " + string.Join(", ", labels);
    }

    // =====================================================================
    //  O'chirish qulfi (D7)
    // =====================================================================

    /// <summary>
    /// Arizani o'chirib BO'LMAYDIMI. Bo'lmasa — nechta topshiriq borligi
    /// (0 = o'chirsa bo'ladi).
    ///
    /// <para>
    /// FK <c>on delete restrict</c> (§4.2) — ya'ni bazaning o'zi ham
    /// to'xtatadi, lekin u 23503 raqami bilan to'xtatadi. Bu yerdagi tekshiruv
    /// xodimga NIMA QILISH kerakligini aytadi: o'chirish emas, "faol emas"
    /// qilib qo'yish.
    /// </para>
    /// </summary>
    public static Task<int> SubmissionCountAsync(
        IAppDbContext db, Guid surveyId, CancellationToken ct = default) =>
        db.SurveySubmissions.AsNoTracking().CountAsync(s => s.SurveyId == surveyId, ct);

    // =====================================================================
    //  Hisoblagichlar (§8.3: "submission counters on the DTO")
    // =====================================================================

    /// <summary>Bitta arizaning uchta hisoblagichi.</summary>
    /// <param name="Total">Hamma topshiriq (takrorlari bilan).</param>
    /// <param name="Leads">Shulardan lid yaratganlari (<c>status = 'lead'</c>).</param>
    /// <param name="LastAt">Oxirgi topshiriq vaqti.</param>
    public readonly record struct Counters(int Total, int Leads, DateTimeOffset? LastAt)
    {
        /// <summary>Hali birorta topshiriq tushmagan ariza.</summary>
        public static readonly Counters Empty = new(0, 0, null);
    }

    /// <summary>
    /// Hamma arizaning hisoblagichlari — BITTA guruhlangan so'rovda.
    ///
    /// <para>
    /// <b>Nega bu yerda navigatsiya xossasi ishlatilmaydi.</b> SM-1 munosabatni
    /// ATAYLAB navigatsiyasiz qurgan (<c>SalesMarketingModel.cs</c>:
    /// <c>HasOne&lt;Survey&gt;().WithMany()</c>) — ya'ni <c>Survey.Submissions</c>
    /// kolleksiyasi YO'Q. Uni qo'shib <c>Include</c> qilish ariza qatoriga
    /// o'nlab ming topshiriqni xotiraga tortardi; har bir qator uchun alohida
    /// <c>Count()</c> esa N+1 bo'lardi. Guruhlangan bitta agregat so'rov —
    /// arizalar soni qancha bo'lsa ham ikkita so'rov.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<Guid, Counters>> CountersAsync(
        IAppDbContext db, CancellationToken ct = default) =>
        await CountersAsync(db, surveyId: null, ct);

    /// <summary>Bitta arizaning hisoblagichi (ro'yxat emas — kartochka uchun).</summary>
    public static async Task<Counters> CountersAsync(
        IAppDbContext db, Guid surveyId, CancellationToken ct = default)
    {
        var map = await CountersAsync(db, (Guid?)surveyId, ct);
        return map.GetValueOrDefault(surveyId, Counters.Empty);
    }

    private static async Task<Dictionary<Guid, Counters>> CountersAsync(
        IAppDbContext db, Guid? surveyId, CancellationToken ct)
    {
        var q = db.SurveySubmissions.AsNoTracking();
        if (surveyId is { } id) q = q.Where(s => s.SurveyId == id);

        // Anonim tipga proyeksiya — loyihadagi naqsh (CertificateTypesController):
        // agregatlar SERVERDA sanaladi, brauzerga taxmin qoldirilmaydi.
        var rows = await q
            .GroupBy(s => s.SurveyId)
            .Select(g => new
            {
                SurveyId = g.Key,
                Total = g.Count(),
                Leads = g.Count(s => s.Status == SurveySubmissionStatus.Lead),
                LastAt = g.Max(s => (DateTimeOffset?)s.CreatedAt),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.SurveyId, x => new Counters(x.Total, x.Leads, x.LastAt));
    }

    // =====================================================================
    //  Ommaviy havola (§5.2, §8.3)
    // =====================================================================

    /// <summary>
    /// Ota-ona ochadigan to'liq havola.
    ///
    /// <para>
    /// <b>Nega APEKS domen, so'rov hosti emas.</b> Admin
    /// <c>test.wunderkindschool.uz</c> da o'tiradi, havola esa Instagram
    /// bio'siga va Telegram postiga tushadi — u yerda maktab REKLAMA QILADIGAN
    /// domen bo'lishi kerak. <c>Tenancy:RootDomain</c> ning BIRINCHI qiymati
    /// olinadi (u vergul bilan ajratilgan ro'yxat — <c>Program.cs</c> ham
    /// xuddi shunday o'qiydi).
    /// </para>
    /// <para>
    /// Sozlama bo'sh bo'lsa (dev, <c>docker-compose.server.yml</c>) — so'rovning
    /// o'z sxemasi va hosti. Shunda havola hech bo'lmasa O'SHA muhitda ochiladi;
    /// jimgina bo'sh satr qaytarish esa xodimga nusxalab bo'lmaydigan chip
    /// ko'rsatardi.
    /// </para>
    /// <para>
    /// Sozlama berilganda sxema QAT'IY <c>https</c> (§5.2). Apeks domen ommaviy
    /// nom — u shifrlanmagan holda ulashilmaydi.
    /// </para>
    /// </summary>
    /// <param name="rootDomainSetting"><c>Tenancy:RootDomain</c> ning xom qiymati.</param>
    /// <param name="requestScheme">Zaxira uchun: <c>Request.Scheme</c>.</param>
    /// <param name="requestHost">Zaxira uchun: <c>Request.Host.Value</c>.</param>
    public static string PublicUrl(
        string? rootDomainSetting, string requestScheme, string? requestHost, string slug)
    {
        var apex = (rootDomainSetting ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        var origin = !string.IsNullOrWhiteSpace(apex)
            ? $"https://{apex}"
            : $"{requestScheme}://{requestHost}";

        return origin + PublicPath + slug;
    }

    // =====================================================================
    //  DTO
    // =====================================================================

    /// <summary>Entity + hisoblangan qiymatlar → sim shakli (§5.2).</summary>
    public static SurveyDto ToDto(Survey s, Counters counters, string publicUrl) =>
        new(s.Id, s.Name, s.Slug, s.Subtitle, s.ImageUrl, s.OfferUrl, s.ThankYouText, s.StageId,
            s.ShowStudentFirstNameInput, s.ShowStudentLastNameInput, s.ShowStudentPhoneNumberInput,
            s.ShowStudentGradeInput, s.ShowStudentGenderInput,
            s.IsActive, publicUrl,
            counters.Total, counters.Leads, counters.LastAt,
            s.CreatedAt, s.UpdatedAt);

    // =====================================================================
    //  Baza xatolarini §5.6 kodlariga o'girish (poyga holati)
    // =====================================================================

    /// <summary>
    /// Slug'ning takrorlanishi (<c>ux_surveys_slug</c>, SQLSTATE 23505).
    /// Xizmat oldindan tekshiradi, lekin ikki admin bir vaqtda saqlasa
    /// tekshiruv bilan INSERT orasida bo'shliq bor — oxirgi hakam indeks.
    /// </summary>
    public static bool IsSlugTakenViolation(DbUpdateException ex) =>
        HasSqlState(ex, "23505", SlugIndex);

    /// <summary>
    /// Topshiriqlari bor arizani o'chirishga urinish (SQLSTATE 23503, D7).
    ///
    /// <para>
    /// Indeks nomi bo'yicha emas, FAQAT SQLSTATE bo'yicha tekshiriladi va bu
    /// ataylab: arizaga ikkita jadval havola qiladi — <c>survey_submissions</c>
    /// (restrict) va <c>leads.survey_id</c> (restrict, §4.4) — ikkalasining
    /// javobi ham xodim uchun bir xil: "o'chirib bo'lmaydi, faol emas qiling".
    /// Chaqiruvchi buni FAQAT <c>db.Surveys.Remove(...)</c> dan keyin
    /// ishlatadi, ya'ni boshqa jadvalning 23503 i bu yerga tushmaydi.
    /// </para>
    /// </summary>
    public static bool IsInUseViolation(DbUpdateException ex) => HasSqlState(ex, "23503", null);

    private static bool HasSqlState(DbUpdateException ex, string sqlState, string? contains)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is DbException db && db.SqlState == sqlState
                && (contains is null || inner.Message.Contains(contains, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }
}
