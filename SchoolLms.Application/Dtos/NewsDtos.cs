namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Yangiliklar — admin API shakllari.
//  Spetsifikatsiya: docs/modules/sales-marketing.md §5.4. Vazifa: SM-5.
// ===========================================================================
//
//  AUDITORIYA: BAZADA UCHTA BOOLEAN, SIMDA BITTA MASSIV (§3.3 N5)
//  --------------------------------------------------------------
//  `news.for_employee | for_parent | for_student` — uchta ustun, chunki
//  "kamida bittasi tanlangan" qoidasi shunda BAZA darajasidagi CHECK bo'ladi
//  (`ck_news_audience`) va lenta so'rovi indeksga tushadi. API esa
//  `audience: ["parent","student"]` ko'rinishida gapiradi — ekran uchun
//  massiv qulayroq va `existing-module-gaps.md` §5.2 aynan shuni so'ragan.
//  O'girish MEXANIK va BITTA joyda: `NewsService.AudienceOf` /
//  `NewsService.ApplyAudience`. Boshqa hech qayerda `ForParent` bilan
//  `"parent"` yonma-yon yozilmaydi.
//
//  MATN — ODDIY MATN, HTML EMAS (§3.3 N2)
//  --------------------------------------
//  `Body` serverga qanday kelsa, shunday saqlanadi va shunday qaytadi:
//  bu yerda ham, xizmatda ham sanitizer yo'q, chunki renderer ham yo'q —
//  uchala klient `whitespace-pre-line` bilan chizadi. Sanitizer + uchta
//  HTML renderer = saqlangan XSS uchun uchta imkoniyat, va bularning
//  hammasi maktab e'lonidagi qalin shrift uchun.
//
//  KANAL — FAQAT TELEGRAM (`CLAUDE.md`)
//  ------------------------------------
//  Shu sababdan bu yerda kanal tanlagich yo'q: `NewsPublishRequest` da
//  bitta `SendTelegram` bayrog'i bor, SMS ham, mobil push ham mahsulotda
//  yo'q va bo'lmaydi.

/// <summary>
/// <c>audience[]</c> massivining qiymatlari — SIM formati (§3.3 N5).
///
/// <para>
/// Domenda emas, DTO'da turadi ATAYLAB: bazada bu qiymatlar umuman yo'q
/// (u yerda uchta boolean), ular faqat API shakli. Domendagi
/// <see cref="SchoolLms.Domain.LeadSource"/> dan farqi shunda — u ustunda
/// saqlanadi va CHECK bilan qulflangan.
/// </para>
/// </summary>
public static class NewsAudience
{
    /// <summary>Xodimlar: o'qituvchi, staff, admin (§3.3 N6).</summary>
    public const string Employee = "employee";

    /// <summary>Ota-onalar.</summary>
    public const string Parent = "parent";

    /// <summary>O'quvchilar.</summary>
    public const string Student = "student";

    /// <summary>
    /// Ruxsat etilgan qiymatlar — ekranda va DTO'da KO'RINADIGAN tartibda
    /// (klientdagi <c>AUDIENCE_ORDER</c> bilan bir xil). Server qaysi
    /// tartibda bersa, ekran ham shu tartibda chizadi.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Employee, Parent, Student];
}

/// <summary>
/// Admin ro'yxatining filtri (<c>?state=</c>, §5.4).
/// <c>Archived</c> — yumshoq o'chirilganlar (<c>deleted_at is not null</c>,
/// §3.3 N8); qolgan uchtasi o'chirilmaganlar ichidan tanlaydi.
/// </summary>
public static class NewsState
{
    /// <summary>O'chirilmaganlarning hammasi — qoralama ham, e'lon qilingani ham.</summary>
    public const string All = "all";

    /// <summary><c>published_at is null</c>.</summary>
    public const string Draft = "draft";

    /// <summary><c>published_at is not null</c>.</summary>
    public const string Published = "published";

    /// <summary>Arxiv: <c>deleted_at is not null</c>.</summary>
    public const string Archived = "archived";
}

/// <summary>
/// Admin ekranidagi bitta yangilik (§5.4 <c>NewsAdminDto</c>).
///
/// <para>
/// <b><see cref="DeletedAt"/> yo'q — ataylab.</b> Qator qaysi ro'yxatdan
/// kelganini ekran o'zi biladi (<c>state=archived</c>), DTO esa arxiv
/// bayrog'ini olib yurmaydi: uni qo'shish "o'chirilgan yangilikni ham
/// e'lon qilsa bo'ladimi?" degan savolni har bir klientga ko'chirardi.
/// Javob bitta va u serverda: bo'lmaydi.
/// </para>
/// </summary>
/// <param name="Audience">
/// <see cref="NewsAudience"/> qiymatlari, <see cref="NewsAudience.All"/>
/// tartibida. Hech qachon bo'sh emas — <c>ck_news_audience</c>.
/// </param>
/// <param name="PublishedAt"><c>null</c> — qoralama (§3.3 N3).</param>
/// <param name="AuthorName">
/// Muallif nomining NUSXASI (<c>news.author_name</c>): xodim nomini
/// o'zgartirsa ham, hisobi o'chsa ham, kim e'lon qilgani o'zgarmaydi.
/// Shuning uchun ro'yxatda birorta JOIN yo'q — N+1 ning manbai ham yo'q.
/// </param>
/// <param name="TelegramSentAt">
/// <c>null</c> uchta holatda: hali e'lon qilinmagan, <c>sendTelegram: false</c>
/// bilan e'lon qilingan yoki bot sozlanmagan (§3.3 N4). DTO ularni
/// ATAYLAB ajratmaydi — yuborishni so'ragan odam aniq jumlani e'lon
/// qilingan zahoti oladi, ro'yxat esa faqat ishonchli gapni yozadi.
/// </param>
public record NewsAdminDto(
    Guid Id,
    string Title,
    string Body,
    string? ImageUrl,
    IReadOnlyList<string> Audience,
    DateTimeOffset? PublishedAt,
    string AuthorName,
    DateTimeOffset? TelegramSentAt,
    int TelegramRecipientCount,
    int TelegramSentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Yangilik yozish/tahrirlash so'rovi (§5.4 <c>NewsSaveRequest</c>).
///
/// <para>
/// <b>Hamma maydon nullable — ataylab.</b> Bo'sh sarlavha uchun ASP.NET
/// ning o'z 400'i (<c>ProblemDetails</c>, inglizcha) qaytsa, klient §5.6
/// dagi <c>code</c> ni topa olmaydi va foydalanuvchi inglizcha jumla
/// ko'radi. Shuning uchun tekshiruv — <see cref="Services.NewsService.Validate"/>,
/// javob esa har doim <c>{ code, message }</c> (o'zbekcha).
/// </para>
/// </summary>
/// <param name="Body">ODDIY MATN (§3.3 N2) — qator tashlash saqlanadi, qolgani matnning o'zi.</param>
/// <param name="ImageUrl">Ixtiyoriy (§3.3 N7): rasm topmaguncha e'lon qila olmaydigan maktab e'lon qilmaydi.</param>
/// <param name="Audience">Bo'sh bo'lmasin; qiymatlar — <see cref="NewsAudience.All"/>.</param>
public record NewsSaveRequest(
    string? Title,
    string? Body,
    string? ImageUrl,
    IReadOnlyList<string>? Audience);

/// <summary>
/// E'lon qilish so'rovi (§5.4). Yagona bayroq — Telegram: bu mahsulotdagi
/// YAGONA tashqi kanal (`CLAUDE.md`), shuning uchun bu yerda kanal
/// tanlagich emas, ha/yo'q turadi. Tana umuman kelmasa — sukut bo'yicha
/// <c>true</c> (§3.3 N4: composer'da belgi yoqilgan holda turadi).
/// </summary>
public record NewsPublishRequest(bool SendTelegram = true);

/// <summary>
/// Sahifalangan ro'yxat (§5.4: <c>{ total, rows }</c>).
/// <see cref="Total"/> — FILTRGA tushgan qatorlar soni (sahifadagi emas):
/// ekrandagi sahifalagich aynan shundan hisoblanadi.
/// </summary>
public record NewsListResultDto(int Total, IReadOnlyList<NewsAdminDto> Rows);
