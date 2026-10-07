using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// An archived pupil is no longer the school's (client, 2026-10-08: "arxivdagi o'quvchilar tizimga ham
/// kirolmasligi kerak"): the sign-in is refused, and a token taken before the archiving stops working
/// on the very next request (Program.cs <c>OnTokenValidated</c>). The Mini App side is covered by
/// <c>TelegramPanelKeyTests.Arxivlangan_akkaunt_boglanishi_uziladi_va_boshqasiga_ulanadi</c>.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ArchivedStudentSignInTests(ApiFixture fixture)
{
    [Fact]
    public async Task Arxivdagi_oquvchi_kira_olmaydi_va_eski_tokeni_ishlamaydi()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Student, email: $"arxivoquvchi{tag}");
        var student = new Student { FullName = "Arxiv O'quvchi " + tag, ClassName = "5-A", EnrollmentDate = "2026-09-01", UserId = user.Id };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });

        using var anonymous = fixture.Api.AnonymousClient();
        var first = await LoginAsync(anonymous, user.Email, password);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var token = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
        using var signedIn = fixture.Api.ClientWithToken(token);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/auth/me")).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.Students.SingleAsync(s => s.Id == student.Id);
            row.IsArchived = true;
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(anonymous, user.Email, password)).StatusCode);
    }

    /// <summary>Each sign-in from its own TEST-NET address — the login rate limit is per IP.</summary>
    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-Forwarded-For", SalesMarketingKit.NextIp());
        return await client.SendAsync(request);
    }
}
