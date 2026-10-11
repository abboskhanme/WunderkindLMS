using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using System.Text;
using System.Text.Json;

namespace SchoolLms.Application.Services;

/// <summary>
/// Telegram Bot API bilan ishlash: e'lon yuborish (sendMessage) va bot yangilanishlarini olish
/// (getUpdates, long polling). Token endi BAZADAN (SchoolMeta) — admin "Sozlamalar" bo'limidan
/// kiritadi; xizmat tokenni xotirada saqlaydi (Load startupda yuklaydi, Set saqlangach yangilaydi).
/// Eski appsettings "Telegram:BotToken" faqat birinchi marta (DB bo'sh bo'lsa) urug' sifatida ishlatiladi.
/// Token bo'sh bo'lsa xizmat "sozlanmagan" — hech narsa yubormaydi, ilova baribir ishlaydi.
/// </summary>
public class TelegramService(
    IConfiguration config, IHttpClientFactory httpFactory, ILogger<TelegramService> logger)
{
    private volatile string _token = "";
    private volatile string _username = "";
    private volatile string _name = "";

    public string BotToken => _token;
    public string BotUsername => _username;
    public string BotName => _name;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_token);

    /// <summary>Xotiradagi token/username/nom'ni yangilaydi (admin sozlamani saqlaganda chaqiriladi).</summary>
    public void Set(string? token, string? username, string? name = null)
    {
        _token = (token ?? "").Trim();
        _username = (username ?? "").Trim().TrimStart('@');
        _name = (name ?? "").Trim();
    }

    /// <summary>
    /// Startupda chaqiriladi: tokenni SchoolMeta'dan yuklaydi. DB bo'sh, lekin appsettings'da token
    /// bo'lsa — uni bir marta DB'ga ko'chiradi (orqaga moslik + UI'da ko'rinishi uchun).
    /// </summary>
    public void Load(IAppDbContext db)
    {
        // Boot paytida so'rov konteksti yo'q — token saqlangan yagona SchoolMeta qatorini o'qiymiz.
        var meta = db.SchoolMeta.IgnoreQueryFilters()
            .FirstOrDefault(m => m.TelegramBotToken != "");

        // Hech kimda token yo'q — eski appsettings urug'i (mavjud birinchi qatorga bir marta ko'chiriladi).
        if (meta is null)
        {
            var cfgToken = config["Telegram:BotToken"];
            if (!string.IsNullOrWhiteSpace(cfgToken))
            {
                var any = db.SchoolMeta.IgnoreQueryFilters().FirstOrDefault();
                if (any is not null)
                {
                    any.TelegramBotToken = cfgToken.Trim();
                    any.TelegramBotUsername = (config["Telegram:BotUsername"] ?? "").Trim().TrimStart('@');
                    db.SaveChanges();
                    meta = any;
                }
            }
        }

        Set(meta?.TelegramBotToken, meta?.TelegramBotUsername, meta?.TelegramBotName);
    }

    private HttpClient Client() => httpFactory.CreateClient("telegram");
    private string ApiBase => $"https://api.telegram.org/bot{_token}";

    /// <summary>Berilgan chatga matn yuboradi (ixtiyoriy reply_markup bilan). Muvaffaqiyat — true.</summary>
    public async Task<bool> SendMessageAsync(
        long chatId, string text, object? replyMarkup = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            var payload = new Dictionary<string, object?> { ["chat_id"] = chatId, ["text"] = text };
            if (replyMarkup is not null) payload["reply_markup"] = replyMarkup;
            using var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await Client().PostAsync($"{ApiBase}/sendMessage", content, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram sendMessage xatosi");
            return false;
        }
    }

    /// <summary>
    /// Berilgan chatga hujjat (fayl) yuboradi (sendDocument, multipart/form-data). Shartnoma
    /// .docx faylini yetkazish uchun. Muvaffaqiyat — true. Token yo'q bo'lsa — false (yubormaydi).
    /// </summary>
    public async Task<bool> SendDocumentAsync(
        long chatId, byte[] bytes, string fileName, string? caption = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(caption), "caption");
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            form.Add(fileContent, "document", fileName);
            var resp = await Client().PostAsync($"{ApiBase}/sendDocument", form, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram sendDocument xatosi");
            return false;
        }
    }

    /// <summary>
    /// <c>sendMessage</c> with <c>parse_mode=HTML</c> (HR topshiriqlari — the old WunderkindHR texts use
    /// <c>&lt;b&gt;</c>). The caller escapes user text. Success — true; not configured — false.
    /// </summary>
    public async Task<bool> SendHtmlAsync(
        long chatId, string html, object? replyMarkup = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["chat_id"] = chatId,
                ["text"] = html,
                ["parse_mode"] = "HTML",
                ["disable_web_page_preview"] = true,
            };
            if (replyMarkup is not null) payload["reply_markup"] = replyMarkup;
            using var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await Client().PostAsync($"{ApiBase}/sendMessage", content, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram sendMessage (HTML) xatosi");
            return false;
        }
    }

    /// <summary>
    /// Formatlangan matn (<see cref="TelegramHtml"/>) — <c>parse_mode=HTML</c>. Telegram formatlashni
    /// o'qiy olmasa ("can't parse entities") — xuddi shu matn teglarsiz yana bir marta yuboriladi,
    /// xabar yo'qolmasin. Boshqa rad etish (bot bloklangan, chat yo'q, tarmoq) qayta urinilmaydi.
    /// </summary>
    public async Task<bool> SendRichAsync(long chatId, string html, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["chat_id"] = chatId,
                ["text"] = html,
                ["parse_mode"] = "HTML",
                ["disable_web_page_preview"] = true,
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Client().PostAsync($"{ApiBase}/sendMessage", content, ct);
            if (resp.IsSuccessStatusCode) return true;
            if (resp.StatusCode != System.Net.HttpStatusCode.BadRequest) return false;
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!body.Contains("parse entities", StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram sendMessage (formatlangan) xatosi");
            return false;
        }
        logger.LogWarning("Telegram formatlangan matnni o'qiy olmadi — oddiy matn bilan qayta yuborilmoqda");
        return await SendMessageAsync(chatId, TelegramHtml.ToPlain(html), ct: ct);
    }

    /// <summary>
    /// <c>sendVoice</c> (multipart) — a manager's voice note on an HR task. Telegram accepts
    /// OGG/OPUS, MP3 and M4A here; anything else is refused by Telegram and we return false.
    /// </summary>
    public async Task<bool> SendVoiceAsync(
        long chatId, byte[] bytes, string fileName, string contentType, string? caption = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(caption), "caption");
            var fileContent = new ByteArrayContent(bytes);
            if (!string.IsNullOrWhiteSpace(contentType))
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "voice", fileName);
            var resp = await Client().PostAsync($"{ApiBase}/sendVoice", form, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram sendVoice xatosi");
            return false;
        }
    }

    /// <summary>
    /// The bot-wide default menu button becomes the plain "commands" menu (client, 2026-09-28: the
    /// panel opens from the "🏫 Panelni ochish" keyboard button, not a Mini App menu button). No
    /// <c>chat_id</c> — the default every chat without its own override shows. Success — true.
    /// </summary>
    public async Task<bool> UseCommandsMenuButtonAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            var payload = new { menu_button = new { type = "commands" } };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await Client().PostAsync($"{ApiBase}/setChatMenuButton", content, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Telegram setChatMenuButton xatosi");
            return false;
        }
    }

    /// <summary>
    /// getUpdates (long polling). Telegram javobidagi "result" massivini (xom JSON) qaytaradi.
    /// Token yo'q yoki xato bo'lsa — null.
    /// </summary>
    public async Task<JsonElement?> GetUpdatesAsync(long offset, int timeoutSec, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        var url = $"{ApiBase}/getUpdates?offset={offset}&timeout={timeoutSec}&allowed_updates=%5B%22message%22%5D";
        var resp = await Client().GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return null;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.TryGetProperty("result", out var result) ? result.Clone() : null;
    }
}
