using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Runs the automatic message rules (<see cref="AutoMessageService"/>) every two minutes. Each rule
/// runs once per period; a restart in the middle is safe because the log skips chats already sent.
/// </summary>
public sealed class AutoMessageScheduler(IServiceProvider services, ILogger<AutoMessageScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (migrations, the bot) before the first tick.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var service = new AutoMessageService(
                    scope.ServiceProvider.GetRequiredService<IAppDbContext>(),
                    scope.ServiceProvider.GetRequiredService<TelegramService>(),
                    scope.ServiceProvider.GetRequiredService<ILogger<AutoMessageService>>());
                await service.RunDueAsync(AppClock.Now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Avtomatik xabarlar tekshiruvi xato bilan tugadi");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
