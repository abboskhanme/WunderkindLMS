namespace SchoolLms.Domain;

/// <summary>
/// Read-only MCP server for AI clients (docs/modules/mcp-readonly.md, 2026-09-26).
///
/// <para>
/// Who may connect: <see cref="Roles.SuperAdmin"/>, <see cref="Roles.Admin"/>, and a
/// <see cref="Roles.Staff"/> member whose access role carries <see cref="PermissionKey"/>.
/// Nobody else — teachers, cashiers, pupils and parents never get a token.
/// </para>
/// </summary>
public static class McpAccess
{
    /// <summary>Permission key a staff access role carries to allow AI connections.</summary>
    public const string PermissionKey = "aiAccess";

    /// <summary>The only OAuth scope the server issues.</summary>
    public const string Scope = "school.read";

    /// <summary>Is this account allowed to hold an MCP token right now?</summary>
    public static bool IsAllowed(AppUser user) =>
        user.Role is Roles.SuperAdmin or Roles.Admin
        || (user.Role == Roles.Staff && user.Permissions.Contains(PermissionKey, StringComparer.Ordinal));
}

/// <summary>
/// An OAuth client registered through dynamic client registration (RFC 7591).
/// Public clients only — no client secret is issued; PKCE (S256) protects the code.
/// </summary>
public class McpClient
{
    /// <summary>Opaque public identifier, e.g. <c>mcp_…</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Exact redirect URIs the client registered (https, or http on a loopback host).</summary>
    public List<string> RedirectUris { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One "connection": a user authorised a client. Every token hangs off a grant, so
/// revoking the grant kills its access and refresh tokens at once.
/// </summary>
public class McpGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ClientId { get; set; } = string.Empty;

    /// <summary>users.id — no FK on purpose: a deleted user simply stops authenticating.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Scope { get; set; } = McpAccess.Scope;

    /// <summary>RFC 8707 resource the tokens are bound to (our <c>/mcp</c> URL).</summary>
    public string Resource { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Who revoked (users.id), or <c>system:…</c> for refresh-token reuse detection.</summary>
    public string? RevokedBy { get; set; }
}

/// <summary>One-time authorization code. Only its SHA-256 hash is stored.</summary>
public class McpAuthCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CodeHash { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>PKCE S256 challenge (base64url of SHA-256 of the verifier).</summary>
    public string CodeChallenge { get; set; } = string.Empty;

    public string Scope { get; set; } = McpAccess.Scope;

    public string Resource { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Grant created when the code was exchanged — revoked if the code is replayed.</summary>
    public Guid? GrantId { get; set; }
}

/// <summary>Access or refresh token. Only its SHA-256 hash is stored.</summary>
public class McpToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GrantId { get; set; }

    /// <summary><see cref="McpTokenKind"/>.</summary>
    public string Kind { get; set; } = McpTokenKind.Access;

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Refresh token already exchanged (rotation). Reuse revokes the grant.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}

public static class McpTokenKind
{
    public const string Access = "access";
    public const string Refresh = "refresh";
}

/// <summary>
/// Audit row: one MCP tool call. Append-only for <c>app_rw</c> (no UPDATE/DELETE).
/// User and client names are snapshots, so a later rename or deletion keeps the log readable.
/// </summary>
public class McpAuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public Guid? GrantId { get; set; }

    public string Tool { get; set; } = string.Empty;

    /// <summary>Short, truncated argument summary (never secrets — tools take none).</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary><c>ok</c> | <c>denied</c> | <c>error</c>.</summary>
    public string Outcome { get; set; } = "ok";

    public int? RowCount { get; set; }

    public int DurationMs { get; set; }

    /// <summary>Client IP (after the trusted proxy), for incident review.</summary>
    public string? Ip { get; set; }

    /// <summary>User-Agent of the calling AI client (truncated).</summary>
    public string? UserAgent { get; set; }
}
