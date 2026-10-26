using System.Globalization;
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
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Templates staff create themselves (2026-10-26: "kelajakda qo'shimcha avtomatik habar yaratish kerak
/// bo'ladigan bo'lsa kod kiritmasdan"). A template is a rule "c_…" that goes by the calendar — once,
/// daily, on chosen week days or monthly — to every pupil or to the chosen classes; it ships switched
/// off, and deleting it archives it so the history keeps its name. Every Telegram call is recorded.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CustomAutoMessageTemplatesTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Url = "/api/admin/auto-messages";
    // Tuesday.
    private static readonly DateTime Tuesday = new(2026, 10, 13, 9, 0, 0);
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  Scheduling
    // =====================================================================

    [Theory]
    [InlineData("once", "2026-10-13 07:59", null)]
    [InlineData("once", "2026-10-13 08:00", "2026-10-13")]
    [InlineData("once", "2026-10-14 09:00", null)]
    [InlineData("daily", "2026-10-14 08:30", "2026-10-14")]
    [InlineData("weekly", "2026-10-13 08:00", "2026-10-13")]   // Tuesday
    [InlineData("weekly", "2026-10-14 08:00", null)]           // Wednesday
    [InlineData("weekly", "2026-10-18 08:00", "2026-10-18")]   // Sunday = 7
    [InlineData("monthly", "2026-10-05 08:00", "2026-10")]
    [InlineData("monthly", "2026-10-04 23:00", null)]
    [InlineData("monthly", "2026-10-08 08:00", null)]
    public void Shablon_qachon_yuradi(string schedule, string now, string? expected)
    {
        var rule = new AutoMessageRule
        {
            Key = "c_0000abcd", Schedule = schedule, SendTime = "08:00", DayOfMonth = 5,
            SendDate = "2026-10-13", WeekDays = [2, 7],
        };
        var at = DateTime.ParseExact(now, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal(expected, AutoMessageService.DuePeriod(rule, at));
    }

    // =====================================================================
    //  API
    // =====================================================================

    [Fact]
    public async Task Shablon_yaratiladi_tekshiriladi_ochiriladi_tarixda_nomi_qoladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var created = new List<string>();
        object Body(string? title = "Majlis eslatmasi", string? schedule = "weekly", int[]? weekDays = null,
            string[]? classIds = null, string text = "Hurmatli {ota_ona}, ertaga majlis.") => new
        {
            enabled = true, sendTime = "18:00", dayOfMonth = 1, minAmount = 0, text, title, schedule,
            weekDays = weekDays ?? [1, 4], classIds = classIds ?? [],
        };
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, Body(title: " "))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, Body(schedule: null))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, Body(schedule: "yearly"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, Body(weekDays: []))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, Body(weekDays: [8]))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, Body(classIds: ["no-such-class"]))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Url, Body(text: " "))).StatusCode);

            var res = await admin.PostAsJsonAsync(Url, Body());
            var rule = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(res.IsSuccessStatusCode, rule.ToString());
            var key = rule.GetProperty("key").GetString()!;
            created.Add(key);
            Assert.Matches("^c_[0-9a-f]{8}$", key);
            // Ships switched off whatever was sent.
            Assert.False(rule.GetProperty("enabled").GetBoolean());
            Assert.True(rule.GetProperty("custom").GetBoolean());
            Assert.Equal("Majlis eslatmasi", rule.GetProperty("title").GetString());
            Assert.Equal([1, 4], rule.GetProperty("weekDays").EnumerateArray().Select(d => d.GetInt32()));
            Assert.Contains(rule.GetProperty("tokens").EnumerateArray(), t => t.GetProperty("token").GetString() == "{sana}");

            // Listed after the built-in rules.
            var keys = (await admin.GetFromJsonAsync<JsonElement>(Url)).EnumerateArray()
                .Select(r => r.GetProperty("key").GetString()).ToList();
            Assert.Equal(["absence", "invoice", "debt", "birthday", "payment"], keys.Take(5));
            Assert.Equal(key, keys.Last());

            // Monthly now: the day is kept, the week days dropped.
            var monthly = await admin.PutAsJsonAsync($"{Url}/{key}", new
            {
                enabled = true, sendTime = "08:00", dayOfMonth = 5, minAmount = 0, text = "Oylik eslatma", schedule = "monthly",
            });
            var saved = await monthly.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(monthly.IsSuccessStatusCode, saved.ToString());
            Assert.Equal(("monthly", 5, true, "Majlis eslatmasi"),
                (saved.GetProperty("schedule").GetString(), saved.GetProperty("dayOfMonth").GetInt32(),
                 saved.GetProperty("monthly").GetBoolean(), saved.GetProperty("title").GetString()));
            Assert.Empty(saved.GetProperty("weekDays").EnumerateArray());
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"{Url}/{key}/preview", new { text = (string?)null })).StatusCode);

            // Built-in rules cannot be deleted; a template is archived and its open runs stop.
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"{Url}/birthday")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{key}")).StatusCode);
            await fixture.Api.WithDbAsync(async db =>
            {
                var row = await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == key);
                Assert.True(row.IsArchived);
                Assert.False(row.Enabled);
                Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == "AutoMessageRule" && a.EntityId == key && a.Action == "delete"));
            });
            var archived = (await admin.GetFromJsonAsync<JsonElement>(Url)).EnumerateArray()
                .Single(r => r.GetProperty("key").GetString() == key);
            Assert.True(archived.GetProperty("archived").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{Url}/{key}", Body())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Url}/{key}")).StatusCode);

            using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync(Url, Body())).StatusCode);
        }
        finally
        {
            // The shared database keeps exactly the built-in rules for the other tests.
            await fixture.Api.WithDbAsync(async db =>
            {
                await db.AutoMessageRules.Where(r => created.Contains(r.Key)).ExecuteDeleteAsync();
            });
        }
    }

    // =====================================================================
    //  Sending
    // =====================================================================

    [Fact]
    public async Task Haftalik_shablon_tanlangan_sinfga_shu_kunlari_ketadi()
    {
        var db = await FreshAsync("am_tpl_weekly");
        var fiveA = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var sixB = new SchoolClass { Name = "6-B", Grade = 6, Language = "uz" };
        db.Classes.AddRange(fiveA, sixB);
        var ali = Pupil("Aliyev Ali Valiyevich", "5-A");
        var bek = Pupil("Bekov Bek", "5-A");                 // no parent on Telegram
        var gone = Pupil("Arxiv Bola", "5-A");
        gone.IsArchived = true;
        var other = Pupil("Boshqa Sinf", "6-B");
        db.Students.AddRange(ali, bek, gone, other);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 701, ParentName = "Karim aka" },
            new TelegramRegistration { StudentId = gone.Id, ChatId = 702 },
            new TelegramRegistration { StudentId = other.Id, ChatId = 703 });
        db.AutoMessageRules.Add(new AutoMessageRule
        {
            Key = "c_0000a001", Title = "Kutubxona kuni", Schedule = AutoMessageSchedule.Weekly, WeekDays = [2],
            ClassIds = [fiveA.Id], Enabled = true, SendTime = "08:00",
            Text = "{ota_ona}, {sinf} — {ism} bugun ({sana}) kitobini olib kelsin.",
            CreatedAt = AppClock.NowInstant, UpdatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));

        var run = Assert.Single(await service.RunDueAsync(Tuesday));
        Assert.Equal(("c_0000a001", "2026-10-13", 2, 1, 1), (run.Key, run.Period, run.Pupils, run.Sent, run.NoTelegram));
        Assert.Equal("Karim aka, 5-A — Ali bugun (13.10.2026) kitobini olib kelsin.", Assert.Single(handler.Texts));

        // Wednesday is not one of its days; the next Tuesday is.
        Assert.Empty(await service.RunDueAsync(Tuesday.AddDays(1)));
        Assert.Single(await service.RunDueAsync(Tuesday.AddDays(7)));
        Assert.Equal(2, handler.Texts.Count);

        // Archived (deleted on the screen): never runs again.
        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == "c_0000a001");
        rule.IsArchived = true;
        await db.SaveChangesAsync();
        Assert.Empty(await service.RunDueAsync(Tuesday.AddDays(14)));
        Assert.Equal(2, handler.Texts.Count);
    }

    [Fact]
    public async Task Bir_martalik_shablon_hammaga_bir_marta_ochirilgan_sinf_hech_kimga()
    {
        var db = await FreshAsync("am_tpl_once");
        var ali = Pupil("Aliyev Ali", "5-A");
        var vali = Pupil("Valiyev Vali", "7-C");
        db.Students.AddRange(ali, vali);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 801 },
            new TelegramRegistration { StudentId = vali.Id, ChatId = 802 });
        db.AutoMessageRules.AddRange(
            new AutoMessageRule
            {
                Key = "c_0000b001", Title = "Bayram", Schedule = AutoMessageSchedule.Once, SendDate = "2026-10-13",
                Enabled = true, SendTime = "08:00", Text = "Bayram bilan, {ism}!",
                CreatedAt = AppClock.NowInstant, UpdatedAt = AppClock.NowInstant,
            },
            // Its only class no longer exists: it must reach nobody, never everyone.
            new AutoMessageRule
            {
                Key = "c_0000b002", Title = "Eski sinf", Schedule = AutoMessageSchedule.Daily, ClassIds = ["deleted-class"],
                Enabled = true, SendTime = "08:00", Text = "Faqat eski sinfga",
                CreatedAt = AppClock.NowInstant, UpdatedAt = AppClock.NowInstant,
            });
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));
        var runs = await service.RunDueAsync(Tuesday);
        Assert.Equal(2, runs.Single(r => r.Key == "c_0000b001").Sent);
        Assert.Equal(0, runs.Single(r => r.Key == "c_0000b002").Pupils);
        Assert.Equal(["Bayram bilan, Ali!", "Bayram bilan, Vali!"], handler.Texts.Order());

        Assert.DoesNotContain(await service.RunDueAsync(Tuesday.AddDays(1)), r => r.Key == "c_0000b001");
        Assert.Equal(2, handler.Texts.Count);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private async Task<AppDbContext> FreshAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static Student Pupil(string fullName, string className)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998900000001");
        s.Id = Guid.NewGuid().ToString();
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

    /// <summary>Records the text of every message; answers like Telegram.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Texts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("text", out var text)) Texts.Add(text.GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":{}}") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
