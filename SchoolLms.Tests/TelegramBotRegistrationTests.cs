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
/// Botga telefon ulashish → <c>TelegramRegistrations</c> (ota-onaning yagona Telegram kanali:
/// kvitansiya, intizom, shartnoma, e'lon).
///
/// <para>
/// <b>Eng muhim tekshiruv — begona kontakt.</b> Boshqa odamning kontakt kartasini yuborgan
/// kishi o'sha ota-onaning xabarlarini olib qolmasligi kerak: faqat <c>contact.user_id ==
/// from.id</c> qabul qilinadi. Hech narsa tarmoqqa chiqmaydi — javoblar soxta
/// <see cref="HttpMessageHandler"/> da qoladi (<c>DisciplineParentNotifyTests</c> naqshi).
/// </para>
/// <para>
/// Yaratilgan o'quvchilar test oxirida O'CHIRILADI: baza butun to'plam uchun umumiy va
/// ba'zi hisobotlar o'quvchi soniga chegara qo'yadi (<c>MaxArrearsStudents</c>).
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
    public async Task Oz_raqamini_ulashgan_ota_ona_royxatdan_otadi_va_tugma_yopiladi()
    {
        var phone = UniquePhone();
        var student = await SeedStudentAsync("Bot Ota-ona", phone);
        var handler = new RecordingHandler();
        const long chat = 7_100_001;

        await Bot(handler).HandleUpdateAsync(ContactUpdate(chat, owner: chat, phone), default);

        Assert.True(await RegisteredAsync(student, chat));
        var reply = Assert.Single(handler.Requests);
        Assert.Contains("Ro'yxatdan o'tdingiz", reply.GetProperty("text").GetString());
        Assert.Contains("Bot Ota-ona", reply.GetProperty("text").GetString());
        Assert.True(reply.GetProperty("reply_markup").GetProperty("remove_keyboard").GetBoolean());
    }

    [Fact]
    public async Task Begona_kontakt_kartasi_royxatga_olinmaydi()
    {
        var phone = UniquePhone();
        var student = await SeedStudentAsync("Bot Begona", phone);
        var handler = new RecordingHandler();
        const long chat = 7_100_002;

        await Bot(handler).HandleUpdateAsync(ContactUpdate(chat, owner: 7_100_999, phone), default);

        Assert.False(await RegisteredAsync(student, chat));
        var reply = Assert.Single(handler.Requests);
        Assert.Contains("o'zingizning raqamingizni", reply.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Telefon_kitobidagi_kartada_user_id_yoq_qabul_qilinmaydi()
    {
        var phone = UniquePhone();
        var student = await SeedStudentAsync("Bot Kitob", phone);
        var handler = new RecordingHandler();
        const long chat = 7_100_003;

        await Bot(handler).HandleUpdateAsync(ContactUpdate(chat, owner: null, phone), default);

        Assert.False(await RegisteredAsync(student, chat));
    }

    [Fact]
    public async Task Arxivdagi_oquvchi_ota_onani_royxatga_olmaydi()
    {
        var phone = UniquePhone();
        var student = await SeedStudentAsync("Bot Arxiv", phone, archived: true);
        var handler = new RecordingHandler();
        const long chat = 7_100_004;

        await Bot(handler).HandleUpdateAsync(ContactUpdate(chat, owner: chat, phone), default);

        Assert.False(await RegisteredAsync(student, chat));
        var reply = Assert.Single(handler.Requests);
        Assert.Contains("mos kelmadi", reply.GetProperty("text").GetString());
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
