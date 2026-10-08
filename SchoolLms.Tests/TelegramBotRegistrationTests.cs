using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Services;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Phone sharing is gone from the bot (client, 2026-10-08: "telefon raqam ulashish ... kerakmas, botdan
/// buni butunlay olib tashla, bu tushunmovchiliklarga xatolarga olib keladi"). The school's notifications
/// follow the accounts signed in on a chat (<c>TelegramSavedAccounts.SyncRegistrationsAsync</c>, tested in
/// <c>TelegramNotificationSyncTests</c>); a contact sent through an old keyboard is not stored.
/// Nothing goes to the network — replies stay in a fake <see cref="HttpMessageHandler"/>.
/// <para>
/// Pupils made here are DELETED at the end: the database is shared by the whole suite and some
/// reports cap the number of pupils (<c>MaxArrearsStudents</c>).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TelegramBotRegistrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _students = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_students.Count == 0) return;
        await fixture.Api.WithDbAsync(async db =>
        {
            await db.TelegramRegistrations.Where(r => _students.Contains(r.StudentId)).ExecuteDeleteAsync();
            await db.Students.Where(s => _students.Contains(s.Id)).ExecuteDeleteAsync();
        });
    }

    [Fact]
    public async Task Kontakt_saqlanmaydi_telefon_kerakmasligi_aytiladi_va_eski_klaviatura_yopiladi()
    {
        var phone = UniquePhone();
        var student = await SeedStudentAsync("Bot Ota-ona", phone);
        var handler = new RecordingHandler();
        const long chat = 7_100_001;

        await Bot(handler).HandleUpdateAsync(ContactUpdate(chat, owner: chat, phone), default);

        // The phone matched a pupil — still nothing is stored: notifications come with the login now.
        Assert.False(await RegisteredAsync(student, chat));
        var first = handler.Requests[0];
        Assert.Contains("Telefon raqam endi kerak emas", first.GetProperty("text").GetString());
        Assert.True(first.GetProperty("reply_markup").GetProperty("remove_keyboard").GetBoolean());
        Assert.DoesNotContain(handler.Requests, r => r.ToString().Contains("request_contact"));
    }

    [Fact]
    public async Task Start_telefon_soramaydi()
    {
        var handler = new RecordingHandler();
        const long chat = 7_100_002;

        await Bot(handler).HandleUpdateAsync(TextUpdate(chat, "/start"), default);

        Assert.NotEmpty(handler.Requests);
        Assert.DoesNotContain(handler.Requests, r => r.ToString().Contains("request_contact"));
        Assert.DoesNotContain(handler.Requests, r => r.GetProperty("text").GetString()!.Contains("telefon raqamingiz", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- yordamchilar

    private static string UniquePhone() => "+99877" + Random.Shared.Next(1_000_000, 9_999_999);

    private async Task<string> SeedStudentAsync(string name, string phone, bool archived = false)
    {
        var student = GeneralSettingsFlagsTests.NewStudent(name, "", phone);
        student.IsArchived = archived;
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        _students.Add(student.Id);
        return student.Id;
    }

    private async Task<bool> RegisteredAsync(string studentId, long chat)
    {
        var found = false;
        await fixture.Api.WithDbAsync(async db =>
            found = await db.TelegramRegistrations.AnyAsync(r => r.StudentId == studentId && r.ChatId == chat));
        return found;
    }

    private static JsonElement TextUpdate(long chat, string text)
    {
        var json = JsonSerializer.Serialize(new
        {
            update_id = 1,
            message = new
            {
                message_id = 1,
                chat = new { id = chat, type = "private" },
                from = new { id = chat, first_name = "Ota" },
                text,
            },
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement ContactUpdate(long chat, long? owner, string phone)
    {
        var contact = owner is { } id
            ? (object)new { phone_number = phone, first_name = "Ota", user_id = id }
            : new { phone_number = phone, first_name = "Ota" };
        var json = JsonSerializer.Serialize(new
        {
            update_id = 1,
            message = new
            {
                message_id = 1,
                chat = new { id = chat, type = "private" },
                from = new { id = chat, first_name = "Ota" },
                contact,
            },
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>Tokeni bor ("sozlangan"), lekin har HTTP so'rov <paramref name="handler"/> da qoladi.</summary>
    private TelegramBotService Bot(RecordingHandler handler)
    {
        var telegram = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        telegram.Set("test-token", "test_bot");
        return new TelegramBotService(
            fixture.Api.Services, telegram, fixture.Api.Services.GetRequiredService<ILogger<TelegramBotService>>());
    }

    /// <summary>Har so'rov tanasini yozib oladi va 200 qaytaradi — tarmoqqa hech narsa chiqmaydi.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<JsonElement> _requests = [];

        public IReadOnlyList<JsonElement> Requests
        {
            get { lock (_requests) return [.. _requests]; }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            lock (_requests) _requests.Add(json.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
