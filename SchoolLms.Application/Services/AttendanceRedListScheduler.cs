using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Runs the red-list scan at its scheduled times (<see cref="AttendanceRedListService.ScanTimes"/>), school
/// days only, plus the daily management digest. Each slot runs once (settings.last_scan_slot), so a restart
/// in the middle of the day does not scan twice.
/// </summary>
public sealed class AttendanceRedListScheduler(IServiceProvider services, ILogger<AttendanceRedListScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (migrations, the bot) before the first tick.
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var service = new AttendanceRedListService(
                    scope.ServiceProvider.GetRequiredService<IAppDbContext>(),
                    scope.ServiceProvider.GetRequiredService<TelegramService>(),
                    scope.ServiceProvider.GetRequiredService<ILogger<AttendanceRedListService>>());
                await service.RunDueAsync(AppClock.Now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Qizil ro'yxat tekshiruvi xato bilan tugadi");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
