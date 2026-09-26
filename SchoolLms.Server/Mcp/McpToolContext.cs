using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using ModelContextProtocol;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;

namespace SchoolLms.Server.Mcp;

/// <summary>Thrown when the signed-in user lacks the area permission. Audited as <c>denied</c>.</summary>
public sealed class McpAccessDeniedException(string message) : McpException(message);

/// <summary>
/// Everything an MCP tool may touch: the signed-in user, the READ-ONLY database context,
/// permission checks, caps and the output serializer. Tools get nothing else — in
/// particular never the request's read/write <see cref="AppDbContext"/> from DI: <see cref="Db"/>
/// is a context opened on the <c>app_ro</c> connection (<see cref="ReadOnlyDatabase"/>).
/// A reflection test enforces that tools depend on this class only.
/// </summary>
public sealed class McpToolContext(IHttpContextAccessor http, ReadOnlyDatabase database, IMemoryCache cache)
    : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Short server-side cache for whole-school aggregates that are expensive to compute
    /// (e.g. the rating over every journal entry). The key must not depend on the user —
    /// the permission check runs BEFORE the cached value is used.
    /// </summary>
    public async Task<T> CachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (cache.TryGetValue("mcp:" + key, out T? hit) && hit is not null) return hit;
        var value = await factory();
        cache.Set("mcp:" + key, value, ttl);
        return value;
    }

    /// <summary>Largest page any list tool returns.</summary>
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 50;

    /// <summary>Longest date range a range tool accepts.</summary>
    public const int MaxRangeDays = 366;

    /// <summary>Per-call state shared with the audit filter (kept on the HttpContext).</summary>
    public sealed class CallState
    {
        public bool Denied { get; set; }
        public int? RowCount { get; set; }
    }

    private const string StateKey = "SchoolLms.Mcp.CallState";

    public static CallState StateOf(HttpContext ctx)
    {
        if (ctx.Items[StateKey] is CallState s) return s;
        s = new CallState();
        ctx.Items[StateKey] = s;
        return s;
    }

    private AppDbContext? _db;

    /// <summary>Read-only context (app_ro, no tracking, SaveChanges refused). Opened lazily.</summary>
    public AppDbContext Db => _db ??= database.CreateContext();

    public void Dispose() => _db?.Dispose();

    public ValueTask DisposeAsync() => _db?.DisposeAsync() ?? ValueTask.CompletedTask;

    public ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    private CallState State => http.HttpContext is { } c ? StateOf(c) : new CallState();

    public bool IsFullAccess => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin);

    /// <summary>Full or view-only permission for a panel section (admin/superadmin: always).</summary>
    public bool CanRead(string section) => User.HasReadPerm(section);

    /// <summary>Finance data: <c>finance</c> or <c>finance:view</c> (admin/superadmin: always).</summary>
    public bool CanReadFinance => CanRead(PermissionCheck.Finance);

    /// <summary>Refuse unless the user may read the area (see <see cref="CanReadArea"/>).</summary>
    public void Require(McpArea area)
    {
        if (CanReadArea(area)) return;
        State.Denied = true;
        throw new McpAccessDeniedException(
            $"Ruxsat yo'q: bu ma'lumot «{area.Title}» sahifasiga tegishli, sizning rolingizda u ochilmagan.");
    }

    /// <summary>
    /// The same rule the web panel applies (schoollms.client/src/lib/access.ts):
    /// <list type="bullet">
    ///   <item>admin / superadmin — everything;</item>
    ///   <item>staff whose role carries PAGE grants (<c>/admin/…|view</c> or <c>|edit</c>) — at least one
    ///     of the area's pages must be granted (a grant for a retired page opens its successor);</item>
    ///   <item>staff with a legacy role (section keys only) — one of the area's section keys, full or <c>:view</c>.</item>
    /// </list>
    /// </summary>
    public bool CanReadArea(McpArea area)
    {
        if (IsFullAccess) return true;
        if (!User.IsInRole(Roles.Staff)) return false;
        var grants = PageGrants(User);
        if (grants.Count > 0) return area.Pages.Any(grants.Contains);
        return area.Sections.Any(CanRead);
    }

    /// <summary>Pages granted at view or edit level, from the user's <c>perm</c> claims.</summary>
    public static HashSet<string> PageGrants(ClaimsPrincipal user)
    {
        var pages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in user.Claims.Where(c => c.Type == AdminPermAttribute.ClaimType && c.Value.StartsWith('/')))
        {
            var i = c.Value.LastIndexOf('|');
            if (i < 0) continue;
            var level = c.Value[(i + 1)..];
            if (level is not ("view" or "edit")) continue;
            var page = c.Value[..i];
            pages.Add(page);
            if (RetiredPages.TryGetValue(page, out var successor)) pages.Add(successor);
        }
        return pages;
    }

    /// <summary>Mirror of <c>RETIRED_PAGES</c> in lib/access.ts.</summary>
    private static readonly Dictionary<string, string> RetiredPages = new(StringComparer.Ordinal)
    {
        ["/admin/teachers"] = "/admin/boshqaruv/staff",
    };

    public static int Page(int? page) => Math.Max(1, page ?? 1);

    public static int PageSize(int? size) => Math.Clamp(size ?? DefaultPageSize, 1, MaxPageSize);

    public static DateOnly Today => DateOnly.FromDateTime(AppClock.Now);

    /// <summary>Parse <c>YYYY-MM-DD</c>; null/empty → <paramref name="fallback"/>.</summary>
    public static DateOnly Date(string? value, DateOnly fallback, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", out var d)) return d;
        throw new McpException($"«{name}» sanasi YYYY-MM-DD ko'rinishida bo'lsin (masalan 2026-09-01).");
    }

    /// <summary>Validated range, at most <paramref name="maxDays"/> days.</summary>
    public static (DateOnly From, DateOnly To) Range(string? from, string? to, int defaultDays = 30, int maxDays = MaxRangeDays)
    {
        var t = Date(to, Today, "to");
        var f = Date(from, t.AddDays(-(defaultDays - 1)), "from");
        if (f > t) throw new McpException("«from» sanasi «to» dan keyin bo'lishi mumkin emas.");
        if (t.DayNumber - f.DayNumber + 1 > maxDays)
            throw new McpException($"Sana oralig'i ko'pi bilan {maxDays} kun bo'lishi mumkin.");
        return (f, t);
    }

    // ---------------------------------------------------------------------
    //  Output
    // ---------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Uzbek letters readable
        WriteIndented = false,
    };

    /// <summary>
    /// Serialize a tool result. <paramref name="rows"/> goes to the audit row count.
    /// Defence in depth: every property whose NAME looks like a credential (password, hash,
    /// token, secret, login, …) is removed from the output, however it got there.
    /// </summary>
    public string Json(object payload, int? rows = null)
    {
        State.RowCount = rows;
        var node = JsonSerializer.SerializeToNode(payload, JsonOptions);
        Scrub(node);
        return node?.ToJsonString(JsonOptions) ?? "null";
    }

    /// <summary>Property names that must never leave the server (case-insensitive).</summary>
    public static bool IsSecretName(string name)
    {
        var n = name.ToLowerInvariant().Replace("_", "").Replace("-", "");
        return n is "pin" or "username" or "otp"
            || n.Contains("login") || n.Contains("email")
            || n.Contains("password") || n.Contains("passwd") || n.Contains("hash") || n.Contains("salt")
            || n.Contains("token") || n.Contains("secret") || n.Contains("apikey") || n.Contains("privatekey")
            || n.Contains("credential") || n.Contains("passport") || n.Contains("pinfl") || n.Contains("jshshir")
            || n.Contains("fcm") || n.Contains("vapid") || n.StartsWith("otp") || n.EndsWith("otp")
            || n.Contains("sessionid") || n.Contains("sessionkey") || n.Contains("sessioncookie");
    }

    private static void Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSecretName(key)) obj.Remove(key);
                    else Scrub(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) Scrub(item);
                break;
        }
    }
}
