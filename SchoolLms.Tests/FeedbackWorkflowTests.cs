using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Taklif va shikoyatlar workflow (client, 2026-10-07): qabul qilindi → jarayonda → hal qilindi, the
/// sender sees their own items with the answer, items go to a department and a responsible person,
/// and the bot tells the right people.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class FeedbackWorkflowTests(ApiFixture fixture)
{
    private const string Admin = "/api/admin/feedback";
    private static readonly Guid Kitchen = new("0f3d1a00-0000-4000-8000-000000000003"); // Ovqatlanish (seeded)

    [Fact]
    public async Task Yuborilgan_murojaat_holatlardan_otadi_va_yuboruvchi_javobni_koradi()
    {
        var (sender, _) = await StudentClientAsync();
        var text = "Oshxonada ovqat sovuq " + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(sender, "/api/student/feedback", text, Kitchen)).StatusCode);

        var mine = await sender.GetFromJsonAsync<List<MyFeedbackDto>>("/api/student/feedback");
        var item = Assert.Single(mine!, m => m.Text == text);
        Assert.Equal(FeedbackStatus.New, item.Status);
        Assert.Equal("Qabul qilindi", item.StatusLabel);
        Assert.Equal("Ovqatlanish", item.DepartmentName);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (staff, _) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: ["feedback"]);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"{Admin}/{item.Id}",
            new { status = "in_progress", assigneeId = staff.Id, note = "Oshpaz bilan gaplashiladi" })).StatusCode);
        var resolved = await admin.PatchAsJsonAsync($"{Admin}/{item.Id}",
            new { status = "resolved", answer = "Isitgich ta'mirlandi, rahmat!" });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        var after = Assert.Single((await sender.GetFromJsonAsync<List<MyFeedbackDto>>("/api/student/feedback"))!, m => m.Id == item.Id);
        Assert.Equal("Hal qilindi", after.StatusLabel);
        Assert.Equal("Isitgich ta'mirlandi, rahmat!", after.Answer);
        Assert.Equal(["new", "in_progress", "resolved"], after.Steps.Select(s => s.Status));
        Assert.DoesNotContain(after.Steps, s => s.Note == "Oshpaz bilan gaplashiladi");   // internal note stays inside

        using var detail = JsonDocument.Parse(await admin.GetStringAsync($"{Admin}/{item.Id}"));
        Assert.Equal(staff.Id, detail.RootElement.GetProperty("item").GetProperty("assigneeId").GetString());
        Assert.Equal(3, detail.RootElement.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task Oqituvchi_oz_murojaatlarini_koradi_boshqanikini_emas()
    {
        var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        var other = await fixture.Api.ClientAsAsync(Roles.Teacher);
        var mineText = "Proyektor ishlamayapti " + Guid.NewGuid().ToString("N")[..6];
        await SubmitAsync(teacher, "/api/teacher/feedback", mineText, null);
        await SubmitAsync(other, "/api/teacher/feedback", "Boshqa ustoz " + Guid.NewGuid().ToString("N")[..6], null);

        var mine = await teacher.GetFromJsonAsync<List<MyFeedbackDto>>("/api/teacher/feedback");
        var row = Assert.Single(mine!);
        Assert.Equal(mineText, row.Text);
        Assert.Null(row.DepartmentName);
    }

    [Fact]
    public async Task Ruxsatsiz_xodim_holatni_ozgartira_olmaydi_oqituvchi_inboxga_kira_olmaydi()
    {
        var (sender, _) = await StudentClientAsync();
        var text = "Ruxsat testi " + Guid.NewGuid().ToString("N")[..6];
        await SubmitAsync(sender, "/api/student/feedback", text, null);
        var id = (await sender.GetFromJsonAsync<List<MyFeedbackDto>>("/api/student/feedback"))!.Single(m => m.Text == text).Id;

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PatchAsJsonAsync($"{Admin}/{id}", new { status = "resolved" })).StatusCode);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.GetAsync(Admin)).StatusCode);
    }

    [Fact]
    public async Task Bolim_masullari_faqat_xodim_yoki_admin_boladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (staff, _) = await fixture.Api.SeedUserAsync(Roles.Staff);
        var (teacher, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var name = "Kutubxona " + Guid.NewGuid().ToString("N")[..6];

        var bad = await admin.PostAsJsonAsync($"{Admin}/departments", new { name, position = 9, isActive = true, memberUserIds = new[] { teacher.Id } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await admin.PostAsJsonAsync($"{Admin}/departments", new { name, position = 9, isActive = true, memberUserIds = new[] { staff.Id } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dep = await ok.Content.ReadFromJsonAsync<FeedbackDepartmentDto>();
        var member = Assert.Single(dep!.Members);
        Assert.Equal(staff.Id, member.UserId);
        Assert.False(member.HasAccess);                         // no "feedback" permission — the screen warns
    }

    [Fact]
    public async Task Bot_bolim_masuliga_va_yuboruvchiga_xabar_beradi()
    {
        var (staff, _) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: ["feedback"]);
        var (senderUser, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var depId = Guid.NewGuid();
        var staffChat = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
        var senderChat = staffChat + 1;
        var item = new Feedback
        {
            SenderRole = "teacher", SenderName = senderUser.FullName, Type = "complaint",
            Text = "Sinfda isitish yo'q", SenderUserId = senderUser.Id, DepartmentId = depId,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.FeedbackDepartments.Add(new FeedbackDepartment { Id = depId, Name = "Xo'jalik " + depId.ToString("N")[..6], CreatedAt = AppClock.NowInstant });
            db.FeedbackDepartmentMembers.Add(new FeedbackDepartmentMember { DepartmentId = depId, UserId = staff.Id });
            db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = staffChat, UserId = staff.Id });
            db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = senderChat, UserId = senderUser.Id });
            db.Feedbacks.Add(item);
            await db.SaveChangesAsync();
        });

        var handler = new RecordingHandler();
        await fixture.Api.WithDbAsync(async db =>
        {
            var notifier = new FeedbackNotifier(db, ConfiguredTelegram(handler),
                fixture.Api.Services.GetRequiredService<ILogger<FeedbackNotifier>>());
            Assert.Equal(1, await notifier.NewAsync(item));
            item.Status = FeedbackStatus.Resolved;
            item.Answer = "Qozonxona yoqildi";
            Assert.Equal(1, await notifier.SenderAsync(item));
        });

        var sent = handler.Requests;
        Assert.Equal(staffChat, sent[0].GetProperty("chat_id").GetInt64());
        Assert.Contains("Yangi shikoyat", sent[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(senderChat, sent[1].GetProperty("chat_id").GetInt64());
        var toSender = sent[1].GetProperty("text").GetString()!;
        Assert.Contains("Hal qilindi", toSender, StringComparison.Ordinal);
        Assert.Contains("Javob: Qozonxona yoqildi", toSender, StringComparison.Ordinal);
    }

    // =================================================================

    private async Task<(HttpClient Client, string StudentId)> StudentClientAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Student);
        var student = new Student
        {
            FullName = "Murojaat " + Guid.NewGuid().ToString("N")[..6], ClassName = "5-A", UserId = user.Id,
            ParentFullName = "Ota Onasi",
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return (fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Student, user.Id, user.FullName, user.Email)), student.Id);
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string url, string text, Guid? departmentId)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("complaint"), "type" },
            { new StringContent(text), "text" },
        };
        if (departmentId is { } d) form.Add(new StringContent(d.ToString()), "departmentId");
        return client.PostAsync(url, form);
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
