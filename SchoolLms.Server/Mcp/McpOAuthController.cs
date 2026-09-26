using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Mcp;

/// <summary>
/// OAuth 2.1 authorization server for the read-only MCP server — the flow the MCP
/// authorization spec prescribes:
/// <list type="bullet">
///   <item><c>/.well-known/oauth-protected-resource[/mcp]</c> — RFC 9728;</item>
///   <item><c>/.well-known/oauth-authorization-server</c> — RFC 8414;</item>
///   <item><c>POST /oauth/register</c> — RFC 7591 dynamic client registration (public clients);</item>
///   <item><c>GET/POST /oauth/authorize</c> — OUR login page, then a consent screen; PKCE S256 only;</item>
///   <item><c>POST /oauth/token</c> — authorization_code (+PKCE) and refresh_token (rotating);</item>
///   <item><c>POST /oauth/revoke</c> — RFC 7009.</item>
/// </list>
/// The password is typed into this page, never into the AI client. Only
/// <see cref="McpAccess.IsAllowed"/> accounts get a code. Codes and tokens are stored hashed.
/// When the feature is off (no read-only connection) every action answers 404.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[AllowAnonymous]
public sealed class McpOAuthController(
    AppDbContext db,
    McpFeature feature,
    McpOptions options,
    IMemoryCache cache,
    IDataProtectionProvider dataProtection,
    ILogger<McpOAuthController> logger) : ControllerBase
{

    private static readonly string DummyHash = PasswordHasher.Hash("not-a-real-password-" + Guid.NewGuid());

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    // =====================================================================
    //  Metadata
    // =====================================================================

    [HttpGet("/.well-known/oauth-protected-resource")]
    [HttpGet("/.well-known/oauth-protected-resource/mcp")]
    public IActionResult ProtectedResource()
    {
        if (!feature.Enabled) return NotFound();
        return OAuthJson(new Dictionary<string, object>
        {
            ["resource"] = McpUrls.Resource(Request),
            ["authorization_servers"] = new[] { McpUrls.BaseUrl(Request) },
            ["scopes_supported"] = new[] { McpAccess.Scope },
            ["bearer_methods_supported"] = new[] { "header" },
            ["resource_name"] = "Wunderkind maktab tizimi (faqat o'qish)",
        });
    }

    [HttpGet("/.well-known/oauth-authorization-server")]
    [HttpGet("/.well-known/oauth-authorization-server/mcp")]
    public IActionResult AuthorizationServer()
    {
        if (!feature.Enabled) return NotFound();
        var b = McpUrls.BaseUrl(Request);
        return OAuthJson(new Dictionary<string, object>
        {
            ["issuer"] = b,
            ["authorization_endpoint"] = b + "/oauth/authorize",
            ["token_endpoint"] = b + "/oauth/token",
            ["registration_endpoint"] = b + "/oauth/register",
            ["revocation_endpoint"] = b + "/oauth/revoke",
            ["response_types_supported"] = new[] { "code" },
            ["response_modes_supported"] = new[] { "query" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            ["revocation_endpoint_auth_methods_supported"] = new[] { "none" },
            ["scopes_supported"] = new[] { McpAccess.Scope },
            ["authorization_response_iss_parameter_supported"] = true,
        });
    }

    // =====================================================================
    //  Dynamic client registration (RFC 7591)
    // =====================================================================

    [HttpPost("/oauth/register")]
    [EnableRateLimiting(McpSetup.RegisterRateLimitPolicy)]
    public async Task<IActionResult> Register(CancellationToken ct)
    {
        if (!feature.Enabled) return NotFound();

        JsonElement body;
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            body = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return RegistrationError("invalid_client_metadata", "Body must be a JSON object");
        }
        if (body.ValueKind != JsonValueKind.Object)
            return RegistrationError("invalid_client_metadata", "Body must be a JSON object");

        var uris = new List<string>();
        if (!body.TryGetProperty("redirect_uris", out var ru) || ru.ValueKind != JsonValueKind.Array)
            return RegistrationError("invalid_redirect_uri", "redirect_uris (array) required");
        foreach (var u in ru.EnumerateArray())
        {
            if (u.ValueKind != JsonValueKind.String)
                return RegistrationError("invalid_redirect_uri", "redirect_uris must be strings");
            uris.Add(u.GetString()!);
        }
        if (uris.Count == 0 || uris.Count > McpLimits.MaxRedirectUris)
            return RegistrationError("invalid_redirect_uri", $"1..{McpLimits.MaxRedirectUris} redirect_uris required");
        foreach (var u in uris)
            if (!options.IsAllowedRedirectUri(u))
                return RegistrationError("invalid_redirect_uri",
                    "redirect_uri must be https on an allowed AI host (" + string.Join(", ", options.AllowedRedirectHosts.Order())
                    + ") or http on loopback (localhost/127.0.0.1/[::1]); canonical form, no fragment, max "
                    + McpLimits.MaxUriLength + " chars");

        foreach (var (prop, allowed) in new[] { ("grant_types", new[] { "authorization_code", "refresh_token" }), ("response_types", new[] { "code" }) })
        {
            if (!body.TryGetProperty(prop, out var arr)) continue;
            if (arr.ValueKind != JsonValueKind.Array)
                return RegistrationError("invalid_client_metadata", prop + " must be an array");
            foreach (var g in arr.EnumerateArray())
                if (g.ValueKind != JsonValueKind.String || !allowed.Contains(g.GetString()))
                    return RegistrationError("invalid_client_metadata", prop + ": only " + string.Join(", ", allowed));
        }

        var name = body.TryGetProperty("client_name", out var cn) && cn.ValueKind == JsonValueKind.String
            ? McpText.Sanitize(cn.GetString(), McpLimits.MaxClientName) : "";
        if (name.Length == 0) name = "AI mijoz";

        // Unauthenticated endpoint: a global daily cap on top of the per-IP limit, and clients
        // that never completed a connection are pruned after 24 h.
        var store = new McpOAuthStore(db);
        await store.PruneAsync(ct);
        var dayAgo = DateTimeOffset.UtcNow.AddDays(-1);
        if (await db.McpClients.CountAsync(c => c.CreatedAt > dayAgo, ct) >= McpLimits.MaxRegistrationsPerDay)
        {
            logger.LogWarning("MCP client registration refused: daily cap {Cap} reached", McpLimits.MaxRegistrationsPerDay);
            return OAuthJson(new Dictionary<string, object>
            {
                ["error"] = "temporarily_unavailable",
                ["error_description"] = "Registration limit reached, try again later",
            }, StatusCodes.Status429TooManyRequests);
        }

        var client = new McpClient
        {
            Id = "mcp_" + McpSecrets.Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)),
            Name = name,
            RedirectUris = uris,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.McpClients.Add(client);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("MCP client registered: {ClientId} '{Name}'", client.Id, client.Name);

        // Public client — no secret. token_endpoint_auth_method is forced to "none" whatever
        // was asked for (RFC 7591 §3.2.1 lets the server replace requested metadata).
        Response.Headers.CacheControl = "no-store";
        return new ContentResult
        {
            StatusCode = StatusCodes.Status201Created,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["client_id"] = client.Id,
                ["client_id_issued_at"] = client.CreatedAt.ToUnixTimeSeconds(),
                ["client_name"] = client.Name,
                ["redirect_uris"] = client.RedirectUris,
                ["grant_types"] = new[] { "authorization_code", "refresh_token" },
                ["response_types"] = new[] { "code" },
                ["token_endpoint_auth_method"] = "none",
                ["scope"] = McpAccess.Scope,
            }, Json),
        };
    }

    // =====================================================================
    //  Authorization endpoint — login page, consent, code
    // =====================================================================

    private sealed record AuthRequest(
        string ClientId, string RedirectUri, string CodeChallenge, string? State, string Resource,
        string? UserId = null);

    [HttpGet("/oauth/authorize")]
    public async Task<IActionResult> Authorize(
        [FromQuery(Name = "response_type")] string? responseType,
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery] string? state,
        [FromQuery] string? scope,
        [FromQuery] string? resource,
        CancellationToken ct)
    {
        if (!feature.Enabled) return NotFound();

        // 1) Client and redirect URI first — until both are trusted we must NOT redirect.
        var client = string.IsNullOrEmpty(clientId) ? null
            : await db.McpClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client is null)
            return Page("Xato", ErrorBox("Noma'lum ilova (client_id). AI ilovasida ulanishni qaytadan qo'shing."), 400);
        var matched = MatchRedirectUri(client, redirectUri);
        if (matched is null)
            return Page("Xato", ErrorBox("Qaytish manzili (redirect_uri) ro'yxatdan o'tganiga mos emas yoki ruxsat etilmagan."), 400);

        // 2) From here on errors go back to the client (RFC 6749 §4.1.2.1) — the URI is registered
        //    AND on the allow-list (RedirectError re-checks).
        if (state is { Length: > McpLimits.MaxState })
            return RedirectError(matched, "invalid_request", "state too long", null);
        if (responseType != "code")
            return RedirectError(matched, "unsupported_response_type", "Only response_type=code", state);
        if (codeChallengeMethod != "S256" || !McpSecrets.IsValidChallenge(codeChallenge))
            return RedirectError(matched, "invalid_request", "PKCE with code_challenge_method=S256 is required", state);
        var canonicalResource = McpUrls.Resource(Request);
        if (!string.IsNullOrEmpty(resource) && !SameResource(resource, canonicalResource))
            return RedirectError(matched, "invalid_target", "Unknown resource", state);
        _ = scope; // Only one scope exists; whatever was asked, `school.read` is what is granted.

        var req = new AuthRequest(client.Id, matched, codeChallenge!, state, canonicalResource);
        return LoginPage(client, matched, Protect(req, TimeSpan.FromMinutes(15)), null);
    }

    [HttpPost("/oauth/authorize")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> AuthorizePost(CancellationToken ct)
    {
        if (!feature.Enabled) return NotFound();
        if (!Request.HasFormContentType) return Page("Xato", ErrorBox("Noto'g'ri so'rov."), 400);
        var form = await Request.ReadFormAsync(ct);
        var step = form["step"].ToString();

        var req = Unprotect(form["req"].ToString());
        if (req is null)
            return Page("Muddati o'tdi", ErrorBox("Sahifa muddati o'tdi. AI ilovasida ulanishni qaytadan boshlang."), 400);
        var client = await db.McpClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.ClientId, ct);
        if (client is null || MatchRedirectUri(client, req.RedirectUri) is null)
            return Page("Xato", ErrorBox("Ilova topilmadi."), 400);

        if (step == "login")
        {
            var login = form["login"].ToString().Trim();
            var password = form["password"].ToString();
            if (login.Length > 200) login = login[..200];

            // Per-login throttle (on top of the per-IP limiter): a distributed guess at ONE
            // account stops after 5 failures for 15 minutes.
            var failKey = "mcp:login-fail:" + login.ToLowerInvariant();
            if (cache.TryGetValue(failKey, out int fails) && fails >= McpLimits.MaxFailuresPerLogin)
            {
                logger.LogWarning("MCP OAuth login throttled: login={Login}", McpText.Sanitize(login, 60));
                return LoginPage(client, req.RedirectUri, form["req"].ToString(),
                    "Juda ko'p noto'g'ri urinish. 15 daqiqadan keyin qayta urinib ko'ring.");
            }

            var user = login.Length == 0 ? null : await db.Users.FirstOrDefaultAsync(u => u.Email == login, ct);
            var ok = PasswordHasher.Verify(password, user?.PasswordHash ?? DummyHash) && user is not null;
            if (!ok)
            {
                cache.Set(failKey, fails + 1, McpLimits.FailureWindow);
                logger.LogWarning("MCP OAuth failed login: login={Login}, IP={IP}",
                    McpText.Sanitize(login, 60), HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?");
                return LoginPage(client, req.RedirectUri, form["req"].ToString(), "Login yoki parol noto'g'ri.");
            }
            cache.Remove(failKey);
            if (await SessionFactory.IsBlockedAsync(db, user!, ct) || !McpAccess.IsAllowed(user!))
            {
                logger.LogWarning("MCP OAuth refused: user {UserId} ({Role}) has no AI access", user!.Id, user.Role);
                return Page("Ruxsat yo'q", ErrorBox(
                    "Sizning akkauntingizga AI ulanish ruxsati berilmagan. Bu imkoniyat faqat rahbariyat uchun — "
                    + "kerak bo'lsa, direktorga murojaat qiling (Boshqaruv → Rollar → «AI ulanish»)."), 403);
            }
            // Same first-login bookkeeping as SessionFactory: the user has now logged in, so the
            // superadmin screen must not keep showing the initial password in the clear.
            var nowIso = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            if (string.IsNullOrEmpty(user!.FirstLoginAt)) user.FirstLoginAt = nowIso;
            user.LastLoginAt = nowIso;
            user.InitialPassword = null;
            await db.SaveChangesAsync(ct);

            var consent = Protect(req with { UserId = user.Id }, TimeSpan.FromMinutes(5));
            return ConsentPage(client, req.RedirectUri, user.FullName, consent);
        }

        if (step == "consent" && req.UserId is not null)
        {
            if (form["decision"] != "allow")
                return RedirectError(req.RedirectUri, "access_denied", "The user denied the request", req.State);

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.UserId, ct);
            if (user is null || !McpAccess.IsAllowed(user))
                return Page("Ruxsat yo'q", ErrorBox("AI ulanish ruxsati yo'q."), 403);

            var code = McpSecrets.New(McpSecrets.CodePrefix);
            var now = DateTimeOffset.UtcNow;
            db.McpAuthCodes.Add(new McpAuthCode
            {
                CodeHash = McpSecrets.Hash(code),
                ClientId = req.ClientId,
                UserId = user.Id,
                RedirectUri = req.RedirectUri,
                CodeChallenge = req.CodeChallenge,
                Resource = req.Resource,
                CreatedAt = now,
                ExpiresAt = now + McpOAuthStore.CodeLifetime,
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("MCP OAuth code issued: user {UserId}, client {ClientId}", user.Id, req.ClientId);

            var query = new Dictionary<string, string?> { ["code"] = code, ["iss"] = McpUrls.BaseUrl(Request) };
            if (req.State is not null) query["state"] = req.State;
            return Redirect(QueryHelpers.AddQueryString(req.RedirectUri, query));
        }

        return Page("Xato", ErrorBox("Noto'g'ri so'rov."), 400);
    }

    // =====================================================================
    //  Token endpoint
    // =====================================================================

    [HttpPost("/oauth/token")]
    [EnableRateLimiting(McpSetup.TokenRateLimitPolicy)]
    public async Task<IActionResult> Token(CancellationToken ct)
    {
        if (!feature.Enabled) return NotFound();
        if (!Request.HasFormContentType) return OAuthError("invalid_request", "application/x-www-form-urlencoded body required");
        var form = await Request.ReadFormAsync(ct);
        var store = new McpOAuthStore(db);
        var grantType = form["grant_type"].ToString();
        var clientId = form["client_id"].ToString();

        if (grantType == "authorization_code")
        {
            var code = form["code"].ToString();
            var verifier = form["code_verifier"].ToString();
            var redirectUri = form["redirect_uri"].ToString();
            if (code.Length == 0 || code.Length > 200) return OAuthError("invalid_grant", "code missing");
            if (!McpSecrets.IsValidVerifier(verifier)) return OAuthError("invalid_grant", "code_verifier missing or malformed");

            var hash = McpSecrets.Hash(code);
            var row = await db.McpAuthCodes.FirstOrDefaultAsync(c => c.CodeHash == hash, ct);
            if (row is null) return OAuthError("invalid_grant", "unknown code");

            if (row.ConsumedAt is not null)
            {
                // Replay: the code leaked. Kill whatever it produced (RFC 6819 §4.4.1.1).
                if (row.GrantId is { } leaked) await store.RevokeGrantAsync(leaked, "system:code-replay", ct);
                logger.LogWarning("MCP OAuth code replay for client {ClientId}; grant revoked", row.ClientId);
                return OAuthError("invalid_grant", "code already used");
            }
            if (row.ExpiresAt <= DateTimeOffset.UtcNow) return OAuthError("invalid_grant", "code expired");
            if (!McpSecrets.VerifyPkce(verifier, row.CodeChallenge)) return OAuthError("invalid_grant", "PKCE verification failed");
            if (!string.Equals(row.ClientId, clientId, StringComparison.Ordinal)) return OAuthError("invalid_grant", "client mismatch");
            if (!string.Equals(row.RedirectUri, redirectUri, StringComparison.Ordinal)) return OAuthError("invalid_grant", "redirect_uri mismatch");
            var resource = form["resource"].ToString();
            if (resource.Length > 0 && !SameResource(resource, row.Resource)) return OAuthError("invalid_target", "resource mismatch");

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
            if (user is null || !McpAccess.IsAllowed(user)) return OAuthError("invalid_grant", "user no longer allowed");

            // Atomic single use: of two concurrent exchanges exactly one flips consumed_at.
            var won = await db.McpAuthCodes.Where(c => c.Id == row.Id && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, DateTimeOffset.UtcNow), ct);
            if (won != 1) return OAuthError("invalid_grant", "code already used");

            var grant = new McpGrant
            {
                ClientId = row.ClientId, UserId = row.UserId, Resource = row.Resource,
                Scope = McpAccess.Scope, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.McpGrants.Add(grant);
            row.GrantId = grant.Id;
            var issued = store.Issue(grant);
            await db.SaveChangesAsync(ct);
            await store.PruneAsync(ct);
            return TokenResponse(issued);
        }

        if (grantType == "refresh_token")
        {
            var refresh = form["refresh_token"].ToString();
            if (!refresh.StartsWith(McpSecrets.RefreshPrefix, StringComparison.Ordinal) || refresh.Length > 200)
                return OAuthError("invalid_grant", "unknown refresh_token");
            var hash = McpSecrets.Hash(refresh);
            var tok = await db.McpTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Kind == McpTokenKind.Refresh, ct);
            if (tok is null) return OAuthError("invalid_grant", "unknown refresh_token");
            var grant = await db.McpGrants.FirstOrDefaultAsync(g => g.Id == tok.GrantId, ct);
            if (grant is null || grant.RevokedAt is not null) return OAuthError("invalid_grant", "grant revoked");
            if (!string.Equals(grant.ClientId, clientId, StringComparison.Ordinal)) return OAuthError("invalid_grant", "client mismatch");

            if (tok.ConsumedAt is not null)
            {
                // Rotation reuse = the old refresh token is in someone else's hands.
                await store.RevokeGrantAsync(grant.Id, "system:refresh-reuse", ct);
                logger.LogWarning("MCP refresh token reuse on grant {GrantId}; grant revoked", grant.Id);
                return OAuthError("invalid_grant", "refresh_token already used");
            }
            if (tok.ExpiresAt <= DateTimeOffset.UtcNow) return OAuthError("invalid_grant", "refresh_token expired");
            if (grant.CreatedAt + McpLimits.GrantLifetime <= DateTimeOffset.UtcNow)
                return OAuthError("invalid_grant", "connection older than 90 days — sign in again");

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == grant.UserId, ct);
            if (user is null || !McpAccess.IsAllowed(user)) return OAuthError("invalid_grant", "user no longer allowed");

            // Atomic rotation: a racing second use of the same refresh token loses here.
            var won = await db.McpTokens.Where(t => t.Id == tok.Id && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTimeOffset.UtcNow), ct);
            if (won != 1)
            {
                await store.RevokeGrantAsync(grant.Id, "system:refresh-reuse", ct);
                return OAuthError("invalid_grant", "refresh_token already used");
            }
            var issued = store.Issue(grant);
            await db.SaveChangesAsync(ct);
            return TokenResponse(issued);
        }

        return OAuthError("unsupported_grant_type", "authorization_code or refresh_token");
    }

    /// <summary>RFC 7009. Revoking either token of a pair ends the whole connection. Always 200.</summary>
    [HttpPost("/oauth/revoke")]
    [EnableRateLimiting(McpSetup.TokenRateLimitPolicy)]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        if (!feature.Enabled) return NotFound();
        if (!Request.HasFormContentType) return OAuthError("invalid_request", "form body required");
        var form = await Request.ReadFormAsync(ct);
        var token = form["token"].ToString();
        if (token.Length is > 0 and <= 200)
        {
            var hash = McpSecrets.Hash(token);
            var tok = await db.McpTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (tok is not null) await new McpOAuthStore(db).RevokeGrantAsync(tok.GrantId, "client:revoke", ct);
        }
        return Ok();
    }

    // =====================================================================
    //  Redirect URI rules
    // =====================================================================

    /// <summary>
    /// Exact match against a registered URI; for loopback redirects the port may differ
    /// (RFC 8252 §7.3). The result must ALSO pass today's allow-list — a client registered
    /// before a host was removed from <c>Mcp:AllowedRedirectHosts</c> gets nothing.
    /// </summary>
    private string? MatchRedirectUri(McpClient client, string? requested)
    {
        string? match = null;
        if (string.IsNullOrEmpty(requested)) match = client.RedirectUris.Count == 1 ? client.RedirectUris[0] : null;
        else
            foreach (var registered in client.RedirectUris)
            {
                if (string.Equals(registered, requested, StringComparison.Ordinal)) { match = requested; break; }
                if (Uri.TryCreate(registered, UriKind.Absolute, out var r) && Uri.TryCreate(requested, UriKind.Absolute, out var q)
                    && r.Scheme == Uri.UriSchemeHttp && q.Scheme == Uri.UriSchemeHttp && McpOptions.IsLoopback(r)
                    && r.IdnHost == q.IdnHost && r.AbsolutePath == q.AbsolutePath && r.Query == q.Query && q.Fragment.Length == 0)
                { match = requested; break; }
            }
        return match is not null && options.IsAllowedRedirectUri(match) ? match : null;
    }

    private static bool SameResource(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    // =====================================================================
    //  Helpers
    // =====================================================================

    private ITimeLimitedDataProtector Protector =>
        dataProtection.CreateProtector("SchoolLms.Mcp.Authorize.v1").ToTimeLimitedDataProtector();

    private string Protect(AuthRequest req, TimeSpan lifetime) =>
        Protector.Protect(JsonSerializer.Serialize(req), lifetime);

    private AuthRequest? Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return JsonSerializer.Deserialize<AuthRequest>(Protector.Unprotect(value)); }
        catch { return null; }
    }

    private IActionResult RedirectError(string redirectUri, string error, string description, string? state)
    {
        // Never bounce an error to a host that is not on the allow-list — show it here instead.
        if (!options.IsAllowedRedirectUri(redirectUri))
            return Page("Xato", ErrorBox("So'rov noto'g'ri: " + description), 400);
        var query = new Dictionary<string, string?>
        {
            ["error"] = error, ["error_description"] = description, ["iss"] = McpUrls.BaseUrl(Request),
        };
        if (state is not null) query["state"] = state;
        return Redirect(QueryHelpers.AddQueryString(redirectUri, query));
    }

    private IActionResult TokenResponse(McpOAuthStore.IssuedTokens t) => OAuthJson(new Dictionary<string, object>
    {
        ["access_token"] = t.AccessToken,
        ["token_type"] = "Bearer",
        ["expires_in"] = t.ExpiresIn,
        ["refresh_token"] = t.RefreshToken,
        ["scope"] = t.Scope,
    });

    private IActionResult OAuthError(string error, string description, int status = 400) =>
        OAuthJson(new Dictionary<string, object> { ["error"] = error, ["error_description"] = description }, status);

    private IActionResult RegistrationError(string error, string description) => OAuthError(error, description);

    private ContentResult OAuthJson(object payload, int status = 200)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return new ContentResult
        {
            StatusCode = status,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(payload, Json),
        };
    }

    // =====================================================================
    //  HTML — our own brand (yellow #FFD006 on white, black text), Uzbek
    // =====================================================================

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private IActionResult LoginPage(McpClient client, string redirectUri, string req, string? error) => Page("Kirish", $"""
        <p class="lead">«{H(client.Name)}» maktab ma'lumotlarini <b>faqat o'qish uchun</b> so'rayapti.
        Davom etish uchun Wunderkind tizimidagi login va parolingizni kiriting.</p>
        {(error is null ? "" : ErrorBox(error))}
        <form method="post" action="/oauth/authorize" autocomplete="on">
          <input type="hidden" name="step" value="login">
          <input type="hidden" name="req" value="{H(req)}">
          <label>Login<input name="login" autocomplete="username" required autofocus></label>
          <label>Parol<input name="password" type="password" autocomplete="current-password" required></label>
          <button type="submit" class="primary">Kirish</button>
        </form>
        <p class="note">Parolingiz AI ilovasiga berilmaydi — u faqat shu sahifada, maktab serverida tekshiriladi.</p>
        """);

    private IActionResult ConsentPage(McpClient client, string redirectUri, string userName, string req) => Page("Ruxsat", $"""
        <p class="lead"><b>{H(userName)}</b>, «{H(client.Name)}» maktab ma'lumotlarini <b>faqat o'qish uchun</b> so'rayapti.</p>
        <div class="dest">Ruxsat bersangiz, kalit <b>{H(McpOptions.DestinationLabel(redirectUri))}</b> ga yuboriladi.
          <span>Ilova ID: {H(client.Id)}</span></div>
        <ul class="facts">
          <li>Faqat o'qiydi — hech narsani o'zgartirmaydi, o'chirmaydi va hech kimga xabar yubormaydi.</li>
          <li>Faqat sizga ruxsat berilgan bo'limlarni ko'radi.</li>
          <li>Har bir so'rov jurnalga yoziladi; ulanishni istalgan payt «AI ulanishlar» sahifasida bekor qilish mumkin.</li>
        </ul>
        <form method="post" action="/oauth/authorize" class="row">
          <input type="hidden" name="step" value="consent">
          <input type="hidden" name="req" value="{H(req)}">
          <button type="submit" name="decision" value="deny" class="secondary">Rad etish</button>
          <button type="submit" name="decision" value="allow" class="primary">Ruxsat berish</button>
        </form>
        """);

    private static string ErrorBox(string message) => $"""<div class="error">{H(message)}</div>""";

    private ContentResult Page(string title, string body, int status = 200)
    {
        // Own CSP: no scripts at all, no framing (clickjacking on a login page), no form-action
        // restriction — the consent POST must be allowed to redirect to the AI client.
        Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Frame-Options"] = "DENY";
        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="uz"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>{{H(title)}} — Wunderkind AI ulanish</title>
            <style>
              *{box-sizing:border-box}
              body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;
                   background:#f5f5f7;color:#111;font:15px/1.5 -apple-system,BlinkMacSystemFont,"SF Pro Text","Segoe UI",Roboto,sans-serif;padding:16px}
              .card{width:100%;max-width:400px;background:#fff;border-radius:20px;padding:28px 24px;
                    box-shadow:0 1px 2px rgba(0,0,0,.04),0 8px 30px rgba(0,0,0,.06)}
              .brand{display:flex;align-items:center;gap:10px;margin-bottom:18px}
              .logo{width:36px;height:36px;border-radius:10px;background:#FFD006;display:flex;align-items:center;
                    justify-content:center;font-weight:700;font-size:18px}
              h1{font-size:19px;margin:0}
              .sub{font-size:12px;color:#6e6e73}
              .lead{margin:0 0 16px}
              label{display:block;font-size:13px;color:#3a3a3c;margin-bottom:12px}
              input{display:block;width:100%;margin-top:6px;padding:11px 12px;border:1px solid #d2d2d7;border-radius:12px;font:inherit;background:#fff}
              input:focus{outline:none;border-color:#111;box-shadow:0 0 0 3px rgba(255,208,6,.45)}
              button{font:inherit;font-weight:600;border:0;border-radius:12px;padding:12px 16px;cursor:pointer;width:100%}
              .primary{background:#FFD006;color:#111}
              .primary:hover{filter:brightness(.96)}
              .secondary{background:#f2f2f7;color:#111}
              .row{display:flex;gap:10px;margin-top:8px}
              .error{background:#fff1f0;color:#b42318;border-radius:12px;padding:10px 12px;margin:0 0 14px;font-size:14px}
              .note{font-size:12px;color:#6e6e73;margin:14px 0 0}
              .facts{padding-left:18px;margin:0 0 16px;color:#3a3a3c;font-size:14px}
              .facts li{margin-bottom:6px}
              .dest{background:#fffbe6;border:1px solid #ffe58a;border-radius:12px;padding:10px 12px;margin:0 0 14px;font-size:14px}
              .dest span{display:block;font-size:11px;color:#6e6e73;margin-top:4px;word-break:break-all}
            </style></head><body><main class="card">
            <div class="brand"><div class="logo">W</div><div><h1>Wunderkind</h1><div class="sub">AI ulanish · faqat o'qish</div></div></div>
            """);
        html.Append(body);
        html.Append("</main></body></html>");
        return new ContentResult { StatusCode = status, ContentType = "text/html; charset=utf-8", Content = html.ToString() };
    }
}
