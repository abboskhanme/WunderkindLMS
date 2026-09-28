using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;
using System.Text.Json;

namespace SchoolLms.Application.Services;

/// <summary>
/// Telegram botni long polling (getUpdates) orqali yurituvchi fon xizmati. Ota-ona botga
/// telefon raqamini ulashganda, raqam o'quvchining ParentPhone'i bilan (oxirgi 9 raqam bo'yicha)
/// solishtirilib, TelegramRegistration yoziladi — keyin shu sinf e'lonlari shu chatga boradi.
/// Token sozlanmagan bo'lsa xizmat darrov to'xtaydi (ilova baribir ishlaydi).
///
/// <para>
/// PANEL ENTRY (client, 2026-09-28): not the Mini App menu button but a persistent keyboard
/// button "🏫 Panelni ochish" inside the bot, for everyone (staff, teachers, parents). A linked
/// account gets it on /start (and once at start-up, see <see cref="SendMissingPanelKeyboardsAsync"/>);
/// an unlinked chat gets an inline "🔐 Panelga kirish" that carries <c>initData</c> so the Mini
/// App can link the account — the keyboard follows from <c>TelegramAuthController</c>.
/// Details and the key's safety rules: <see cref="TelegramPanelKeys"/>.
/// </para>
/// </summary>
public class TelegramBotService(
    IServiceProvider sp, TelegramService telegram, ILogger<TelegramBotService> logger) : BackgroundService
{
    /// <summary>Mini App address for the buttons; null — none sent. Tests set it (no public URL there).</summary>
    public string? MiniAppUrl { get; set; } = TelegramPanelKeys.MiniAppUrl(sp.GetService<IConfiguration>());

    private const string PanelHint = "Panelni ochish uchun pastdagi «🏫 Panelni ochish» tugmasini bosing.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long offset = 0;
        var announced = false;
        var prepared = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Token sozlanmagan bo'lsa kutamiz — admin keyin sozlamadan qo'shsa, bot o'zi ishga tushadi.
            if (!telegram.IsConfigured)
            {
                announced = false;
                if (!await DelayAsync(10000, stoppingToken)) break;
                continue;
            }
            if (!announced)
            {
                logger.LogInformation("Telegram bot ishga tushdi (long polling).");
                announced = true;
            }
            if (!prepared)
            {
                prepared = true;
                await PrepareAsync(stoppingToken);
            }

            try
            {
                var updates = await telegram.GetUpdatesAsync(offset, 30, stoppingToken);
                if (updates is null)
                {
                    if (!await DelayAsync(2000, stoppingToken)) break;
                    continue;
                }
                foreach (var upd in updates.Value.EnumerateArray())
                {
                    if (upd.TryGetProperty("update_id", out var idEl))
                        offset = idEl.GetInt64() + 1;
                    await HandleUpdateAsync(upd, stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram getUpdates xatosi");
                if (!await DelayAsync(3000, stoppingToken)) break;
            }
        }
    }

    /// <summary>Kutish; to'xtatilsa false qaytaradi (sikldan chiqish uchun).</summary>
    private static async Task<bool> DelayAsync(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>
    /// Once per start: the Mini App menu button goes (the keyboard replaces it) and every linked
    /// account that has never had the keyboard gets it. Failures are logged, never fatal.
    /// </summary>
    private async Task PrepareAsync(CancellationToken ct)
    {
        try
        {
            if (MiniAppUrl is null)
            {
                logger.LogWarning("Telegram: Mini App manzili sozlanmagan ({Key}) — panel tugmasi yuborilmaydi",
                    TelegramPanelKeys.MiniAppUrlKey);
                return;
            }
            if (!await telegram.UseCommandsMenuButtonAsync(ct))
                logger.LogWarning("Telegram: menyu tugmasini almashtirib bo'lmadi");
            var sent = await SendMissingPanelKeyboardsAsync(ct);
            if (sent > 0) logger.LogInformation("Telegram: {Count} ta akkauntga panel tugmasi yuborildi", sent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Telegram: panel tugmasini tayyorlashda xato");
        }
    }

    /// <summary>
    /// Every linked account without a panel key yet gets the keyboard — the one-time rollout
    /// when the menu button goes away, and later a catch-up for anyone missed. Returns messages sent.
    /// </summary>
    public async Task<int> SendMissingPanelKeyboardsAsync(CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var accounts = await db.TelegramAccounts.Where(a => a.PanelKeyIssuedAt == null).ToListAsync(ct);
        var sent = 0;
        foreach (var account in accounts)
        {
            if (await TelegramPanelKeys.SendKeyboardAsync(db, telegram, MiniAppUrl, account,
                    "Panel endi shu yerda: pastdagi «🏫 Panelni ochish» tugmasini bosing — login va parol shart emas.", ct))
                sent++;
            // Telegram: ~30 messages/s per bot — a small gap keeps a big school well under it.
            await Task.Delay(50, ct);
        }
        return sent;
    }

    /// <summary>Bitta update. Testlar to'g'ridan-to'g'ri chaqiradi (polling'siz).</summary>
    public async Task HandleUpdateAsync(JsonElement upd, CancellationToken ct)
    {
        if (!upd.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
        var chatId = chatIdEl.GetInt64();

        // Kontakt ulashildi — ro'yxatdan o'tkazamiz.
        if (msg.TryGetProperty("contact", out var contact))
        {
            // Faqat O'Z raqami: boshqa odamning kontakt kartasini yuborib, o'sha ota-onaning
            // kvitansiya, intizom va shartnoma xabarlarini olib bo'lmasin.
            if (!IsOwnContact(msg, contact))
            {
                await telegram.SendMessageAsync(chatId,
                    "Faqat o'zingizning raqamingizni pastdagi tugma orqali ulashing.", ContactKeyboard, ct);
                return;
            }
            var phone = contact.TryGetProperty("phone_number", out var p) ? p.GetString() : null;
            await RegisterByPhoneAsync(chatId, phone, SenderName(msg), ct);
            return;
        }

        var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var start = text.StartsWith("/start");

        // Linked account → the panel keyboard (a fresh key every time).
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var account = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.TelegramUserId == chatId, ct);
            if (account is not null
                && await TelegramPanelKeys.SendKeyboardAsync(db, telegram, MiniAppUrl, account,
                    start ? "Assalomu alaykum! " + PanelHint : PanelHint, ct))
                return;
        }

        if (start)
        {
            await telegram.SendMessageAsync(chatId,
                "Assalomu alaykum! Maktab e'lonlari va shartnomalarini olish uchun telefon raqamingizni " +
                "ulashing. Raqamingiz ota-ona yoki xodim ma'lumotidagi raqam bilan solishtiriladi.",
                ContactKeyboard, ct);
        }
        else
        {
            await telegram.SendMessageAsync(chatId,
                "Iltimos, pastdagi tugma orqali telefon raqamingizni ulashing.", ContactKeyboard, ct);
        }
        // Not linked yet: an inline web_app button carries `initData`, so the Mini App can link the
        // account; the keyboard then arrives by itself (TelegramAuthController).
        if (MiniAppUrl is { } url)
            await telegram.SendMessageAsync(chatId,
                "Panelga kirish uchun shu tugmani bosing va login-parolingizni bir marta kiriting. " +
                "Keyin panel pastdagi «🏫 Panelni ochish» tugmasidan ochiladi.",
                TelegramPanelKeys.SignInButton(url), ct);
    }

    private async Task RegisterByPhoneAsync(long chatId, string? phone, string senderName, CancellationToken ct)
    {
        var key = PhoneUtil.Key(phone);
        if (key.Length < 7)
        {
            await telegram.SendMessageAsync(chatId, "Telefon raqami noto'g'ri. Qaytadan urinib ko'ring.", ct: ct);
            return;
        }

        var digits = PhoneUtil.DigitsOnly(phone);
        var linked = new List<string>();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        {
            // Arxivdagi o'quvchi (ketgan yoki test yozuvi) ota-onani ro'yxatga olmaydi.
            var matchedStudents = (await db.Students.Where(s => !s.IsArchived).ToListAsync(ct))
                .Where(s => PhoneUtil.Key(s.ParentPhone) == key).ToList();
            var matchedTeachers = (await db.Teachers.Where(t => !t.IsArchived).ToListAsync(ct))
                .Where(t => PhoneUtil.Key(t.Phone) == key).ToList();

            foreach (var s in matchedStudents)
            {
                var exists = await db.TelegramRegistrations.AnyAsync(
                    r => r.StudentId == s.Id && r.ChatId == chatId, ct);
                if (!exists)
                    db.TelegramRegistrations.Add(new TelegramRegistration
                    {
                        StudentId = s.Id,
                        ChatId = chatId,
                        ParentName = senderName,
                        Phone = digits,
                        CreatedAt = AppClock.Now,
                    });
                linked.Add($"{s.FullName} ({s.ClassName})");
            }

            foreach (var t in matchedTeachers)
            {
                var exists = await db.TelegramRegistrations.AnyAsync(
                    r => r.TeacherId == t.Id && r.ChatId == chatId, ct);
                if (!exists)
                    db.TelegramRegistrations.Add(new TelegramRegistration
                    {
                        TeacherId = t.Id,
                        StudentId = "",
                        ChatId = chatId,
                        ParentName = senderName,
                        Phone = digits,
                        CreatedAt = AppClock.Now,
                    });
                linked.Add($"{t.FullName} (xodim)");
            }

            await db.SaveChangesAsync(ct);
        }

        if (linked.Count == 0)
        {
            await telegram.SendMessageAsync(chatId,
                $"Bu raqam ({phone}) hech bir ota-ona yoki xodim raqami bilan mos kelmadi. " +
                "Iltimos, maktab ma'muriyatiga murojaat qiling.", ct: ct);
            return;
        }

        var registered = "✅ Ro'yxatdan o'tdingiz. Endi quyidagilar bo'yicha e'lon va shartnomalarni olasiz:\n• " +
                         string.Join("\n• ", linked);
        // A linked account keeps its panel button (the phone row is gone now); otherwise the
        // phone keyboard simply closes.
        var account = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.TelegramUserId == chatId, ct);
        if (account is not null
            && await TelegramPanelKeys.SendKeyboardAsync(db, telegram, MiniAppUrl, account, registered, ct))
            return;
        await telegram.SendMessageAsync(chatId, registered, RemoveKeyboard, ct);
    }

    /// <summary>
    /// Kontakt yuboruvchining o'ziniki: <c>contact.user_id == from.id</c>. Telefon kitobidan
    /// yuborilgan kartada <c>user_id</c> bo'lmasligi yoki boshqa odamniki bo'lishi mumkin.
    /// </summary>
    private static bool IsOwnContact(JsonElement msg, JsonElement contact) =>
        msg.TryGetProperty("from", out var from)
        && from.TryGetProperty("id", out var fromId) && fromId.ValueKind == JsonValueKind.Number
        && contact.TryGetProperty("user_id", out var owner) && owner.ValueKind == JsonValueKind.Number
        && owner.GetInt64() == fromId.GetInt64();

    private static string SenderName(JsonElement msg)
    {
        if (!msg.TryGetProperty("from", out var from)) return "";
        var fn = from.TryGetProperty("first_name", out var f) ? f.GetString() : null;
        var ln = from.TryGetProperty("last_name", out var l) ? l.GetString() : null;
        return string.Join(" ", new[] { fn, ln }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    /// <summary>Ro'yxatdan o'tgach "Telefon raqamni ulashish" tugmasi yopiladi.</summary>
    private static object RemoveKeyboard => new { remove_keyboard = true };

    /// <summary>Telefon raqamini so'rovchi klaviatura (request_contact tugmasi).</summary>
    private static object ContactKeyboard => new
    {
        keyboard = new[] { new[] { new { text = "📱 Telefon raqamni ulashish", request_contact = true } } },
        resize_keyboard = true,
        one_time_keyboard = true,
    };
}
