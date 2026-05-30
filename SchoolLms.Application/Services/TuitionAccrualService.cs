using SchoolLms.Application.Abstractions;

namespace SchoolLms.Application.Services;

/// <summary>
/// Fon xizmati: startupda va har 12 soatda joriy oygacha hisoblanmagan
/// oylik to'lovlarni avtomatik hisoblaydi (yangi oyga o'tilganda ishlaydi).
/// </summary>
public class TuitionAccrualService(IServiceProvider services, ILogger<TuitionAccrualService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var accrued = await TuitionService.AccrueDue(db);
                if (accrued.Count > 0)
                    logger.LogInformation("Oylik to'lov hisoblandi: {Months}", string.Join(", ", accrued));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Oylik to'lovni hisoblashda xatolik");
            }

            try { await Task.Delay(TimeSpan.FromHours(12), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
