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
/// </summary>
public class ReferenceCache(IMemoryCache cache, IServiceScopeFactory scopeFactory)
{
    private async Task<T> GetAsync<T>(string key, TimeSpan ttl, Func<IAppDbContext, Task<T>> load) =>
        (await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            return await load(db);
        }))!;

    /// <summary>Portal meta (choraklar, dars vaqtlari, davomat sabablari + joriy chorak/hafta). TTL 30s.</summary>
    public Task<PortalMetaDto> MetaAsync() =>
        GetAsync("ref:meta", TimeSpan.FromSeconds(30), PortalSchedule.BuildMetaAsync);

    /// <summary>Fan id → nomi. TTL 2 daqiqa.</summary>
    public Task<Dictionary<string, string>> SubjectNamesAsync() =>
        GetAsync("ref:subjectNames", TimeSpan.FromMinutes(2),
            db => db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name));

    /// <summary>O'qituvchi id → FISH. TTL 2 daqiqa.</summary>
    public Task<Dictionary<string, string>> TeacherNamesAsync() =>
        GetAsync("ref:teacherNames", TimeSpan.FromMinutes(2),
            db => db.Teachers.ToDictionaryAsync(t => t.Id, t => t.FullName));
}
