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
        // Shared-DB: boot'da so'rov (tenant) konteksti yo'q — global filter SchoolMeta'ni yashiradi.
        // Tokenli maktab qatorini tenant'lararo qidiramiz (xotirada bitta token — amalda bitta maktab uchun).
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
