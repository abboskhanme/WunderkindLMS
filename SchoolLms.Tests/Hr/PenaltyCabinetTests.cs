using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Hr;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Hr;

/// <summary>
/// Client, 2026-10-07: "qo'yilgan jarima ustozga shaxsiy kabinetida ko'rinishi va unga xabar bo'lib
/// borishi". The teacher sees only their own fines (cancelled ones marked) and gets a Telegram
/// message when one is written or cancelled.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class PenaltyCabinetTests(ApiFixture fixture)
{
    private const string Adjustments = "/api/admin/hr/adjustments";
    private const string Reasons = "/api/admin/hr/adjustment-reasons";

    [Fact]
    public async Task Oqituvchi_faqat_oz_jarimalarini_koradi_bekor_qilingani_belgilangan()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (me, meTeacher) = await SeedTeacherAsync();
        var (_, otherTeacher) = await SeedTeacherAsync();
        var penalty = await CreateReasonAsync(admin, AdjustmentKind.Penalty, "Kechikish " + Guid.NewGuid());
        var bonus = await CreateReasonAsync(admin, AdjustmentKind.Bonus, "Faollik " + Guid.NewGuid());

        var kept = await CreateAsync(admin, meTeacher, "penalty", penalty, 50_000m, "Darsga 10 daqiqa kechikdi");
        var cancelled = await CreateAsync(admin, meTeacher, "penalty", penalty, 30_000m, null);
        await CreateAsync(admin, meTeacher, "bonus", bonus, 200_000m, null);
        await CreateAsync(admin, otherTeacher, "penalty", penalty, 70_000m, null);
        var reverse = await admin.PostAsJsonAsync($"{Adjustments}/{cancelled.Id}/reverse", new { reason = "Xato yozilgan" });
        Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);

        using var teacher = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, me.Id, me.FullName, me.Email));
        var rows = await teacher.GetFromJsonAsync<List<EmployeePenaltyDto>>("/api/teacher/penalties");

        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);                                   // no bonus, no other teacher, no mirror row
        var open = Assert.Single(rows, r => r.Id == kept.Id);
        Assert.False(open.Cancelled);
        Assert.Equal(50_000m, open.Amount);
        Assert.Equal("Darsga 10 daqiqa kechikdi", open.Comment);
        var gone = Assert.Single(rows, r => r.Id == cancelled.Id);
        Assert.True(gone.Cancelled);
        Assert.Equal("Xato yozilgan", gone.CancelReason);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_bolmagan_rol_jarimalar_royxatini_ololmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/teacher/penalties")).StatusCode);
    }

    [Fact]
    public async Task Jarima_xabari_ikkala_bogLanishga_bir_martadan_boradi_bonusga_bormaydi()
    {
        var (me, teacherId) = await SeedTeacherAsync();
        var bot = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
        var app = bot + 1;
        await fixture.Api.WithDbAsync(async db =>
        {
            db.TelegramRegistrations.Add(new TelegramRegistration { TeacherId = teacherId, StudentId = "", ChatId = bot, ParentName = me.FullName });
            db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = app, UserId = me.Id });
            // The same chat through both links must not get the message twice.
            db.TelegramRegistrations.Add(new TelegramRegistration { TeacherId = teacherId, StudentId = "", ChatId = app, ParentName = me.FullName });
            await db.SaveChangesAsync();
        });
        var fine = Dto(teacherId, AdjustmentKind.Penalty, "Kechikish", 50_000m, "10 daqiqa");

        var handler = new RecordingHandler();
        await fixture.Api.WithDbAsync(async db =>
        {
            var notifier = new PenaltyNotifier(db, ConfiguredTelegram(handler),
                fixture.Api.Services.GetRequiredService<ILogger<PenaltyNotifier>>());

            Assert.Equal(2, await notifier.NotifyCreatedAsync(fine));
            Assert.Equal(0, await notifier.NotifyCreatedAsync(fine with { Kind = AdjustmentKind.Bonus }));
            Assert.Equal(2, await notifier.NotifyCancelledAsync(fine with { ReversalOf = Guid.NewGuid() }, "Xato"));
        });

        var sent = handler.Requests;
        Assert.Equal(4, sent.Count);
        Assert.Equal([bot, app], sent.Take(2).Select(r => r.GetProperty("chat_id").GetInt64()).Order());
        var created = sent[0].GetProperty("text").GetString()!;
        Assert.Contains("jarima yozildi", created, StringComparison.Ordinal);
        Assert.Contains("Kechikish", created, StringComparison.Ordinal);
        Assert.Contains("50 000 so'm", created, StringComparison.Ordinal);
        Assert.Contains("Izoh: 10 daqiqa", created, StringComparison.Ordinal);
        var cancelled = sent[3].GetProperty("text").GetString()!;
        Assert.Contains("bekor qilindi", cancelled, StringComparison.Ordinal);
        Assert.Contains("Bekor qilish sababi: Xato", cancelled, StringComparison.Ordinal);
    }

    // =================================================================

    private static PayrollAdjustmentDto Dto(string teacherId, string kind, string reason, decimal amount, string? comment) =>
        new(Guid.NewGuid(), EmployeeKind.Teacher, teacherId, "Ustoz", kind, Guid.NewGuid(), reason, amount,
            (short)AppClock.Today.Year, (short)AppClock.Today.Month, comment, null, "admin", "Admin",
            AppClock.NowInstant, null, false);

    private async Task<(AppUser User, string TeacherId)> SeedTeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        string teacherId = string.Empty;
        await fixture.Api.WithDbAsync(async db =>
            teacherId = await db.Teachers.Where(t => t.UserId == user.Id).Select(t => t.Id).SingleAsync());
        return (user, teacherId);
    }

    private static async Task<Guid> CreateReasonAsync(HttpClient admin, string kind, string name)
    {
        var response = await admin.PostAsJsonAsync(Reasons, new { kind, name, position = 0 });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AdjustmentReasonDto>())!.Id;
    }

    private static async Task<PayrollAdjustmentDto> CreateAsync(
        HttpClient admin, string teacherId, string kind, Guid reasonId, decimal amount, string? comment)
    {
        var today = AppClock.Today;
        var response = await admin.PostAsJsonAsync(Adjustments, new
        {
            employeeKind = "teacher", employeeId = teacherId, kind, reasonId, amount,
            periodYear = today.Year, periodMonth = today.Month, comment,
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PayrollAdjustmentDto>())!;
    }

    private TelegramService ConfiguredTelegram(RecordingHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>Records every request body and answers 200 — nothing leaves the machine.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<JsonElement> _requests = [];

        public IReadOnlyList<JsonElement> Requests
        {
            get { lock (_requests) return [.. _requests]; }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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
