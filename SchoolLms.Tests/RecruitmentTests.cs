using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Ishga qabul va adaptatsiya (client, 2026-10-07): "vakansiya qoldirgan xodimlar — ishga olinish
/// jarayonidagilar bosqichli holatda (Ma'lumot/CV -> Test -> Demo -> HR suhbat -> Direktor suhbat) —
/// ishga olingan va adaptatsiya holatiga o'tganlar (30 kun, 11 bosqich)".
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class RecruitmentTests(ApiFixture fixture)
{
    private const string Admin = "/api/admin/recruitment";
    private const string Public = "/api/public/vacancies";
    private static readonly byte[] Pdf = "%PDF-1.4 test cv"u8.ToArray();

    [Fact]
    public async Task Ochiq_formadan_ariza_CV_bilan_keladi_takror_va_honeypot_yozilmaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var vacancy = await CreateVacancyAsync(admin, "Ingliz tili o'qituvchisi");
        Assert.StartsWith("ingliz-tili-oqituvchisi-", vacancy.Slug, StringComparison.Ordinal);

        var anon = fixture.Api.CreateClient();
        var list = await anon.GetFromJsonAsync<List<PublicVacancyDto>>(Public);
        Assert.Contains(list!, v => v.Slug == vacancy.Slug);

        var phone = "+99890" + Random.Shared.Next(1_000_000, 9_999_999);
        Assert.Equal(HttpStatusCode.OK, (await ApplyAsync(anon, vacancy.Slug, "Aliyeva Dilnoza", phone, ("cv.pdf", Pdf))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApplyAsync(anon, vacancy.Slug, "Aliyeva Dilnoza", phone, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApplyAsync(anon, vacancy.Slug, "Bot", "+998901112233", null, website: "spam.example")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApplyAsync(anon, vacancy.Slug, "Kimdir", "12345", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApplyAsync(anon, vacancy.Slug, "Kimdir", phone, ("cv.exe", Pdf))).StatusCode);

        var rows = await admin.GetFromJsonAsync<List<ApplicantRowDto>>($"{Admin}/applicants?tab=new&vacancyId={vacancy.Id}");
        var row = Assert.Single(rows!);                                       // the repeat and the bot left nothing
        Assert.Equal("public", row.Source);
        Assert.True(row.HasCv);
        Assert.Equal(phone, row.Phone);

        var cv = await admin.GetAsync($"{Admin}/applicants/{row.Id}/cv");
        Assert.Equal(HttpStatusCode.OK, cv.StatusCode);
        Assert.Equal(Pdf, await cv.Content.ReadAsByteArrayAsync());
        string path = "";
        await fixture.Api.WithDbAsync(async db => path = (await db.JobApplicants.SingleAsync(a => a.Id == row.Id)).CvPath!);
        Assert.StartsWith("private/recruitment/", path, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/uploads/" + path)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Admin}/vacancies/{vacancy.Id}/close", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ApplyAsync(anon, vacancy.Slug, "Kech qolgan", "+998901234500", null)).StatusCode);
        Assert.DoesNotContain((await anon.GetFromJsonAsync<List<PublicVacancyDto>>(Public))!, v => v.Slug == vacancy.Slug);
    }

    [Fact]
    public async Task Nomzod_bosqichlardan_otib_ishga_olinadi_va_adaptatsiyani_yakunlaydi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var id = await CreateManualAsync(admin, "Karimov Jasur");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Admin}/applicants/{id}/results/test", new { score = 80 })).StatusCode);
        await MoveAsync(admin, id, "cv");
        await MoveAsync(admin, id, "test");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Admin}/applicants/{id}/results/test", new { score = 85, comment = "Grammatika kuchli" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Admin}/applicants/{id}/results/test", new { score = 120 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/hire", new { hiredOn = (string?)null })).StatusCode);
        await MoveAsync(admin, id, "demo");
        await MoveAsync(admin, id, "hr_interview");
        await MoveAsync(admin, id, "director_interview");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/move", new { stage = "onboarding" })).StatusCode);

        var hiredOn = AppClock.Today.AddDays(-3);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/hire", new { hiredOn = hiredOn.ToString("yyyy-MM-dd") })).StatusCode);

        var detail = (await admin.GetFromJsonAsync<ApplicantDetailDto>($"{Admin}/applicants/{id}"))!;
        Assert.Equal("onboarding", detail.Row.Stage);
        Assert.Equal(4, detail.OnboardingDay);
        var active = (await admin.GetFromJsonAsync<List<OnboardingTemplateDto>>($"{Admin}/onboarding-steps"))!.Count(t => t.IsActive);
        Assert.Equal(active, detail.Onboarding.Count);
        Assert.Equal(hiredOn.ToString("yyyy-MM-dd"), detail.Onboarding[0].DueOn);
        Assert.Contains(detail.Onboarding, s => s.Overdue);                   // due on day 1–3, not done
        Assert.Equal(85, Assert.Single(detail.Results).Score);
        Assert.Equal(["new", "cv", "test", "demo", "hr_interview", "director_interview", "onboarding"], detail.Events.Select(e => e.ToStage));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/adapted", new { stage = "adapted" })).StatusCode);
        foreach (var step in detail.Onboarding)
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Admin}/applicants/{id}/onboarding/{step.Id}", new { done = true, comment = "Bajarildi" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/adapted", new { stage = "adapted", note = "Doimiy ishga" })).StatusCode);

        var archive = await admin.GetFromJsonAsync<List<ApplicantRowDto>>($"{Admin}/applicants?tab=archive&q=Karimov Jasur");
        var done = Assert.Single(archive!, r => r.Id == id);
        Assert.Equal("adapted", done.Stage);
        Assert.Equal(done.StepsTotal, done.StepsDone);
    }

    [Fact]
    public async Task Rad_etish_sababi_bilan_va_qayta_korib_chiqish_mumkin()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var id = await CreateManualAsync(admin, "Rad Etilgan");
        await MoveAsync(admin, id, "cv");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/reject", new { reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/reject", new { reason = "Tajriba yetarli emas" })).StatusCode);

        var rejected = (await admin.GetFromJsonAsync<ApplicantDetailDto>($"{Admin}/applicants/{id}"))!.Row;
        Assert.Equal("rejected", rejected.Stage);
        Assert.Equal("cv", rejected.RejectedAtStage);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/reject", new { reason = "Yana" })).StatusCode);

        await MoveAsync(admin, id, "cv");
        var back = (await admin.GetFromJsonAsync<ApplicantDetailDto>($"{Admin}/applicants/{id}"))!.Row;
        Assert.Equal("cv", back.Stage);
        Assert.Null(back.RejectReason);
    }

    [Fact]
    public async Task Adaptatsiya_bosqichlari_sozlanadi_yangi_ishga_olinganga_qollanadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var original = (await admin.GetFromJsonAsync<List<OnboardingTemplateDto>>($"{Admin}/onboarding-steps"))!;
        Assert.True(original.Count >= 11);
        try
        {
            var bad = await admin.PutAsJsonAsync($"{Admin}/onboarding-steps", new[] { new { title = "X", dueDay = 31, isActive = true } });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var two = await admin.PutAsJsonAsync($"{Admin}/onboarding-steps", new object[]
            {
                new { title = "Shartnoma", description = (string?)null, dueDay = 1, isActive = true },
                new { title = "Yakuniy suhbat", description = (string?)null, dueDay = 30, isActive = true },
                new { title = "O'chirilgan", description = (string?)null, dueDay = 5, isActive = false },
            });
            Assert.Equal(HttpStatusCode.OK, two.StatusCode);

            var id = await CreateManualAsync(admin, "Ikki Bosqichli");
            await MoveAsync(admin, id, "director_interview");
            var start = AppClock.Today;
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/hire", new { hiredOn = start.ToString("yyyy-MM-dd") })).StatusCode);
            var steps = (await admin.GetFromJsonAsync<ApplicantDetailDto>($"{Admin}/applicants/{id}"))!.Onboarding;
            Assert.Equal(["Shartnoma", "Yakuniy suhbat"], steps.Select(s => s.Title));
            Assert.Equal(start.AddDays(29).ToString("yyyy-MM-dd"), steps[1].DueOn);
            Assert.Equal(30, steps[1].Day);
        }
        finally
        {
            var restore = await admin.PutAsJsonAsync($"{Admin}/onboarding-steps",
                original.Select(t => new { id = t.Id, title = t.Title, description = t.Description, dueDay = t.DueDay, isActive = t.IsActive }));
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        }
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rol_nomzodlarni_kormaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Admin}/applicants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Admin}/vacancies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Admin}/vacancies", new { title = "X" })).StatusCode);
    }

    [Fact]
    public async Task Ruxsati_bor_xodim_vakansiya_ochadi()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, RecruitmentService.Permission);
        var created = await staff.PostAsJsonAsync($"{Admin}/vacancies", new { title = "Matematika o'qituvchisi", department = "O'quv bo'limi" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Admin}/applicants?tab=pipeline")).StatusCode);
    }

    [Fact]
    public void Slug_lotin_harflaridan_va_qisqa_dumdan_iborat()
    {
        Assert.Matches("^matematika-oqituvchisi-[0-9a-f]{4}$", RecruitmentService.SlugFrom("Matematika o'qituvchisi"));
        Assert.Matches("^vakansiya-[0-9a-f]{4}$", RecruitmentService.SlugFrom("Учитель"));
    }

    // =================================================================

    private static async Task<VacancyDto> CreateVacancyAsync(HttpClient admin, string title)
    {
        var response = await admin.PostAsJsonAsync($"{Admin}/vacancies", new { title, department = "O'quv bo'limi", description = "To'liq stavka", requirements = "IELTS 7+" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<VacancyDto>())!;
    }

    private static async Task<Guid> CreateManualAsync(HttpClient admin, string name)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(name), "fullName" },
            { new StringContent("+99891" + Random.Shared.Next(1_000_000, 9_999_999)), "phone" },
            { new StringContent("Oshpaz"), "position" },
        };
        var response = await admin.PostAsync($"{Admin}/applicants", form);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApplicantDetailDto>())!.Row.Id;
    }

    private static async Task MoveAsync(HttpClient admin, Guid id, string stage)
    {
        var response = await admin.PostAsJsonAsync($"{Admin}/applicants/{id}/move", new { stage, note = (string?)null });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{stage}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>Each public request from its own TEST-NET address — the form's rate limit is per IP.</summary>
    private static async Task<HttpResponseMessage> ApplyAsync(
        HttpClient anon, string slug, string name, string phone, (string Name, byte[] Bytes)? cv, string? website = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(name), "fullName" },
            { new StringContent(phone), "phone" },
            { new StringContent("5 yil maktabda"), "experience" },
        };
        if (website is not null) form.Add(new StringContent(website), "website");
        if (cv is { } file)
        {
            var content = new ByteArrayContent(file.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(content, "cv", file.Name);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Public}/{slug}/apply") { Content = form };
        request.Headers.Add("X-Forwarded-For", SalesMarketingKit.NextIp());
        return await anon.SendAsync(request);
    }
}
