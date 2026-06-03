using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

/// <summary>
/// Kam o'zgaradigan ma'lumotlar (portal meta, fan/o'qituvchi nomlari) uchun qisqa-TTL kesh.
/// Portal endpointlari ularni deyarli har so'rovda o'qiydi — kesh DB yukini sezilarli kamaytiradi.
///
/// <para><b>MUHIM:</b> faqat o'zgarmas DTO/lug'atlar keshlanadi (EF entity EMAS) — shuning uchun
/// so'rovlar/oqimlar orasida bo'lishish xavfsiz. Yuklash paytida alohida scope'da DbContext
/// ochiladi (singleton xizmat scoped DbContext'ni ushlab qolmasligi uchun).</para>
///
/// <para><b>Multi-tenant:</b> ko'p maktabga o'tilganda kesh kalitlariga TenantId qo'shilishi SHART
/// (masalan <c>$"ref:meta:{tenantId}"</c>), aks holda maktablar bir-birining ma'lumotini ko'radi.</para>
/// </summary>
public class ReferenceCache(IMemoryCache cache, IServiceScopeFactory scopeFactory)
{
    // Multi-tenant: kesh kaliti tenantId bilan ajratiladi VA yuklash scope'ida joriy maktab
    // o'rnatiladi — aks holda global query filter bo'sh qaytaradi yoki maktablar bir-birini ko'radi.
    private async Task<T> GetAsync<T>(string tenantId, string key, TimeSpan ttl, Func<IAppDbContext, Task<T>> load) =>
        (await cache.GetOrCreateAsync($"{key}:{tenantId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId, tenantId);
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            return await load(db);
        }))!;

    /// <summary>Portal meta (choraklar, dars vaqtlari, davomat sabablari + joriy chorak/hafta). TTL 30s.</summary>
    public Task<PortalMetaDto> MetaAsync(string tenantId) =>
        GetAsync(tenantId, "ref:meta", TimeSpan.FromSeconds(30), PortalSchedule.BuildMetaAsync);

    /// <summary>Fan id → nomi. TTL 2 daqiqa.</summary>
    public Task<Dictionary<string, string>> SubjectNamesAsync(string tenantId) =>
        GetAsync(tenantId, "ref:subjectNames", TimeSpan.FromMinutes(2),
            db => db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name));

    /// <summary>O'qituvchi id → FISH. TTL 2 daqiqa.</summary>
    public Task<Dictionary<string, string>> TeacherNamesAsync(string tenantId) =>
        GetAsync(tenantId, "ref:teacherNames", TimeSpan.FromMinutes(2),
            db => db.Teachers.ToDictionaryAsync(t => t.Id, t => t.FullName));
}
