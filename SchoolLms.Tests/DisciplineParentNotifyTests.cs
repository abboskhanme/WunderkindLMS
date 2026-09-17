using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// §6.3, 4–5-qadam — intizomiy sababning <c>notify_parent</c> / <c>description</c> /
/// <c>is_active</c> maydonlari va ota-onaga Telegram xabari.
///
/// <para>
/// <b>Hech narsa tarmoqqa chiqmaydi.</b> API testlarida ilovaning o'z
/// <see cref="TelegramService"/> i sozlanmagan (tokensiz) — birinchi test buni
/// TEKSHIRADI, faraz qilmaydi. Yuborish mantig'i esa <see cref="DisciplineParentNotifier"/>
/// ning o'zida, O'Z bazasida va soxta <see cref="HttpMessageHandler"/> bilan tekshiriladi:
/// tokeni bor, lekin har so'rov qo'lga olinib sanaladi (<c>ReceiptTests</c> naqshi).
/// </para>
/// <para>
/// <b>Eng muhim tekshiruv — bayroqsiz sabab JIM.</b> Sukut bo'yicha har bir sabab
/// ota-onaga xabar yubormaydi (§6.3 ogohlantirishi, §9 Q5).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DisciplineParentNotifyTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Reasons = "/api/admin/discipline/reasons";
    private const string Points = "/api/admin/discipline/points";

    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Har test o'z bazasini ochgan bo'lsa — faqat o'shalarning hovuzi yopiladi.</summary>
    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1. RUXSAT va SABAB MAYDONLARI
    // =====================================================================

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rol_sabab_va_ball_yoza_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "discipline");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Reasons, new { name = "X", points = -1, notifyParent = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Points, new { studentId = "x", reasonId = "x" })).StatusCode);
    }

    /// <summary>
    /// Yangi sabab: maydonlar berilmasa xabar O'CHIQ, sabab FAOL. Tahrirda berilmagan maydon
    /// O'ZGARMAYDI — eski mijoz yoqilgan bayroqni tasodifan o'chirib yubormasin.
    /// </summary>
    [Fact]
    public async Task Sabab_maydonlari_sukuti_va_berilmagani_ozgarmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var plain = await ReadAsync(await admin.PostAsJsonAsync(Reasons, new { name = $"Kechikdi {tag}", points = -2 }));
        Assert.False(plain.GetProperty("notifyParent").GetBoolean());
        Assert.True(plain.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("description").ValueKind);

        var id = plain.GetProperty("id").GetString()!;
        var flagged = await ReadAsync(await admin.PutAsJsonAsync($"{Reasons}/{id}",
            new { name = $"Janjal {tag}", points = -10, notifyParent = true, description = "  Jismoniy to'qnashuv  " }));
        Assert.True(flagged.GetProperty("notifyParent").GetBoolean());
        Assert.Equal("Jismoniy to'qnashuv", flagged.GetProperty("description").GetString());

        // Eski shakldagi so'rov (faqat nom va ball) — bayroq va izoh joyida qoladi.
        var legacy = await ReadAsync(await admin.PutAsJsonAsync($"{Reasons}/{id}", new { name = $"Janjal {tag}", points = -12 }));
        Assert.True(legacy.GetProperty("notifyParent").GetBoolean());
        Assert.Equal("Jismoniy to'qnashuv", legacy.GetProperty("description").GetString());
        Assert.Equal(-12, legacy.GetProperty("points").GetInt32());

        // Faolsizlantirilgan: `activeOnly` da yo'q, sukutda (tarix filtrlari uchun) bor.
        await admin.PutAsJsonAsync($"{Reasons}/{id}", new { name = $"Janjal {tag}", points = -12, isActive = false });
        Assert.DoesNotContain(await RowsAsync(admin, $"{Reasons}?activeOnly=true"), r => r.GetProperty("id").GetString() == id);
        Assert.Contains(await RowsAsync(admin, Reasons), r => r.GetProperty("id").GetString() == id);
    }

    /// <summary>Faolsiz sabab bilan YANGI ball qo'yilmaydi — ekran unutsa ham server rad etadi.</summary>
    [Fact]
    public async Task Faolsiz_sabab_bilan_ball_qoyilmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var studentId = await SeedSharedStudentAsync(tag);
        var reasonId = await CreateReasonAsync(admin, $"Eski sabab {tag}", -3, notifyParent: false, isActive: false);

        var response = await admin.PostAsJsonAsync(Points, new { studentId, reasonId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =====================================================================
    //  2. API: testda HECH NARSA yuborilmaydi
    // =====================================================================

    /// <summary>
    /// Ilova bot tokenisiz ishlaydi (ApiFactory). Shu sharoitda bayroqli sabab bilan ham,
    /// bayroqsiz sabab bilan ham ball YOZILADI va <c>notifiedParents = 0</c> — ya'ni yuborish
    /// muvaffaqiyatsizligi ball yozilishiga ta'sir qilmaydi va test hech kimga xabar jo'natmaydi.
    /// </summary>
    [Fact]
    public async Task Testda_bot_sozlanmagan_ball_yoziladi_xabar_ketmaydi()
    {
        Assert.False(fixture.Api.Services.GetRequiredService<TelegramService>().IsConfigured,
            "Test ilovasida bot tokeni bo'lmasligi SHART — aks holda testlar haqiqiy ota-onaga yozadi.");

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var studentId = await SeedSharedStudentAsync(tag, registerParentChat: 7_000_000_001);
        var flagged = await CreateReasonAsync(admin, $"Janjal {tag}", -10, notifyParent: true);
        var quiet = await CreateReasonAsync(admin, $"Kechikdi {tag}", -1, notifyParent: false);

        foreach (var reasonId in new[] { flagged, quiet })
        {
            var point = await ReadAsync(await admin.PostAsJsonAsync(Points, new { studentId, reasonId, note = "3-dars" }));
            Assert.Equal(0, point.GetProperty("notifiedParents").GetInt32());
        }

        var history = await RowsAsync(admin, $"{Points}?studentId={studentId}");
        Assert.Equal(2, history.Count);
    }

    // =====================================================================
    //  3. Xabar yuboruvchining O'ZI — soxta Telegram bilan
    // =====================================================================

    /// <summary>
    /// Bayroqsiz sabab — ota-ona botga ulangan, bot sozlangan bo'lsa ham BIRORTA HTTP
    /// so'rov yo'q. Bu shu faylning asosiy va'dasi.
    /// </summary>
    [Fact]
    public async Task Bayroqsiz_sabab_hech_narsa_yubormaydi()
    {
        await using var db = await NewDbAsync();
        var (student, _) = await SeedAsync(db, chats: [1001, 1002]);
        var reason = new DisciplineReason { Name = "Kechikdi", Points = -1, NotifyParent = false };
        var point = Point(student, reason, note: "");
        var handler = new RecordingHandler();

        var sent = await Notifier(db, ConfiguredTelegram(handler)).NotifyAsync(student, reason, point);

        Assert.Equal(0, sent);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Bayroqli sabab — shu o'quvchiga ulangan HAR BIR chatga bittadan xabar,
    /// boshqa o'quvchining ota-onasiga hech narsa. Matn — hisobotda yozilganidek.
    /// </summary>
    [Fact]
    public async Task Bayroqli_sabab_faqat_shu_oquvchining_ota_onalariga_yuboradi()
    {
        await using var db = await NewDbAsync();
        var (student, other) = await SeedAsync(db, chats: [2001, 2002], otherChats: [2999]);
        var reason = new DisciplineReason { Name = "Janjal", Points = -10, NotifyParent = true };
        var point = Point(student, reason, note: "Tanaffusda");
        var handler = new RecordingHandler();

        var sent = await Notifier(db, ConfiguredTelegram(handler)).NotifyAsync(student, reason, point);

        Assert.Equal(2, sent);
        Assert.Equal(2, handler.Requests.Count);
        var chats = handler.Requests.Select(r => r.GetProperty("chat_id").GetInt64()).OrderBy(x => x).ToList();
        Assert.Equal([2001L, 2002L], chats);
        Assert.All(handler.Requests, r =>
            Assert.Equal(DisciplineParentNotifier.BuildMessage(student, point), r.GetProperty("text").GetString()));
        Assert.DoesNotContain(handler.Requests, r => r.GetProperty("chat_id").GetInt64() == 2999);
        Assert.NotEqual(student.Id, other.Id);
    }

    /// <summary>Ota-ona botga ulanmagan — so'rov yo'q. Bot sozlanmagan — chat ham qidirilmaydi.</summary>
    [Fact]
    public async Task Ulanmagan_ota_ona_yoki_sozlanmagan_bot_jim()
    {
        await using var db = await NewDbAsync();
        var (student, _) = await SeedAsync(db, chats: []);
        var reason = new DisciplineReason { Name = "Janjal", Points = -10, NotifyParent = true };
        var handler = new RecordingHandler();

        Assert.Equal(0, await Notifier(db, ConfiguredTelegram(handler)).NotifyAsync(student, reason, Point(student, reason, "")));
        Assert.Empty(handler.Requests);

        var (registered, _) = await SeedAsync(db, chats: [3001]);
        var unconfigured = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        Assert.Equal(0, await Notifier(db, unconfigured).NotifyAsync(registered, reason, Point(registered, reason, "")));
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Ota-ona o'qiydigan matnning AYNAN o'zi. Bu test hisobotdagi "ota-ona nima oladi"
    /// javobini ushlab turadi: matn o'zgarsa, test ham, hisobot ham yangilanishi kerak.
    /// </summary>
    [Fact]
    public void Xabar_matni()
    {
        var student = new Student { FullName = "Aliyev Vali", ClassName = "7-A" };
        var minus = new DisciplinePoint
        {
            ReasonName = "Darsda janjal", Points = -10, Note = "Tanaffusda", CreatedAt = "2026-09-17T10:45:12.0000000+05:00",
        };
        var plus = new DisciplinePoint { ReasonName = "Olimpiada g'olibi", Points = 5, Note = "", CreatedAt = "2026-09-17T14:05:00" };

        Assert.Equal(
            "Intizomiy ball\n\nO'quvchi: Aliyev Vali (7-A)\nSabab: Darsda janjal\nBall: -10\nIzoh: Tanaffusda\nVaqt: 17.09.2026 10:45",
            DisciplineParentNotifier.BuildMessage(student, minus));
        Assert.Equal(
            "Rag'bat bali\n\nO'quvchi: Aliyev Vali (7-A)\nSabab: Olimpiada g'olibi\nBall: +5\nVaqt: 17.09.2026 14:05",
            DisciplineParentNotifier.BuildMessage(student, plus));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<AppDbContext> NewDbAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("discipline_notify");
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<(Student Student, Student Other)> SeedAsync(
        AppDbContext db, long[] chats, long[]? otherChats = null)
    {
        var tag = Tag();
        var student = GeneralSettingsFlagsTests.NewStudent($"Xabar {tag}", "7-A", "+998901234567");
        var other = GeneralSettingsFlagsTests.NewStudent($"Qo'shni {tag}", "7-A", "+998907654321");
        db.Students.AddRange(student, other);
        foreach (var chat in chats)
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = student.Id, ChatId = chat, ParentName = "Ota" });
        foreach (var chat in otherChats ?? [])
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = other.Id, ChatId = chat, ParentName = "Qo'shni" });
        await db.SaveChangesAsync();
        return (student, other);
    }

    private static DisciplinePoint Point(Student student, DisciplineReason reason, string note) => new()
    {
        StudentId = student.Id,
        ReasonId = reason.Id,
        ReasonName = reason.Name,
        Points = reason.Points,
        Note = note,
        CreatedAt = AppClock.Now.ToString("o"),
        CreatedBy = "Test",
    };

    private DisciplineParentNotifier Notifier(AppDbContext db, TelegramService telegram) =>
        new(db, telegram, fixture.Api.Services.GetRequiredService<ILogger<DisciplineParentNotifier>>());

    /// <summary>Tokeni bor ("sozlangan"), lekin har HTTP so'rov <paramref name="handler"/> da qoladi.</summary>
    private TelegramService ConfiguredTelegram(RecordingHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    private async Task<string> SeedSharedStudentAsync(string tag, long? registerParentChat = null)
    {
        var student = GeneralSettingsFlagsTests.NewStudent($"Intizom {tag}", $"DN-{tag}", "+998900000002");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            if (registerParentChat is { } chat)
                db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = student.Id, ChatId = chat, ParentName = "Ota" });
            await db.SaveChangesAsync();
        });
        return student.Id;
    }

    private static async Task<string> CreateReasonAsync(
        HttpClient client, string name, int points, bool notifyParent, bool isActive = true)
    {
        var row = await ReadAsync(await client.PostAsJsonAsync(Reasons, new { name, points, notifyParent, isActive }));
        return row.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    /// <summary>Har so'rov tanasini yozib oladi va 200 qaytaradi — tarmoqqa hech narsa chiqmaydi.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<JsonElement> _requests = [];

        public IReadOnlyList<JsonElement> Requests
        {
            get { lock (_requests) return [.. _requests]; }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            lock (_requests) _requests.Add(json.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
