using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain.Platform;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Infrastructure.Tenancy;

/// <summary>
/// <see cref="ITenantDbRunner"/> — fon xizmatlari (so'rov konteksti yo'q) har aktiv maktab uchun
/// ish bajaradi. Har maktab uchun alohida scope yaratib, scoped <see cref="TenantContext"/>'ni o'sha
/// maktabga sozlaydi — shunda <see cref="AppDbContext"/> global filtri faqat shu maktabni ko'rsatadi.
/// </summary>
public class TenantDbRunner(IServiceScopeFactory scopeFactory) : ITenantDbRunner
{
    public async Task ForEachActiveTenantAsync(Func<IAppDbContext, Task> action, CancellationToken ct = default)
    {
        List<(string Id, string Slug)> tenants;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenants = (await db.Tenants.AsNoTracking()
                    .Where(t => t.Status == TenantStatus.Active)
                    .Select(t => new { t.Id, t.Slug })
                    .ToListAsync(ct))
                .Select(x => (x.Id, x.Slug)).ToList();
        }

        foreach (var t in tenants)
        {
            ct.ThrowIfCancellationRequested();
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(t.Id, t.Slug);
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await action(db);
        }
    }
}
