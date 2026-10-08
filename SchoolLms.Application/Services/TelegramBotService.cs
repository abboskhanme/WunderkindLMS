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
            var synced = await SyncAllRegistrationsAsync(ct);
            logger.LogInformation("Telegram: {Count} ta chat bildirishnoma ro'yxati akkauntlar bo'yicha yangilandi", synced);
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

    /// <summary>
    /// Every linked chat's school notifications follow its accounts (2026-10-08 — phone sharing gone):
    /// on start, so chats linked before the change (most never shared a phone) receive them too.
    /// Returns chats synced; one chat's error never stops the others.
    /// </summary>
    public async Task<int> SyncAllRegistrationsAsync(CancellationToken ct = default)
    {
        List<long> chats;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            chats = await db.TelegramAccounts.AsNoTracking().Select(a => a.TelegramUserId)
                .Union(db.TelegramSavedAccounts.AsNoTracking().Select(s => s.TelegramUserId))
                .ToListAsync(ct);
        }
        var synced = 0;
        foreach (var chat in chats)
        {
            // A context per chat: a failed one leaves nothing pending for the next.
            using var scope = sp.CreateScope();
            try
            {
                await TelegramSavedAccounts.SyncRegistrationsAsync(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), chat, ct);
                synced++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Telegram: bildirishnoma ro'yxati yangilanmadi, tg={Chat}", chat);
            }
        }
        return synced;
    }

    /// <summary>Bitta update. Testlar to'g'ridan-to'g'ri chaqiradi (polling'siz).</summary>
    public async Task HandleUpdateAsync(JsonElement upd, CancellationToken ct)
    {
        if (!upd.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
        var chatId = chatIdEl.GetInt64();

        // No phone sharing any more (client, 2026-10-08: "telefon raqam ulashish ... kerakmas, botdan buni
        // butunlay olib tashla"). An old keyboard may still offer it — the contact is not stored; the
        // panel keyboard (or none) replaces that keyboard, and the sign-in way is explained.
        var sharedContact = msg.TryGetProperty("contact", out _);

        var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var start = text.StartsWith("/start");

        // Linked account → the panel keyboard (a fresh key every time; it also replaces an old keyboard
        // that still had the phone row).
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var account = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.TelegramUserId == chatId, ct);
            var hint = sharedContact ? NoPhoneHint + " " + PanelHint : start ? "Assalomu alaykum! " + PanelHint : PanelHint;
            if (account is not null && await TelegramPanelKeys.SendKeyboardAsync(db, telegram, MiniAppUrl, account, hint, ct))
                return;
        }

        // Not linked yet: the old phone keyboard goes, and an inline web_app button (it carries
        // `initData`) lets the Mini App link the account with the login and password; the panel
        // keyboard then arrives by itself (TelegramAuthController).
        await telegram.SendMessageAsync(chatId,
            sharedContact ? NoPhoneHint : "Assalomu alaykum! Wunderkind maktab paneli shu bot orqali ochiladi.",
            RemoveKeyboard, ct);
        if (MiniAppUrl is { } url)
            await telegram.SendMessageAsync(chatId,
                "Panelga kirish uchun shu tugmani bosing va maktab bergan login-parolni bir marta kiriting. " +
                "Shundan keyin maktab xabarlari ham shu chatga keladi.",
                TelegramPanelKeys.SignInButton(url), ct);
    }

    /// <summary>The answer to a phone shared through an old keyboard.</summary>
    private const string NoPhoneHint = "Telefon raqam endi kerak emas — panelga login va parol bilan kiriladi.";

    /// <summary>Closes an old reply keyboard (the phone-sharing one) in a chat without a linked account.</summary>
    private static object RemoveKeyboard => new { remove_keyboard = true };
}
