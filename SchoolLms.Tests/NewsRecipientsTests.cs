using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Who a news item's Telegram message reached (client, 2026-10-12: "Telegram: 120/150 bunda aynan
//  kimlarga borgan bormaganini bilsa bo'ladimi"): every chat by name with sent / failed, and the
//  parents and employees with no Telegram at all.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class NewsRecipientsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string News = "/api/admin/news";
    private readonly List<TestDatabase> _fresh = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var d in _fresh) NpgsqlConnection.ClearPool(new NpgsqlConnection(d.OwnerConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Har_chat_kimniki_va_yetdimi_yozib_qoyiladi_ulanmaganlar_ham()
    {
        var database = await _freshDb();
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);

        var author = User("Muallif", Roles.Admin);
        var ali = Pupil("Ali Valiyev", "7-A", "Karim Valiyev");
        var vali = Pupil("Vali Karimov", "7-A", "Salim Karimov");          // parents not on Telegram
        var gone = Pupil("Arxiv Bola", "7-A", "X");
        gone.IsArchived = true;
        var mother = User("Malika Valiyeva", "parent");
        var guardian = new Guardian { FullName = mother.FullName, UserId = mother.Id, Phone = "+998900000001", PhoneKey = "998900000001" };
        var t1 = new Teacher { FullName = "Rahimov Sardor" };
        var t2 = new Teacher { FullName = "Qodirova Nodira" };                // no Telegram
        var staffOn = User("Ergasheva Malika", Roles.Staff);
        var staffOff = User("Yusupov Ravshan", Roles.Staff);                  // no Telegram
        db.Users.AddRange(author, mother, staffOn, staffOff);
        db.Students.AddRange(ali, vali, gone);
        db.Guardians.Add(guardian);
        db.Teachers.AddRange(t1, t2);
        await db.SaveChangesAsync();

        db.StudentGuardians.Add(new StudentGuardian { StudentId = ali.Id, GuardianId = guardian.Id });
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = ali.Id, ChatId = 1001, ParentName = "Karim aka" });
        db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = 1002, UserId = mother.Id, DisplayName = "Malika" });
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = "", ChatId = 2001, TeacherId = t1.Id, ParentName = "Sardor" });
        db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = 2002, UserId = staffOn.Id, DisplayName = "Malika E." });
        var news = new NewsItem
        {
            Title = "Yig'ilish", Body = "Ertaga 15:00", ForParent = true, ForEmployee = true,
            AuthorId = author.Id, AuthorName = author.FullName, CreatedAt = AppClock.NowInstant,
        };
        db.News.Add(news);
        await db.SaveChangesAsync();

        // Telegram refuses chat 2002.
        var notifier = new NewsTelegramNotifier(db, ConfiguredTelegram(2002),
            fixture.Api.Services.GetRequiredService<ILogger<NewsTelegramNotifier>>());
        var result = await notifier.SendAsync(news);
        await db.SaveChangesAsync();

        Assert.Equal(4, result.RecipientCount);
        Assert.Equal(3, result.SentCount);

        var rows = await db.NewsTelegramRecipients.AsNoTracking().Where(r => r.NewsId == news.Id).ToListAsync();
        var byChat = rows.Where(r => r.ChatId != null).ToDictionary(r => r.ChatId!.Value);
        // Exactly the chats the message went to.
        Assert.Equal((await NewsTelegramNotifier.RecipientsAsync(db, news)).OrderBy(x => x), byChat.Keys.OrderBy(x => x));

        Assert.Equal(("parent", "Karim aka", "Ali Valiyev (7-A)", "sent"), Row(byChat[1001]));
        Assert.Equal(("parent", "Malika Valiyeva", "Ali Valiyev (7-A)", "sent"), Row(byChat[1002]));
        Assert.Equal(("employee", "Rahimov Sardor", "O'qituvchi", "sent"), Row(byChat[2001]));
        Assert.Equal(("employee", "Ergasheva Malika", "Xodim", "failed"), Row(byChat[2002]));

        var none = rows.Where(r => r.Status == BroadcastDelivery.NoTelegram).Select(Row).ToList();
        Assert.Contains(("parent", "Salim Karimov", "Vali Karimov (7-A)", "no_telegram"), none);
        Assert.Contains(("employee", "Qodirova Nodira", "O'qituvchi", "no_telegram"), none);
        Assert.Contains(("employee", "Yusupov Ravshan", "Xodim", "no_telegram"), none);
        // Reachable people and archived pupils are not "not connected".
        Assert.DoesNotContain(none, r => r.Detail is not null && (r.Detail.StartsWith("Ali") || r.Detail.StartsWith("Arxiv")));
        Assert.DoesNotContain(none, r => r.Name is "Rahimov Sardor" or "Ergasheva Malika");
        // The author is an admin with no Telegram — an employee the message missed too.
        Assert.Contains(("employee", "Muallif", "Administrator", "no_telegram"), none);
        Assert.All(rows.Where(r => r.Status == BroadcastDelivery.NoTelegram), r => Assert.Null(r.ChatId));
    }

    [Fact]
    public async Task Batafsil_muammolilar_tepada_eski_yangilikda_royxat_yoq_va_faqat_marketing()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var withList = await CreateNewsAsync(admin);
        var withoutList = await CreateNewsAsync(admin);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.NewsTelegramRecipients.AddRange(
                new NewsTelegramRecipient { NewsId = withList, ChatId = 1, Kind = "parent", Name = "Yetgan", Status = "sent" },
                new NewsTelegramRecipient { NewsId = withList, ChatId = null, Kind = "parent", Name = "Ulanmagan", Detail = "Bola (7-A)", Status = "no_telegram" },
                new NewsTelegramRecipient { NewsId = withList, ChatId = 2, Kind = "employee", Name = "Yetmagan", Status = "failed" });
            await db.SaveChangesAsync();
        });

        var dto = await admin.GetFromJsonAsync<JsonElement>($"{News}/{withList}/recipients");
        Assert.True(dto.GetProperty("hasDetails").GetBoolean());
        Assert.Equal(["Yetmagan", "Ulanmagan", "Yetgan"],
            dto.GetProperty("recipients").EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToArray());
        Assert.Equal("Bola (7-A)", dto.GetProperty("recipients")[1].GetProperty("detail").GetString());

        var empty = await admin.GetFromJsonAsync<JsonElement>($"{News}/{withoutList}/recipients");
        Assert.False(empty.GetProperty("hasDetails").GetBoolean());
        Assert.Equal(0, empty.GetProperty("recipients").GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{News}/{Guid.NewGuid()}/recipients")).StatusCode);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{News}/{withList}/recipients")).StatusCode);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static (string Kind, string Name, string? Detail, string Status) Row(NewsTelegramRecipient r) =>
        (r.Kind, r.Name, r.Detail, r.Status);

    private async Task<TestDatabase> _freshDb()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("newsrcpt");
        _fresh.Add(database);
        return database;
    }

    private static AppUser User(string fullName, string role) =>
        new() { FullName = fullName, Role = role, Email = $"{role}.{Guid.NewGuid():N}"[..20] };

    private static Student Pupil(string fullName, string className, string parent)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998900000009");
        s.Id = Guid.NewGuid().ToString();
        s.ParentFullName = parent;
        return s;
    }

    private static async Task<Guid> CreateNewsAsync(HttpClient admin)
    {
        var res = await admin.PostAsJsonAsync(News, new
        {
            title = "Yangilik " + Guid.NewGuid().ToString("N")[..6], body = "Matn", imageUrl = (string?)null, audience = new[] { "parent" },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private TelegramService ConfiguredTelegram(params long[] failFor)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new Factory(new Handler(failFor)),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>Answers every send with 200, except the chats in <paramref name="failFor"/> — nothing leaves the machine.</summary>
    private sealed class Handler(long[] failFor) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var chat = doc.RootElement.GetProperty("chat_id").GetInt64();
            return new HttpResponseMessage(failFor.Contains(chat) ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}"),
            };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
