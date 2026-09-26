using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading.RateLimiting;
using Npgsql;

namespace SchoolLms.Server.Mcp;

/// <summary>MCP settings (section <c>Mcp</c>).</summary>
public sealed class McpOptions
{
    /// <summary>Hosts an OAuth client may send the user back to (https). Loopback http is always allowed.</summary>
    public static readonly string[] DefaultRedirectHosts = ["claude.ai", "claude.com", "chatgpt.com", "chat.openai.com"];

    /// <summary><c>Mcp:PublicBaseUrl</c>, e.g. <c>https://lms.wunderkindedu.uz</c>. Required outside Development.</summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary><c>Mcp:AllowedRedirectHosts</c> — comma-separated or an array; IDN (punycode) form.</summary>
    public IReadOnlySet<string> AllowedRedirectHosts { get; init; } = new HashSet<string>(DefaultRedirectHosts);

    public static McpOptions From(IConfiguration config)
    {
        var section = config.GetSection("Mcp:AllowedRedirectHosts");
        var hosts = section.GetChildren().Select(c => c.Value).ToList();
        if (hosts.Count == 0 && !string.IsNullOrWhiteSpace(section.Value)) hosts = [.. section.Value.Split(',')];
        var set = hosts.Select(h => (h ?? "").Trim().ToLowerInvariant()).Where(h => h.Length > 0).ToHashSet(StringComparer.Ordinal);
        return new McpOptions
        {
            PublicBaseUrl = config["Mcp:PublicBaseUrl"]?.Trim().TrimEnd('/'),
            AllowedRedirectHosts = set.Count > 0 ? set : new HashSet<string>(DefaultRedirectHosts, StringComparer.Ordinal),
        };
    }

    public static bool IsLoopback(Uri uri) => uri.IdnHost is "localhost" or "127.0.0.1" or "[::1]" or "::1";

    /// <summary>
    /// A redirect URI we will ever send a code (or an error) to:
    /// <list type="bullet">
    ///   <item>exact canonical form (raw string == <see cref="Uri.AbsoluteUri"/>) — no case/encoding tricks;</item>
    ///   <item>https to an allow-listed host (compared on <see cref="Uri.IdnHost"/>), or http on loopback;</item>
    ///   <item>no fragment, no user-info, at most 512 characters.</item>
    /// </list>
    /// Custom schemes (cursor://, vscode://) are refused.
    /// </summary>
    public bool IsAllowedRedirectUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > McpLimits.MaxUriLength) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(value, uri.AbsoluteUri, StringComparison.Ordinal)) return false;
        if (uri.Fragment.Length > 0 || uri.UserInfo.Length > 0) return false;
        if (uri.Scheme == Uri.UriSchemeHttp) return IsLoopback(uri);
        return uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && AllowedRedirectHosts.Contains(uri.IdnHost.ToLowerInvariant());
    }

    /// <summary>What the consent screen tells the user about where the key goes.</summary>
    public static string DestinationLabel(string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var u)
            ? IsLoopback(u) ? "kompyuteringizdagi dastur" : u.IdnHost
            : "?";
}

public static class McpLimits
{
    public const int MaxRedirectUris = 5;
    public const int MaxUriLength = 512;
    public const int MaxClientName = 60;
    public const int MaxState = 1000;

    /// <summary>Global dynamic-registration cap per 24 hours.</summary>
    public const int MaxRegistrationsPerDay = 50;

    /// <summary>Absolute lifetime of a connection — refresh is refused after this.</summary>
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromDays(90);

    /// <summary>Failed OAuth logins per login name before a cool-down.</summary>
    public const int MaxFailuresPerLogin = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    /// <summary>Concurrent /mcp calls: per connection and process-wide.</summary>
    public const int ConcurrentPerGrant = 2;
    public const int ConcurrentGlobal = 6;

    /// <summary>Per-IP (/64) request budget on /mcp and /oauth before authentication.</summary>
    public const int PreAuthPerMinute = 300;
}

/// <summary>Network helpers.</summary>
public static class McpNet
{
    /// <summary>
    /// Rate-limit key — same rule as Program.cs <c>PublicFormPartition</c>: IPv6 by /64 (one
    /// subscriber gets a whole /64, so per-address buckets are trivially evaded), IPv4 as is.
    /// </summary>
    public static string Partition(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.AddressFamily == AddressFamily.InterNetworkV6
            ? Convert.ToHexString(ip.GetAddressBytes(), 0, 8) + "/64"
            : ip.ToString();
    }
}

/// <summary>Display-safe text from untrusted clients.</summary>
public static class McpText
{
    /// <summary>
    /// Strip control/format characters (bidi overrides, zero-width joiners, newlines — the
    /// raw material of "«Wunderkind official»"-style spoofing), collapse spaces, cap length.
    /// </summary>
    public static string Sanitize(string? value, int max)
    {
        var sb = new StringBuilder();
        foreach (var ch in (value ?? "").Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(ch)) { sb.Append(' '); continue; }
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned) continue;
            sb.Append(ch);
        }
        var s = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= max ? s : s[..max];
    }
}

/// <summary>
/// Hardening and a startup self-test of the read-only connection string.
/// </summary>
public static class McpReadOnlyConnection
{
    /// <summary>
    /// Pool of at most 8 (fits under the server's max_connections next to app_rw), 5 s connect
    /// timeout, 30 s command timeout, NO error detail (it can echo row data into logs), and
    /// every session starts read-only even if the role setting were lost.
    /// </summary>
    public static string Harden(string raw)
    {
        var b = new NpgsqlConnectionStringBuilder(raw)
        {
            IncludeErrorDetail = false,
            Timeout = Math.Min(new NpgsqlConnectionStringBuilder(raw).Timeout, 5),
            CommandTimeout = 30,
            ApplicationName = "SchoolLms.Mcp",
        };
        b.MaxPoolSize = Math.Min(b.MaxPoolSize, 8);
        if (b.MinPoolSize > b.MaxPoolSize) b.MinPoolSize = 0;
        const string ro = "-c default_transaction_read_only=on";
        if (!(b.Options ?? "").Contains("default_transaction_read_only", StringComparison.OrdinalIgnoreCase))
            b.Options = string.IsNullOrWhiteSpace(b.Options) ? ro : b.Options + " " + ro;
        return b.ConnectionString;
    }

    /// <summary>
    /// <c>null</c> = the role is what it claims: not superuser, not app_rw / the owner /
    /// the bootstrap user, not a member of a writing role, session read-only, no CREATE on the
    /// schema, and not a single non-SELECT privilege on any table or column. Otherwise the reason.
    /// Retries a few times — at container start the database may still be coming up.
    /// </summary>
    public static string? SelfTest(string connectionString)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();
                using var cmd = new NpgsqlCommand("""
                    SELECT current_user::text,
                           (SELECT rolsuper FROM pg_roles WHERE rolname = current_user),
                           current_setting('transaction_read_only'),
                           has_schema_privilege(current_user, 'public', 'CREATE'),
                           EXISTS (SELECT 1 FROM pg_roles r
                                    WHERE r.rolname IN ('app_rw', 'schoollms_owner', 'schoollms')
                                      AND r.rolname <> current_user
                                      AND pg_has_role(current_user, r.oid, 'MEMBER')),
                           (SELECT count(*) FROM information_schema.role_table_grants
                             WHERE grantee = current_user AND privilege_type <> 'SELECT'),
                           (SELECT count(*) FROM information_schema.column_privileges
                             WHERE grantee = current_user AND privilege_type <> 'SELECT'),
                           (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                             WHERE n.nspname = 'public' AND pg_get_userbyid(c.relowner) = current_user)
                    """, conn);
                using var r = cmd.ExecuteReader();
                r.Read();
                var user = r.GetString(0);
                if (user is "app_rw" or "schoollms_owner" or "schoollms" or "postgres") return $"connected as '{user}'";
                if (r.GetBoolean(1)) return "role is a superuser";
                if (r.GetString(2) != "on") return "session is not read-only";
                if (r.GetBoolean(3)) return "role may CREATE in schema public";
                if (!r.IsDBNull(4) && r.GetBoolean(4)) return "role is a member of a writing role";
                if (r.GetInt64(5) > 0) return "role holds non-SELECT table privileges";
                if (r.GetInt64(6) > 0) return "role holds non-SELECT column privileges";
                if (r.GetInt64(7) > 0) return "role owns objects";
                return null;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }
        }
        // Never echo the exception text: it can carry the connection string.
        return "cannot connect (" + last?.GetType().Name + ")";
    }
}

/// <summary>Request guards for /mcp and /oauth that live outside the endpoint pipeline.</summary>
public static class McpMiddleware
{
    private static readonly PartitionedRateLimiter<HttpContext> PreAuth =
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
            McpNet.Partition(ctx),
            _ => new FixedWindowRateLimiterOptions
            { PermitLimit = McpLimits.PreAuthPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    private static bool IsMcpPath(HttpContext ctx) =>
        ctx.Request.Path.StartsWithSegments("/mcp") || ctx.Request.Path.StartsWithSegments("/oauth");

    /// <summary>
    /// BEFORE authentication: a per-IP (/64) budget, so a flood of bogus bearer tokens cannot
    /// turn into a flood of token-hash lookups.
    /// </summary>
    public static IApplicationBuilder UseMcpPreAuthLimiter(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (!IsMcpPath(ctx)) { await next(); return; }
        using var lease = await PreAuth.AcquireAsync(ctx, 1, ctx.RequestAborted);
        if (!lease.IsAcquired)
        {
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        await next();
    });

    private static readonly SemaphoreSlim Global = new(McpLimits.ConcurrentGlobal, McpLimits.ConcurrentGlobal);
    private static readonly ConcurrentDictionary<string, int> PerGrant = new(StringComparer.Ordinal);

    /// <summary>
    /// AFTER authorization (the MCP principal is known): at most 2 concurrent calls per
    /// connection and 6 process-wide, no queue — an AI agent fanning out parallel heavy
    /// queries gets 429 instead of starving the database.
    /// </summary>
    public static IApplicationBuilder UseMcpConcurrency(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var grant = ctx.Request.Path.StartsWithSegments("/mcp") ? ctx.User.FindFirst(McpClaims.GrantId)?.Value : null;
        if (grant is null) { await next(); return; }

        if (PerGrant.AddOrUpdate(grant, 1, (_, n) => n + 1) > McpLimits.ConcurrentPerGrant)
        {
            Release(grant);
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        if (!Global.Wait(0))
        {
            Release(grant);
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        try { await next(); }
        finally
        {
            Global.Release();
            Release(grant);
        }
    });

    private static void Release(string grant)
    {
        if (PerGrant.AddOrUpdate(grant, 0, (_, n) => n - 1) <= 0)
            PerGrant.TryRemove(new KeyValuePair<string, int>(grant, 0));
    }
}
