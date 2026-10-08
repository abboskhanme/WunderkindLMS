using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Several accounts on one Telegram (client, 2026-10-08: "bitta oila farzandlari uchun 2 xil login parol
/// bilan kirsa ham boshqa farzandini akkauntiga o'tishi oson bo'lishi kerak ... o'qituvchi va ota-ona
/// akkauntini ham almashtirish"). Each account is added once with its own password; switching needs
/// none; "Chiqish" from one opens the next; an archived one drops out.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TelegramAccountSwitchTests(ApiFixture fixture)
{
    private static long _telegram = 8_300_000_000 + Random.Shared.Next(1, 90_000_000);

    [Fact]
    public async Task Oqituvchi_farzandini_qoshadi_va_parolsiz_almashtiradi()
    {
        var (teacher, account, key) = await LinkedTeacherAsync();
        var (child, childPassword) = await StudentAsync("5-A");
        using var anonymous = fixture.Api.AnonymousClient();
        var teacherToken = await TokenByKeyAsync(anonymous, key);

        // Add the child's account — its own login and password, once.
        using var asTeacher = fixture.Api.ClientWithToken(teacherToken);
        var added = await Read(await PostWithIpAsync(asTeacher, "/api/tg/accounts", new { login = child.Email, password = childPassword }));
        Assert.Equal(child.Id, added.GetProperty("user").GetProperty("id").GetString());

        using var asChild = fixture.Api.ClientWithToken(added.GetProperty("token").GetString()!);
        var list = (await Read(await asChild.GetAsync("/api/tg/accounts"))).GetProperty("accounts");
        Assert.Equal(2, list.GetArrayLength());
        var first = list[0];
        Assert.Equal(child.Id, first.GetProperty("userId").GetString());
        Assert.True(first.GetProperty("active").GetBoolean());
        Assert.Equal("5-A sinf · O'quvchi", first.GetProperty("subtitle").GetString());

        // Back to the teacher — no password.
        var back = await Read(await asChild.PostAsync($"/api/tg/accounts/{teacher.Id}/switch", null));
        Assert.Equal(teacher.Id, back.GetProperty("user").GetProperty("id").GetString());
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(teacher.Id, (await db.TelegramAccounts.SingleAsync(a => a.Id == account.Id)).UserId));

        // The bot's button opens whichever is active now — the key survived the switches.
        var reopened = await Read(await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key }));
        Assert.Equal(teacher.Id, reopened.GetProperty("user").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Saqlanmagan_akkaunt_notogri_parol_va_veb_sessiya_rad_etiladi()
    {
        var (_, _, key) = await LinkedTeacherAsync();
        var (stranger, password) = await StudentAsync("6-B");
        using var anonymous = fixture.Api.AnonymousClient();
        using var asTeacher = fixture.Api.ClientWithToken(await TokenByKeyAsync(anonymous, key));

        Assert.Equal(HttpStatusCode.NotFound, (await asTeacher.PostAsync($"/api/tg/accounts/{stranger.Id}/switch", null)).StatusCode);
        // 400, not 401 — the session is fine, only the added account's password is wrong.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostWithIpAsync(asTeacher, "/api/tg/accounts", new { login = stranger.Email, password = password + "x" })).StatusCode);

        // A web session (no Telegram in it) cannot use the switcher.
        using var web = await fixture.Api.ClientAsAsync(Roles.Admin);
        var refused = await web.GetAsync("/api/tg/accounts");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("telegram_only", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Chiqish_keyingi_saqlangan_akkauntni_ochadi_oxirgisi_uziladi()
    {
        var (teacher, account, key) = await LinkedTeacherAsync();
        var (child, childPassword) = await StudentAsync("2-A");
        using var anonymous = fixture.Api.AnonymousClient();
        using var asTeacher = fixture.Api.ClientWithToken(await TokenByKeyAsync(anonymous, key));
        var added = await Read(await PostWithIpAsync(asTeacher, "/api/tg/accounts", new { login = child.Email, password = childPassword }));

        // "Chiqish" from the child → the teacher opens, the Telegram stays linked (same row, same key).
        using var asChild = fixture.Api.ClientWithToken(added.GetProperty("token").GetString()!);
        var left = await Read(await asChild.DeleteAsync("/api/tg/link"));
        Assert.Equal(teacher.Id, left.GetProperty("user").GetProperty("id").GetString());
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.Equal(teacher.Id, (await db.TelegramAccounts.SingleAsync(a => a.Id == account.Id)).UserId);
            Assert.False(await db.TelegramSavedAccounts.AnyAsync(s => s.TelegramUserId == account.TelegramUserId && s.UserId == child.Id));
        });

        // "Chiqish" from the last one — unlinked, a ticket to sign in again.
        using var asTeacherAgain = fixture.Api.ClientWithToken(left.GetProperty("token").GetString()!);
        var gone = await Read(await asTeacherAgain.DeleteAsync("/api/tg/link"));
        Assert.False(string.IsNullOrEmpty(gone.GetProperty("ticket").GetString()));
        await fixture.Api.WithDbAsync(async db => Assert.False(await db.TelegramAccounts.AnyAsync(a => a.Id == account.Id)));
    }

    [Fact]
    public async Task Arxivlangan_akkaunt_royxatdan_chiqadi_va_keyingisi_ochiladi()
    {
        var (teacher, account, key) = await LinkedTeacherAsync();
        var (child, childPassword) = await StudentAsync("3-A");
        using var anonymous = fixture.Api.AnonymousClient();
        using var asTeacher = fixture.Api.ClientWithToken(await TokenByKeyAsync(anonymous, key));
        await Read(await PostWithIpAsync(asTeacher, "/api/tg/accounts", new { login = child.Email, password = childPassword }));

        // The child (active now) is archived — the next open lands in the teacher, not on the login screen.
        await fixture.Api.WithDbAsync(async db =>
        {
            (await db.Students.SingleAsync(s => s.UserId == child.Id)).IsArchived = true;
            await db.SaveChangesAsync();
        });
        var opened = await Read(await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key }));
        Assert.Equal(teacher.Id, opened.GetProperty("user").GetProperty("id").GetString());
        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.TelegramSavedAccounts.AnyAsync(s => s.TelegramUserId == account.TelegramUserId && s.UserId == child.Id)));
    }

    // =================================================================

    private async Task<(AppUser User, TelegramAccount Account, string Key)> LinkedTeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var account = new TelegramAccount { TelegramUserId = Interlocked.Increment(ref _telegram), UserId = user.Id, DisplayName = "Test" };
        var key = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            db.TelegramAccounts.Add(account);
            key = TelegramPanelKeys.Issue(account);
            await db.SaveChangesAsync();
        });
        return (user, account, key);
    }

    private async Task<(AppUser User, string Password)> StudentAsync(string className)
    {
        var (user, password) = await fixture.Api.SeedUserAsync(Roles.Student);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(new Student { FullName = user.FullName, ClassName = className, EnrollmentDate = "2026-09-01", UserId = user.Id });
            await db.SaveChangesAsync();
        });
        return (user, password);
    }

    private static async Task<string> TokenByKeyAsync(HttpClient client, string key) =>
        (await Read(await client.PostAsJsonAsync("/api/tg/auth/key", new { key }))).GetProperty("token").GetString()!;

    /// <summary>The add endpoint shares the login rate limit (per IP) — each call from its own address.</summary>
    private static async Task<HttpResponseMessage> PostWithIpAsync(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Forwarded-For", SalesMarketingKit.NextIp());
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }
}
