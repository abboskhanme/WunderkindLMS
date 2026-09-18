using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.Billing;

// ===========================================================================
//  Direktorga oylik eslatma — F6.01 (finance-parity.md §2.6.3).
// ===========================================================================
//
//  QACHON YURADI
//  -------------
//  Har kuni <see cref="ReminderRunAt"/> da (08:00, maktab mintaqasi) —
//  <c>AnomalyScanService</c> bilan bir xil shakl (`UntilNextRun`), lekin
//  ATAYLAB ish kuni BOSHIDA, tungi 03:00 da EMAS: bu direktorga mo'ljallangan
//  xabar, u tungi vaqtda o'qilmaydi va Telegram bildirishnomasi uxlab
//  yotgan odamni uyg'otishi mumkin.
//
//  NEGA ISHGA TUSHISHDA DARHOL YURMAYDI — `AnomalyScanService` DAN FARQLI
//  -------------------------------------------------------------------------
//  `AnomalyScanService` ishga tushishda darhol yuradi, chunki tekshiruv
//  IDEMPOTENT: bazadagi unikal indeks (kind, ref_id) takroriy bayroqning
//  oldini oladi. Bu xizmatda esa "bugun allaqachon yuborilganmi" degan
//  bazadagi iz YO'Q (`expense_templates` ustunlar ro'yxati aniq: id, name,
//  category, amount, day_of_month, is_active, created_at — "oxirgi eslatma"
//  ustuni yo'q). Ishga tushishda darhol yursa, har deploy direktorga bir xil
//  kunlik eslatmani IKKINCHI marta yuborardi. Shuning uchun bu xizmat FAQAT
//  soat bo'yicha yuradi: sikl "keyingi 08:00 gacha" kutadi, ya'ni bir xil
//  kalendar kunida ikki marta ishga tushmaydi (agar jarayon aynan shu
//  tor oynada qayta ishga tushmasa — amalda ahamiyatsiz xavf, P2 gap uchun
//  qabul qilingan murosaga).
//
//  DIREKTORNING TELEGRAM CHATI QANDAY TOPILADI
//  ----------------------------------------------
//  Kodda "direktor uchun Telegram chat" degan alohida ustun YO'Q — mavjud
//  yagona xodim kanali `TelegramRegistrations.TeacherId`
//  (`TelegramBotService.RegisterByPhoneAsync`: xodim botga telefon raqamini
//  ulashadi, u `teachers.phone` bilan solishtiriladi). Direktor (rol
//  `superadmin`) bu kanaldan FAQAT shunda o'tadi, agar uning
//  `teachers.user_id` orqali bog'langan xodim kartochkasi bo'lsa VA o'sha
//  kartochkaning telefon raqami bilan botga ro'yxatdan o'tgan bo'lsa —
//  xuddi boshqa xodim kabi. Bu YAGONA mavjud yo'l (`DisciplineParentNotifier`
//  ota-ona uchun `StudentId` orqali qanday ishlasa, bu yerda `superadmin`
//  uchun `TeacherId` orqali xuddi shunday). Agar direktorning xodim
//  kartochkasi yo'q yoki botga ulanmagan bo'lsa — hech narsa yuborilmaydi,
//  hech narsa yiqilmaydi (boshqa Telegram bildirishnomalari bilan bir xil
//  "jim" xulq-atvor). Bu — kelgusi ishga tavsiya: agar direktor xodim
//  kartochkasisiz sof `app_users` yozuvi bo'lsa, uni ham botga ulash
//  mumkin bo'lgan ikkinchi yo'l kerak bo'ladi (masalan `app_users.phone`).
// ===========================================================================

/// <summary>Bitta yurish natijasi — testda va jurnalda ishlatiladi.</summary>
public record ExpenseTemplateReminderResult(DateOnly Date, int DueCount, int ChatsNotified);

/// <summary>
/// Fon xizmati: har kuni direktorga bugun "kutilayotgan" faol shablonlar
/// haqida Telegram xabari yuboradi. Batafsil: fayl boshidagi izoh.
/// </summary>
public class ExpenseTemplateReminderService(
    IServiceProvider services, ILogger<ExpenseTemplateReminderService> logger) : BackgroundService
{
    /// <summary>Eslatma har kuni shu vaqtda (maktab mintaqasi) — fayl boshidagi izoh.</summary>
    public static readonly TimeOnly ReminderRunAt = new(8, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(UntilNextRun(), stoppingToken); }
            catch (TaskCanceledException) { break; }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Xato butun xizmatni O'LDIRMASIN: ertaga qayta urinadi.
                logger.LogError(ex, "Rejalashtirilgan chiqim eslatmasi yuborilmadi");
            }
        }
    }

    /// <summary>
    /// Bitta yurish. Testdan ham shu metod chaqiriladi (fon tsiklidan
    /// mustaqil) — `AnomalyScanService.RunOnceAsync` bilan bir xil naqsh.
    /// </summary>
    public async Task<ExpenseTemplateReminderResult> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var telegram = scope.ServiceProvider.GetRequiredService<TelegramService>();

        var today = AppClock.Today;

        var due = await db.ExpenseTemplates.AsNoTracking()
            .Where(t => t.IsActive && t.DayOfMonth == today.Day)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            logger.LogDebug("Rejalashtirilgan chiqim eslatmasi: bugun ({Date}) hech qanday shablon yo'q.", today);
            return new ExpenseTemplateReminderResult(today, 0, 0);
        }

        if (!telegram.IsConfigured)
        {
            logger.LogInformation(
                "Rejalashtirilgan chiqim eslatmasi yuborilmadi (bot sozlanmagan): {Count} ta shablon bugun.",
                due.Count);
            return new ExpenseTemplateReminderResult(today, due.Count, 0);
        }

        var chatIds = await DirectorChatIdsAsync(db, ct);
        if (chatIds.Count == 0)
        {
            logger.LogInformation(
                "Rejalashtirilgan chiqim eslatmasi yuborilmadi (direktor botga ulanmagan): {Count} ta shablon bugun.",
                due.Count);
            return new ExpenseTemplateReminderResult(today, due.Count, 0);
        }

        var text = BuildMessage(today, due);
        var sent = 0;
        foreach (var chatId in chatIds)
            if (await telegram.SendMessageAsync(chatId, text, ct: ct)) sent++;

        logger.LogInformation(
            "Rejalashtirilgan chiqim eslatmasi: {Date}, {Count} ta shablon, {Chats} ta chatga yuborildi ({Sent} muvaffaqiyatli).",
            today, due.Count, chatIds.Count, sent);

        return new ExpenseTemplateReminderResult(today, due.Count, sent);
    }

    /// <summary>
    /// Direktor (rol <c>superadmin</c>) botga ulangan chatlari — fayl
    /// boshidagi izohdagi yagona mavjud yo'l orqali.
    /// </summary>
    private static async Task<List<long>> DirectorChatIdsAsync(IAppDbContext db, CancellationToken ct)
    {
        var directorTeacherIds = await (
            from t in db.Teachers.AsNoTracking()
            join u in db.Users.AsNoTracking() on t.UserId equals u.Id
            where u.Role == Roles.SuperAdmin
            select t.Id
        ).Distinct().ToListAsync(ct);

        if (directorTeacherIds.Count == 0) return [];

        return await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.TeacherId != null && directorTeacherIds.Contains(r.TeacherId))
            .Select(r => r.ChatId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>Direktor o'qiydigan matn — nom, toifa, summa, jami.</summary>
    private static string BuildMessage(DateOnly date, IReadOnlyList<ExpenseTemplate> due)
    {
        var lines = new List<string>
        {
            $"Rejalashtirilgan chiqimlar — bugun ({date:dd.MM.yyyy}):",
            "",
        };

        foreach (var t in due)
            lines.Add($"- {t.Name} ({CategoryLabel(t.Category)}): {AuditService.Money(t.Amount)} so'm");

        lines.Add("");
        lines.Add($"Jami: {AuditService.Money(due.Sum(t => t.Amount))} so'm");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// <see cref="ExpenseTemplateService"/> dagi <c>CategoryLabels</c> bilan
    /// QASDDAN takrorlangan (u <c>private</c>) — ikkalasi ham
    /// <see cref="Accounts.ExpenseCategories"/> ning yopiq ro'yxatidan, ya'ni
    /// bir-biridan uzoqlashish xavfi past.
    /// </summary>
    private static string CategoryLabel(string category) => category switch
    {
        "salary" => "Oylik maosh",
        "utilities" => "Kommunal",
        "supplies" => "Jihoz/materiallar",
        "rent" => "Ijara",
        "repair" => "Ta'mirlash",
        _ => "Boshqa chiqim",
    };

    /// <summary>Keyingi 08:00 gacha qolgan vaqt (maktab mintaqasi bo'yicha).</summary>
    private static TimeSpan UntilNextRun()
    {
        var now = AppClock.Now;
        var next = now.Date.Add(ReminderRunAt.ToTimeSpan());
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
