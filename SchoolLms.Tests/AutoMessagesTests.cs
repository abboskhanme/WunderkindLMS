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
/// Automatic Telegram messages to parents (docs/modules/auto-messages.md, 2026-10-13): every rule
/// ships switched off; a switched-on rule runs once per period and a parent chat is never messaged
/// twice for the same rule and period.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AutoMessagesTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Url = "/api/admin/auto-messages";
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private readonly List<string> _connectionStrings = [];

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
    public async Task Qoidalar_ochiq_emas_saqlash_tekshiriladi_va_ruxsat_bilan()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var rules = await admin.GetFromJsonAsync<JsonElement>(Url);
        // The payment rule (2026-10-15) ships off like the others.
        Assert.Equal(["absence", "invoice", "debt", "birthday", "payment"],
            rules.EnumerateArray().Select(r => r.GetProperty("key").GetString()!));
        Assert.All(rules.EnumerateArray(), r => Assert.False(r.GetProperty("enabled").GetBoolean()));
        var debt = rules.EnumerateArray().Single(r => r.GetProperty("key").GetString() == "debt");
        Assert.True(debt.GetProperty("monthly").GetBoolean());
        Assert.Contains(debt.GetProperty("tokens").EnumerateArray(), t => t.GetProperty("token").GetString() == "{qarzdorlik}");
        // The seeded text is the service's default, word for word.
        Assert.Equal(AutoMessageService.DefaultText["debt"], debt.GetProperty("text").GetString());

        AutoMessageRule original = null!;
        await fixture.Api.WithDbAsync(async db => original = await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "birthday"));
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Url}/birthday",
                new { enabled = true, sendTime = "25:00", dayOfMonth = 1, minAmount = 0, text = "Tabrik" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Url}/birthday",
                new { enabled = true, sendTime = "09:00", dayOfMonth = 1, minAmount = 0, text = "  " })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Url}/debt",
                new { enabled = true, sendTime = "09:00", dayOfMonth = 31, minAmount = 0, text = "Qarz" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{Url}/sms",
                new { enabled = true, sendTime = "09:00", dayOfMonth = 1, minAmount = 0, text = "x" })).StatusCode);

            var ok = await admin.PutAsJsonAsync($"{Url}/birthday",
                new { enabled = true, sendTime = "09:30", dayOfMonth = 1, minAmount = 0, text = "Tabrik, {ism}!" });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            await fixture.Api.WithDbAsync(async db =>
            {
                var saved = await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "birthday");
                Assert.Equal((true, "09:30", "Tabrik, {ism}!"), (saved.Enabled, saved.SendTime, saved.Text));
                Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == "AutoMessageRule" && a.EntityId == "birthday"));
            });

            var preview = await admin.PostAsJsonAsync($"{Url}/birthday/preview", new { text = "Salom, {ism}" });
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Url}/log?rule=birthday&from=2026-10-01&to=2026-10-31")).StatusCode);

            using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync($"{Url}/birthday",
                new { enabled = false, sendTime = "09:00", dayOfMonth = 1, minAmount = 0, text = "x" })).StatusCode);
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                var r = await db.AutoMessageRules.SingleAsync(x => x.Key == "birthday");
                (r.Enabled, r.SendTime, r.Text) = (original.Enabled, original.SendTime, original.Text);
                await db.SaveChangesAsync();
            });
        }
    }

    // =====================================================================
    //  Scheduling
    // =====================================================================

    [Theory]
    [InlineData("birthday", 1, "2026-10-13 07:59", null)]
    [InlineData("birthday", 1, "2026-10-13 08:00", "2026-10-13")]
    [InlineData("debt", 10, "2026-10-09 23:00", null)]
    [InlineData("debt", 10, "2026-10-10 07:00", null)]
    [InlineData("debt", 10, "2026-10-10 08:00", "2026-10")]
    [InlineData("debt", 10, "2026-10-12 06:00", "2026-10")]
    [InlineData("debt", 10, "2026-10-13 09:00", null)]
    public void Qachon_yuradi(string key, int day, string now, string? expected)
    {
        var rule = new AutoMessageRule { Key = key, SendTime = "08:00", DayOfMonth = day };
        var at = DateTime.ParseExact(now, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, AutoMessageService.DuePeriod(rule, at));
    }

    [Fact]
    public void Bosh_qiymatli_qator_tushib_qoladi_qiymatlar_ekranlanadi()
    {
        var text = AutoMessageService.Render("**Davomat**\n{fish} kelmadi.\nSabab: {sabab}",
            new Dictionary<string, string> { ["{fish}"] = "Ali <Vali>", ["{sabab}"] = "" });
        Assert.Equal("<b>Davomat</b>\nAli &lt;Vali&gt; kelmadi.", text);
    }

    // =====================================================================
    //  Rules
    // =====================================================================

    [Fact]
    public async Task Tugilgan_kun_bir_marta_ulanmaganlar_jurnalda()
    {
        var db = await FreshAsync("auto_birthday");
        var now = new DateTime(2026, 10, 13, 9, 0, 0);
        var ali = Pupil("Aliyev Ali Valiyevich", "5-A", "2015-10-13");
        var bek = Pupil("Bekov Bek", "5-A", "2014-10-13");           // no parent on Telegram
        var other = Pupil("Boshqa Kun", "5-A", "2015-10-14");
        var gone = Pupil("Arxiv Bola", "5-A", "2015-10-13");
        gone.IsArchived = true;
        db.Students.AddRange(ali, bek, other, gone);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 501, ParentName = "Karim aka" },
            new TelegramRegistration { StudentId = ali.Id, ChatId = 502, ParentName = "Malika opa" },
            new TelegramRegistration { StudentId = gone.Id, ChatId = 503 });
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));

        // Switched off: nothing.
        Assert.Empty(await service.RunDueAsync(now));
        Assert.Empty(handler.Texts);

        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == "birthday");
        rule.Enabled = true;
        rule.Text = "Bugun {ism}ning tug'ilgan kuni, {ota_ona}!";
        await db.SaveChangesAsync();

        Assert.Empty(await service.RunDueAsync(now.Date.AddHours(7)));   // before 08:00
        var run = Assert.Single(await service.RunDueAsync(now));
        Assert.Equal((2, 2, 0, 0, 1), (run.Pupils, run.Sent, run.AlreadySent, run.Failed, run.NoTelegram));
        Assert.Equal(
            ["Bugun Alining tug'ilgan kuni, Karim aka!", "Bugun Alining tug'ilgan kuni, Malika opa!"],
            handler.Texts.Order());
        Assert.Equal("2026-10-13", (await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "birthday")).LastPeriod);

        // Later the same day: the period is closed.
        Assert.Empty(await service.RunDueAsync(now.AddHours(3)));
        // Run by hand again: the log stops a second message to the same chat.
        var again = await service.RunAsync(rule, "2026-10-13");
        Assert.Equal((0, 2), (again.Sent, again.AlreadySent));
        Assert.Equal(2, handler.Texts.Count);

        var log = await db.AutoMessageLog.AsNoTracking().ToListAsync();
        Assert.Equal(3, log.Count);
        Assert.Single(log, l => l.StudentId == bek.Id && l.ChatId == null && l.Status == BroadcastDelivery.NoTelegram);
        Assert.Equal(2, log.Count(l => l.StudentId == ali.Id && l.Status == BroadcastDelivery.Sent));
    }

    [Fact]
    public async Task Qarz_eslatmasi_muddati_otganlarga_eng_kam_summadan()
    {
        var db = await FreshAsync("auto_debt");
        var late = Pupil("Kechikkan Ota", "7-B", "2013-01-01");
        var small = Pupil("Kichik Qarz", "7-B", "2013-01-01");
        var notDue = Pupil("Muddati Kelmagan", "7-B", "2013-01-01");
        db.Students.AddRange(late, small, notDue);
        var today = AppClock.Today;
        db.Invoices.AddRange(
            Invoice(late.Id, new DateOnly(2026, 8, 1), 1_200_000m, new DateOnly(2026, 8, 10)),
            Invoice(late.Id, new DateOnly(2026, 9, 1), 1_300_000m, new DateOnly(2026, 9, 10)),
            Invoice(small.Id, new DateOnly(2026, 9, 1), 50_000m, new DateOnly(2026, 9, 10)),
            Invoice(notDue.Id, new DateOnly(today.Year, today.Month, 1), 2_000_000m, today.AddDays(5)));
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = late.Id, ChatId = 701 },
            new TelegramRegistration { StudentId = small.Id, ChatId = 702 },
            new TelegramRegistration { StudentId = notDue.Id, ChatId = 703 });
        await db.SaveChangesAsync();

        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == "debt");
        rule.MinAmount = 100_000m;
        rule.Text = "{fish}: {qarzdorlik} so'm ({oylar})";
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var run = await new AutoMessageService(db, ConfiguredTelegram(handler)).RunAsync(rule, "2026-10");
        Assert.Equal((1, 1), (run.Pupils, run.Sent));
        Assert.Equal("Kechikkan Ota: 2 500 000 so'm (Avgust, Sentabr)", Assert.Single(handler.Texts));
    }

    [Fact]
    public async Task Oylik_hisob_xabari_hisob_bolmasa_davr_ochiq_qoladi()
    {
        var db = await FreshAsync("auto_invoice");
        var ali = Pupil("Aliyev Ali", "3-A", "2017-05-05");
        db.Students.Add(ali);
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = ali.Id, ChatId = 801 });
        await db.SaveChangesAsync();

        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == "invoice");
        rule.Text = "{oy}: {summa} so'm, jami {qarzdorlik} so'm";
        await db.SaveChangesAsync();
        var handler = new RecordingHandler();
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));

        var early = await service.RunAsync(rule, "2026-11");
        Assert.False(early.Closed);
        Assert.Null((await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "invoice")).LastPeriod);
        Assert.Empty(handler.Texts);

        var september = Invoice(ali.Id, new DateOnly(2026, 9, 1), 1_000_000m, new DateOnly(2026, 9, 10));
        var november = Invoice(ali.Id, new DateOnly(2026, 11, 1), 1_500_000m, new DateOnly(2026, 11, 10));
        november.Discount = 100_000m;
        db.Invoices.AddRange(september, november);
        await db.SaveChangesAsync();

        var run = await service.RunAsync(rule, "2026-11");
        Assert.True(run.Closed);
        Assert.Equal("Noyabr: 1 400 000 so'm, jami 2 400 000 so'm", Assert.Single(handler.Texts));
    }

    [Fact]
    public async Task Yoqlik_xabari_tugma_bilan_bir_marta()
    {
        var db = await FreshAsync("auto_absence");
        const string monday = "2026-01-12";
        var cls = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var chemistry = new Subject { Name = "Kimyo" };
        db.Classes.Add(cls);
        db.Subjects.Add(chemistry);
        var ali = Pupil("Aliyev Ali", "5-A", "2015-01-01");
        var dil = Pupil("Dilova Dil", "5-A", "2015-01-01");
        db.Students.AddRange(ali, dil);
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        db.AbsenceReasons.Add(unexcused);
        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = "2026-01-10", EndDate = "2026-03-20" });
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = ali.Id, ChatId = 901, ParentName = "Ota" });
        await db.SaveChangesAsync();

        var week = ScheduleMath.GetQuarterWeeks("2026-01-10", "2026-03-20")
            .First(w => string.CompareOrdinal(monday, w.StartISO) >= 0 && string.CompareOrdinal(monday, w.EndISO) <= 0);
        var tpl = new ScheduleTemplate
        {
            ClassId = cls.Id, Name = "Asosiy",
            Lessons = [new ScheduleLesson { Day = 0, Period = 1, SubjectId = chemistry.Id }],
        };
        db.ScheduleTemplates.Add(tpl);
        await db.SaveChangesAsync();
        db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = 3, Week = week.Week, TemplateId = tpl.Id });
        db.DailyAttendanceMarks.Add(new DailyAttendanceMark
        {
            ClassId = cls.Id, SubjectId = chemistry.Id, Date = monday, Period = 1, MarkedBy = "nurse", MarkedAt = AppClock.NowInstant,
        });
        db.JournalEntries.Add(new JournalEntry
        {
            ClassId = cls.Id, SubjectId = chemistry.Id, StudentId = ali.Id, Date = monday, Period = 1, Quarter = 3, ReasonId = unexcused.Id,
        });
        await db.SaveChangesAsync();

        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == "absence");
        var handler = new RecordingHandler();
        var telegram = ConfiguredTelegram(handler);
        var run = await new AutoMessageService(db, telegram).RunAsync(rule, monday);
        Assert.Equal((1, 1), (run.Pupils, run.Sent));
        var text = Assert.Single(handler.Texts);
        Assert.Contains("Aliyev Ali (5-A) 12.01.2026 kuni maktabga kelmadi", text);
        Assert.Contains("Sabab: Sababsiz", text);

        // The nurse's button now sees the pupil as told and sends nothing.
        var notice = await db.AbsenceNotices.AsNoTracking().SingleAsync();
        Assert.Equal((ali.Id, AutoMessageService.SystemUser, unexcused.Id), (notice.StudentId, notice.SentBy, notice.ReasonId));
        var notifier = new DailyAbsenceNotifier(db, telegram, fixture.Api.Services.GetRequiredService<ILogger<DailyAbsenceNotifier>>());
        var button = await notifier.SendAsync(monday, "nurse");
        Assert.Equal((0, 1), (button.Sent, button.AlreadySent));
        Assert.Single(handler.Texts);
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

    private static Student Pupil(string fullName, string className, string birthDate)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998900000001");
        s.Id = Guid.NewGuid().ToString();
        s.BirthDate = birthDate;
        s.FirstName = "";
        return s;
    }

    private static Invoice Invoice(string studentId, DateOnly month, decimal amount, DateOnly due) => new()
    {
        StudentId = studentId, CategoryId = TuitionCategory, PeriodMonth = month, Amount = amount,
        Discount = 0m, DueOn = due, Status = InvoiceStatus.Open, CreatedAt = AppClock.NowInstant,
    };

    private TelegramService ConfiguredTelegram(HttpMessageHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new Factory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>Answers every send with 200 and keeps the texts — nothing leaves the machine.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Texts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Texts.Add(doc.RootElement.GetProperty("text").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
