using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  Direktorga oylik eslatma — F6.01 (finance-parity.md §2.6.3).
// ===========================================================================
//
//  Naqsh: `DisciplineParentNotifyTests` (soxta `HttpMessageHandler`, hech narsa
//  tarmoqqa chiqmaydi) + `InvoiceServiceTests.Fon_xizmati_bir_yurishda_oylarni_hisoblaydi`
//  (`ServiceCollection` orqali fon xizmatiga o'z bog'liqliklarini beradi,
//  `RunOnceAsync` fon tsiklidan MUSTAQIL chaqiriladi).
//
//  Har test O'Z BAZASIDA — direktor/xodim/shablon ro'yxati boshqa testlarning
//  ma'lumotlariga bog'liq bo'lib qolmasin.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class ExpenseTemplateReminderServiceTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Bugun_kutilayotgan_shablonlar_direktorga_bitta_xabarda_yuboriladi()
    {
        await using var db = await NewDbAsync();
        var today = AppClock.Today;
        var directorTeacherId = await SeedDirectorAsync(db, chatId: 5001);

        await AddTemplateAsync(db, "Internet", "utilities", 350_000m, today.Day, isActive: true);
        await AddTemplateAsync(db, "Ijara", "rent", 4_700_000m, today.Day, isActive: true);
        // Boshqa kun — bugun ko'rinmasligi kerak.
        var otherDay = today.Day == 1 ? 2 : 1;
        await AddTemplateAsync(db, "Boshqa kun", "other", 100_000m, otherDay, isActive: true);
        // Faolsiz — bugun bo'lsa ham ko'rinmasligi kerak.
        await AddTemplateAsync(db, "Faolsiz", "supplies", 999_000m, today.Day, isActive: false);

        var handler = new RecordingHandler();
        var (provider, _) = BuildProvider(db, handler, configured: true);
        var job = new ExpenseTemplateReminderService(provider, NullLogger<ExpenseTemplateReminderService>.Instance);

        var result = await job.RunOnceAsync();

        Assert.Equal(2, result.DueCount);
        Assert.Equal(1, result.ChatsNotified);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(5001L, sent.GetProperty("chat_id").GetInt64());
        var text = sent.GetProperty("text").GetString()!;
        Assert.Contains("Internet", text, StringComparison.Ordinal);
        Assert.Contains("Ijara", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Boshqa kun", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Faolsiz", text, StringComparison.Ordinal);
        Assert.Contains("5 050 000", text, StringComparison.Ordinal); // jami

        Assert.NotEqual(Guid.Empty.ToString(), directorTeacherId);
    }

    [Fact]
    public async Task Hech_narsa_kutilmasa_hech_kimga_yuborilmaydi()
    {
        await using var db = await NewDbAsync();
        var today = AppClock.Today;
        await SeedDirectorAsync(db, chatId: 5002);
        var otherDay = today.Day == 1 ? 2 : 1;
        await AddTemplateAsync(db, "Boshqa kun", "other", 100_000m, otherDay, isActive: true);

        var handler = new RecordingHandler();
        var (provider, _) = BuildProvider(db, handler, configured: true);
        var job = new ExpenseTemplateReminderService(provider, NullLogger<ExpenseTemplateReminderService>.Instance);

        var result = await job.RunOnceAsync();

        Assert.Equal(0, result.DueCount);
        Assert.Equal(0, result.ChatsNotified);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Direktor_botga_ulanmagan_bolsa_jim_hech_narsa_yiqilmaydi()
    {
        await using var db = await NewDbAsync();
        var today = AppClock.Today;
        // Direktor bor, lekin botga ULANMAGAN (TelegramRegistration yo'q).
        await SeedDirectorAsync(db, chatId: null);
        await AddTemplateAsync(db, "Internet", "utilities", 100_000m, today.Day, isActive: true);

        var handler = new RecordingHandler();
        var (provider, _) = BuildProvider(db, handler, configured: true);
        var job = new ExpenseTemplateReminderService(provider, NullLogger<ExpenseTemplateReminderService>.Instance);

        var result = await job.RunOnceAsync();

        Assert.Equal(1, result.DueCount);
        Assert.Equal(0, result.ChatsNotified);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Bot_sozlanmagan_bolsa_chat_umuman_qidirilmaydi()
    {
        await using var db = await NewDbAsync();
        var today = AppClock.Today;
        await SeedDirectorAsync(db, chatId: 5003);
        await AddTemplateAsync(db, "Internet", "utilities", 100_000m, today.Day, isActive: true);

        var handler = new RecordingHandler();
        var (provider, _) = BuildProvider(db, handler, configured: false);
        var job = new ExpenseTemplateReminderService(provider, NullLogger<ExpenseTemplateReminderService>.Instance);

        var result = await job.RunOnceAsync();

        Assert.Equal(1, result.DueCount);
        Assert.Equal(0, result.ChatsNotified);
        Assert.Empty(handler.Requests);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<AppDbContext> NewDbAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("exptplreminder");
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>
    /// Direktor: `superadmin` rolli `AppUser` + unga bog'langan `Teacher`
    /// (xuddi shu yo'l orqali xodim botga ulanadi — fayl boshidagi izoh).
    /// <paramref name="chatId"/> null bo'lsa — botga ULANMAGAN direktor.
    /// </summary>
    private static async Task<string> SeedDirectorAsync(AppDbContext db, long? chatId)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var user = new AppUser { FullName = "Direktor", Role = Roles.SuperAdmin, Email = $"dir.{tag}@test.local" };
        var teacher = new Teacher { FullName = "Direktor", Phone = $"+99890{tag}", UserId = user.Id };
        db.Users.Add(user);
        db.Teachers.Add(teacher);
        if (chatId is { } id)
            db.TelegramRegistrations.Add(new TelegramRegistration
            {
                TeacherId = teacher.Id, StudentId = "", ChatId = id, ParentName = "Direktor",
            });
        await db.SaveChangesAsync();
        return teacher.Id;
    }

    private static async Task AddTemplateAsync(
        AppDbContext db, string name, string category, decimal amount, int dayOfMonth, bool isActive)
    {
        db.ExpenseTemplates.Add(new ExpenseTemplate
        {
            Name = name, Category = category, Amount = amount, DayOfMonth = (short)dayOfMonth,
            IsActive = isActive, CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// `ExpenseTemplateReminderService` o'z ichida scope ochib
    /// `IAppDbContext`/`TelegramService` ni SHU provaydan oladi
    /// (`InvoiceServiceTests.Fon_xizmati...` bilan bir xil naqsh).
    /// </summary>
    private static (IServiceProvider Provider, RecordingHandler Handler) BuildProvider(
        AppDbContext db, RecordingHandler handler, bool configured)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));
        services.AddSingleton(sp =>
        {
            var telegram = new TelegramService(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                NullLogger<TelegramService>.Instance);
            if (configured) telegram.Set("test-token", "test_bot");
            return telegram;
        });
        var provider = services.BuildServiceProvider();
        return (provider, handler);
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
