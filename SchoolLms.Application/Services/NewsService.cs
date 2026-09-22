using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Yangiliklar — admin CRUD ning QOIDALARI.
//  Spetsifikatsiya: docs/modules/sales-marketing.md §5.4, §3.3. Vazifa: SM-5.
// ===========================================================================
//
//  NEGA STATIK VA NEGA DI'DA YO'Q
//  ------------------------------
//  `Program.cs` bu to'lqinda boshqa agent qo'lida (§7.1: SM-12 ning yagona
//  wiring passi), ya'ni yangi xizmatni ro'yxatdan o'tkazib bo'lmaydi.
//  Loyihada bunday hol uchun tayyor naqsh bor — `CertificateService`,
//  `StudentProfileBuilder`: statik klass, `IAppDbContext` parametr
//  sifatida. Controller `AppDbContext` ni DI'dan oladi va shu yerga
//  uzatadi.
//
//  MATN ODDIY MATN BO'LIB QOLADI (§3.3 N2)
//  ---------------------------------------
//  Bu faylda sanitizer ham, markdown ham, HTML ham yo'q va QO'SHILMAYDI:
//  `body` qanday kelsa shunday saqlanadi. Uchala klient uni
//  `whitespace-pre-line` bilan chizadi. Sanitizer kerak bo'lgan kun —
//  spetsifikatsiya o'zgargan kun, kod o'zgargan kun emas.
//
//  E'LON QILISH — ALOHIDA QADAM VA U IKKI MARTA SAQLAYDI
//  -----------------------------------------------------
//  `PublishAsync` ataylab ikki marta `SaveChanges` qiladi. Sababi
//  metodning o'zida yozilgan: birinchi saqlash lenta yozuvini BIRORTA
//  Telegram xabari ketishidan oldin bazaga qo'yadi, ya'ni takroriy
//  bosish 409 ga uriladi va bitta ota-ona ikkita xabar olmaydi.

/// <summary>
/// E'lon qilish paytidagi Telegram tarqatmasi — SM-6 ning
/// <c>NewsTelegramNotifier.cs</c> fayli shu shartnomani bajaradi.
///
/// <para>
/// <b>Nega interfeys SM-5 da yozilgan.</b> SM-6 (§7.2) mening fayllarimni
/// tahrirlamasligi uchun: tarqatmani ulash uchun unga faqat shu
/// interfeysning realizatsiyasi va <c>Program.cs</c> dagi bitta ro'yxatga
/// olish satri kerak (SM-12), <see cref="NewsService"/> ga esa tegish
/// shart emas.
/// </para>
///
/// <para><b>Realizatsiyaga qo'yiladigan uchta shart:</b></para>
/// <list type="number">
///   <item><b>XATO TASHLAMAYDI.</b> Bitta chatga yuborilmagan xabar —
///     muvaffaqiyatsiz xabar, muvaffaqiyatsiz E'LON emas. Lenta yozuvi bu
///     amalning asosiy mahsuloti va u allaqachon saqlangan bo'ladi;
///     istisno esa uni 500 ga aylantirib, admin ko'rgan yagona natijani
///     yo'q qilardi.</item>
///   <item><b>Chat id'lar DUBLIKATSIZ</b> (§3.3 N6): bitta ota-ona
///     <c>telegram_registrations</c> da ham, <c>telegram_accounts</c> da
///     ham bo'lishi mumkin. Ikki marta yuborish — funksiyani o'chirib
///     qo'yiladigan darajadagi nuqson.</item>
///   <item><b>Bot sozlanmagan bo'lsa</b> (<c>TelegramService.IsConfigured
///     == false</c>) — <see cref="NewsTelegramResult.NotSent"/>. E'lon
///     baribir muvaffaqiyatli bo'ladi, hisoblagichlar 0 bo'lib qoladi
///     (§3.3 N4, §8.3).</item>
/// </list>
/// </summary>
public interface INewsTelegramNotifier
{
    /// <summary>
    /// Yangilikni auditoriyasiga yuboradi. Auditoriya
    /// <see cref="NewsItem.ForEmployee"/> / <see cref="NewsItem.ForParent"/> /
    /// <see cref="NewsItem.ForStudent"/> dan o'qiladi — qo'shimcha parametr
    /// yo'q, chunki qator allaqachon haqiqatning manbai.
    ///
    /// <para>
    /// <see cref="NewsService.PublishAsync"/> uni bitta e'lon uchun AYNAN
    /// BIR MARTA va <c>published_at</c> saqlangandan KEYIN chaqiradi.
    /// </para>
    /// </summary>
    Task<NewsTelegramResult> SendAsync(NewsItem news, CancellationToken ct = default);
}

/// <summary>
/// Tarqatmaning natijasi — <c>news</c> qatoridagi uchta ustunga yoziladi
/// (§4.3: <c>telegram_sent_at</c>, <c>telegram_recipient_count</c>,
/// <c>telegram_sent_count</c>).
/// </summary>
/// <param name="Attempted">
/// Tarqatma HAQIQATAN ishga tushdimi. <c>false</c> — bot sozlanmagan yoki
/// admin "Telegram orqali ham yuborilsin" ni o'chirgan; bunda
/// <c>telegram_sent_at</c> NULL bo'lib qoladi, ya'ni ekran "yuborilmagan"
/// deb yozadi. <c>true</c> bo'lib, ikkala son 0 chiqishi ham normal holat:
/// bot bor, lekin hali hech kim botga yozilmagan.
/// </param>
/// <param name="RecipientCount">Topilgan chat id'lar soni (dublikatsiz).</param>
/// <param name="SentCount">Telegram qabul qilgan xabarlar soni.</param>
public readonly record struct NewsTelegramResult(bool Attempted, int RecipientCount, int SentCount)
{
    /// <summary>Tarqatma bo'lmadi: bot sozlanmagan yoki so'ralmagan.</summary>
    public static readonly NewsTelegramResult NotSent = new(false, 0, 0);
}

/// <summary>
/// Yangilik qatorining butun hayoti: tekshirish, auditoriya o'girish,
/// ro'yxat so'rovi, qoralama → e'lon → e'londan qaytarish → arxiv.
/// Audit qatorini ham SHU YER yozadi (controller emas), shunda "yangilik
/// o'zgardi" degan yozuv o'zgarish bilan bir joyda turadi.
///
/// <para>
/// <b><c>SaveChanges</c> — faqat <see cref="PublishAsync"/> da.</b> Qolgan
/// metodlar qatorni o'zgartiradi va qaytadi; saqlashni controller qiladi
/// (<c>CertificateService.ApplyAsync</c> dagi kabi). Sabab: bitta
/// so'rovda bitta tranzaksiya bo'lsin. E'lon qilish esa istisno va
/// istisnoning sababi o'sha metodda yozilgan.
/// </para>
/// </summary>
public static class NewsService
{
    /// <summary>
    /// Audit jurnalidagi yorliq — <see cref="AuditService.EntityNews"/>.
    /// </summary>
    public const string AuditEntity = AuditService.EntityNews;

    /// <summary>Ro'yxatning sukutdagi sahifa hajmi (§5.3 bilan bir xil).</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Sahifa hajmining shifti — bitta so'rov butun jadvalni tortib olmasin.</summary>
    public const int MaxPageSize = 200;

    public const string TitleRequiredMessage = "Yangilik sarlavhasini yozing";
    public const string BodyRequiredMessage = "Yangilik matnini yozing";

    public const string AudienceRequiredMessage =
        "Yangilik kimga ko'rinishini tanlang: xodim, ota-ona yoki o'quvchi";

    public const string AudienceUnknownMessage =
        "Qatnashuvchi qiymati noto'g'ri — faqat employee, parent yoki student";

    public const string AudienceLockedMessage =
        "Yangilik allaqachon e'lon qilingan — kimga ko'rinishini o'zgartirish uchun "
        + "avval uni e'londan qaytaring.";

    public const string AlreadyPublishedMessage =
        "Bu yangilik allaqachon e'lon qilingan. Telegram xabari ikkinchi marta yuborilmaydi.";

    public const string NotFoundMessage = "Yangilik topilmadi";

    /// <summary>Audit jumlalaridagi auditoriya yorliqlari (§5.4 misoli: "ota-ona, o'quvchi").</summary>
    private static readonly Dictionary<string, string> AudienceLabels = new(StringComparer.Ordinal)
    {
        [NewsAudience.Employee] = "xodim",
        [NewsAudience.Parent] = "ota-ona",
        [NewsAudience.Student] = "o'quvchi",
    };

    /* ===================================================================
     *  1. Auditoriya — uchta boolean ⇄ bitta massiv (§3.3 N5)
     * ================================================================ */

    /// <summary>Qatordagi uchta bayroq → <c>audience[]</c>, doimiy tartibda.</summary>
    public static IReadOnlyList<string> AudienceOf(NewsItem row)
    {
        var audience = new List<string>(3);
        if (row.ForEmployee) audience.Add(NewsAudience.Employee);
        if (row.ForParent) audience.Add(NewsAudience.Parent);
        if (row.ForStudent) audience.Add(NewsAudience.Student);
        return audience;
    }

    /// <summary>
    /// So'rovdagi massiv → uchta bayroq. Bu yagona joy: boshqa hech qayerda
    /// <c>"parent"</c> satri bilan <see cref="NewsItem.ForParent"/> yonma-yon
    /// yozilmaydi.
    /// </summary>
    private static void ApplyAudience(NewsItem row, IReadOnlyCollection<string> audience)
    {
        row.ForEmployee = audience.Contains(NewsAudience.Employee);
        row.ForParent = audience.Contains(NewsAudience.Parent);
        row.ForStudent = audience.Contains(NewsAudience.Student);
    }

    /// <summary>
    /// So'rovdagi qiymatlarni tozalaydi: bo'sh satrlar tushadi, katta harf
    /// kichrayadi, takror olib tashlanadi. NOTANISH qiymat SAQLANADI —
    /// uni <see cref="Validate"/> ko'rib, xato qaytarishi uchun; jimgina
    /// tashlab yuborish "parnet" deb yozilgan so'rovni "hech kimga
    /// ko'rinmaydigan" yangilikka aylantirardi.
    /// </summary>
    private static List<string> Normalize(IReadOnlyList<string>? audience) =>
        (audience ?? [])
            .Select(a => (a ?? "").Trim().ToLowerInvariant())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Audit jumlasi uchun: "ota-ona, o'quvchi".</summary>
    public static string AudienceText(NewsItem row) =>
        string.Join(", ", AudienceOf(row).Select(a => AudienceLabels[a]));

    /* ===================================================================
     *  2. Tekshirish (§5.6 `validation`)
     * ================================================================ */

    /// <summary>
    /// Saqlash so'rovini tekshiradi. <c>null</c> — hammasi joyida, aks holda
    /// o'zbekcha jumla (controller uni <c>{ code:"validation", message }</c>
    /// ga o'raydi).
    ///
    /// <para>
    /// Uchala shart bazada ham bor (<c>ck_news_title</c>, <c>ck_news_body</c>,
    /// <c>ck_news_audience</c>) — lekin baza ularni 23514 raqami bilan
    /// aytadi. Bu yerdagi tekshiruv o'sha raqamni odam o'qiydigan jumlaga
    /// aylantiradi; CHECK esa poyga holatida oxirgi to'siq bo'lib qoladi.
    /// </para>
    ///
    /// <para>
    /// <b>Uzunlik chegarasi ATAYLAB yo'q.</b> Ustunlar <c>text</c>, va
    /// e'lon matnini "juda uzun" deb rad etadigan raqamni spetsifikatsiya
    /// aytmagan. Telegram xabarining 4096 belgilik chegarasi tarqatmaning
    /// ishi (SM-6), yozishning emas: matn lentada baribir to'liq turadi.
    /// </para>
    /// </summary>
    public static string? Validate(NewsSaveRequest req)
    {
        if ((req.Title ?? "").Trim().Length == 0) return TitleRequiredMessage;
        if ((req.Body ?? "").Trim().Length == 0) return BodyRequiredMessage;
        if (!SurveyService.IsSafeUrl(req.ImageUrl)) return SurveyService.UnsafeUrlMessage;

        var audience = Normalize(req.Audience);
        if (audience.Count == 0) return AudienceRequiredMessage;
        if (audience.Any(a => !NewsAudience.All.Contains(a))) return AudienceUnknownMessage;

        return null;
    }

    /// <summary>
    /// So'rov auditoriyani O'ZGARTIRYAPTIMI. E'lon qilingan yangilikda bu
    /// 409 <c>news_published</c> beradi (§5.4): birinchi auditoriyaga ketgan
    /// Telegram xabarini qaytarib bo'lmaydi, ya'ni "faqat xodimlarga" deb
    /// o'zgartirilgan e'lon ota-onalarning telefonida o'sha joyda qolardi.
    ///
    /// <para>Sarlavha va matnni tahrirlash esa TAQIQLANMAGAN — ularda bunday yolg'on yo'q.</para>
    /// </summary>
    public static bool AudienceChanged(NewsItem row, NewsSaveRequest req)
    {
        var audience = Normalize(req.Audience);
        return row.ForEmployee != audience.Contains(NewsAudience.Employee)
            || row.ForParent != audience.Contains(NewsAudience.Parent)
            || row.ForStudent != audience.Contains(NewsAudience.Student);
    }

    /* ===================================================================
     *  3. O'qish
     * ================================================================ */

    /// <summary>Qator → DTO (§5.4). Auditoriya shu yerda massivga aylanadi.</summary>
    public static NewsAdminDto ToDto(NewsItem row) => new(
        row.Id, row.Title, row.Body, row.ImageUrl,
        AudienceOf(row),
        row.PublishedAt,
        row.AuthorName,
        row.TelegramSentAt, row.TelegramRecipientCount, row.TelegramSentCount,
        row.CreatedAt, row.UpdatedAt);

    /// <summary>
    /// <c>?state=</c> filtri (§5.4). Notanish qiymat — <see cref="NewsState.All"/>:
    /// ro'yxat FILTR, forma emas, va noto'g'ri yozilgan filtr uchun 400
    /// qaytarish ekranni bo'sh qoldirardi.
    ///
    /// <para>
    /// <b>Yumshoq o'chirilgan qatorlar</b> faqat <see cref="NewsState.Archived"/>
    /// da ko'rinadi — qolgan uchtasida ular YO'Q (§3.3 N8).
    /// </para>
    /// </summary>
    public static IQueryable<NewsItem> Filter(IQueryable<NewsItem> query, string? state) =>
        (state ?? "").Trim().ToLowerInvariant() switch
        {
            NewsState.Draft => query.Where(n => n.DeletedAt == null && n.PublishedAt == null),
            NewsState.Published => query.Where(n => n.DeletedAt == null && n.PublishedAt != null),
            NewsState.Archived => query.Where(n => n.DeletedAt != null),
            _ => query.Where(n => n.DeletedAt == null),
        };

    /// <summary>
    /// Sahifalangan ro'yxat, yangisi tepada (§5.4).
    ///
    /// <para>
    /// <b>Birorta JOIN yo'q va bu tasodif emas.</b> Ekranga kerak bo'lgan
    /// yagona "begona" qiymat — muallif ismi, va u <c>news.author_name</c>
    /// da NUSXA bo'lib turadi (§4.3). Ya'ni bu ro'yxatda N+1 ning manbai
    /// yo'q: 20 ta qator — bitta <c>SELECT</c> va bitta <c>COUNT</c>.
    /// </para>
    ///
    /// <para>
    /// Tartib <c>created_at desc</c>, <c>id</c> bilan uziladi: teng
    /// vaqtli ikki qator sahifalar orasida sakrab yurmasligi uchun.
    /// </para>
    /// </summary>
    public static async Task<NewsListResultDto> ListAsync(
        IAppDbContext db, string? state, int? page, int? pageSize, CancellationToken ct = default)
    {
        var query = Filter(db.News.AsNoTracking(), state);

        var total = await query.CountAsync(ct);
        var take = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var skip = (Math.Max(page ?? 1, 1) - 1) * take;

        var rows = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        return new NewsListResultDto(total, rows.Select(ToDto).ToList());
    }

    /* ===================================================================
     *  4. Yozish
     * ================================================================ */

    /// <summary>
    /// Yangi yangilik — HAR DOIM qoralama (§3.3 N3): e'lon qilish alohida
    /// qadam, chunki u haqiqiy odamlarga haqiqiy xabar yuboradi.
    /// <c>SaveChanges</c> QILMAYDI.
    /// </summary>
    /// <param name="authorName">
    /// Muallif ismining NUSXASI (<c>Broadcast.SenderName</c> kabi): keyin
    /// xodimning nomi o'zgarsa ham, hisobi o'chsa ham bu jumla o'zgarmaydi.
    /// </param>
    public static NewsItem Create(
        IAppDbContext db, AuditService audit, NewsSaveRequest req,
        string authorId, string authorName)
    {
        var row = new NewsItem
        {
            Title = (req.Title ?? "").Trim(),
            Body = (req.Body ?? "").Trim(),
            ImageUrl = Clean(req.ImageUrl),
            AuthorId = authorId,
            AuthorName = authorName,
        };
        ApplyAudience(row, Normalize(req.Audience));

        db.News.Add(row);
        audit.Record(AuditEntity, row.Id.ToString(), "create",
            $"Yangilik yozildi: {row.Title} — {AudienceText(row)}",
            after: Snapshot(row));

        return row;
    }

    /// <summary>
    /// Sarlavha, matn, rasm va auditoriyani yangilaydi. <c>SaveChanges</c>
    /// QILMAYDI. Auditoriyani e'lon qilingan yangilikda o'zgartirish mumkin
    /// emasligini chaqiruvchi <see cref="AudienceChanged"/> bilan oldindan
    /// tekshiradi.
    /// </summary>
    public static void Update(AuditService audit, NewsItem row, NewsSaveRequest req)
    {
        var before = Snapshot(row);

        row.Title = (req.Title ?? "").Trim();
        row.Body = (req.Body ?? "").Trim();
        row.ImageUrl = Clean(req.ImageUrl);
        ApplyAudience(row, Normalize(req.Audience));
        row.UpdatedAt = AppClock.NowInstant;

        audit.Record(AuditEntity, row.Id.ToString(), "update",
            $"Yangilik tahrirlandi: {row.Title}",
            before: before, after: Snapshot(row));
    }

    /// <summary>
    /// E'lon qilish: lentaga chiqaradi va (so'ralgan bo'lsa) Telegram
    /// tarqatmasini ishga tushiradi, natijani qatorga yozadi (§3.3 N4).
    /// Chaqiruvchi OLDIN <c>published_at is null</c> ekanini tekshiradi —
    /// aks holda 409 <c>news_already_published</c> (§5.4).
    ///
    /// <para>
    /// <b>Nega ikkita <c>SaveChanges</c>.</b> Birinchisi
    /// <c>published_at</c> ni BIRORTA xabar ketishidan oldin bazaga
    /// qo'yadi. Shunda ikki marta bosilgan tugma ikkinchi safar 409 ga
    /// uriladi va bitta ota-ona ikkita xabar olmaydi. Teskari tartibda
    /// (avval yuborib, keyin saqlash) saqlash xatosi eng yomon holatni
    /// berardi: xabarlar ketgan, lenta esa bo'sh, va admin qayta
    /// bosadi.
    /// </para>
    /// <para>
    /// Ikkinchi saqlash — hisoblagichlar va audit. U yiqilsa e'lon joyida
    /// qoladi, faqat sonlar 0 bo'lib qoladi: bu ko'rinishdagi yo'qotish,
    /// ma'lumotdagi emas.
    /// </para>
    /// </summary>
    /// <param name="sendTelegram">Composer'dagi "Telegram orqali ham yuborilsin" belgisi.</param>
    /// <param name="notifier">
    /// SM-6 ning tarqatmasi. <c>null</c> — u hali ulanmagan (§7.2); bunda
    /// e'lon MUVAFFAQIYATLI bo'ladi va hisoblagichlar 0 bo'lib qoladi,
    /// ya'ni bot sozlanmagan holat bilan bir xil natija (§8.3).
    /// </param>
    /// <returns>
    /// <c>null</c> — yozuvni boshqa so'rov allaqachon e'lon qilgan (409).
    /// </returns>
    public static async Task<NewsTelegramResult?> PublishAsync(
        IAppDbContext db, AuditService audit, NewsItem row,
        bool sendTelegram, INewsTelegramNotifier notifier, CancellationToken ct = default)
    {
        // 1. Lenta yozuvi — ATOMAR "egallash". O'qib-keyin-yozish bo'lganda ikki admin
        //    (yoki ikki oyna) bir vaqtda `published_at is null` ni ko'rib, ikkalasi ham
        //    tarqatardi — har bir ota-onaga ikki nusxa. Bitta UPDATE ... WHERE
        //    published_at is null esa faqat BITTA so'rovga 1 qator qaytaradi.
        var now = AppClock.NowInstant;
        var claimed = await db.News
            .Where(x => x.Id == row.Id && x.PublishedAt == null && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PublishedAt, (DateTimeOffset?)now)
                .SetProperty(x => x.UpdatedAt, (DateTimeOffset?)now), ct);
        if (claimed == 0) return null;
        row.PublishedAt = now;
        row.UpdatedAt = now;

        // 2. Telegram. Shartnoma bo'yicha xato TASHLAMAYDI — interfeys izohiga qarang.
        //    `CancellationToken.None` ATAYLAB: e'lon allaqachon bazada; admin oynani
        //    yopsa yoki proksi 100 s da uzsa, qolgan ota-onalar jimgina tashlab
        //    ketilmasin va hisoblagichlar saqlanmay qolmasin (aks holda "qayta e'lon"
        //    hammaga ikkinchi nusxa yuborardi). `MessagesController.SendBroadcast`
        //    ham `ct` uzatmaydi.
        var delivery = sendTelegram
            ? await notifier.SendAsync(row, CancellationToken.None)
            : NewsTelegramResult.NotSent;

        if (delivery.Attempted)
        {
            row.TelegramSentAt = AppClock.NowInstant;
            row.TelegramRecipientCount = delivery.RecipientCount;
            row.TelegramSentCount = delivery.SentCount;
        }

        // 3. Hisoblagichlar + audit — bitta saqlash (u ham bekor qilinmaydi, 2-qadamga qarang).
        audit.Record(AuditEntity, row.Id.ToString(), "publish",
            PublishSummary(row, delivery),
            after: Snapshot(row));
        await db.SaveChangesAsync(CancellationToken.None);

        return delivery;
    }

    /// <summary>
    /// E'londan qaytarish: lentadan yashiradi (§3.3 N3). <c>SaveChanges</c>
    /// QILMAYDI.
    ///
    /// <para>
    /// <b>Telegram hisoblagichlari TEGILMAYDI.</b> Ketgan xabarni qaytarib
    /// bo'lmaydi, ya'ni <c>telegram_sent_at</c> ni tozalash tarixni
    /// yolg'onga aylantirardi: "yuborilmagan" deb turgan qator, aslida
    /// 412 ta telefonda ochilgan xabar. Ekran buni tasdiqdan oldin
    /// aytadi.
    /// </para>
    /// </summary>
    public static void Unpublish(AuditService audit, NewsItem row)
    {
        var before = Snapshot(row);

        row.PublishedAt = null;
        row.UpdatedAt = AppClock.NowInstant;

        audit.Record(AuditEntity, row.Id.ToString(), "unpublish",
            $"Yangilik e'londan qaytarildi: {row.Title}",
            before: before, after: Snapshot(row));
    }

    /// <summary>
    /// YUMSHOQ o'chirish (§3.3 N8): qator qoladi, <c>deleted_at</c> qo'yiladi.
    /// <c>SaveChanges</c> QILMAYDI.
    ///
    /// <para>
    /// Telegram orqali ketgan, keyin admin ro'yxatidan G'OYIB bo'lgan va
    /// matnini hech kim ayta olmaydigan yangilik — bu qo'ng'iroq
    /// qilinadigan nosozlik. Shuning uchun qator arxivda turadi va
    /// <c>state=archived</c> bilan o'qiladi; barcha lentalardan esa
    /// yo'qoladi (<c>ix_news_feed</c> ham uni indekslamaydi).
    /// </para>
    /// </summary>
    public static void SoftDelete(AuditService audit, NewsItem row)
    {
        var before = Snapshot(row);

        row.DeletedAt = AppClock.NowInstant;
        row.UpdatedAt = row.DeletedAt;

        audit.Record(AuditEntity, row.Id.ToString(), "delete",
            $"Yangilik arxivga olindi: {row.Title}",
            before: before, after: Snapshot(row));
    }

    /* ===================================================================
     *  5. Yordamchilar
     * ================================================================ */

    /// <summary>
    /// §5.4 dagi jumla: "Yangilik e'lon qilindi: Ota-onalar yig'ilishi —
    /// ota-ona, o'quvchi · 408/412".
    /// </summary>
    private static string PublishSummary(NewsItem row, NewsTelegramResult delivery)
    {
        var telegram = delivery.Attempted
            ? $"{delivery.SentCount}/{delivery.RecipientCount}"
            // Sababini (bot sozlanmagan / so'ralmagan) ataylab yozmaymiz:
            // audit qatori faqat o'zi BILGAN narsani aytadi.
            : "Telegram: yuborilmadi";

        return $"Yangilik e'lon qilindi: {row.Title} — {AudienceText(row)} · {telegram}";
    }

    /// <summary>
    /// Audit uchun qator surati. To'liq DTO emas: <c>created_at</c> hali
    /// bazadan qaytmagan bo'lishi mumkin (u <c>default now()</c>), ya'ni
    /// DTO jurnalga 0001-01-01 yozib qo'yardi.
    /// </summary>
    private static object Snapshot(NewsItem row) => new
    {
        row.Title,
        row.Body,
        row.ImageUrl,
        Audience = AudienceOf(row),
        row.PublishedAt,
        row.TelegramSentAt,
        row.TelegramRecipientCount,
        row.TelegramSentCount,
        row.DeletedAt,
    };

    /// <summary>Bo'sh satr — bu "rasm yo'q", ya'ni <c>null</c> (§3.3 N7).</summary>
    private static string? Clean(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
