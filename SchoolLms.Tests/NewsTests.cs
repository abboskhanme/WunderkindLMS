using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Yangiliklar — qoralama → e'lon → lentalar, auditoriya va Telegram.
//  docs/modules/sales-marketing.md §8.3 (NewsTests), §3.3 N3–N9, §5.4, §5.5.
// ===========================================================================
//
//  BESHTA LENTA, BITTA SAVOL: "KIM NIMANI KO'RADI"
//  -----------------------------------------------
//    tg/parent/news        — ota-ona (Mini App)        → for_parent
//    student/news (parent) — ota-ona (portal)          → for_parent
//    student/news (student)— o'quvchi (portal)         → for_student
//    tg/teacher/news       — o'qituvchi (Mini App)     → for_employee
//    admin/news/feed       — admin panel               → for_employee
//  Har bir hayot bosqichi (qoralama, e'lon, qaytarish, arxiv) BESHALASIGA
//  qarab tekshiriladi. Auditoriya oqib ketishi — hech kim sezmaydigan xato:
//  xodimlarga yozilgan e'lon ota-onaning telefonida paydo bo'ladi.
//
//  TELEGRAM — TARMOQQA HECH NARSA CHIQMAYDI
//  ---------------------------------------
//  Asosiy host'da bot SOZLANMAGAN (ApiFactory `Telegram__BotToken=""`).
//  Haqiqiy tarqatma kerak bo'lgan joyda `TelegramService` soxta
//  `IHttpClientFactory` bilan quriladi (`ReceiptTests` naqshi) — har so'rov
//  `TelegramRecorder` da qoladi va sanaladi.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class NewsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string News = "/api/admin/news";

    private readonly List<TestDatabase> _freshDatabases = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var database in _freshDatabases)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. Qoralama → e'lon → to'g'ri lenta (N3, §5.5)
    // =====================================================================

    /// <summary>
    /// Qoralama hech bir lentada yo'q. E'lon qilingach — FAQAT ota-ona
    /// lentalarida; o'quvchi, o'qituvchi va admin lentasida yo'q. Har bir
    /// lentadagi HAR BIR yozuv (umumiy bazada boshqa testlarniki ham) o'z
    /// auditoriyasiga tegishli va e'lon qilingan, o'chirilmagan.
    /// </summary>
    [Fact]
    public async Task Qoralama_hech_bir_lentada_yoq_elon_qilingach_faqat_oz_auditoriyasida()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var readers = await ReadersAsync(admin);
        var draft = await CreateAsync(admin, ["parent"]);
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("publishedAt").ValueKind);
        var id = draft.GetProperty("id").GetGuid();

        foreach (var feed in readers.All)
            Assert.DoesNotContain(id, await FeedIdsAsync(feed));

        var published = await PublishAsync(admin, id);
        Assert.NotEqual(JsonValueKind.Null, published.GetProperty("publishedAt").ValueKind);

        Assert.Contains(id, await FeedIdsAsync(readers.TgParent));
        Assert.Contains(id, await FeedIdsAsync(readers.PortalParent));
        Assert.DoesNotContain(id, await FeedIdsAsync(readers.PortalStudent));
        Assert.DoesNotContain(id, await FeedIdsAsync(readers.TgTeacher));
        Assert.DoesNotContain(id, await FeedIdsAsync(readers.AdminFeed));

        await using var db = NewDb();
        await AssertEveryItemBelongsAsync(db, readers.TgParent, n => n.ForParent);
        await AssertEveryItemBelongsAsync(db, readers.PortalParent, n => n.ForParent);
        await AssertEveryItemBelongsAsync(db, readers.PortalStudent, n => n.ForStudent);
        await AssertEveryItemBelongsAsync(db, readers.TgTeacher, n => n.ForEmployee);
        await AssertEveryItemBelongsAsync(db, readers.AdminFeed, n => n.ForEmployee);
    }

    /// <summary>
    /// O'quvchi lentasining shakli admin DTO'sidan ATAYLAB kichik (§5.5):
    /// auditoriya massivi, hisoblagichlar va muallif id'si yo'q.
    /// </summary>
    [Fact]
    public async Task Lenta_yozuvida_auditoriya_hisoblagich_va_muallif_idsi_yoq()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var parent = await fixture.Api.ClientAsAsync("parent");
        var created = await CreateAsync(admin, ["parent"], body: "25-sentabr\nsoat 15:00");
        var id = created.GetProperty("id").GetGuid();
        await PublishAsync(admin, id);

        using var doc = JsonDocument.Parse(await parent.GetStringAsync("/api/tg/parent/news?take=50"));
        var item = Assert.Single(doc.RootElement.EnumerateArray().ToList(), e => e.GetProperty("id").GetGuid() == id);
        Assert.Equal(
            ["authorName", "body", "id", "imageUrl", "publishedAt", "title"],
            item.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        // Matn ODDIY matn, qator uzilishi bilan AYNAN saqlanadi (N2).
        Assert.Equal("25-sentabr\nsoat 15:00", item.GetProperty("body").GetString());
    }

    // =====================================================================
    //  2. E'londan qaytarish (N3)
    // =====================================================================

    [Fact]
    public async Task Elondan_qaytarilgan_yangilik_hamma_lentadan_yoqoladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var readers = await ReadersAsync(admin);
        var id = (await CreateAsync(admin, ["employee", "parent", "student"])).GetProperty("id").GetGuid();
        await PublishAsync(admin, id);
        foreach (var feed in readers.All)
            Assert.Contains(id, await FeedIdsAsync(feed));

        var response = await admin.PostAsync($"{News}/{id}/unpublish", null);
        var dto = await OkJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("publishedAt").ValueKind);

        foreach (var feed in readers.All)
            Assert.DoesNotContain(id, await FeedIdsAsync(feed));

        Assert.Contains(id, await AdminListIdsAsync(admin, "draft"));
        Assert.DoesNotContain(id, await AdminListIdsAsync(admin, "published"));
        await using var db = NewDb();
        var row = await db.News.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.Null(row.PublishedAt);
        Assert.Null(row.DeletedAt);
    }

    // =====================================================================
    //  3. Yumshoq o'chirish (N8)
    // =====================================================================

    /// <summary>
    /// O'chirilgan yangilik HAMMA lentadan yo'qoladi, lekin admin qatori qoladi:
    /// <c>GET /{id}</c> 200, <c>state=archived</c> da bor, boshqa holatlarda yo'q.
    /// Arxivdagi qatorga yozish amallari 404, qayta o'chirish esa xato emas (204).
    /// </summary>
    [Fact]
    public async Task Ochirilgan_yangilik_hamma_lentadan_yoqoladi_admin_qatori_qoladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var readers = await ReadersAsync(admin);
        var created = await CreateAsync(admin, ["employee", "parent", "student"]);
        var id = created.GetProperty("id").GetGuid();
        var title = created.GetProperty("title").GetString();
        await PublishAsync(admin, id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{News}/{id}")).StatusCode);

        foreach (var feed in readers.All)
            Assert.DoesNotContain(id, await FeedIdsAsync(feed));

        var kept = await OkJsonAsync(await admin.GetAsync($"{News}/{id}"));
        Assert.Equal(title, kept.GetProperty("title").GetString());
        Assert.Contains(id, await AdminListIdsAsync(admin, "archived"));
        Assert.DoesNotContain(id, await AdminListIdsAsync(admin, "all"));
        Assert.DoesNotContain(id, await AdminListIdsAsync(admin, "published"));

        DateTimeOffset? deletedAt;
        await using (var db = NewDb())
        {
            deletedAt = (await db.News.AsNoTracking().SingleAsync(n => n.Id == id)).DeletedAt;
            Assert.NotNull(deletedAt);
        }

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync($"{News}/{id}", SaveBody("Tiriltirish", ["parent"]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync($"{News}/{id}/publish", new { sendTelegram = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{News}/{id}")).StatusCode);

        await using var after = NewDb();
        var row = await after.News.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.Equal(deletedAt, row.DeletedAt);
        Assert.Equal(title, row.Title);
    }

    // =====================================================================
    //  4. audience[] ⇄ uchta boolean (N5)
    // =====================================================================

    /// <summary>
    /// So'rovdagi massiv (har qanday tartib, registr, bo'shliq, takror bilan) →
    /// bazada uchta bayroq → javobda KANONIK tartibdagi massiv. Keyin tahrir
    /// bilan to'ldiruvchi to'plamga o'tkaziladi — teskari yo'nalish ham ishlaydi.
    /// </summary>
    [Theory]
    [InlineData("employee", true, false, false, "employee")]
    [InlineData("parent", false, true, false, "parent")]
    [InlineData("student", false, false, true, "student")]
    [InlineData("student,employee", true, false, true, "employee,student")]
    [InlineData("parent,employee", true, true, false, "employee,parent")]
    [InlineData(" PARENT ,parent,Student", false, true, true, "parent,student")]
    [InlineData("student,parent,employee", true, true, true, "employee,parent,student")]
    public async Task Auditoriya_massivi_va_uchta_bayroq_ikki_tomonga_mos(
        string requested, bool employee, bool parent, bool student, string canonical)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var created = await CreateAsync(admin, requested.Split(','));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(canonical.Split(','), AudienceOf(created));
        await AssertFlagsAsync(id, employee, parent, student);
        Assert.Equal(canonical.Split(','), AudienceOf(await OkJsonAsync(await admin.GetAsync($"{News}/{id}"))));

        // Teskari yo'nalish: to'ldiruvchi to'plam (hammasi tanlangan bo'lsa — faqat ota-ona).
        var flipped = new List<string>();
        if (!employee) flipped.Add("employee");
        if (!parent) flipped.Add("parent");
        if (!student) flipped.Add("student");
        if (flipped.Count == 0) flipped.Add("parent");

        var updated = await OkJsonAsync(await admin.PutAsJsonAsync($"{News}/{id}", SaveBody("Tahrir", flipped.ToArray())));
        Assert.Equal(flipped.ToArray(), AudienceOf(updated));
        await AssertFlagsAsync(id, flipped.Contains("employee"), flipped.Contains("parent"), flipped.Contains("student"));
    }

    [Theory]
    [InlineData("", "Yangilik kimga ko'rinishini tanlang: xodim, ota-ona yoki o'quvchi")]
    [InlineData("parnet", "Qatnashuvchi qiymati noto'g'ri — faqat employee, parent yoki student")]
    [InlineData("parent,teacher", "Qatnashuvchi qiymati noto'g'ri — faqat employee, parent yoki student")]
    public async Task Auditoriya_bosh_yoki_notanish_bolsa_400(string requested, string message)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var title = $"Auditoriyasiz {SalesMarketingKit.Tag()}";
        string[] audience = requested.Length == 0 ? [] : requested.Split(',');

        var response = await admin.PostAsJsonAsync(News, SaveBody(title, audience));

        await AssertValidationAsync(response, message);
        await using var db = NewDb();
        Assert.False(await db.News.AnyAsync(n => n.Title == title));
    }

    [Theory]
    [InlineData("   ", "Matn", "Yangilik sarlavhasini yozing")]
    [InlineData("Sarlavha", "  ", "Yangilik matnini yozing")]
    public async Task Sarlavha_yoki_matn_bosh_bolsa_400(string title, string body, string message)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var marker = $"bosh-{SalesMarketingKit.Tag()}";

        var response = await admin.PostAsJsonAsync(News,
            new { title, body, imageUrl = marker, audience = new[] { "parent" } });

        await AssertValidationAsync(response, message);
        await using var db = NewDb();
        Assert.False(await db.News.AnyAsync(n => n.ImageUrl == marker));
    }

    // =====================================================================
    //  5. Ikki marta e'lon qilish (§5.4) va e'londan keyingi tahrir
    // =====================================================================

    /// <summary>
    /// Ikkinchi "E'lon qilish" — 409 <c>news_already_published</c>, 200 emas:
    /// aks holda bitta ota-ona bir xil xabarni ikki marta olardi. Qator
    /// o'zgarmaydi (<c>published_at</c> birinchisiniki).
    /// </summary>
    [Fact]
    public async Task Ikki_marta_elon_qilish_409()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var id = (await CreateAsync(admin, ["parent"])).GetProperty("id").GetGuid();
        await PublishAsync(admin, id);

        DateTimeOffset? first;
        await using (var db = NewDb())
            first = (await db.News.AsNoTracking().SingleAsync(n => n.Id == id)).PublishedAt;

        var again = await admin.PostAsJsonAsync($"{News}/{id}/publish", new { sendTelegram = true });
        await AssertConflictAsync(again, "news_already_published",
            "Bu yangilik allaqachon e'lon qilingan. Telegram xabari ikkinchi marta yuborilmaydi.");

        await using var after = NewDb();
        Assert.Equal(first, (await after.News.AsNoTracking().SingleAsync(n => n.Id == id)).PublishedAt);
    }

    /// <summary>
    /// E'lon qilingan yangilikning AUDITORIYASI o'zgarmaydi (409
    /// <c>news_published</c>) — birinchi auditoriyaga ketgan nusxa joyida
    /// qolardi. Sarlavha/matnni tuzatish esa mumkin.
    /// </summary>
    [Fact]
    public async Task Elon_qilingan_yangilik_auditoriyasini_ozgartirib_bolmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var id = (await CreateAsync(admin, ["parent"])).GetProperty("id").GetGuid();
        await PublishAsync(admin, id);

        var widened = await admin.PutAsJsonAsync($"{News}/{id}", SaveBody("Kengaytirish", ["parent", "employee"]));
        await AssertConflictAsync(widened, "news_published",
            "Yangilik allaqachon e'lon qilingan — kimga ko'rinishini o'zgartirish uchun avval uni e'londan qaytaring.");
        await AssertFlagsAsync(id, employee: false, parent: true, student: false);

        var fixedTitle = await OkJsonAsync(await admin.PutAsJsonAsync($"{News}/{id}", SaveBody("Tuzatilgan sarlavha", ["parent"])));
        Assert.Equal("Tuzatilgan sarlavha", fixedTitle.GetProperty("title").GetString());
        Assert.NotEqual(JsonValueKind.Null, fixedTitle.GetProperty("publishedAt").ValueKind);
    }

    // =====================================================================
    //  6. Telegram — bot sozlanmagan (N4)
    // =====================================================================

    /// <summary>
    /// Bot sozlanmagan: e'lon baribir muvaffaqiyatli (200, lentada bor),
    /// hisoblagichlar 0, <c>telegramSentAt</c> bo'sh. Ota-ona Telegram'da
    /// ro'yxatdan o'tgan bo'lsa ham — yuboradigan bot yo'q.
    /// </summary>
    [Fact]
    public async Task Bot_sozlanmagan_bolsa_elon_200_hisoblagichlar_0()
    {
        Assert.False(fixture.Api.Services.GetRequiredService<TelegramService>().IsConfigured,
            "Asosiy host'da bot sozlangan — boshqa test tokenni o'rnatib qo'ygan; bu test ma'nosiz bo'lib qoladi.");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.TelegramRegistrations.Add(new TelegramRegistration
            {
                StudentId = $"nw-{SalesMarketingKit.Tag()}",
                ChatId = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000),
                ParentName = "Ota",
            });
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var parent = await fixture.Api.ClientAsAsync("parent");
        var id = (await CreateAsync(admin, ["parent"])).GetProperty("id").GetGuid();

        var dto = await PublishAsync(admin, id);

        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("publishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("telegramSentAt").ValueKind);
        Assert.Equal(0, dto.GetProperty("telegramRecipientCount").GetInt32());
        Assert.Equal(0, dto.GetProperty("telegramSentCount").GetInt32());
        Assert.Contains(id, await FeedIdsAsync(new Feed(parent, "/api/tg/parent/news?take=50")));

        await using var db2 = NewDb();
        var row = await db2.News.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.Null(row.TelegramSentAt);
        Assert.Equal(0, row.TelegramRecipientCount);
        Assert.Equal(0, row.TelegramSentCount);
    }

    // =====================================================================
    //  7. Telegram — oxirigacha: HTTP → notifier → har chatga BIR MARTA (N4, N6)
    // =====================================================================

    /// <summary>
    /// Bot sozlangan hosila host (HTTP soxta — tarmoqqa chiqmaydi). Botga
    /// kontakt ulashgan (ikki farzandi uchun ikki marta) VA Mini App'ga
    /// bog'langan ota-ona xabarni AYNAN BIR MARTA oladi; bitta ham chat ikki
    /// marta takrorlanmaydi; hisoblagichlar javobda, bazada va auditda bir xil.
    /// </summary>
    [Fact]
    public async Task Elon_Telegram_orqali_har_chatga_bir_marta_boradi_va_hisoblagich_yoziladi()
    {
        var chatId = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);
        var (parentUser, _) = await fixture.Api.SeedUserAsync("parent");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = chatId, UserId = parentUser.Id, DisplayName = "Ota" });
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = $"nw-a-{chatId}", ChatId = chatId, ParentName = "Ota" });
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = $"nw-b-{chatId}", ChatId = chatId, ParentName = "Ota" });
            await db.SaveChangesAsync();
        });

        var recorder = new TelegramRecorder();
        await using var host = fixture.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(recorder))));
        host.Services.GetRequiredService<TelegramService>().Set("test-token", "test_bot");

        var (adminUser, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var admin = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            fixture.Api.TokenFor(Roles.Admin, adminUser.Id, adminUser.FullName, adminUser.Email));

        var title = $"Yig'ilish {SalesMarketingKit.Tag()}";
        var id = (await CreateAsync(admin, ["parent"], title: title)).GetProperty("id").GetGuid();
        var dto = await PublishAsync(admin, id);

        var sent = recorder.Sent;
        Assert.NotEmpty(sent);
        Assert.Equal(1, sent.Count(s => s.ChatId == chatId));
        Assert.Equal(sent.Count, sent.Select(s => s.ChatId).Distinct().Count());
        Assert.All(sent, s => Assert.False(s.HasParseMode, "parse_mode yuborilmasligi kerak (N2, §5.4)"));
        Assert.All(sent, s => Assert.Equal("/bottest-token/sendMessage", s.Path));

        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("telegramSentAt").ValueKind);
        Assert.Equal(sent.Count, dto.GetProperty("telegramRecipientCount").GetInt32());
        Assert.Equal(sent.Count, dto.GetProperty("telegramSentCount").GetInt32());

        await using var db = NewDb();
        var row = await db.News.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.Equal(sent.Count, row.TelegramRecipientCount);
        Assert.Equal(sent.Count, row.TelegramSentCount);
        Assert.NotNull(row.TelegramSentAt);

        // Yuborilgan chatlar — aynan `RecipientsAsync` hisoblagan to'plam, har xabar — `BuildMessage`.
        var expected = await NewsTelegramNotifier.RecipientsAsync(db, row);
        Assert.Equal(expected.OrderBy(x => x), sent.Select(s => s.ChatId).OrderBy(x => x));
        var text = NewsTelegramNotifier.BuildMessage(row);
        Assert.All(sent, s => Assert.Equal(text, s.Text));

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityType == "News" && a.EntityId == id.ToString() && a.Action == "publish");
        Assert.Equal($"Yangilik e'lon qilindi: {title} — ota-ona · {sent.Count}/{sent.Count}", audit.Summary);
    }

    // =====================================================================
    //  8. Qabul qiluvchilar (N6) — toza bazada, aniq to'plamlar
    // =====================================================================

    /// <summary>
    /// Ikki jadval birlashmasi, chat id QIYMATI bo'yicha dublikatsiz:
    /// ota-ona <c>1001</c> ikkala jadvalda (va ikki farzand uchun ikki marta)
    /// — bitta. Auditoriya xaritasi N6 jadvalining aynan o'zi; <c>0</c> chat
    /// hech qachon qabul qiluvchi emas.
    /// </summary>
    [Fact]
    public async Task Qabul_qiluvchilar_ikki_jadval_birlashmasi_dublikatsiz()
    {
        var database = await RecipientWorldAsync();
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);

        var parents = await RecipientsAsync(db, parent: true);
        var students = await RecipientsAsync(db, student: true);
        var employees = await RecipientsAsync(db, employee: true);
        var everyone = await RecipientsAsync(db, employee: true, parent: true, student: true);

        Assert.Equal(new long[] { 1001, 1002 }, parents);
        Assert.Equal(new long[] { 3001 }, students);
        Assert.Equal(new long[] { 2001, 2002, 2003, 2004, 2005, 2006 }, employees);
        Assert.Equal(new long[] { 1001, 1002, 2001, 2002, 2003, 2004, 2005, 2006, 3001 }, everyone);
    }

    /// <summary>
    /// Tarqatma har chatga BIR MARTA yuboradi; Telegram rad etgan xabar
    /// "yuborildi" deb sanalmaydi (<c>SentCount &lt; RecipientCount</c>) va
    /// xato tashlanmaydi.
    /// </summary>
    [Fact]
    public async Task Tarqatma_har_chatga_bir_marta_yuboradi_rad_etilganini_sanamaydi()
    {
        var database = await RecipientWorldAsync();
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);
        var recorder = new TelegramRecorder(1002);
        var notifier = new NewsTelegramNotifier(db, ConfiguredTelegram(recorder),
            fixture.Api.Services.GetRequiredService<ILogger<NewsTelegramNotifier>>());
        var news = new NewsItem { Title = "Yig'ilish", Body = "Ertaga soat 15:00", ForParent = true };

        var result = await notifier.SendAsync(news);

        Assert.True(result.Attempted);
        Assert.Equal(2, result.RecipientCount);
        Assert.Equal(1, result.SentCount);
        Assert.Equal([1001L, 1002L], recorder.Sent.Select(s => s.ChatId).OrderBy(x => x).ToArray());
        Assert.All(recorder.Sent, s => Assert.Equal(NewsTelegramNotifier.BuildMessage(news), s.Text));
    }

    [Fact]
    public async Task Bot_sozlanmagan_tarqatma_hech_narsa_yubormaydi()
    {
        var database = await RecipientWorldAsync();
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);
        var recorder = new TelegramRecorder();
        var unconfigured = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(recorder),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        var notifier = new NewsTelegramNotifier(db, unconfigured,
            fixture.Api.Services.GetRequiredService<ILogger<NewsTelegramNotifier>>());

        var result = await notifier.SendAsync(new NewsItem { Title = "T", Body = "B", ForParent = true, ForEmployee = true });

        Assert.Equal(NewsTelegramResult.NotSent, result);
        Assert.Empty(recorder.Sent);
    }

    // =====================================================================
    //  9. Xabar matni (§5.4) va 4096 belgilik chegara
    // =====================================================================

    /// <summary>
    /// §5.4 dagi shakl: <c>📰 {title}</c>, bo'sh qator, <c>{body}</c> —
    /// oddiy matn, <c>parse_mode</c> siz.
    /// </summary>
    [Fact]
    public void Telegram_xabari_spetsifikatsiyadagi_shaklda()
    {
        var text = NewsTelegramNotifier.BuildMessage(
            new NewsItem { Title = "Ota-onalar yig'ilishi", Body = "25-sentabr, soat 15:00" });

        Assert.Equal("📰 Ota-onalar yig'ilishi\n\n25-sentabr, soat 15:00", text);
    }

    /// <summary>
    /// Aynan 4096 belgi — kesilmaydi. 4097 — chegaragacha kesiladi, oxiriga
    /// "to'liq matn ilovada" qo'shiladi va natija 4096 dan oshmaydi. Xabar
    /// BITTA bo'lib qoladi (bo'linmaydi).
    /// </summary>
    [Fact]
    public void Uzun_xabar_4096_belgida_kesiladi()
    {
        const string title = "Sarlavha";
        var header = Header(title);
        var suffix = NewsTelegramNotifier.TruncatedSuffix;

        var exactBody = new string('a', 4096 - header.Length);
        var exact = NewsTelegramNotifier.BuildMessage(new NewsItem { Title = title, Body = exactBody });
        Assert.Equal(4096, exact.Length);
        Assert.Equal(header + exactBody, exact);

        var longBody = exactBody + "b";
        var cut = NewsTelegramNotifier.BuildMessage(new NewsItem { Title = title, Body = longBody });
        Assert.True(cut.Length <= NewsTelegramNotifier.TelegramMaxLength, $"uzunlik {cut.Length}");
        Assert.EndsWith(suffix, cut, StringComparison.Ordinal);
        Assert.Equal((header + longBody)[..(4096 - suffix.Length)], cut[..^suffix.Length]);
    }

    /// <summary>
    /// Kesish nuqtasi emoji (surrogat juft) o'rtasiga tushsa — emoji BUTUNLAY
    /// tushib qoladi, yarmi qolmaydi (yarim juft Telegram'da "?" yoki xato).
    /// Emoji chegaradan butunlay oldin tugasa — u saqlanadi.
    /// </summary>
    [Fact]
    public void Kesish_surrogat_juftni_ikkiga_bolmaydi()
    {
        const string title = "Emoji";
        var header = Header(title);
        var suffix = NewsTelegramNotifier.TruncatedSuffix;
        var budget = NewsTelegramNotifier.TelegramMaxLength - suffix.Length;

        // Yuqori surrogat AYNAN budget-1 da — ya'ni juft kesish chizig'ini kesib o'tadi.
        var straddling = new string('a', budget - 1 - header.Length);
        var split = NewsTelegramNotifier.BuildMessage(
            new NewsItem { Title = title, Body = straddling + "😀" + new string('b', 300) });
        AssertNoLoneSurrogates(split);
        Assert.Equal(header + straddling + suffix, split);

        // Juft chiziqdan oldin to'liq tugaydi — saqlanadi.
        var fitting = new string('a', budget - 2 - header.Length);
        var whole = NewsTelegramNotifier.BuildMessage(
            new NewsItem { Title = title, Body = fitting + "😀" + new string('b', 300) });
        AssertNoLoneSurrogates(whole);
        Assert.Equal(header + fitting + "😀" + suffix, whole);
    }

    // =====================================================================
    //  10. Admin qo'ng'irog'i (N9)
    // =====================================================================

    [Fact]
    public async Task Qongiroqda_elon_qilingan_yangilik_chiqadi_qoralama_va_ochirilgani_chiqmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var published = await CreateAsync(admin, ["parent"]);
        var publishedId = published.GetProperty("id").GetGuid();
        await PublishAsync(admin, publishedId);
        var draftId = (await CreateAsync(admin, ["parent"])).GetProperty("id").GetGuid();
        var deletedId = (await CreateAsync(admin, ["parent"])).GetProperty("id").GetGuid();
        await PublishAsync(admin, deletedId);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{News}/{deletedId}")).StatusCode);

        using var bell = JsonDocument.Parse(await admin.GetStringAsync("/api/admin/notifications"));
        var items = bell.RootElement.GetProperty("items").EnumerateArray().ToList();

        var item = Assert.Single(items, i => i.GetProperty("id").GetString() == $"news:{publishedId}");
        Assert.Equal("news", item.GetProperty("kind").GetString());
        Assert.Equal("Yangilik e'lon qilindi", item.GetProperty("title").GetString());
        Assert.Equal(published.GetProperty("title").GetString(), item.GetProperty("text").GetString());
        Assert.Equal("/admin/marketing/yangiliklar", item.GetProperty("link").GetString());
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetString() == $"news:{draftId}");
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetString() == $"news:{deletedId}");
    }

    /// <summary>
    /// Ikki admin (yoki ikki oyna) BIR VAQTDA "E'lon qilish" ni bosdi: faqat bittasi
    /// o'tadi, ikkinchisi 409. O'qib-keyin-yozishda ikkalasi ham tarqatib, har bir
    /// ota-onaga ikki nusxa yuborardi.
    /// </summary>
    [Fact]
    public async Task Bir_vaqtdagi_ikki_elon_faqat_bittasi_otadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var id = (await CreateAsync(admin, ["parent"])).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            admin.PostAsJsonAsync($"{News}/{id}/publish", new { sendTelegram = true })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static object SaveBody(string title, string[] audience) =>
        new { title, body = "Ota-onalar yig'ilishi — 25-sentabr, soat 15:00", imageUrl = (string?)null, audience };

    private static async Task<JsonElement> CreateAsync(
        HttpClient admin, string[] audience, string? title = null, string body = "Matn")
    {
        var response = await admin.PostAsJsonAsync(News,
            new { title = title ?? $"Yangilik {SalesMarketingKit.Tag()}", body, imageUrl = (string?)null, audience });
        return await OkJsonAsync(response);
    }

    private static async Task<JsonElement> PublishAsync(HttpClient admin, Guid id) =>
        await OkJsonAsync(await admin.PostAsJsonAsync($"{News}/{id}/publish", new { sendTelegram = true }));

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string[] AudienceOf(JsonElement dto) =>
        dto.GetProperty("audience").EnumerateArray().Select(a => a.GetString()!).ToArray();

    private async Task AssertFlagsAsync(Guid id, bool employee, bool parent, bool student)
    {
        await using var db = NewDb();
        var row = await db.News.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.Equal((employee, parent, student), (row.ForEmployee, row.ForParent, row.ForStudent));
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("validation", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, doc.RootElement.GetProperty("message").GetString());
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, doc.RootElement.GetProperty("message").GetString());
    }

    private static async Task<List<Guid>> AdminListIdsAsync(HttpClient admin, string state)
    {
        using var doc = JsonDocument.Parse(await admin.GetStringAsync($"{News}?state={state}&pageSize=200"));
        return [.. doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetGuid())];
    }

    private sealed record Feed(HttpClient Client, string Url);

    private sealed class Readers(HttpClient parent, HttpClient student, HttpClient teacher, HttpClient admin) : IDisposable
    {
        public Feed TgParent { get; } = new(parent, "/api/tg/parent/news?take=50");
        public Feed PortalParent { get; } = new(parent, "/api/student/news?take=50");
        public Feed PortalStudent { get; } = new(student, "/api/student/news?take=50");
        public Feed TgTeacher { get; } = new(teacher, "/api/tg/teacher/news?take=50");
        public Feed AdminFeed { get; } = new(admin, "/api/admin/news/feed?take=50");
        public Feed[] All => [TgParent, PortalParent, PortalStudent, TgTeacher, AdminFeed];

        public void Dispose()
        {
            parent.Dispose();
            student.Dispose();
            teacher.Dispose();
        }
    }

    /// <summary>Beshta lentaning o'quvchilari. O'quvchi <c>students</c> qatoriga bog'lanadi (token bekor qilinmasin).</summary>
    private async Task<Readers> ReadersAsync(HttpClient admin)
    {
        var parent = await fixture.Api.ClientAsAsync("parent");
        var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);

        var (pupilUser, _) = await fixture.Api.SeedUserAsync(Roles.Student);
        await fixture.Api.WithDbAsync(async db =>
        {
            var pupil = GeneralSettingsFlagsTests.NewStudent(pupilUser.FullName, $"NW-{SalesMarketingKit.Tag()[..4]}", "+998900000088");
            pupil.UserId = pupilUser.Id;
            db.Students.Add(pupil);
            await db.SaveChangesAsync();
        });
        var student = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Student, pupilUser.Id, pupilUser.FullName, pupilUser.Email));

        return new Readers(parent, student, teacher, admin);
    }

    private static async Task<List<Guid>> FeedIdsAsync(Feed feed)
    {
        var response = await feed.Client.GetAsync(feed.Url);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{feed.Url}: {(int)response.StatusCode} {text}");
        using var doc = JsonDocument.Parse(text);
        return [.. doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];
    }

    /// <summary>Lentadagi HAR BIR yozuv — e'lon qilingan, o'chirilmagan va shu auditoriyaniki.</summary>
    private static async Task AssertEveryItemBelongsAsync(AppDbContext db, Feed feed, Func<NewsItem, bool> audience)
    {
        var ids = await FeedIdsAsync(feed);
        var rows = await db.News.AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync();
        Assert.Equal(ids.Count, rows.Count);
        Assert.All(rows, n =>
        {
            Assert.True(audience(n), $"{feed.Url} begona auditoriya yangiligini qaytardi: {n.Id} «{n.Title}»");
            Assert.NotNull(n.PublishedAt);
            Assert.Null(n.DeletedAt);
        });
    }

    /// <summary>
    /// N6 jadvalining har bir satri uchun bittadan (yoki ataylab ikkitadan) yozuv:
    /// ota-ona 1001 — ikkala jadvalda va ikki farzand uchun; ota-ona 1002 —
    /// faqat botda; xodimlar 2001–2006 (o'qituvchi ikkala jadvalda); o'quvchi
    /// 3001; va bitta buzuq <c>chat_id = 0</c>.
    /// </summary>
    private async Task<TestDatabase> RecipientWorldAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("smnews");
        _freshDatabases.Add(database);

        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);
        void Account(string role, long telegramUserId)
        {
            var user = new AppUser { FullName = $"{role} {telegramUserId}", Role = role, Email = $"{role}.{telegramUserId}" };
            db.Users.Add(user);
            db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = telegramUserId, UserId = user.Id, DisplayName = user.FullName });
        }
        void Registration(string studentId, long chatId, string? teacherId = null) =>
            db.TelegramRegistrations.Add(new TelegramRegistration
                { StudentId = studentId, ChatId = chatId, TeacherId = teacherId, ParentName = "Ota-ona" });

        Account("parent", 1001);
        Registration("s-1", 1001);
        Registration("s-2", 1001);
        Registration("s-3", 1002);
        Registration("s-0", 0);

        Account(Roles.Teacher, 2001);
        Registration("t-1", 2001, teacherId: "teacher-1");
        Account(Roles.Staff, 2002);
        Account(Roles.Cashier, 2003);
        Account(Roles.Admin, 2004);
        Account(Roles.SuperAdmin, 2005);
        Registration("t-2", 2006, teacherId: "teacher-2");

        Account(Roles.Student, 3001);

        await db.SaveChangesAsync();
        return database;
    }

    private static async Task<long[]> RecipientsAsync(
        AppDbContext db, bool employee = false, bool parent = false, bool student = false)
    {
        var news = new NewsItem { ForEmployee = employee, ForParent = parent, ForStudent = student };
        return [.. (await NewsTelegramNotifier.RecipientsAsync(db, news)).OrderBy(x => x)];
    }

    private TelegramService ConfiguredTelegram(TelegramRecorder recorder)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(recorder),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>Xabar boshlanishi (sarlavha qismi) — shakldan qat'i nazar, matn qayerdan boshlanishi.</summary>
    private static string Header(string title)
    {
        var probe = NewsTelegramNotifier.BuildMessage(new NewsItem { Title = title, Body = "§" });
        var at = probe.IndexOf('§');
        Assert.True(at > 0, $"Matn xabarda topilmadi: '{probe}'");
        return probe[..at];
    }

    private static void AssertNoLoneSurrogates(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                Assert.True(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]), $"Yolg'iz yuqori surrogat: {i}");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(text[i]), $"Yolg'iz quyi surrogat: {i}");
            }
        }
    }

    /// <summary>
    /// Telegram Bot API o'rnida: har <c>sendMessage</c> ni yozib oladi va
    /// <paramref name="failFor"/> dagi chatlar uchun 500, qolganiga 200 qaytaradi.
    /// </summary>
    private sealed class TelegramRecorder(params long[] failFor) : HttpMessageHandler
    {
        private readonly List<SentMessage> _sent = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<SentMessage> Sent
        {
            get { lock (_gate) return [.. _sent]; }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var chatId = doc.RootElement.GetProperty("chat_id").GetInt64();
            var message = new SentMessage(
                request.RequestUri!.AbsolutePath,
                chatId,
                doc.RootElement.GetProperty("text").GetString()!,
                doc.RootElement.TryGetProperty("parse_mode", out _));
            lock (_gate) _sent.Add(message);

            return new HttpResponseMessage(failFor.Contains(chatId) ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}"),
            };
        }
    }

    private sealed record SentMessage(string Path, long ChatId, string Text, bool HasParseMode);

    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
