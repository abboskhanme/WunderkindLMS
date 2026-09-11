using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Login va token oqimi. Ayni paytda bu harness'ning "JWT bera oladimi" qismini
/// isbotlaydi (P1-03 qabul mezoni), keyinchalik esa RBAC testlarining poydevori bo'ladi.
///
/// <para>
/// DIQQAT: <c>/api/auth/login</c> da IP bo'yicha daqiqada 10 ta urinish chegarasi bor
/// (Program.cs, "login" policy). TestServer'da IP null — hamma test bitta "unknown"
/// partitsiyada. Shuning uchun bu faylda LOGIN so'rovlari soni ataylab kam (4 ta).
/// Yangi login testi qo'shsangiz chegarani hisobga oling.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AuthEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Login_notogri_parol_bilan_401_va_token_bermaydi()
    {
        var (user, password) = await fixture.Api.SeedUserAsync("admin");
        using var client = fixture.Api.AnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = password + "-xato" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        Assert.Equal("Login yoki parol noto'g'ri", body.RootElement.GetProperty("message").GetString());
        // Javobda token sizib chiqmasin.
        Assert.False(body.RootElement.TryGetProperty("token", out _));
        Assert.DoesNotContain("eyJ", raw, StringComparison.Ordinal); // JWT prefiksi

        // Muvaffaqiyatsiz urinish akkauntga TEGMASLIGI kerak.
        await fixture.Api.WithDbAsync(async db =>
        {
            var fresh = await db.Users.SingleAsync(u => u.Id == user.Id);
            Assert.Null(fresh.LastLoginAt);
            Assert.Equal(password, fresh.InitialPassword);
        });
    }

    [Fact]
    public async Task Login_mavjud_bolmagan_login_bilan_401()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "yoq-" + Guid.NewGuid().ToString("N"), password = "istalgan-parol" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // Xabar mavjud bo'lmagan login va noto'g'ri parol uchun BIR XIL —
        // aks holda login sanab chiqish (user enumeration) mumkin bo'lardi.
        Assert.Equal("Login yoki parol noto'g'ri", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_togri_parol_bilan_200_va_ishlaydigan_token_qaytaradi()
    {
        var (user, password) = await fixture.Api.SeedUserAsync("admin");
        using var client = fixture.Api.AnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var token = root.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var returned = root.GetProperty("user");
        Assert.Equal(user.Id, returned.GetProperty("id").GetString());
        Assert.Equal(user.Email, returned.GetProperty("email").GetString());
        Assert.Equal(user.FullName, returned.GetProperty("fullName").GetString());
        Assert.Equal("admin", returned.GetProperty("role").GetString());
        // Parol/hash javobda BO'LMASLIGI kerak.
        Assert.False(returned.TryGetProperty("passwordHash", out _));
        Assert.False(returned.TryGetProperty("initialPassword", out _));

        // Token haqiqiy JWT va to'g'ri da'volarni olib yuradi.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(user.Id, jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("admin", jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);

        // Token HAQIQATAN ishlaydi — himoyalangan endpoint'ni ochadi.
        using var authed = fixture.Api.ClientWithToken(token!);
        var me = await authed.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var meBody = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(user.Id, meBody.RootElement.GetProperty("id").GetString());

        // Login kuzatuvi: LastLoginAt/FirstLoginAt yoziladi, ochiq dastlabki parol o'chadi.
        await fixture.Api.WithDbAsync(async db =>
        {
            var fresh = await db.Users.SingleAsync(u => u.Id == user.Id);
            Assert.Null(fresh.InitialPassword);
            Assert.False(string.IsNullOrEmpty(fresh.LastLoginAt));
            Assert.Equal(fresh.FirstLoginAt, fresh.LastLoginAt);
        });
    }

    [Fact]
    public async Task Me_tokensiz_401_qaytaradi()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Me_muddati_otgan_token_bilan_401_qaytaradi()
    {
        var (user, _) = await fixture.Api.SeedUserAsync("admin");
        // JWT standart clock skew'i 5 daqiqa — undan kattaroq o'tmish kerak.
        var expired = fixture.Api.TokenFor("admin", user.Id, lifetime: TimeSpan.FromMinutes(-10));
        using var client = fixture.Api.ClientWithToken(expired);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Bearer challenge sababni aytadi — token MUDDATI o'tgan (imzo buzilgani emas).
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("invalid_token", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expired", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Me_buzilgan_imzo_bilan_401_qaytaradi()
    {
        var (user, _) = await fixture.Api.SeedUserAsync("admin");
        var token = fixture.Api.TokenFor("admin", user.Id);
        // Oxirgi belgini almashtiramiz — imzo buziladi.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        using var client = fixture.Api.ClientWithToken(tampered);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// P1-03 qabul mezoni: ApiFactory <c>superadmin | admin | cashier | staff | teacher</c>
    /// rollarining HAR BIRI uchun ishlaydigan token bera olsin.
    /// <c>cashier</c> ataylab shu ro'yxatda — u hali <c>Roles.cs</c> da yo'q (P1-04),
    /// ya'ni harness rolni enum emas, string sifatida qabul qilishi isbotlanadi.
    /// </summary>
    [Theory]
    [InlineData("superadmin")]
    [InlineData("admin")]
    [InlineData("cashier")]
    [InlineData("staff")]
    [InlineData("teacher")]
    public async Task ClientAsAsync_har_bir_rol_uchun_ishlaydigan_token_beradi(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        using var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(user.Id, body.RootElement.GetProperty("id").GetString());
        Assert.Equal(role, body.RootElement.GetProperty("role").GetString());
        Assert.Equal(user.FullName, body.RootElement.GetProperty("fullName").GetString());
    }

    /// <summary>
    /// <c>ClientAsAsync</c> — bitta qatorlik qulaylik metodi: foydalanuvchini o'zi yaratadi.
    /// Token revocation (Program.cs, OnTokenValidated) admin/staff/teacher uchun bazada
    /// qator TALAB qiladi, shuning uchun bu metod haqiqatan 200 berishi kerak.
    /// </summary>
    [Theory]
    [InlineData("superadmin")]
    [InlineData("staff")]
    [InlineData("teacher")]
    public async Task ClientAsAsync_bir_qatorda_ishlaydigan_klient_beradi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(role, body.RootElement.GetProperty("role").GetString());
    }

    /// <summary>
    /// Token revocation: bazada qatori yo'q admin tokeni — imzo to'g'ri bo'lsa ham 401.
    /// Bu harness'ning "token yasadim, lekin foydalanuvchi yaratmadim" xatosini ham tutadi.
    /// </summary>
    [Fact]
    public async Task Bazada_qatori_yoq_admin_tokeni_401()
    {
        var token = fixture.Api.TokenFor("admin", "yoq-" + Guid.NewGuid().ToString("N"));
        using var client = fixture.Api.ClientWithToken(token);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
