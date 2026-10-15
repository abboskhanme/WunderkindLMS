using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Thank-you to the best, warning to the lowest after a test (client, 2026-10-15: "Oylik testni eng top va bottom
/// o'quvchilarining accountiga xabar jo'natish ... nechta o'quvchiga jo'natish sonini qo'lda kiritishli qilish").
/// Telegram is a recording fake — nothing leaves the machine.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ExamAwardsTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    private sealed record World(string ExamId, Dictionary<string, Student> Pupils);

    /// <summary>
    /// 7-A: Ali 95, Bobur 90, Dilnoza 90, Eldor 60, Farrux 40; 7-B: Gulnora 88, Hasan 70, Iroda 30.
    /// Absent Jasur (no result) and archived Kamol (99) never count. Every pupil has one chat but Bobur.
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var a = new SchoolClass { Name = "7-A", Grade = 7 };
        var b = new SchoolClass { Name = "7-B", Grade = 7 };
        db.Classes.AddRange(a, b);
        var exam = new Exam
        {
            Title = "Oylik nazorat — oktabr", Kind = ExamKind.Block, Delivery = ExamDelivery.Manual,
            ExamDate = "2026-10-10", Status = ExamStatus.Closed,
        };
        db.Exams.Add(exam);
        var pupils = new Dictionary<string, Student>();
        void Add(string name, SchoolClass cls, decimal? percent, string status = ExamParticipantStatus.Finished,
            bool archived = false, bool chat = true)
        {
            var s = new Student
            {
                FullName = $"{name} Testov", FirstName = name, LastName = "Testov", BirthDate = "2013-01-01",
                Gender = "male", ClassName = cls.Name, EnrollmentDate = "2025-09-01", IsArchived = archived,
            };
            pupils[name] = s;
            db.Students.Add(s);
            db.ExamParticipants.Add(new ExamParticipant
            {
                ExamId = exam.Id, ParticipantKind = ExamParticipantKind.Student, StudentId = s.Id, ClassId = cls.Id,
                Status = status, TotalPoints = percent is null ? null : percent / 2, MaxPoints = 50m, Percent = percent,
            });
            if (chat)
                db.TelegramRegistrations.Add(new TelegramRegistration
                {
                    StudentId = s.Id, ChatId = 7_000_000 + pupils.Count, ParentName = $"{name}ning onasi", Phone = "998900000000",
                });
        }
        Add("Ali", a, 95m);
        Add("Bobur", a, 90m, chat: false);
        Add("Dilnoza", a, 90m);
        Add("Eldor", a, 60m);
        Add("Farrux", a, 40m);
        Add("Gulnora", b, 88m);
        Add("Hasan", b, 70m);
        Add("Iroda", b, 30m);
        Add("Jasur", b, null, ExamParticipantStatus.Absent);
        Add("Kamol", a, 99m, archived: true);
        await db.SaveChangesAsync();
        return new World(exam.Id, pupils);
    }

    [Fact]
    public async Task Eng_yaxshi_va_eng_past_royxati_teng_natijalar_bilan()
    {
        await using var db = await FreshAsync("awards_preview");
        var w = await SeedAsync(db);
        var service = new ExamAwardService(db, ConfiguredTelegram(new RecordingHandler()), NullLogger<ExamAwardService>.Instance);

        // Whole exam, top 2: Ali (1) and the tie Bobur/Dilnoza (2) — three pupils; bottom 1: Iroda.
        var (whole, error) = await service.PreviewAsync(w.ExamId, 2, 1, perClass: false);
        Assert.Null(error);
        Assert.Equal(8, whole!.Scored);
        var group = Assert.Single(whole.Groups);
        Assert.Equal(["Ali Testov", "Bobur Testov", "Dilnoza Testov"], group.Top.Select(r => r.FullName));
        Assert.Equal([1, 2, 2], group.Top.Select(r => r.Rank));
        Assert.Equal(0, group.Top.Single(r => r.FullName == "Bobur Testov").Chats);
        Assert.Equal("Iroda Testov", Assert.Single(group.Bottom).FullName);
        Assert.DoesNotContain(group.Top.Concat(group.Bottom), r => r.FullName.StartsWith("Kamol") || r.FullName.StartsWith("Jasur"));

        // Per class, 1 and 1: each class its own best and lowest.
        var (perClass, _) = await service.PreviewAsync(w.ExamId, 1, 1, perClass: true);
        var classA = perClass!.Groups.Single(g => g.ClassName == "7-A");
        var classB = perClass.Groups.Single(g => g.ClassName == "7-B");
        Assert.Equal("Ali Testov", Assert.Single(classA.Top).FullName);
        Assert.Equal("Farrux Testov", Assert.Single(classA.Bottom).FullName);
        Assert.Equal("Gulnora Testov", Assert.Single(classB.Top).FullName);
        Assert.Equal("Iroda Testov", Assert.Single(classB.Bottom).FullName);

        // A class of three with top 2 and bottom 2: nobody is in both lists.
        var (overlap, _) = await service.PreviewAsync(w.ExamId, 2, 2, perClass: true);
        var b = overlap!.Groups.Single(g => g.ClassName == "7-B");
        Assert.Empty(b.Top.Select(r => r.StudentId).Intersect(b.Bottom.Select(r => r.StudentId)));
        Assert.Equal(["Iroda Testov"], b.Bottom.Select(r => r.FullName));

        Assert.NotNull((await service.PreviewAsync(w.ExamId, 51, 0, false)).Error);
        Assert.Equal(ExamAwardService.NotFound, (await service.PreviewAsync("no-such-exam", 1, 1, false)).Error);
    }

    [Fact]
    public async Task Xabar_va_tashakkurnoma_yuboriladi_ikkinchi_marta_yuborilmaydi()
    {
        await using var db = await FreshAsync("awards_send");
        var w = await SeedAsync(db);
        var handler = new RecordingHandler();
        var service = new ExamAwardService(db, ConfiguredTelegram(handler), NullLogger<ExamAwardService>.Instance);
        var sender = await SeedUserAsync(db);

        var (result, error) = await service.SendAsync(w.ExamId, new ExamAwardSendRequest(
            2, 1, false, ExamAwardService.DefaultTopText, ExamAwardService.DefaultBottomText, Diploma: true, null), sender);
        Assert.Null(error);
        Assert.Equal(3, result!.TopSent);
        Assert.Equal(1, result.BottomSent);
        Assert.Equal(1, result.NoTelegram);   // Bobur has no chat
        Assert.Equal(3, result.Delivered);    // Ali, Dilnoza, Iroda
        Assert.Equal(0, result.Skipped);

        // Texts: tokens filled in, the right kind to the right pupil; PDF only to the best who have a chat.
        Assert.Contains(handler.Texts, t => t.Contains("Ali Testov") && t.Contains("1-o'rinni") && t.Contains("47.5/50") && t.Contains("95%"));
        Assert.Contains(handler.Texts, t => t.Contains("Dilnoza Testov") && t.Contains("2-o'rinni") && t.Contains("Tashakkurnoma"));
        Assert.Contains(handler.Texts, t => t.Contains("Iroda Testov") && t.Contains("Ogohlantirish") && t.Contains("30%"));
        Assert.DoesNotContain(handler.Texts, t => t.Contains('{'));
        Assert.Equal(2, handler.Documents);

        // Each message is a broadcast: in the pupil's feed and Xabarlar's register with who got it.
        var notices = await db.ExamAwardNotices.AsNoTracking().Where(n => n.ExamId == w.ExamId).ToListAsync();
        Assert.Equal(4, notices.Count);
        var bobur = notices.Single(n => n.StudentId == w.Pupils["Bobur"].Id);
        Assert.Equal(0, bobur.Delivered);
        Assert.False(bobur.WithDiploma);
        Assert.True(notices.Single(n => n.StudentId == w.Pupils["Ali"].Id).WithDiploma);
        var feed = await BroadcastFeed.For(db, w.Pupils["Ali"].Id, "7-A").ToListAsync();
        var mine = Assert.Single(feed, f => f.ClassName.StartsWith("Tashakkurnoma"));
        Assert.Contains("Ali Testov", mine.Text);
        Assert.Contains(await db.BroadcastRecipients.Where(r => r.StudentId == w.Pupils["Bobur"].Id).ToListAsync(),
            r => r.Status == BroadcastDelivery.NoTelegram);

        // Pressing again sends nothing twice.
        var texts = handler.Texts.Count;
        var (again, _) = await service.SendAsync(w.ExamId, new ExamAwardSendRequest(
            2, 1, false, ExamAwardService.DefaultTopText, ExamAwardService.DefaultBottomText, true, null), sender);
        Assert.Equal(4, again!.Skipped);
        Assert.Equal(0, again.TopSent + again.BottomSent);
        Assert.Equal(texts, handler.Texts.Count);

        var sent = await service.SentAsync(w.ExamId);
        Assert.Equal(4, sent.Count);
        Assert.All(sent, s => Assert.Equal("Direktor Testov", s.SentByName));
    }

    [Fact]
    public async Task Belgilanmagan_otkaziladi_va_notogri_sorov_rad_etiladi()
    {
        await using var db = await FreshAsync("awards_rules");
        var w = await SeedAsync(db);
        var handler = new RecordingHandler();
        var service = new ExamAwardService(db, ConfiguredTelegram(handler), NullLogger<ExamAwardService>.Instance);
        var sender = await SeedUserAsync(db);

        // Unticked in the preview — left out.
        var (result, _) = await service.SendAsync(w.ExamId,
            new ExamAwardSendRequest(1, 0, false, "Rahmat, {ism}!", null, false, [w.Pupils["Ali"].Id]), sender);
        Assert.Equal(0, result!.TopSent);

        Assert.NotNull((await service.SendAsync(w.ExamId, new ExamAwardSendRequest(0, 0, false, "x", "y", false, null), sender)).Error);
        Assert.NotNull((await service.SendAsync(w.ExamId, new ExamAwardSendRequest(1, 0, false, " ", null, false, null), sender)).Error);
        Assert.NotNull((await service.SendAsync(w.ExamId, new ExamAwardSendRequest(0, 1, false, null, new string('x', 2001), false, null), sender)).Error);

        // Bot not configured — refused, nothing recorded.
        var silent = new ExamAwardService(db, UnconfiguredTelegram(), NullLogger<ExamAwardService>.Instance);
        Assert.NotNull((await silent.SendAsync(w.ExamId, new ExamAwardSendRequest(1, 1, false, "a", "b", false, null), sender)).Error);
        Assert.Empty(await db.ExamAwardNotices.Where(n => n.ExamId == w.ExamId).ToListAsync());
    }

    [Fact]
    public void Tashakkurnoma_pdf_chiziladi()
    {
        var pdf = new ExamDiplomaDocument(new ExamDiplomaModel(
            "Wunderkind", "Ali Testov", "7-A", "Oylik nazorat", "10.10.2026", 1, "47.5/50", "95%", new DateOnly(2026, 10, 15))).Render();
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Ruxsat_yuborish_imtihon_va_xabar_kaliti_bilan()
    {
        var examId = Guid.NewGuid().ToString();
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Exams.Add(new Exam
            {
                Id = examId, Title = "RBAC test", Kind = ExamKind.Block, Delivery = ExamDelivery.Manual,
                ExamDate = "2026-10-10", Status = ExamStatus.Closed,
            });
            await db.SaveChangesAsync();
        });
        var url = $"/api/admin/exams/{examId}/awards";
        var body = new ExamAwardSendRequest(1, 1, false, "a", "b", false, null);

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{url}/preview")).StatusCode);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{url}/preview")).StatusCode);

        // The exam section without messages may look, not send; with both it reaches the service
        // (which refuses here: the test bot is not configured).
        using var examsOnly = await fixture.Api.ClientAsAsync(Roles.Staff, "exams");
        Assert.Equal(HttpStatusCode.OK, (await examsOnly.GetAsync($"{url}/preview?top=2&bottom=2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await examsOnly.PostAsJsonAsync($"{url}/send", body)).StatusCode);
        using var messagesOnly = await fixture.Api.ClientAsAsync(Roles.Staff, "messages");
        Assert.Equal(HttpStatusCode.Forbidden, (await messagesOnly.PostAsJsonAsync($"{url}/send", body)).StatusCode);
        using var both = await fixture.Api.ClientAsAsync(Roles.Staff, "exams", "messages");
        Assert.Equal(HttpStatusCode.BadRequest, (await both.PostAsJsonAsync($"{url}/send", body)).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/exams/no-such-exam/awards/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{url}/preview?top=51")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
    }

    /* =====================================================================
     *  Helpers
     * ================================================================== */

    private async Task<AppDbContext> FreshAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<string> SeedUserAsync(AppDbContext db)
    {
        var user = new AppUser { FullName = "Direktor Testov", Role = Roles.SuperAdmin, Email = $"dir.{Guid.NewGuid():N}" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private TelegramService ConfiguredTelegram(HttpMessageHandler handler)
    {
        var service = UnconfiguredTelegram(handler);
        service.Set("test-token", "test_bot");
        return service;
    }

    private TelegramService UnconfiguredTelegram(HttpMessageHandler? handler = null) => new(
        fixture.Api.Services.GetRequiredService<IConfiguration>(),
        new Factory(handler ?? new RecordingHandler()),
        fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());

    /// <summary>Answers every call with 200; keeps message texts and counts documents.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Texts { get; } = [];
        public int Documents { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is MultipartFormDataContent || request.RequestUri!.AbsolutePath.EndsWith("/sendDocument"))
                Documents++;
            else
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("text", out var text)) Texts.Add(text.GetString()!);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
