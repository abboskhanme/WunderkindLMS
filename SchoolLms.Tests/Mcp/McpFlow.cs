using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Mcp;

/// <summary>
/// Drives the MCP OAuth flow and JSON-RPC calls exactly like a standard MCP client would.
/// Every flow gets its own fake client IP (X-Forwarded-For) so the per-IP login / register
/// rate limits of one test never starve another (TestServer has no real IP).
/// </summary>
public sealed partial class McpFlow(ApiFactory api)
{
    public const string RedirectUri = "http://127.0.0.1:53123/callback";

    public string Ip { get; } = $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    public HttpClient Client()
    {
        var c = api.AnonymousClient();
        c.DefaultRequestHeaders.Add("X-Forwarded-For", Ip);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("schoollms-tests/1.0");
        return c;
    }

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = B64(RandomNumberGenerator.GetBytes(32));
        return (verifier, B64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task<string> RegisterAsync(string name = "Test AI", string redirect = RedirectUri)
    {
        using var c = Client();
        var res = await c.PostAsJsonAsync("/oauth/register", new
        {
            client_name = name,
            redirect_uris = new[] { redirect },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("client_id").GetString()!;
    }

    public static string AuthorizeUrl(string clientId, string challenge, string redirect = RedirectUri,
        string method = "S256", string state = "st-1") =>
        QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = redirect,
            ["code_challenge"] = challenge, ["code_challenge_method"] = method, ["state"] = state,
            ["scope"] = McpAccess.Scope, ["resource"] = "https://localhost/mcp",
        });

    [GeneratedRegex("name=\"req\" value=\"([^\"]+)\"")]
    private static partial Regex ReqField();

    public static string ReqOf(string html)
    {
        var m = ReqField().Match(html);
        Assert.True(m.Success, "hidden `req` field not found in: " + html[..Math.Min(400, html.Length)]);
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    /// <summary>GET authorize → login POST. Returns the response of the login POST.</summary>
    public async Task<HttpResponseMessage> LoginAsync(string clientId, string challenge, string login, string password)
    {
        using var c = Client();
        var page = await c.GetAsync(AuthorizeUrl(clientId, challenge));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var req = ReqOf(await page.Content.ReadAsStringAsync());
        return await c.PostAsync("/oauth/authorize", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["step"] = "login", ["req"] = req, ["login"] = login, ["password"] = password,
        }));
    }

    /// <summary>Full browser part: login + consent (allow). Returns the authorization code.</summary>
    public async Task<string> CodeAsync(string clientId, string challenge, string login, string password, string decision = "allow")
    {
        var loginRes = await LoginAsync(clientId, challenge, login, password);
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var consentReq = ReqOf(await loginRes.Content.ReadAsStringAsync());
        using var c = Client();
        var res = await c.PostAsync("/oauth/authorize", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["step"] = "consent", ["req"] = consentReq, ["decision"] = decision,
        }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location!.ToString();
        Assert.StartsWith(RedirectUri, location);
        var query = QueryHelpers.ParseQuery(new Uri(location).Query);
        Assert.Equal("st-1", query["state"].ToString());
        return decision == "allow" ? query["code"].ToString() : query["error"].ToString();
    }

    public async Task<HttpResponseMessage> TokenAsync(Dictionary<string, string> form)
    {
        using var c = Client();
        return await c.PostAsync("/oauth/token", new FormUrlEncodedContent(form));
    }

    public sealed record Tokens(string Access, string Refresh, string ClientId);

    /// <summary>Whole flow for a user: register → authorize → token.</summary>
    public async Task<Tokens> ConnectAsync(string login, string password)
    {
        var clientId = await RegisterAsync();
        var (verifier, challenge) = Pkce();
        var code = await CodeAsync(clientId, challenge, login, password);
        var res = await TokenAsync(new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId, ["code_verifier"] = verifier,
        });
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, body);
        using var doc = JsonDocument.Parse(body);
        return new Tokens(doc.RootElement.GetProperty("access_token").GetString()!,
            doc.RootElement.GetProperty("refresh_token").GetString()!, clientId);
    }

    /// <summary>Seed a user of <paramref name="role"/> and connect it.</summary>
    public async Task<(AppUser User, Tokens Tokens)> ConnectNewAsync(string role, params string[] perms)
    {
        var (user, password) = await api.SeedUserAsync(role, permissions: perms);
        return (user, await ConnectAsync(user.Email, password));
    }

    // -----------------------------------------------------------------
    //  JSON-RPC over Streamable HTTP
    // -----------------------------------------------------------------

    private int _id;

    public async Task<HttpResponseMessage> PostMcpAsync(string? access, object message)
    {
        var c = Client();
        if (access is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        req.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        return await c.SendAsync(req);
    }

    /// <summary>One JSON-RPC request; returns the <c>result</c> (or throws with the error).</summary>
    public async Task<JsonNode> RpcAsync(string access, string method, object? @params = null)
    {
        var res = await PostMcpAsync(access, new { jsonrpc = "2.0", id = ++_id, method, @params = @params ?? new { } });
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{method}: {(int)res.StatusCode} {text}");
        var json = ExtractJson(text);
        var node = JsonNode.Parse(json)!;
        if (node["error"] is { } err) throw new Xunit.Sdk.XunitException($"{method} JSON-RPC error: {err.ToJsonString()}");
        return node["result"]!;
    }

    /// <summary>The response may be plain JSON or an SSE stream (`data: {...}`).</summary>
    public static string ExtractJson(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{')) return trimmed;
        var data = body.Split('\n').Where(l => l.StartsWith("data:")).Select(l => l[5..].Trim())
            .LastOrDefault(l => l.StartsWith('{'));
        return data ?? throw new Xunit.Sdk.XunitException("No JSON in response: " + body);
    }

    public Task<JsonNode> InitializeAsync(string access) => RpcAsync(access, "initialize", new
    {
        protocolVersion = "2025-06-18",
        capabilities = new { },
        clientInfo = new { name = "schoollms-tests", version = "1.0" },
    });

    /// <summary>tools/call → (isError, text).</summary>
    public async Task<(bool IsError, string Text)> CallAsync(string access, string tool, object? args = null)
    {
        var result = await RpcAsync(access, "tools/call", new { name = tool, arguments = args ?? new { } });
        var isError = result["isError"]?.GetValue<bool>() ?? false;
        var text = string.Concat(result["content"]!.AsArray().Select(c => c?["text"]?.GetValue<string>() ?? ""));
        return (isError, text);
    }
}
