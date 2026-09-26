using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;

namespace SchoolLms.Server.Mcp;

public sealed class McpTokenAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// Bearer authentication for <c>/mcp</c> with our opaque OAuth access tokens.
///
/// <para>
/// FRESHNESS — the same rule as the web panel (Program.cs <c>OnTokenValidated</c>): on
/// EVERY request the token hash is looked up, the grant must not be revoked, the user must
/// still exist and must still be allowed (<see cref="McpAccess.IsAllowed"/>: superadmin,
/// admin, or staff whose role carries <c>aiAccess</c>). Removing <c>aiAccess</c> from a role
/// or deleting the user therefore kills the connection on the next call — no token outlives
/// the permission. The staff member's section permissions are loaded from the DB as
/// <c>perm</c> claims (plus the derived finance roles), exactly like the web panel, and every
/// tool checks them.
/// </para>
/// <para>
/// A 401 carries <c>WWW-Authenticate: Bearer resource_metadata="…"</c> (RFC 9728) so a
/// standard MCP client discovers our authorization server and starts the OAuth flow.
/// </para>
/// </summary>
public sealed class McpTokenAuthenticationHandler(
    IOptionsMonitor<McpTokenAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<McpTokenAuthenticationOptions>(options, logger, encoder)
{
    private bool _tokenPresented;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = auth["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.NoResult();
        _tokenPresented = true;

        if (!token.StartsWith(McpSecrets.AccessPrefix, StringComparison.Ordinal) || token.Length > 200)
            return AuthenticateResult.Fail("invalid token");

        var hash = McpSecrets.Hash(token);
        var now = DateTimeOffset.UtcNow;

        var row = await (
            from t in db.McpTokens
            join g in db.McpGrants on t.GrantId equals g.Id
            join c in db.McpClients on g.ClientId equals c.Id
            where t.TokenHash == hash && t.Kind == McpTokenKind.Access
            select new { t.ExpiresAt, Grant = g, ClientName = c.Name }).FirstOrDefaultAsync(Context.RequestAborted);

        if (row is null || row.ExpiresAt <= now || row.Grant.RevokedAt is not null
            || row.Grant.CreatedAt + McpLimits.GrantLifetime <= now)
            return AuthenticateResult.Fail("invalid token");

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.Grant.UserId, Context.RequestAborted);
        if (user is null || !McpAccess.IsAllowed(user))
            return AuthenticateResult.Fail("user no longer allowed");

        // Last-used at most once a minute — one small UPDATE, not one per call.
        if (row.Grant.LastUsedAt is null || now - row.Grant.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            await db.McpGrants.Where(g => g.Id == row.Grant.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.LastUsedAt, now), Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        identity.AddClaim(new Claim("sub", user.Id));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.FullName));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        if (user.Role == Roles.Staff)
            foreach (var perm in user.Permissions)
            {
                identity.AddClaim(new Claim(AdminPermAttribute.ClaimType, perm));
                if (Roles.PermissionRoles.TryGetValue(perm, out var derived))
                    identity.AddClaim(new Claim(ClaimTypes.Role, derived));
            }
        identity.AddClaim(new Claim(McpClaims.GrantId, row.Grant.Id.ToString()));
        identity.AddClaim(new Claim(McpClaims.ClientId, row.Grant.ClientId));
        identity.AddClaim(new Claim(McpClaims.ClientName, row.ClientName));

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        var header = $"Bearer resource_metadata=\"{McpUrls.ResourceMetadata(Request)}\", scope=\"{McpAccess.Scope}\"";
        if (_tokenPresented) header += ", error=\"invalid_token\"";
        Response.Headers.WWWAuthenticate = header;
        return Task.CompletedTask;
    }
}
