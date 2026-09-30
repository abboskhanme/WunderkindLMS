using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Dars tahlili (docs/modules/lesson-analysis.md) — HTTP + scoring.
//
//  COVERS: RBAC on every endpoint (anonymous 401, teacher 403 on the staff API,
//  staff without the key 403, staff with it 200, admin-only actions 403 for
//  staff, admin 200); scoring (unmarked not counted, round 2, recompute on save,
//  cleared rows dropped, answers to deactivated criteria kept); rating average +
//  date filter; a teacher sees only their own COMPLETED observations; who may
//  correct a completed one; complete is idempotent (AI called once — a fake);
//  no-key AI error without a network call; no-store on every response; the
//  seeder; the lessons-by-date endpoint; the JSON contract shape.
//
//  AI. The shared host has no Gemini key, so completing there records the
//  "kalit sozlanmagan" error and never touches the network. Tests that need to
//  count model calls use a DERIVED host (WithWebHostBuilder — the
//  TelegramMiniAppTests pattern) with a fake ILessonAnalysisAi.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class LessonAnalysisTests(ApiFixture fixture)
{
    private const string Admin = "/api/admin/lesson-analysis";
    private const string Mine = "/api/teacher/lesson-analysis";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // =====================================================================
    //  1. RBAC — every endpoint
    // =====================================================================

    /// <summary>Every staff endpoint; <c>{x}</c> becomes a random id (authorization runs first).</summary>
    public static TheoryData<string, string> StaffEndpoints() => new()
    {
        { "GET", "criteria" },
        { "POST", "criteria" },
        { "PATCH", "criteria/{x}" },
        { "GET", "teachers" },
        { "GET", "teachers/random" },
        { "GET", "teachers/{x}" },
        { "GET", "teachers/{x}/lessons?date=2026-09-28" },
        { "GET", "observations" },
        { "POST", "observations" },
        { "GET", "observations/{x}" },
        { "PATCH", "observations/{x}" },
        { "POST", "observations/{x}/complete" },
        { "POST", "observations/{x}/ai-summary" },
        { "DELETE", "observations/{x}" },
        { "GET", "observations/{x}/report.pdf" },
        { "GET", "rating" },
        { "GET", "rating.xlsx" },
        { "GET", "dashboard" },
        { "GET", "analytics" },
    };

    public static TheoryData<string> TeacherEndpoints() => new()
    {
        "observations", "observations/{x}", "observations/{x}/report.pdf", "analytics",
    };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Staff_API_anonim_401_oqituvchi_va_ruxsatsiz_xodim_403(string method, string path)
    {
        var url = $"{Admin}/{path.Replace("{x}", Guid.NewGuid().ToString())}";

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, method, url)).StatusCode);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(teacher, method, url)).StatusCode);

        // no key at all, and a different section's key (GatedRead: reads are closed too)
        using var bare = await fixture.Api.ClientAsAsync(Roles.Staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(bare, method, url)).StatusCode);
        using var other = await fixture.Api.ClientAsAsync(Roles.Staff, "discipline", "teachers");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(other, method, url)).StatusCode);

        using var cashier = await fixture.Api.ClientAsAsync(Roles.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(cashier, method, url)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(TeacherEndpoints))]
    public async Task Oqituvchi_API_anonim_401_admin_va_xodim_403(string path)
    {
        var url = $"{Mine}/{path.Replace("{x}", Guid.NewGuid().ToString())}";

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(url)).StatusCode);

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Ruxsatli_xodim_va_admin_oqiydi_va_tahlil_yozadi(string role)
    {
        using var client = role == Roles.Staff
            ? await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey)
            : await fixture.Api.ClientAsAsync(role);
        var teacherId = await NewTeacherAsync("Ruxsat");

        foreach (var path in new[]
                 {
                     "criteria", "criteria?includeInactive=true", "teachers", "teachers?q=Ruxsat", "teachers/random",
                     $"teachers/{teacherId}", $"teachers/{teacherId}/lessons?date=2026-09-28", "observations",
                     "rating", "rating?dateFrom=2026-01-01&dateTo=2026-12-31", "rating.xlsx", "dashboard",
                     "analytics", $"analytics?teacherId={teacherId}",
                 })
        {
            var response = await client.GetAsync($"{Admin}/{path}");
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"{role} GET {path} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        var criteria = await ActiveCriteriaAsync(client);
        var created = await CreateAsync(client, teacherId, "2026-05-04", (criteria[0], true, null));
        var id = created.GetProperty("id").GetString()!;

        foreach (var path in new[] { $"observations/{id}", $"observations/{id}/report.pdf", $"observations?teacherId={teacherId}" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Admin}/{path}")).StatusCode);

        var patch = await client.PatchAsJsonAsync($"{Admin}/observations/{id}", new { className = "7-B" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Admin}/observations/{id}/complete", null)).StatusCode);
    }

    /// <summary>"Faqat ko'rish" (<c>lessonAnalysis:view</c>) reads, but never writes.</summary>
    [Fact]
    public async Task Faqat_korish_xodimi_oqiydi_yoza_olmaydi()
    {
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey + Roles.ViewSuffix);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Admin}/rating")).StatusCode);
        var teacherId = await NewTeacherAsync("Korish");
        var post = await viewer.PostAsJsonAsync($"{Admin}/observations", Input(teacherId, "2026-05-05"));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Admin_amallari_xodimga_403_adminga_ruxsat()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var super = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var sections = await SectionsAsync(admin, includeInactive: true);
        var sectionId = sections[0].GetProperty("id").GetString()!;

        // POST criteria
        var body = new { sectionId, text = "Staff yozgan mezon " + Guid.NewGuid().ToString("N")[..6] };
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"{Admin}/criteria", body)).StatusCode);
        var created = await admin.PostAsJsonAsync($"{Admin}/criteria", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var criterion = await ReadAsync(created);
        var criterionId = criterion.GetProperty("id").GetString()!;
        Assert.True(criterion.GetProperty("isActive").GetBoolean());

        // omitted orderIndex → appended after the last criterion of the section
        var maxOrder = sections[0].GetProperty("criteria").EnumerateArray().Max(c => c.GetProperty("orderIndex").GetInt32());
        Assert.True(criterion.GetProperty("orderIndex").GetInt32() > maxOrder);

        // PATCH criteria
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PatchAsJsonAsync($"{Admin}/criteria/{criterionId}", new { isActive = false })).StatusCode);
        var patched = await super.PatchAsJsonAsync($"{Admin}/criteria/{criterionId}", new { isActive = false, text = "Yangi matn" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var p = await ReadAsync(patched);
        Assert.False(p.GetProperty("isActive").GetBoolean());
        Assert.Equal("Yangi matn", p.GetProperty("text").GetString());

        // the deactivated criterion is hidden by default, listed with includeInactive
        Assert.DoesNotContain(criterionId, AllCriterionIds(await SectionsAsync(admin, includeInactive: false)));
        Assert.Contains(criterionId, AllCriterionIds(await SectionsAsync(admin, includeInactive: true)));

        // DELETE observation
        var teacherId = await NewTeacherAsync("Ochirish");
        var obs = await CreateAsync(staff, teacherId, "2026-05-06");
        var id = obs.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"{Admin}/observations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Admin}/observations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Admin}/observations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Admin}/observations/{id}")).StatusCode);
    }

    /// <summary>
    /// Mijoz, 2026-09-29: mezon qo'shish ruxsati ROL orqali beriladi — alohida belgi
    /// <see cref="LessonAnalysisAccess.CriteriaPermissionKey"/>. Oddiy <c>lessonAnalysis</c>
    /// uni ochmaydi (yuqoridagi test), belgining o'zi esa sinf darvozasidan o'tkazmaydi.
    /// </summary>
    [Fact]
    public async Task Mezon_ruxsati_rol_belgisi_bilan_beriladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var director = await fixture.Api.ClientAsAsync(Roles.Staff,
            LessonAnalysisAccess.PermissionKey, LessonAnalysisAccess.CriteriaPermissionKey);
        using var flagOnly = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.CriteriaPermissionKey);
        using var viewerWithFlag = await fixture.Api.ClientAsAsync(Roles.Staff,
            LessonAnalysisAccess.PermissionKey + Roles.ViewSuffix, LessonAnalysisAccess.CriteriaPermissionKey);
        var sectionId = (await SectionsAsync(admin, includeInactive: true))[0].GetProperty("id").GetString()!;
        object Body() => new { sectionId, text = "Direktor mezoni " + Guid.NewGuid().ToString("N")[..6] };

        var created = await director.PostAsJsonAsync($"{Admin}/criteria", Body());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var criterionId = (await ReadAsync(created)).GetProperty("id").GetString()!;
        var patched = await director.PatchAsJsonAsync($"{Admin}/criteria/{criterionId}", new { text = "Tahrirlangan", isActive = false });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        // Belgi bo'lsa ham dars tahlili bo'limining TO'LIQ ruxsati shart (sinf darvozasi).
        Assert.Equal(HttpStatusCode.Forbidden, (await flagOnly.PostAsJsonAsync($"{Admin}/criteria", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerWithFlag.PostAsJsonAsync($"{Admin}/criteria", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewerWithFlag.PatchAsJsonAsync($"{Admin}/criteria/{criterionId}", new { isActive = true })).StatusCode);
    }

    // =====================================================================
    //  2. Scoring (SPEC §4)
    // =====================================================================

    [Fact]
    public async Task Belgilanmagan_mezon_hisobga_kirmaydi_foiz_2_xonali()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        var c = await ActiveCriteriaAsync(staff);
        var teacherId = await NewTeacherAsync("Ball");

        // 2 done + 1 not done + 1 unmarked-with-comment → 2/3 = 66.67; the unmarked row is kept
        var obs = await CreateAsync(staff, teacherId, "2026-05-07",
            (c[0], true, null), (c[1], true, "yaxshi"), (c[2], false, null), (c[3], null, "kuzatilmadi"), (c[4], null, null));
        Assert.Equal(2, obs.GetProperty("scoreDone").GetInt32());
        Assert.Equal(3, obs.GetProperty("scoreTotal").GetInt32());
        Assert.Equal(66.67, obs.GetProperty("scorePercent").GetDouble());
        Assert.Equal("draft", obs.GetProperty("status").GetString());
        var details = obs.GetProperty("details").EnumerateArray().ToList();
        Assert.Equal(4, details.Count); // the row with no mark and no comment is not stored
        var unmarked = details.Single(d => d.GetProperty("criterionId").GetString() == c[3]);
        Assert.Equal(JsonValueKind.Null, unmarked.GetProperty("isDone").ValueKind);
        Assert.Equal("kuzatilmadi", unmarked.GetProperty("comment").GetString());

        // recomputed on every save; a cleared mark (no mark, no comment) is dropped
        var id = obs.GetProperty("id").GetString()!;
        var patched = await ReadAsync(await staff.PatchAsJsonAsync($"{Admin}/observations/{id}", new
        {
            details = new object[]
            {
                new { criterionId = c[0], isDone = true, comment = (string?)null },
                new { criterionId = c[1], isDone = (bool?)null, comment = (string?)null },
                new { criterionId = c[2], isDone = false, comment = (string?)null },
            },
        }));
        Assert.Equal(1, patched.GetProperty("scoreDone").GetInt32());
        Assert.Equal(2, patched.GetProperty("scoreTotal").GetInt32());
        Assert.Equal(50, patched.GetProperty("scorePercent").GetDouble());
        Assert.DoesNotContain(c[1], patched.GetProperty("details").EnumerateArray().Select(d => d.GetProperty("criterionId").GetString()));
        // c[3] (not sent this time) stays as it was
        Assert.Contains(c[3], patched.GetProperty("details").EnumerateArray().Select(d => d.GetProperty("criterionId").GetString()));

        // nothing answered → 0, and complete refuses
        var empty = await CreateAsync(staff, teacherId, "2026-05-08", (c[0], null, "faqat izoh"));
        Assert.Equal(0, empty.GetProperty("scoreTotal").GetInt32());
        Assert.Equal(0, empty.GetProperty("scorePercent").GetDouble());
        var complete = await staff.PostAsync($"{Admin}/observations/{empty.GetProperty("id").GetString()}/complete", null);
        Assert.Equal(HttpStatusCode.BadRequest, complete.StatusCode);
        Assert.Contains("Kamida bitta mezon", await complete.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ochirilgan_mezon_javobi_saqlanadi_va_hisoblanadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var c = await ActiveCriteriaAsync(admin);
        var sectionId = (await SectionsAsync(admin, includeInactive: false))[0].GetProperty("id").GetString()!;
        var extra = await ReadAsync(await admin.PostAsJsonAsync($"{Admin}/criteria",
            new { sectionId, text = "Vaqtinchalik mezon " + Guid.NewGuid().ToString("N")[..6], orderIndex = 99 }));
        var extraId = extra.GetProperty("id").GetString()!;
        Assert.Equal(99, extra.GetProperty("orderIndex").GetInt32());

        var teacherId = await NewTeacherAsync("Nofaol");
        var obs = await CreateAsync(admin, teacherId, "2026-05-09", (c[0], true, null), (extraId, false, null));
        Assert.Equal(2, obs.GetProperty("scoreTotal").GetInt32());

        await admin.PatchAsJsonAsync($"{Admin}/criteria/{extraId}", new { isActive = false });

        // the form no longer shows the deactivated criterion, so it is not in the PATCH
        var id = obs.GetProperty("id").GetString()!;
        var patched = await ReadAsync(await admin.PatchAsJsonAsync($"{Admin}/observations/{id}", new
        {
            details = new object[] { new { criterionId = c[0], isDone = true, comment = (string?)null } },
        }));
        Assert.Contains(extraId, patched.GetProperty("details").EnumerateArray().Select(d => d.GetProperty("criterionId").GetString()));
        Assert.Equal(2, patched.GetProperty("scoreTotal").GetInt32());
        Assert.Equal(50, patched.GetProperty("scorePercent").GetDouble());
    }

    [Fact]
    public async Task Patch_faqat_berilgan_maydonlarni_ozgartiradi()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        var c = await ActiveCriteriaAsync(staff);
        var teacherId = await NewTeacherAsync("Patch");
        var obs = await CreateAsync(staff, teacherId, "2026-05-11", (c[0], true, null));
        var id = obs.GetProperty("id").GetString()!;

        // explicit null teacherId / lessonDate = leave as is; subject changes; details untouched
        var patched = await ReadAsync(await staff.PatchAsJsonAsync($"{Admin}/observations/{id}",
            new { teacherId = (string?)null, lessonDate = (string?)null, subject = "Fizika", strengths = "Kuchli" }));
        Assert.Equal(teacherId, patched.GetProperty("teacherId").GetString());
        Assert.Equal("2026-05-11", patched.GetProperty("lessonDate").GetString());
        Assert.Equal("Fizika", patched.GetProperty("subject").GetString());
        Assert.Equal("5-A", patched.GetProperty("className").GetString());
        Assert.Equal("Kuchli", patched.GetProperty("strengths").GetString());
        Assert.Single(patched.GetProperty("details").EnumerateArray());

        // validation
        Assert.Equal(HttpStatusCode.BadRequest,
            (await staff.PatchAsJsonAsync($"{Admin}/observations/{id}", new { lessonDate = "05.11.2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await staff.PatchAsJsonAsync($"{Admin}/observations/{id}", new { className = new string('x', 51) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await staff.PatchAsJsonAsync($"{Admin}/observations/{id}", new { teacherId = Guid.NewGuid().ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await staff.PostAsJsonAsync($"{Admin}/observations", Input(Guid.NewGuid().ToString(), "2026-05-11"))).StatusCode);
    }

    [Theory]
    [InlineData(2.675, 2, 2.67)]
    [InlineData(3.125, 2, 3.12)]
    [InlineData(3.375, 2, 3.38)]
    [InlineData(66.66666666666667, 2, 66.67)]
    [InlineData(12.5, 0, 12)]
    [InlineData(13.5, 0, 14)]
    [InlineData(0.05, 1, 0.1)]
    [InlineData(0.25, 1, 0.2)]
    public void PyRound_python_round_bilan_bir_xil(double value, int digits, double expected) =>
        Assert.Equal(expected, LessonAnalysisScoring.PyRound(value, digits));

    [Fact]
    public void Recompute_eski_formula()
    {
        var o = new LessonObservation();
        o.Details.AddRange(Enumerable.Range(0, 32).Select(i => new LessonObservationDetail { CriterionId = $"c{i}", IsDone = i == 0 }));
        o.Details.Add(new LessonObservationDetail { CriterionId = "u", IsDone = null, Comment = "x" });
        LessonAnalysisScoring.Recompute(o);
        Assert.Equal(1, o.ScoreDone);
        Assert.Equal(32, o.ScoreTotal);
        Assert.Equal(3.12m, o.ScorePercent); // Python: round(1 / 32 * 100, 2) == 3.12

        o.Details.Clear();
        LessonAnalysisScoring.Recompute(o);
        Assert.Equal(0m, o.ScorePercent);
        Assert.Equal("62", LessonAnalysisScoring.FormatWhole(62.5)); // f"{62.5:.0f}" == "62"
    }

    // =====================================================================
    //  3. Rating board, dashboard, analytics
    // =====================================================================

    [Fact]
    public async Task Reyting_ortacha_va_sana_filtri()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        var c = await ActiveCriteriaAsync(staff);
        var teacherId = await NewTeacherAsync("Reyting");

        // 2031-03-02: 1/2 = 50%; 2031-04-06: 2/3 = 66.67% → (50.0 + 66.67) / 2 is the float
        // 58.33500000000000085… → Python round(…, 2) == 58.34 (the old board's number)
        await CompleteNewAsync(staff, teacherId, "2031-03-02", (c[0], true, null), (c[1], false, null));
        await CompleteNewAsync(staff, teacherId, "2031-04-06", (c[0], true, null), (c[1], true, null), (c[2], false, null));
        // a draft never counts
        await CreateAsync(staff, teacherId, "2031-04-07", (c[0], false, null));

        var all = await RatingRowAsync(staff, teacherId, "");
        Assert.NotNull(all);
        Assert.Equal(2, all.Value.GetProperty("observationsCount").GetInt32());
        Assert.Equal(58.34, all.Value.GetProperty("rating").GetDouble());
        Assert.Equal("2031-04-06", all.Value.GetProperty("lastObservationDate").GetString());

        var april = await RatingRowAsync(staff, teacherId, "?dateFrom=2031-04-01");
        Assert.Equal(66.67, april!.Value.GetProperty("rating").GetDouble());
        Assert.Equal(1, april.Value.GetProperty("observationsCount").GetInt32());

        var march = await RatingRowAsync(staff, teacherId, "?dateTo=2031-03-31");
        Assert.Equal(50, march!.Value.GetProperty("rating").GetDouble());

        // not observed in the range → listed as not evaluated, not on the board
        var board = await ReadAsync(await staff.GetAsync($"{Admin}/rating?dateFrom=2031-05-01&dateTo=2031-05-31"));
        Assert.DoesNotContain(teacherId, board.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("teacherId").GetString()));
        Assert.Contains(teacherId, board.GetProperty("notEvaluated").EnumerateArray().Select(t => t.GetProperty("id").GetString()));

        // teacher card numbers
        var card = await ReadAsync(await staff.GetAsync($"{Admin}/teachers/{teacherId}"));
        Assert.Equal(2, card.GetProperty("observationsCount").GetInt32());
        Assert.Equal(1, card.GetProperty("drafts").GetInt32());
        Assert.Equal(all.Value.GetProperty("rating").GetDouble(), card.GetProperty("rating").GetDouble());

        // Excel export is a real workbook
        var xlsx = await staff.GetAsync($"{Admin}/rating.xlsx?dateFrom=2031-01-01");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType?.MediaType);
        var bytes = await xlsx.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public async Task Analitika_oqituvchi_boyicha_eski_formulalar()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        var c = await ActiveCriteriaAsync(staff);
        var teacherId = await NewTeacherAsync("Analitika");
        await CompleteNewAsync(staff, teacherId, "2031-06-01", (c[0], true, null), (c[1], true, null), (c[2], false, null), (c[3], null, "izoh"));

        var a = await ReadAsync(await staff.GetAsync($"{Admin}/analytics?teacherId={teacherId}"));
        var monthly = Assert.Single(a.GetProperty("monthly").EnumerateArray());
        Assert.Equal("2031-06", monthly.GetProperty("month").GetString());
        Assert.Equal(1, monthly.GetProperty("count").GetInt32());
        Assert.Equal(66.7, monthly.GetProperty("average").GetDouble());

        var sections = a.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal(8, sections.Count);
        Assert.Equal(3, sections.Sum(s => s.GetProperty("total").GetInt32())); // the unmarked row does not count
        Assert.Equal(2, sections.Sum(s => s.GetProperty("done").GetInt32()));

        var buckets = a.GetProperty("distribution").EnumerateArray()
            .ToDictionary(b => b.GetProperty("bucket").GetString()!, b => b.GetProperty("count").GetInt32());
        Assert.Equal(["0–59%", "60–74%", "75–89%", "90–100%"], buckets.Keys);
        Assert.Equal(1, buckets["60–74%"]);
        Assert.Equal(1, buckets.Values.Sum());

        var top = Assert.Single(a.GetProperty("topTeachers").EnumerateArray());
        Assert.Equal(teacherId, top.GetProperty("teacherId").GetString());
        Assert.Equal(66.7, top.GetProperty("rating").GetDouble());

        var worst = a.GetProperty("criteriaMisses").EnumerateArray().First();
        Assert.Equal(c[2], worst.GetProperty("criterionId").GetString());
        Assert.Equal(100, worst.GetProperty("missPercent").GetDouble());
        Assert.Equal(1, worst.GetProperty("missed").GetInt32());

        // dashboard: a fresh active teacher with no observation at all is "not evaluated"
        var fresh = await NewTeacherAsync("Yangi");
        var freshName = await TeacherNameAsync(fresh);
        var d = await ReadAsync(await staff.GetAsync($"{Admin}/dashboard"));
        Assert.Contains(freshName, d.GetProperty("notEvaluated").EnumerateArray().Select(x => x.GetString()));
        Assert.True(d.GetProperty("observationsTotal").GetInt32() >= 1);
        Assert.True(d.GetProperty("teachersActive").GetInt32() >= 2);
        Assert.True(d.GetProperty("top").GetArrayLength() <= 5);
    }

    // =====================================================================
    //  4. Completed observation: who may correct it
    // =====================================================================

    [Fact]
    public async Task Yakunlangan_tahlilni_muallif_yoki_admin_tuzatadi()
    {
        var (authorUser, _) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: [LessonAnalysisAccess.PermissionKey]);
        using var author = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Staff, authorUser.Id));
        using var colleague = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var c = await ActiveCriteriaAsync(admin);
        var teacherId = await NewTeacherAsync("Muallif");

        var obs = await CreateAsync(author, teacherId, "2026-05-12", (c[0], true, null));
        var id = obs.GetProperty("id").GetString()!;
        Assert.Equal(authorUser.Id, obs.GetProperty("observerId").GetString());

        // a draft is open to anyone with the key
        Assert.Equal(HttpStatusCode.OK,
            (await colleague.PatchAsJsonAsync($"{Admin}/observations/{id}", new { subject = "Tarix" })).StatusCode);
        Assert.True((await ReadAsync(await colleague.GetAsync($"{Admin}/observations/{id}"))).GetProperty("canEdit").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"{Admin}/observations/{id}/complete", null)).StatusCode);

        Assert.False((await ReadAsync(await colleague.GetAsync($"{Admin}/observations/{id}"))).GetProperty("canEdit").GetBoolean());
        Assert.True((await ReadAsync(await author.GetAsync($"{Admin}/observations/{id}"))).GetProperty("canEdit").GetBoolean());
        Assert.True((await ReadAsync(await admin.GetAsync($"{Admin}/observations/{id}"))).GetProperty("canEdit").GetBoolean());

        var denied = await colleague.PatchAsJsonAsync($"{Admin}/observations/{id}", new { subject = "Kimyo" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("muallifi yoki administrator", await denied.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await colleague.PostAsync($"{Admin}/observations/{id}/ai-summary", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await author.PatchAsJsonAsync($"{Admin}/observations/{id}", new { subject = "Kimyo" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PatchAsJsonAsync($"{Admin}/observations/{id}", new { subject = "Biologiya" })).StatusCode);
        Assert.Equal("Biologiya", (await ReadAsync(await admin.GetAsync($"{Admin}/observations/{id}"))).GetProperty("subject").GetString());
    }

    // =====================================================================
    //  5. AI: idempotent complete, best-effort errors
    // =====================================================================

    [Fact]
    public async Task Complete_idempotent_AI_bir_marta_chaqiriladi()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: [LessonAnalysisAccess.PermissionKey]);
        using var client = HostClient(fixture.Api.TokenFor(Roles.Staff, user.Id));
        var c = await ActiveCriteriaAsync(client);
        var teacherId = await NewTeacherAsync("AI");
        var obs = await CreateAsync(client, teacherId, "2026-05-13", (c[0], true, "zo'r"), (c[1], false, null));
        var id = obs.GetProperty("id").GetString()!;

        var first = await ReadAsync(await client.PostAsync($"{Admin}/observations/{id}/complete", null));
        var second = await ReadAsync(await client.PostAsync($"{Admin}/observations/{id}/complete", null));

        Assert.Equal(1, Fake.CallsFor(id));
        Assert.Equal("completed", first.GetProperty("status").GetString());
        Assert.Equal(FakeAi.Summary, first.GetProperty("aiSummary").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("aiError").ValueKind);
        Assert.Equal(first.GetProperty("aiSummary").GetString(), second.GetProperty("aiSummary").GetString());

        // the prompt is the old one: verbatim header lines + section title + marks + comment
        var prompt = Fake.PromptFor(id);
        Assert.Contains("Natija: 1/2 (50%)", prompt);
        Assert.Contains("[BAJARILDI]", prompt);
        Assert.Contains("[BAJARILMADI]", prompt);
        Assert.Contains("Kuzatuvchi izohi: zo'r", prompt);
        Assert.Contains("MEZONLAR:", prompt);

        // regenerate calls again; a provider failure → 502, recorded, the old summary kept
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Admin}/observations/{id}/ai-summary", null)).StatusCode);
        Assert.Equal(2, Fake.CallsFor(id));
        Fake.FailFor(id);
        var failed = await client.PostAsync($"{Admin}/observations/{id}/ai-summary", null);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        var after = await ReadAsync(await client.GetAsync($"{Admin}/observations/{id}"));
        Assert.Equal(FakeAi.Error, after.GetProperty("aiError").GetString());
        Assert.Equal(FakeAi.Summary, after.GetProperty("aiSummary").GetString());
    }

    [Fact]
    public async Task AI_xatosi_yakunlashni_toxtatmaydi()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var client = HostClient(fixture.Api.TokenFor(Roles.Admin, user.Id));
        var c = await ActiveCriteriaAsync(client);
        var teacherId = await NewTeacherAsync("AIXato");
        var id = (await CreateAsync(client, teacherId, "2026-05-14", (c[0], true, null))).GetProperty("id").GetString()!;
        Fake.FailFor(id);

        var done = await ReadAsync(await client.PostAsync($"{Admin}/observations/{id}/complete", null));
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Equal(FakeAi.Error, done.GetProperty("aiError").GetString());
        Assert.Equal(JsonValueKind.Null, done.GetProperty("aiSummary").ValueKind);
    }

    /// <summary>The shared host has no key: a clear error, no HTTP call, the observation completes.</summary>
    [Fact]
    public async Task Kalit_yoq_bolsa_aniq_xato_va_tarmoqqa_chiqmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var c = await ActiveCriteriaAsync(admin);
        var teacherId = await NewTeacherAsync("Kalitsiz");
        var id = (await CreateAsync(admin, teacherId, "2026-05-15", (c[0], true, null))).GetProperty("id").GetString()!;

        var done = await ReadAsync(await admin.PostAsync($"{Admin}/observations/{id}/complete", null));
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.StartsWith("Gemini API kaliti sozlanmagan", done.GetProperty("aiError").GetString());

        // and directly: a handler that fails the test if it is ever called
        var ai = new GeminiLessonAnalysisAi(new HttpClient(new NoNetwork()),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var result = await ai.SummarizeAsync(new LessonAiObservation(
            "x", "T", null, null, new DateOnly(2026, 5, 15), 1, 1, 100m, [], null, null, null));
        Assert.Null(result.Summary);
        Assert.Equal(GeminiLessonAnalysisAi.MissingKeyError, result.Error);
    }

    // =====================================================================
    //  6. Teacher portal: own COMPLETED only
    // =====================================================================

    [Fact]
    public async Task Oqituvchi_faqat_oz_yakunlangan_tahlillarini_koradi()
    {
        var (meUser, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        using var me = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, meUser.Id));
        var myTeacherId = await TeacherIdOfUserAsync(meUser.Id);
        var (otherUser, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var otherTeacherId = await TeacherIdOfUserAsync(otherUser.Id);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var c = await ActiveCriteriaAsync(admin);
        var mineDone = await CompleteNewAsync(admin, myTeacherId, "2031-07-01", (c[0], true, null), (c[1], false, null));
        var mineDraft = (await CreateAsync(admin, myTeacherId, "2031-07-02", (c[0], true, null))).GetProperty("id").GetString()!;
        var others = await CompleteNewAsync(admin, otherTeacherId, "2031-07-03", (c[0], true, null));

        var list = (await ReadAsync(await me.GetAsync($"{Mine}/observations"))).EnumerateArray().ToList();
        Assert.Equal([mineDone], list.Select(o => o.GetProperty("id").GetString()));
        Assert.Empty((await ReadAsync(await me.GetAsync($"{Mine}/observations?dateFrom=2031-08-01"))).EnumerateArray());

        var detail = await me.GetAsync($"{Mine}/observations/{mineDone}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await ReadAsync(detail);
        Assert.False(body.GetProperty("canEdit").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("aiError").ValueKind); // provider errors are staff-only
        Assert.Equal(8, body.GetProperty("sections").GetArrayLength()); // criterion texts for the teacher page

        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"{Mine}/observations/{mineDraft}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"{Mine}/observations/{others}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"{Mine}/observations/{Guid.NewGuid()}")).StatusCode);

        var pdf = await me.GetAsync($"{Mine}/observations/{mineDone}/report.pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"{Mine}/observations/{mineDraft}/report.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"{Mine}/observations/{others}/report.pdf")).StatusCode);

        // analytics narrowed to self — never a ranking of colleagues
        var a = await ReadAsync(await me.GetAsync($"{Mine}/analytics"));
        var top = Assert.Single(a.GetProperty("topTeachers").EnumerateArray());
        Assert.Equal(myTeacherId, top.GetProperty("teacherId").GetString());
        Assert.Equal(1, a.GetProperty("distribution").EnumerateArray().Sum(b => b.GetProperty("count").GetInt32()));
        Assert.Empty((await ReadAsync(await me.GetAsync($"{Mine}/analytics?dateTo=2031-06-30"))).GetProperty("monthly").EnumerateArray());

        // the staff endpoints never add the catalogue
        Assert.False((await ReadAsync(await admin.GetAsync($"{Admin}/observations/{mineDone}"))).TryGetProperty("sections", out _));
    }

    // =====================================================================
    //  7. Cache-Control: no-store on every response
    // =====================================================================

    [Fact]
    public async Task Har_javobda_no_store()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        using var bare = await fixture.Api.ClientAsAsync(Roles.Staff);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);

        foreach (var response in new[]
                 {
                     await staff.GetAsync($"{Admin}/rating"),                         // 200
                     await staff.GetAsync($"{Admin}/observations/{Guid.NewGuid()}"),  // 404
                     await staff.GetAsync($"{Admin}/rating.xlsx"),                    // file
                     await staff.GetAsync($"{Admin}/observations?limit=0"),           // 400
                     await bare.GetAsync($"{Admin}/rating"),                          // 403 (AdminPerm)
                     await teacher.GetAsync($"{Mine}/observations"),                  // 200
                     await teacher.GetAsync($"{Mine}/observations/{Guid.NewGuid()}"), // 404
                     await teacher.GetAsync($"{Mine}/analytics"),                     // 200
                 })
        {
            Assert.True(response.Headers.CacheControl?.NoStore == true,
                $"{response.RequestMessage?.RequestUri} ({(int)response.StatusCode}) has no Cache-Control: no-store");
        }
    }

    // =====================================================================
    //  8. Seeder
    // =====================================================================

    [Fact]
    public async Task Seeder_34_mezon_8_bolim_faqat_bosh_jadvalga()
    {
        // the shared database was seeded at start-up; a second run does nothing
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.Equal(8, await db.LessonObservationSections.CountAsync());
            Assert.False(await LessonAnalysisSeeder.SeedAsync(db));
            Assert.Equal(8, await db.LessonObservationSections.CountAsync());
        });

        // a pristine database: seeded once, verbatim, then left alone
        var fresh = await fixture.Postgres.CreateDatabaseAsync("laseed");
        await using var db = PostgresFixture.NewContext(fresh.OwnerConnectionString);
        Assert.True(await LessonAnalysisSeeder.SeedAsync(db));
        Assert.False(await LessonAnalysisSeeder.SeedAsync(db));

        var sections = await db.LessonObservationSections.Include(s => s.Criteria).OrderBy(s => s.OrderIndex).ToListAsync();
        Assert.Equal(8, sections.Count);
        Assert.Equal(34, sections.Sum(s => s.Criteria.Count));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], sections.Select(s => s.OrderIndex));
        Assert.Equal("1-BO'LIM: DARSGA TAYYORGARLIK", sections[0].Title);
        Assert.Equal("#7a9a2e", sections[0].Color);
        Assert.Equal("#b03a5b", sections[7].Color);
        Assert.Equal([2, 4, 8, 3, 4, 4, 5, 4], sections.Select(s => s.Criteria.Count));
        Assert.Equal("Dars rejasi (lesson plan) oldindan tuzilgan va taqdim etilgan",
            sections[0].Criteria.OrderBy(c => c.OrderIndex).First().Text);
        Assert.All(sections.SelectMany(s => s.Criteria), c => Assert.True(c.IsActive));
    }

    // =====================================================================
    //  9. Lessons of a teacher on a date (timetable)
    // =====================================================================

    [Fact]
    public async Task Oqituvchining_sanadagi_darslari()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        var teacherId = await NewTeacherAsync("Jadval");
        var otherTeacherId = await NewTeacherAsync("Boshqa");
        var suffix = Guid.NewGuid().ToString("N")[..5];
        var className = $"LA-{suffix}";
        var subjectName = $"Algebra {suffix}";

        // a quarter far from every other test: 2031-09-01 (Mon) .. 2031-10-31
        await fixture.Api.WithDbAsync(async db =>
        {
            var cls = new SchoolClass { Name = className, Grade = 7, Language = "uz" };
            var subject = new Subject { Name = subjectName };
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = "2031-09-01", EndDate = "2031-10-31" });
            var tpl = new ScheduleTemplate
            {
                ClassId = cls.Id,
                Name = "LA",
                Lessons =
                [
                    new ScheduleLesson { Day = 2, Period = 3, SubjectId = subject.Id, TeacherId = teacherId },
                    new ScheduleLesson { Day = 2, Period = 1, SubjectId = subject.Id, TeacherId = teacherId },
                    new ScheduleLesson { Day = 2, Period = 2, SubjectId = subject.Id, TeacherId = otherTeacherId },
                    new ScheduleLesson { Day = 3, Period = 1, SubjectId = subject.Id, TeacherId = teacherId },
                ],
            };
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
            var week = PortalSchedule.WeekOf(ScheduleMath.GetQuarterWeeks("2031-09-01", "2031-10-31"), "2031-09-17");
            db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = 1, Week = week, TemplateId = tpl.Id });
            await db.SaveChangesAsync();
        });

        // 2031-09-17 is a Wednesday (Day = 2)
        var lessons = (await ReadAsync(await staff.GetAsync($"{Admin}/teachers/{teacherId}/lessons?date=2031-09-17")))
            .EnumerateArray().ToList();
        Assert.Equal([1, 3], lessons.Select(l => l.GetProperty("period").GetInt32()));
        Assert.All(lessons, l =>
        {
            Assert.Equal(className, l.GetProperty("className").GetString());
            Assert.Equal(subjectName, l.GetProperty("subjectName").GetString());
            Assert.True(l.TryGetProperty("startTime", out _));
            Assert.True(l.TryGetProperty("endTime", out _));
        });

        // another week of the same quarter has no assignment; Sunday and out-of-quarter are empty
        Assert.Empty((await ReadAsync(await staff.GetAsync($"{Admin}/teachers/{teacherId}/lessons?date=2031-09-24"))).EnumerateArray());
        Assert.Empty((await ReadAsync(await staff.GetAsync($"{Admin}/teachers/{teacherId}/lessons?date=2031-09-21"))).EnumerateArray());
        Assert.Empty((await ReadAsync(await staff.GetAsync($"{Admin}/teachers/{teacherId}/lessons?date=2032-01-14"))).EnumerateArray());

        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync($"{Admin}/teachers/{teacherId}/lessons")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await staff.GetAsync($"{Admin}/teachers/{Guid.NewGuid()}/lessons?date=2031-09-17")).StatusCode);
    }

    // =====================================================================
    //  10. JSON contract (schoollms.client/src/types/lessonAnalysis.ts)
    // =====================================================================

    [Fact]
    public async Task Javob_shakli_TS_shartnomasiga_mos()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, LessonAnalysisAccess.PermissionKey);
        var c = await ActiveCriteriaAsync(staff);
        var teacherId = await NewTeacherAsync("Shartnoma");
        var obs = await CreateAsync(staff, teacherId, "2026-05-16", (c[0], true, "ok"));

        AssertKeys(obs,
            "id", "teacherId", "teacherName", "observerId", "observerName", "lessonDate", "lessonTime", "className",
            "subject", "status", "strengths", "growthAreas", "nextLessonAdvice", "aiSummary", "aiError", "scoreDone",
            "scoreTotal", "scorePercent", "createdAt", "canEdit", "details");
        AssertKeys(obs.GetProperty("details")[0], "criterionId", "isDone", "comment");

        var item = (await ReadAsync(await staff.GetAsync($"{Admin}/observations?teacherId={teacherId}")))[0];
        AssertKeys(item,
            "id", "teacherId", "teacherName", "observerName", "lessonDate", "className", "subject", "status",
            "scoreDone", "scoreTotal", "scorePercent", "hasAiSummary");

        var summary = await ReadAsync(await staff.GetAsync($"{Admin}/teachers/{teacherId}"));
        AssertKeys(summary, "id", "fullName", "phone", "subject", "photoUrl", "observationsCount", "drafts", "rating", "lastObservationDate");
        AssertKeys(await ReadAsync(await staff.GetAsync($"{Admin}/teachers/random")), "id", "fullName", "phone", "subject", "photoUrl");

        var section = (await SectionsAsync(staff, includeInactive: false))[0];
        AssertKeys(section, "id", "title", "orderIndex", "color", "criteria");
        AssertKeys(section.GetProperty("criteria")[0], "id", "text", "orderIndex", "isActive");

        AssertKeys(await ReadAsync(await staff.GetAsync($"{Admin}/rating")), "rows", "notEvaluated");
        AssertKeys(await ReadAsync(await staff.GetAsync($"{Admin}/dashboard")),
            "teachersActive", "observationsTotal", "observationsLast30Days", "averageRating", "drafts", "notEvaluated", "top", "bottom");
        var analytics = await ReadAsync(await staff.GetAsync($"{Admin}/analytics"));
        AssertKeys(analytics, "monthly", "sections", "distribution", "topTeachers", "criteriaMisses");
        AssertKeys(analytics.GetProperty("sections")[0], "sectionId", "title", "color", "done", "total", "percent");
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static void AssertKeys(JsonElement e, params string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), e.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PATCH") request.Content = JsonContent.Create(new { });
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {(int)response.StatusCode} {raw}");
        return JsonSerializer.Deserialize<JsonElement>(raw, Json);
    }

    private static async Task<List<JsonElement>> SectionsAsync(HttpClient client, bool includeInactive) =>
        [.. (await ReadAsync(await client.GetAsync($"{Admin}/criteria?includeInactive={includeInactive}"))).EnumerateArray()];

    private static IEnumerable<string?> AllCriterionIds(IEnumerable<JsonElement> sections) =>
        sections.SelectMany(s => s.GetProperty("criteria").EnumerateArray()).Select(c => c.GetProperty("id").GetString());

    private static readonly HashSet<string> SeededTexts =
        [.. LessonAnalysisSeeder.Sections.SelectMany(s => s.Criteria)];

    /// <summary>Active SEEDED criteria in catalogue order (tests add their own criteria to section 1).</summary>
    private static async Task<List<string>> ActiveCriteriaAsync(HttpClient client) =>
        [.. (await SectionsAsync(client, includeInactive: false))
            .SelectMany(s => s.GetProperty("criteria").EnumerateArray())
            .Where(c => SeededTexts.Contains(c.GetProperty("text").GetString()!))
            .Select(c => c.GetProperty("id").GetString()!)];

    private static object Input(string teacherId, string date, params (string Id, bool? Done, string? Comment)[] details) => new
    {
        teacherId,
        lessonDate = date,
        lessonTime = "08:30",
        className = "5-A",
        subject = "Matematika",
        strengths = (string?)null,
        growthAreas = (string?)null,
        nextLessonAdvice = (string?)null,
        details = details.Select(d => new { criterionId = d.Id, isDone = d.Done, comment = d.Comment }).ToArray(),
    };

    private static async Task<JsonElement> CreateAsync(
        HttpClient client, string teacherId, string date, params (string Id, bool? Done, string? Comment)[] details)
    {
        var response = await client.PostAsJsonAsync($"{Admin}/observations", Input(teacherId, date, details));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<string> CompleteNewAsync(
        HttpClient client, string teacherId, string date, params (string Id, bool? Done, string? Comment)[] details)
    {
        var id = (await CreateAsync(client, teacherId, date, details)).GetProperty("id").GetString()!;
        var done = await ReadAsync(await client.PostAsync($"{Admin}/observations/{id}/complete", null));
        Assert.Equal("completed", done.GetProperty("status").GetString());
        return id;
    }

    private static async Task<JsonElement?> RatingRowAsync(HttpClient client, string teacherId, string query)
    {
        var board = await ReadAsync(await client.GetAsync($"{Admin}/rating{query}"));
        foreach (var row in board.GetProperty("rows").EnumerateArray())
            if (row.GetProperty("teacherId").GetString() == teacherId) return row;
        return null;
    }

    private async Task<string> NewTeacherAsync(string label)
    {
        var teacher = new Teacher { FullName = $"LA {label} {Guid.NewGuid().ToString("N")[..8]}" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
        });
        return teacher.Id;
    }

    private async Task<string> TeacherNameAsync(string id)
    {
        var name = "";
        await fixture.Api.WithDbAsync(async db => name = await db.Teachers.Where(t => t.Id == id).Select(t => t.FullName).SingleAsync());
        return name;
    }

    private async Task<string> TeacherIdOfUserAsync(string userId)
    {
        var id = "";
        await fixture.Api.WithDbAsync(async db => id = await db.Teachers.Where(t => t.UserId == userId).Select(t => t.Id).SingleAsync());
        return id;
    }

    // ---------------------------------------------------------------------
    //  Derived host with a fake AI (the shared fixture.Api is left untouched)
    // ---------------------------------------------------------------------

    private static readonly FakeAi Fake = new();
    private static readonly Lock HostLock = new();
    private static WebApplicationFactory<AuthController>? _host;

    private WebApplicationFactory<AuthController> Host
    {
        get
        {
            lock (HostLock)
            {
                return _host ??= fixture.Api.WithWebHostBuilder(b => b.ConfigureServices(s =>
                {
                    s.RemoveAll<ILessonAnalysisAi>();
                    s.AddSingleton<ILessonAnalysisAi>(Fake);
                }));
            }
        }
    }

    private HttpClient HostClient(string token)
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class FakeAi : ILessonAnalysisAi
    {
        public const string Summary = """{"umumiy_xulosa":"Dars yaxshi o'tdi","kuchli_tomonlar":["a"],"rivojlantirish":[],"tavsiyalar":["b"]}""";
        public const string Error = "Gemini API xatosi (503): test";

        private readonly ConcurrentDictionary<string, int> _calls = new();
        private readonly ConcurrentDictionary<string, string> _prompts = new();
        private readonly ConcurrentDictionary<string, bool> _fail = new();

        public int CallsFor(string id) => _calls.GetValueOrDefault(id);
        public string PromptFor(string id) => _prompts.GetValueOrDefault(id) ?? "";
        public void FailFor(string id) => _fail[id] = true;

        public Task<LessonAiResult> SummarizeAsync(LessonAiObservation observation, CancellationToken ct = default)
        {
            _calls.AddOrUpdate(observation.ObservationId, 1, (_, n) => n + 1);
            _prompts[observation.ObservationId] = LessonAiPrompt.BuildObservationText(observation);
            return Task.FromResult(_fail.ContainsKey(observation.ObservationId)
                ? new LessonAiResult(null, Error)
                : new LessonAiResult(Summary, null));
        }
    }

    /// <summary>Fails the test if the Gemini client ever sends a request.</summary>
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException($"Unexpected network call: {request.RequestUri}");
    }
}
