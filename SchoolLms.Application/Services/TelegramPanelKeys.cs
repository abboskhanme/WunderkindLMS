using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  "🏫 Panelni ochish" — the bot's persistent keyboard button (client, 2026-09-28: "mini app
//  button emas, botning ichida bitta button bo'lsin" — for staff, teachers and parents).
//
//  WHY A KEY. Telegram gives a Mini App opened from a KEYBOARD button no `initData` (official:
//  "It is empty if the Mini App was launched from a keyboard button"), so it cannot tell who
//  pressed it. The bot therefore puts a personal key in the button URL (`/tg/?k=...`) and the
//  Mini App trades it for the usual session (`POST /api/tg/auth/key`).
//
//  THE KEY IS A PASSWORD. It opens the owner's full panel — an admin's too — so:
//   * 256 random bits; only its SHA-256 is stored (`telegram_accounts.panel_key_hash`);
//   * every keyboard the bot sends carries a NEW key and the previous one dies at once;
//   * it lives 90 days at most and dies with the Telegram link (unlink deletes the row);
//   * who the key belongs to is re-checked on every use (account blocked → refused);
//   * it is not logged: Caddy keeps no access log, ASP.NET request lines are Warning-only, and
//     the Mini App drops it from the address and sends `Referrer-Policy: strict-origin`.
// ===========================================================================

public static class TelegramPanelKeys
{
    /// <summary>How long one key works. The keyboard is refreshed on every /start.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>Keyboard label — the bot shows exactly this.</summary>
    public const string ButtonText = "🏫 Panelni ochish";

    /// <summary>Config key of the Mini App address, e.g. <c>https://lms.wunderkindedu.uz/tg/</c>.</summary>
    public const string MiniAppUrlKey = "Telegram:MiniAppUrl";

    /// <summary>Base64url of 32 random bytes → 43 characters.</summary>
    private const int KeyLength = 43;

    /// <summary>
    /// The Mini App address: <c>Telegram:MiniAppUrl</c>, else <c>Receipts:PublicBaseUrl</c> + <c>/tg/</c>.
    /// Null — unknown (dev without config): the bot then sends no panel button.
    /// </summary>
    public static string? MiniAppUrl(Microsoft.Extensions.Configuration.IConfiguration? config)
    {
        var explicitUrl = config?[MiniAppUrlKey];
        if (!string.IsNullOrWhiteSpace(explicitUrl)) return explicitUrl.Trim();
        var baseUrl = config?["Receipts:PublicBaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/') + "/tg/";
    }

    /// <summary>A new key for <paramref name="account"/> (the old one stops working). Caller saves.</summary>
    public static string Issue(TelegramAccount account)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        account.PanelKeyHash = Hash(key);
        account.PanelKeyIssuedAt = AppClock.Now;
        return key;
    }

    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>The account a key belongs to; null when unknown, replaced or older than <see cref="Lifetime"/>.</summary>
    public static async Task<TelegramAccount?> ResolveAsync(IAppDbContext db, string? key, CancellationToken ct = default)
    {
        key = key?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length != KeyLength) return null;
        var hash = Hash(key);
        var account = await db.TelegramAccounts.FirstOrDefaultAsync(a => a.PanelKeyHash == hash, ct);
        if (account?.PanelKeyIssuedAt is not { } issued || issued < AppClock.Now - Lifetime) return null;
        return account;
    }

    /// <summary><c>https://…/tg/?k=KEY</c> — any query already in the address is kept.</summary>
    public static string PanelUrl(string miniAppUrl, string key) =>
        miniAppUrl + (miniAppUrl.Contains('?') ? "&" : "?") + "k=" + Uri.EscapeDataString(key);

    /// <summary>
    /// The persistent reply keyboard: the panel button only. No phone-sharing button (client,
    /// 2026-10-08: "telefon raqam ulashish ... kerakmas, botdan buni butunlay olib tashla") — the
    /// school's notifications follow the signed-in accounts (<see cref="TelegramSavedAccounts.SyncRegistrationsAsync"/>).
    /// Sending it also replaces an older keyboard that still had the phone row.
    /// </summary>
    public static object Keyboard(string panelUrl) => new
    {
        keyboard = new[] { new object[] { new { text = ButtonText, web_app = new { url = panelUrl } } } },
        resize_keyboard = true,
        is_persistent = true,
    };

    /// <summary>
    /// Sends <paramref name="text"/> with a FRESH panel keyboard to the account's chat and saves
    /// the new key only if Telegram accepted the message (otherwise the old key stays valid and
    /// nothing is recorded — e.g. the person has not started the bot yet).
    /// </summary>
    public static async Task<bool> SendKeyboardAsync(
        IAppDbContext db, TelegramService telegram, string? miniAppUrl, TelegramAccount account, string text,
        CancellationToken ct = default) =>
        await SendKeyboardWithKeyAsync(db, telegram, miniAppUrl, account, text, ct) is not null;

    /// <summary><see cref="SendKeyboardAsync"/> that returns the new key (null — not sent, nothing changed).</summary>
    public static async Task<string?> SendKeyboardWithKeyAsync(
        IAppDbContext db, TelegramService telegram, string? miniAppUrl, TelegramAccount account, string text,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(miniAppUrl) || !telegram.IsConfigured) return null;
        var chat = account.TelegramUserId;

        var (oldHash, oldIssued) = (account.PanelKeyHash, account.PanelKeyIssuedAt);
        var key = Issue(account);
        if (!await telegram.SendMessageAsync(chat, text, Keyboard(PanelUrl(miniAppUrl, key)), ct))
        {
            (account.PanelKeyHash, account.PanelKeyIssuedAt) = (oldHash, oldIssued);
            return null;
        }
        await db.SaveChangesAsync(ct);
        return key;
    }

    /// <summary>
    /// For a chat with NO linked account: an inline button — inline web_app launches DO carry
    /// <c>initData</c>, so the Mini App can link the account (login + password or a code).
    /// </summary>
    public static object SignInButton(string miniAppUrl) => new
    {
        inline_keyboard = new[] { new[] { new { text = "🔐 Panelga kirish", web_app = new { url = miniAppUrl } } } },
    };
}
