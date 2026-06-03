namespace SchoolLms.Application.Abstractions;

/// <summary>
/// Joriy so'rov uchun aniqlangan tenant (maktab). Scoped — har so'rovda
/// <c>TenantResolutionMiddleware</c> to'ldiradi. <see cref="TenantId"/> shared-DB'dagi global
/// query filter qaysi maktab qatorlarini ko'rsatishini belgilaydi.
///
/// <para>Asosiy domen (Control Plane) so'rovlarida <see cref="IsPlatform"/> = true va
/// <see cref="TenantId"/> = null bo'ladi.</para>
/// </summary>
public interface ITenantContext
{
    /// <summary>Asosiy domen (loyiha boshlig'i paneli) so'rovimi?</summary>
    bool IsPlatform { get; }
    /// <summary>Maktab subdomeni (slug).</summary>
    string? Slug { get; }
    /// <summary>Tenant (maktab) Id — qatorlardagi TenantId qiymati. null bo'lsa filtr hech narsa ko'rsatmaydi.</summary>
    string? TenantId { get; }

    /// <summary>Middleware (yoki fon xizmati) tenantni aniqlagach chaqiradi.</summary>
    void SetTenant(string tenantId, string slug);
    /// <summary>Asosiy domen (Control Plane) so'rovi sifatida belgilaydi.</summary>
    void SetPlatform();
}
