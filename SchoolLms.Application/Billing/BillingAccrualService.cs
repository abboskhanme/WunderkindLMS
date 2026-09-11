using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

/// <summary>
/// Fon xizmati: startupda va har 12 soatda hisoblanmagan oylarni to'ldiradi
/// (yangi oyga o'tilganda o'zi ishlaydi). Vazifa: P1-09.
///
/// <para>
/// Shakli ATAYLAB eski <c>TuitionService.TuitionAccrualService</c> bilan bir xil:
/// bir xil davr (12 soat), bir xil xato ushlash tartibi, bir xil "nimani yozdim"
/// jurnali. Farqi — u <c>monthly_charges</c> ga (bitta toifa), bu esa
/// <c>invoices</c> ga (toifalar kesimida) yozadi.
/// </para>
///
/// <para>
/// <b>Ikkalasi yonma-yon ishlamasligi kerak.</b> P1-15 <c>Program.cs</c> da eski
/// <c>TuitionAccrualService</c> registratsiyasini olib tashlab, shuni qo'yadi —
/// ikkisi bir vaqtda yursa o'quvchi IKKI marta hisob oladi (bir marta eski
/// jadvalda, bir marta yangisida). Shu sababli bu xizmat hozircha HECH QAYERDA
/// ro'yxatdan o'tkazilmagan: docs/PENDING_WIRING.md → P1-15.
/// </para>
/// </summary>
public class BillingAccrualService(IServiceProvider services, ILogger<BillingAccrualService> logger)
    : BackgroundService
{
    /// <summary>Eski accrual bilan bir xil davr — sutkada ikki marta yetarli.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Xato butun xizmatni O'LDIRMASIN: keyingi tsiklda qayta urinadi.
                // Hisoblash idempotent, shuning uchun qayta urinish xavfsiz —
                // yarim hisoblangan oy ikkinchi yurishda oxiriga yetkaziladi.
                logger.LogError(ex, "Oylik hisob-fakturalarni hisoblashda xatolik");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// Bitta yurish: hisoblanmagan barcha oylarni to'ldiradi va YOZILGAN oylar
    /// ro'yxatini jurnalga chiqaradi. Testdan va (kelajakda) admin tugmasidan
    /// ham shu metod chaqiriladi.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

        var actorId = await ResolveActorAsync(db, ct);
        if (actorId is null)
        {
            // `ledger_entries.created_by` — users(id) ga FK va NOT NULL: yozuvni
            // kim qo'yganini ko'rsatmasdan pul yozib bo'lmaydi (SPEC §4.4).
            // Bo'sh bazada (hali admin yaratilmagan) hisoblaydigan obuna ham yo'q,
            // shuning uchun bu xato emas — kutib turamiz.
            logger.LogWarning(
                "Oylik hisoblash o'tkazib yuborildi: bazada admin/superadmin yo'q, "
                + "jurnal yozuvini kimga yozishni aniqlab bo'lmadi.");
            return;
        }

        var results = await invoices.AccrueDueAsync(actorId, ct);

        var written = results.Where(r => r.Created > 0).ToList();
        if (written.Count == 0)
        {
            logger.LogDebug("Oylik hisoblash: yangi hisob-faktura yo'q ({Months} oy tekshirildi).",
                results.Count);
            return;
        }

        logger.LogInformation(
            "Oylik hisob-fakturalar yozildi: {Months} — jami {Count} ta, {Total} so'm.",
            string.Join(", ", written.Select(r => r.PeriodMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture))),
            written.Sum(r => r.Created),
            written.Sum(r => r.Total));
    }

    /// <summary>
    /// Fon xizmati kimning nomidan yozadi. Tizim foydalanuvchisi YO'Q (uni
    /// yaratish migratsiya talab qiladi — P1-15/P1-21 ishi), shuning uchun
    /// direktor (superadmin), u ham bo'lmasa admin olinadi. Tartib barqaror
    /// (<c>id</c> bo'yicha), ya'ni har yurishda bir xil odam ko'rinadi va
    /// jurnalni o'qish chalkashmaydi.
    /// </summary>
    private static async Task<string?> ResolveActorAsync(IAppDbContext db, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.SuperAdmin || u.Role == Roles.Admin)
            .OrderBy(u => u.Role == Roles.SuperAdmin ? 0 : 1)
            .ThenBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
}
