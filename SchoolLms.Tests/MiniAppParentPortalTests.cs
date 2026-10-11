using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Mini App parent panel on the `/api/student/*` surface.
// ===========================================================================
//
//  The Mini App's parent panel lists children from `/api/tg/parent/children`
//  (guardianship) but loads every tab from `/api/student/*` with
//  `?studentId=`. Those endpoints used to IGNORE the id for a parent and pick
//  the first child whose `parent_phone` matched the login — so with two
//  children the switcher changed the name in the header while every tab kept
//  showing the first child, and a parent linked only through `guardians`
//  (phone on the student row differs) got 404 on every tab.
//
//  These tests pin the fixed contract (`GuardianAccess.ParentTargetAsync`):
//  an id naming one of the caller's OWN children is honoured, a foreign id is
//  ignored exactly as before, and guardianship works without a phone match.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class MiniAppParentPortalTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static int _nextPhone;

    [Fact]
    public async Task Two_children_each_tab_shows_the_selected_child()
    {
        var phone = NewPhone();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var elder = await AddStudentAsync(admin, "Portalov Akbar Anvarovich", "9-A", phone);
        var younger = await AddStudentAsync(admin, "Portalova Aziza Anvar qizi", "3-B", phone);
        var stranger = await AddStudentAsync(admin, "Begonov Bek Begonovich", "5-A", NewPhone());

        using var parent = await ParentClientAsync(admin, phone);

        foreach (var (id, name) in new[] { elder, younger })
        {
            var dashboard = await GetJsonAsync(parent, $"/api/student/dashboard?studentId={id}");
            Assert.Equal(id, dashboard.GetProperty("profile").GetProperty("id").GetString());

            var billing = await GetJsonAsync(parent, $"/api/student/billing?studentId={id}");
            Assert.Equal(name, billing.GetProperty("studentName").GetString());

            // Every call the Bosh / Jadval tabs make — none may answer 403 or 404
            // (`pickup` with no request today is 204, which the panel reads as null).
            // "announcements" — "Maktab e'lonlari" (2026-10-11: chat o'rnida, guruh chati olib tashlandi).
            foreach (var path in new[] { "journal", "attendance", "announcements", "pickup" })
            {
                var response = await parent.GetAsync($"/api/student/{path}?studentId={id}");
                Assert.True(response.IsSuccessStatusCode,
                    $"{path} for {name} -> {(int)response.StatusCode}");
            }
        }

        // A foreign id is IGNORED, as it always was: the caller gets their own
        // child and nothing about the stranger.
        var foreignDashboard = await GetJsonAsync(parent, $"/api/student/dashboard?studentId={stranger.Id}");
        var shown = foreignDashboard.GetProperty("profile").GetProperty("id").GetString();
        Assert.Contains(shown, new[] { elder.Id, younger.Id });

        var foreignBilling = await GetJsonAsync(parent, $"/api/student/billing?studentId={stranger.Id}");
        Assert.NotEqual(stranger.Name, foreignBilling.GetProperty("studentName").GetString());

        // Pickup is a write: the younger child by id, the stranger never.
        var pickup = await parent.PostAsJsonAsync("/api/student/pickup", new { studentId = younger.Id });
        Assert.Equal(HttpStatusCode.OK, pickup.StatusCode);
        var created = JsonSerializer.Deserialize<JsonElement>(await pickup.Content.ReadAsStringAsync(), Json);
        Assert.Equal(younger.Id, created.GetProperty("studentId").GetString());

        var foreignPickup = await parent.PostAsJsonAsync("/api/student/pickup", new { studentId = stranger.Id });
        Assert.Equal(HttpStatusCode.NotFound, foreignPickup.StatusCode);
    }

    /// <summary>
    /// A second guardian (added on the student form) lives only in `guardians`:
    /// the student row's `parent_phone` is the FIRST guardian's number. Before
    /// the fix that account got 404 on every `/api/student/*` tab.
    /// </summary>
    [Fact]
    public async Task Guardian_linked_without_a_phone_match_sees_the_child()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var child = await AddStudentAsync(admin, "Vasiyev Sardor Olimovich", "7-B", NewPhone());

        var secondPhone = NewPhone();
        var (user, _) = await fixture.Api.SeedUserAsync(
            "parent", fullName: "Vasiyeva Dilnoza", email: new string(secondPhone.Where(char.IsDigit).ToArray()));
        await fixture.Api.WithDbAsync(async db =>
        {
            var guardian = new Guardian { FullName = "Vasiyeva Dilnoza", Phone = secondPhone, UserId = user.Id };
            db.Guardians.Add(guardian);
            db.StudentGuardians.Add(new StudentGuardian
            {
                StudentId = child.Id,
                GuardianId = guardian.Id,
                Relation = GuardianRelation.Parent,
                IsPrimary = false,
            });
            await db.SaveChangesAsync();
        });

        using var parent = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor("parent", user.Id, user.FullName, user.Email));

        var me = await GetJsonAsync(parent, "/api/student/me");
        Assert.Equal(child.Id, me.GetProperty("id").GetString());

        var dashboard = await GetJsonAsync(parent, $"/api/student/dashboard?studentId={child.Id}");
        Assert.Equal(child.Id, dashboard.GetProperty("profile").GetProperty("id").GetString());

        var billing = await GetJsonAsync(parent, $"/api/student/billing?studentId={child.Id}");
        Assert.Equal(child.Name, billing.GetProperty("studentName").GetString());
    }

    /// <summary>
    /// A pupil account runs the same Mini App panel. `/api/tg/parent/*` is
    /// parent-only (403 for a pupil — the panel must fall back, not fail), and
    /// the `/api/student/*` calls the panel makes must all open for the pupil.
    /// </summary>
    [Fact]
    public async Task Pupil_account_opens_every_student_call_the_panel_makes()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var pupil = await AddStudentAsync(admin, "O'quvchiyev Jasur Karimovich", "8-A", NewPhone());

        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Student);
        await fixture.Api.WithDbAsync(async db =>
        {
            var s = await db.Students.SingleAsync(x => x.Id == pupil.Id);
            s.UserId = user.Id;
            await db.SaveChangesAsync();
        });

        using var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Student, user.Id, user.FullName, user.Email));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/tg/parent/children")).StatusCode);

        var me = await GetJsonAsync(client, "/api/student/me");
        Assert.Equal(pupil.Id, me.GetProperty("id").GetString());

        foreach (var path in new[] { "dashboard", "billing", "journal", "attendance", "announcements", "pickup", "meta" })
        {
            var response = await client.GetAsync($"/api/student/{path}?studentId={pupil.Id}");
            Assert.True(response.IsSuccessStatusCode, $"{path} -> {(int)response.StatusCode}");
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tg/me")).StatusCode);
    }

    /// <summary>
    /// `POST /api/tg/receipts/{id}/send` — the Mini App's receipt button (a
    /// blob download does nothing in Telegram's Android WebView). Parent and
    /// pupil only; the caller's own Telegram chat only; own child's payment only.
    /// </summary>
    [Fact]
    public async Task Receipt_to_telegram_is_guarded_by_role_link_and_ownership()
    {
        var phone = NewPhone();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var child = await AddStudentAsync(admin, "Chekov Bobur Anvarovich", "6-A", phone);
        var stranger = await AddStudentAsync(admin, "Begonov Chek Begonovich", "6-B", NewPhone());
        using var parent = await ParentClientAsync(admin, phone);
        // `/api/tg/*` is limited to 20 calls a minute per IP, and in TestServer every test shares one
        // address — in a full run the other Telegram tests used the quota up and this one got 429
        // instead of the answers it checks. Its own address, like the other rate-limited tests.
        var ip = SalesMarketingKit.NextIp();
        parent.DefaultRequestHeaders.Add("X-Forwarded-For", ip);

        var own = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var (cashier, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        await fixture.Api.WithDbAsync(async db =>
        {
            foreach (var (id, studentId) in new[] { (own, child.Id), (foreign, stranger.Id) })
                db.Payments.Add(new Payment
                {
                    Id = id,
                    ReceiptNo = Random.Shared.NextInt64(900_000_000, 999_999_999),
                    StudentId = studentId,
                    Amount = 100_000m,
                    CashierId = cashier.Id,
                    ReceivedAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
        });

        // Roles: anonymous 401, teacher 403.
        using var anonymous = fixture.Api.AnonymousClient();
        anonymous.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/tg/receipts/{own}/send", null)).StatusCode);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        teacher.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsync($"/api/tg/receipts/{own}/send", null)).StatusCode);

        // No Telegram account linked → 409, nothing to send to.
        var notLinked = await parent.PostAsync($"/api/tg/receipts/{own}/send", null);
        Assert.Equal(HttpStatusCode.Conflict, notLinked.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var userId = await db.Guardians
                .Where(g => g.Phone == phone).Select(g => g.UserId).SingleAsync();
            db.TelegramAccounts.Add(new TelegramAccount
            {
                TelegramUserId = Random.Shared.NextInt64(800_000_000, 899_999_999),
                UserId = userId!,
                DisplayName = "Test ota-ona",
            });
            await db.SaveChangesAsync();
        });

        // Someone else's payment, or none at all → 404.
        Assert.Equal(HttpStatusCode.NotFound,
            (await parent.PostAsync($"/api/tg/receipts/{foreign}/send", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await parent.PostAsync($"/api/tg/receipts/{Guid.NewGuid()}/send", null)).StatusCode);

        // Own payment passes every check and stops at the bot, which the test
        // host leaves unconfigured — so nothing leaves the machine.
        var ownSend = await parent.PostAsync($"/api/tg/receipts/{own}/send", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ownSend.StatusCode);
    }

    // ---------------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------------

    /// <summary>A number unique to this run — `guardians.phone_key` is unique.</summary>
    private static string NewPhone() =>
        $"+998 93 {Interlocked.Increment(ref _nextPhone):000} {DateTime.UtcNow:ss} {Random.Shared.Next(10, 99)}";

    private static async Task<(string Id, string Name)> AddStudentAsync(
        HttpClient admin, string fullName, string className, string parentPhone)
    {
        var created = await PostJsonAsync(admin, "/api/admin/students", new
        {
            fullName,
            birthDate = "2015-05-05",
            address = "Toshkent",
            gender = "male",
            parentFullName = "Ota " + fullName.Split(' ')[0],
            parentPhone,
            className,
            enrollmentDate = "2026-09-01",
        });
        return (created.GetProperty("id").GetString()!, fullName);
    }

    /// <summary>
    /// The guardian `GuardianSync` made from the phone, given an account the
    /// way the admin screen does it; returns a client holding that parent's token.
    /// </summary>
    private async Task<HttpClient> ParentClientAsync(HttpClient admin, string phone)
    {
        var list = await GetJsonAsync(admin, "/api/admin/guardians?search=" + Uri.EscapeDataString(phone));
        var guardianId = Assert.Single(list.EnumerateArray().ToList()).GetProperty("id").GetString()!;

        var account = await PostJsonAsync(admin, $"/api/admin/guardians/{guardianId}/account",
            new { newPassword = "Demo123!" });
        Assert.Equal("parent", account.GetProperty("role").GetString());

        string? userId = null;
        await fixture.Api.WithDbAsync(async db =>
            userId = await db.Guardians.Where(g => g.Id == guardianId).Select(g => g.UserId).SingleAsync());

        return fixture.Api.ClientWithToken(fixture.Api.TokenFor("parent", userId!));
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"GET {path} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);
    }

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode,
            $"POST {path} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);
    }
}
