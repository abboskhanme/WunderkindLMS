using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Hubs;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Turniket jonli kuzatuvi. Integratsiya YOQILGAN bo'lsa (SchoolMeta.TurnstileEnabled + host),
/// har ~20 soniyada qurilmadan hodisalarni sinxronlaydi va yangi o'tish bo'lsa admin panellariga
/// (LiveHub "turnstile" → "turnstileChanged") push qiladi — davomat/o'quvchi turniketi jonli yangilanadi.
/// Yoqilmagan bo'lsa hech narsa qilmaydi (qurilmaga so'rov yubormaydi).
/// </summary>
public class TurnstileLiveService(
    IServiceProvider services, IHubContext<LiveHub> live, ILogger<TurnstileLiveService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup migratsiya/seed tugashi uchun biroz kutamiz.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var turnstile = scope.ServiceProvider.GetRequiredService<TurnstileService>();

                var meta = await db.SchoolMeta.FirstOrDefaultAsync(stoppingToken);
                if (meta is { TurnstileEnabled: true } && !string.IsNullOrWhiteSpace(meta.TurnstileHost))
                {
                    var res = await turnstile.SyncAsync(db);
                    // Faqat HAQIQATAN yangi o'tish bo'lsa push qilamiz (bekorga reload qilmaymiz).
                    if (res.Ok && res.EventsFetched > 0)
                        await live.Clients.Group(LiveHub.Group("turnstile"))
                            .SendAsync("turnstileChanged", new { at = AppClock.Iso() }, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Turniket jonli sinxronlash xatosi");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
