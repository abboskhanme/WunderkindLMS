using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Mcp;

/// <summary>
/// MCP OAuth 2.1 flow (docs/modules/mcp-readonly.md "Tests"): metadata, DCR, authorize with
/// PKCE (success, wrong password, not-allowed user, bad redirect, missing/plain PKCE), token
/// exchange, refresh rotation, revocation, and the live permission re-check on /mcp.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class McpOAuthTests(ApiFixture fixture)
{
    private McpFlow Flow() => new(fixture.Api);

    [Fact]
    public async Task Metadata_hujjatlari_standartga_mos()
    {
        using var c = Flow().Client();
        using var prm = JsonDocument.Parse(await c.GetStringAsync("/.well-known/oauth-protected-resource/mcp"));
        Assert.Equal("https://localhost/mcp", prm.RootElement.GetProperty("resource").GetString());
        Assert.Equal("https://localhost", prm.RootElement.GetProperty("authorization_servers")[0].GetString());

        using var asm = JsonDocument.Parse(await c.GetStringAsync("/.well-known/oauth-authorization-server"));
        var r = asm.RootElement;
        Assert.Equal("https://localhost", r.GetProperty("issuer").GetString());
        Assert.Equal("https://localhost/oauth/authorize", r.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://localhost/oauth/token", r.GetProperty("token_endpoint").GetString());
        Assert.Equal("https://localhost/oauth/register", r.GetProperty("registration_endpoint").GetString());
        Assert.Equal(["S256"], r.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("refresh_token", r.GetProperty("grant_types_supported").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Tokensiz_mcp_401_va_resource_metadata_sarlavhasi()
    {
        var res = await Flow().PostMcpAsync(null, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var www = res.Headers.WwwAuthenticate.ToString();
        Assert.Contains("resource_metadata=\"https://localhost/.well-known/oauth-protected-resource/mcp\"", www);

        var bogus = await Flow().PostMcpAsync("wkm_at_notarealtoken", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
        Assert.Contains("invalid_token", bogus.Headers.WwwAuthenticate.ToString());

        // A web-panel JWT is NOT an MCP token.
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var jwt = admin.DefaultRequestHeaders.Authorization!.Parameter!;
        var withJwt = await Flow().PostMcpAsync(jwt, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, withJwt.StatusCode);
    }

    [Fact]
    public async Task DCR_https_va_loopback_qabul_qilinadi_boshqasi_rad()
    {
        var flow = Flow();
        Assert.StartsWith("mcp_", await flow.RegisterAsync("Claude", "https://claude.ai/api/mcp/auth_callback"));
        Assert.StartsWith("mcp_", await flow.RegisterAsync("Desktop", "http://localhost:6274/cb"));

        using var c = flow.Client();
        foreach (var bad in new[] { "http://evil.example.com/cb", "https://x.example/cb#frag", "javascript:alert(1)", "not a uri",
                     // H1: any https host is NOT enough — only the allow-listed AI hosts.
                     "https://evil.example.com/cb", "https://claude.ai.evil.com/cb", "https://claude.ai:8443/cb",
                     // non-canonical spellings are refused (raw must equal AbsoluteUri)
                     "HTTPS://claude.ai/api/mcp/auth_callback", "https://CLAUDE.ai/cb", "https://claude.ai/a/../cb",
                     "cursor://anysphere.cursor-retrieval/oauth/callback",
                     "https://claude.ai/" + new string('a', 600) })
        {
            // Fresh client IP per attempt: registration is rate-limited per IP (10/h).
            using var fresh = Flow().Client();
            var res = await fresh.PostAsJsonAsync("/oauth/register", new { client_name = "x", redirect_uris = new[] { bad } });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("invalid_redirect_uri", await res.Content.ReadAsStringAsync());
        }
        var none = await c.PostAsJsonAsync("/oauth/register", new { client_name = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);

        // Response carries no secret — public client.
        var ok = await c.PostAsJsonAsync("/oauth/register", new { client_name = "p", redirect_uris = new[] { McpFlow.RedirectUri } });
        var body = await ok.Content.ReadAsStringAsync();
        Assert.DoesNotContain("client_secret", body);
        Assert.Contains("\"token_endpoint_auth_method\":\"none\"", body);
    }

    [Fact]
    public async Task Toliq_oqim_admin_token_oladi_va_mcp_ishlaydi()
    {
        var flow = Flow();
        var (user, tokens) = await flow.ConnectNewAsync(Roles.Admin);
        Assert.StartsWith("wkm_at_", tokens.Access);
        Assert.StartsWith("wkm_rt_", tokens.Refresh);

        var init = await flow.InitializeAsync(tokens.Access);
        Assert.Equal("wunderkind-school", init["serverInfo"]!["name"]!.GetValue<string>());

        // Stored hashed, never in clear.
        await fixture.Api.WithDbAsync(async db =>
        {
            var grant = await db.McpGrants.SingleAsync(g => g.UserId == user.Id);
            var stored = await db.McpTokens.Where(t => t.GrantId == grant.Id).Select(t => t.TokenHash).ToListAsync();
            Assert.Equal(2, stored.Count);
            Assert.DoesNotContain(tokens.Access, stored);
            Assert.All(stored, h => Assert.Matches("^[0-9a-f]{64}$", h));
            Assert.NotNull((await db.McpAuthCodes.SingleAsync(c => c.UserId == user.Id)).ConsumedAt);
        });
    }

    [Fact]
    public async Task Notogri_parol_kod_bermaydi()
    {
        var flow = Flow();
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var clientId = await flow.RegisterAsync();
        var (_, challenge) = McpFlow.Pkce();
        var res = await flow.LoginAsync(clientId, challenge, user.Email, password + "x");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Login yoki parol noto&#39;g&#39;ri", html);
        Assert.Null(res.Headers.Location);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Staff)] // staff WITHOUT aiAccess
    public async Task Ruxsatsiz_foydalanuvchi_rad_etiladi(string role)
    {
        var flow = Flow();
        var (user, password) = await fixture.Api.SeedUserAsync(role, permissions: ["students", "finance"]);
        // A pupil account without a pupil row is blocked anyway — refused either way, and no
        // pupil row is added (the shared DB is near the arrears-pivot 600-pupil cap).
        var clientId = await flow.RegisterAsync();
        var (_, challenge) = McpFlow.Pkce();
        var res = await flow.LoginAsync(clientId, challenge, user.Email, password);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("AI ulanish ruxsati berilmagan", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AiAccess_li_xodim_ulanadi()
    {
        var (_, tokens) = await Flow().ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey, "students");
        Assert.StartsWith("wkm_at_", tokens.Access);
    }

    [Fact]
    public async Task Notogri_redirect_uri_redirect_qilmaydi()
    {
        var flow = Flow();
        var clientId = await flow.RegisterAsync();
        var (_, challenge) = McpFlow.Pkce();
        using var c = flow.Client();
        var res = await c.GetAsync(McpFlow.AuthorizeUrl(clientId, challenge, redirect: "https://evil.example.com/cb"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Null(res.Headers.Location);

        var unknown = await c.GetAsync(McpFlow.AuthorizeUrl("mcp_unknown", challenge));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Null(unknown.Headers.Location);

        // Loopback: another port is fine (RFC 8252 §7.3).
        var otherPort = await c.GetAsync(McpFlow.AuthorizeUrl(clientId, challenge, redirect: "http://127.0.0.1:61000/callback"));
        Assert.Equal(HttpStatusCode.OK, otherPort.StatusCode);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    public async Task PKCE_S256_majburiy(string method)
    {
        var flow = Flow();
        var clientId = await flow.RegisterAsync();
        var (verifier, challenge) = McpFlow.Pkce();
        using var c = flow.Client();
        var url = method == "plain"
            ? McpFlow.AuthorizeUrl(clientId, verifier, method: "plain")
            : QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
            {
                ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = McpFlow.RedirectUri, ["state"] = "s",
            });
        var res = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var q = QueryHelpers.ParseQuery(res.Headers.Location!.Query);
        Assert.Equal("invalid_request", q["error"].ToString());
        _ = challenge;
    }

    [Fact]
    public async Task Rad_etish_access_denied_qaytaradi()
    {
        var flow = Flow();
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.SuperAdmin);
        var clientId = await flow.RegisterAsync();
        var (_, challenge) = McpFlow.Pkce();
        Assert.Equal("access_denied", await flow.CodeAsync(clientId, challenge, user.Email, password, decision: "deny"));
    }

    [Fact]
    public async Task Token_almashinuvi_PKCE_va_kodni_qayta_ishlatish()
    {
        var flow = Flow();
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var clientId = await flow.RegisterAsync();
        var (verifier, challenge) = McpFlow.Pkce();
        var code = await flow.CodeAsync(clientId, challenge, user.Email, password);

        Dictionary<string, string> Form(string v) => new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = McpFlow.RedirectUri,
            ["client_id"] = clientId, ["code_verifier"] = v,
        };

        var wrong = await flow.TokenAsync(Form(McpFlow.Pkce().Verifier));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("invalid_grant", await wrong.Content.ReadAsStringAsync());

        var ok = await flow.TokenAsync(Form(verifier));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("no-store", ok.Headers.CacheControl?.ToString());
        using var doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        var access = doc.RootElement.GetProperty("access_token").GetString()!;
        Assert.Equal(3600, doc.RootElement.GetProperty("expires_in").GetInt32());

        // Replay of a used code: refused AND the grant it produced is revoked.
        var replay = await flow.TokenAsync(Form(verifier));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var after = await flow.PostMcpAsync(access, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotatsiya_va_eski_refreshni_qayta_ishlatish_grantni_bekor_qiladi()
    {
        var flow = Flow();
        var (_, tokens) = await flow.ConnectNewAsync(Roles.Admin);

        var r1 = await flow.TokenAsync(new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Refresh, ["client_id"] = tokens.ClientId,
        });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        using var doc = JsonDocument.Parse(await r1.Content.ReadAsStringAsync());
        var access2 = doc.RootElement.GetProperty("access_token").GetString()!;
        var refresh2 = doc.RootElement.GetProperty("refresh_token").GetString()!;
        Assert.NotEqual(tokens.Refresh, refresh2);
        await flow.InitializeAsync(access2);

        // Other client id cannot use it.
        var other = await flow.TokenAsync(new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refresh2, ["client_id"] = "mcp_other",
        });
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);

        // Reuse of the OLD refresh token = theft signal → whole grant revoked.
        var reuse = await flow.TokenAsync(new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Refresh, ["client_id"] = tokens.ClientId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        var dead = await flow.PostMcpAsync(access2, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, dead.StatusCode);
        var deadRefresh = await flow.TokenAsync(new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refresh2, ["client_id"] = tokens.ClientId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, deadRefresh.StatusCode);
    }

    [Fact]
    public async Task Superadmin_bekor_qilgan_token_401()
    {
        var flow = Flow();
        var (user, tokens) = await flow.ConnectNewAsync(Roles.Admin);
        await flow.InitializeAsync(tokens.Access);

        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var list = await boss.GetFromJsonAsync<JsonElement>("/api/admin/mcp/connections");
        var grant = list.EnumerateArray().First(g => g.GetProperty("userId").GetString() == user.Id);
        Assert.True(grant.GetProperty("active").GetBoolean());
        var revoke = await boss.PostAsync($"/api/admin/mcp/connections/{grant.GetProperty("id").GetString()}/revoke", null);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var res = await flow.PostMcpAsync(tokens.Access, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task AiAccess_olib_tashlansa_yoki_user_ochirilsa_token_ishlamaydi()
    {
        var flow = Flow();
        var (user, tokens) = await flow.ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey, "students");
        await flow.InitializeAsync(tokens.Access);

        await fixture.Api.WithDbAsync(async db =>
        {
            var u = await db.Users.SingleAsync(x => x.Id == user.Id);
            u.Permissions = ["students"];
            await db.SaveChangesAsync();
        });
        var res = await flow.PostMcpAsync(tokens.Access, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var refresh = await flow.TokenAsync(new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Refresh, ["client_id"] = tokens.ClientId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);

        var (admin, adminTokens) = await flow.ConnectNewAsync(Roles.Admin);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Users.Remove(await db.Users.SingleAsync(x => x.Id == admin.Id));
            await db.SaveChangesAsync();
        });
        var gone = await flow.PostMcpAsync(adminTokens.Access, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, gone.StatusCode);
    }

    [Fact]
    public async Task Revoke_endpoint_RFC7009()
    {
        var flow = Flow();
        var (_, tokens) = await flow.ConnectNewAsync(Roles.Admin);
        using var c = flow.Client();
        var res = await c.PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = tokens.Refresh }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dead = await flow.PostMcpAsync(tokens.Access, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, dead.StatusCode);
    }

    [Fact]
    public async Task Login_formasi_chastota_bilan_cheklangan()
    {
        var flow = Flow();
        var clientId = await flow.RegisterAsync();
        var (_, challenge) = McpFlow.Pkce();
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 12; i++)
            last = (await flow.LoginAsync(clientId, challenge, "nobody-" + i, "wrong")).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task AI_ulanishlar_API_faqat_superadmin()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/mcp/connections")).StatusCode);
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "staff", McpAccess.PermissionKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/mcp/audit")).StatusCode);
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var status = await boss.GetFromJsonAsync<JsonElement>("/api/admin/mcp/status");
        Assert.True(status.GetProperty("enabled").GetBoolean());
    }
}

/// <summary>Security review fixes (H1, M4, M5, L1, L5, L7, INFO) — OAuth side.</summary>
[Collection(SchoolLmsCollection.Name)]
public class McpOAuthHardeningTests(ApiFixture fixture)
{
    private McpFlow Flow() => new(fixture.Api);

    [Fact]
    public void Redirect_allow_list_qoidalari()
    {
        var o = new SchoolLms.Server.Mcp.McpOptions();
        Assert.True(o.IsAllowedRedirectUri("https://claude.ai/api/mcp/auth_callback"));
        Assert.True(o.IsAllowedRedirectUri("https://chatgpt.com/connector_platform_oauth_redirect"));
        Assert.True(o.IsAllowedRedirectUri("http://127.0.0.1:33418/callback"));
        Assert.True(o.IsAllowedRedirectUri("http://localhost:6274/cb"));
        Assert.False(o.IsAllowedRedirectUri("https://evil.example.com/cb"));
        Assert.False(o.IsAllowedRedirectUri("http://claude.ai/cb"));
        Assert.False(o.IsAllowedRedirectUri("https://user:pw@claude.ai/cb"));
        Assert.False(o.IsAllowedRedirectUri("https://xn--claude-8va.ai/cb")); // look-alike IDN
        var custom = new SchoolLms.Server.Mcp.McpOptions { AllowedRedirectHosts = new HashSet<string> { "example.org" } };
        Assert.True(custom.IsAllowedRedirectUri("https://example.org/cb"));
        Assert.False(custom.IsAllowedRedirectUri("https://claude.ai/api/mcp/auth_callback"));
        Assert.Equal("kompyuteringizdagi dastur", SchoolLms.Server.Mcp.McpOptions.DestinationLabel("http://127.0.0.1:1/cb"));
        Assert.Equal("claude.ai", SchoolLms.Server.Mcp.McpOptions.DestinationLabel("https://claude.ai/cb"));
    }

    [Fact]
    public async Task Client_name_tozalanadi_va_uri_soni_cheklangan()
    {
        using var c = Flow().Client();
        var res = await c.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Wunder‮kind​ official\n\u0007" + new string('x', 200),
            redirect_uris = new[] { McpFlow.RedirectUri },
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var name = doc.RootElement.GetProperty("client_name").GetString()!;
        Assert.True(name.Length <= 60, name);
        Assert.StartsWith("Wunderkind official xxx", name);
        Assert.DoesNotContain(name, ch => char.IsControl(ch) || ch is '‮' or '​');

        var six = await c.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "x", redirect_uris = Enumerable.Range(1, 6).Select(i => $"http://127.0.0.1:{5000 + i}/cb").ToArray(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, six.StatusCode);
        var wrongKind = await c.PostAsJsonAsync("/oauth/register", new { client_name = "x", redirect_uris = new[] { McpFlow.RedirectUri }, grant_types = new object[] { 1 } });
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);
        var notArray = await c.PostAsJsonAsync("/oauth/register", new { client_name = "x", redirect_uris = McpFlow.RedirectUri });
        Assert.Equal(HttpStatusCode.BadRequest, notArray.StatusCode);
    }

    [Fact]
    public async Task Rozilik_sahifasi_kalit_qayerga_ketishini_korsatadi_va_bookkeeping()
    {
        var flow = Flow();
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var clientId = await flow.RegisterAsync();
        var (_, challenge) = McpFlow.Pkce();
        var res = await flow.LoginAsync(clientId, challenge, user.Email, password);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("kompyuteringizdagi dastur", html);
        Assert.Contains(clientId, html);
        // L5 — same bookkeeping as a normal login.
        await fixture.Api.WithDbAsync(async db =>
        {
            var u = await db.Users.SingleAsync(x => x.Id == user.Id);
            Assert.Null(u.InitialPassword);
            Assert.NotNull(u.LastLoginAt);
            Assert.NotNull(u.FirstLoginAt);
        });
    }

    [Fact]
    public async Task Juda_uzun_state_invalid_request()
    {
        var flow = Flow();
        var clientId = await flow.RegisterAsync();
        var (_, challenge) = McpFlow.Pkce();
        using var c = flow.Client();
        var res = await c.GetAsync(McpFlow.AuthorizeUrl(clientId, challenge, state: new string('s', 1001)));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("invalid_request", QueryHelpers.ParseQuery(res.Headers.Location!.Query)["error"].ToString());
    }

    [Fact]
    public async Task OpenId_alias_yoq()
    {
        using var c = Flow().Client();
        var res = await c.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Bir_login_uchun_xato_urinishlar_cheklanadi()
    {
        var flow = Flow();
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var clientId = await flow.RegisterAsync();
        var (_, challenge) = McpFlow.Pkce();
        for (var i = 0; i < 5; i++)
            await flow.LoginAsync(clientId, challenge, user.Email, "wrong-" + i);
        // Correct password now — still refused for the cool-down (another IP would be too).
        var other = Flow();
        var res = await other.LoginAsync(clientId, challenge, user.Email, password);
        Assert.Contains("Juda ko&#39;p noto&#39;g&#39;ri urinish", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Parol_ozgarsa_AI_ulanishlari_bekor_bolad()
    {
        // 1) Own account page.
        var flow = Flow();
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: [McpAccess.PermissionKey, "students"]);
        var tokens = await flow.ConnectAsync(user.Email, password);
        await flow.InitializeAsync(tokens.Access);
        using (var own = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Staff, user.Id)))
        {
            var put = await own.PutAsJsonAsync("/api/auth/account", new { currentPassword = password, newPassword = "Yangi-parol-123" });
            Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await flow.PostMcpAsync(tokens.Access, new { jsonrpc = "2.0", id = 1, method = "tools/list" })).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal("system:password-change", (await db.McpGrants.SingleAsync(g => g.UserId == user.Id)).RevokedBy));

        // 2) Superadmin resets a staff member's password.
        var flow2 = Flow();
        var (staff, pw2) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: [McpAccess.PermissionKey, "students"]);
        var t2 = await flow2.ConnectAsync(staff.Email, pw2);
        using var boss = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var reset = await boss.PostAsync($"/api/admin/staff/{staff.Id}/reset-password", null);
        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await flow2.PostMcpAsync(t2.Access, new { jsonrpc = "2.0", id = 1, method = "tools/list" })).StatusCode);
        var refresh = await flow2.TokenAsync(new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = t2.Refresh, ["client_id"] = t2.ClientId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task Ulanish_90_kundan_keyin_yangilanmaydi()
    {
        var flow = Flow();
        var (user, tokens) = await flow.ConnectNewAsync(Roles.Admin);
        await fixture.Api.WithDbAsync(async db =>
        {
            var g = await db.McpGrants.SingleAsync(x => x.UserId == user.Id);
            g.CreatedAt = DateTimeOffset.UtcNow.AddDays(-91);
            await db.SaveChangesAsync();
        });
        var refresh = await flow.TokenAsync(new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Refresh, ["client_id"] = tokens.ClientId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.Contains("90", await refresh.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await flow.PostMcpAsync(tokens.Access, new { jsonrpc = "2.0", id = 1, method = "tools/list" })).StatusCode);
    }

    [Fact]
    public async Task Kod_parallel_almashinuvda_faqat_bir_marta_ishlaydi()
    {
        var flow = Flow();
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var clientId = await flow.RegisterAsync();
        var (verifier, challenge) = McpFlow.Pkce();
        var code = await flow.CodeAsync(clientId, challenge, user.Email, password);
        Dictionary<string, string> Form() => new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = McpFlow.RedirectUri,
            ["client_id"] = clientId, ["code_verifier"] = verifier,
        };
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => flow.TokenAsync(Form())));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact]
    public async Task Audit_ip_va_user_agent_saqlanadi()
    {
        var flow = Flow();
        var (user, tokens) = await flow.ConnectNewAsync(Roles.Admin);
        await flow.CallAsync(tokens.Access, "classes_list");
        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.McpAudit.OrderByDescending(a => a.Id).FirstAsync(a => a.UserId == user.Id);
            Assert.Equal(flow.Ip, row.Ip);
            Assert.False(string.IsNullOrEmpty(row.UserAgent));
        });
    }
}
