using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Pictures on automatic messages (2026-10-25: announcements got pictures on 2026-10-24, the
/// automatic rules had to get them too). A rule keeps up to 10 uploaded pictures; its run takes them
/// with the text, and every chat gets <c>sendPhoto</c> / <c>sendMediaGroup</c> — the file once, then
/// Telegram's <c>file_id</c>. Every Telegram call lands in a recording handler.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AutoMessageImagesTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Url = "/api/admin/auto-messages";
    private static readonly DateTime Morning = new(2026, 10, 13, 9, 0, 0);
    private readonly List<string> _connectionStrings = [];

    // 1×1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  API
    // =====================================================================

    [Fact]
    public async Task Qoidaga_rasm_saqlanadi_yaroqsizi_rad_etiladi_tolov_qoidasiga_qoshilmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var first = await UploadAsync(admin, Png, "tabrik.png", "image/png");
        var second = await UploadAsync(admin, Png, "gul.png", "image/png");
        var pdf = await UploadAsync(admin, "%PDF-1.4"u8.ToArray(), "hujjat.pdf", "application/pdf");

        AutoMessageRule birthday = null!, payment = null!;
        await fixture.Api.WithDbAsync(async db =>
        {
            birthday = await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "birthday");
            payment = await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "payment");
        });
        object Body(object? imageUrls) => new
        {
            enabled = false, sendTime = "09:00", dayOfMonth = 1, minAmount = 0, text = "Tabrik, {ism}!", imageUrls,
        };
        try
        {
            var ok = await admin.PutAsJsonAsync($"{Url}/birthday", Body(new[] { first, " ", second }));
            var saved = await ok.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(ok.IsSuccessStatusCode, saved.ToString());
            Assert.Equal([first, second], saved.GetProperty("imageUrls").EnumerateArray().Select(u => u.GetString()));
            var listed = (await admin.GetFromJsonAsync<JsonElement>(Url)).EnumerateArray()
                .Single(r => r.GetProperty("key").GetString() == "birthday");
            Assert.Equal(2, listed.GetProperty("imageUrls").GetArrayLength());

            // Not sent at all — unchanged; an empty list — no pictures.
            Assert.True((await admin.PutAsJsonAsync($"{Url}/birthday", Body(null))).IsSuccessStatusCode);
            await fixture.Api.WithDbAsync(async db =>
                Assert.Equal([first, second], (await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "birthday")).ImageUrls));
            Assert.True((await admin.PutAsJsonAsync($"{Url}/birthday", Body(Array.Empty<string>()))).IsSuccessStatusCode);
            await fixture.Api.WithDbAsync(async db =>
                Assert.Empty((await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "birthday")).ImageUrls));

            string[][] bad =
            [
                [pdf],                                                      // not a picture
                ["/uploads/../appsettings.json"],                           // a path, not an upload name
                [$"/uploads/{Guid.NewGuid():N}.jpg"],                       // never uploaded
                [.. Enumerable.Repeat(first, TelegramService.MaxPhotos + 1)], // over Telegram's album limit
            ];
            foreach (var urls in bad)
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Url}/birthday", Body(urls))).StatusCode);

            // The payment message is the receipt's caption: pictures are not taken.
            var pay = await admin.PutAsJsonAsync($"{Url}/payment", Body(new[] { first }));
            Assert.True(pay.IsSuccessStatusCode);
            await fixture.Api.WithDbAsync(async db =>
                Assert.Empty((await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "payment")).ImageUrls));
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                foreach (var original in new[] { birthday, payment })
                {
                    var r = await db.AutoMessageRules.SingleAsync(x => x.Key == original.Key);
                    (r.Enabled, r.SendTime, r.Text, r.ImageUrls) = (original.Enabled, original.SendTime, original.Text, original.ImageUrls);
                }
                await db.SaveChangesAsync();
            });
        }
    }

    // =====================================================================
    //  Sending
    // =====================================================================

    [Fact]
    public async Task Rasmli_qoida_rasm_bilan_ketadi_fayl_bir_marta_yuklanadi()
    {
        var db = await FreshAsync("am_img_send");
        var ali = Pupil("Aliyev Ali Valiyevich", "5-A", "2015-10-13");
        db.Students.Add(ali);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 601, ParentName = "Karim aka" },
            new TelegramRegistration { StudentId = ali.Id, ChatId = 602, ParentName = "Malika opa" });
        await db.SaveChangesAsync();
        var picture = WriteUpload(Png, "png");
        await EnableAsync(db, "birthday", "Tabrik, **{ism}**!", [picture]);

        var handler = new RecordingHandler();
        var run = Assert.Single(await Service(db, handler).RunDueAsync(Morning));
        Assert.Equal((1, 2, 0), (run.Pupils, run.Sent, run.Failed));

        Assert.Equal(["sendPhoto", "sendPhoto"], handler.Calls.Select(c => c.Method));
        Assert.True(handler.Calls[0].HasFile);
        Assert.Equal("Tabrik, <b>Ali</b>!", handler.Calls[0].Fields["caption"]);
        Assert.Equal("HTML", handler.Calls[0].Fields["parse_mode"]);
        // Second chat: Telegram's id, not the file again.
        Assert.False(handler.Calls[1].HasFile);
        Assert.Equal("file-0", handler.Calls[1].Fields["photo"]);

        var saved = await db.AutoMessageRuns.AsNoTracking().SingleAsync();
        Assert.Equal([picture], saved.ImageUrls);
        Assert.Equal([picture], (await Service(db, handler).GetRunAsync(saved.Id))!.ImageUrls);
        Assert.All(await db.AutoMessageLog.AsNoTracking().ToListAsync(), l => Assert.Equal(BroadcastDelivery.Sent, l.Status));
    }

    [Fact]
    public async Task Tasdiqlangan_yuborish_tayyorlangandagi_rasmlar_bilan_ketadi()
    {
        var db = await FreshAsync("am_img_approve");
        var ali = Pupil("Aliyev Ali", "5-A", "2015-10-13");
        db.Students.Add(ali);
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = ali.Id, ChatId = 611 });
        await db.SaveChangesAsync();
        var first = WriteUpload(Png, "png");
        var second = WriteUpload(Png, "png");
        await EnableAsync(db, "birthday", "Tabrik, {ism}!", [first, second], requiresApproval: true);

        var handler = new RecordingHandler();
        var service = Service(db, handler);
        Assert.Single(await service.RunDueAsync(Morning));
        Assert.Empty(handler.Calls);
        var run = await db.AutoMessageRuns.AsNoTracking().SingleAsync();
        Assert.Equal(AutoMessageStatus.PendingApproval, run.Status);

        // The rule's pictures change while the run waits — the approved run sends what it prepared.
        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == "birthday");
        rule.ImageUrls = [];
        await db.SaveChangesAsync();

        var approved = await service.ApproveAsync(run.Id, "director", Morning.AddHours(1));
        Assert.Equal(1, approved.Sent);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("sendMediaGroup", call.Method);
        using var media = JsonDocument.Parse(call.Fields["media"]);
        Assert.Equal(["attach://p0", "attach://p1"], media.RootElement.EnumerateArray().Select(m => m.GetProperty("media").GetString()));
        Assert.Equal("Tabrik, Ali!", media.RootElement[0].GetProperty("caption").GetString());
    }

    [Fact]
    public async Task Rasm_ochib_ketgan_bolsa_matn_baribir_ketadi()
    {
        var db = await FreshAsync("am_img_missing");
        var ali = Pupil("Aliyev Ali", "5-A", "2015-10-13");
        db.Students.Add(ali);
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = ali.Id, ChatId = 621 });
        await db.SaveChangesAsync();
        await EnableAsync(db, "birthday", "Tabrik, {ism}!", [$"/uploads/{Guid.NewGuid():N}.png"]);

        var handler = new RecordingHandler();
        var run = Assert.Single(await Service(db, handler).RunDueAsync(Morning));
        Assert.Equal(1, run.Sent);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(("sendMessage", "Tabrik, Ali!"), (call.Method, call.Fields["text"]));
    }

    [Fact]
    public async Task Rasmsiz_qoida_avvalgidek_faqat_matn()
    {
        var db = await FreshAsync("am_img_none");
        var ali = Pupil("Aliyev Ali", "5-A", "2015-10-13");
        db.Students.Add(ali);
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = ali.Id, ChatId = 631 });
        await db.SaveChangesAsync();
        await EnableAsync(db, "birthday", "Tabrik, {ism}!", []);

        var handler = new RecordingHandler();
        Assert.Equal(1, Assert.Single(await Service(db, handler).RunDueAsync(Morning)).Sent);
        Assert.Equal(["sendMessage"], handler.Calls.Select(c => c.Method));
        Assert.Empty((await db.AutoMessageRuns.AsNoTracking().SingleAsync()).ImageUrls);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private IWebHostEnvironment Env => fixture.Api.Services.GetRequiredService<IWebHostEnvironment>();

    private AutoMessageService Service(AppDbContext db, RecordingHandler handler) =>
        new(db, ConfiguredTelegram(handler), env: Env);

    /// <summary>A file as the upload endpoint would have stored it; returns its <c>/uploads/…</c> URL.</summary>
    private string WriteUpload(byte[] bytes, string extension)
    {
        var dir = Path.Combine(Env.ContentRootPath, "uploads");
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}.{extension}";
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
        return $"/uploads/{name}";
    }

    private static async Task<string> UploadAsync(HttpClient client, byte[] bytes, string name, string type)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        var res = await client.PostAsync("/api/admin/uploads", new MultipartFormDataContent { { content, "file", name } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<UploadedFileDto>())!.Url;
    }

    private async Task<AppDbContext> FreshAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task EnableAsync(
        AppDbContext db, string key, string text, List<string> images, bool requiresApproval = false)
    {
        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == key);
        rule.Enabled = true;
        rule.Text = text;
        rule.RequiresApproval = requiresApproval;
        rule.ImageUrls = images;
        await db.SaveChangesAsync();
    }

    private static Student Pupil(string fullName, string className, string birthDate)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998900000001");
        s.Id = Guid.NewGuid().ToString();
        s.BirthDate = birthDate;
        s.FirstName = "";
        return s;
    }

    private TelegramService ConfiguredTelegram(HttpMessageHandler handler)
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
