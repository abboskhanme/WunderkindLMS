using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Infrastructure.Tenancy;

/// <summary>Slug bo'yicha topilgan maktab haqida qisqa ma'lumot (obuna holati bilan).</summary>
public record TenantInfo(
    string Id, string Slug, string Status,
    DateTime? SubscriptionStartsAt, DateTime? SubscriptionEndsAt, IReadOnlyList<string> EnabledModules);

public interface ITenantStore
{
    /// <summary>Slug bo'yicha maktabni topadi (topilmasa null). Natija qisqa muddatga keshlanadi.</summary>
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default);
}

/// <summary>
/// Maktab reestrini shared-bazadan (Tenants jadvali — filtrlanmaydi) o'qiydi. Har so'rovda
/// subdomenni hal qilish uchun ishlatiladi, shuning uchun ijobiy natijalar 30 soniyaga keshlanadi.
/// </summary>
public class TenantStore(AppDbContext db, IMemoryCache cache) : ITenantStore
{
    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var key = "tenant:" + slug.ToLowerInvariant();
        if (cache.TryGetValue(key, out TenantInfo? cached)) return cached;

        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        if (t is null) return null;

        var info = new TenantInfo(t.Id, t.Slug, t.Status,
            t.SubscriptionStartsAt, t.SubscriptionEndsAt, t.EnabledModules.ToList());
        cache.Set(key, info, TimeSpan.FromSeconds(30));
        return info;
    }
}
