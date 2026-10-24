using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Announcements with pictures (client, 2026-10-24: "e'lon jo'natish qismida xabarlarga rasm qo'shib
/// jo'natish"). The panel uploads through <c>/api/admin/uploads</c> and sends the URLs; Telegram gets
/// <c>sendPhoto</c> / <c>sendMediaGroup</c>, the file once and its <c>file_id</c> after that.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BroadcastImagesTests(ApiFixture fixture)
{
    private const string Broadcast = "/api/admin/messages/broadcast";

    // 1×1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static async Task<string> UploadAsync(HttpClient client, byte[] bytes, string name, string type)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        var res = await client.PostAsync("/api/admin/uploads", new MultipartFormDataContent { { content, "file", name } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<UploadedFileDto>())!.Url;
    }

    private async Task<string> ClassWithParentAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var className = $"BI-{tag}";
        var student = GeneralSettingsFlagsTests.NewStudent($"Rasmli {tag}", className, "+998900000021");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = student.Id, ChatId = 7_000_021, ParentName = "Ota" });
            await db.SaveChangesAsync();
        });
        return className;
    }

    [Fact]
    public async Task Rasmli_elon_saqlanadi_matnsiz_ham_boladi()
    {
        var className = await ClassWithParentAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var first = await UploadAsync(admin, Png, "tadbir.png", "image/png");
        var second = await UploadAsync(admin, Png, "sinf.PNG", "image/png");

        var sent = await admin.PostAsJsonAsync(Broadcast, new
        {
            scope = "class", className, onlyDebtors = false, text = "", imageUrls = new[] { first, second },
        });
        var body = await sent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sent.IsSuccessStatusCode, body.ToString());
        Assert.Equal([first, second], body.GetProperty("imageUrls").EnumerateArray().Select(u => u.GetString()));

        var id = body.GetProperty("id").GetString();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/messages/broadcasts/{id}");
        Assert.Equal(2, detail.GetProperty("broadcast").GetProperty("imageUrls").GetArrayLength());
        var list = await admin.GetFromJsonAsync<List<JsonElement>>($"/api/admin/messages/broadcasts?className={className}");
        Assert.Equal(2, Assert.Single(list!).GetProperty("imageUrls").GetArrayLength());
    }

    [Fact]
    public async Task Yaroqsiz_rasm_rad_etiladi_va_hech_narsa_saqlanmaydi()
    {
        var className = await ClassWithParentAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var pdf = await UploadAsync(admin, "%PDF-1.4"u8.ToArray(), "hujjat.pdf", "application/pdf");
        var png = await UploadAsync(admin, Png, "rasm.png", "image/png");

        string[][] bad =
        [
            [pdf],                                                   // not a picture
            ["/uploads/../appsettings.json"],                        // a path, not an upload name
            ["https://example.com/a.jpg"],                           // not ours
            [$"/uploads/{Guid.NewGuid():N}.jpg"],                    // never uploaded
            [.. Enumerable.Repeat(png, TelegramService.MaxPhotos + 1)], // over Telegram's album limit
        ];
        foreach (var urls in bad)
        {
            var res = await admin.PostAsJsonAsync(Broadcast, new { scope = "class", className, onlyDebtors = false, text = "Salom", imageUrls = urls });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
        // No text and no picture — the old rule stays.
        var empty = await admin.PostAsJsonAsync(Broadcast, new { scope = "class", className, onlyDebtors = false, text = " " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var list = await admin.GetFromJsonAsync<List<JsonElement>>($"/api/admin/messages/broadcasts?className={className}");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Rasmsiz_elon_avvalgidek_bosh_royxat_qaytaradi()
    {
        var className = await ClassWithParentAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync(Broadcast, new { scope = "class", className, onlyDebtors = false, text = "Faqat matn" });
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(res.IsSuccessStatusCode, body.ToString());
        Assert.Equal(0, body.GetProperty("imageUrls").GetArrayLength());
    }

    [Fact]
    public async Task Bitta_rasm_sendPhoto_ikkinchi_chatga_file_id_ketadi()
    {
        var handler = new RecordingHandler();
        var telegram = Configured(handler);
        var photos = new List<TelegramPhoto> { new("a.png", "image/png", Png) };

        Assert.True(await telegram.SendPhotosWithTextAsync(1, photos, "📢 <b>Maktab e'loni</b>\n\nErtaga tadbir"));
        Assert.True(await telegram.SendPhotosWithTextAsync(2, photos, "📢 <b>Maktab e'loni</b>\n\nErtaga tadbir"));

        Assert.Equal(["sendPhoto", "sendPhoto"], handler.Calls.Select(c => c.Method));
        Assert.True(handler.Calls[0].HasFile);
        Assert.Equal("HTML", handler.Calls[0].Fields["parse_mode"]);
        Assert.Contains("Ertaga tadbir", handler.Calls[0].Fields["caption"]);
        // Second chat: no upload, Telegram's id instead.
        Assert.False(handler.Calls[1].HasFile);
        Assert.Equal("file-0", handler.Calls[1].Fields["photo"]);
    }

    [Fact]
    public async Task Bir_nechta_rasm_albom_uzun_matn_alohida_xabar()
    {
        var handler = new RecordingHandler();
        var telegram = Configured(handler);
        var photos = Enumerable.Range(0, 3).Select(i => new TelegramPhoto($"{i}.png", "image/png", Png)).ToList();
        var longText = "📢 <b>Maktab e'loni</b>\n\n" + new string('a', TelegramService.CaptionLimit);

        Assert.True(await telegram.SendPhotosWithTextAsync(1, photos, longText));

        Assert.Equal(["sendMediaGroup", "sendMessage"], handler.Calls.Select(c => c.Method));
        using var media = JsonDocument.Parse(handler.Calls[0].Fields["media"]);
        Assert.Equal(["attach://p0", "attach://p1", "attach://p2"],
            media.RootElement.EnumerateArray().Select(m => m.GetProperty("media").GetString()));
        Assert.False(media.RootElement[0].TryGetProperty("caption", out _));
        Assert.Equal(longText, handler.Calls[1].Fields["text"]);

        // Short text: the caption goes under the first picture of the album, the files are not sent again.
        Assert.True(await telegram.SendPhotosWithTextAsync(2, photos, "Qisqa"));
        using var again = JsonDocument.Parse(handler.Calls[2].Fields["media"]);
        Assert.Equal(["file-0", "file-1", "file-2"], again.RootElement.EnumerateArray().Select(m => m.GetProperty("media").GetString()));
        Assert.Equal("Qisqa", again.RootElement[0].GetProperty("caption").GetString());
        Assert.False(handler.Calls[2].HasFile);
    }

    [Fact]
    public async Task Formatlash_oqilmasa_oddiy_matn_bilan_qayta_yuboriladi()
    {
        var handler = new RecordingHandler { RejectHtmlOnce = true };
        var telegram = Configured(handler);
        var photos = new List<TelegramPhoto> { new("a.png", "image/png", Png) };

        Assert.True(await telegram.SendPhotosAsync(1, photos, "<b>Diqqat</b>"));

        Assert.Equal(2, handler.Calls.Count);
        Assert.False(handler.Calls[1].Fields.ContainsKey("parse_mode"));
        Assert.Equal("Diqqat", handler.Calls[1].Fields["caption"]);
    }

    [Fact]
    public async Task Bot_sozlanmagan_bolsa_hech_narsa_yuborilmaydi()
    {
        var handler = new RecordingHandler();
        var telegram = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(), new Factory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        Assert.False(await telegram.SendPhotosAsync(1, [new TelegramPhoto("a.png", "image/png", Png)], "x"));
        Assert.Empty(handler.Calls);
    }

    private TelegramService Configured(HttpMessageHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(), new Factory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    private sealed record Call(string Method, Dictionary<string, string> Fields, bool HasFile);

    /// <summary>
    /// Records every call. Photo sends answer like Telegram: each picture with a <c>file_id</c>
    /// (<c>file-0</c>, <c>file-1</c> … by position).
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        public bool RejectHtmlOnce { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var fields = new Dictionary<string, string>();
            var hasFile = false;
            if (request.Content is MultipartFormDataContent form)
            {
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (part.Headers.ContentDisposition.FileName is not null) hasFile = true;
                    else fields[name] = await part.ReadAsStringAsync(ct);
                }
            }
            else
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                foreach (var p in doc.RootElement.EnumerateObject())
                    fields[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
            }
            Calls.Add(new Call(method, fields, hasFile));

            if (RejectHtmlOnce && fields.ContainsKey("parse_mode"))
            {
                RejectHtmlOnce = false;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"ok\":false,\"description\":\"Bad Request: can't parse entities\"}"),
                };
            }
            static string Message(int i) => $"{{\"photo\":[{{\"file_id\":\"small-{i}\"}},{{\"file_id\":\"file-{i}\"}}]}}";
            var result = method switch
            {
                "sendPhoto" => Message(0),
                "sendMediaGroup" => "[" + string.Join(",", Enumerable.Range(0, JsonDocument.Parse(fields["media"]).RootElement.GetArrayLength()).Select(Message)) + "]",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"ok\":true,\"result\":{result}}}") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
