using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Yangilik e'lon qilinganda — Telegram tarqatmasi.
//  Spetsifikatsiya: docs/modules/sales-marketing.md §3.3 N4, N6. Vazifa: SM-6.
//
//  KANAL: FAQAT TELEGRAM (CLAUDE.md). Yuboruvchi — mavjud
//  `TelegramService.SendMessageAsync`, `DisciplineParentNotifier` va
//  `MessagesController.SendBroadcast` bilan bir xil yo'l.
//
//  QABUL QILUVCHILAR — IKKI JADVAL, CHAT ID BO'YICHA DUBLIKATSIZ (N6)
//  -----------------------------------------------------------------
//  Ota-ona botga kontakt ulashib yoziladi (`telegram_registrations`), Mini App
//  foydalanuvchisi esa `telegram_accounts` orqali bog'lanadi. Bitta odam
//  ikkalasida ham bo'lishi mumkin va shaxsiy chatda `chat_id == user_id`.
//  Shuning uchun ikkala manba bitta `HashSet<long>` ga yig'iladi — bitta
//  ota-onaga ikki marta yuborish funksiyani o'chirib qo'yishga sabab bo'ladigan
//  nuqson.
//
//  MATN — ODDIY MATN (N2)
//  ----------------------
//  `parse_mode` berilmaydi, ya'ni Telegram matnni formatlamaydi va hech narsani
//  ekranlash (escape) shart emas. Telegram bitta xabarni 4096 belgi bilan
//  cheklaydi; undan uzun yangilik qisqartiriladi va oxiriga "to'liq matn
//  ilovada" deb yoziladi — lentada esa to'liq matn turadi.
//
//  HECH QACHON XATO TASHLAMAYDI
//  ----------------------------
//  Lenta yozuvi allaqachon saqlangan; tarqatmadagi har qanday xato 500 ga
//  aylanib, admin ko'rgan yagona natijani yo'q qilmasligi kerak
//  (`INewsTelegramNotifier` shartnomasi, NewsService.cs).
// ===========================================================================

public sealed class NewsTelegramNotifier(
    IAppDbContext db, TelegramService telegram, ILogger<NewsTelegramNotifier> logger) : INewsTelegramNotifier
{
    /// <summary>Telegram <c>sendMessage</c> matnining eng katta uzunligi (UTF-16 birliklarda).</summary>
    public const int TelegramMaxLength = 4096;

    /// <summary>Qisqartirilgan xabar oxiriga qo'shiladigan izoh.</summary>
    public const string TruncatedSuffix = "…\n\nTo'liq matn — maktab ilovasida.";

    /// <summary>
    /// Ota-ona roli. <see cref="Roles"/> da konstanta yo'q — <c>TelegramParentController</c>
    /// va <c>StudentPortalController</c> ham shu literalni ishlatadi.
    /// </summary>
    private const string ParentRole = "parent";

    /// <summary>Xodim auditoriyasiga kiradigan tizim rollari (N6).</summary>
    private static readonly string[] EmployeeRoles =
        [Roles.Teacher, Roles.Staff, Roles.Admin, Roles.SuperAdmin, Roles.Cashier];

    public async Task<NewsTelegramResult> SendAsync(NewsItem news, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured)
        {
            logger.LogInformation("Yangilik Telegram'ga yuborilmadi (bot sozlanmagan): news={NewsId}", news.Id);
            return NewsTelegramResult.NotSent;
        }

        try
        {
            var chatIds = await RecipientsAsync(db, news, ct);
            var text = BuildMessage(news);
            var sent = 0;
            foreach (var chatId in chatIds)
                if (await telegram.SendMessageAsync(chatId, text, ct: ct)) sent++;

            logger.LogInformation(
                "Yangilik Telegram'ga yuborildi: news={NewsId}, chats={Total}, sent={Sent}",
                news.Id, chatIds.Count, sent);
            return new NewsTelegramResult(true, chatIds.Count, sent);
        }
        catch (Exception ex)
        {
            // Qabul qiluvchilarni o'qishda xato (masalan, so'rov bekor qilindi) —
            // e'lon baribir muvaffaqiyatli, tarqatma esa "urinildi, 0 ta" deb yoziladi.
            logger.LogWarning(ex, "Yangilik tarqatmasida xato: news={NewsId}", news.Id);
            return new NewsTelegramResult(true, 0, 0);
        }
    }

    /// <summary>
    /// Auditoriya bo'yicha chat id'lar — ikki jadval birlashmasi, dublikatsiz (N6).
    /// Testlar tekshira olishi uchun ochiq.
    /// </summary>
    public static async Task<IReadOnlyCollection<long>> RecipientsAsync(
        IAppDbContext db, NewsItem news, CancellationToken ct = default)
    {
        var roles = new List<string>();
        if (news.ForParent) roles.Add(ParentRole);
        if (news.ForStudent) roles.Add(Roles.Student);
        if (news.ForEmployee) roles.AddRange(EmployeeRoles);

        var ids = new HashSet<long>();

        // telegram_registrations: ota-ona yozuvida teacher_id NULL, xodimnikida to'la.
        if (news.ForParent || news.ForEmployee)
        {
            var regs = await db.TelegramRegistrations.AsNoTracking()
                .Where(r => (news.ForParent && r.TeacherId == null)
                         || (news.ForEmployee && r.TeacherId != null))
                .Select(r => r.ChatId)
                .ToListAsync(ct);
            ids.UnionWith(regs);
        }

        // telegram_accounts: Mini App'ga bog'langan tizim akkauntlari, roli bo'yicha.
        if (roles.Count > 0)
        {
            var accounts = await (
                from a in db.TelegramAccounts.AsNoTracking()
                join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                where roles.Contains(u.Role)
                select a.TelegramUserId).ToListAsync(ct);
            ids.UnionWith(accounts);
        }

        ids.Remove(0); // bo'sh / noto'g'ri yozuv — Telegram'da bunday chat yo'q
        return ids;
    }

    /// <summary>
    /// Ota-ona telefonida bildirishnoma bo'lib chiqadigan matn: sarlavha, bo'sh
    /// qator, matn. 4096 belgidan uzun bo'lsa qisqartiriladi.
    /// </summary>
    public static string BuildMessage(NewsItem news)
    {
        // Shakl — sales-marketing.md §5.4: "📰 {sarlavha}", bo'sh qator, matn.
        var text = $"📰 {news.Title.Trim()}\n\n{news.Body.Trim()}";
        if (text.Length <= TelegramMaxLength) return text;

        var cut = TelegramMaxLength - TruncatedSuffix.Length;
        // Surrogat juftini (emoji) o'rtasidan kesmaymiz.
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + TruncatedSuffix;
    }
}
