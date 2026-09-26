using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Mcp;

/// <summary>Public URLs of the MCP server / OAuth authorization server.</summary>
public static class McpUrls
{
    /// <summary>
    /// <c>Mcp:PublicBaseUrl</c> when configured (recommended in production so the issuer never
    /// depends on a Host header), otherwise scheme+host of the request (behind Caddy the
    /// forwarded-headers middleware already restored <c>https</c>).
    /// </summary>
    public static string BaseUrl(HttpRequest request)
    {
        var configured = request.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Mcp:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');
        return $"{request.Scheme}://{request.Host}";
    }

    public static string Resource(HttpRequest r) => BaseUrl(r) + "/mcp";
    public static string ResourceMetadata(HttpRequest r) => BaseUrl(r) + "/.well-known/oauth-protected-resource/mcp";
}

/// <summary>Opaque secrets: generation, hashing, PKCE.</summary>
public static class McpSecrets
{
    public const string AccessPrefix = "wkm_at_";
    public const string RefreshPrefix = "wkm_rt_";
    public const string CodePrefix = "wkm_ac_";

    public static string New(string prefix) =>
        prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Lower-case hex SHA-256 — what the database stores instead of the secret.</summary>
    public static string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>RFC 7636 §4.1: 43–128 chars of [A-Z a-z 0-9 - . _ ~].</summary>
    public static bool IsValidVerifier(string? v) =>
        v is { Length: >= 43 and <= 128 }
        && v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

    /// <summary>S256 challenge check in constant time.</summary>
    public static bool VerifyPkce(string verifier, string challenge)
    {
        var computed = Encoding.ASCII.GetBytes(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        var expected = Encoding.ASCII.GetBytes(challenge);
        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    /// <summary>A challenge is base64url(SHA-256) = exactly 43 chars.</summary>
    public static bool IsValidChallenge(string? c) =>
        c is { Length: 43 } && c.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
}

/// <summary>
/// Token issuing and lookup for the OAuth flow. Runs on the READ/WRITE app context —
/// this is the only MCP code that writes, and it writes only mcp_* tables.
/// </summary>
public sealed class McpOAuthStore(AppDbContext db)
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    public sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresIn, string Scope);

    /// <summary>New access + refresh pair under <paramref name="grant"/>. Caller saves.</summary>
    public IssuedTokens Issue(McpGrant grant)
    {
        var now = DateTimeOffset.UtcNow;
        var access = McpSecrets.New(McpSecrets.AccessPrefix);
        var refresh = McpSecrets.New(McpSecrets.RefreshPrefix);
        db.McpTokens.Add(new McpToken
        {
            GrantId = grant.Id, Kind = McpTokenKind.Access, TokenHash = McpSecrets.Hash(access),
            CreatedAt = now, ExpiresAt = now + AccessLifetime,
        });
        db.McpTokens.Add(new McpToken
        {
            GrantId = grant.Id, Kind = McpTokenKind.Refresh, TokenHash = McpSecrets.Hash(refresh),
            CreatedAt = now, ExpiresAt = now + RefreshLifetime,
        });
        return new IssuedTokens(access, refresh, (int)AccessLifetime.TotalSeconds, grant.Scope);
    }

    /// <summary>Revoke a grant (and so every token under it).</summary>
    public async Task RevokeGrantAsync(Guid grantId, string by, CancellationToken ct = default)
    {
        var grant = await db.McpGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        if (grant is null || grant.RevokedAt is not null) return;
        grant.RevokedAt = DateTimeOffset.UtcNow;
        grant.RevokedBy = by;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Housekeeping: expired codes/tokens (> 1 day) and clients that never got a grant (> 24 h).</summary>
    public async Task PruneAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        await db.McpAuthCodes.Where(c => c.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
        await db.McpTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
        // Registered but never connected (no grant) within 24 h — abandoned or abusive.
        await db.McpClients.Where(c => c.CreatedAt < cutoff && !db.McpGrants.Any(g => g.ClientId == c.Id))
            .ExecuteDeleteAsync(ct);
    }
}
