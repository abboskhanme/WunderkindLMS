using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Telegram Mini App — HTTP oqimi. SPEC §6 Faza 3.
// ===========================================================================
//
//  `TelegramInitDataTests` imzo algoritmini SOF funksiya sifatida tekshiradi.
//  Bu yerda esa haqiqiy so'rovlar yuboriladi: soxta `initData` eshikdan o'ta
//  oladimi, bog'lanmagan akkaunt qanday javob oladi, kod bilan bog'langandan
//  keyin token ISHLAYDIMI va ikki farzandli ota-ona IKKALASINI ham ko'radimi.
//
//  ALOHIDA HOST. Bot tokeni `TelegramService` singleton'ida yashaydi. Uni
//  umumiy `fixture.Api` da yoqib qo'ysak, chek yuborish yo'li (P1-12) tirik
//  bo'lib qolardi va boshqa testlar api.telegram.org ga chiqishga urinardi.
//  Shuning uchun `WithWebHostBuilder` bilan HOSILA fabrika ko'tariladi — o'sha
//  baza, o'sha JWT kaliti, faqat DI konteyneri boshqa (RbacMatrixTests namunasi).
//
//  RATE LIMIT. `/api/tg/auth` va `/api/tg/link` — IP bo'yicha 20/daqiqa.
//  TestServer'da IP null, ya'ni butun fayl bitta "unknown" partitsiyada.
//  Shuning uchun bu yerdagi tg-chaqiruvlar soni ATAYLAB kam (~12) va yangi
//  test qo'shishdan oldin shu chegarani hisobga oling.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class TelegramMiniAppTests(ApiFixture fixture)
{
    /// <summary>Soxta token — haqiqiysi hech qachon repoda bo'lmaydi.</summary>
    private const string BotToken = "123456789:TEST-ONLY-BOT-TOKEN-not-a-real-one";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // =====================================================================
    //  1. Kirish: soxta rad etiladi, to'g'risi qabul qilinadi
    // =====================================================================

    [Fact]
    public async Task Soxta_initData_401_va_token_bermaydi()
    {
        using var client = AnonymousClient();

        // Mukammal ko'rinishdagi, lekin BOSHQA token bilan imzolangan initData.
        var forged = TelegramInitData.Sign(InitFields(NewTelegramId()), "999:BEGONA-BOT-TOKEN");

        var response = await client.PostAsJsonAsync("/api/tg/auth", new { initData = forged });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        Assert.Equal("invalid_init_data", body.RootElement.GetProperty("code").GetString());
        // Javobda na token, na bot tokeni bo'lagi sizib chiqmasin.
        Assert.DoesNotContain("eyJ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST-ONLY-BOT-TOKEN", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Boglanmagan_telegram_401_emas_unlinked_qaytaradi()
    {
        using var client = AnonymousClient();
        var initData = TelegramInitData.Sign(InitFields(NewTelegramId()), BotToken);

        var response = await client.PostAsJsonAsync("/api/tg/auth", new { initData });

        // 401 EMAS: kimligi aniq, faqat maktab uni tanimaydi. Frontend shu
        // holatda kod so'rash oynasini ochadi.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("unlinked", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("token").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("user").ValueKind);
        // Bog'lash ekraniga kerak bo'lgan ma'lumot javobda bor.
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("telegram").GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Notogri_kod_bilan_boglanib_bolmaydi()
    {
        using var client = AnonymousClient();
        var initData = TelegramInitData.Sign(InitFields(NewTelegramId()), BotToken);

        var response = await client.PostAsJsonAsync("/api/tg/link",
            new { initData, code = "AAAA-BBBB" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_code", body.RootElement.GetProperty("code").GetString());
    }

    // =====================================================================
    //  2. To'liq oqim: ikki farzandli ota-ona
    // =====================================================================

    /// <summary>
    /// SPEC §6 Faza 3 ning qabul mezoni: "ikki farzandli vasiy Telegram'da
    /// ular orasida almasha oladi va IKKALASINING ham ma'lumotini ko'radi".
    ///
    /// <para>
    /// Bitta test butun zanjirni bosib o'tadi, chunki zanjirning har bo'g'ini
    /// alohida yashil bo'lib, birgalikda ishlamasligi mumkin:
    /// o'quvchi qo'shish → `GuardianSync` bitta vasiy yasaydi (ikkita emas) →
    /// vasiyga akkaunt → admin kod chiqaradi → Mini App kod bilan bog'lanadi →
    /// olingan token ODDIY endpointda ham ishlaydi → farzandlar ro'yxati
    /// IKKITA qator qaytaradi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ikki_farzandli_vasiy_ikkalasini_ham_koradi()
    {
        var phone = NewPhone();
        using var admin = await AdminClientAsync();

        var elder = await AddStudentAsync(admin, "Testov Akbar Anvarovich", "9-A", "Testov Anvar", phone);
        var younger = await AddStudentAsync(admin, "Testova Aziza Anvar qizi", "3-B", "Testov Anvar", phone);

        // ---- GuardianSync: bitta raqam = BITTA vasiy, ikkita bog'lanish ----
        var guardian = await FindGuardianAsync(admin, phone);
        Assert.Equal("Testov Anvar", guardian.GetProperty("fullName").GetString());
        var children = guardian.GetProperty("children").EnumerateArray()
            .Select(c => c.GetProperty("studentId").GetString()).ToHashSet();
        Assert.Equal(2, children.Count);
        Assert.Contains(elder, children);
        Assert.Contains(younger, children);

        // ---- Vasiyga akkaunt (rol = parent, login = raqam) ----
        var guardianId = guardian.GetProperty("id").GetString()!;
        var account = await PostJsonAsync(admin, $"/api/admin/guardians/{guardianId}/account",
            new { newPassword = "Demo123!" });
        Assert.Equal("parent", account.GetProperty("role").GetString());

        var userId = await UserIdOfGuardianAsync(guardianId);

        // ---- Admin bir martalik kod chiqaradi ----
        var issued = await PostJsonAsync(admin, "/api/admin/telegram/link-codes", new { userId });
        var code = issued.GetProperty("code").GetString()!;
        Assert.Matches(@"^[A-Z2-9]{4}-[A-Z2-9]{4}$", code);

        // Kod OCHIQ saqlanmaydi — bazada faqat hash'i.
        await fixture.Api.WithDbAsync(async db =>
        {
            var stored = await db.TelegramLinkCodes.Where(c => c.UserId == userId).ToListAsync();
            var single = Assert.Single(stored);
            Assert.DoesNotContain(code.Replace("-", ""), single.CodeHash, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(64, single.CodeHash.Length);           // SHA-256 hex
            Assert.Null(single.UsedAt);
        });

        // ---- Mini App: kod bilan bog'lanish ----
        var telegramId = NewTelegramId();
        var initData = TelegramInitData.Sign(InitFields(telegramId), BotToken);

        using var anonymous = AnonymousClient();
        var linked = await anonymous.PostAsJsonAsync("/api/tg/link", new { initData, code });
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);

        using var linkedBody = JsonDocument.Parse(await linked.Content.ReadAsStringAsync());
        Assert.Equal("ok", linkedBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("parent", linkedBody.RootElement.GetProperty("user").GetProperty("role").GetString());
        var token = linkedBody.RootElement.GetProperty("token").GetString()!;

        // ---- Token ODDIY login tokeni bilan bir xil: mavjud endpoint ochiladi ----
        using var parent = ClientWithToken(token);
        var me = await parent.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var meBody = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(userId, meBody.RootElement.GetProperty("id").GetString());

        // ---- Farzand almashtirgichi: IKKITA farzand ----
        var cards = await GetJsonAsync(parent, "/api/tg/parent/children");
        var byId = cards.EnumerateArray().ToDictionary(c => c.GetProperty("studentId").GetString()!);
        Assert.Equal(2, byId.Count);
        Assert.Equal("9-A", byId[elder].GetProperty("className").GetString());
        Assert.Equal("3-B", byId[younger].GetProperty("className").GetString());

        // ---- Har bir farzandning ekrani ochiladi ----
        foreach (var studentId in new[] { elder, younger })
        {
            var overview = await GetJsonAsync(parent, $"/api/tg/parent/children/{studentId}/overview");
            Assert.Equal(studentId, overview.GetProperty("child").GetProperty("studentId").GetString());

            var finance = await parent.GetAsync($"/api/tg/parent/children/{studentId}/finance");
            Assert.Equal(HttpStatusCode.OK, finance.StatusCode);
        }

        // ---- BEGONA bola — 404 (403 emas: mavjudligi ham ma'lumot) ----
        var stranger = await AddStudentAsync(
            admin, "Begonov Begona Begonovich", "5-A", "Begonov Ota", NewPhone());
        foreach (var path in new[] { "overview", "grades", "attendance", "finance", "announcements", "pickup" })
        {
            var denied = await parent.GetAsync($"/api/tg/parent/children/{stranger}/{path}");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }

        // ---- Bog'lanishdan keyin oddiy `auth` ham ishlaydi ----
        var reauth = await anonymous.PostAsJsonAsync("/api/tg/auth", new { initData });
        Assert.Equal(HttpStatusCode.OK, reauth.StatusCode);
        using var reauthBody = JsonDocument.Parse(await reauth.Content.ReadAsStringAsync());
        Assert.Equal("ok", reauthBody.RootElement.GetProperty("status").GetString());

        // ---- Kod BIR MARTALIK: ikkinchi Telegram akkaunti uni ishlata olmaydi ----
        var secondInit = TelegramInitData.Sign(InitFields(NewTelegramId()), BotToken);
        var reused = await anonymous.PostAsJsonAsync("/api/tg/link", new { initData = secondInit, code });
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);

        // ---- Bog'lanishni uzish mumkin ----
        var unlink = await parent.DeleteAsync("/api/tg/link");
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.TelegramAccounts.AnyAsync(a => a.TelegramUserId == telegramId)));
    }

    // =====================================================================
    //  3. Rollar
    // =====================================================================

    [Fact]
    public async Task Tokensiz_tg_endpointlari_401()
    {
        using var client = AnonymousClient();

        foreach (var path in new[] { "/api/tg/me", "/api/tg/parent/children", "/api/tg/teacher/today" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Oqituvchi_ota_ona_yuzasiga_kira_olmaydi()
    {
        var (teacher, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        using var client = ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, teacher.Id, teacher.FullName, teacher.Email));

        var response = await client.GetAsync("/api/tg/parent/children");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ota_ona_oqituvchi_yuzasiga_kira_olmaydi()
    {
        var (parent, _) = await fixture.Api.SeedUserAsync("parent");
        using var client = ClientWithToken(
            fixture.Api.TokenFor("parent", parent.Id, parent.FullName, parent.Email));

        var response = await client.GetAsync("/api/tg/teacher/today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Oqituvchi_bugungi_ekranini_ochadi()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            t.Permissions = [.. TeacherPermissions.All];
            await db.SaveChangesAsync();
        });

        using var client = ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email));

        var body = await GetJsonAsync(client, "/api/tg/teacher/today");

        Assert.Equal(AppClock.Today.ToString("yyyy-MM-dd"), body.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("lessons").ValueKind);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("pickups").ValueKind);
    }

    /// <summary>
    /// O'qilmagan xabarlar sanog'i — `chat_reads` jadvalining yagona ma'nosi.
    /// O'qituvchining O'Z xabari o'qilmagan bo'la olmaydi, shuning uchun xabar
    /// boshqa foydalanuvchi nomidan yoziladi.
    /// </summary>
    [Fact]
    public async Task Oqilmagan_xabarlar_sanaladi_va_oqilgan_deb_belgilanadi()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var (author, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            t.Permissions = [.. TeacherPermissions.All];
            db.ChatMessages.Add(new ChatMessage
            {
                ClassName = ChatService.StaffChannel,
                SenderUserId = author.Id,
                SenderName = author.FullName,
                SenderRole = Roles.Admin,
                Text = "Ertaga pedkengash soat 15:00 da.",
                CreatedAt = AppClock.Now,
            });
            await db.SaveChangesAsync();
        });

        using var client = ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email));

        var before = await GetJsonAsync(client, "/api/tg/teacher/chat/unread");
        var staffBefore = before.EnumerateArray()
            .Single(c => c.GetProperty("channel").GetString() == ChatService.StaffChannel);
        Assert.True(staffBefore.GetProperty("unread").GetInt32() >= 1);

        var marked = await client.PostAsync(
            $"/api/tg/teacher/chat/{ChatService.StaffChannel}/read", content: null);
        Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);

        var after = await GetJsonAsync(client, "/api/tg/teacher/chat/unread");
        var staffAfter = after.EnumerateArray()
            .Single(c => c.GetProperty("channel").GetString() == ChatService.StaffChannel);
        Assert.Equal(0, staffAfter.GetProperty("unread").GetInt32());
    }

    /// <summary>
    /// Kod chiqarish — admin bo'limi. O'qituvchi yoki ota-ona uni o'zi uchun
    /// chiqara olsa, bog'lanishning butun ma'nosi yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Kodni_faqat_admin_chiqara_oladi()
    {
        var (teacher, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        using var client = ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, teacher.Id, teacher.FullName, teacher.Email));

        var response = await client.PostAsJsonAsync("/api/admin/telegram/link-codes",
            new { userId = teacher.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static long _nextTelegramId = 700_000_000;
    private static int _nextPhone;

    private static long NewTelegramId() => Interlocked.Increment(ref _nextTelegramId);

    /// <summary>Har testga o'z raqami — vasiylardagi `phone_key` unikal.</summary>
    private static string NewPhone() =>
        $"+998 90 {Interlocked.Increment(ref _nextPhone):000} {DateTime.UtcNow:ss} {Random.Shared.Next(10, 99)}";

    private static Dictionary<string, string> InitFields(long telegramUserId) => new()
    {
        ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        ["query_id"] = "AAH" + telegramUserId,
        ["user"] = JsonSerializer.Serialize(new
        {
            id = telegramUserId,
            first_name = "Test",
            last_name = "Ota-ona",
            username = "test" + telegramUserId,
            language_code = "uz",
        }),
    };

    private async Task<HttpClient> AdminClientAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        return ClientWithToken(fixture.Api.TokenFor(Roles.Admin, user.Id, user.FullName, user.Email));
    }

    /// <summary>O'quvchi qo'shadi va uning id'sini qaytaradi.</summary>
    private static async Task<string> AddStudentAsync(
        HttpClient admin, string fullName, string className, string parentName, string parentPhone)
    {
        var created = await PostJsonAsync(admin, "/api/admin/students", new
        {
            fullName,
            birthDate = "2015-05-05",
            address = "Toshkent",
            gender = "male",
            parentFullName = parentName,
            parentPhone,
            className,
            enrollmentDate = "2026-09-01",
        });
        return created.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> FindGuardianAsync(HttpClient admin, string phone)
    {
        var list = await GetJsonAsync(admin, "/api/admin/guardians?search=" + Uri.EscapeDataString(phone));
        var rows = list.EnumerateArray().ToList();
        Assert.Single(rows);        // bitta raqam = bitta vasiy
        return rows[0];
    }

    private async Task<string> UserIdOfGuardianAsync(string guardianId)
    {
        string? userId = null;
        await fixture.Api.WithDbAsync(async db =>
            userId = await db.Guardians.Where(g => g.Id == guardianId)
                .Select(g => g.UserId).SingleAsync());
        Assert.False(string.IsNullOrEmpty(userId));
        return userId!;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), Json);
    }

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode,
            $"POST {path} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), Json);
    }

    // ---------------------------------------------------------------------
    //  Bot tokeni yoqilgan alohida host — fayl boshidagi izohga qarang
    // ---------------------------------------------------------------------

    private static readonly Lock HostLock = new();
    private static WebApplicationFactory<AuthController>? _host;

    private WebApplicationFactory<AuthController> Host
    {
        get
        {
            lock (HostLock)
            {
                if (_host is not null) return _host;
                var host = fixture.Api.WithWebHostBuilder(_ => { });
                // Singleton shu hosildagi konteynerniki — umumiy `fixture.Api` ga tegmaydi.
                host.Services.GetRequiredService<TelegramService>().Set(BotToken, "wunderkind_test_bot");
                return _host = host;
            }
        }
    }

    private HttpClient AnonymousClient() => Host.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private HttpClient ClientWithToken(string token)
    {
        var client = AnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
