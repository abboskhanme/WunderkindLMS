using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
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
/// <para><b>Saqlash joyi:</b> <see cref="IDistributedCache"/> — Program.cs da Redis mavjud bo'lsa
/// Redis, aks holda jarayon ichidagi xotira. Qiymatlar JSON ko'rinishida saqlanadi, shuning uchun
/// bir nechta konteyner (yoki restart) bitta keshni bo'lisha oladi.</para>
///
/// <para><b>Xatoga chidamlilik:</b> kesh o'qish/yozishdagi har qanday xato (Redis tushib qolgan,
/// tarmoq uzilgan, eski JSON formati) YUTILADI va so'rov to'g'ridan-to'g'ri DB'dan javob oladi.
/// Kesh nosozligi portalni ISHDAN CHIQARMAYDI — faqat sekinlashtiradi.</para>
/// </summary>
public class ReferenceCache(IDistributedCache cache, IServiceScopeFactory scopeFactory, ILogger<ReferenceCache> log)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<T> GetAsync<T>(string key, TimeSpan ttl, Func<IAppDbContext, Task<T>> load)
    {
        // 1) Keshdan o'qish. Redis yiqilgan bo'lsa ham davom etamiz (DB'dan o'qiymiz).
        try
        {
            var bytes = await cache.GetAsync(key);
            if (bytes is { Length: > 0 })
            {
                var hit = JsonSerializer.Deserialize<T>(bytes, JsonOpts);
                if (hit is not null) return hit;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Kesh o'qishda xato (kalit {Key}) — DB'dan yuklanadi", key);
        }

        // 2) Manbadan yuklash — alohida scope'da (singleton scoped DbContext'ni ushlab qolmasin).
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var value = await load(db);

        // 3) Keshga yozish. Yozilmasa ham javob qaytaveradi.
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Kesh yozishda xato (kalit {Key}) — e'tiborsiz qoldirildi", key);
        }

        return value;
    }

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
