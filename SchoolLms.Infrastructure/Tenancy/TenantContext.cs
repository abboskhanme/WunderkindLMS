using SchoolLms.Application.Abstractions;

namespace SchoolLms.Infrastructure.Tenancy;

/// <summary>Scoped <see cref="ITenantContext"/> — har so'rovda middleware to'ldiradi.</summary>
public class TenantContext : ITenantContext
{
    public bool IsPlatform { get; private set; }
    public string? Slug { get; private set; }
    public string? TenantId { get; private set; }

    public void SetTenant(string tenantId, string slug)
    {
        IsPlatform = false;
        TenantId = tenantId;
        Slug = slug;
    }

    public void SetPlatform()
    {
        IsPlatform = true;
        Slug = null;
        TenantId = null;
    }
}
