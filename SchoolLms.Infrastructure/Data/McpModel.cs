using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Read-only MCP server: OAuth clients, grants, one-time codes, hashed tokens and the
/// tool-call audit (docs/modules/mcp-readonly.md, <c>McpReadOnly</c> migration).
/// Secrets are never stored in the clear — codes and tokens are SHA-256 hashes.
/// </summary>
internal static class McpModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<McpClient>(e =>
        {
            e.ToTable("mcp_clients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<McpGrant>(e =>
        {
            e.ToTable("mcp_grants");
            e.HasKey(x => x.Id);
            e.Property(x => x.ClientId).HasMaxLength(64);
            e.Property(x => x.Scope).HasMaxLength(100);
            e.Property(x => x.Resource).HasMaxLength(500);
            e.Property(x => x.RevokedBy).HasMaxLength(100);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ClientId);
            e.HasOne<McpClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<McpAuthCode>(e =>
        {
            e.ToTable("mcp_auth_codes");
            e.HasKey(x => x.Id);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.Property(x => x.ClientId).HasMaxLength(64);
            e.Property(x => x.RedirectUri).HasMaxLength(2000);
            e.Property(x => x.CodeChallenge).HasMaxLength(128);
            e.Property(x => x.Scope).HasMaxLength(100);
            e.Property(x => x.Resource).HasMaxLength(500);
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<McpClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<McpToken>(e =>
        {
            e.ToTable("mcp_tokens", t =>
                t.HasCheckConstraint("ck_mcp_tokens_kind", "kind in ('access','refresh')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.GrantId);
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<McpGrant>().WithMany().HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<McpAuditEntry>(e =>
        {
            e.ToTable("mcp_audit", t =>
                t.HasCheckConstraint("ck_mcp_audit_outcome", "outcome in ('ok','denied','error')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.UserName).HasMaxLength(200);
            e.Property(x => x.ClientId).HasMaxLength(64);
            e.Property(x => x.ClientName).HasMaxLength(200);
            e.Property(x => x.Tool).HasMaxLength(100);
            e.Property(x => x.Arguments).HasMaxLength(500);
            e.Property(x => x.Outcome).HasMaxLength(16);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.UserId, x.At });
        });
    }
}
