namespace SchoolLms.Application.Abstractions;

/// <summary>
/// Fon xizmatlari (so'rov konteksti yo'q) barcha aktiv maktablar bo'ylab ishlashi uchun
/// yordamchi. Har maktab uchun alohida <see cref="IAppDbContext"/> (o'sha maktab DB'siga
/// ulangan) yaratib, berilgan amalni bajaradi. Infrastructure qatlamida amalga oshiriladi.
/// </summary>
public interface ITenantDbRunner
{
    /// <summary>Har aktiv maktab DB'si uchun <paramref name="action"/>ni ketma-ket bajaradi.</summary>
    Task ForEachActiveTenantAsync(Func<IAppDbContext, Task> action, CancellationToken ct = default);
}
