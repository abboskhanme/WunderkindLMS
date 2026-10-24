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
    /// <see cref="SendDocumentAsync"/> with a formatted caption (<c>parse_mode=HTML</c>, the payment
    /// auto-message, 2026-10-15). If Telegram cannot parse the formatting, the same file goes again
    /// with the caption as plain text — the document must not be lost over a tag.
    /// </summary>
    public async Task<bool> SendDocumentHtmlAsync(
        long chatId, byte[] bytes, string fileName, string captionHtml, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            form.Add(new StringContent(captionHtml), "caption");
            form.Add(new StringContent("HTML"), "parse_mode");
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            form.Add(fileContent, "document", fileName);
            using var resp = await Client().PostAsync($"{ApiBase}/sendDocument", form, ct);
            if (resp.IsSuccessStatusCode) return true;
            if (resp.StatusCode != System.Net.HttpStatusCode.BadRequest) return false;
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!body.Contains("parse entities", StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram sendDocument (formatlangan) xatosi");
            return false;
        }
        logger.LogWarning("Telegram fayl izohini o'qiy olmadi — oddiy matn bilan qayta yuborilmoqda");
        return await SendDocumentAsync(chatId, bytes, fileName, TelegramHtml.ToPlain(captionHtml), ct);
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

    /// <summary>Telegram's limit on a photo caption, in visible characters (after the tags are parsed).</summary>
    public const int CaptionLimit = 1024;

    /// <summary>Pictures in one album (<c>sendMediaGroup</c>): Telegram allows 2–10.</summary>
    public const int MaxPhotos = 10;

    /// <summary>
    /// Pictures plus a formatted text (announcement with images, 2026-10-24). The text fits a caption
    /// (<see cref="CaptionLimit"/>) → one message, the text under the pictures; longer → the pictures
    /// first and the text right after them. Success — everything reached the chat.
    /// </summary>
    public async Task<bool> SendPhotosWithTextAsync(
        long chatId, IReadOnlyList<TelegramPhoto> photos, string html, CancellationToken ct = default)
    {
        if (TelegramHtml.ToPlain(html).Length <= CaptionLimit) return await SendPhotosAsync(chatId, photos, html, ct);
        return await SendPhotosAsync(chatId, photos, null, ct) && await SendRichAsync(chatId, html, ct);
    }

    /// <summary>
    /// One picture → <c>sendPhoto</c>, 2–10 → <c>sendMediaGroup</c> (an album, the caption under the
    /// first one). The caller keeps <paramref name="captionHtml"/> within <see cref="CaptionLimit"/>.
    /// A caption Telegram cannot parse goes again as plain text, as in <see cref="SendRichAsync"/>.
    /// On success the photos' <see cref="TelegramPhoto.FileId"/> are filled, so the next chat gets
    /// Telegram's id instead of the file again.
    /// </summary>
    public async Task<bool> SendPhotosAsync(
        long chatId, IReadOnlyList<TelegramPhoto> photos, string? captionHtml, CancellationToken ct = default)
    {
        if (!IsConfigured || photos.Count is 0 or > MaxPhotos) return false;
        var sent = await PostPhotosAsync(chatId, photos, captionHtml, html: true, ct);
        if (sent != PhotoSend.ParseError) return sent == PhotoSend.Ok;
        logger.LogWarning("Telegram rasm izohini o'qiy olmadi — oddiy matn bilan qayta yuborilmoqda");
        return await PostPhotosAsync(chatId, photos, TelegramHtml.ToPlain(captionHtml), html: false, ct) == PhotoSend.Ok;
    }

    private enum PhotoSend { Ok, ParseError, Failed }

    private async Task<PhotoSend> PostPhotosAsync(
        long chatId, IReadOnlyList<TelegramPhoto> photos, string? caption, bool html, CancellationToken ct)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            var hasCaption = !string.IsNullOrWhiteSpace(caption);
            string method;
            if (photos.Count == 1)
            {
                method = "sendPhoto";
                var photo = photos[0];
                if (photo.FileId is { } id) form.Add(new StringContent(id), "photo");
                else AddPhotoFile(form, photo, "photo");
                if (hasCaption)
                {
                    form.Add(new StringContent(caption!), "caption");
                    if (html) form.Add(new StringContent("HTML"), "parse_mode");
                }
            }
            else
            {
                method = "sendMediaGroup";
                var media = new List<Dictionary<string, object?>>(photos.Count);
                for (var i = 0; i < photos.Count; i++)
                {
                    var item = new Dictionary<string, object?> { ["type"] = "photo", ["media"] = photos[i].FileId ?? $"attach://p{i}" };
                    if (photos[i].FileId is null) AddPhotoFile(form, photos[i], $"p{i}");
                    if (i == 0 && hasCaption)
                    {
                        item["caption"] = caption;
                        if (html) item["parse_mode"] = "HTML";
                    }
                    media.Add(item);
                }
                form.Add(new StringContent(JsonSerializer.Serialize(media)), "media");
            }
            using var resp = await Client().PostAsync($"{ApiBase}/{method}", form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode)
            {
                RememberFileIds(photos, body);
                return PhotoSend.Ok;
            }
            return resp.StatusCode == System.Net.HttpStatusCode.BadRequest
                && body.Contains("parse entities", StringComparison.OrdinalIgnoreCase)
                ? PhotoSend.ParseError : PhotoSend.Failed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram rasm yuborish xatosi");
            return PhotoSend.Failed;
        }
    }

    private static void AddPhotoFile(MultipartFormDataContent form, TelegramPhoto photo, string name)
    {
        var file = new ByteArrayContent(photo.Bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(photo.ContentType);
        form.Add(file, name, photo.FileName);
    }

    /// <summary>
    /// <c>result</c> is one message (<c>sendPhoto</c>) or an array of them (<c>sendMediaGroup</c>);
    /// each carries the picture in several sizes, the last one the largest.
    /// </summary>
    private static void RememberFileIds(IReadOnlyList<TelegramPhoto> photos, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return;
            var messages = result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().ToList() : [result];
            for (var i = 0; i < photos.Count && i < messages.Count; i++)
            {
                if (photos[i].FileId is not null || messages[i].ValueKind != JsonValueKind.Object) continue;
                if (!messages[i].TryGetProperty("photo", out var sizes) || sizes.ValueKind != JsonValueKind.Array) continue;
                var count = sizes.GetArrayLength();
                if (count > 0 && sizes[count - 1].TryGetProperty("file_id", out var id)) photos[i].FileId = id.GetString();
            }
        }
        catch (JsonException)
        {
            // The send itself succeeded; the next chat simply gets the file again.
        }
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

/// <summary>
/// One picture for <see cref="TelegramService.SendPhotosAsync"/>. The first successful send uploads
/// <see cref="Bytes"/>; Telegram's <see cref="FileId"/> is kept, and every next chat gets the id —
/// the file travels once per announcement, not once per parent.
/// </summary>
public sealed class TelegramPhoto(string fileName, string contentType, byte[] bytes)
{
    public string FileName { get; } = fileName;
    public string ContentType { get; } = contentType;
    public byte[] Bytes { get; } = bytes;
    public string? FileId { get; set; }
}
